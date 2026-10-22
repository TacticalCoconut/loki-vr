//! skills/swordman/vitalstrike.cpp: class SkillVitalStrike : public SkillImpl (LK_JOINTBEAT).

use crate::battle::calc::Damage;
use crate::battle::BF_WEAPON;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_JOINTBEAT;
use crate::status::defs::{BREAK_ANKLE, BREAK_KNEE, BREAK_NECK, BREAK_SHOULDER, BREAK_WAIST, BREAK_WRIST, SCSTART_NONE};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// status.hpp e_joint_break BREAK_FLAGS.
pub const BREAK_FLAGS: i32 = BREAK_ANKLE | BREAK_WRIST | BREAK_KNEE | BREAK_SHOULDER | BREAK_WAIST | BREAK_NECK;

pub struct SkillVitalStrike;

impl SkillImpl for SkillVitalStrike {
    fn skill_id(&self) -> u16 {
        LK_JOINTBEAT
    }

    /// SkillVitalStrike::calculateSkillRatio: the 2x damage is only for the BREAK_NECK ailment.
    fn calculate_skill_ratio(&self, c: &Ctx, wd: &Damage, _src: &Unit, target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 10 * skill_lv as i32 - 50;
        if wd.miscflag & BREAK_NECK != 0 || sce(c, target, SC_JOINTBEAT).map(|e| e.val2 & BREAK_NECK != 0).unwrap_or(false) {
            *base_skillratio *= 2;
        }
    }

    /// SkillVitalStrike::castendDamageId: a random joint (the break becomes the caller's `flag`, as in rAthena — the
    /// pipeline then reads it: BREAK_ANKLE sets bit 1, BREAK_NECK is SKILL_NOCONSUME_REQ's bit).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        *flag = 1 << (c.rnd() % 6);
        if *flag != BREAK_NECK && sce(c, target, SC_JOINTBEAT).map(|e| e.val2 & BREAK_NECK != 0).unwrap_or(false) {
            *flag = BREAK_NECK; // Target should always receive double damage if neck is already broken
        }
        if skill_attack(c, BF_WEAPON, src, src, target, id, skill_lv, tick, *flag) != 0 {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            // (tstatus is the target's live status: its STR as it is after the hit)
            let tstr = crate::status::status_get_status_data(c, &t).str as i32;
            let rate = (50 * (skill_lv as i32 + 1) - (270 * tstr) / 100) * 10;
            sc::status_change_start(c, Some(&s), &t, SC_JOINTBEAT, rate, skill_lv as i32, *flag & BREAK_FLAGS, src.id as i32, 0, skill_get_time2(id, skill_lv) as i64, SCSTART_NONE, 0);
        }
    }
}
