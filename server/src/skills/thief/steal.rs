//! skills/thief/steal.cpp: class SkillSteal : public SkillImpl (TF_STEAL).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::fail::USESKILL_FAIL;
use crate::skill::clif::{clif_skill_fail, clif_skill_nodamage};
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillSteal;

impl SkillImpl for SkillSteal {
    fn skill_id(&self) -> u16 {
        TF_STEAL
    }

    /// SkillSteal::castendNoDamageId: pc_steal_item; the animation on success, USESKILL_FAIL otherwise.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, bl: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind != BL_PC {
            return;
        }
        let id = self.skill_id();
        if crate::skill::misc::pc_steal_item(c, src, bl, skill_lv as i32) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let b = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
            clif_skill_nodamage(c, Some(&s), &b, id, skill_lv as i32, true);
        } else {
            clif_skill_fail(c, src, id, USESKILL_FAIL, 0, 0);
        }
    }
}
