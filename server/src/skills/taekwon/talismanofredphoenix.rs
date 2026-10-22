//! skills/taekwon/talismanofredphoenix.cpp: class SkillTalismanOfRedPhoenix : public SkillImplRecursiveDamageSplash (SOA_TALISMAN_OF_RED_PHOENIX).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_T_FIFTH_GOD, SC_T_SECOND_GOD};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillTalismanOfRedPhoenix;

impl SkillImpl for SkillTalismanOfRedPhoenix {
    fn skill_id(&self) -> u16 {
        SOA_TALISMAN_OF_RED_PHOENIX
    }

    crate::splash_skill_defaults!();

    /// SkillTalismanOfRedPhoenix::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 1650 + 2150 * lv;
        *skillratio += pc_checkskill(c, src, SOA_TALISMAN_MASTERY) * 15 * lv;
        *skillratio += 5 * sstatus.spl as i32;
        if sce(c, src, SC_T_FIFTH_GOD).is_some() {
            *skillratio += 600 + 500 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillTalismanOfRedPhoenix {
    /// SkillTalismanOfRedPhoenix::splashSearch.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        // skill_area_temp[0] = map_foreachinallrange(skill_area_sub, target, skill_get_splash(…), BL_CHAR, src, …,
        // BCT_ENEMY, skill_area_sub_count)
        let n = foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::None, |c, bl| {
            area::skill_area_sub(c, bl, src, id, skill_lv, tick, BCT_ENEMY, area::skill_area_sub_count)
        });
        area::set_temp(0, n);
        if sce(c, src, SC_T_SECOND_GOD).is_some() {
            sc::sc_start(c, Some(src), src, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        splash_search_default(self, c, &s, target, skill_lv, tick, flag);
    }
}
