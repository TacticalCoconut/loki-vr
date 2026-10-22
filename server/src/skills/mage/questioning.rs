//! skills/mage/questioning.cpp: class SkillQuestioning : public SkillImpl (SA_QUESTION).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc_ext::ET_QUESTION;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillQuestioning;

impl SkillImpl for SkillQuestioning {
    fn skill_id(&self) -> u16 {
        SA_QUESTION
    }

    /// SkillQuestioning::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        // Skill does nothing. It is only triggered randomly by Hocus Pocus
        // clif_emotion(*src, ET_QUESTION): ZC_EMOTION to the caster's area
        crate::clif::emotion(c, src, ET_QUESTION);
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
    }
}
