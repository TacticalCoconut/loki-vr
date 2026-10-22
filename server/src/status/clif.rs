//! The status packets of clif.cpp: clif_updatestatus (ZC_PAR_CHANGE 0x00b0, ZC_LONGPAR_CHANGE 0x00b1,
//! ZC_LONGLONGPAR_CHANGE 0x0acb, ZC_STATUS_CHANGE 0x00be, ZC_COUPLESTATUS 0x0141, ZC_ATTACK_RANGE 0x013a,
//! ZC_NOTIFY_CARTITEM_COUNTINFO 0x0121), clif_initialstatus (ZC_STATUS 0x00bd), clif_statusupack (0x00bc),
//! clif_misceffect (0x019b), clif_status_change (0x0983 / 0x0196), clif_heal (ZC_RECOVERY 0x0a27), clif_displayexp
//! (0x0acc), clif_weight_limit (0x0ade), clif_changemanner (0x01ab), clif_update_hp / clif_hpmeter_single (0x080e),
//! clif_resurrection (0x0148), clif_manner_message (0x014a) and clif_changelook for LOOK_CLOTHES_COLOR.

use super::consts::*;
use super::data;
use super::defs::*;
use crate::clif::{self as cl, Target};
use crate::net::packets::*;
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// clif.cpp client_tick.
fn client_tick(tick: i64) -> u32 {
    tick as u32
}

/// clif.cpp client_exp (PACKETVER >= 20170830).
fn client_exp(exp: u64) -> i64 {
    exp.min(MAX_EXP) as i64
}

fn self_unit(c: &Ctx, aid: u32) -> Option<Unit> {
    unit::get(c, aid)
}

/// clif_par_change: ZC_PAR_CHANGE.
fn par_change(c: &Ctx, u: &Unit, var_id: u16, count: i32) {
    let p = PACKET_ZC_PAR_CHANGE { PacketType: PACKET_ZC_PAR_CHANGE::ID as i16, varID: var_id, count };
    cl::send(c, &p.to_vec(), u, Target::SelfOnly);
}

/// clif_longpar_change: ZC_LONGPAR_CHANGE.
fn longpar_change(c: &Ctx, u: &Unit, var_id: u16, amount: i32) {
    let p = PACKET_ZC_LONGPAR_CHANGE { PacketType: PACKET_ZC_LONGPAR_CHANGE::ID as i16, varID: var_id, amount };
    cl::send(c, &p.to_vec(), u, Target::SelfOnly);
}

/// clif_longlongpar_change: ZC_LONGLONGPAR_CHANGE.
fn longlongpar_change(c: &Ctx, u: &Unit, var_id: u16, amount: i64) {
    let p = PACKET_ZC_LONGLONGPAR_CHANGE { PacketType: PACKET_ZC_LONGLONGPAR_CHANGE::ID as i16, varID: var_id, amount };
    cl::send(c, &p.to_vec(), u, Target::SelfOnly);
}

/// clif_zc_status_change: ZC_STATUS_CHANGE.
fn zc_status_change(c: &Ctx, u: &Unit, status_id: u16, value: u8) {
    let p = PACKET_ZC_STATUS_CHANGE { PacketType: PACKET_ZC_STATUS_CHANGE::ID as i16, statusID: status_id, value };
    cl::send(c, &p.to_vec(), u, Target::SelfOnly);
}

/// clif_attackrange: ZC_ATTACK_RANGE.
fn attackrange(c: &Ctx, u: &Unit, range: i16) {
    let p = PACKET_ZC_ATTACK_RANGE { PacketType: PACKET_ZC_ATTACK_RANGE::ID as i16, currentAttRange: range };
    cl::send(c, &p.to_vec(), u, Target::SelfOnly);
}

