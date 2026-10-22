//! skills/npc/darkpiercing.cpp: class SkillDarkPiercing : public SkillImpl (NPC_DARKPIERCING), rAthena e985006.

use super::acidbreath::breath_castend_damage_id;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDarkPiercing;

impl SkillImpl for SkillDarkPiercing {
    fn skill_id(&self) -> u16 {
        NPC_DARKPIERCING
    }

    /// SkillDarkPiercing::castendDamageId (the breath body, acidbreath.rs).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        breath_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }
}
