//! skills/taekwon/sunsetblast.cpp: class SkillSunsetBlast : public SkillImplRecursiveDamageSplash (SKE_SUNSET_BLAST).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSunsetBlast;

impl SkillImpl for SkillSunsetBlast {
    fn skill_id(&self) -> u16 {
        SKE_SUNSET_BLAST
    }

    crate::splash_skill_defaults!();

    /// SkillSunsetBlast::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 1200 + 500 * lv;
        *skillratio += pc_checkskill(c, src, SKE_SKY_MASTERY) * 5 * lv;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillSunsetBlast {
    /// SkillSunsetBlast::splashSearch.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
