//! skills/homunculus/homunculus_stonewall.cpp: class SkillStoneWall : public SkillImpl (MH_STEINWAND). The cast itself
//! (on the homunculus and on its master) is skill_castend_nodamage_id's MH_STEINWAND arm (skill/castend.rs), which
//! calls skill_castend_pos2 for each of them.

use super::unit_castend_pos2;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillStoneWall;

impl SkillImpl for SkillStoneWall {
    fn skill_id(&self) -> u16 {
        MH_STEINWAND
    }

    /// SkillStoneWall::castendPos2: the ground unit (skill_unitsetting).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        unit_castend_pos2(c, self.skill_id(), src, x, y, skill_lv, flag);
    }
}
