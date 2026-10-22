//! skills/elemental/cooler.cpp: class SkillCooler : public SkillImpl (EL_COOLER), rAthena e985006.

use super::{option_castend_nodamage_id, OptionAnim};
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillCooler;

impl SkillImpl for SkillCooler {
    fn skill_id(&self) -> u16 {
        EL_COOLER
    }

    /// SkillCooler::castendNoDamageId: the elemental's option skill, with the dummy damage packet on the target (see [`option_castend_nodamage_id`]).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        option_castend_nodamage_id(c, self.skill_id(), src, target, skill_lv, tick, OptionAnim::Target);
    }
}
