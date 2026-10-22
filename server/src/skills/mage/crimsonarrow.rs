//! skills/mage/crimsonarrow.cpp: class SkillCrimsonArrow : public SkillImpl (AG_CRIMSON_ARROW), class
//! SkillCrimsonArrowAttack : public SkillImplRecursiveDamageSplash (AG_CRIMSON_ARROW_ATK), rAthena e985006.
//!
//! AG_CRIMSON_ARROW_ATK is cast by SkillCrimsonArrow::castendDamageId itself (skill_castend_damage_id with
//! SD_LEVEL | SD_ANIMATION after the arrow's path hit): SkillImplRecursiveDamageSplash's splash around the target.

use super::stormcannon::eightpath_castend_damage_id;
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::{SD_ANIMATION, SD_LEVEL};
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_CLIMAX;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// AG_CRIMSON_ARROW
pub struct SkillCrimsonArrow;

impl SkillImpl for SkillCrimsonArrow {
    fn skill_id(&self) -> u16 {
        AG_CRIMSON_ARROW
    }

    /// SkillCrimsonArrow::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 400 * skill_lv as i32 + 3 * sstatus.spl as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillCrimsonArrow::castendDamageId: the arrow's path (as SkillStormCannon's), then AG_CRIMSON_ARROW_ATK on the
    /// target.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        eightpath_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        skill_castend_damage_id(c, &s, &t, AG_CRIMSON_ARROW_ATK, skill_lv, tick, *flag | SD_LEVEL | SD_ANIMATION);
    }
}

/// AG_CRIMSON_ARROW_ATK
pub struct SkillCrimsonArrowAttack;

impl SkillImpl for SkillCrimsonArrowAttack {
    fn skill_id(&self) -> u16 {
        AG_CRIMSON_ARROW_ATK
    }

    crate::splash_skill_defaults!();

    /// SkillCrimsonArrowAttack::modifyDamageData.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if sce(c, src, SC_CLIMAX).is_some() {
            dmg.div_ = 2;
        }
    }

    /// SkillCrimsonArrowAttack::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 750 * skill_lv as i32 + 5 * sstatus.spl as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillCrimsonArrowAttack {}
