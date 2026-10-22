//! skills/thief/venompressure.cpp: class SkillVenomPressure : public WeaponSkillImpl (GC_VENOMPRESSURE).
//! (The poison it applies is skill.cpp's: skill_additional_effect's GC_VENOMPRESSURE arm.)

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillVenomPressure;

impl SkillImpl for SkillVenomPressure {
    fn skill_id(&self) -> u16 {
        GC_VENOMPRESSURE
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillVenomPressure::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 900;
    }

    /// SkillVenomPressure::modifyHitRate.
    fn modify_hit_rate(&self, _c: &Ctx, hit_rate: &mut i16, _src: &Unit, _target: &Unit, skill_lv: u16) {
        *hit_rate = hit_rate.wrapping_add(10 + 4 * skill_lv as i16);
    }
}
