//! skills/gunslinger/rapidshower.cpp: class SkillRapidShower : public WeaponSkillImpl (GS_RAPIDSHOWER), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRapidShower;

impl SkillImpl for SkillRapidShower {
    fn skill_id(&self) -> u16 {
        GS_RAPIDSHOWER
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillRapidShower::calculateSkillRatio: + 400 + 50% per level.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 400 + 50 * skill_lv as i32;
    }
}
