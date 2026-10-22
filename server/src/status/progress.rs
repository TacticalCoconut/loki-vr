//! Character progression (pc.cpp): stats (pc_getstat / pc_setstat, pc_need_status_point, pc_maxparameterincrease,
//! pc_statusup / pc_statusup2 and the trait versions, pc_resetstate), experience (pc_gainexp, pc_calcexp,
//! pc_lostexp, pc_checkbaselevelup / pc_checkjoblevelup, pc_baselevelchanged), plus the CZ_STATUS_CHANGE /
//! CZ_ADVANCED_STATUS_CHANGE parsers. pc_skill and pc_calc_skilltree forward to the skill tree (skilltree.rs).

use super::consts::*;
use super::data;
use super::defs::*;
use crate::character::Character;
use crate::db::jobs::{self, *};
use crate::unit::Unit;
use crate::world::Ctx;

fn ch_of(c: &Ctx, aid: u32) -> Option<Character> {
    crate::pc::character_of(c, aid)
}

/// pc.cpp pc_getstat (-1 for other types).
pub fn pc_getstat(ch: &Character, type_: i32) -> i32 {
    match type_ {
        SP_STR => ch.str as i32,
        SP_AGI => ch.agi as i32,
        SP_VIT => ch.vit as i32,
        SP_INT => ch.int as i32,
        SP_DEX => ch.dex as i32,
        SP_LUK => ch.luk as i32,
        SP_POW => ch.pow as i32,
        SP_STA => ch.sta as i32,
        SP_WIS => ch.wis as i32,
        SP_SPL => ch.spl as i32,
        SP_CON => ch.con as i32,
        SP_CRT => ch.crt as i32,
        _ => -1,
    }
}

/// pc.cpp pc_setstat: returns the new value (-1 for other types). The caller stores the character.
pub fn pc_setstat(ch: &mut Character, type_: i32, val: i32) -> i32 {
    let v = val as u16;
    match type_ {
        SP_STR => ch.str = v,
        SP_AGI => ch.agi = v,
        SP_VIT => ch.vit = v,
        SP_INT => ch.int = v,
        SP_DEX => ch.dex = v,
        SP_LUK => ch.luk = v,
        SP_POW => ch.pow = v,
        SP_STA => ch.sta = v,
        SP_WIS => ch.wis = v,
        SP_SPL => ch.spl = v,
        SP_CON => ch.con = v,
        SP_CRT => ch.crt = v,
        _ => return -1,
    }
    val
}

/// pc.cpp pc_maxparameter for the character's class.
pub fn pc_maxparameter(c: &Ctx, aid: u32, ch: &Character, param: usize) -> i32 {
    let class_ = data::sd_get(c, aid).map(|s| s.class_).unwrap_or_else(|| pc_jobid2mapid(ch.class));
    jobs::pc_maxparameter(class_, ch.sex as i32, param) as i32
}

/// pc.cpp PC_STATUS_POINT_COST (RENEWAL_STAT).
fn pc_status_point_cost(low: i32) -> i32 {
    if low < 100 {
        2 + (low - 1) / 10
    } else {
        16 + 4 * ((low - 100) / 5)
    }
}

fn need_status_point(c: &Ctx, aid: u32, ch: &Character, type_: i32, val: i32) -> i32 {
    if val == 0 {
        return 0;
    }
    let mut low = pc_getstat(ch, type_);
    let max = pc_maxparameter(c, aid, ch, (type_ - SP_STR) as usize);
    if low >= max && val > 0 {
        return 0; // Official servers show '0' when max is reached
    }
    let mut high = low + val;
    if val < 0 {
        std::mem::swap(&mut low, &mut high);
    }
    let mut sp = 0;
    while low < high {
        sp += pc_status_point_cost(low);
        low += 1;
    }
    sp
}

/// pc.cpp pc_need_status_point: status points needed to change the stat by `val`.
pub fn pc_need_status_point(c: &Ctx, aid: u32, type_: i32, val: i32) -> i32 {
    match ch_of(c, aid) {
        Some(ch) => need_status_point(c, aid, &ch, type_, val),
        None => 0,
    }
}

