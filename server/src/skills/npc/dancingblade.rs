//! skills/npc/dancingblade.cpp: class SkillDancingBlade : public SkillImpl (NPC_DANCINGBLADE), rAthena e985006. The
//! hits are NPC_DANCINGBLADE_ATK timerskills (skill/timerskill.rs), each one bouncing to another enemy.

use crate::db::skills::*;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skills::novice::status_get_amotion;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDancingBlade;

impl SkillImpl for SkillDancingBlade {
    fn skill_id(&self) -> u16 {
        NPC_DANCINGBLADE
    }

    /// SkillDancingBlade::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        skill_addtimerskill(c, src, tick + status_get_amotion(c, src) as Tick, target.id, 0, 0, NPC_DANCINGBLADE_ATK, skill_lv, 0, 0);
    }
}
