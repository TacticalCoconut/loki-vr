//! skills/npc/darknessjupitel.cpp: class SkillDarknessJupitel : public SkillImpl (NPC_DARKTHUNDER), rAthena e985006.

use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDarknessJupitel;

impl SkillImpl for SkillDarknessJupitel {
    fn skill_id(&self) -> u16 {
        NPC_DARKTHUNDER
    }

    /// SkillDarknessJupitel::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }
}
