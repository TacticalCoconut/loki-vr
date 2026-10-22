//! skills/mage/fourspiritanalysis.cpp: class SkillFourSpiritAnalysis : public SkillImpl (SO_EL_ANALYSIS).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_itemlistwindow, clif_skill_nodamage};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFourSpiritAnalysis;

impl SkillImpl for SkillFourSpiritAnalysis {
    fn skill_id(&self) -> u16 {
        SO_EL_ANALYSIS
    }

    /// SkillFourSpiritAnalysis::castendNoDamageId: for a player, the packet and the item list window (0x07e3 with the
    /// skill level; the answer 0x07e4 runs skill_elementalanalysis).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind == BL_PC {
            let id = self.skill_id();
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            clif_skill_itemlistwindow(c, src, id, skill_lv);
        }
    }
}
