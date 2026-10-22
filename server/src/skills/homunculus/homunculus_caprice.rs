//! skills/homunculus/homunculus_caprice.cpp: class SkillCaprice : public SkillImpl (HVAN_CAPRICE).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// The bolts Caprice casts (`subskills`).
const SUBSKILLS: [u16; 4] = [MG_COLDBOLT, MG_FIREBOLT, MG_LIGHTNINGBOLT, WZ_EARTHSPIKE];

pub struct SkillCaprice;

impl SkillImpl for SkillCaprice {
    fn skill_id(&self) -> u16 {
        HVAN_CAPRICE
    }

    /// SkillCaprice::castendDamageId: a random bolt (`subskills.at(rnd() % subskills.size())`) at Caprice's level.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let subskill_id = SUBSKILLS[c.rnd() as usize % SUBSKILLS.len()];
        crate::skill::attack::skill_attack(c, skill_get_type(subskill_id), src, src, target, subskill_id, skill_lv, tick, *flag);
    }
}
