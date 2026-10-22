//! skills/thief/chaospanic.cpp: class SkillChaosPanic : public SkillImpl (SC_CHAOSPANIC).

use crate::db::skills::*;
use crate::skill::iface::skill_unitsetting;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillChaosPanic;

impl SkillImpl for SkillChaosPanic {
    fn skill_id(&self) -> u16 {
        SC_CHAOSPANIC
    }

    /// SkillChaosPanic::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; //Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