/// pc.cpp pc_maxparameterincrease.
fn pc_maxparameterincrease(c: &Ctx, aid: u32, ch: &Character, type_: i32) -> i32 {
    let base = pc_getstat(ch, type_);
    let mut final_val = base;
    let mut status_points = ch.status_point as i32;
    let max_param = pc_maxparameter(c, aid, ch, (type_ - SP_STR) as usize);
    while final_val <= max_param && status_points >= 0 {
        status_points -= pc_status_point_cost(final_val);
        final_val += 1;
    }
    final_val -= 1;
    if final_val > base {
        final_val - base
    } else {
        0
    }
}

/// pc.cpp pc_statusup: raises a stat with status points (CZ_STATUS_CHANGE).
pub fn pc_statusup(c: &Ctx, aid: u32, type_: i32, mut increase: i32) -> bool {
    let Some(mut ch) = ch_of(c, aid) else { return false };
    // check conditions
    if !(SP_STR..=SP_LUK).contains(&type_) || increase <= 0 {
        super::clif::statusupack(c, aid, type_, false, 0);
        return false;
    }
    // check limits
    let current = pc_getstat(&ch, type_);
    let max_increase = pc_maxparameterincrease(c, aid, &ch, type_);
    increase = cap_value(increase, 0, max_increase); // cap to the maximum status points available
    if increase <= 0 || current + increase > pc_maxparameter(c, aid, &ch, (type_ - SP_STR) as usize) {
        super::clif::statusupack(c, aid, type_, false, 0);
        return false;
    }
    // check status points
    let needed_points = need_status_point(c, aid, &ch, type_, increase);
    if needed_points < 0 || needed_points as i64 > ch.status_point as i64 {
        // Sanity check
        super::clif::statusupack(c, aid, type_, false, 0);
        return false;
    }
    // set new values
    let final_value = pc_setstat(&mut ch, type_, current + increase);
    ch.status_point -= needed_points as u32;
    super::put_char(c, &ch);
    super::calc_pc(c, aid, SCO_NONE);
    // update increase cost indicator
    super::clif::updatestatus(c, aid, SP_USTR + type_ - SP_STR);
    // update statpoint count
    super::clif::updatestatus(c, aid, SP_STATUSPOINT);
    // update stat value
    super::clif::statusupack(c, aid, type_, true, final_value);
    if final_value > 255 {
        super::clif::updatestatus(c, aid, type_); // send after the 'ack' to override the truncated value
    }
    crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_GOAL_STATUS, &[final_value]);
    true
}

/// pc.cpp pc_statusup2: changes a stat without spending points (script statusup2).
pub fn pc_statusup2(c: &Ctx, aid: u32, type_: i32, val: i32) -> i32 {
    let Some(mut ch) = ch_of(c, aid) else { return 0 };
    if !(SP_STR..=SP_LUK).contains(&type_) {
        super::clif::statusupack(c, aid, type_, false, 0);
        return 0;
    }
    let need = need_status_point(c, aid, &ch, type_, 1);
    let max = pc_maxparameter(c, aid, &ch, (type_ - SP_STR) as usize); // set new value
    let cur = pc_getstat(&ch, type_);
    let v = pc_setstat(&mut ch, type_, cap_value(cur + val, 1, max));
    super::put_char(c, &ch);
    super::calc_pc(c, aid, SCO_NONE);
    // update increase cost indicator
    if need != pc_need_status_point(c, aid, type_, 1) {
        super::clif::updatestatus(c, aid, SP_USTR + type_ - SP_STR);
    }
    // update stat value
    super::clif::statusupack(c, aid, type_, true, v);
    if v > 255 {
        super::clif::updatestatus(c, aid, type_); // send after the 'ack' to override the truncated value
    }
    v
}

/// pc.cpp pc_need_trait_point.
pub fn pc_need_trait_point(c: &Ctx, aid: u32, type_: i32, val: i32) -> i32 {
    if val == 0 || !(SP_POW..=SP_CRT).contains(&type_) {
        return 0;
    }
    let Some(ch) = ch_of(c, aid) else { return 0 };
    let mut low = pc_getstat(&ch, type_);
    let max = pc_maxparameter(c, aid, &ch, PARAM_POW + (type_ - SP_POW) as usize);
    if low >= max && val > 0 {
        return 0; // Official servers show '0' when max is reached
    }
    let mut high = low + val;
    if val < 0 {
        std::mem::swap(&mut low, &mut high);
    }
    let mut sp = 0;
    while low < high {
        sp += 1;
        low += 1;
    }
    sp
}

