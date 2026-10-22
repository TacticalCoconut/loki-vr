//! skills/archer/arrowstorm.cpp: class SkillArrowStorm : public SkillImplRecursiveDamageSplash (RA_ARROWSTORM).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_CAMOUFLAGE, SC_FEARBREEZE};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillArrowStorm;

impl SkillImpl for SkillArrowStorm {
    fn skill_id(&self) -> u16 {
        RA_ARROWSTORM
    }

    crate::splash_skill_defaults!();

    /// SkillArrowStorm::calculateSkillRatio: under Fear Breeze -100 + 200 + 250 * lv, else -100 + 200 + 180 * lv;
    /// RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        if sce(c, src, SC_FEARBREEZE).is_some() {
            *skillratio += -100 + 200 + 250 * skill_lv as i32;
        } else {
            *skillratio += -100 + 200 + 180 * skill_lv as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillArrowStorm {
    /// SkillArrowStorm::splashSearch: the parent's search, then Camouflage ends.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
        sc::status_change_end(c, src.id, SC_CAMOUFLAGE, 0);
    }
}
