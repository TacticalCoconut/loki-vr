//! skills/mage/soulexpansion.cpp: class SkillSoulExpansion : public SkillImplRecursiveDamageSplash (WL_SOULEXPANSION),
//! rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::Ctx;

pub struct SkillSoulExpansion;

impl SkillImpl for SkillSoulExpansion {
    fn skill_id(&self) -> u16 {
        WL_SOULEXPANSION
    }

    crate::splash_skill_defaults!();

    /// SkillSoulExpansion::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 1000 + skill_lv as i32 * 200;
        *skillratio += sstatus.int_ as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillSoulExpansion {}
