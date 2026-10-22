//! skills/thief/scribble.cpp: class SkillScribble : public SkillImpl (RG_GRAFFITI).

use crate::db::skills::*;
use crate::skill::iface::skill_unitsetting;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillScribble;

impl SkillImpl for SkillScribble {
    fn skill_id(&self) -> u16 {
        RG_GRAFFITI
    }

    /// SkillScribble::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
        *flag |= 1;
    }
}
