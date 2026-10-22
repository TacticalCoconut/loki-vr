//! The homunculus packets of clif.cpp: clif_homunculus_updatestatus (ZC_HO_PAR_CHANGE), clif_hominfo
//! (ZC_PROPERTY_HOMUN), clif_send_homdata (ZC_CHANGESTATE_MER), clif_homskillinfoblock (ZC_HOSKILLINFO_LIST),
//! clif_homskillup (ZC_HOSKILLINFO_UPDATE), clif_hom_food (ZC_FEED_MER), and the request handlers
//! clif_parse_ChangeHomunculusName (CZ_RENAME_MER), clif_parse_HomMenu (CZ_COMMAND_MER), and the
//! CONFIG_HOMUNCULUS_AUTOFEED arm of clif_parse_configuration. (clif_parse_HomMoveToMaster / HomMoveTo / HomAttack,
//! shared with the mercenary, are mercenary/clif.rs'.) (The homunculus arms of
//! clif_parse_UseSkillToId / ToPos are the skill pipeline's: skill/parse.rs.)

use super::data::{self, HomData, HOMSET_DISPLAY_LUK, HOM_ST_REST, SP_ACK, SP_HUNGRY, SP_INTIMATE};
use crate::clif::{self, Target};
use crate::net::packets::*;
use crate::status::consts::{SP_BASEEXP, SP_HP, SP_SP};
use crate::unit;
use crate::world::Ctx;

/// Sends to the player `aid` (clif_send(…, sd, SELF)).
fn send_self(c: &Ctx, aid: u32, buf: &[u8]) {
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, buf, &sd, Target::SelfOnly);
    }
}

