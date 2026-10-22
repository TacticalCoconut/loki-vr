//! skills/homunculus/homunculus_volcanicash.cpp: class SkillVolcanicAsh : public SkillImpl (MH_VOLCANIC_ASH).

use super::unit_castend_pos2;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillVolcanicAsh;

impl SkillImpl for SkillVolcanicAsh {
    fn skill_id(&self) -> u16 {
        MH_VOLCANIC_ASH
    }

    /// SkillVolcanicAsh::castendPos2: the ground unit (skill_unitsetting).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        unit_castend_pos2(c, self.skill_id(), src, x, y, skill_lv, flag);
    }
}
