//! skills/ninja/hiddenwater.cpp: class SkillHiddenWater : public SkillImpl (NJ_SUITON), rAthena e985006.

use crate::db::skills::*;
use crate::skill::iface::skill_unitsetting;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHiddenWater;

impl SkillImpl for SkillHiddenWater {
    fn skill_id(&self) -> u16 {
        NJ_SUITON
    }

    /// SkillHiddenWater::castendPos2: flag 1 keeps the ammo (it is deleted when the group is).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        *flag |= 1;
        skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
