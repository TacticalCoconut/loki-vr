//! skills/mage/jackfrost.cpp: class SkillJackFrost : public SkillImplRecursiveDamageSplash (WL_JACKFROST), rAthena
//! e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_MISTY_FROST;
use crate::unit::Unit;
use crate::world::Ctx;

pub struct SkillJackFrost;

impl SkillImpl for SkillJackFrost {
    fn skill_id(&self) -> u16 {
        WL_JACKFROST
    }

    crate::splash_skill_defaults!();

    /// SkillJackFrost::calculateSkillRatio: more against a target under Misty Frost.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        if sce(c, target, SC_MISTY_FROST).is_some() {
            *skillratio += -100 + 1200 + 600 * skill_lv as i32;
        } else {
            *skillratio += -100 + 1000 + 300 * skill_lv as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillJackFrost {}
