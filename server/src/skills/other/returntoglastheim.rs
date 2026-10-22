//! skills/other/returntoglastheim.cpp: class SkillReturnToGlastHeim : public SkillImpl (ALL_GLASTHEIM_RECALL).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillReturnToGlastHeim;

impl SkillImpl for SkillReturnToGlastHeim {
    fn skill_id(&self) -> u16 {
        ALL_GLASTHEIM_RECALL
    }

    /// SkillReturnToGlastHeim::castendNoDamageId: to MAP_GLASTHEIM ("glast_01") 200,268.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        super::recall_castend_nodamage_id(c, self.skill_id(), src, target, skill_lv, flag, "glast_01", 200, 268);
    }
}
