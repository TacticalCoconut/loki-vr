//! skills/archer/verduretrap.cpp: class SkillVerdureTrap : public SkillImpl (RA_VERDURETRAP). A trap; the unit is skillunit's.

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillVerdureTrap;

impl SkillImpl for SkillVerdureTrap {
    fn skill_id(&self) -> u16 {
        RA_VERDURETRAP
    }

    /// SkillVerdureTrap::castendPos2: `flag |= 1` (the ammo is deleted with the group), skill_unitsetting.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        super::unit_castend_pos2(c, self.skill_id(), src, x, y, skill_lv, flag);
    }
}
