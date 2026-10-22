//! skills/archer/phantasmicarrow.cpp: class SkillPhantasmicArrow : public WeaponSkillImpl (HT_PHANTASMIC).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillPhantasmicArrow;

impl SkillImpl for SkillPhantasmicArrow {
    fn skill_id(&self) -> u16 {
        HT_PHANTASMIC
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillPhantasmicArrow::calculateSkillRatio: Renewal: `base_skillratio += 400`.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 400;
    }
}
