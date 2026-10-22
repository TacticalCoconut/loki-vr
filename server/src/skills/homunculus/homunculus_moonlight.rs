//! skills/homunculus/homunculus_moonlight.cpp: class SkillMoonlight : public WeaponSkillImpl (HFLI_MOON).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMoonlight;

impl SkillImpl for SkillMoonlight {
    fn skill_id(&self) -> u16 {
        HFLI_MOON
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillMoonlight::calculateSkillRatio: + 10 + 110 * lv.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 10 + 110 * skill_lv as i32;
    }
}
