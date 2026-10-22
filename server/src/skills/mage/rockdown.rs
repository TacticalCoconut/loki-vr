//! skills/mage/rockdown.cpp: class SkillRockDown : public SkillImplRecursiveDamageSplash (AG_ROCK_DOWN), rAthena
//! e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_CLIMAX;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRockDown;

impl SkillImpl for SkillRockDown {
    fn skill_id(&self) -> u16 {
        AG_ROCK_DOWN
    }

    crate::splash_skill_defaults!();

    /// SkillRockDown::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;

        *skillratio += -100 + 1550 * lv + 5 * sstatus.spl as i32;

        if sce(c, src, SC_CLIMAX).is_some() {
            *skillratio += 300 * lv;
        }

        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillRockDown {
    /// SkillRockDown::splashSearch: the animation on the target, then SkillImplRecursiveDamageSplash::splashSearch.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);

        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
