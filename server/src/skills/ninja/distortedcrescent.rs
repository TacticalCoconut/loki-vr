//! skills/ninja/distortedcrescent.cpp: class SkillDistortedCrescent : public StatusSkillImpl (OB_ZANGETSU), rAthena
//! e985006.

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDistortedCrescent;

impl SkillImpl for SkillDistortedCrescent {
    fn skill_id(&self) -> u16 {
        OB_ZANGETSU
    }

    /// SkillDistortedCrescent::castendNoDamageId: StatusSkillImpl's start, then the damage-less animation.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        super::status_then_damage_animation(c, self.skill_id(), src, target, skill_lv, tick);
    }
}
