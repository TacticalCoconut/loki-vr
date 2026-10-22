//! skills/thief/createdeadlypoison.cpp: class SkillCreateDeadlyPoison : public SkillImpl (ASC_CDP).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::fail::USESKILL_FAIL_STUFF_INSUFFICIENT;
use crate::skill::clif::{clif_skill_fail, clif_skill_nodamage};
use crate::skill::condition::ITEMID_POISON_BOTTLE;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillCreateDeadlyPoison;

impl SkillImpl for SkillCreateDeadlyPoison {
    fn skill_id(&self) -> u16 {
        ASC_CDP
    }

    /// SkillCreateDeadlyPoison::castendNoDamageId: Produce a Poison Bottle.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind != BL_PC {
            return;
        }
        let id = self.skill_id();
        if crate::itemui::produce::skill_produce_mix(c, src.id, id, ITEMID_POISON_BOTTLE, 0, 0, 0, 1, -1) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
        } else {
            clif_skill_fail(c, src, id, USESKILL_FAIL_STUFF_INSUFFICIENT, 0, 0);
        }
    }
}