/// pc.cpp pc_maxtraitparameterincrease.
fn pc_maxtraitparameterincrease(c: &Ctx, aid: u32, ch: &Character, type_: i32) -> i32 {
    if !(SP_POW..=SP_CRT).contains(&type_) {
        return 0;
    }
    let base = pc_getstat(ch, type_);
    let mut final_val = base;
    let mut trait_points = ch.trait_point as i64;
    let max_param = pc_maxparameter(c, aid, ch, PARAM_POW + (type_ - SP_POW) as usize);
    while final_val <= max_param && trait_points >= 0 {
        trait_points -= 1;
        final_val += 1;
    }
    final_val -= 1;
    if final_val > base {
        final_val - base
    } else {
        0
    }
}

/// pc.cpp pc_traitstatusup (CZ_ADVANCED_STATUS_CHANGE).
pub fn pc_traitstatusup(c: &Ctx, aid: u32, type_: i32, mut increase: i32) -> bool {
    let Some(mut ch) = ch_of(c, aid) else { return false };
    if !(SP_POW..=SP_CRT).contains(&type_) || increase <= 0 {
        super::clif::statusupack(c, aid, type_, false, 0);
        return false;
    }
    let current = pc_getstat(&ch, type_);
    let max_increase = pc_maxtraitparameterincrease(c, aid, &ch, type_);
    increase = cap_value(increase, 0, max_increase);
    if increase <= 0 || current + increase > pc_maxparameter(c, aid, &ch, PARAM_POW + (type_ - SP_POW) as usize) {
        super::clif::statusupack(c, aid, type_, false, 0);
        return false;
    }
    let needed_points = pc_need_trait_point(c, aid, type_, increase);
    if needed_points < 0 || needed_points as i64 > ch.trait_point as i64 {
        super::clif::statusupack(c, aid, type_, false, 0);
        return false;
    }
    let final_value = pc_setstat(&mut ch, type_, current + increase);
    ch.trait_point -= needed_points as u32;
    super::put_char(c, &ch);
    super::calc_pc(c, aid, SCO_NONE);
    super::clif::updatestatus(c, aid, SP_UPOW + type_ - SP_POW);
    super::clif::updatestatus(c, aid, SP_TRAITPOINT);
    super::clif::statusupack(c, aid, type_, true, final_value);
    if final_value > 255 {
        super::clif::updatestatus(c, aid, type_);
    }
    true
}

/// pc.cpp pc_traitstatusup2.
pub fn pc_traitstatusup2(c: &Ctx, aid: u32, type_: i32, val: i32) -> i32 {
    let Some(mut ch) = ch_of(c, aid) else { return 0 };
    if !(SP_POW..=SP_CRT).contains(&type_) {
        super::clif::statusupack(c, aid, type_, false, 0);
        return 0;
    }
    let need = pc_need_trait_point(c, aid, type_, 1);
    let max = pc_maxparameter(c, aid, &ch, PARAM_POW + (type_ - SP_POW) as usize);
    let cur = pc_getstat(&ch, type_);
    let v = pc_setstat(&mut ch, type_, cap_value(cur + val, 0, max));
    super::put_char(c, &ch);
    super::calc_pc(c, aid, SCO_NONE);
    if need != pc_need_trait_point(c, aid, type_, 1) {
        super::clif::updatestatus(c, aid, SP_UPOW + type_ - SP_POW);
    }
    super::clif::statusupack(c, aid, type_, true, v);
    if v > 255 {
        super::clif::updatestatus(c, aid, type_);
    }
    v
}

