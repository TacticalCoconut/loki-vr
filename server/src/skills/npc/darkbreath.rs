//! skills/npc/darkbreath.cpp: class SkillDarkBreath : public SkillImpl (NPC_DARKBREATH), rAthena e985006.

use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// clif.hpp enum emotion_type ET_ANGER.
pub const ET_ANGER: u8 = 7;

pub struct SkillDarkBreath;

impl SkillImpl for SkillDarkBreath {
    fn skill_id(&self) -> u16 {
        NPC_DARKBREATH
    }

    /// SkillDarkBreath::castendDamageId: the anger emotion, then a 50% chance of the hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        crate::clif::emotion(c, src, ET_ANGER);
        if c.rnd() % 2 == 0 {
            return; // 50% chance
        }
        skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
    }
}

#[cfg(test)]
mod tests {
    #[test]
    fn constants_match_script_constants() {
        assert_eq!(crate::data::constant("ET_ANGER"), Some(super::ET_ANGER as i64));
    }
}
