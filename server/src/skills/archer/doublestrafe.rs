//! skills/archer/doublestrafe.cpp: class SkillDoubleStrafe : public WeaponSkillImpl (AC_DOUBLE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDoubleStrafe;

impl SkillImpl for SkillDoubleStrafe {
    fn skill_id(&self) -> u16 {
        AC_DOUBLE
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillDoubleStrafe::calculateSkillRatio: `base_skillratio += 10 * (skill_lv - 1)`.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 10 * (skill_lv as i32 - 1);
    }
}
