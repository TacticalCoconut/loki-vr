//! skills/other/catcry.cpp: class SkillCatCry : public SkillImpl (ALL_CATCRY).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillCatCry;

impl SkillImpl for SkillCatCry {
    fn skill_id(&self) -> u16 {
        ALL_CATCRY
    }

    /// SkillCatCry::castendNoDamageId: the packet only.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
    }
}
