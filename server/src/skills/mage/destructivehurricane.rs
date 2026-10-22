//! skills/mage/destructivehurricane.cpp: class SkillDestructiveHurricane : public SkillImplRecursiveDamageSplash
//! (AG_DESTRUCTIVE_HURRICANE), class SkillDestructiveHurricaneClimax : public SkillImpl
//! (AG_DESTRUCTIVE_HURRICANE_CLIMAX), rAthena e985006.
//!
//! The cast is a castendNoDamageId (the area around the target, each enemy through skill_castend_damage_id with
//! flag | 1: the inherited SkillImplRecursiveDamageSplash::castendDamageId's recursive hit).
//! AG_DESTRUCTIVE_HURRICANE_CLIMAX is cast by SkillDestructiveHurricane::applyAdditionalEffects on every target hit
//! while Climax level 1 is active (skill_castend_damage_id with SD_LEVEL | SD_ANIMATION).

use crate::battle::calc::Damage;
use crate::battle::{BCT_ENEMY, BF_MAGIC};
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::{SD_ANIMATION, SD_LEVEL, SD_SPLASH};
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_CLIMAX;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// AG_DESTRUCTIVE_HURRICANE
pub struct SkillDestructiveHurricane;

impl SkillImpl for SkillDestructiveHurricane {
    fn skill_id(&self) -> u16 {
        AG_DESTRUCTIVE_HURRICANE
    }

    crate::splash_skill_defaults!();

    /// SkillDestructiveHurricane::modifyDamageData.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if sce(c, src, SC_CLIMAX).map(|cl| cl.val1 == 2).unwrap_or(false) {
            dmg.blewcount = 2;
        }
    }

    /// SkillDestructiveHurricane::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);

        *skillratio += -100 + 600 + 3250 * skill_lv as i32;
        *skillratio += 10 * sstatus.spl as i32;

        // (climax buff applied with pc_skillatk_bonus)
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillDestructiveHurricane::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;

        if *flag & 1 != 0 {
            // Buff from Crystal Impact with level 1 Climax.
            sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
        } else {
            let mut climax_lv: u16 = 0;
            let mut splash_size = skill_get_splash(id, skill_lv) as u16;

            if let Some(cl) = sce(c, src, SC_CLIMAX) {
                climax_lv = cl.val1 as u16;
            }

            if climax_lv == 5 {
                // Adjusts splash AoE size depending on skill.
                splash_size = 9; // 19x19
            }

            area::set_temp(1, 0);
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);

            if climax_lv == 4 {
                // Buff for caster instead of damage AoE.
                sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
            } else {
                if climax_lv == 1 {
                    // Display extra animation for the additional hit cast.
                    clif_skill_nodamage(c, Some(src), target, AG_DESTRUCTIVE_HURRICANE_CLIMAX, skill_lv as i32, true);
                }

                // (map_foreachinrange's int16 range)
                let f = *flag | BCT_ENEMY | SD_SPLASH | 1;
                foreach_in_range(c, target, splash_size as i16, BL_CHAR, Wall::Config, |c, bl| {
                    area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id)
                });
            }
        }
    }

    /// SkillDestructiveHurricane::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        // Targets hit are dealt a additional hit through Climax.
        if sce(c, src, SC_CLIMAX).map(|cl| cl.val1 == 1).unwrap_or(false) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            skill_castend_damage_id(c, &s, &t, AG_DESTRUCTIVE_HURRICANE_CLIMAX, skill_lv, tick, SD_LEVEL | SD_ANIMATION);
        }
    }
}

impl RecursiveDamageSplash for SkillDestructiveHurricane {}

/// AG_DESTRUCTIVE_HURRICANE_CLIMAX
pub struct SkillDestructiveHurricaneClimax;

impl SkillImpl for SkillDestructiveHurricaneClimax {
    fn skill_id(&self) -> u16 {
        AG_DESTRUCTIVE_HURRICANE_CLIMAX
    }

    /// SkillDestructiveHurricaneClimax::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + 12500;
        // Skill not affected by Baselevel and SPL
    }

    /// SkillDestructiveHurricaneClimax::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }
}
