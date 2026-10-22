//! skills/taekwon/circleofdirectionsandelementals.cpp: class SkillCircleOfDirectionsAndElementals : public SkillImplRecursiveDamageSplash (SOA_CIRCLE_OF_DIRECTIONS_AND_ELEMENTALS).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillCircleOfDirectionsAndElementals;

impl SkillImpl for SkillCircleOfDirectionsAndElementals {
    fn skill_id(&self) -> u16 {
        SOA_CIRCLE_OF_DIRECTIONS_AND_ELEMENTALS
    }

    crate::splash_skill_defaults!();

    /// SkillCircleOfDirectionsAndElementals::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 500 + 2000 * lv;
        *skillratio += pc_checkskill(c, src, SOA_TALISMAN_MASTERY) * 15 * lv;
        *skillratio += pc_checkskill(c, src, SOA_SOUL_MASTERY) * 15 * lv;
        *skillratio += 5 * sstatus.spl as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillCircleOfDirectionsAndElementals {
    /// SkillCircleOfDirectionsAndElementals::splashSearch.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        // skill_area_temp[0] = map_foreachinallrange(skill_area_sub, target, skill_get_splash(…), BL_CHAR, src, …,
        // BCT_ENEMY, skill_area_sub_count)
        let n = foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::None, |c, bl| {
            area::skill_area_sub(c, bl, src, id, skill_lv, tick, BCT_ENEMY, area::skill_area_sub_count)
        });
        area::set_temp(0, n);
        sc::sc_start(c, Some(src), src, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        splash_search_default(self, c, &s, target, skill_lv, tick, flag);
    }
}