/// clif_couplestatus: ZC_COUPLESTATUS.
fn couplestatus(c: &Ctx, u: &Unit, status_type: u32, default_status: i32, plus_status: i32) {
    let p = PACKET_ZC_COUPLESTATUS { PacketType: PACKET_ZC_COUPLESTATUS::ID as i16, statusType: status_type, defaultStatus: default_status, plusStatus: plus_status };
    cl::send(c, &p.to_vec(), u, Target::SelfOnly);
}

/// pc.hpp pc_leftside_atk (RENEWAL).
pub fn pc_leftside_atk(sd: &data::PcStatus) -> i32 {
    sd.battle_status.batk
}
/// pc.hpp pc_rightside_atk (RENEWAL).
pub fn pc_rightside_atk(sd: &data::PcStatus) -> i32 {
    sd.battle_status.watk as i32 + sd.battle_status.watk2 as i32 + sd.battle_status.eatk as i32
}
/// pc.hpp pc_leftside_def (RENEWAL).
pub fn pc_leftside_def(sd: &data::PcStatus) -> i32 {
    sd.battle_status.def2 as i32
}
/// pc.hpp pc_rightside_def (RENEWAL).
pub fn pc_rightside_def(sd: &data::PcStatus) -> i32 {
    sd.battle_status.def as i32
}
/// pc.hpp pc_leftside_mdef (RENEWAL).
pub fn pc_leftside_mdef(sd: &data::PcStatus) -> i32 {
    sd.battle_status.mdef2 as i32
}
/// pc.hpp pc_rightside_mdef (RENEWAL).
pub fn pc_rightside_mdef(sd: &data::PcStatus) -> i32 {
    sd.battle_status.mdef as i32
}
/// pc.hpp pc_leftside_matk (RENEWAL): status_base_matk_min of the battle status at the base level.
pub fn pc_leftside_matk(sd: &data::PcStatus, base_level: i32) -> i32 {
    super::calc::status_base_matk_pc(&sd.battle_status, base_level) as i32
}
/// pc.hpp pc_rightside_matk (RENEWAL).
pub fn pc_rightside_matk(c: &Ctx, sd: &data::PcStatus) -> i32 {
    let sc = super::sc::load(c, sd.account_id);
    let pseudo = match (crate::pc::character_of(c, sd.account_id), unit::get(c, sd.account_id)) {
        (Some(ch), Some(u)) => super::calc_sc::status_calc_pseudobuff_matk(&super::calc::who(c, sd, &ch, &u), &sc, 0) as i32,
        _ => 0,
    };
    sd.battle_status.rhw.matk as i32 + sd.battle_status.lhw.matk as i32 + sd.bonus.ematk + super::calc_sc::status_calc_consumablematk(&sc, 0) as i32 + pseudo
}

