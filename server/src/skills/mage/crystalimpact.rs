//! skills/mage/crystalimpact.cpp: class SkillCrystalImpact : public SkillImplRecursiveDamageSplash
//! (AG_CRYSTAL_IMPACT), class SkillCrystalImpactAttack : public SkillImplRecursiveDamageSplash (AG_CRYSTAL_IMPACT_ATK),
//! rAthena e985006.
//!
//! The cast is a castendNoDamageId (the area around the target, each enemy through skill_castend_damage_id with
//! flag | 1: the inherited SkillImplRecursiveDamageSplash::castendDamageId's recursive hit). AG_CRYSTAL_IMPACT_ATK
//! is cast by SkillCrystalImpact::applyAdditionalEffects on every target hit (skill_castend_damage_id with SD_LEVEL:
//! the aftershock splash around it).

use crate::battle::calc::Damage;
use crate::battle::{BCT_ALLY, BCT_ENEMY};
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::{skill_castend_damage_id, skill_castend_nodamage_id};
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::{SD_LEVEL, SD_SPLASH};
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_CLIMAX;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// AG_CRYSTAL_IMPACT
pub struct SkillCrystalImpact;

impl SkillImpl for SkillCrystalImpact {
    fn skill_id(&self) -> u16 {
        AG_CRYSTAL_IMPACT
    }

    crate::splash_skill_defaults!();

    /// SkillCrystalImpact::modifyDamageData.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if sce(c, src, SC_CLIMAX).map(|cl| cl.val1 == 2).unwrap_or(false) {
            dmg.div_ = 2;
        }
    }

    /// SkillCrystalImpact::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);

        *skillratio += -100 + 400 + 1400 * skill_lv as i32;
        *skillratio += 10 * sstatus.spl as i32;

        // (climax buff applied with pc_skillatk_bonus)
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillCrystalImpact::castendNoDamageId.
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
                splash_size = 7; // 15x15
            }

            area::set_temp(1, 0);
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);

            // (map_foreachinrange's int16 range)
            let range = splash_size as i16;
            if climax_lv == 1 {
                // Buffs the caster and allies instead of doing damage AoE.
                let f = *flag | BCT_ALLY | SD_SPLASH | 1;
                foreach_in_range(c, target, range, BL_CHAR, Wall::Config, |c, bl| {
                    area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id)
                });
            } else {
                let f = *flag | BCT_ENEMY | SD_SPLASH | 1;
                foreach_in_range(c, target, range, BL_CHAR, Wall::Config, |c, bl| {
                    area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id)
                });
            }
        }
    }

    /// SkillCrystalImpact::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        // Targets hit are dealt aftershock damage.
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        skill_castend_damage_id(c, &s, &t, AG_CRYSTAL_IMPACT_ATK, skill_lv, tick, SD_LEVEL);
    }
}

impl RecursiveDamageSplash for SkillCrystalImpact {}

/// AG_CRYSTAL_IMPACT_ATK
pub struct SkillCrystalImpactAttack;

impl SkillImpl for SkillCrystalImpactAttack {
    fn skill_id(&self) -> u16 {
        AG_CRYSTAL_IMPACT_ATK
    }

    crate::splash_skill_defaults!();

    /// SkillCrystalImpactAttack::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);

        *skillratio += -100 + 400 + 1400 * skill_lv as i32;
        *skillratio += 10 * sstatus.spl as i32;

        // (climax buff applied with pc_skillatk_bonus)
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillCrystalImpactAttack {
    /// SkillCrystalImpactAttack::getSearchSize.
    fn get_search_size(&self, c: &Ctx, src: &Unit, skill_lv: u16) -> i16 {
        if sce(c, src, SC_CLIMAX).map(|cl| cl.val1 == 5).unwrap_or(false) {
            return 2; // Gives the aftershock hit a 5x5 splash AoE.
        }
        // SkillImplRecursiveDamageSplash::getSearchSize
        skill_get_splash(self.skill_id(), skill_lv) as i16
    }

    /// SkillCrystalImpactAttack::getSplashSearchSize.
    fn get_splash_search_size(&self, c: &Ctx, src: &Unit, skill_lv: u16) -> i16 {
        if sce(c, src, SC_CLIMAX).map(|cl| cl.val1 == 5).unwrap_or(false) {
            return 2; // Gives the aftershock hit a 5x5 splash AoE.
        }
        // SkillImplRecursiveDamageSplash::getSplashSearchSize
        skill_get_splash(self.skill_id(), skill_lv) as i16
    }
}
