//! skill.cpp skill_additional_effect (what a hit sets off on the target: item bonus statuses, the skill's own
//! applyAdditionalEffects, the normal attack's automatic skills, equipment breaking, autospell, polymorph),
//! skill_onskillusage (bAutoSpellOnSkill, autobonus3), skill_counter_additional_effect (what it sets off on the
//! attacker: counter statuses, applyCounterAdditionalEffects, SG_MIRACLE, Soul Drain, HP / SP gain on kill,
//! bAutoSpellWhenHit, autobonus / autobonus2), skill_break_equip and skill_strip_equip.
//! (Wave C's battle agent ported the normal-attack parts in battle/addeff.rs; they live here now, completed.)

use crate::battle::bl;
use crate::battle::*;
use crate::db::skills::*;
use crate::inventory::{EQI_ACC_L, EQI_ACC_R, EQI_ARMOR, EQI_GARMENT, EQI_HAND_L, EQI_HAND_R, EQI_HEAD_TOP, EQI_MAX, EQI_SHADOW_ACC_L, EQI_SHADOW_ACC_R, EQI_SHADOW_ARMOR, EQI_SHADOW_SHIELD, EQI_SHADOW_SHOES, EQI_SHADOW_WEAPON, EQI_SHOES};
use crate::inventory::{EQP_ACC, EQP_ACC_L, EQP_ACC_R, EQP_ARMOR, EQP_GARMENT, EQP_HELM, EQP_SHADOW_ACC_L, EQP_SHADOW_ACC_R, EQP_SHADOW_ARMOR, EQP_SHADOW_GEAR, EQP_SHADOW_SHIELD, EQP_SHADOW_SHOES, EQP_SHADOW_WEAPON, EQP_SHIELD, EQP_SHOES, EQP_WEAPON};
use crate::status::consts::SC_NONE;
use crate::map::{BL_MOB, BL_PC};
use crate::status::consts::*;
use crate::status::defs::*;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

use super::conf;

/// script_constants ET_HUK (e_emotion_type).
const ET_HUK: u8 = 23;

/// pc.hpp e_autospell_flags.
pub const AUTOSPELL_FORCE_SELF: u8 = 0x0;
pub const AUTOSPELL_FORCE_TARGET: u8 = 0x1;
pub const AUTOSPELL_FORCE_RANDOM_LEVEL: u8 = 0x2;
pub const AUTOSPELL_FORCE_ALL: u8 = 0x3;

/// The ATF_ trigger rules of bAddEff / bAddEffWhenHit (attack type and range considerations).
fn addeff_applies(flag: u8, attack_type: i32) -> bool {
    let flag = flag as i32;
    if flag & (ATF_WEAPON | ATF_MAGIC | ATF_MISC) != (ATF_WEAPON | ATF_MAGIC | ATF_MISC) {
        // Trigger has attack type consideration.
        if !((flag & ATF_WEAPON != 0 && attack_type & BF_WEAPON != 0) || (flag & ATF_MAGIC != 0 && attack_type & BF_MAGIC != 0) || (flag & ATF_MISC != 0 && attack_type & BF_MISC != 0)) {
            return false;
        }
    }
    if flag & (ATF_LONG | ATF_SHORT) != (ATF_LONG | ATF_SHORT) {
        // Trigger has range consideration.
        if (flag & ATF_LONG != 0 && attack_type & BF_LONG == 0) || (flag & ATF_SHORT != 0 && attack_type & BF_SHORT == 0) {
            return false; // Range Failed.
        }
    }
    true
}

/// The e_battle_flag masks of an autobonus / autospell trigger.
fn trigger_covers(atk_type: i32, attack_type: i32) -> bool {
    let f = atk_type & attack_type;
    f & BF_WEAPONMASK != 0 && f & BF_RANGEMASK != 0 && f & BF_SKILLMASK != 0
}

/// sd->state.autocast = v.
fn set_autocast(c: &Ctx, aid: u32, v: bool) {
    super::state::with(c, aid, |s| s.autocast = v);
}

/// The canact delay of an autospell (skill.cpp "Set canact delay. [Skotlex]").
fn autospell_canact(c: &Ctx, src_id: u32, skill: u16, skill_lv: u16, tick: Tick, show: bool) {
    let Some(mut u) = unit::get(c, src_id) else { return };
    let delay = super::castfix::skill_delayfix(c, &u, skill, skill_lv) as i64;
    if u.ud.canact_tick - (tick + delay) < 0 {
        u.ud.canact_tick = (tick + delay).max(u.ud.canact_tick);
        unit::save(c, &u);
        if crate::conf::display_status_timers() && show {
            crate::status::clif::status_change(c, &u, EFST_POSTDELAY, 1, delay, 0, 0, 0);
        }
    }
}

/// Casts an autospell the way skill_additional_effect / skill_counter_additional_effect / skill_onskillusage do:
/// by its cast type, from `caster` onto `tbl`.
fn cast_autospell(c: &Ctx, caster: &Unit, tbl: &Unit, skill: u16, lv: u16, type_: CastType, tick: Tick) {
    match type_ {
        CastType::Ground => {
            super::iface::skill_castend_pos2(c, caster, tbl.x, tbl.y, skill, lv, tick, 0);
        }
        CastType::NoDamage => {
            super::castend::skill_castend_nodamage_id(c, caster, tbl, skill, lv, tick, 0);
        }
        CastType::Damage => {
            super::castend::skill_castend_damage_id(c, caster, tbl, skill, lv, tick, 0);
        }
    }
}

