//! skills/merchant/twilightalchemy1.cpp: class SkillTwilightAlchemy1 : public SkillImpl (AM_TWILIGHT1).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillTwilightAlchemy1::SkillTwilightAlchemy1() : SkillImpl(AM_TWILIGHT1).
pub struct SkillTwilightAlchemy1;

impl SkillImpl for SkillTwilightAlchemy1 {
    fn skill_id(&self) -> u16 {
        AM_TWILIGHT1
    }

    /// SkillTwilightAlchemy1::castendNoDamageId: "Prepare 200 White Potions" through skill_produce_mix (qty 200, produce_idx -1); it
    /// fails with clif_skill_fail when nothing could be made.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != BL_PC {
            return;
        }
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        if !crate::itemui::produce::skill_produce_mix(c, src.id, id, super::ids::ITEMID_WHITE_POTION, 0, 0, 0, 200, -1) {
            if let Some(s) = crate::unit::get(c, src.id) {
                skill_fail(c, &s, id);
            }
        }
    }
}
