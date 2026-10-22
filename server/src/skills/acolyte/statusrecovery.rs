//! skills/acolyte/statusrecovery.cpp: class SkillStatusRecovery : public SkillImpl (PR_STRECOVERY).

use crate::db::skills::*;
use crate::map::BL_MOB;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::status::sc::{self, battle_check_undead};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillStatusRecovery;

impl SkillImpl for SkillStatusRecovery {
    fn skill_id(&self) -> u16 {
        PR_STRECOVERY
    }

    /// SkillStatusRecovery::castendNoDamageId: undead targets are blinded one second later (skill_timerskill's
    /// PR_STRECOVERY arm: SC_BLIND at rate skl->type = 100); others have their body state reset to normal (a
    /// monster's AI goes idle).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let tstatus = crate::status::status_get_status_data(c, target);
        let has_tsc = crate::battle::mobif::has_sc(target.kind); // status_get_sc(target) != nullptr
        let dstmd = target.kind == BL_MOB;
        if sc::status_isimmune(c, target) != 0 {
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, false);
            return;
        }
        if battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) {
            skill_addtimerskill(c, src, tick + 1000, target.id, 0, 0, id, skill_lv, 100, *flag);
        } else {
            // Bodystate is reset to "normal" for non-undead
            if has_tsc {
                // The following are bodystate status changes
                for t in [SC_STONE, SC_FREEZE, SC_STUN, SC_SLEEP, SC_STONEWAIT, SC_BURNING, SC_WHITEIMPRISON] {
                    sc::status_change_end(c, target.id, t, 0);
                }
            }
            // Resetting bodystate to normal always also resets the monster AI to idle
            if dstmd {
                crate::mob::ai::mob_unlocktarget(c, target.id, tick);
            }
        }
        if has_tsc {
            // Ends SC_NETHERWORLD and SC_NORECOVER_STATE (even on undead)
            sc::status_change_end(c, target.id, SC_NETHERWORLD, 0);
            sc::status_change_end(c, target.id, SC_NORECOVER_STATE, 0);
        }
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
    }
}