/// clif.cpp clif_updatestatus: sends one SP_* value to the player.
pub fn updatestatus(c: &Ctx, aid: u32, type_: i32) {
    let Some(u) = self_unit(c, aid) else { return };
    let Some(sd) = data::sd_get(c, aid) else { return };
    let Some(ch) = crate::pc::character_of(c, aid) else { return };
    let t = type_ as u16;
    let bs = &sd.battle_status;
    match type_ {
        // sd->weight / sd->max_weight: the items system's pc_items row
        SP_WEIGHT => {
            super::calc::pc_updateweightstatus(c, aid);
            let w = crate::inventory::st(c, aid).map(|s| s.weight).unwrap_or(0);
            par_change(c, &u, t, w as i32);
        }
        SP_MAXWEIGHT => par_change(c, &u, t, crate::inventory::st(c, aid).map(|s| s.max_weight).unwrap_or(0) as i32),
        SP_SPEED => par_change(c, &u, t, unit::pc_effective_speed(bs.speed, &u) as i32),
        SP_BASELEVEL => par_change(c, &u, t, ch.base_level as i32),
        SP_JOBLEVEL => par_change(c, &u, t, ch.job_level as i32),
        // Adding this back, I wonder if the client intercepts this - [Lance]
        SP_KARMA => par_change(c, &u, t, ch.karma as u8 as i32), // unsigned char karma (mmo.hpp:578)
        SP_MANNER => par_change(c, &u, t, ch.manner as i32),
        SP_STATUSPOINT => par_change(c, &u, t, ch.status_point as i32),
        SP_SKILLPOINT => par_change(c, &u, t, ch.skill_point as i32),
        SP_HIT => par_change(c, &u, t, bs.hit as i32),
        SP_FLEE1 => par_change(c, &u, t, bs.flee as i32),
        SP_FLEE2 => par_change(c, &u, t, bs.flee2 as i32 / 10),
        SP_MAXHP => par_change(c, &u, t, bs.max_hp as i32),
        SP_MAXSP => par_change(c, &u, t, bs.max_sp as i32),
        SP_HP => {
            // On officials the HP never go below 1, even if you die [Lemongrass]
            // On officials the HP Novice class never go below 50%, even if you die [Napster]
            if bs.hp == 0 {
                let v = if (sd.class_ & crate::db::jobs::MAPID_SECONDMASK) != crate::db::jobs::MAPID_NOVICE { 1 } else { bs.max_hp / 2 };
                par_change(c, &u, t, v as i32);
            } else {
                par_change(c, &u, t, bs.hp as i32);
            }
        }
        SP_SP => par_change(c, &u, t, bs.sp as i32),
        SP_ASPD => par_change(c, &u, t, bs.amotion as i32),
        SP_ATK1 => par_change(c, &u, t, pc_leftside_atk(&sd)),
        SP_DEF1 => par_change(c, &u, t, pc_leftside_def(&sd)),
        SP_MDEF1 => par_change(c, &u, t, pc_leftside_mdef(&sd)),
        SP_ATK2 => par_change(c, &u, t, pc_rightside_atk(&sd)),
        SP_DEF2 => par_change(c, &u, t, pc_rightside_def(&sd)),
        // (RENEWAL: no negative check)
        SP_MDEF2 => par_change(c, &u, t, pc_rightside_mdef(&sd)),
        SP_CRITICAL => par_change(c, &u, t, bs.cri as i32 / 10),
        SP_MATK1 => par_change(c, &u, t, pc_rightside_matk(c, &sd)),
        SP_MATK2 => par_change(c, &u, t, pc_leftside_matk(&sd, ch.base_level as i32)),
        SP_ZENY => longpar_change(c, &u, t, ch.zeny as i32),
        // PACKETVER >= 20170830
        SP_BASEEXP => longlongpar_change(c, &u, t, client_exp(ch.base_exp)),
        SP_JOBEXP => longlongpar_change(c, &u, t, client_exp(ch.job_exp)),
        SP_NEXTBASEEXP => longlongpar_change(c, &u, t, client_exp(crate::db::jobs::pc_nextbaseexp(ch.class, ch.base_level as u32))),
        SP_NEXTJOBEXP => longlongpar_change(c, &u, t, client_exp(crate::db::jobs::pc_nextjobexp(ch.class, ch.job_level as u32))),
        // SP_U<STAT> are used to update the amount of points necessary to increase that stat
        SP_USTR | SP_UAGI | SP_UVIT | SP_UINT | SP_UDEX | SP_ULUK => {
            let need = super::progress::pc_need_status_point(c, aid, type_ - SP_USTR + SP_STR, 1);
            zc_status_change(c, &u, t, need as u8);
        }
        SP_ATTACKRANGE => attackrange(c, &u, bs.rhw.range as i16),
        SP_STR => couplestatus(c, &u, t as u32, ch.str as i32, bs.str as u16 as i32 - ch.str as i32),
        SP_AGI => couplestatus(c, &u, t as u32, ch.agi as i32, bs.agi as u16 as i32 - ch.agi as i32),
        SP_VIT => couplestatus(c, &u, t as u32, ch.vit as i32, bs.vit as u16 as i32 - ch.vit as i32),
        SP_INT => couplestatus(c, &u, t as u32, ch.int as i32, bs.int_ as u16 as i32 - ch.int as i32),
        SP_DEX => couplestatus(c, &u, t as u32, ch.dex as i32, bs.dex as u16 as i32 - ch.dex as i32),
        SP_LUK => couplestatus(c, &u, t as u32, ch.luk as i32, bs.luk as u16 as i32 - ch.luk as i32),
        SP_CARTINFO => crate::clif_items::cartcount(c, aid),
        // PACKETVER_RE_NUM >= 20200724
        SP_AP => par_change(c, &u, SP_AP as u16, bs.ap as i32),
        SP_TRAITPOINT => par_change(c, &u, SP_TRAITPOINT as u16, ch.trait_point as i32),
        SP_MAXAP => par_change(c, &u, SP_MAXAP as u16, bs.max_ap as i32),
        SP_POW => couplestatus(c, &u, SP_POW as u32, ch.pow as i32, bs.pow as u16 as i32 - ch.pow as i32),
        SP_STA => couplestatus(c, &u, SP_STA as u32, ch.sta as i32, bs.sta as u16 as i32 - ch.sta as i32),
        SP_WIS => couplestatus(c, &u, SP_WIS as u32, ch.wis as i32, bs.wis as u16 as i32 - ch.wis as i32),
        SP_SPL => couplestatus(c, &u, SP_SPL as u32, ch.spl as i32, bs.spl as u16 as i32 - ch.spl as i32),
        SP_CON => couplestatus(c, &u, SP_CON as u32, ch.con as i32, bs.con as u16 as i32 - ch.con as i32),
        SP_CRT => couplestatus(c, &u, SP_CRT as u32, ch.crt as i32, bs.crt as u16 as i32 - ch.crt as i32),
        SP_UPOW | SP_USTA | SP_UWIS | SP_USPL | SP_UCON | SP_UCRT => {
            let need = super::progress::pc_need_trait_point(c, aid, type_ - SP_UPOW + SP_POW, 1);
            zc_status_change(c, &u, t, need as u8);
        }
        SP_PATK => par_change(c, &u, SP_PATK as u16, bs.patk as i32),
        SP_SMATK => par_change(c, &u, SP_SMATK as u16, bs.smatk as i32),
        SP_RES => par_change(c, &u, SP_RES as u16, bs.res as i32),
        SP_MRES => par_change(c, &u, SP_MRES as u16, bs.mres as i32),
        SP_HPLUS => par_change(c, &u, SP_HPLUS as u16, bs.hplus as i32),
        SP_CRATE => par_change(c, &u, SP_CRATE as u16, bs.crate_ as i32),
        _ => {
            log::error!("clif_updatestatus : unrecognized type {type_}");
            return;
        }
    }
    // Additional update packets that should be sent right after
    match type_ {
        SP_BASELEVEL => crate::party::updatestatus_baselevel(c, &u),
        SP_HP => update_hp(c, aid),
        _ => {}
    }
}