/// skill.cpp skill_additional_effect: effects on the target when a hit lands (`dmg_lv` ATK_BLOCK and above).
#[allow(clippy::too_many_arguments)]
pub fn skill_additional_effect(c: &Ctx, src: &Unit, bl: &Unit, skill_id: u16, skill_lv: u16, attack_type: i32, dmg_lv: u8, tick: Tick) -> i32 {
    if skill_id > 0 && skill_lv == 0 {
        return 0; // don't forget auto attacks! - celest
    }
    if dmg_lv < ATK_BLOCK {
        return 0; // Don't apply effect if miss.
    }
    let sd = bl::sd(c, src);
    let scl = bl::sc(c, src);
    let sstatus = bl::status(c, src);
    let tstatus = bl::status(c, bl);
    // Taekwon combos activate on traps, so we need to check them even for targets that don't have status
    if let (Some(_), Some(s)) = (&sd, &scl) {
        if skill_id == 0 && attack_type & BF_SKILL == 0 {
            // Chance to trigger Taekwon kicks
            let dur = (2000 - 4 * sstatus.agi as i32 - 2 * sstatus.dex as i32) as i64;
            if s.has(SC_READYSTORM) && sc::sc_start4(c, Some(src), src, SC_COMBO, 15, TK_STORMKICK as i32, 0, 2, 0, dur) {
                // Stance triggered
            } else if s.has(SC_READYDOWN) && sc::sc_start4(c, Some(src), src, SC_COMBO, 15, TK_DOWNKICK as i32, 0, 2, 0, dur) {
                // Stance triggered
            } else if s.has(SC_READYTURN) && sc::sc_start4(c, Some(src), src, SC_COMBO, 15, TK_TURNKICK as i32, 0, 2, 0, dur) {
                // Stance triggered
            } else if s.has(SC_READYCOUNTER) {
                // additional chance from SG_FRIEND [Komurka]
                let mut rate = 20;
                if let Some(e) = sc::get_sce(c, src.id, SC_SKILLRATE_UP).filter(|e| e.val1 == TK_COUNTER as i32) {
                    rate += rate * e.val2 / 100;
                    sc::status_change_end(c, src.id, SC_SKILLRATE_UP, 0);
                }
                sc::sc_start4(c, Some(src), src, SC_COMBO, rate, TK_COUNTER as i32, 0, 2, 0, dur); // Stance triggered
            }
        }
    }
    if bl::sc(c, bl).is_none() {
        // skill additional effect is about adding effects to the target...
        // So if the target can't be inflicted with statuses, this is pointless.
        return 0;
    }
    if let Some(sd) = &sd {
        // These statuses would be applied anyway even if the damage was blocked by some skills. [Inkfish]
        if !matches!(skill_id, WS_CARTTERMINATION | AM_DEMONSTRATION | CR_REFLECTSHIELD | MS_REFLECTSHIELD | GN_HELLS_PLANT_ATK) {
            // Trigger status effects
            for it in &sd.st.lists.addeff {
                let mut rate = it.rate;
                if attack_type & BF_LONG != 0 {
                    // Any ranged physical attack takes status arrows into account (Grimtooth...) [DracoRPG]
                    rate += it.arrow_rate as i32;
                }
                if rate == 0 || !addeff_applies(it.flag, attack_type) {
                    continue;
                }
                if it.flag as i32 & ATF_TARGET != 0 {
                    let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
                    sc::status_change_start(c, Some(src), &t, it.sc, rate, 7, 0, 0, 0, it.duration as i64, SCSTART_NONE, 100);
                }
                if it.flag as i32 & ATF_SELF != 0 {
                    let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                    sc::status_change_start(c, Some(src), &s, it.sc, rate, 7, 0, 0, 0, it.duration as i64, SCSTART_NONE, 100);
                }
            }
        }
        // Enchanting Shadow gives a chance to inflict Shadow Wounds to the enemy.
        if let Some(e) = scl.as_ref().and_then(|s| s.get(SC_SHADOW_WEAPON)) {
            if c.rnd_chance(e.val1, 100) {
                super::unit::unit_addshadowscar(c, bl.id, skill_get_time2(SHC_ENCHANTING_SHADOW, e.val1 as u16));
            }
        }
        if skill_id != 0 {
            // Trigger status effects on skills
            for it in &sd.st.lists.addeff_onskill {
                if skill_id as i16 != it.skill_id || it.rate == 0 {
                    continue;
                }
                if it.target as i32 & ATF_TARGET != 0 {
                    let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
                    sc::status_change_start(c, Some(src), &t, it.sc, it.rate, 7, 0, 0, 0, it.duration as i64, SCSTART_NONE, 100);
                }
                if it.target as i32 & ATF_SELF != 0 {
                    let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                    sc::status_change_start(c, Some(src), &s, it.sc, it.rate, 7, 0, 0, 0, it.duration as i64, SCSTART_NONE, 100);
                }
            }
            // "While the damage can be blocked by Pneuma, the chance to break armor remains", irowiki. [Cydh]
            if dmg_lv == ATK_BLOCK && skill_id == AM_ACIDTERROR {
                let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
                sc::sc_start2(c, Some(src), &t, SC_BLEEDING, skill_lv as i32 * 3, skill_lv as i32, src.id as i32, skill_get_time2(skill_id, skill_lv) as i64);
                if skill_break_equip(c, src, bl, EQP_ARMOR as u16, (1000 * skill_lv as i32 + 500) - 1000, BCT_ENEMY) != 0 {
                    if let Some(t) = unit::get(c, bl.id) {
                        crate::clif::emotion(c, &t, ET_HUK);
                    }
                }
            }
        }
    }
    if dmg_lv < ATK_DEF {
        return 0; // no damage, return;
    }
    if let Some(imp) = crate::skills::skill_impl(skill_id) {
        let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        imp.apply_additional_effects(c, &s, &t, skill_lv, tick, attack_type, dmg_lv);
    }
    if skill_id == 0 && attack_type & BF_SKILL == 0 {
        // Normal attacks (no skill used); a normal attack that is a skill is splash damage. [Inkfish]
        if let Some(sd) = &sd {
            let chk = |id| bl::checkskill(c, Some(sd), id);
            let falcon = src.sc.option & OPTION_FALCON != 0;
            // Automatic trigger of Blitz Beat
            let skill = chk(HT_BLITZBEAT);
            if falcon && sd.ch.weapon as i32 == W_BOW && skill > 0 && ((c.rnd() % 1000) as i32) <= sstatus.luk as i32 * 10 / 3 + 1 {
                let rate = if sd.st.class_ & crate::db::jobs::MAPID_THIRDMASK == crate::db::jobs::MAPID_RANGER { 5 } else { (sd.ch.job_level as i32 + 9) / 10 };
                if let Some(t) = unit::get(c, bl.id) {
                    super::castend::skill_castend_damage_id(c, src, &t, HT_BLITZBEAT, skill.min(rate) as u16, tick, super::SD_LEVEL);
                }
            }
            // Automatic trigger of Warg Strike
            let skill = chk(RA_WUGSTRIKE);
            if src.sc.option & OPTION_WUG != 0 && skill > 0 {
                let mut rate = sstatus.luk as i32 * 10 / 3 + 1;
                if falcon {
                    rate /= 3;
                }
                if ((c.rnd() % 1000) as i32) <= rate {
                    if let Some(t) = unit::get(c, bl.id) {
                        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                        super::castend::skill_castend_damage_id(c, &s, &t, RA_WUGSTRIKE, skill as u16, tick, 0);
                    }
                }
            }
            // Automatic trigger of Hawk Rush
            let skill = chk(WH_HAWKRUSH);
            if falcon && sd.ch.weapon as i32 == W_BOW && skill > 0 {
                let mut rate = sstatus.con as i32 * 10 / 3 + 1;
                rate += rate * (20 * chk(WH_NATUREFRIENDLY)) / 100;
                if ((c.rnd() % 1000) as i32) <= rate {
                    if let Some(t) = unit::get(c, bl.id) {
                        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                        super::castend::skill_castend_damage_id(c, &s, &t, WH_HAWKRUSH, skill as u16, tick, 0);
                    }
                }
            }
            // Gank
            let skill = chk(RG_SNATCHER);
            if bl.kind == BL_MOB && sd.ch.weapon as i32 != W_BOW && skill > 0 && (skill * 15 + 55) + chk(TF_STEAL) * 10 > (c.rnd() % 1000) as i32 {
                if super::misc::pc_steal_item(c, src, bl, chk(TF_STEAL)) {
                    if let Some(t) = unit::get(c, bl.id) {
                        super::clif::clif_skill_nodamage(c, Some(src), &t, TF_STEAL, skill, true);
                    }
                } else {
                    super::clif::skill_fail(c, src, RG_SNATCHER);
                }
            }
        }
        if let Some(s) = &scl {
            // Enchant Poison gives a chance to poison attacked enemies
            if let Some(e) = s.get(SC_ENCPOISON) {
                // Don't use sc_start since chance comes in 1/10000 rate.
                let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
                sc::status_change_start(c, Some(src), &t, SC_POISON, e.val2, e.val1, src.id as i32, 0, 0, skill_get_time2(AS_ENCHANTPOISON, e.val1 as u16) as i64, SCSTART_NONE, 0);
            }
            // Enchant Deadly Poison gives a chance to deadly poison attacked enemies
            if let Some(e) = s.get(SC_EDP) {
                let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
                sc::sc_start4(c, Some(src), &t, SC_DPOISON, e.val2, e.val1, src.id as i32, 0, 0, skill_get_time2(ASC_EDP, e.val1 as u16) as i64);
            }
            if let Some(e) = s.get(SC_LUXANIMA) {
                if ((c.rnd() % 100) as i32) < e.val2 {
                    if let Some(t) = unit::get(c, bl.id) {
                        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                        super::castend::skill_castend_nodamage_id(c, &s, &t, RK_STORMBLAST, 1, tick, 0);
                    }
                }
            }
        }
    }
    // Pass heritage to Master for status causing effects. [Skotlex]
    let mut src = unit::get(c, src.id).unwrap_or_else(|| src.clone());
    // `sc` stays status_get_sc of the caster itself (taken at the top of the function; the swap below changes only
    // src and sd)
    let sc_owner = src.clone();
    let mut sd = bl::sd(c, &src);
    if src.kind == BL_MOB && conf::summons_trigger_autospells() {
        if let Some(md) = crate::mob::get(c, src.id) {
            use crate::battle::mobif::{AI_ABR, AI_BIONIC};
            let ai = md.special_ai as i32;
            if md.master_id != 0 && ai != 0 && ai != AI_ABR && ai != AI_BIONIC {
                if let Some(m) = unit::get(c, md.master_id).filter(|u| u.kind == BL_PC) {
                    sd = bl::sd(c, &m);
                    src = m;
                }
            }
        }
    }
    let scl = bl::sc(c, &sc_owner);
    if attack_type & BF_WEAPON != 0 {
        // Breaking Equipment
        if sd.is_some() && conf::equip_self_break_rate() != 0 {
            // Self weapon breaking
            let rate = conf::equip_natural_break_rate();
            if rate != 0 {
                skill_break_equip(c, &src, &src, EQP_WEAPON as u16, rate, BCT_SELF);
            }
        }
        if conf::equip_skill_break_rate() != 0 && skill_id != WS_CARTTERMINATION && skill_id != ITM_TOMAHAWK {
            // Cart Termination/Tomahawk won't trigger breaking data. Why? No idea, go ask Gravity.
            // Target weapon breaking
            let mut rate = sd.as_ref().map(|s| s.st.bonus.break_weapon_rate).unwrap_or(0);
            if let Some(s) = &scl {
                if s.has(SC_MELTDOWN) {
                    rate += s.v2(SC_MELTDOWN);
                }
                if s.has(SC_WEAPONBREAKER) {
                    rate += s.v2(SC_WEAPONBREAKER);
                }
            }
            if rate != 0 {
                skill_break_equip(c, &src, bl, EQP_WEAPON as u16, rate, BCT_ENEMY);
            }
            // Target armor breaking
            let mut rate = sd.as_ref().map(|s| s.st.bonus.break_armor_rate).unwrap_or(0);
            if let Some(s) = &scl {
                if s.has(SC_MELTDOWN) {
                    rate += s.v3(SC_MELTDOWN);
                }
            }
            if rate != 0 {
                skill_break_equip(c, &src, bl, EQP_ARMOR as u16, rate, BCT_ENEMY);
            }
        }
        if let Some(sd) = &sd {
            if skill_id == 0 && bl.kind == BL_PC {
                // This effect does not work with skills.
                let r = tstatus.race as usize;
                let races = &sd.st.races;
                let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
                if let Some(e) = races.def_set_race.get(r).filter(|e| e.rate != 0) {
                    sc::status_change_start(c, Some(&src), &t, SC_DEFSET, e.rate, e.value as i32, 0, 0, 0, e.tick as i64, SCSTART_NOTICKDEF, 0);
                }
                if let Some(e) = races.mdef_set_race.get(r).filter(|e| e.rate != 0) {
                    sc::status_change_start(c, Some(&src), &t, SC_MDEFSET, e.rate, e.value as i32, 0, 0, 0, e.tick as i64, SCSTART_NOTICKDEF, 0);
                }
                if let Some(e) = races.norecover_state_race.get(r).filter(|e| e.rate != 0) {
                    sc::status_change_start(c, Some(&src), &t, SC_NORECOVER_STATE, e.rate, 0, 0, 0, 0, e.tick as i64, SCSTART_NONE, 0);
                }
            }
        }
    }
    // sd->ed: the elemental's option autocasts on a normal attack (skill.cpp:1468). `sc` is the caster's own (as
    // above); the skill's level passed to skill_delayfix is this function's skill_lv (0 for a normal attack), and
    // the 25% roll is drawn before `skill` is tested, as rAthena's `rnd()%100 < 25 && skill` does.
    let has_ed = sd.as_ref().map(|s| crate::elemental::data::of_master(c, s.aid).is_some()).unwrap_or(false);
    if let Some(esc) = bl::sc(c, &sc_owner).filter(|_| has_ed && !sc::status_isdead(c, bl) && skill_id == 0) {
        let skill = if let Some(e) = esc.get(SC_WILD_STORM_OPTION) {
            e.val2
        } else if let Some(e) = esc.get(SC_UPHEAVAL_OPTION) {
            e.val3
        } else if let Some(e) = esc.get(SC_TROPIC_OPTION) {
            e.val3
        } else if let Some(e) = esc.get(SC_CHILLY_AIR_OPTION) {
            e.val3
        } else {
            0
        };
        if ((c.rnd() % 100) as i32) < 25 && skill != 0 {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            super::castend::skill_castend_damage_id(c, &s, bl, skill as u16, 5, tick, 0);
            // ud = unit_bl2ud(src): the canact delay, EFST_POSTDELAY with display_status_timers
            autospell_canact(c, src.id, skill as u16, skill_lv, tick, true);
        }
    }
    // Autospell when attacking
    if let Some(sdv) = &sd {
        if !sc::status_isdead(c, bl) && !sdv.st.lists.autospell.is_empty() {
            let aid = sdv.aid;
            for it in sdv.st.lists.autospell.clone() {
                if !trigger_covers(it.battle_flag as i32, attack_type) {
                    continue; // one or more trigger conditions were not fulfilled
                }
                let skill = it.id;
                set_autocast(c, aid, true);
                let Some(su) = unit::get(c, aid) else { break };
                if super::condition::skill_is_not_ok(c, &su, skill) {
                    set_autocast(c, aid, false);
                    continue;
                }
                set_autocast(c, aid, false);
                let mut autospl_skill_lv = if it.lv != 0 { it.lv } else { 1 };
                if it.flag & AUTOSPELL_FORCE_RANDOM_LEVEL != 0 {
                    autospl_skill_lv = c.rnd_value(1, autospl_skill_lv as i32) as u16;
                }
                let arrow_atk = crate::battle::state::get(c, aid).arrow_atk;
                let rate = if !arrow_atk { it.rate as i32 } else { it.rate as i32 / 2 };
                if (c.rnd() % 1000) as i32 >= rate {
                    continue;
                }
                let Some(src_now) = unit::get(c, aid) else { break };
                let Some(bl_now) = unit::get(c, bl.id) else { break };
                let tbl = if it.flag & AUTOSPELL_FORCE_TARGET != 0 { bl_now.clone() } else { src_now.clone() };
                let mut type_ = skill_get_casttype(skill);
                if type_ == CastType::Ground && !super::iface::skill_pos_maxcount_check(c, &src_now, tbl.x, tbl.y, skill, autospl_skill_lv, BL_PC, false) {
                    continue;
                }
                if conf::autospell_check_range() && !crate::battle::target::battle_check_range(c, &bl_now, &tbl, super::misc::skill_get_range2(c, &src_now, skill, autospl_skill_lv, true)) {
                    continue;
                }
                if skill == PF_SPIDERWEB {
                    type_ = CastType::Ground; // Special case, due to its nature of coding.
                }
                set_autocast(c, aid, true);
                super::condition::skill_consume_requirement(c, &src_now, skill, autospl_skill_lv, 1);
                let src_now = unit::get(c, aid).unwrap_or(src_now);
                cast_autospell(c, &src_now, &tbl, skill, autospl_skill_lv, type_, tick);
                set_autocast(c, aid, false);
                // Set canact delay. [Skotlex]
                autospell_canact(c, aid, skill, autospl_skill_lv, tick, true);
            }
        }
        // Polymorph
        let sdv = bl::sd(c, &src).unwrap_or_else(|| sdv.clone());
        if sdv.st.bonus.classchange != 0 && attack_type & BF_WEAPON != 0 && bl.kind == BL_MOB && !crate::battle::mobif::has_mode(tstatus.mode, MD_STATUSIMMUNE as i32) && ((c.rnd() % 10000) as i32) < sdv.st.bonus.classchange {
            let class_ = crate::mob::spawn::get_random_id(c, crate::db::mobs::MOBG_BRANCH_OF_DEAD_TREE, crate::db::mobs::RMF_DB_RATE, 0);
            if class_ != 0 && crate::db::mobs::mobdb_checkid(class_) != 0 {
                crate::mob::dead::mob_class_change(c, bl.id, class_);
            }
        }
    }
    0
}

