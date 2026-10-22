//! skills/mage/napalmvulcan.cpp: class SkillNapalmVulcan : public SkillImplRecursiveDamageSplash (HW_NAPALMVULCAN).
//! castendDamageId / castendPos2 are inherited (splash_skill_defaults).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_CURSE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillNapalmVulcan;

impl SkillImpl for SkillNapalmVulcan {
    fn skill_id(&self) -> u16 {
        HW_NAPALMVULCAN
    }

    /// SkillNapalmVulcan::calculateSkillRatio (RENEWAL branch).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 70 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillNapalmVulcan::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_CURSE, 5 * skill_lv as i32, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    crate::splash_skill_defaults!();
}

impl RecursiveDamageSplash for SkillNapalmVulcan {}
