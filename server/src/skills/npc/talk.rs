//! skills/npc/talk.cpp: class SkillTalk : public SkillImpl (NPC_TALK).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillTalk;

impl SkillImpl for SkillTalk {
    fn skill_id(&self) -> u16 {
        NPC_TALK
    }

    /// SkillTalk::castendNoDamageId: the animation only.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
    }
}
