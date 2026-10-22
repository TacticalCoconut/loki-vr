//! skills/elemental/eyesofstorm.cpp: class SkillEyesOfStorm : public SkillImpl (EM_EL_EYES_OF_STORM), rAthena e985006.

use super::{option_castend_nodamage_id, OptionAnim};
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillEyesOfStorm;

impl SkillImpl for SkillEyesOfStorm {
    fn skill_id(&self) -> u16 {
        EM_EL_EYES_OF_STORM
    }

    /// SkillEyesOfStorm::castendNoDamageId: the elemental's option skill (no dummy damage packet) (see [`option_castend_nodamage_id`]).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        option_castend_nodamage_id(c, self.skill_id(), src, target, skill_lv, tick, OptionAnim::None);
    }
}
