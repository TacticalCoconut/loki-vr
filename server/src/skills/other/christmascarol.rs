//! skills/other/christmascarol.cpp: class SkillChristmasCarol : public SkillImpl (ALL_WEWISH).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillChristmasCarol;

impl SkillImpl for SkillChristmasCarol {
    fn skill_id(&self) -> u16 {
        ALL_WEWISH
    }

    /// SkillChristmasCarol::castendNoDamageId: the packet only.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
    }
}
