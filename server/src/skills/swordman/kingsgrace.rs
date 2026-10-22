//! skills/swordman/kingsgrace.cpp: class SkillKingsGrace : public SkillImpl (LG_KINGS_GRACE).

use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillKingsGrace;

impl SkillImpl for SkillKingsGrace {
    fn skill_id(&self) -> u16 {
        LG_KINGS_GRACE
    }

    /// SkillKingsGrace::castendPos2: the skill's unit group (skillunit agent).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
