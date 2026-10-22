//! skills/other/guardiansrecall.cpp: class SkillGuardiansRecall : public SkillImpl (ALL_GUARDIAN_RECALL).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGuardiansRecall;

impl SkillImpl for SkillGuardiansRecall {
    fn skill_id(&self) -> u16 {
        ALL_GUARDIAN_RECALL
    }

    /// SkillGuardiansRecall::castendNoDamageId: to MAP_MORA ("mora") 44,151.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        super::recall_castend_nodamage_id(c, self.skill_id(), src, target, skill_lv, flag, "mora", 44, 151);
    }
}