/// clif.cpp clif_update_hp: the HP bar for GMs that may see it (hpmeter), the party, the battleground team.
pub fn update_hp(c: &Ctx, aid: u32) {
    let Some(u) = self_unit(c, aid) else { return };
    // clif_hpmeter searches around sd->m / x / y whether or not the player is in the block list yet (a player still
    // loading its map is seen by the GMs around its cell, as on the oracle)
    {
        // (mapdata->hpmeter_visible counts players with PC_PERM_VIEW_HPMETER on the map; clif_hpmeter sends to exactly
        // those in the area, so the count only short-cuts the search)
        let Some(sd) = data::sd_get(c, aid) else { return };
        let r = crate::conf::area_size();
        for t in crate::map::units_in_area(c, u.map, u.x - r, u.y - r, u.x + r, u.y + r, BL_PC) {
            if crate::pc::has_permission(c, t.id, crate::rules::PC_PERM_VIEW_HPMETER) {
                hpmeter_single(c, &t, aid, sd.battle_status.hp, sd.battle_status.max_hp);
            }
        }
    }
    // party_hp_mode 0 and a party: clif_party_hp
    crate::party::update_hp(c, &u);
    crate::clif_bg::update_hp(c, aid); // if (sd.bg_id) clif_bg_hp(&sd)
}