/// pc.cpp pc_resetstate: every stat back to its start value, the points of the level returned.
pub fn pc_resetstate(c: &Ctx, aid: u32) -> i32 {
    let Some(mut ch) = ch_of(c, aid) else { return 0 };
    let Some(sd) = data::sd_get(c, aid) else { return 0 };
    let maxlv = pc_maxbaselv(ch.class);
    if ch.base_level as u32 > maxlv {
        log::error!("pc_resetstate: Capping the Level to {maxlv} to reset the stats of {}:{}, the base level ({}) is greater than the max level supported.", ch.account_id, ch.id, ch.base_level);
        ch.base_level = maxlv as u16;
        super::put_char(c, &ch);
        super::clif::updatestatus(c, aid, SP_BASELEVEL);
    }
    let mut status_point = statpoint_db().get_table_point(ch.base_level);
    let mut trait_point = statpoint_db().get_trait_table_point(ch.base_level);
    if sd.class_ & JOBL_UPPER != 0 || pc_is_primary_fourth(sd.class_) {
        status_point += crate::conf::transcendent_status_points();
    }
    if pc_is_trait_job(sd.class_) {
        trait_point += crate::conf::trait_points_job_change();
    }
    ch.status_point = status_point;
    ch.trait_point = trait_point;
    for t in [SP_STR, SP_AGI, SP_VIT, SP_INT, SP_DEX, SP_LUK] {
        pc_setstat(&mut ch, t, 1);
    }
    for t in [SP_POW, SP_STA, SP_WIS, SP_SPL, SP_CON, SP_CRT] {
        pc_setstat(&mut ch, t, 0);
    }
    super::put_char(c, &ch);
    for t in [
        SP_STR, SP_AGI, SP_VIT, SP_INT, SP_DEX, SP_LUK, SP_POW, SP_STA, SP_WIS, SP_SPL, SP_CON, SP_CRT, SP_USTR, SP_UAGI, SP_UVIT, SP_UINT, SP_UDEX, SP_ULUK, SP_UPOW,
        SP_USTA, SP_UWIS, SP_USPL, SP_UCON, SP_UCRT, SP_STATUSPOINT, SP_TRAITPOINT,
    ] {
        super::clif::updatestatus(c, aid, t);
    }
    // bugreport:2200: the Taekwon mission is dropped (pc_taekwon.rs)
    crate::pc_taekwon::pc_resetstate_mission(c, aid);
    super::calc_pc(c, aid, SCO_NONE);
    1
}

// ---------------------------------------------------------------------------------------------- experience

