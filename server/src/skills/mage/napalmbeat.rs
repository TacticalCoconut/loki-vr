//! skills/mage/napalmbeat.cpp: class SkillNapalmBeat : public SkillImplRecursiveDamageSplash (MG_NAPALMBEAT).
//! Its castendDamageId override only calls SkillImplRecursiveDamageSplash::castendDamageId (splash_skill_defaults).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::Ctx;

pub struct SkillNapalmBeat;

impl SkillImpl for SkillNapalmBeat {
    fn skill_id(&self) -> u16 {
        MG_NAPALMBEAT
    }

    /// SkillNapalmBeat::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -30 + 10 * skill_lv as i32;
    }

    // SkillNapalmBeat::castendDamageId = SkillImplRecursiveDamageSplash::castendDamageId; castendPos2 inherited.
    crate::splash_skill_defaults!();
}

impl RecursiveDamageSplash for SkillNapalmBeat {}
