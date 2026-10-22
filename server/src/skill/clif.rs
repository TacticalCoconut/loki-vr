//! The skill-use packets of clif.cpp (rAthena e985006, PACKETVER 20211103): clif_skillcasting (ZC_USESKILL_ACK3),
//! clif_skill_scale, clif_skillcastcancel (ZC_DISPEL), clif_skill_fail (ZC_ACK_TOUSESKILL), clif_skill_cooldown
//! (ZC_SKILL_POSTDELAY), clif_skill_damage (ZC_NOTIFY_SKILL2), clif_skill_nodamage (ZC_USE_SKILL2),
//! clif_skill_poseffect (ZC_NOTIFY_GROUNDSKILL), clif_skill_teleportmessage (ZC_NOTIFY_MAPINFO), clif_combo_delay
//! (ZC_COMBODELAY), clif_spiritball / clif_soulball / clif_servantball / clif_abyssball / clif_spiritcharm.
//!
//! Disguises (atcmd's pc_disguise, crate::atcmd_disguise): a disguised player's area gets the packet (AREA_WOS) and its
//! own client the copy with the disguise id (disguised_bl_id), as in rAthena.

use crate::atcmd_disguise::{disguised, disguised_bl_id};
use crate::battle::clif::{clif_calc_delay, clif_hallucination_damage};
use crate::clif::{self, Target};
use crate::net::packets::*;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// clif.hpp DMGVAL_IGNORE: a damage value the client does not display (skill_area_sub's SD_PREAMBLE packet).
pub const DMGVAL_IGNORE: i64 = -30000;

