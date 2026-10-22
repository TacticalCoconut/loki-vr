//! skills/thief/hallucinationwalk.cpp: class SkillHallucinationWalk : public StatusSkillImpl (GC_HALLUCINATIONWALK).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_fail;
use crate::skill::clif::fail::USESKILL_FAIL_HP_INSUFFICIENT;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillHallucinationWalk;

impl SkillImpl for SkillHallucinationWalk {
    fn skill_id(&self) -> u16 {
        GC_HALLUCINATIONWALK
    }

    /// SkillHallucinationWalk::castendNoDamageId: costs a tenth of the target's max HP (the skill fails when the HP
    /// is not enough), then StatusSkillImpl::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let is_sd = src.kind == BL_PC;
        let tstatus = crate::status::status_get_status_data(c, target);
        let heal = tstatus.max_hp as i32 / 10;
        if (tstatus.hp as i32) < heal {
            // if you haven't enough HP skill fails.
            if is_sd {
                clif_skill_fail(c, src, id, USESKILL_FAIL_HP_INSUFFICIENT, 0, 0);
            }
            return;
        }
        if crate::status::hp::status_charge(c, target.id, heal as i64, 0) == 0 {
            if is_sd {
                clif_skill_fail(c, src, id, USESKILL_FAIL_HP_INSUFFICIENT, 0, 0);
            }
            return;
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        status_castend_nodamage_id(c, id, false, &s, &t, skill_lv);
    }
}
