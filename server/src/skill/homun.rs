//! The homunculus parts of the skill pipeline (rAthena e985006, RENEWAL):
//! - skill.cpp skill_isNotOk_hom (the homunculus' own checks, its spirit sphere cost, then the master's
//!   skill_isNotOk) and skill_check_condition_mercenary (a homunculus' / mercenary's HP / SP / state / item
//!   requirements and their consumption; the one port of it, for both kinds);
//! - clif.cpp clif_parse_UseSkillToId_homun / clif_parse_UseSkillToPos_homun (the master's request for its
//!   homunculus, dispatched from clif_parse_skill_toid / clif_parse_UseSkillToPosSub in skill/parse.rs).

use crate::db::skills::*;
use crate::homunculus::data::{self as hdata, HomData, HOMGRADE_CORDIAL, HOMGRADE_HATE_WITH_PASSION, MH_MD_FIGHTING, MH_MD_GRAPPLING};
use crate::map::{BL_HOM, BL_MER};
use crate::status::consts::{SC_ANGRIFFS_MODUS, SC_COMBO, SC_GOLDENE_FERSE, SC_STYLE_CHANGE};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

use super::clif::fail::*;
use super::clif::{clif_skill_fail, skill_fail};

/// hd->master as a unit (nullptr when the master is not in the world).
fn master_of(c: &Ctx, hd: &HomData) -> Option<Unit> {
    crate::pc::get(c, hd.master)?;
    unit::get(c, hd.master)
}

// ---------------------------------------------------------------------------------------------- skill_isNotOk_hom

/// skill.cpp skill_isNotOk_hom: true when the homunculus cannot use the skill (a cooldown, the intimacy / style /
/// combo rules of its skills, too few spirit spheres); the spheres the skill costs are taken here (without a client
/// update: `hom_delspiritball(hd, spiritball, 1)`, "TODO: no update on the client ?" upstream), except Blazing And
/// Furious's (skill_attack removes them all). Then the master's skill_isNotOk.
pub fn skill_isnotok_hom(c: &Ctx, hd_id: u32, skill_id: u16, skill_lv: u16) -> bool {
    let Some(hd) = hdata::get(c, hd_id) else { return true }; // nullpo_retr(true, hd)
    let spiritball = skill_get_spiritball(skill_id, skill_lv) as i8;
    let Some(sd) = master_of(c, &hd) else { return true };
    // status_get_sc(hd), nullptr when empty
    let sc = unit::get(c, hd_id).and_then(|u| crate::battle::bl::sc_nonempty(c, &u));
    let style = sc.as_ref().and_then(|s| s.get(SC_STYLE_CHANGE)).map(|e| e.val1);
    let combo = sc.as_ref().and_then(|s| s.get(SC_COMBO)).map(|e| e.val1);
    let has = |t: i32| sc.as_ref().map(|s| s.has(t)).unwrap_or(false);
    let fail = |cause: u8, btype: i32| clif_skill_fail(c, &sd, skill_id, cause, btype, 0);

    if crate::homunculus::cooldown::is_blocked(c, hd_id, skill_id) {
        fail(USESKILL_FAIL_SKILLINTERVAL, 0);
        return true;
    }
    match skill_id {
        HFLI_SBR44 => {
            if crate::homunculus::hom_get_intimacy_grade(&hd) <= HOMGRADE_HATE_WITH_PASSION {
                fail(USESKILL_FAIL_RELATIONGRADE, 0);
                return true;
            }
        }
        HVAN_EXPLOSION => {
            if hd.homunculus.intimacy < crate::homunculus::hvan_explosion_intimate() {
                fail(USESKILL_FAIL_RELATIONGRADE, 0);
                return true;
            }
        }
        MH_LIGHT_OF_REGENE => {
            // Must be cordial
            if crate::homunculus::hom_get_intimacy_grade(&hd) < HOMGRADE_CORDIAL {
                fail(USESKILL_FAIL_RELATIONGRADE, 0);
                return true;
            }
        }
        MH_GOLDENE_FERSE => {
            // Can't be used with Angriff's Modus
            if has(SC_ANGRIFFS_MODUS) {
                return true;
            }
        }
        MH_ANGRIFFS_MODUS => {
            if has(SC_GOLDENE_FERSE) {
                return true;
            }
        }
        MH_TINDER_BREAKER => {
            // Must be in grappling mode
            if style != Some(MH_MD_GRAPPLING) {
                fail(USESKILL_FAIL_STYLE_CHANGE_GRAPPLER, 1);
                return true;
            }
        }
        MH_SONIC_CRAW => {
            // Must be in fighting mode
            if style != Some(MH_MD_FIGHTING) {
                fail(USESKILL_FAIL_STYLE_CHANGE_FIGHTER, 0);
                return true;
            }
        }
        MH_SILVERVEIN_RUSH => {
            if style != Some(MH_MD_FIGHTING) {
                fail(USESKILL_FAIL_STYLE_CHANGE_FIGHTER, 0);
                return true;
            }
            if combo != Some(MH_SONIC_CRAW as i32) {
                fail(USESKILL_FAIL_COMBOSKILL, MH_SONIC_CRAW as i32);
                return true;
            }
        }
        MH_MIDNIGHT_FRENZY => {
            if style != Some(MH_MD_FIGHTING) {
                fail(USESKILL_FAIL_STYLE_CHANGE_FIGHTER, 0);
                return true;
            }
            if combo != Some(MH_SILVERVEIN_RUSH as i32) {
                fail(USESKILL_FAIL_COMBOSKILL, MH_SILVERVEIN_RUSH as i32);
                return true;
            }
        }
        MH_CBC => {
            if style != Some(MH_MD_GRAPPLING) {
                fail(USESKILL_FAIL_STYLE_CHANGE_GRAPPLER, 0);
                return true;
            }
            if combo != Some(MH_TINDER_BREAKER as i32) {
                fail(USESKILL_FAIL_COMBOSKILL, MH_TINDER_BREAKER as i32);
                return true;
            }
        }
        MH_EQC => {
            if style != Some(MH_MD_GRAPPLING) {
                fail(USESKILL_FAIL_STYLE_CHANGE_GRAPPLER, 0);
                return true;
            }
            if combo != Some(MH_CBC as i32) {
                fail(USESKILL_FAIL_COMBOSKILL, MH_CBC as i32);
                return true;
            }
        }
        _ => {}
    }
    if spiritball != 0 {
        if hd.homunculus.spiritball < spiritball {
            fail(USESKILL_FAIL_SPIRITS, spiritball as i32);
            return true;
        }
        // Blazing And Furious removes all spirit balls after calculating the amount of hits
        if skill_id != MH_BLAZING_AND_FURIOUS {
            crate::homunculus::hom_delspiritball(c, hd_id, spiritball as i32, 1); // TODO: no update on the client ?
        }
    }
    // Use master's criteria.
    let sd = unit::get(c, sd.id).unwrap_or(sd);
    super::condition::skill_is_not_ok(c, &sd, skill_id)
}

