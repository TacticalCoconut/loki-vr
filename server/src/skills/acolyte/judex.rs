//! skills/acolyte/judex.cpp: class SkillJudex : public SkillImplRecursiveDamageSplash (AB_JUDEX).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::Ctx;

pub struct SkillJudex;

impl SkillImpl for SkillJudex {
    fn skill_id(&self) -> u16 {
        AB_JUDEX
    }

    crate::splash_skill_defaults!();

    /// SkillJudex::calculateSkillRatio: 300% + 70% per level (RE_LVL_DMOD 100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 300 + 70 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillJudex {}
