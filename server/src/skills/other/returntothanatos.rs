//! skills/other/returntothanatos.cpp: class SkillReturnToThanatos : public SkillImpl (ALL_THANATOS_RECALL).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillReturnToThanatos;

impl SkillImpl for SkillReturnToThanatos {
    fn skill_id(&self) -> u16 {
        ALL_THANATOS_RECALL
    }

    /// SkillReturnToThanatos::castendNoDamageId: to MAP_THANATOS ("hu_fild01") 139,156.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        super::recall_castend_nodamage_id(c, self.skill_id(), src, target, skill_lv, flag, "hu_fild01", 139, 156);
    }
}