// ---------------------------------------------------------------------------------------------- skill_check_condition_mercenary

/// skill.cpp skill_check_condition_mercenary: the requirements of a homunculus' (or mercenary's) skill. `type_` &1:
/// the cast finished (items, HP and SP are consumed); &2: a menu entry was picked (no HP / SP check, items only).
/// Failures go to the master (clif_skill_fail). Without a master rAthena's `sd == nullptr` answers 0.
pub fn skill_check_condition_mercenary(c: &Ctx, bl: &Unit, skill_id: u16, skill_lv: u16, type_: i32) -> bool {
    let sd = match bl.kind {
        // case BL_HOM: sd = ((TBL_HOM*)bl)->master; (skill.cpp:3562)
        BL_HOM => hdata::get(c, bl.id).and_then(|hd| master_of(c, &hd)),
        // case BL_MER: sd = ((TBL_MER*)bl)->master; (skill.cpp:3563; mercenary/data.rs)
        BL_MER => crate::mercenary::data::get(c, bl.id).filter(|md| crate::pc::get(c, md.master).is_some()).and_then(|md| unit::get(c, md.master)),
        _ => None,
    };
    let Some(sd) = sd else { return false };
    let status = crate::status::status_get_status_data(c, bl);
    let skill_lv = skill_lv.clamp(1, MAX_SKILL_LEVEL as u16);
    let Some(skill) = skill_db().find(skill_id) else { return false };
    let l = skill_lv as usize - 1;
    // Requirements
    let itemid = skill.require.itemid;
    let amount = skill.require.amount;
    let mut hp = skill.require.hp[l];
    let mut sp = skill.require.sp[l];
    let hp_rate = skill.require.hp_rate[l];
    let sp_rate = skill.require.sp_rate[l];
    let state = skill.require.state;
    let mhp = skill.require.mhp[l];
    // (uint32 status arithmetic added to the int32 costs, as rAthena's)
    if mhp > 0 {
        hp = (hp as u32).wrapping_add(status.max_hp.wrapping_mul(mhp as u32) / 100) as i32;
    }
    if hp_rate > 0 {
        hp = (hp as u32).wrapping_add(status.hp.wrapping_mul(hp_rate as u32) / 100) as i32;
    } else {
        hp = (hp as u32).wrapping_add(status.max_hp.wrapping_mul(hp_rate.wrapping_neg() as u32) / 100) as i32;
    }
    if sp_rate > 0 {
        sp = (sp as u32).wrapping_add(status.sp.wrapping_mul(sp_rate as u32) / 100) as i32;
    } else {
        sp = (sp as u32).wrapping_add(status.max_sp.wrapping_mul(sp_rate.wrapping_neg() as u32) / 100) as i32;
    }
    if type_ & 2 == 0 {
        if hp > 0 && status.hp <= hp as u32 {
            clif_skill_fail(c, &sd, skill_id, USESKILL_FAIL_HP_INSUFFICIENT, 0, 0);
            return false;
        }
        if sp > 0 && status.sp <= sp as u32 {
            clif_skill_fail(c, &sd, skill_id, USESKILL_FAIL_SP_INSUFFICIENT, 0, 0);
            return false;
        }
    }
    if type_ == 0 && state == ST_MOVE_ENABLE {
        let u = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
        if !unit::can_move(c, &u) {
            skill_fail(c, &sd, skill_id);
            return false;
        }
    }
    if type_ & 1 == 0 {
        return true;
    }
    // Check item existences
    let mut index = [-1i16; MAX_SKILL_ITEM_REQUIRE];
    for i in 0..MAX_SKILL_ITEM_REQUIRE {
        index[i] = -1;
        if itemid[i] == 0 {
            continue; // No item
        }
        index[i] = crate::inventory::pc_search_inventory(c, sd.id, itemid[i]);
        let enough = index[i] >= 0 && crate::inventory::inv_get(c, sd.id, index[i] as usize).map(|r| r.item.amount as i32 >= amount[i]).unwrap_or(false);
        if !enough {
            skill_fail(c, &sd, skill_id);
            return false;
        }
    }
    // Consume items
    for i in 0..MAX_SKILL_ITEM_REQUIRE {
        if index[i] >= 0 {
            crate::inventory::pc_delitem(c, sd.id, index[i] as i32, amount[i], 0, 1, crate::map_log::LOG_TYPE_CONSUME);
        }
    }
    if type_ & 2 != 0 {
        return true;
    }
    if sp != 0 || hp != 0 {
        let u = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
        super::condition::skill_consume_hpspap(c, &u, skill_id, hp, sp, 0);
    }
    true
}

