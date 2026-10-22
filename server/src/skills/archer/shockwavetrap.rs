//! skills/archer/shockwavetrap.cpp: class SkillShockwaveTrap : public SkillImpl (HT_SHOCKWAVE). A trap; the unit is skillunit's.

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillShockwaveTrap;

impl SkillImpl for SkillShockwaveTrap {
    fn skill_id(&self) -> u16 {
        HT_SHOCKWAVE
    }

    /// SkillShockwaveTrap::castendPos2: `flag |= 1` (the ammo is deleted with the group), skill_unitsetting.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        super::unit_castend_pos2(c, self.skill_id(), src, x, y, skill_lv, flag);
    }

    /// SkillShockwaveTrap::applyAdditionalEffects: `status_percent_damage(src, target, 0, -(15*skill_lv+5), false)`.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        crate::status::hp::status_percent_damage(c, Some(src), target.id, 0, -(15 * skill_lv as i32 + 5), 0, false);
    }
}