/// clif.cpp clif_homunculus_updatestatus (PACKETVER >= 20090610: ZC_HO_PAR_CHANGE 0xba5, value.Q): SP_BASEEXP, SP_HP
/// or SP_SP of the player's active homunculus. PACKETVER_RE_NUM 20211103 >= 20200723: an HP / SP above INT32_MAX is
/// sent as a percentage.
pub fn clif_homunculus_updatestatus(c: &Ctx, aid: u32, type_: i32) {
    let Some(hd) = data::of_master(c, aid) else { return };
    if !data::hom_is_active(Some(&hd)) {
        return;
    }
    let status = &hd.battle_status;
    let value: u64 = match type_ {
        SP_BASEEXP => hd.homunculus.exp.min(u64::MAX),
        SP_HP => {
            // Tested maximum: 2147483647 (INT32_MAX)
            if status.max_hp > i32::MAX as u32 {
                (status.hp / (status.max_hp / 100)) as u64
            } else {
                status.hp as u64
            }
        }
        SP_SP => {
            if status.max_sp > i32::MAX as u32 {
                (status.sp / (status.max_sp / 100)) as u64
            } else {
                status.sp as u64
            }
        }
        _ => {
            log::error!("clif_homunculus_updatestatus: Unsupported type {type_}.");
            return;
        }
    };
    let p = PACKET_ZC_HO_PAR_CHANGE { packetType: PACKET_ZC_HO_PAR_CHANGE::ID as i16, r#type: type_ as u16, value };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_hominfo (ZC_PROPERTY_HOMUN 0xba4 at PACKETVER 20211103): the homunculus' window to its master. flag 1
/// sends an attack speed of 0.
pub fn clif_hominfo(c: &Ctx, aid: u32, hd_id: u32, flag: i32) {
    let Some(hd) = data::get(c, hd_id) else { return };
    if crate::pc::get(c, aid).is_none() {
        return;
    }
    let p = hominfo_packet(c, &hd, flag);
    send_self(c, aid, &p.to_vec());
}

/// The ZC_PROPERTY_HOMUN of clif_hominfo.
pub fn hominfo_packet(c: &Ctx, hd: &HomData, flag: i32) -> PACKET_ZC_PROPERTY_HOMUN {
    let status = &hd.battle_status;
    let h = &hd.homunculus;
    // Bit field, bit 0 : rename_flag (1 = already renamed), bit 1 : homunc vaporized (1 = true), bit 2 : homunc dead (1 = true)
    let flags = (if !super::hom_rename() && h.rename_flag != 0 { 0x1 } else { 0x0 }) | (if h.vaporize as i32 == HOM_ST_REST { 0x2 } else { 0 }) | (if h.hp > 0 { 0x4 } else { 0 });
    let crit = if super::hom_setting() & HOMSET_DISPLAY_LUK != 0 {
        (status.luk as i32 / 3 + 1) as u16 //crit is a +1 decimal value! Just display purpose.[Vicious]
    } else {
        (status.cri as i32 / 10) as u16
    };
    // Homunculus HP and SP bars will screw up if the percentage calculation exceeds signed values
    // (PACKETVER_RE_NUM >= 20200723: Tested maximum: 2147483647 (INT32_MAX))
    let (hp, max_hp) = if status.max_hp > i32::MAX as u32 { (status.hp / (status.max_hp / 100), 100) } else { (status.hp, status.max_hp) };
    let (sp, max_sp) = if status.max_sp > i32::MAX as u32 { (status.sp / (status.max_sp / 100), 100) } else { (status.sp, status.max_sp) };
    let mut exp_next = hd.exp_next.min(i64::MAX as u64) as i64;
    match super::hom_class2type(h.class_ as i32) {
        super::data::HT_REG | super::data::HT_EVO => {
            if h.level as i32 >= super::hom_max_level() {
                exp_next = 0;
            }
        }
        super::data::HT_S => {
            if h.level as i32 >= super::HOM_S_MAX_LEVEL {
                exp_next = 0;
            }
        }
        _ => {}
    }
    let _ = c;
    PACKET_ZC_PROPERTY_HOMUN {
        packetType: PACKET_ZC_PROPERTY_HOMUN::ID as i16,
        name: h.name.chars().take(crate::rules::NAME_MAX).collect(),
        flags,
        level: h.level as u16,
        hunger: h.hunger as u16,
        intimacy: (h.intimacy / 100) as u16,
        atk2: (status.rhw.atk2 as i32 + status.batk).clamp(0, i16::MAX as i32) as u16,
        // i16min(status->matk_max, INT16_MAX): i16min takes int16, so a matk_max above INT16_MAX wraps negative and comes
        // back unchanged in the uint16 field (the raw value is sent) //FIXME capping to INT16 here is too late
        matk: status.matk_max,
        hit: status.hit as u16,
        crit,
        // RENEWAL
        def: (status.def as i32 + status.def2 as i32) as u16,
        mdef: (status.mdef as i32 + status.mdef2 as i32) as u16,
        flee: status.flee as u16,
        amotion: if flag != 0 { 0 } else { status.amotion },
        hp,
        maxHp: max_hp,
        sp,
        maxSp: max_sp,
        exp: h.exp.min(i64::MAX as u64) as i64,
        expNext: exp_next,
        skillPoints: h.skillpts as u16,
        // status_get_range(hd)
        range: status.rhw.range,
    }
}

/// clif.cpp clif_send_homdata (ZC_CHANGESTATE_MER): SP_ACK (0), SP_INTIMATE (intimacy / 100; a Loyal evolved
/// homunculus' skill tree is recalculated first) or SP_HUNGRY (hunger), to the master.
pub fn clif_send_homdata(c: &Ctx, hd_id: u32, state: u8) {
    let Some(hd) = data::get(c, hd_id) else { return };
    if data::master_sd(c, &hd).is_none() {
        return;
    }
    let param: u32 = match state {
        SP_ACK => 0,
        SP_INTIMATE => {
            // TODO: Why is something like this here? [Lemongrass]
            let evolved = hd.db().map(|db| hd.homunculus.class_ as i32 == db.evo_class).unwrap_or(false);
            if evolved && super::hom_intimacy_intimacy2grade(hd.homunculus.intimacy) >= super::data::HOMGRADE_LOYAL {
                super::hom_calc_skilltree(c, hd_id);
            }
            let hd = data::get(c, hd_id).unwrap_or(hd.clone());
            hd.homunculus.intimacy / 100
        }
        SP_HUNGRY => hd.homunculus.hunger as u32,
        _ => return,
    };
    let p = PACKET_ZC_CHANGESTATE_MER { packetType: PACKET_ZC_CHANGESTATE_MER::ID as i16, r#type: 0, state, gid: hd_id, data: param };
    send_self(c, hd.master, &p.to_vec());
}

/// The `upgradable` byte of the skill packets: the homunculus' level is below the skill's RequiredLevel or the skill
/// is at its tree's maximum → 0.
fn upgradable(hd: &HomData, id: u16, lv: u8) -> u8 {
    let class_ = hd.homunculus.class_ as i32;
    if (hd.homunculus.level as i32) < super::hom_skill_get_min_level(class_, id) as i32 || lv as i32 >= super::hom_skill_tree_get_max(id, class_) {
        0
    } else {
        1
    }
}

/// clif.cpp clif_homskillinfoblock (ZC_HOSKILLINFO_LIST): every known homunculus skill to the master.
pub fn clif_homskillinfoblock(c: &Ctx, hd_id: u32) {
    let Some(hd) = data::get(c, hd_id) else { return };
    if data::master_sd(c, &hd).is_none() {
        return;
    }
    let Some(hu) = unit::get(c, hd_id) else { return };
    let mut p = PACKET_ZC_HOSKILLINFO_LIST { packetType: PACKET_ZC_HOSKILLINFO_LIST::ID as i16, packetLength: 0, skills: Vec::new() };
    for i in 0..crate::int_homun::MAX_HOMUNSKILL {
        let id = hd.homunculus.hskill[i].id;
        if id == 0 {
            continue;
        }
        let combo = hd.homunculus.hskill[i].flag & crate::status::defs::SKILL_FLAG_TMP_COMBO;
        let idx = super::hom_skill_get_index(id);
        if idx == -1 {
            continue;
        }
        let lv = hd.homunculus.hskill[idx as usize].lv;
        p.skills.push(PACKET_ZC_HOSKILLINFO_LIST_sub {
            id,
            inf: if combo != 0 { crate::db::skills::INF_SELF_SKILL } else { crate::db::skills::skill_get_inf(id) as u16 },
            unknown: 0,
            level: lv as u16,
            sp: crate::db::skills::skill_get_sp(id, lv as u16) as u16,
            range: crate::skill::misc::skill_get_range2(c, &hu, id, lv as u16, false) as u16,
            name: crate::db::skills::skill_get_name(id).chars().take(crate::rules::NAME_MAX).collect(),
            upgradable: upgradable(&hd, id, lv),
        });
    }
    send_self(c, hd.master, &p.to_vec());
}

/// clif.cpp clif_homskillup (ZC_HOSKILLINFO_UPDATE): one skill's new level to the master.
pub fn clif_homskillup(c: &Ctx, hd_id: u32, skill_id: u16) {
    let idx = super::hom_skill_get_index(skill_id);
    if idx == -1 {
        return;
    }
    let Some(hd) = data::get(c, hd_id) else { return };
    if data::master_sd(c, &hd).is_none() {
        return;
    }
    let Some(hu) = unit::get(c, hd_id) else { return };
    let lv = hd.homunculus.hskill[idx as usize].lv;
    let p = PACKET_ZC_HOSKILLINFO_UPDATE {
        packetType: PACKET_ZC_HOSKILLINFO_UPDATE::ID as i16,
        skill_id,
        Level: lv as i16,
        SP: crate::db::skills::skill_get_sp(skill_id, lv as u16) as i16,
        AttackRange: crate::skill::misc::skill_get_range2(c, &hu, skill_id, lv as u16, false) as i16,
        upgradable: upgradable(&hd, skill_id, lv),
    };
    send_self(c, hd.master, &p.to_vec());
}

/// clif.cpp clif_hom_food (ZC_FEED_MER): the result of feeding the homunculus.
pub fn clif_hom_food(c: &Ctx, aid: u32, foodid: i32, success: bool) {
    let p = PACKET_ZC_FEED_MER { packetType: PACKET_ZC_FEED_MER::ID as i16, result: success as u8, itemId: crate::db::items::client_nameid(foodid as u32) };
    send_self(c, aid, &p.to_vec());
}

// ---------------------------------------------------------------------------------------------- requests

fn field_u8(pkt: &[u8], cmd: u16, n: usize) -> Option<u8> {
    let o = *packet_pos(cmd).get(n)? as usize;
    pkt.get(o).copied()
}

/// The homunculus of `aid` when it is active and its block id is `id` (`hom_is_active(sd->hd) && sd->hd->id == id`;
/// mercenary/clif.rs' HomMoveToMaster / HomMoveTo / HomAttack).
pub fn active_hd(c: &Ctx, aid: u32, id: u32) -> Option<HomData> {
    data::of_master(c, aid).filter(|hd| data::hom_is_active(Some(hd)) && hd.id == id)
}

/// The homunculus request handlers (net/mod.rs dispatches here by clif_parse_* name); false when `func` is not one of
/// them. The handlers shared with the mercenary (HomMoveToMaster / HomMoveTo / HomAttack) are mercenary/clif.rs'.
pub fn parse(c: &Ctx, aid: u32, func: &str, pkt: &[u8]) -> bool {
    if pkt.len() < 2 {
        return false;
    }
    let cmd = u16::from_le_bytes([pkt[0], pkt[1]]);
    match func {
        // clif_parse_ChangeHomunculusName (CZ_RENAME_MER): the 24-byte name field
        "clif_parse_ChangeHomunculusName" => {
            let o = packet_pos(cmd).first().copied().unwrap_or(2) as usize;
            let raw = pkt.get(o..(o + crate::mob::NAME_LENGTH).min(pkt.len())).unwrap_or(&[]);
            super::hom_change_name(c, aid, raw);
        }
        // clif_parse_HomMoveToMaster / HomMoveTo / HomAttack (CZ_REQUEST_MOVETOOWNER / MOVENPC / ACTNPC) serve the
        // mercenary and the homunculus in one handler each: mercenary/clif.rs (net/mod.rs tries it after this one)
        // clif_parse_HomMenu (CZ_COMMAND_MER)
        "clif_parse_HomMenu" => {
            if !data::is_active_of(c, aid) {
                return true;
            }
            // TODO: shuffle packet
            if let Some(v) = field_u8(pkt, cmd, 1) {
                super::hom_menu(c, aid, v as i32);
            }
        }
        _ => return false,
    }
    true
}

/// clif_parse_configuration's CONFIG_HOMUNCULUS_AUTOFEED arm: false when the request is ignored ("Player can not
/// click this if he does not have a homunculus or it is vaporized").
pub fn config_autofeed(c: &Ctx, aid: u32, flag: bool) -> bool {
    let Some(mut hd) = data::of_master(c, aid) else { return false };
    if hd.homunculus.vaporize != 0 || !super::feature_homunculus_autofeed() {
        return false;
    }
    hd.homunculus.autofeed = flag;
    data::put(c, &hd);
    true
}
