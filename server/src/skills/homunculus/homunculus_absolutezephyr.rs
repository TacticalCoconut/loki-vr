//! skills/homunculus/homunculus_absolutezephyr.cpp: class SkillAbsoluteZephyr : public SkillImplRecursiveDamageSplash
//! (MH_ABSOLUTE_ZEPHYR).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::Ctx;

pub struct SkillAbsoluteZephyr;

impl SkillImpl for SkillAbsoluteZephyr {
    fn skill_id(&self) -> u16 {
        MH_ABSOLUTE_ZEPHYR
    }

    crate::splash_skill_defaults!();

    /// SkillAbsoluteZephyr::calculateSkillRatio: -100 + 1000 + 450 * lv * base level / 100 + INT (!TODO upstream:
    /// confirm the base level and INT bonus).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 1000 + 450 * skill_lv as i32 * crate::status::status_get_lv(c, src) / 100 + sstatus.int_ as i32;
    }
}

impl RecursiveDamageSplash for SkillAbsoluteZephyr {}
