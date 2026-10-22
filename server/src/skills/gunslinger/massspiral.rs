//! skills/gunslinger/massspiral.cpp: class SkillMassSpiral : public WeaponSkillImpl (RL_MASS_SPIRAL), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_BLEEDING;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMassSpiral;

impl SkillImpl for SkillMassSpiral {
    fn skill_id(&self) -> u16 {
        RL_MASS_SPIRAL
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillMassSpiral::calculateSkillRatio: 200% per level.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 200 * skill_lv as i32;
    }

    /// SkillMassSpiral::applyAdditionalEffects: bleeding, 30 + 10% per level (skill_get_time).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start2(c, Some(src), target, SC_BLEEDING, 30 + 10 * skill_lv as i32, skill_lv as i32, src.id as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }
}
