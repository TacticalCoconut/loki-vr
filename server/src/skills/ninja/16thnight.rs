//! skills/ninja/16thnight.cpp: class Skill16thNight : public StatusSkillImpl (KO_IZAYOI), rAthena e985006.

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct Skill16thNight;

impl SkillImpl for Skill16thNight {
    fn skill_id(&self) -> u16 {
        KO_IZAYOI
    }

    /// Skill16thNight::castendNoDamageId: StatusSkillImpl's start, then the damage-less animation.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        super::status_then_damage_animation(c, self.skill_id(), src, target, skill_lv, tick);
    }
}
