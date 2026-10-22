//! skills/ninja/shadowwarrior.cpp: class SkillShadowWarrior : public StatusSkillImpl (KG_KAGEMUSYA), rAthena e985006.

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillShadowWarrior;

impl SkillImpl for SkillShadowWarrior {
    fn skill_id(&self) -> u16 {
        KG_KAGEMUSYA
    }

    /// SkillShadowWarrior::castendNoDamageId: StatusSkillImpl's start, then the damage-less animation.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        super::status_then_damage_animation(c, self.skill_id(), src, target, skill_lv, tick);
    }
}
