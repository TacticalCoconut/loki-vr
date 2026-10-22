//! skills/acolyte/earthshaker.cpp: class SkillEarthShaker : public WeaponSkillImpl (SR_EARTHSHAKER).

use crate::battle::calc::Damage;
use crate::battle::{BCT_ENEMY, DMG_SINGLE};
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_MOB, BL_SKILL};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::{clif_skill_damage, DMGVAL_IGNORE};
use crate::skill::SD_SPLASH;
use crate::skills::novice::{re_lvl_dmod, sce, status_get_amotion};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::{SC_CAMOUFLAGE, SC_CLOAKINGEXCEED, SC_EARTHSHAKER, SC_STEALTHFIELD, SC_STUN, SC_SV_ROOTTWIST, SC__SHADOWFORM};
use crate::status::defs::{OPTION_CHASEWALK, OPTION_CLOAK, OPTION_HIDE};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillEarthShaker;

impl SkillImpl for SkillEarthShaker {
    fn skill_id(&self) -> u16 {
        SR_EARTHSHAKER
    }

    /// SkillEarthShaker::applyAdditionalEffects: SC_EARTHSHAKER on a monster (boss included) that is not a guardian
    /// type [Atemo], a (25 + 5 x level)% stun, and Root Twist ends.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        // dstmd != nullptr && dstmd->guardian_data == nullptr
        if target.kind == BL_MOB && crate::battle::mobif::guardian_guild_id(c, target).is_none() {
            sc::sc_start(c, Some(src), target, SC_EARTHSHAKER, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
        }
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        sc::sc_start(c, Some(src), &t, SC_STUN, 25 + 5 * skill_lv as i32, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        sc::status_change_end(c, target.id, SC_SV_ROOTTWIST, 0);
    }

    /// SkillEarthShaker::calculateSkillRatio: against a hidden / cloaked / camouflaged / shadow-formed target
    /// [(level x 300) x base level / 100 + STR x 3]%, else [(level x 400) x base level / 100 + STR x 2]%.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let str_ = crate::status::status_get_status_data(c, src).str as i32;
        let hidden = crate::battle::mobif::has_sc(target.kind)
            && (target.sc.option & (OPTION_HIDE | OPTION_CLOAK | OPTION_CHASEWALK) != 0
                || sce(c, target, SC_CAMOUFLAGE).is_some()
                || sce(c, target, SC_STEALTHFIELD).is_some()
                || sce(c, target, SC__SHADOWFORM).is_some());
        if hidden {
            // [(Skill Level x 300) x (Caster Base Level / 100) + (Caster STR x 3)] %
            *skillratio += -100 + 300 * skill_lv as i32;
            re_lvl_dmod(c, src, skillratio, 100);
            *skillratio += str_ * 3;
        } else {
            // [(Skill Level x 400) x (Caster Base Level / 100) + (Caster STR x 2)] %
            *skillratio += -100 + 400 * skill_lv as i32;
            re_lvl_dmod(c, src, skillratio, 100);
            *skillratio += str_ * 2;
        }
    }

    /// SkillEarthShaker::castendDamageId: flag & 1: the hit on one target (by default cloaking skills are removed by
    /// AoE skills, so no more checking / removing except Cloaking Exceed and a (100 - 10 x level)% chance to end
    /// Shadow Form); else the splash around the target (map_foreachinrange) and the dummy damage packet on the caster.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            weapon_castend_damage_id(c, id, src, target, skill_lv, tick, *flag);
            sc::status_change_end(c, target.id, SC_CLOAKINGEXCEED, 0);
            if let Some(e) = sce(c, target, SC__SHADOWFORM) {
                // [100 - (Skill Level x 10)] %
                if ((c.rnd() % 100) as i32) < 100 - e.val1 * 10 {
                    sc::status_change_end(c, target.id, SC__SHADOWFORM, 0);
                }
            }
        } else {
            let f = *flag | BCT_ENEMY | SD_SPLASH | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR | BL_SKILL, Wall::Config, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            clif_skill_damage(c, &s, &s, tick, status_get_amotion(c, &s), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        }
    }

    /// SkillEarthShaker::castendNoDamageId: skill_castend_damage_id on the caster (the splash around it).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_castend_damage_id(c, src, src, self.skill_id(), skill_lv, tick, *flag);
    }
}
