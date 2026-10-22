//! skills/elemental/coolair.cpp: class SkillCoolAir : public SkillImpl (EL_CHILLY_AIR), rAthena e985006.

use super::{option_castend_nodamage_id, OptionAnim};
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillCoolAir;

impl SkillImpl for SkillCoolAir {
    fn skill_id(&self) -> u16 {
        EL_CHILLY_AIR
    }

    /// SkillCoolAir::castendNoDamageId: the elemental's option skill, with the dummy damage packet on the target (see [`option_castend_nodamage_id`]).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        option_castend_nodamage_id(c, self.skill_id(), src, target, skill_lv, tick, OptionAnim::Target);
    }
}