/// pc.cpp pc_checkbaselevelup.
pub fn pc_checkbaselevelup(c: &Ctx, aid: u32) -> i32 {
    let Some(mut ch) = ch_of(c, aid) else { return 0 };
    let class = ch.class;
    let mut next = pc_nextbaseexp(class, ch.base_level as u32);
    if next == 0 || ch.base_exp < next || pc_is_maxbaselv(class, ch.base_level as u32) {
        return 0;
    }
    let base_level0 = ch.base_level as u32; // `uint32 base_level = sd->status.base_level;`
    loop {
        ch.base_exp -= next;
        // Kyoki pointed out that the max overcarry exp is the exp needed for the previous level -1. [Skotlex]
        if (!crate::conf::multi_level_up() || (crate::conf::multi_level_up_base() > 0 && ch.base_level as u32 >= crate::conf::multi_level_up_base())) && ch.base_exp > next - 1 {
            ch.base_exp = next - 1;
        }
        ch.status_point = ch.status_point.wrapping_add(statpoint_db().pc_gets_status_point(ch.base_level));
        ch.trait_point += statpoint_db().pc_gets_trait_point(ch.base_level);
        ch.base_level += 1;
        if pc_is_maxbaselv(class, ch.base_level as u32) {
            ch.base_exp = ch.base_exp.min(MAX_LEVEL_BASE_EXP);
            break;
        }
        next = pc_nextbaseexp(class, ch.base_level as u32);
        if !(next > 0 && ch.base_exp >= next) {
            break;
        }
    }
    super::put_char(c, &ch);
    // (pet_lv_rate: 0)
    super::clif::updatestatus(c, aid, SP_STATUSPOINT);
    super::clif::updatestatus(c, aid, SP_TRAITPOINT);
    super::clif::updatestatus(c, aid, SP_BASELEVEL);
    super::clif::updatestatus(c, aid, SP_BASEEXP);
    super::clif::updatestatus(c, aid, SP_NEXTBASEEXP);
    super::calc_pc(c, aid, SCO_FORCE);
    super::hp::status_percent_heal(c, aid, 100, 100, 0);
    let class_ = data::sd_get(c, aid).map(|s| s.class_).unwrap_or(0);
    if let Some(u) = crate::unit::get(c, aid) {
        if (class_ & MAPID_SECONDMASK) == MAPID_SUPER_NOVICE {
            for s in crate::db::misc::status::status_db().iter() {
                if s.has_flag(SCF_SUPERNOVICEANGEL) {
                    super::sc::sc_start(c, Some(&u), &u, s.type_, 100, 1, 120000); // All buffs with super novice flag for 2 minutes
                }
            }
            // Reenable steelbody resurrection on dead (rA pc.cpp:8284-8285)
            if crate::battle::state::get(c, aid).snovice_dead_flag {
                crate::battle::state::with(c, aid, |b| b.snovice_dead_flag = false);
            }
        } else if (class_ & MAPID_FIRSTMASK) == MAPID_TAEKWON {
            for s in crate::db::misc::status::status_db().iter() {
                if s.has_flag(SCF_TAEKWONANGEL) {
                    super::sc::sc_start(c, Some(&u), &u, s.type_, 100, 10, 600000);
                }
            }
        }
    }
    if let Some(u) = crate::unit::get(c, aid) {
        super::clif::misceffect(c, &u, NOTIFYEFFECT_BASE_LEVEL_UP);
    }
    crate::script::npc_run::npc_script_event(c, aid, crate::script::npc_run::NPCE_BASELVUP);
    if crate::pc::character_of(c, aid).map(|ch| ch.party_id != 0).unwrap_or(false) {
        crate::party::party_send_levelup(c, aid);
    }
    pc_baselevelchanged(c, aid);
    {
        use crate::db::misc::achievement::{AG_GOAL_LEVEL, AG_GOAL_STATUS};
        let mut base_level = base_level0;
        while ch_of(c, aid).map(|ch| base_level <= ch.base_level as u32).unwrap_or(false) {
            let class = ch_of(c, aid).map(|ch| ch.class as i32).unwrap_or(0);
            crate::achievement::achievement_update_objective(c, Some(aid), AG_GOAL_LEVEL, &[base_level as i32]);
            crate::achievement::achievement_update_objective(c, Some(aid), AG_GOAL_STATUS, &[base_level as i32, class]);
            base_level += 1;
        }
    }
    1
}

/// pc.cpp pc_baselevelchanged: equipment with a maximum level comes off and the quest icons refresh.
pub fn pc_baselevelchanged(c: &Ctx, aid: u32) {
    let Some(ch) = ch_of(c, aid) else { return };
    for i in 0..EQI_MAX {
        let idx = super::calc::equip_index(c, aid)[i];
        if idx < 0 {
            continue;
        }
        let Some(row) = crate::inventory::inv_get(c, aid, idx as usize) else { continue };
        let d = crate::inventory::data_of(&row.item);
        if d.elvmax != 0 && ch.base_level as u32 > d.elvmax as u32 {
            crate::pc_equip::pc_unequipitem(c, aid, idx as i32, 3);
        }
    }
    crate::quest::pc_show_questinfo(c, aid);
}

