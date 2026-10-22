//! skills/mage/crimsonrock.cpp: class SkillCrimsonRock : public SkillImplRecursiveDamageSplash (WL_CRIMSONROCK),
//! rAthena e985006. The splash centre (skill_area_temp[4] / [5]) is the target's cell: skill_attack's knockback of
//! WL_CRIMSONROCK pushes away from it (skill/attack.rs).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::area;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillCrimsonRock;

impl SkillImpl for SkillCrimsonRock {
    fn skill_id(&self) -> u16 {
        WL_CRIMSONROCK
    }

    crate::splash_skill_defaults!();

    /// SkillCrimsonRock::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 700 + 600 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillCrimsonRock {
    /// SkillCrimsonRock::splashSearch: remembers the target's cell, then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        area::set_temp(4, target.x as i32);
        area::set_temp(5, target.y as i32);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
