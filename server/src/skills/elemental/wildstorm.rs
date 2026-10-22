//! skills/elemental/wildstorm.cpp: class SkillWildStorm : public SkillImpl (EL_WILD_STORM), rAthena e985006.

use super::{option_castend_nodamage_id, OptionAnim};
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWildStorm;

impl SkillImpl for SkillWildStorm {
    fn skill_id(&self) -> u16 {
        EL_WILD_STORM
    }

    /// SkillWildStorm::castendNoDamageId: the elemental's option skill, with the dummy damage packet on the elemental itself (see [`option_castend_nodamage_id`]).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        option_castend_nodamage_id(c, self.skill_id(), src, target, skill_lv, tick, OptionAnim::Caster);
    }
}
