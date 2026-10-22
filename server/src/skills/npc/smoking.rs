//! skills/npc/smoking.cpp: class SkillSmoking : public SkillImpl (NPC_SMOKING). A self skill: skill_castend_nodamage_id
//! hands it to skill_castend_damage_id (castend.rs, "Since it is a self skill, this one ends here rather than in
//! damage_id").

use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSmoking;

impl SkillImpl for SkillSmoking {
    fn skill_id(&self) -> u16 {
        NPC_SMOKING
    }

    /// SkillSmoking::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
    }
}
