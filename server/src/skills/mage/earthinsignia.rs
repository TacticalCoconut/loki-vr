//! skills/mage/earthinsignia.cpp: class SkillEarthInsignia : public SkillImpl (SO_EARTH_INSIGNIA).

use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillEarthInsignia;

impl SkillImpl for SkillEarthInsignia {
    fn skill_id(&self) -> u16 {
        SO_EARTH_INSIGNIA
    }

    /// SkillEarthInsignia::castendPos2: the skill's unit group.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
