//! skills/mage/soulvulcanstrike.cpp: class SkillSoulVulcanStrike : public SkillImplRecursiveDamageSplash
//! (AG_SOUL_VC_STRIKE), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::Ctx;

pub struct SkillSoulVulcanStrike;

impl SkillImpl for SkillSoulVulcanStrike {
    fn skill_id(&self) -> u16 {
        AG_SOUL_VC_STRIKE
    }

    crate::splash_skill_defaults!();

    /// SkillSoulVulcanStrike::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);

        *skillratio += -100 + 50 + 330 * skill_lv as i32;
        *skillratio += 3 * sstatus.spl as i32;

        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillSoulVulcanStrike {}
