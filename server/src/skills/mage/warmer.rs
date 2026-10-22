//! skills/mage/warmer.cpp: class SkillWarmer : public SkillImpl (SO_WARMER).

use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWarmer;

impl SkillImpl for SkillWarmer {
    fn skill_id(&self) -> u16 {
        SO_WARMER
    }

    /// SkillWarmer::castendPos2: the skill's unit group (flag |= 8 first).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 8;
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