/// pc.cpp pc_checkjoblevelup.
pub fn pc_checkjoblevelup(c: &Ctx, aid: u32) -> i32 {
    let Some(mut ch) = ch_of(c, aid) else { return 0 };
    let class = ch.class;
    let mut next = pc_nextjobexp(class, ch.job_level as u32);
    if next == 0 || ch.job_exp < next || pc_is_maxjoblv(class, ch.job_level as u32) {
        return 0;
    }
    let job_level0 = ch.job_level as u32; // `uint32 job_level = sd->status.job_level;`
    loop {
        ch.job_exp -= next;
        // Kyoki pointed out that the max overcarry exp is the exp needed for the previous level -1. [Skotlex]
        if (!crate::conf::multi_level_up() || (crate::conf::multi_level_up_job() > 0 && ch.job_level as u32 >= crate::conf::multi_level_up_job())) && ch.job_exp > next - 1 {
            ch.job_exp = next - 1;
        }
        ch.job_level += 1;
        ch.skill_point = ch.skill_point.wrapping_add(1);
        if pc_is_maxjoblv(class, ch.job_level as u32) {
            ch.job_exp = ch.job_exp.min(MAX_LEVEL_JOB_EXP);
            break;
        }
        next = pc_nextjobexp(class, ch.job_level as u32);
        if !(next > 0 && ch.job_exp >= next) {
            break;
        }
    }
    super::put_char(c, &ch);
    super::clif::updatestatus(c, aid, SP_JOBLEVEL);
    super::clif::updatestatus(c, aid, SP_JOBEXP);
    super::clif::updatestatus(c, aid, SP_NEXTJOBEXP);
    super::clif::updatestatus(c, aid, SP_SKILLPOINT);
    super::calc_pc(c, aid, SCO_FORCE);
    if let Some(u) = crate::unit::get(c, aid) {
        super::clif::misceffect(c, &u, NOTIFYEFFECT_JOB_LEVEL_UP);
        let class_ = data::sd_get(c, aid).map(|s| s.class_).unwrap_or(0);
        if crate::skill::checkskill(c, aid, crate::db::skills::SG_DEVIL) != 0 && ((class_ & MAPID_THIRDMASK) == MAPID_STAR_EMPEROR || pc_is_maxjoblv(class, ch.job_level as u32)) {
            super::clif::status_change(c, &u, EFST_DEVIL1, 1, 0, 0, 0, 1); // Permanent blind effect from SG_DEVIL.
        }
    }
    crate::script::npc_run::npc_script_event(c, aid, crate::script::npc_run::NPCE_JOBLVUP);
    let mut job_level = job_level0;
    while ch_of(c, aid).map(|ch| job_level <= ch.job_level as u32).unwrap_or(false) {
        crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_GOAL_LEVEL, &[job_level as i32]);
        job_level += 1;
    }
    crate::quest::pc_show_questinfo(c, aid);
    1
}

/// pc.cpp pc_calcexp: the personal EXP bonuses (bExpAddRace / bExpAddClass of the source, EXP boost statuses).
fn pc_calcexp(c: &Ctx, aid: u32, base_exp: &mut u64, job_exp: &mut u64, src: Option<&Unit>) {
    let Some(sd) = data::sd_get(c, aid) else { return };
    let mut bonus: i32 = 0;
    if let Some(s) = src {
        let st = super::status_get_status_data(c, s);
        let race = st.race as usize;
        let class = st.class_ as usize;
        let ib = &sd.indexed_bonus;
        if let Some(&v) = ib.expaddrace.get(race) {
            bonus += v;
        }
        bonus += ib.expaddrace[RC_ALL as usize];
        if let Some(&v) = ib.expaddclass.get(class) {
            bonus += v;
        }
        bonus += ib.expaddclass[CLASS_ALL as usize];
        if crate::conf::pk_mode() != 0 && super::status_get_lv(c, s) - ch_of(c, aid).map(|ch| ch.base_level as i32).unwrap_or(0) >= 20 {
            bonus += 15; // pk_mode additional exp if monster >20 levels [Valaris]
        }
        // (VIP_ENABLE is off)
    }
    let scl = super::sc::load(c, aid);
    // Give EXPBOOST for quests even if src is nullptr.
    if scl.has(SC_EXPBOOST) {
        bonus += scl.v1(SC_EXPBOOST);
    }
    if scl.has(SC_PERIOD_PLUSEXP_2ND) {
        bonus += scl.v1(SC_PERIOD_PLUSEXP_2ND);
    }
    if *base_exp != 0 {
        let exp = (*base_exp as f64 + (*base_exp as f64 * (bonus as f64 / 100.))) as u64;
        *base_exp = cap_value(exp, 1, MAX_EXP);
    }
    // Give JEXPBOOST for quests even if src is nullptr.
    if scl.has(SC_JEXPBOOST) {
        bonus += scl.v1(SC_JEXPBOOST);
    }
    if scl.has(SC_PERIOD_PLUSEXP_2ND) {
        // Increase Jexp as well
        bonus += scl.v1(SC_PERIOD_PLUSEXP_2ND);
    }
    if *job_exp != 0 {
        let exp = (*job_exp as f64 + (*job_exp as f64 * (bonus as f64 / 100.))) as u64;
        *job_exp = cap_value(exp, 1, MAX_EXP);
    }
}

