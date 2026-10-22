//! skills/ninja/throwshuriken.cpp: class SkillThrowShuriken : public WeaponSkillImpl (NJ_SYURIKEN), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillThrowShuriken;

impl SkillImpl for SkillThrowShuriken {
    fn skill_id(&self) -> u16 {
        NJ_SYURIKEN
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillThrowShuriken::calculateSkillRatio (RENEWAL: + 5% per level).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 5 * skill_lv as i32;
    }
}