/// clif.hpp enum useskill_fail_cause.
pub mod fail {
    pub const USESKILL_FAIL_LEVEL: u8 = 0;
    pub const USESKILL_FAIL_SP_INSUFFICIENT: u8 = 1;
    pub const USESKILL_FAIL_HP_INSUFFICIENT: u8 = 2;
    pub const USESKILL_FAIL_STUFF_INSUFFICIENT: u8 = 3;
    pub const USESKILL_FAIL_SKILLINTERVAL: u8 = 4;
    pub const USESKILL_FAIL_MONEY: u8 = 5;
    pub const USESKILL_FAIL_THIS_WEAPON: u8 = 6;
    pub const USESKILL_FAIL_REDJAMSTONE: u8 = 7;
    pub const USESKILL_FAIL_BLUEJAMSTONE: u8 = 8;
    pub const USESKILL_FAIL_WEIGHTOVER: u8 = 9;
    pub const USESKILL_FAIL: u8 = 10;
    pub const USESKILL_FAIL_TOTARGET: u8 = 11;
    pub const USESKILL_FAIL_ANCILLA_NUMOVER: u8 = 12;
    pub const USESKILL_FAIL_HOLYWATER: u8 = 13;
    pub const USESKILL_FAIL_ANCILLA: u8 = 14;
    pub const USESKILL_FAIL_DUPLICATE_RANGEIN: u8 = 15;
    pub const USESKILL_FAIL_NEED_OTHER_SKILL: u8 = 16;
    pub const USESKILL_FAIL_NEED_HELPER: u8 = 17;
    pub const USESKILL_FAIL_INVALID_DIR: u8 = 18;
    pub const USESKILL_FAIL_SUMMON: u8 = 19;
    pub const USESKILL_FAIL_SUMMON_NONE: u8 = 20;
    pub const USESKILL_FAIL_IMITATION_SKILL_NONE: u8 = 21;
    pub const USESKILL_FAIL_DUPLICATE: u8 = 22;
    pub const USESKILL_FAIL_CONDITION: u8 = 23;
    pub const USESKILL_FAIL_PAINTBRUSH: u8 = 24;
    pub const USESKILL_FAIL_DRAGON: u8 = 25;
    pub const USESKILL_FAIL_POS: u8 = 26;
    pub const USESKILL_FAIL_HELPER_SP_INSUFFICIENT: u8 = 27;
    pub const USESKILL_FAIL_NEER_WALL: u8 = 28;
    pub const USESKILL_FAIL_NEED_EXP_1PERCENT: u8 = 29;
    pub const USESKILL_FAIL_CHORUS_SP_INSUFFICIENT: u8 = 30;
    pub const USESKILL_FAIL_GC_WEAPONBLOCKING: u8 = 31;
    pub const USESKILL_FAIL_GC_POISONINGWEAPON: u8 = 32;
    pub const USESKILL_FAIL_MADOGEAR: u8 = 33;
    pub const USESKILL_FAIL_NEED_EQUIPMENT_KUNAI: u8 = 34;
    pub const USESKILL_FAIL_TOTARGET_PLAYER: u8 = 35;
    pub const USESKILL_FAIL_SIZE: u8 = 36;
    pub const USESKILL_FAIL_CANONBALL: u8 = 37;
    pub const USESKILL_FAIL_MADOGEAR_HOVERING: u8 = 40;
    pub const USESKILL_FAIL_GUILLONTINE_POISON: u8 = 43;
    pub const USESKILL_FAIL_MADOGEAR_RIDE: u8 = 50;
    pub const USESKILL_FAIL_SPELLBOOK: u8 = 51;
    pub const USESKILL_FAIL_SPELLBOOK_DIFFICULT_SLEEP: u8 = 52;
    pub const USESKILL_FAIL_SPELLBOOK_PRESERVATION_POINT: u8 = 53;
    pub const USESKILL_FAIL_SPELLBOOK_READING: u8 = 54;
    pub const USESKILL_FAIL_CART: u8 = 57;
    pub const USESKILL_FAIL_NO_MORE_SPELL: u8 = 60;
    pub const USESKILL_FAIL_MANUAL_NOTIFY: u8 = 70;
    pub const USESKILL_FAIL_NEED_ITEM: u8 = 71;
    pub const USESKILL_FAIL_NEED_EQUIPMENT: u8 = 72;
    pub const USESKILL_FAIL_COMBOSKILL: u8 = 73;
    pub const USESKILL_FAIL_SPIRITS: u8 = 74;
    pub const USESKILL_FAIL_EXPLOSIONSPIRITS: u8 = 75;
    pub const USESKILL_FAIL_HP_TOOMANY: u8 = 76;
    pub const USESKILL_FAIL_NEED_ROYAL_GUARD_BANDING: u8 = 77;
    pub const USESKILL_FAIL_NEED_EQUIPPED_WEAPON_CLASS: u8 = 78;
    pub const USESKILL_FAIL_EL_SUMMON: u8 = 79;
    pub const USESKILL_FAIL_RELATIONGRADE: u8 = 80;
    pub const USESKILL_FAIL_STYLE_CHANGE_FIGHTER: u8 = 81;
    pub const USESKILL_FAIL_STYLE_CHANGE_GRAPPLER: u8 = 82;
    pub const USESKILL_FAIL_THERE_ARE_NPC_AROUND: u8 = 83;
    pub const USESKILL_FAIL_NEED_MORE_BULLET: u8 = 84;
    pub const USESKILL_FAIL_COINS: u8 = 85;
    pub const USESKILL_FAIL_SH_TEMPORARY_COMMUNION: u8 = 86;
    pub const USESKILL_FAIL_MAP: u8 = 87;
    pub const USESKILL_FAIL_NEED_SUMMON_SP: u8 = 88;
    pub const USESKILL_FAIL_SAME_VEND: u8 = 89;
    pub const USESKILL_FAIL_NEED_BULLETS: u8 = 90;
    pub const USESKILL_FAIL_NEED_ARROWS: u8 = 91;
    pub const USESKILL_FAIL_NEED_PARTY: u8 = 92;
    pub const USESKILL_FAIL_NEED_PARTY_LEADER: u8 = 93;
    pub const USESKILL_FAIL_NEED_PARTNER_SP: u8 = 94;
    pub const USESKILL_FAIL_NEED_WEAPON: u8 = 95;
    pub const USESKILL_FAIL_NEED_ENERGY_SPHERE: u8 = 96;
    pub const USESKILL_FAIL_NEED_WEAPON_BLOCKING: u8 = 97;
    pub const USESKILL_FAIL_MAX_TRAPS: u8 = 98;
    pub const USESKILL_FAIL_NEED_MAGIC_SPELLBOOK: u8 = 99;
    pub const USESKILL_FAIL_AP_INSUFFICIENT: u8 = 100;
    pub const USESKILL_FAIL_NEED_SERVANT_WEAPON: u8 = 101;
    pub const USESKILL_FAIL_NEED_TWINKLING_GALAXY: u8 = 102;
    pub const USESKILL_FAIL_NEED_SOUL_ENGERY: u8 = 103;
    pub const USESKILL_FAIL_NEED_AMULET: u8 = 104;
    pub const USESKILL_FAIL_NEED_GATLING_SHOTGUT: u8 = 105;
    pub const USESKILL_FAIL_NEED_RIFLE_REVOLVER: u8 = 106;
    pub const USESKILL_FAIL_NEED_GLAUNCHER_RIFLE: u8 = 107;
    pub const USESKILL_FAIL_NEED_GATLING_REVOLVER: u8 = 108;
    pub const USESKILL_FAIL_NEED_SHOTGUN_GLAUNCHER: u8 = 109;
    pub const USESKILL_FAIL_NEED_SHIELD: u8 = 110;
    pub const USESKILL_FAIL_TARGET_SHADOW_SPACE: u8 = 111;
    pub const USESKILL_FAIL_MAX: u8 = 112;
}