/// pc.cpp pc_gainexp. exp_flag 1: quest EXP; 2: param EXP (no guild tax, no adjustments). The party share happens
/// before this, in party_exp_share (parties, phase 6).
pub fn pc_gainexp(c: &Ctx, aid: u32, src: Option<&Unit>, mut base_exp: u64, mut job_exp: u64, exp_flag: u8) {
    let Some(u) = crate::unit::get(c, aid) else { return };
    if !u.on_map || crate::pc::isdead(c, aid) {
        return;
    }
    if exp_flag & 2 == 0 {
        if !crate::conf::pvp_exp() && crate::npc::mapflag(c, u.map, crate::npc::MF_PVP) {
            return; // no exp on pvp maps
        }
        if crate::guild::guild_id_of(c, aid) > 0 {
            base_exp -= crate::guild::guild_payexp(c, aid, base_exp);
        }
    }
    let Some(ch) = ch_of(c, aid) else { return };
    let class = ch.class;
    let flag: u8 = (base_exp != 0) as u8 | ((job_exp != 0) as u8) << 1 | (pc_is_maxbaselv(class, ch.base_level as u32) as u8) << 2 | (pc_is_maxjoblv(class, ch.job_level as u32) as u8) << 3;
    if exp_flag & 2 == 0 {
        pc_calcexp(c, aid, &mut base_exp, &mut job_exp, src);
    }
    let nextb = pc_nextbaseexp(class, ch.base_level as u32);
    let nextj = pc_nextjobexp(class, ch.job_level as u32);
    if flag & 4 != 0 {
        if ch.base_exp >= MAX_LEVEL_BASE_EXP {
            base_exp = 0;
        } else if ch.base_exp + base_exp >= MAX_LEVEL_BASE_EXP {
            base_exp = MAX_LEVEL_BASE_EXP - ch.base_exp;
        }
    }
    if flag & 8 != 0 {
        if ch.job_exp >= MAX_LEVEL_JOB_EXP {
            job_exp = 0;
        } else if ch.job_exp + job_exp >= MAX_LEVEL_JOB_EXP {
            job_exp = MAX_LEVEL_JOB_EXP - ch.job_exp;
        }
    }
    if exp_flag & 2 == 0 && crate::conf::max_exp_gain_rate() != 0 && (base_exp != 0 || job_exp != 0) {
        // Note that this value should never be greater than the original therefore no overflow checks are needed.
        let rate = crate::conf::max_exp_gain_rate() as f64 / 1000.;
        if nextb > 0 {
            let nextbp = base_exp as f32 / nextb as f32;
            if nextbp as f64 > rate {
                base_exp = (rate * nextb as f64) as u32 as u64;
            }
        }
        if nextj > 0 {
            let nextjp = job_exp as f32 / nextj as f32;
            if nextjp as f64 > rate {
                job_exp = (rate * nextj as f64) as u32 as u64;
            }
        }
    }
    // Give EXP for Base Level
    if base_exp != 0 {
        if let Some(mut ch) = ch_of(c, aid) {
            ch.base_exp = safe_addition_cap(ch.base_exp as i64, base_exp as i64, MAX_EXP as i64) as u64;
            super::put_char(c, &ch);
        }
        if pc_checkbaselevelup(c, aid) == 0 {
            super::clif::updatestatus(c, aid, SP_BASEEXP);
        }
    }
    // Give EXP for Job Level
    if job_exp != 0 {
        if let Some(mut ch) = ch_of(c, aid) {
            ch.job_exp = safe_addition_cap(ch.job_exp as i64, job_exp as i64, MAX_EXP as i64) as u64;
            super::put_char(c, &ch);
        }
        if pc_checkjoblevelup(c, aid) == 0 {
            super::clif::updatestatus(c, aid, SP_JOBEXP);
        }
    }
    if flag & 1 != 0 {
        super::clif::displayexp(c, aid, if flag & 4 != 0 { 0 } else { base_exp }, SP_BASEEXP, exp_flag & 1 != 0, false);
    }
    if flag & 2 != 0 {
        super::clif::displayexp(c, aid, if flag & 8 != 0 { 0 } else { job_exp }, SP_JOBEXP, exp_flag & 1 != 0, false);
    }
    if crate::atcmd_pc::get(c, aid).showexp && (base_exp != 0 || job_exp != 0) {
        crate::atcmd_pc::pc_gainexp_disp(c, aid, base_exp, nextb, job_exp, nextj, false);
    }
}

