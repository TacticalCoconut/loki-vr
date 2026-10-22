//! skills/homunculus/homunculus_holypole.cpp: class SkillHolyPole : public SkillImplRecursiveDamageSplash
//! (MH_HEILIGE_STANGE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::Ctx;

pub struct SkillHolyPole;

impl SkillImpl for SkillHolyPole {
    fn skill_id(&self) -> u16 {
        MH_HEILIGE_STANGE
    }

    crate::splash_skill_defaults!();

    /// SkillHolyPole::calculateSkillRatio: -100 + 1500 + 250 * lv * base level / 150 + VIT (!TODO upstream: confirm
    /// the VIT bonus).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 1500 + 250 * skill_lv as i32 * crate::status::status_get_lv(c, src) / 150 + sstatus.vit as i32;
    }
}

impl RecursiveDamageSplash for SkillHolyPole {}
