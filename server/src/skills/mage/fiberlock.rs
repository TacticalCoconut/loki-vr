//! skills/mage/fiberlock.cpp: class SkillFiberLock : public SkillImpl (PF_SPIDERWEB).

use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFiberLock;

impl SkillImpl for SkillFiberLock {
    fn skill_id(&self) -> u16 {
        PF_SPIDERWEB
    }

    /// SkillFiberLock::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
