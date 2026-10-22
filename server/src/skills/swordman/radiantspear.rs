//! skills/swordman/radiantspear.cpp: class SkillRadiantSpear : public SkillImplRecursiveDamageSplash (IG_RADIANT_SPEAR).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_SPEAR_SCAR;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRadiantSpear;

impl SkillImpl for SkillRadiantSpear {
    fn skill_id(&self) -> u16 {
        IG_RADIANT_SPEAR
    }

    crate::splash_skill_defaults!();

    /// SkillRadiantSpear::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 3500 + 1150 * lv;
        *skillratio += pc_checkskill(c, src, IG_SPEAR_SWORD_M) * 50;
        *skillratio += 5 * sstatus.pow as i32; // !TODO: check POW ratio
        if sce(c, src, SC_SPEAR_SCAR).is_some() {
            *skillratio += 250 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillRadiantSpear {
    /// SkillRadiantSpear::splashSearch.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