/// skill.cpp skill_onskillusage: bAutoSpellOnSkill (autospell3) and the autobonus3 of a skill that was used.
pub fn skill_onskillusage(c: &Ctx, aid: u32, bl: Option<&Unit>, skill_id: u16, tick: Tick) -> i32 {
    if skill_id == 0 {
        return 0;
    }
    let Some(sd) = crate::status::data::sd_get(c, aid) else { return 0 };
    let list = sd.lists.autospell3.clone();
    for (i, it) in list.iter().enumerate() {
        if it.trigger_skill != skill_id {
            continue;
        }
        if crate::status::data::sd_get(c, aid).and_then(|s| s.lists.autospell3.get(i).map(|e| e.lock)).unwrap_or(false) {
            continue; // autospell already being executed
        }
        let skill = it.id;
        set_autocast(c, aid, true); // set this to bypass sd->canskill_tick check
        let Some(su) = unit::get(c, aid) else { return 0 };
        if super::condition::skill_is_not_ok(c, &su, skill) {
            set_autocast(c, aid, false);
            continue;
        }
        set_autocast(c, aid, false);
        // DANGER DANGER: here force target actually means use yourself as target!
        let tbl = if it.flag & AUTOSPELL_FORCE_TARGET != 0 { Some(su.clone()) } else { bl.and_then(|b| unit::get(c, b.id)).or_else(|| bl.cloned()) };
        let Some(tbl) = tbl else { continue }; // No target
        if (c.rnd() % 1000) as i32 >= it.rate as i32 {
            continue;
        }
        let mut skill_lv = if it.lv != 0 { it.lv } else { 1 };
        if it.flag & AUTOSPELL_FORCE_RANDOM_LEVEL != 0 {
            skill_lv = c.rnd_value(1, skill_lv as i32) as u16; // random skill_lv
        }
        let type_ = skill_get_casttype(skill);
        // (rAthena checks the maximum count of the triggering skill here, not the cast one)
        if type_ == CastType::Ground && !super::iface::skill_pos_maxcount_check(c, &su, tbl.x, tbl.y, skill_id, skill_lv, BL_PC, false) {
            continue;
        }
        if conf::autospell_check_range() {
            // battle_check_range(bl, tbl, …): without a target its nullpo check answers false
            let Some(from) = bl else { continue };
            if !crate::battle::target::battle_check_range(c, from, &tbl, super::misc::skill_get_range2(c, &su, skill, skill_lv, true)) {
                continue;
            }
        }
        set_autocast(c, aid, true);
        crate::status::data::with_sd(c, aid, |s| {
            if let Some(e) = s.lists.autospell3.get_mut(i) {
                e.lock = true;
            }
        });
        super::condition::skill_consume_requirement(c, &su, skill, skill_lv, 1);
        let su = unit::get(c, aid).unwrap_or(su);
        cast_autospell(c, &su, &tbl, skill, skill_lv, type_, tick);
        crate::status::data::with_sd(c, aid, |s| {
            if let Some(e) = s.lists.autospell3.get_mut(i) {
                e.lock = false;
            }
        });
        set_autocast(c, aid, false);
    }
    // Check for player and pet autobonuses when being attacked by skill_id
    let n = crate::status::data::sd_get(c, aid).map(|s| s.autobonus3.len()).unwrap_or(0);
    for i in 0..n {
        let Some(it) = crate::status::data::sd_get(c, aid).and_then(|s| s.autobonus3.get(i).cloned()) else { break };
        if c.rnd_value(0, 1000) >= it.rate as i32 {
            continue;
        }
        if it.atk_type != skill_id {
            continue;
        }
        crate::status::bonus::pc_exeautobonus(c, aid, 3, i);
    }
    // Pet (sd->pd->autobonus3: pet/bonus.rs)
    crate::pet::bonus::run_pet_autobonus3(c, aid, skill_id);
    1
}

