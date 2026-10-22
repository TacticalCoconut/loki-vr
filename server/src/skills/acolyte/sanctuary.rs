//! skills/acolyte/sanctuary.cpp: class SkillSanctuary : public SkillImpl (PR_SANCTUARY).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSanctuary;

impl SkillImpl for SkillSanctuary {
    fn skill_id(&self) -> u16 {
        PR_SANCTUARY
    }

    /// SkillSanctuary::castendPos2: the Sanctuary cell group (skill_unitsetting; the heals are its unit's
    /// skill_unit_onplace_timer).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        *flag |= 1;
        crate::skill::iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
