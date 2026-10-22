//! skills/mage/increasingactivity.cpp: class SkillIncreasingActivity : public SkillImpl (EM_INCREASING_ACTIVITY),
//! rAthena e985006.

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_fail, clif_skill_nodamage, fail::USESKILL_FAIL};
use crate::skills::SkillImpl;
use crate::status::hp::status_heal;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillIncreasingActivity;

impl SkillImpl for SkillIncreasingActivity {
    fn skill_id(&self) -> u16 {
        EM_INCREASING_ACTIVITY
    }

    /// SkillIncreasingActivity::castendNoDamageId: gives a player target 10 * level AP.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        // map_session_data* sd = BL_CAST(BL_PC, src);
        let is_pc = src.kind == BL_PC;

        if target.kind == BL_PC {
            clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
            status_heal(c, target.id, 0, 0, 10 * skill_lv as i64, 0);
        } else if is_pc {
            clif_skill_fail(c, src, self.skill_id(), USESKILL_FAIL, 0, 0);
        }
    }
}
