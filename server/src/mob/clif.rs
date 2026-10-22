//! The monster packets of clif.cpp: clif_monster_hp_bar, clif_mvp_effect / clif_mvp_exp / clif_mvp_item,
//! clif_class_change, clif_clearunit_delayed, and the BL_MOB parts of clif_spawn / clif_getareachar_unit (tiny / big
//! monster effects, the HP bar for players in the damage log, the name).

use super::{get, MobData};
use crate::clif::{self as core_clif, Target};
use crate::net::packets::*;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// clif.cpp clif_monster_hp_bar (PACKETVER >= 20120404): 0x0977 <id>.L <hp>.L <max_hp>.L to one player.
pub fn monster_hp_bar(c: &Ctx, md: &MobData, sd: &Unit) {
    let mut w = Vec::with_capacity(14);
    w.extend_from_slice(&0x0977u16.to_le_bytes());
    w.extend_from_slice(&md.id.to_le_bytes());
    w.extend_from_slice(&md.status.hp.to_le_bytes());
    w.extend_from_slice(&md.status.max_hp.to_le_bytes());
    core_clif::send(c, &w, sd, Target::SelfOnly);
}

/// clif.cpp clif_mvp_effect: ZC_MVP around the MVP.
pub fn mvp_effect(c: &Ctx, sd: &Unit) {
    let p = PACKET_ZC_MVP { packetType: PACKET_ZC_MVP::ID as i16, AID: sd.id };
    core_clif::send(c, &p.to_vec(), sd, Target::Area);
}

