//! skills/swordman/hundredspear.cpp: class SkillHundredSpear : public SkillImplRecursiveDamageSplash (RK_HUNDREDSPEAR).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_CHARGINGPIERCE_COUNT, SC_DRAGONIC_AURA};
use crate::unit::Unit;
use crate::world::Ctx;

pub struct SkillHundredSpear;

impl SkillImpl for SkillHundredSpear {
    fn skill_id(&self) -> u16 {
        RK_HUNDREDSPEAR
    }

    crate::splash_skill_defaults!();

    /// SkillHundredSpear::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 600 + 200 * skill_lv as i32;
        // if (sd)
        *skillratio += 50 * pc_checkskill(c, src, LK_SPIRALPIERCE);
        if let Some(e) = sce(c, src, SC_DRAGONIC_AURA) {
            *skillratio += e.val1 * 160;
        }
        if sce(c, src, SC_CHARGINGPIERCE_COUNT).map(|e| e.val1 >= 10).unwrap_or(false) {
            *skillratio *= 2;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillHundredSpear {}
