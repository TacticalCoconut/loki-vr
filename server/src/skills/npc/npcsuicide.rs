//! skills/npc/npcsuicide.cpp: class SkillNpcSuicide : public SkillImpl (NPC_SUICIDE).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillNpcSuicide;

impl SkillImpl for SkillNpcSuicide {
    fn skill_id(&self) -> u16 {
        NPC_SUICIDE
    }

    /// SkillNpcSuicide::castendNoDamageId: the animation, then the caster dies.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        crate::status::hp::status_kill(c, src.id); // When suiciding, neither exp nor drops is given.
    }
}
