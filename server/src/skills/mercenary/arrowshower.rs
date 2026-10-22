//! skills/mercenary/mercenary_arrowshower.cpp: class SkillMercenaryArrowShower : public SkillImplRecursiveDamageSplash
//! (MA_SHOWER).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::Ctx;

pub struct SkillMercenaryArrowShower;

impl SkillImpl for SkillMercenaryArrowShower {
    fn skill_id(&self) -> u16 {
        MA_SHOWER
    }

    // SkillImplRecursiveDamageSplash::castendDamageId / castendPos2 (not overridden).
    crate::splash_skill_defaults!();

    /// SkillMercenaryArrowShower::calculateSkillRatio (RENEWAL).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 50 + 10 * skill_lv as i32;
    }
}

/// SkillImplRecursiveDamageSplash's own virtuals: none overridden.
impl RecursiveDamageSplash for SkillMercenaryArrowShower {}
