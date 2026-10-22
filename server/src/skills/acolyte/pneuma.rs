//! skills/acolyte/pneuma.cpp: class SkillPneuma : public SkillImpl (AL_PNEUMA).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillPneuma;

impl SkillImpl for SkillPneuma {
    fn skill_id(&self) -> u16 {
        AL_PNEUMA
    }

    /// SkillPneuma::castendPos2: the Pneuma cell group (skill_unitsetting).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        *flag |= 1;
        crate::skill::iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
