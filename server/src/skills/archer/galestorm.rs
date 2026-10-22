//! skills/archer/galestorm.cpp: class SkillGaleStorm : public SkillImplRecursiveDamageSplash (WH_GALESTORM).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::skill::area::{foreach_in_range, skill_area_sub, skill_area_sub_count, Wall};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_CALAMITYGALE;
use crate::status::defs::{RC_BRUTE, RC_FISH};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGaleStorm;

impl SkillImpl for SkillGaleStorm {
    fn skill_id(&self) -> u16 {
        WH_GALESTORM
    }

    crate::splash_skill_defaults!();

    /// SkillGaleStorm::calculateSkillRatio: -100 + 1350 * lv + 10 * CON, RE_LVL_DMOD(100), + 50% under Calamity Gale
    /// against brutes and fish.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let tstatus = crate::status::status_get_status_data(c, target);
        *skillratio += -100 + 1350 * skill_lv as i32;
        *skillratio += 10 * sstatus.con as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if sce(c, src, SC_CALAMITYGALE).is_some() && (tstatus.race as i32 == RC_BRUTE || tstatus.race as i32 == RC_FISH) {
            *skillratio += *skillratio * 50 / 100;
        }
    }
}

impl RecursiveDamageSplash for SkillGaleStorm {
    /// SkillGaleStorm::splashSearch: a player hitting 3 or more enemies around the target gains 10 AP; the no-damage
    /// packet; then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        let id = self.skill_id();
        // Give AP if 3 or more targets are hit.
        if src.kind == crate::map::BL_PC
            && foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, crate::map::BL_CHAR, Wall::None, |c, bl| {
                skill_area_sub(c, bl, src, id, skill_lv, tick, BCT_ENEMY, skill_area_sub_count)
            }) >= 3
        {
            crate::status::hp::status_heal(c, src.id, 0, 0, 10, 0);
        }
        let s = super::fresh(c, src);
        clif_skill_nodamage(c, Some(&s), target, id, skill_lv as i32, true);
        splash_search_default(self, c, &s, target, skill_lv, tick, flag);
    }
}
