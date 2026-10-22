//! skills/mage/whiteimprison.cpp: class SkillWhiteImprison : public SkillImpl (WL_WHITEIMPRISON), rAthena e985006.
//! The skill's status (skill_get_sc: SC_WHITEIMPRISON) on the caster itself or an enemy that is not a boss.

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, fail::USESKILL_FAIL_TOTARGET, skill_fail, skill_fail_cause};
use crate::skill::cooldown::skill_blockpc_start;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::defs::CLASS_BOSS;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillWhiteImprison;

impl SkillImpl for SkillWhiteImprison {
    fn skill_id(&self) -> u16 {
        WL_WHITEIMPRISON
    }

    /// SkillWhiteImprison::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        // map_session_data* sd = BL_CAST(BL_PC, src)
        let is_pc = src.kind == BL_PC;
        let on_self = src.id == target.id;
        // Should not work with Bosses.
        if (on_self || crate::battle::target::battle_check_target(c, src, target, BCT_ENEMY) > 0)
            && crate::battle::bl::class_type(c, target) != CLASS_BOSS
            && sc::status_isimmune(c, target) == 0
        {
            // ( sd? sd->status.job_level : 50 ) / 4
            let job_level = if is_pc { crate::pc::character_of(c, src.id).map(|ch| ch.job_level as i32).unwrap_or(0) } else { 50 };
            let mut rate = job_level / 4;
            if on_self {
                rate = 100; // Success Chance: On self, 100%
            } else if target.kind == BL_PC {
                rate += 20 + 10 * skill_lv as i32; // On Players, (20 + 10 * Skill Level) %
            } else {
                rate += 40 + 10 * skill_lv as i32; // On Monsters, (40 + 10 * Skill Level) %
            }
            if is_pc {
                skill_blockpc_start(c, src.id, id, 4000);
            }
            // !(tsc && tsc->getSCE(type))
            if sce(c, target, type_).is_none() {
                let duration = if on_self {
                    5000
                } else if target.kind == BL_PC {
                    skill_get_time(id, skill_lv)
                } else {
                    skill_get_time2(id, skill_lv)
                };
                let i = sc::sc_start2(c, Some(src), target, type_, rate, skill_lv as i32, src.id as i32, duration as i64);
                let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, i);
                if is_pc && !i {
                    skill_fail(c, &s, id);
                }
            }
        } else if is_pc {
            skill_fail_cause(c, src, id, USESKILL_FAIL_TOTARGET);
        }
    }
}