/// clif.cpp client_tick: the tick as the client's 32-bit clock.
pub fn client_tick(tick: Tick) -> u32 {
    tick as u32
}

/// packets_struct.hpp `skillscale = 0xA41` (PACKETVER >= 20151223): ZC_SKILL_SCALE's header.
const HEADER_ZC_SKILL_SCALE: i16 = 0x0A41;

/// clif.cpp clif_skill_scale (PACKETVER >= 20151223; battle_config.show_skill_scale).
#[allow(clippy::too_many_arguments)]
pub fn clif_skill_scale(c: &Ctx, bl: &Unit, src_id: u32, x: i16, y: i16, skill_id: u16, skill_lv: u16, casttime: i32) {
    if !super::conf::show_skill_scale() {
        return;
    }
    let p = PACKET_ZC_SKILL_SCALE {
        PacketType: HEADER_ZC_SKILL_SCALE,
        AID: src_id,
        skill_id: skill_id as i16,
        skill_lv: skill_lv as i16,
        x,
        y,
        casttime: casttime as u32,
    };
    let mut p = p;
    if disguised(c, bl) {
        clif::send(c, &p.to_vec(), bl, Target::AreaWos);
        p.AID = disguised_bl_id(bl.id);
        clif::send(c, &p.to_vec(), bl, Target::SelfOnly);
    } else {
        clif::send(c, &p.to_vec(), bl, Target::Area);
    }
}