/// skill.cpp skill_counter_additional_effect: effects on the attacker (and kill rewards) of a hit; called even when
/// the hit killed the target.
#[allow(clippy::too_many_arguments)]
pub fn skill_counter_additional_effect(c: &Ctx, src: &Unit, bl: &Unit, skill_id: u16, skill_lv: u16, attack_type: i32, tick: Tick) -> i32 {
    if skill_id > 0 && skill_lv == 0 {
        return 0; // don't forget auto attacks! - celest
    }
    let sd = bl::sd(c, src);
    let dstsd = bl::sd(c, bl);
    if let Some(dstsd) = &dstsd {
        if attack_type & BF_WEAPONMASK != 0 {
            // Counter effects.
            for it in &dstsd.st.lists.addeff_atked {
                let mut rate = it.rate;
                if attack_type & BF_LONG != 0 {
                    rate += it.arrow_rate as i32;
                }
                if rate == 0 || !addeff_applies(it.flag, attack_type) {
                    continue;
                }
                if it.flag as i32 & ATF_TARGET != 0 && src.id != bl.id {
                    let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                    sc::status_change_start(c, Some(src), &s, it.sc, rate, 7, 0, 0, 0, it.duration as i64, SCSTART_NONE, 100);
                }
                if it.flag as i32 & ATF_SELF != 0 && !sc::status_isdead(c, bl) {
                    let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
                    sc::status_change_start(c, Some(src), &t, it.sc, rate, 7, 0, 0, 0, it.duration as i64, SCSTART_NONE, 100);
                }
            }
        }
    }
    let mut attack_type = attack_type;
    if let Some(imp) = crate::skills::skill_impl(skill_id) {
        let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        imp.apply_counter_additional_effects(c, &s, &t, skill_lv, tick, &mut attack_type);
    }
    if let Some(sd) = &sd {
        if sd.st.class_ & crate::db::jobs::MAPID_SECONDMASK == crate::db::jobs::MAPID_STAR_GLADIATOR && !crate::npc::mapflag(c, src.map, crate::npc::MF_NOSUNMOONSTARMIRACLE) {
            // SG_MIRACLE [Komurka]: 0.005% chance per sg_miracle_skill_ratio; reduced if agi is above 46
            if c.rnd_chance(conf::sg_miracle_skill_ratio(), 20000) && c.rnd_chance(46, sd.st.battle_status.agi as i32) {
                let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                sc::sc_start(c, Some(&s), &s, SC_MIRACLE, 100, 1, conf::sg_miracle_skill_duration());
            }
        }
        let rate = bl::checkskill(c, Some(sd), HW_SOULDRAIN);
        if skill_id != 0 && attack_type & BF_MAGIC != 0 && sc::status_isdead(c, bl) && skill_get_inf(skill_id) & (INF_GROUND_SKILL | INF_SELF_SKILL) as i32 == 0 && rate > 0 {
            // Soul Drain should only work on targetted spells [Skotlex]
            if crate::inventory::pc_issit(c, sd.aid) {
                crate::pc::setstand(c, sd.aid, true); // Character stuck in attacking animation while 'sitting' fix. [Skotlex]
            }
            if let Some(t) = unit::get(c, bl.id) {
                super::clif::clif_skill_nodamage(c, Some(src), &t, HW_SOULDRAIN, rate, true);
            }
            let lv = bl::lv_of(c, bl);
            crate::status::hp::status_heal(c, src.id, 0, (lv * (95 + 15 * rate) / 100) as i64, 0, 2);
        }
        if sc::status_isdead(c, bl) {
            let (mut sp, mut hp) = (0i64, 0i64);
            if attack_type & (BF_WEAPON | BF_SHORT) == (BF_WEAPON | BF_SHORT) {
                sp += sd.st.bonus.sp_gain_value as i64;
                let race = bl::status(c, bl).race as usize;
                let sgr = &sd.st.indexed_bonus.sp_gain_race;
                sp += sgr.get(race).copied().unwrap_or(0) as i64 + sgr[RC_ALL as usize] as i64;
                hp += sd.st.bonus.hp_gain_value as i64;
            }
            if attack_type & (BF_WEAPON | BF_LONG) == (BF_WEAPON | BF_LONG) {
                sp += sd.st.bonus.long_sp_gain_value as i64;
                hp += sd.st.bonus.long_hp_gain_value as i64;
            }
            if attack_type & BF_MAGIC != 0 {
                sp += sd.st.bonus.magic_sp_gain_value as i64;
                hp += sd.st.bonus.magic_hp_gain_value as i64;
                if skill_id == WZ_WATERBALL {
                    // (bugreport:5303)
                    if let Some(mut e) = sc::get_sce(c, src.id, SC_SPIRIT).filter(|e| e.val2 == SL_WIZARD as i32 && e.val3 == WZ_WATERBALL as i32) {
                        e.val3 = 0; // Clear bounced spell check.
                        sc::put_sce(c, &e);
                    }
                }
            }
            if hp != 0 || sp != 0 {
                // updated to force healing to allow healing through berserk
                crate::status::hp::status_heal(c, src.id, hp, sp, 0, if conf::show_hp_sp_gain() { 3 } else { 1 });
            }
        }
    }
    let nodamage = skill_id != 0 && skill_get_nk(skill_id, &[NK_NODAMAGE]);
    if let Some(dstsd) = &dstsd {
        if !sc::status_isdead(c, bl) && !nodamage && sc::has_sce(c, bl.id, SC_DORAM_SVSP) && attack_type & (BF_MAGIC | BF_LONG) != 0 {
            let lv = bl::checkskill(c, Some(dstsd), SU_SV_STEMSPEAR);
            if let (Some(b), Some(s)) = (unit::get(c, bl.id), unit::get(c, src.id)) {
                super::castend::skill_castend_damage_id(c, &b, &s, SU_SV_STEMSPEAR, if lv != 0 { lv as u16 } else { 1 }, tick, 0);
            }
        }
    }
    // Trigger counter-spells to retaliate against damage causing skills.
    if let Some(dstsd) = bl::sd(c, bl) {
        if !sc::status_isdead(c, bl) && !dstsd.st.lists.autospell2.is_empty() && !nodamage {
            let daid = dstsd.aid;
            for it in dstsd.st.lists.autospell2.clone() {
                if !trigger_covers(it.battle_flag as i32, attack_type) {
                    continue; // one or more trigger conditions were not fulfilled
                }
                let autospl_skill_id = it.id;
                let mut autospl_skill_lv = if it.lv != 0 { it.lv } else { 1 };
                if it.flag & AUTOSPELL_FORCE_RANDOM_LEVEL != 0 {
                    autospl_skill_lv = c.rnd_value(1, autospl_skill_lv as i32) as u16;
                }
                let mut autospl_rate = it.rate as i32;
                // Physical range attacks only trigger autospells half of the time
                if attack_type & (BF_WEAPON | BF_LONG) == (BF_WEAPON | BF_LONG) {
                    autospl_rate /= 2;
                }
                set_autocast(c, daid, true);
                let Some(bu) = unit::get(c, daid) else { break };
                if super::condition::skill_is_not_ok(c, &bu, autospl_skill_id) {
                    set_autocast(c, daid, false);
                    continue;
                }
                set_autocast(c, daid, false);
                if (c.rnd() % 1000) as i32 >= autospl_rate {
                    continue;
                }
                let s_now = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                let tbl = if it.flag & AUTOSPELL_FORCE_TARGET != 0 { s_now.clone() } else { bu.clone() };
                let type_ = skill_get_casttype(autospl_skill_id);
                if type_ == CastType::Ground && !super::iface::skill_pos_maxcount_check(c, &bu, tbl.x, tbl.y, autospl_skill_id, autospl_skill_lv, BL_PC, false) {
                    continue;
                }
                if !crate::battle::target::battle_check_range(c, &bu, &tbl, super::misc::skill_get_range2(c, &s_now, autospl_skill_id, autospl_skill_lv, true)) && conf::autospell_check_range() {
                    continue;
                }
                set_autocast(c, daid, true);
                super::condition::skill_consume_requirement(c, &bu, autospl_skill_id, autospl_skill_lv, 1);
                let bu = unit::get(c, daid).unwrap_or(bu);
                cast_autospell(c, &bu, &tbl, autospl_skill_id, autospl_skill_lv, type_, tick);
                set_autocast(c, daid, false);
                // Set canact delay. [Skotlex]
                autospell_canact(c, daid, autospl_skill_id, autospl_skill_lv, tick, true);
            }
        }
    }
    // Check for player and pet autobonuses when attacking (the pet's loop: pet/bonus.rs)
    if let Some(sd) = &sd {
        run_autobonus(c, sd.aid, 1, attack_type);
        crate::pet::bonus::run_pet_autobonus(c, sd.aid, 1, attack_type);
    }
    // Check for player and pet autobonuses when attacked
    if let Some(dstsd) = &dstsd {
        if !sc::status_isdead(c, bl) && !nodamage {
            run_autobonus(c, dstsd.aid, 2, attack_type);
            crate::pet::bonus::run_pet_autobonus(c, dstsd.aid, 2, attack_type);
        }
    }
    0
}