// ---------------------------------------------------------------------------------------------- clif.cpp requests

/// clif.cpp clif_parse_UseSkillToId_homun: the master asks its homunculus (sd->hd) to use a targeted skill.
pub fn clif_parse_use_skill_to_id_homun(c: &Ctx, aid: u32, tick: Tick, skill_id: u16, skill_lv: u16, target_id: u32) {
    let Some(hd) = hdata::of_master(c, aid) else { return }; // !hd
    let mut skill_lv = skill_lv;
    let mut target_id = target_id;
    if skill_isnotok_hom(c, hd.id, skill_id, skill_lv) {
        if let Some(hu) = unit::get(c, hd.id) {
            crate::clif::emotion(c, &hu, crate::homunculus::ET_THINK);
        }
        return;
    }
    if hd.id != target_id && skill_get_inf(skill_id) & INF_SELF_SKILL as i32 != 0 {
        target_id = hd.id;
    }
    let Some(hu) = unit::get(c, hd.id) else { return };
    if hu.ud.skilltimer != 0 {
        if skill_id != SA_CASTCANCEL && skill_id != SO_SPELLFIST {
            return;
        }
    } else if tick - hu.ud.canact_tick < 0 {
        crate::clif::emotion(c, &hu, crate::homunculus::ET_THINK);
        if let Some(hd) = hdata::get(c, hd.id) {
            if let Some(mu) = master_of(c, &hd) {
                clif_skill_fail(c, &mu, skill_id, USESKILL_FAIL_SKILLINTERVAL, 0, 0);
            }
        }
        return;
    }
    let lv = crate::homunculus::hom_checkskill_id(c, hd.id, skill_id);
    if skill_lv as i32 > lv as i32 {
        skill_lv = lv.max(0) as u16;
    }
    if skill_lv != 0 {
        super::unit::unit_skilluse_id(c, hd.id, target_id, skill_id, skill_lv);
    }
}

/// clif.cpp clif_parse_UseSkillToPos_homun: the master asks its homunculus (sd->hd) to use a ground skill. (The
/// pre-renewal SC_BASILICA check is `#ifndef RENEWAL`: not in this build.)
pub fn clif_parse_use_skill_to_pos_homun(c: &Ctx, aid: u32, tick: Tick, skill_id: u16, skill_lv: u16, x: i16, y: i16) {
    let Some(hd) = hdata::of_master(c, aid) else { return }; // !hd
    let mut skill_lv = skill_lv;
    if skill_isnotok_hom(c, hd.id, skill_id, skill_lv) {
        if let Some(hu) = unit::get(c, hd.id) {
            crate::clif::emotion(c, &hu, crate::homunculus::ET_THINK);
        }
        return;
    }
    let Some(hu) = unit::get(c, hd.id) else { return };
    if hu.ud.skilltimer != 0 {
        if skill_id != SA_CASTCANCEL && skill_id != SO_SPELLFIST {
            return;
        }
    } else if tick - hu.ud.canact_tick < 0 {
        crate::clif::emotion(c, &hu, crate::homunculus::ET_THINK);
        if let Some(hd) = hdata::get(c, hd.id) {
            if let Some(mu) = master_of(c, &hd) {
                clif_skill_fail(c, &mu, skill_id, USESKILL_FAIL_SKILLINTERVAL, 0, 0);
            }
        }
        return;
    }
    let lv = crate::homunculus::hom_checkskill_id(c, hd.id, skill_id);
    if skill_lv as i32 > lv as i32 {
        skill_lv = lv.max(0) as u16;
    }
    if skill_lv != 0 {
        super::unit::unit_skilluse_pos(c, hd.id, x, y, skill_id, skill_lv);
    }
}
