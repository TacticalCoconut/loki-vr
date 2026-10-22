//! skills/archer/deepblindtrap.cpp: class SkillDeepBlindTrap : public SkillImpl (WH_DEEPBLINDTRAP). A Windhawk trap; the unit is skillunit's.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDeepBlindTrap;

impl SkillImpl for SkillDeepBlindTrap {
    fn skill_id(&self) -> u16 {
        WH_DEEPBLINDTRAP
    }

    /// SkillDeepBlindTrap::calculateSkillRatio: -100 + 850 * lv + 5 * CON, RE_LVL_DMOD(100), then + 20% per Advanced Trap level
    /// (5 levels for a non-player).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 850 * skill_lv as i32 + 5 * sstatus.con as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        let adv = if src.kind == crate::map::BL_PC { pc_checkskill(c, src, WH_ADVANCED_TRAP) } else { 5 };
        *skillratio += *skillratio * (20 * adv) / 100;
    }

    /// SkillDeepBlindTrap::castendPos2: `flag |= 1` (the ammo is deleted with the group), skill_unitsetting.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        super::unit_castend_pos2(c, self.skill_id(), src, x, y, skill_lv, flag);
    }
}
