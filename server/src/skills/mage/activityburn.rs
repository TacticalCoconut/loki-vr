//! skills/mage/activityburn.cpp: class SkillActivityBurn : public SkillImpl (EM_ACTIVITY_BURN), rAthena e985006.

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_fail, clif_skill_nodamage, fail::USESKILL_FAIL};
use crate::skills::SkillImpl;
use crate::status::hp::status_zap;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillActivityBurn;

impl SkillImpl for SkillActivityBurn {
    fn skill_id(&self) -> u16 {
        EM_ACTIVITY_BURN
    }

    /// SkillActivityBurn::castendNoDamageId: burns the AP of a player target (20 + 10 * level % chance).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        // map_session_data* sd = BL_CAST(BL_PC, src);
        let is_pc = src.kind == BL_PC;

        // `target->type == BL_PC && rnd() % 100 < …`: the roll only for a player target
        if target.kind == BL_PC && ((c.rnd() % 100) as i32) < 20 + 10 * skill_lv as i32 {
            let ap_burn: [u8; 5] = [20, 30, 50, 60, 70];
            clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
            // ap_burn[skill_lv - 1] (the level is the skill's own, 1..5; out of the table it burns nothing here instead
            // of reading past the array)
            let ap = (skill_lv as usize).checked_sub(1).and_then(|i| ap_burn.get(i)).copied().unwrap_or(0);
            status_zap(c, target.id, 0, 0, ap as i64);
        } else if is_pc {
            clif_skill_fail(c, src, self.skill_id(), USESKILL_FAIL, 0, 0);
        }
    }
}