/// clif.cpp clif_mvp_item: ZC_MVP_GETTING_ITEM.
pub fn mvp_item(c: &Ctx, sd: &Unit, nameid: u32) {
    let p = PACKET_ZC_MVP_GETTING_ITEM { packetType: PACKET_ZC_MVP_GETTING_ITEM::ID as i16, itemId: crate::db::items::client_nameid(nameid) };
    core_clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// clif.cpp clif_mvp_exp (PACKETVER >= 20131223): only a colour message when client.conf mvp_exp_reward_message is on
/// (it is `no`), so nothing is sent.
pub fn mvp_exp(_c: &Ctx, _sd: &Unit, _exp: u64) {
    // client.conf mvp_exp_reward_message: no
}

/// clif.cpp clif_skill_poseffect: ZC_NOTIFY_GROUNDSKILL around `bl` (the summon script command's effect; disguises
/// arrive with their system).
pub fn skill_poseffect(c: &Ctx, bl: &Unit, skill_id: u16, skill_lv: u16, x: i16, y: i16, tick: Tick) {
    let mut p = PACKET_ZC_NOTIFY_GROUNDSKILL {
        PacketType: PACKET_ZC_NOTIFY_GROUNDSKILL::ID as i16,
        SKID: skill_id,
        AID: bl.id,
        level: skill_lv as i16,
        xPos: x,
        yPos: y,
        startTime: tick as u32, // client_tick
    };
    if crate::atcmd_disguise::disguised(c, bl) {
        core_clif::send(c, &p.to_vec(), bl, Target::AreaWos);
        p.AID = crate::atcmd_disguise::disguised_bl_id(bl.id);
        core_clif::send(c, &p.to_vec(), bl, Target::SelfOnly);
    } else {
        core_clif::send(c, &p.to_vec(), bl, Target::Area);
    }
}

/// clif.cpp clif_class_change: ZC_NPCSPRITE_CHANGE (not for player classes: "player classes yield missing
/// sprites"), around `bl` or to one player.
pub fn class_change(c: &Ctx, bl: &Unit, class_: i32, target: Target, sd: Option<&Unit>) {
    if core_clif::pcdb_checkid(class_) {
        return;
    }
    let p = PACKET_ZC_NPCSPRITE_CHANGE { packetType: PACKET_ZC_NPCSPRITE_CHANGE::ID as i16, GID: bl.id, r#type: 0, class_: class_ as u32 };
    core_clif::send(c, &p.to_vec(), sd.unwrap_or(bl), target);
}

/// clif.cpp clif_clearunit_delayed: ZC_NOTIFY_VANISH for the unit's id at its current position, at `tick` (a copy
/// of the block_list is kept, so the packet goes out even if the monster is gone by then). The copy lives in the
/// timer: target = the id, data = type << 48 | map << 32 | x << 16 | y.
pub fn clearunit_delayed(c: &Ctx, bl: &Unit, clrtype: u8, tick: Tick) {
    let data = ((clrtype as i64) << 48) | ((bl.map as i64) << 32) | (((bl.x as u16) as i64) << 16) | ((bl.y as u16) as i64);
    crate::timer::add(c, tick, crate::timer::Func::ClifClearUnitDelayed, bl.id, data);
}

/// clif.cpp clif_clearunit_delayed_sub (timer).
pub fn clearunit_delayed_sub(c: &Ctx, id: u32, data: i64) {
    let clrtype = ((data >> 48) & 0xff) as u8;
    let m = ((data >> 32) & 0xffff) as u16;
    let x = ((data >> 16) & 0xffff) as u16 as i16;
    let y = (data & 0xffff) as u16 as i16;
    // tbl->type = BL_NUL
    let fake = Unit {
        id,
        kind: crate::map::BL_NUL,
        map: m,
        x,
        y,
        on_map: false,
        name: String::new(),
        vd: Default::default(),
        ud: Default::default(),
        sc: Default::default(),
        speed: 0,
        mode: 0,
        level: 0,
        char_id: 0,
        head_dir: 0,
        font: 0,
        manner: 0,
        karma: false,
        guild_id: 0,
        emblem_id: 0,
        hp: 0,
        max_hp: 0,
        dead: false,
        size: 0,
        boss: 0,
        invisible: false,
        npc_owner_char: 0,
    };
    core_clif::clearunit_area(c, &fake, clrtype);
}

/// clif.cpp clif_spawn, BL_MOB branch: tiny / big monsters' effect, ABR / Bionic summon init, the
/// name to the area.
pub fn spawn_mob(c: &Ctx, u: &Unit) {
    if let Some(md) = get(c, u.id) {
        let size = md.special_size as i32;
        if size == crate::status::defs::SZ_BIG {
            // tiny/big mobs [Valaris]
            core_clif::specialeffect(c, u, crate::data::constant("EF_GIANTBODY2").unwrap_or(0) as i32, Target::Area);
        } else if size == crate::status::defs::SZ_MEDIUM {
            core_clif::specialeffect(c, u, crate::data::constant("EF_BABYBODY2").unwrap_or(0) as i32, Target::Area);
        }
        if md.special_ai == super::AI_ABR || md.special_ai == super::AI_BIONIC {
            clif_summon_init(c, &md, u);
        }
    }
    core_clif::name_area(c, u);
}

/// clif.cpp clif_getareachar_unit, BL_MOB branch: the size effect and — when this player already hit it — the HP
/// bar, then the name to the area.
pub fn getareachar_mob(c: &Ctx, sd: &Unit, u: &Unit) {
    if let Some(md) = get(c, u.id) {
        let size = md.special_size as i32;
        if size == crate::status::defs::SZ_BIG {
            // tiny/big mobs [Valaris]
            core_clif::specialeffect_single(c, u, crate::data::constant("EF_GIANTBODY2").unwrap_or(0) as i32, sd);
        } else if size == crate::status::defs::SZ_MEDIUM {
            core_clif::specialeffect_single(c, u, crate::data::constant("EF_BABYBODY2").unwrap_or(0) as i32, sd);
        }
        if crate::conf::monster_hp_bars_info() && !crate::npc::mapflag(c, u.map, crate::npc::MF_HIDEMOBHPBAR) {
            // Must show hp bar to all char who already hit the mob.
            for entry in &md.dmglog {
                if entry.id == sd.char_id {
                    monster_hp_bar(c, &md, sd);
                }
            }
        }
    }
    core_clif::name_area(c, u);
}

/// clif.cpp clif_summon_init (PACKETVER_RE_NUM >= 20200724): ZC_SUMMON_HP_INIT to the summon's master
/// (battle_get_master; SELF: nothing reaches a master that is not a player).
pub fn clif_summon_init(c: &Ctx, md: &MobData, u: &Unit) {
    let master = crate::battle::target::battle_get_master(c, u);
    let p = PACKET_ZC_SUMMON_HP_INIT { PacketType: PACKET_ZC_SUMMON_HP_INIT::ID as i16, summonAID: md.id, CurrentHP: md.status.hp, MaxHP: md.status.max_hp };
    core_clif::send(c, &p.to_vec(), &master, Target::SelfOnly);
}

/// clif.cpp clif_summon_hp_bar (PACKETVER_RE_NUM >= 20200724): ZC_SUMMON_HP_UPDATE (SP_HP) to the summon's master.
pub fn clif_summon_hp_bar(c: &Ctx, md: &MobData, u: &Unit) {
    let master = crate::battle::target::battle_get_master(c, u);
    let p = PACKET_ZC_SUMMON_HP_UPDATE { PacketType: PACKET_ZC_SUMMON_HP_UPDATE::ID as i16, summonAID: md.id, VarId: crate::status::consts::SP_HP as u16, Value: md.status.hp };
    core_clif::send(c, &p.to_vec(), &master, Target::SelfOnly);
}
