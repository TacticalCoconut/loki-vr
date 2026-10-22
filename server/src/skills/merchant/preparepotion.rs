//! skills/merchant/preparepotion.cpp: class SkillPreparePotion : public SkillImpl (AM_PHARMACY).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillPreparePotion::SkillPreparePotion() : SkillImpl(AM_PHARMACY).
pub struct SkillPreparePotion;

impl SkillImpl for SkillPreparePotion {
    fn skill_id(&self) -> u16 {
        AM_PHARMACY
    }

    /// SkillPreparePotion::castendNoDamageId: the produce list (trigger 22), then the skill packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind == BL_PC {
            crate::itemui::produce::clif_skill_produce_mix_list(c, src.id, id as i32, 22);
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        }
    }
}
