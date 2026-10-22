//! skills/mage/stonecurse.cpp: class SkillStoneCurse : public SkillImpl (MG_STONECURSE).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_PETROLOGY_OPTION;
use crate::status::defs::{MD_STATUSIMMUNE, SCSTART_NONE};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillStoneCurse;

impl SkillImpl for SkillStoneCurse {
    fn skill_id(&self) -> u16 {
        MG_STONECURSE
    }

    /// SkillStoneCurse::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let sd = src.kind == BL_PC; // map_session_data *sd = BL_CAST(BL_PC, src)
        let tstatus = crate::status::status_get_status_data(c, target);
        let type_ = skill_get_sc(id) as i32;

        if crate::battle::mobif::has_mode(tstatus.mode, MD_STATUSIMMUNE) {
            if sd {
                skill_fail(c, src, id);
            }
            return;
        }
        // status_isimmune(target) || !tsc
        if sc::status_isimmune(c, target) != 0 || !crate::battle::mobif::has_sc(target.kind) {
            return;
        }

        let mut brate = 0;
        if sd {
            if let Some(e) = sce(c, src, SC_PETROLOGY_OPTION) {
                brate = e.val3;
            }
        }

        let lv = skill_lv as i32;
        // Except for players, the skill animation shows even if the status change doesn't start
        // Players get a skill has failed message instead
        // sc_start2(src, target, type, rate, skill_lv, src->id, skill_get_time2(...), delay = skill_get_time(...))
        let started = sc::status_change_start(c, Some(src), target, type_, 100 * ((lv * 4 + 20) + brate), lv, src.id as i32, 0, 0, skill_get_time2(id, skill_lv) as i64, SCSTART_NONE, skill_get_time(id, skill_lv));
        if started || !sd {
            let target = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(src), &target, id, lv, true);
        } else {
            skill_fail(c, src, id);
            // Level 6-10 doesn't consume a red gem if it fails [celest]
            if skill_lv > 5 {
                // not to consume items
                *flag |= SKILL_NOCONSUME_REQ;
            }
        }
    }
}
