//! skills/mage/gravity.cpp: class SkillGravity : public SkillImpl (SA_GRAVITY).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGravity;

impl SkillImpl for SkillGravity {
    fn skill_id(&self) -> u16 {
        SA_GRAVITY
    }

    /// SkillGravity::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        // Skill does nothing. It is only triggered randomly by Hocus Pocus
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
    }
}