/// clif.cpp clif_skillcasting: ZC_USESKILL_ACK (0x0b1a) to the source's area; the skill scale when the skill shows it.
#[allow(clippy::too_many_arguments)]
pub fn clif_skillcasting(c: &Ctx, src: &Unit, dst: Option<&Unit>, dst_x: u16, dst_y: u16, skill_id: u16, skill_lv: u16, property: i32, casttime: i32) {
    use crate::status::defs::{ELE_ALL, ELE_NEUTRAL, ELE_NONE};
    let element = if property > ELE_NONE && property < ELE_ALL { property } else { ELE_NEUTRAL };
    let mut p = PACKET_ZC_USESKILL_ACK {
        packetType: PACKET_ZC_USESKILL_ACK::ID as i16,
        srcId: src.id,
        dstId: dst.map(|d| d.id).unwrap_or(0),
        x: dst_x,
        y: dst_y,
        skillId: skill_id,
        element: element as u32,
        delayTime: casttime as u32,
        disposable: 0,
        attackMT: 0,
    };
    if disguised(c, src) {
        clif::send(c, &p.to_vec(), src, Target::AreaWos);
        p.srcId = disguised_bl_id(src.id);
        clif::send(c, &p.to_vec(), src, Target::SelfOnly);
    } else {
        clif::send(c, &p.to_vec(), src, Target::Area);
    }
    if crate::db::skills::skill_get_inf2(skill_id, &[crate::db::skills::INF2_SHOWSCALE]) {
        clif_skill_scale(c, src, src.id, src.x, src.y, skill_id, skill_lv, casttime);
    }
}

/// clif.cpp clif_skillcastcancel: ZC_DISPEL to the unit's area.
pub fn clif_skillcastcancel(c: &Ctx, bl: &Unit) {
    let p = PACKET_ZC_DISPEL { packetType: PACKET_ZC_DISPEL::ID as i16, gid: bl.id };
    clif::send(c, &p.to_vec(), bl, Target::Area);
}

