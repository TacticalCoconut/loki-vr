//! skills/merchant/vulcanarm.cpp: class SkillVulcanArm : public SkillImplRecursiveDamageSplash (NC_VULCANARM).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_ABR_DUAL_CANNON;
use crate::status::status_get_status_data;
use crate::unit::Unit;
use crate::world::Ctx;

/// SkillVulcanArm::SkillVulcanArm() : SkillImplRecursiveDamageSplash(NC_VULCANARM).
pub struct SkillVulcanArm;

impl SkillImpl for SkillVulcanArm {
    fn skill_id(&self) -> u16 {
        NC_VULCANARM
    }
    crate::splash_skill_defaults!();

    /// modifyDamageData: `dmg.div_ = 2` while the caster has SC_ABR_DUAL_CANNON.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if crate::skills::novice::sce(c, src, SC_ABR_DUAL_CANNON).is_some() {
            dmg.div_ = 2;
        }
    }

    /// SkillVulcanArm::calculateSkillRatio: 230% per level + DEX ("!TODO: What's the DEX bonus?"), RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = status_get_status_data(c, src);
        *skillratio += -100 + 230 * skill_lv as i32 + sstatus.dex as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillVulcanArm {}