/// The autobonus loop of skill_counter_additional_effect over sd->autobonus (1) / autobonus2 (2): each entry rolls
/// rnd_value(0, 1000) against its rate, then its trigger flags must match, then pc_exeautobonus.
fn run_autobonus(c: &Ctx, aid: u32, which: u8, attack_type: i32) {
    let len = |c: &Ctx| crate::status::data::sd_get(c, aid).map(|s| if which == 2 { s.autobonus2.len() } else { s.autobonus.len() }).unwrap_or(0);
    let mut i = 0;
    while i < len(c) {
        let Some(sd) = crate::status::data::sd_get(c, aid) else { return };
        let list = if which == 2 { &sd.autobonus2 } else { &sd.autobonus };
        let Some(it) = list.get(i).cloned() else { return };
        i += 1;
        if c.rnd_value(0, 1000) >= it.rate as i32 {
            continue;
        }
        if !trigger_covers(it.atk_type as i32, attack_type) {
            continue; // one or more trigger conditions were not fulfilled
        }
        crate::status::bonus::pc_exeautobonus(c, aid, which, i - 1);
    }
}

/// skill.cpp skill_break_equip: breaks equipment (`rate` 0..10000); on a non-player it starts the matching strip
/// status. `flag`: BCT_ENEMY / BCT_PARTY / BCT_SELF (which break rate adjustment applies). Returns what broke.
pub fn skill_break_equip(c: &Ctx, src: &Unit, bl: &Unit, where_: u16, rate: i32, flag: i32) -> i32 {
    let src_meltdown = bl::sc(c, src).map(|s| s.has(SC_MELTDOWN)).unwrap_or(false);
    // Grant player skills/items the ability to "break" non-player equipment. WS_MELTDOWN is exempt from this check.
    if !conf::break_mob_equip() && bl.kind != BL_PC && !src_meltdown {
        return 0;
    }
    let where_list: [u32; 6] = [EQP_WEAPON, EQP_ARMOR, EQP_SHIELD, EQP_HELM, EQP_ACC, EQP_SHADOW_GEAR];
    let scatk: [i32; 6] = [SC_STRIPWEAPON, SC_STRIPARMOR, SC_STRIPSHIELD, SC_STRIPHELM, SC__STRIPACCESSORY, SC_SHADOW_STRIP];
    let scdef: [i32; 6] = [SC_CP_WEAPON, SC_CP_ARMOR, SC_CP_SHIELD, SC_CP_HELM, SC_NONE, SC_PROTECTSHADOWEQUIP];
    let tsc = bl::sc_nonempty(c, bl);
    let sd = bl::sd(c, bl);
    let mut where_ = where_ as u32;
    let mut rate = rate;
    if let Some(sd) = &sd {
        if sd.st.bonus.unbreakable_equip != 0 {
            where_ &= !(sd.st.bonus.unbreakable_equip as u32);
        }
        if sd.st.bonus.unbreakable != 0 {
            rate -= rate * sd.st.bonus.unbreakable as i32 / 100;
        }
        if where_ & EQP_WEAPON != 0 {
            match sd.ch.weapon as i32 {
                // Bare fists should not break :P; Axes and Maces can't be broken [DracoRPG]; Rods and Books can't be
                // broken [Skotlex]; Axe usage during dual wield should also prevent breaking [Neutral]
                W_FIST | W_1HAXE | W_2HAXE | W_MACE | W_2HMACE | W_STAFF | W_2HSTAFF | W_BOOK | W_HUUMA | W_DOUBLE_AA | W_DOUBLE_DA | W_DOUBLE_SA => where_ &= !EQP_WEAPON,
                _ => {}
            }
        }
    }
    if flag & BCT_ENEMY != 0 {
        if conf::equip_skill_break_rate() != 100 {
            rate = rate * conf::equip_skill_break_rate() / 100;
        }
    } else if flag & (BCT_PARTY | BCT_SELF) != 0 && conf::equip_self_break_rate() != 100 {
        rate = rate * conf::equip_self_break_rate() / 100;
    }
    for i in 0..6 {
        if where_ & where_list[i] != 0 {
            if tsc.as_ref().map(|s| s.has(scdef[i])).unwrap_or(false) {
                where_ &= !where_list[i];
            } else if (c.rnd() % 10000) as i32 >= rate {
                where_ &= !where_list[i];
            } else if sd.is_none() {
                // Cause Strip effect.
                let sk = crate::db::misc::status::status_db().get_skill(scatk[i]);
                if let Some(t) = unit::get(c, bl.id) {
                    sc::sc_start(c, Some(src), &t, scatk[i], 100, 0, skill_get_time(sk, 1) as i64);
                }
            }
        }
    }
    if where_ == 0 {
        return 0; // Nothing to break.
    }
    if let Some(sd) = &sd {
        for i in 0..EQI_MAX {
            let Some(&j) = sd.it.equip_index.get(i) else { continue };
            if j < 0 {
                continue;
            }
            let Some(row) = crate::inventory::inv_get(c, sd.aid, j as usize) else { continue };
            if row.item.attribute == 1 {
                continue;
            }
            let data = crate::inventory::data_of(&row.item);
            let f = match i {
                EQI_HEAD_TOP => where_ & EQP_HELM != 0,   // Upper Head
                EQI_ARMOR => where_ & EQP_ARMOR != 0,     // Body
                EQI_HAND_R | EQI_HAND_L => {
                    // Left/Right hands
                    (where_ & EQP_WEAPON != 0 && data.r#type as i32 == crate::db::items::IT_WEAPON as i32) || (where_ & EQP_SHIELD != 0 && data.r#type as i32 == crate::db::items::IT_ARMOR as i32)
                }
                EQI_SHOES => where_ & EQP_SHOES != 0,
                EQI_GARMENT => where_ & EQP_GARMENT != 0,
                EQI_ACC_L => where_ & EQP_ACC_L != 0,
                EQI_ACC_R => where_ & EQP_ACC_R != 0,
                EQI_SHADOW_ARMOR => where_ & EQP_SHADOW_ARMOR != 0,
                EQI_SHADOW_WEAPON => where_ & EQP_SHADOW_WEAPON != 0,
                EQI_SHADOW_SHIELD => where_ & EQP_SHADOW_SHIELD != 0,
                EQI_SHADOW_SHOES => where_ & EQP_SHADOW_SHOES != 0,
                EQI_SHADOW_ACC_R => where_ & EQP_SHADOW_ACC_R != 0,
                EQI_SHADOW_ACC_L => where_ & EQP_SHADOW_ACC_L != 0,
                _ => continue,
            };
            if f {
                let mut r = row.clone();
                r.item.attribute = 1;
                crate::inventory::inv_put(c, &r);
                crate::pc_equip::pc_unequipitem(c, sd.aid, j as i32, 3);
            }
        }
        crate::clif_items::equiplist(c, sd.aid);
    }
    where_ as i32 // Return list of pieces broken.
}

/// skill.cpp skill_strip_equip: strips equipment from a target (the strip statuses); true when something was.
pub fn skill_strip_equip(c: &Ctx, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16) -> bool {
    let Some(tsc) = bl::sc(c, target) else { return false };
    let pos: [u32; 6] = [EQP_WEAPON, EQP_SHIELD, EQP_ARMOR, EQP_HELM, EQP_ACC, EQP_SHADOW_GEAR];
    let sc_atk: [i32; 6] = [SC_STRIPWEAPON, SC_STRIPSHIELD, SC_STRIPARMOR, SC_STRIPHELM, SC__STRIPACCESSORY, SC_SHADOW_STRIP];
    let sc_def: [i32; 6] = [SC_CP_WEAPON, SC_CP_SHIELD, SC_CP_ARMOR, SC_CP_HELM, SC_NONE, SC_PROTECTSHADOWEQUIP];
    let sstatus = bl::status(c, src);
    let tstatus = bl::status(c, target);
    let (sdex, tdex) = (sstatus.dex as i32, tstatus.dex as i32);
    let lv = skill_lv as i32;
    let mut mod_ = 100;
    let rate = match skill_id {
        RG_STRIPWEAPON | RG_STRIPARMOR | RG_STRIPSHIELD | RG_STRIPHELM | GC_WEAPONCRUSH => {
            mod_ = 1000;
            50 * (lv + 1) + 2 * (sdex - tdex)
        }
        ST_FULLSTRIP => {
            let min_rate = 50 + 20 * lv;
            mod_ = 1000;
            (min_rate + 2 * (sdex - tdex)).max(min_rate)
        }
        GS_DISARM => {
            let r = sdex / (4 * (7 - lv)) + sstatus.luk as i32 / (4 * (6 - lv));
            r + bl::lv_of(c, src) - (tstatus.agi as i32 * r / 100) - tstatus.luk as i32 - bl::lv_of(c, target)
        }
        WL_EARTHSTRAIN => {
            let job_lv = if src.kind == BL_PC { crate::pc::character_of(c, src.id).map(|ch| ch.job_level as i32).unwrap_or(0) } else { 0 };
            6 * lv + job_lv / 4 + sdex / 10
        }
        SC_STRIPACCESSARY => 12 + 2 * lv,
        ABC_STRIP_SHADOW => {
            mod_ = 1000;
            50 * (lv + 3) + 2 * (sdex - tdex)
        }
        _ => return false,
    };
    if (c.rnd() % mod_ as u32) as i32 >= rate {
        return false;
    }
    let time: i32 = match skill_id {
        // Duration
        SC_STRIPACCESSARY | GS_DISARM => skill_get_time(skill_id, skill_lv),
        _ => {
            let mut t = if skill_id == WL_EARTHSTRAIN { skill_get_time2(skill_id, skill_lv) } else { skill_get_time(skill_id, skill_lv) };
            if target.kind == BL_PC {
                t += (lv + 500 * (sdex - tdex)).max(1);
            } else {
                t += 15000;
                t += (lv + 500 * (sdex - tdex)).max(1);
            }
            t
        }
    };
    let mut location: u32 = match skill_id {
        GC_WEAPONCRUSH | RG_STRIPWEAPON | GS_DISARM => EQP_WEAPON,
        RG_STRIPARMOR => EQP_ARMOR,
        RG_STRIPSHIELD => EQP_SHIELD,
        RG_STRIPHELM => EQP_HELM,
        ST_FULLSTRIP => EQP_WEAPON | EQP_SHIELD | EQP_ARMOR | EQP_HELM,
        SC_STRIPACCESSARY => EQP_ACC,
        ABC_STRIP_SHADOW => EQP_SHADOW_GEAR,
        _ => 0,
    };
    for i in 0..pos.len() {
        if location & pos[i] != 0 && sc_def[i] > SC_NONE && tsc.has(sc_def[i]) {
            location &= !pos[i];
        }
    }
    if location == 0 {
        return false;
    }
    for i in 0..pos.len() {
        if location & pos[i] != 0 {
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            if !sc::sc_start(c, Some(src), &t, sc_atk[i], 100, lv, time as i64) {
                location &= !pos[i];
            }
        }
    }
    location != 0
}