/// clif.cpp clif_hpmeter_single: ZC_NOTIFY_HP_TO_GROUPM to `to`.
pub fn hpmeter_single(c: &Ctx, to: &Unit, id: u32, hp: u32, maxhp: u32) {
    let p = PACKET_ZC_NOTIFY_HP_TO_GROUPM { PacketType: PACKET_ZC_NOTIFY_HP_TO_GROUPM::ID as i16, AID: id, hp: hp as _, maxhp: maxhp as _ };
    cl::send(c, &p.to_vec(), to, Target::SelfOnly);
}

/// clif.cpp clif_changemanner: ZC_PAR_CHANGE_USER (SP_MANNER) to the area without the player.
pub fn changemanner(c: &Ctx, aid: u32) {
    let Some(u) = self_unit(c, aid) else { return };
    let Some(ch) = crate::pc::character_of(c, aid) else { return };
    let p = PACKET_ZC_PAR_CHANGE_USER { packetType: PACKET_ZC_PAR_CHANGE_USER::ID as i16, gid: u.id, r#type: SP_MANNER as _, value: ch.manner as _ };
    cl::send(c, &p.to_vec(), &u, Target::AreaWos);
}

/// clif.cpp clif_initialstatus: ZC_STATUS, then the stat / range / ASPD / 4th-job updates.
pub fn initialstatus(c: &Ctx, aid: u32) {
    let Some(u) = self_unit(c, aid) else { return };
    let Some(sd) = data::sd_get(c, aid) else { return };
    let Some(ch) = crate::pc::character_of(c, aid) else { return };
    let need = |t| super::progress::pc_need_status_point(c, aid, t, 1) as u8;
    let p = PACKET_ZC_STATUS {
        packetType: PACKET_ZC_STATUS::ID as i16,
        point: (ch.status_point as u32).min(i16::MAX as u32) as u16,
        str: ch.str.min(u8::MAX as u16) as u8,
        standardStr: need(SP_STR),
        agi: ch.agi.min(u8::MAX as u16) as u8,
        standardAgi: need(SP_AGI),
        vit: ch.vit.min(u8::MAX as u16) as u8,
        standardVit: need(SP_VIT),
        int_: ch.int.min(u8::MAX as u16) as u8,
        standardInt: need(SP_INT),
        dex: ch.dex.min(u8::MAX as u16) as u8,
        standardDex: need(SP_DEX),
        luk: ch.luk.min(u8::MAX as u16) as u8,
        standardLuk: need(SP_LUK),
        attPower: pc_leftside_atk(&sd) as i16,
        refiningPower: pc_rightside_atk(&sd) as i16,
        max_mattPower: pc_rightside_matk(c, &sd) as i16,
        min_mattPower: pc_leftside_matk(&sd, ch.base_level as i32) as i16,
        itemdefPower: pc_leftside_def(&sd) as i16,
        plusdefPower: pc_rightside_def(&sd) as i16,
        mdefPower: pc_leftside_mdef(&sd) as i16,
        // RENEWAL
        plusmdefPower: pc_rightside_mdef(&sd) as i16,
        hitSuccessValue: sd.battle_status.hit,
        avoidSuccessValue: sd.battle_status.flee,
        plusAvoidSuccessValue: sd.battle_status.flee2 / 10,
        criticalSuccessValue: sd.battle_status.cri / 10,
        ASPD: sd.battle_status.amotion as i16,
        plusASPD: 0,
    };
    cl::send(c, &p.to_vec(), &u, Target::SelfOnly);
    for t in [SP_STR, SP_AGI, SP_VIT, SP_INT, SP_DEX, SP_LUK, SP_ATTACKRANGE, SP_ASPD] {
        updatestatus(c, aid, t);
    }
    // RENEWAL
    for t in [
        SP_POW, SP_STA, SP_WIS, SP_SPL, SP_CON, SP_CRT, SP_PATK, SP_SMATK, SP_RES, SP_MRES, SP_HPLUS, SP_CRATE, SP_TRAITPOINT, SP_AP, SP_MAXAP, SP_UPOW, SP_USTA,
        SP_UWIS, SP_USPL, SP_UCON, SP_UCRT,
    ] {
        updatestatus(c, aid, t);
    }
}

/// clif.cpp clif_statusupack: ZC_STATUS_CHANGE_ACK.
pub fn statusupack(c: &Ctx, aid: u32, type_: i32, success: bool, val: i32) {
    let Some(u) = self_unit(c, aid) else { return };
    let p = PACKET_ZC_STATUS_CHANGE_ACK { packetType: PACKET_ZC_STATUS_CHANGE_ACK::ID as i16, sp: type_ as _, ok: success as _, value: cap_value(val, 0, u8::MAX as i32) as _ };
    cl::send(c, &p.to_vec(), &u, Target::SelfOnly);
}

/// clif.cpp clif_misceffect: ZC_NOTIFY_EFFECT to the area.
pub fn misceffect(c: &Ctx, bl: &Unit, type_: i32) {
    let p = PACKET_ZC_NOTIFY_EFFECT { packetType: PACKET_ZC_NOTIFY_EFFECT::ID as i16, aid: bl.id, effectId: type_ as u32 };
    cl::send(c, &p.to_vec(), bl, Target::Area);
}

/// clif.cpp clif_status_change_sub: ZC_MSG_STATE_CHANGE3 (0x0983, on with display_status_timers) or
/// ZC_MSG_STATE_CHANGE (0x0196, off).
#[allow(clippy::too_many_arguments)]
pub fn status_change_sub(c: &Ctx, bl: &Unit, id: u32, type_: i32, flag: i32, mut tick: i64, val1: i32, val2: i32, val3: i32, target: Target) {
    if type_ == EFST_BLANK {
        // It shows nothing on the client...
        return;
    }
    if type_ == EFST_POSTDELAY && tick == 0 {
        return;
    }
    // Statuses with an infinite duration, but still needs a duration sent to display properly.
    if type_ == EFST_LUNARSTANCE || type_ == EFST_UNIVERSESTANCE || type_ == EFST_SUNSTANCE || type_ == EFST_STARSTANCE {
        tick = 200;
    }
    let mut w = Vec::with_capacity(29);
    if flag != 0 && crate::conf::display_status_timers() {
        if tick <= 0 {
            tick = 9999; // this is indeed what official servers do
        }
        w.extend_from_slice(&0x0983u16.to_le_bytes());
        w.extend_from_slice(&(type_ as u16).to_le_bytes());
        w.extend_from_slice(&id.to_le_bytes());
        w.push(flag as u8);
        // at this stage remain and total are the same value I believe
        w.extend_from_slice(&client_tick(tick).to_le_bytes());
        w.extend_from_slice(&client_tick(tick).to_le_bytes());
        w.extend_from_slice(&val1.to_le_bytes());
        w.extend_from_slice(&val2.to_le_bytes());
        w.extend_from_slice(&val3.to_le_bytes());
    } else {
        w.extend_from_slice(&0x0196u16.to_le_bytes());
        w.extend_from_slice(&(type_ as u16).to_le_bytes());
        w.extend_from_slice(&id.to_le_bytes());
        w.push(flag as u8);
    }
    cl::send(c, &w, bl, target);
}

/// clif.cpp clif_status_change: the status icon of `bl` to the clients around it (only itself while GM-invisible;
/// without itself for non-players).
#[allow(clippy::too_many_arguments)]
pub fn status_change(c: &Ctx, bl: &Unit, type_: i32, flag: i32, tick: i64, val1: i32, val2: i32, val3: i32) {
    if type_ == EFST_BLANK {
        // It shows nothing on the client...
        return;
    }
    if type_ == EFST_POSTDELAY && tick == 0 {
        return;
    }
    if type_ == EFST_ILLUSION && !crate::conf::display_hallucination() {
        // Disable Hallucination.
        return;
    }
    // (PACKETVER_RE_NUM >= 20191106: EFST_MADOGEAR_TYPE stays)
    // Check if current bl type is in the returned bitmask and only send status changes that actually matter to the client
    if crate::db::misc::status::status_db().status_efst_get_bl_type(type_) & bl.kind == 0 {
        return;
    }
    let target = if bl.kind == BL_PC {
        if bl.sc.option & OPTION_INVISIBLE != 0 {
            Target::SelfOnly
        } else {
            Target::Area
        }
    } else {
        Target::AreaWos
    };
    status_change_sub(c, bl, bl.id, type_, flag, tick, val1, val2, val3, target);
}

/// clif.cpp clif_efst_status_change: ZC_EFST_SET_ENTER (0x0984) about `tbl` (the unit with the status), sent
/// relative to `bl` (PACKETVER >= 20111108; clif_efst_status_change_sub uses it for PACKETVER > 20120418).
#[allow(clippy::too_many_arguments)]
pub fn efst_status_change(c: &Ctx, bl: &Unit, tbl: &Unit, target: Target, type_: i32, mut tick: i64, val1: i32, val2: i32, val3: i32) {
    if type_ == EFST_BLANK {
        return;
    }
    if tick <= 0 {
        tick = 9999;
    }
    let duration = client_tick(tick);
    let p = PACKET_ZC_EFST_SET_ENTER { packetType: PACKET_ZC_EFST_SET_ENTER::ID, targetID: tbl.id, r#type: type_ as _, duration: duration as _, duration2: duration as _, val1, val2, val3 };
    cl::send(c, &p.to_vec(), bl, target);
}

/// clif.cpp clif_efst_status_change_sub: the displayed status changes (sd->sc_display / nd->sc_display, status_display_add)
/// of `bl` (a player or an NPC coming into view), sent relative to `tbl`.
pub fn efst_status_change_sub(c: &Ctx, tbl: &Unit, bl: &Unit, target: Target) {
    let spheres_sent = match bl.kind {
        BL_PC => !crate::pc::get(c, bl.id).map(|p| p.connect_new).unwrap_or(false),
        BL_NPC => true,
        _ => return,
    };
    // `target` is the C++ parameter: once a sphere switches it to AREA_WOS it stays so for the entries after it.
    let mut target = target;
    for d in super::sc::unit_sc(c, bl.id).display {
        let t = d.sc_type;
        let sce = super::sc::get_sce(c, bl.id, t);
        let mut tick: i64 = 0;
        if let Some(due) = sce.as_ref().filter(|e| e.timer != 0).and_then(|e| crate::timer::due(c, e.timer)) {
            tick = due - c.tick();
        }
        // Status changes that need special handling
        match t {
            SC_SPHERE_1 | SC_SPHERE_2 | SC_SPHERE_3 | SC_SPHERE_4 | SC_SPHERE_5 => {
                if spheres_sent {
                    target = Target::AreaWos;
                }
            }
            SC_HELLS_PLANT => {
                if let Some(e) = &sce {
                    tick = e.val4 as i64;
                }
            }
            _ => {}
        }
        let icon = crate::db::misc::status::status_db().get_icon(t);
        efst_status_change(c, tbl, bl, target, icon, tick, d.val1, d.val2, d.val3);
    }
}

/// clif.cpp clif_heal: ZC_RECOVERY.
pub fn heal(c: &Ctx, aid: u32, type_: i32, val: u32) {
    let Some(u) = self_unit(c, aid) else { return };
    let p = PACKET_ZC_RECOVERY { packetType: PACKET_ZC_RECOVERY::ID as i16, r#type: type_ as u16, amount: val.min(i32::MAX as u32) as i32 };
    cl::send(c, &p.to_vec(), &u, Target::SelfOnly);
}

/// clif.cpp clif_resurrection: ZC_RESURRECTION to the area (a disguised player is spawned again).
pub fn resurrection(c: &Ctx, bl: &Unit) {
    let p = PACKET_ZC_RESURRECTION { packetType: PACKET_ZC_RESURRECTION::ID as i16, gid: bl.id, r#type: 0 };
    cl::send(c, &p.to_vec(), bl, Target::Area);
    if crate::atcmd_disguise::disguised(c, bl) {
        cl::spawn(c, bl, false);
    }
}

/// clif.cpp clif_displayexp: ZC_NOTIFY_EXP2 (0x0acc).
pub fn displayexp(c: &Ctx, aid: u32, exp: u64, type_: i32, quest: bool, lost: bool) {
    let Some(u) = self_unit(c, aid) else { return };
    let mut w = Vec::with_capacity(18);
    w.extend_from_slice(&0x0accu16.to_le_bytes());
    w.extend_from_slice(&u.id.to_le_bytes());
    w.extend_from_slice(&(client_exp(exp).wrapping_mul(if lost { -1 } else { 1 })).to_le_bytes());
    w.extend_from_slice(&(type_ as u16).to_le_bytes());
    // NOTE: Somehow JobEXP always in yellow color
    w.extend_from_slice(&((quest && type_ != SP_JOBEXP) as u16).to_le_bytes());
    cl::send(c, &w, &u, Target::SelfOnly);
}

/// clif.cpp clif_weight_limit: 0x0ade with natural_heal_weight_rate.
pub fn weight_limit(c: &Ctx, aid: u32) {
    let Some(u) = self_unit(c, aid) else { return };
    let mut w = Vec::with_capacity(6);
    w.extend_from_slice(&0x0adeu16.to_le_bytes());
    w.extend_from_slice(&(crate::conf::natural_heal_weight_rate() as i32).to_le_bytes());
    cl::send(c, &w, &u, Target::SelfOnly);
}

/// clif.cpp clif_manner_message: ZC_ACK_GIVE_MANNER_POINT (0x014a).
pub fn manner_message(c: &Ctx, aid: u32, type_: u32) {
    let Some(u) = self_unit(c, aid) else { return };
    let mut w = Vec::with_capacity(6);
    w.extend_from_slice(&0x014au16.to_le_bytes());
    w.extend_from_slice(&type_.to_le_bytes());
    cl::send(c, &w, &u, Target::SelfOnly);
}

/// clif.cpp clif_changelook(bl, LOOK_CLOTHES_COLOR, val): the palette (0 while a costume option ignores palettes:
/// the *_ignorepalette settings are off) is stored in the view data and sent to the area (players: pc_equip.rs).
pub fn changelook_clothes_color(c: &Ctx, u: &Unit, val: i32) {
    const LOOK_CLOTHES_COLOR: u8 = 7;
    if u.kind == BL_PC {
        crate::pc_equip::clif_changelook(c, u.id, crate::pc_equip::LOOK_CLOTHES_COLOR, val);
        return;
    }
    let Some(mut u2) = unit::get(c, u.id) else { return };
    u2.vd.clothes_color = val;
    unit::save(c, &u2);
    // prevent leaking the presence of GM-hidden objects
    let target = if u2.sc.option & OPTION_INVISIBLE != 0 { Target::SelfOnly } else { Target::Area };
    cl::sprite_change(c, &u2, u2.id, LOOK_CLOTHES_COLOR, val, 0, target);
}
