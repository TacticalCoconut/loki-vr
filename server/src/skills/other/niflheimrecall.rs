//! skills/other/niflheimrecall.cpp: class SkillNiflheimRecall : public SkillImpl (ALL_NIFLHEIM_RECALL).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillNiflheimRecall;

impl SkillImpl for SkillNiflheimRecall {
    fn skill_id(&self) -> u16 {
        ALL_NIFLHEIM_RECALL
    }

    /// SkillNiflheimRecall::castendNoDamageId: to MAP_NIFLHEIM ("niflheim") 193,186.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        super::recall_castend_nodamage_id(c, self.skill_id(), src, target, skill_lv, flag, "niflheim", 193, 186);
    }
}
