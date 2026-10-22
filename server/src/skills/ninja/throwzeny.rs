//! skills/ninja/throwzeny.cpp: class SkillThrowZeny : public SkillImpl (NJ_ZENYNAGE), rAthena e985006.

use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillThrowZeny;

impl SkillImpl for SkillThrowZeny {
    fn skill_id(&self) -> u16 {
        NJ_ZENYNAGE
    }

    /// SkillThrowZeny::castendDamageId: a hit of the skill's own type (the damage is battle.cpp's misc attack).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
    }
}