/// clif.cpp clif_skill_fail: ZC_ACK_TOUSESKILL to the player (display_skill_fail 2: every message; the
/// SKILLINTERVAL one only with @showdelay).
pub fn clif_skill_fail(c: &Ctx, sd: &Unit, skill_id: u16, cause: u8, btype: i32, item_id: u32) {
    use crate::db::skills::{RG_SNATCHER, TF_POISON};
    let dsf = super::conf::display_skill_fail();
    if dsf & 1 != 0 {
        return; // Disable all skill failed messages
    }
    // sd->state.showdelay: @showdelay's field (atcmd_pc.rs; the merge of atcmd and skillcore in the vending branch)
    if cause == fail::USESKILL_FAIL_SKILLINTERVAL && !crate::atcmd_pc::get(c, sd.id).showdelay {
        return; // Disable delay failed messages
    }
    if skill_id == RG_SNATCHER && dsf & 4 != 0 {
        return;
    }
    if skill_id == TF_POISON && dsf & 8 != 0 {
        return;
    }
    let p = PACKET_ZC_ACK_TOUSESKILL { packetType: PACKET_ZC_ACK_TOUSESKILL::ID as i16, skillId: skill_id, btype, itemId: item_id, flag: 0, cause };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// clif_skill_fail(sd, skill_id) with the defaults (USESKILL_FAIL_LEVEL, 0, 0).
pub fn skill_fail(c: &Ctx, sd: &Unit, skill_id: u16) {
    clif_skill_fail(c, sd, skill_id, fail::USESKILL_FAIL_LEVEL, 0, 0);
}

/// clif_skill_fail(sd, skill_id, cause).
pub fn skill_fail_cause(c: &Ctx, sd: &Unit, skill_id: u16, cause: u8) {
    clif_skill_fail(c, sd, skill_id, cause, 0, 0);
}

/// clif.cpp clif_skill_cooldown: ZC_SKILL_POSTDELAY (the cooldown icon) to the player.
pub fn clif_skill_cooldown(c: &Ctx, sd: &Unit, skill_id: u16, tick: Tick) {
    let p = PACKET_ZC_SKILL_POSTDELAY { PacketType: PACKET_ZC_SKILL_POSTDELAY::ID as i16, SKID: skill_id, DelayTM: client_tick(tick) };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// clif.cpp clif_skill_damage: ZC_NOTIFY_SKILL2 (0x01de) to the target's area. `skill_lv` is the uint16 rAthena
/// passes (callers use -1 / -2 for special displays; the packet's level is int16).
#[allow(clippy::too_many_arguments)]
pub fn clif_skill_damage(c: &Ctx, src: &Unit, dst: &Unit, tick: Tick, sdelay: i32, ddelay: i32, sdamage: i64, div: i16, skill_id: u16, skill_lv: i32, type_: u8) {
    use crate::battle::{DMG_MULTI_HIT, DMG_SINGLE};
    let mut type_ = clif_calc_delay(c, dst, type_, div, sdamage, ddelay, tick);
    let sdamage = clif_hallucination_damage(c, dst, sdamage);
    // std::min(static_cast<int32>(sdamage), INT32_MAX): the cast comes first (the value wraps), the min does nothing
    let damage = sdamage as i32;
    let pdamage = if crate::battle::conf::hide_woe_damage() && crate::npc::mapflag_gvg(c, src.map) {
        if damage != 0 {
            div as i32
        } else {
            0
        }
    } else {
        damage
    };
    // For some reason, late 2013 and newer clients have a issue that causes players and monsters to endure type 6
    // (ACTION_SKILL) skills. So we have to do a small hack to set all type 6 to be sent as type 8 ACTION_ATTACK_MULTIPLE
    if damage as i64 != DMGVAL_IGNORE && type_ == DMG_SINGLE {
        type_ = DMG_MULTI_HIT;
    }
    let mut p = PACKET_ZC_NOTIFY_SKILL {
        PacketType: PACKET_ZC_NOTIFY_SKILL::ID as i16,
        SKID: skill_id,
        AID: src.id,
        targetID: dst.id,
        startTime: client_tick(tick),
        attackMT: sdelay,
        attackedMT: ddelay,
        damage: pdamage,
        level: skill_lv as u16 as i16,
        count: div,
        action: type_ as i8,
    };
    if disguised(c, dst) {
        clif::send(c, &p.to_vec(), dst, Target::AreaWos);
        p.targetID = disguised_bl_id(dst.id);
        clif::send(c, &p.to_vec(), dst, Target::SelfOnly);
    } else {
        clif::send(c, &p.to_vec(), dst, Target::Area);
    }
    if disguised(c, src) {
        p.AID = disguised_bl_id(src.id);
        if disguised(c, dst) {
            p.targetID = dst.id;
        }
        if damage > 0 {
            p.damage = -1;
        }
        clif::send(c, &p.to_vec(), src, Target::SelfOnly);
    }
}

/// clif.cpp clif_skill_nodamage: ZC_USE_SKILL2 (0x09cb) to the target's area; returns `success`.
pub fn clif_skill_nodamage(c: &Ctx, src: Option<&Unit>, dst: &Unit, skill_id: u16, heal: i32, success: bool) -> bool {
    let mut p = PACKET_ZC_USE_SKILL {
        PacketType: PACKET_ZC_USE_SKILL::ID as i16,
        SKID: skill_id,
        level: heal,
        targetAID: dst.id,
        srcAID: src.map(|s| s.id).unwrap_or(0),
        result: success as i8,
    };
    // the disguised branches (atcmd's disguises: its copy of this function in battle/atcommand.rs, merged here in the
    // vending branch)
    use crate::atcmd_disguise::{disguised, disguised_bl_id};
    if disguised(c, dst) {
        clif::send(c, &p.to_vec(), dst, Target::AreaWos);
        p.targetAID = disguised_bl_id(dst.id);
        clif::send(c, &p.to_vec(), dst, Target::SelfOnly);
    } else {
        clif::send(c, &p.to_vec(), dst, Target::Area);
    }
    if let Some(src) = src.filter(|s| disguised(c, s)) {
        p.srcAID = disguised_bl_id(src.id);
        if disguised(c, dst) {
            // It is necessary to revert the changes done above for the disguised target
            p.targetAID = dst.id;
        }
        clif::send(c, &p.to_vec(), src, Target::SelfOnly);
    }
    success
}

/// clif.cpp clif_skill_poseffect: ZC_NOTIFY_GROUNDSKILL to the unit's area.
pub fn clif_skill_poseffect(c: &Ctx, bl: &Unit, skill_id: u16, skill_lv: u16, x: i16, y: i16, tick: Tick) {
    crate::mob::clif::skill_poseffect(c, bl, skill_id, skill_lv, x, y, tick);
}

/// clif.cpp clif_skill_estimation: ZC_MONSTER_INFO (0x018c, Sense's window) about the monster `md` to the player,
/// or to its party members on its map (PARTY_SAMEMAP) when it is in a party.
pub fn clif_skill_estimation(c: &Ctx, sd: &Unit, md: &Unit) {
    use crate::status::defs::{ELE_DARK, ELE_EARTH, ELE_FIRE, ELE_GHOST, ELE_HOLY, ELE_POISON, ELE_UNDEAD, ELE_WATER, ELE_WIND};
    let est = super::conf::estimation_type();
    let st = crate::status::status_get_status_data(c, md);
    // md.level: the mob_data row (the unit's status_get_lv is the same value for a monster)
    let level = crate::mob::get_or_freed(c, md.id).map(|m| m.level).unwrap_or(md.level);
    let db = crate::db::misc::status::elemental_attribute_db();
    // The following caps negative attributes to 0 since the client displays them as 255-fix. [Skotlex]
    let attr = |ele: i32| db.get_attribute(st.ele_lv as u16, ele as u16, st.def_ele as u16).max(0) as u8;
    let def = (if est & 1 != 0 { st.def as i32 } else { 0 }) + (if est & 2 != 0 { st.def2 as i32 } else { 0 });
    let mdef = (if est & 1 != 0 { st.mdef as i32 } else { 0 }) + (if est & 2 != 0 { st.mdef2 as i32 } else { 0 });
    let p = PACKET_ZC_MONSTER_INFO {
        packetType: PACKET_ZC_MONSTER_INFO::ID as i16,
        class_: md.vd.class as u16, // md.vd->look[LOOK_BASE]
        level: level as u16,
        size: st.size as u16,
        hp: st.hp,
        def: def as i16,
        race: st.race as u16,
        mdef: mdef as i16,
        element: st.def_ele as u16,
        water: attr(ELE_WATER),
        earth: attr(ELE_EARTH),
        fire: attr(ELE_FIRE),
        wind: attr(ELE_WIND),
        poison: attr(ELE_POISON),
        holy: attr(ELE_HOLY),
        shadow: attr(ELE_DARK),
        ghost: attr(ELE_GHOST),
        undead: attr(ELE_UNDEAD),
    };
    if crate::party::party_id_of(c, sd.id) > 0 {
        crate::party::send(c, &p.to_vec(), sd, crate::party::PTarget::PartySamemap);
    } else {
        clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
    }
}

/// clif.cpp clif_snap: 0x08d2 <id>.L <x>.W <y>.W (packet_len 10), the instant move of Snap / Fallen Angel / All In
/// The Sky, to the unit's area (a disguised unit: the area without it, then itself with its disguise's id). (Added
/// by the acolyte agent for MO_BODYRELOCATION; the ninjags / taekwon classes call it too.)
pub fn clif_snap(c: &Ctx, bl: &Unit, x: i16, y: i16) {
    use crate::atcmd_disguise::{disguised, disguised_bl_id};
    let mut w = Vec::with_capacity(10);
    w.extend_from_slice(&0x08d2u16.to_le_bytes());
    w.extend_from_slice(&bl.id.to_le_bytes());
    w.extend_from_slice(&x.to_le_bytes());
    w.extend_from_slice(&y.to_le_bytes());
    if disguised(c, bl) {
        clif::send(c, &w, bl, Target::AreaWos);
        w[2..6].copy_from_slice(&disguised_bl_id(bl.id).to_le_bytes());
        clif::send(c, &w, bl, Target::SelfOnly);
    } else {
        clif::send(c, &w, bl, Target::Area);
    }
}

/// clif.hpp e_notify_mapinfo_result.
pub const NOTIFY_MAPINFO_CANT_TP: i16 = 0;
pub const NOTIFY_MAPINFO_CANT_MEMO: i16 = 1;
pub const NOTIFY_MAPINFO_CANT_USE_SKILL: i16 = 2;
pub const NOTIFY_MAPINFO_CANT_USE_ITEM: i16 = 3;

/// clif.cpp clif_skill_teleportmessage: ZC_NOTIFY_MAPINFO to the player.
pub fn clif_skill_teleportmessage(c: &Ctx, sd: &Unit, result: i16) {
    let p = PACKET_ZC_NOTIFY_MAPINFO { packetType: PACKET_ZC_NOTIFY_MAPINFO::ID as i16, r#type: result };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// clif.cpp clif_combo_delay: ZC_COMBODELAY to the unit's area.
pub fn clif_combo_delay(c: &Ctx, bl: &Unit, wait: Tick) {
    let p = PACKET_ZC_COMBODELAY { packetType: PACKET_ZC_COMBODELAY::ID as i16, AID: bl.id, delay: client_tick(wait) };
    clif::send(c, &p.to_vec(), bl, Target::Area);
}

/// clif.cpp clif_spiritball: ZC_SPIRITS with the player's (or homunculus') spheres, around `bl` or to `target`.
pub fn clif_spiritball(c: &Ctx, bl: &Unit, target: Option<&Unit>, send_target: Target) {
    let num = match bl.kind {
        crate::map::BL_PC => super::state::get(c, bl.id).spiritball as i16,
        // BL_HOM: static_cast<const homun_data*>(bl)->homunculus.spiritball (homunculus/data.rs)
        crate::map::BL_HOM => crate::homunculus::data::get(c, bl.id).map(|hd| hd.homunculus.spiritball as i16).unwrap_or(0),
        _ => 0,
    };
    let p = PACKET_ZC_SPIRITS { PacketType: PACKET_ZC_SPIRITS::ID as i16, AID: bl.id, num };
    clif::send(c, &p.to_vec(), target.unwrap_or(bl), send_target);
}

/// clif.cpp clif_item_skill: ZC_AUTORUN_SKILL to the player (a skill offered by an item or the itemskill script
/// command): skill id, skill_get_inf, level, skill_get_sp, skill_get_range2(sd, id, lv, false), the AEGIS name
/// (safestrncpy NAME_LENGTH) and up_flag false.
pub fn clif_item_skill(c: &Ctx, sd: &Unit, skill_id: u16, skill_lv: u16) {
    use crate::db::skills::{skill_get_inf, skill_get_name, skill_get_sp};
    let p = PACKET_ZC_AUTORUN_SKILL {
        packetType: PACKET_ZC_AUTORUN_SKILL::ID as i16,
        skill_id,
        skill_type: skill_get_inf(skill_id) as u32,
        skill_lv,
        skill_sp: skill_get_sp(skill_id, skill_lv) as u16,
        skill_range: super::misc::skill_get_range2(c, sd, skill_id, skill_lv, false) as u16,
        skill_name: skill_get_name(skill_id).to_string(),
        up_flag: 0,
    };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// packet_db 0x07e3 (ZC_ITEMLISTWIN_OPEN): 6 bytes (clif_packetdb.hpp `packet(0x07e3,6)`, no struct in the generated
/// packet table).
pub const ZC_ITEMLISTWIN_OPEN: u16 = 0x07e3;
pub const ZC_ITEMLISTWIN_OPEN_LEN: usize = 6;

/// clif.cpp clif_skill_itemlistwindow (PACKETVER >= 20090922): the item list window of Four Spirit Analysis
/// (SO_EL_ANALYSIS) and Change Material (GN_CHANGEMATERIAL): sets sd->menuskill_id / menuskill_val (to prevent
/// hacking; menuskill_val2 is left as it is), then 0x07e3 <type>.L (the skill level, 0 for Change Material) to the
/// player. Always returns 1. (The answer, CZ_ITEMLISTWIN_RES 0x07e4, is clif_parse_ItemListWindowSelected.)
pub fn clif_skill_itemlistwindow(c: &Ctx, sd: &Unit, skill_id: u16, skill_lv: u16) -> i32 {
    let mut skill_lv = skill_lv;
    // sd->menuskill_id = skill_id; sd->menuskill_val = skill_lv;
    crate::itemui::update(c, sd.id, |s| {
        s.menuskill_id = skill_id as i32;
        s.menuskill_val = skill_lv as i32;
    });
    if skill_id == crate::db::skills::GN_CHANGEMATERIAL {
        skill_lv = 0; // Changematerial
    }
    let mut buf = vec![0u8; ZC_ITEMLISTWIN_OPEN_LEN];
    buf[0..2].copy_from_slice(&ZC_ITEMLISTWIN_OPEN.to_le_bytes());
    buf[2..6].copy_from_slice(&(skill_lv as u32).to_le_bytes());
    clif::send(c, &buf, sd, Target::SelfOnly);
    1
}

/// packet_db 0x01cf (ZC_DEVOTIONLIST): 28 bytes (no struct in the generated packet table).
pub const ZC_DEVOTIONLIST: u16 = 0x01cf;
pub const ZC_DEVOTIONLIST_LEN: usize = 28;

/// clif.cpp clif_devotion: 0x01cf (ZC_DEVOTIONLIST, 28 bytes) with the player's first 5 devotion links (the client
/// shows 5) and CR_DEVOTION's range, to `tsd` or else the area. A mercenary (BL_MER) sends its master as the only
/// link when it devotes him (md->devotion_flag) and ML_DEVOTION's range; other unit types send nothing.
pub fn clif_devotion(c: &Ctx, src: &Unit, tsd: Option<&Unit>) {
    use crate::db::skills::{CR_DEVOTION, ML_DEVOTION};
    let mut buf = vec![0u8; ZC_DEVOTIONLIST_LEN];
    buf[0..2].copy_from_slice(&ZC_DEVOTIONLIST.to_le_bytes());
    buf[2..6].copy_from_slice(&src.id.to_le_bytes());
    match src.kind {
        crate::map::BL_MER => {
            // clif.cpp:8491-8496: `if( md && md->master && md->devotion_flag ) WBUFL(buf,6) = md->master->id;`
            let md = crate::mercenary::data::get(c, src.id);
            if md.as_ref().map(|m| m.devotion_flag).unwrap_or(false) {
                if let Some(master) = super::iface::mercenary_master_id(c, src) {
                    buf[6..10].copy_from_slice(&master.to_le_bytes());
                }
            }
            // skill_get_range2(src, ML_DEVOTION, mercenary_checkskill(md, ML_DEVOTION), false)
            let lv = md.as_ref().map(|m| crate::mercenary::mercenary_checkskill(m, ML_DEVOTION)).unwrap_or(0);
            let range = super::misc::skill_get_range2(c, src, ML_DEVOTION, lv, false) as u16;
            buf[26..28].copy_from_slice(&range.to_le_bytes());
        }
        crate::map::BL_PC => {
            let s = super::state::get(c, src.id);
            // Client only able show to 5 links
            for i in 0..5 {
                let d = s.devotion.get(i).copied().unwrap_or(0);
                buf[6 + 4 * i..10 + 4 * i].copy_from_slice(&d.to_le_bytes());
            }
            let lv = crate::skill::checkskill(c, src.id, CR_DEVOTION);
            let range = super::misc::skill_get_range2(c, src, CR_DEVOTION, lv as u16, false) as u16;
            buf[26..28].copy_from_slice(&range.to_le_bytes());
        }
        _ => return,
    }
    match tsd {
        Some(t) => clif::send(c, &buf, t, Target::SelfOnly),
        None => clif::send(c, &buf, src, Target::Area),
    }
}