/// pc.cpp pc_lostexp.
pub fn pc_lostexp(c: &Ctx, aid: u32, mut base_exp: u64, mut job_exp: u64) {
    let Some(mut ch) = ch_of(c, aid) else { return };
    if base_exp != 0 {
        base_exp = ch.base_exp.min(base_exp);
        ch.base_exp -= base_exp;
        super::put_char(c, &ch);
        super::clif::displayexp(c, aid, base_exp, SP_BASEEXP, false, true);
        super::clif::updatestatus(c, aid, SP_BASEEXP);
    }
    if job_exp != 0 {
        let Some(mut ch) = ch_of(c, aid) else { return };
        job_exp = ch.job_exp.min(job_exp);
        ch.job_exp -= job_exp;
        super::put_char(c, &ch);
        super::clif::displayexp(c, aid, job_exp, SP_JOBEXP, false, true);
        super::clif::updatestatus(c, aid, SP_JOBEXP);
    }
    if crate::atcmd_pc::get(c, aid).showexp && (base_exp != 0 || job_exp != 0) {
        if let Some(ch) = ch_of(c, aid) {
            let (nb, nj) = (pc_nextbaseexp(ch.class, ch.base_level as u32), pc_nextjobexp(ch.class, ch.job_level as u32));
            crate::atcmd_pc::pc_gainexp_disp(c, aid, base_exp, nb, job_exp, nj, true);
        }
    }
}

// ---------------------------------------------------------------------------------------------- skills

/// pc.cpp pc_calc_skilltree: ported in full by the skill tree (skilltree.rs; the item-granted levels are part of it).
pub fn pc_calc_skilltree(c: &Ctx, aid: u32) {
    crate::skilltree::pc_calc_skilltree(c, aid);
}

/// pc.cpp pc_skill: grants a skill (0 permanent, 1 item bonus, 2 item bonus added to the learned level, 3 permanent
/// outside the skill tree), with the skill window updates — skilltree.rs.
pub fn pc_skill(c: &Ctx, aid: u32, skill_id: u16, level: i32, type_: i32) -> bool {
    crate::skilltree::pc_skill(c, aid, skill_id, level, type_)
}

// ---------------------------------------------------------------------------------------------- packets

/// clif.cpp clif_parse_StatusUp: CZ_STATUS_CHANGE (0x00bb <status id>.W <amount>.B).
pub fn parse_status_up(c: &Ctx, aid: u32, pkt: &[u8]) {
    if pkt.len() < 5 {
        return;
    }
    let type_ = u16::from_le_bytes([pkt[2], pkt[3]]) as i32;
    let increase_amount = pkt[4] as i32; // RFIFOB: never negative
    pc_statusup(c, aid, type_, increase_amount);
}

/// clif.cpp clif_parse_traitstatus_up: CZ_ADVANCED_STATUS_CHANGE (0x0b24 <status id>.W <amount>.W).
pub fn parse_traitstatus_up(c: &Ctx, aid: u32, pkt: &[u8]) {
    if pkt.len() < 6 {
        return;
    }
    let type_ = i16::from_le_bytes([pkt[2], pkt[3]]) as i32;
    let amount = i16::from_le_bytes([pkt[4], pkt[5]]) as i32;
    if amount < 0 {
        log::debug!("clif_parse_traitstatus_up: Negative 'increase' value sent by client! (AID: {aid}, value: {amount})");
        return;
    }
    pc_traitstatusup(c, aid, type_, amount);
}
