//! skills/archer/makingarrow.cpp: class SkillMakingArrow : public SkillImpl (AC_MAKINGARROW). The list and the
//! crafting (clif_arrow_create_list, clif_parse_SelectArrow, skill_arrow_create) are itemui's (itemui/produce.rs).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMakingArrow;

impl SkillImpl for SkillMakingArrow {
    fn skill_id(&self) -> u16 {
        AC_MAKINGARROW
    }

    /// SkillMakingArrow::castendNoDamageId: a player gets the list of arrows it can make, then the no-damage packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind == crate::map::BL_PC {
            crate::itemui::produce::clif_arrow_create_list(c, src.id);
            clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        }
    }
}
