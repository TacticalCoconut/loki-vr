//! skills/other/returntoeclage.cpp: class SkillReturnToEclage : public SkillImpl (ECLAGE_RECALL).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillReturnToEclage;

impl SkillImpl for SkillReturnToEclage {
    fn skill_id(&self) -> u16 {
        ECLAGE_RECALL
    }

    /// SkillReturnToEclage::castendNoDamageId: to MAP_ECLAGE_IN ("ecl_in01") 47,31.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        super::recall_castend_nodamage_id(c, self.skill_id(), src, target, skill_lv, flag, "ecl_in01", 47, 31);
    }
}
