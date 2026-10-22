//! skills/taekwon/talismanofblacktortoise.cpp: class SkillTalismanOfBlackTortoise : public SkillImpl (SOA_TALISMAN_OF_BLACK_TORTOISE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface::skill_unitsetting;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::{SC_T_FIFTH_GOD, SC_T_THIRD_GOD};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillTalismanOfBlackTortoise;

impl SkillImpl for SkillTalismanOfBlackTortoise {
    fn skill_id(&self) -> u16 {
        SOA_TALISMAN_OF_BLACK_TORTOISE
    }

    /// SkillTalismanOfBlackTortoise::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 2150 + 1600 * lv;
        *skillratio += pc_checkskill(c, src, SOA_TALISMAN_MASTERY) * 15 * lv;
        *skillratio += 5 * sstatus.spl as i32;
        if sce(c, src, SC_T_FIFTH_GOD).is_some() {
            *skillratio += 150 + 500 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillTalismanOfBlackTortoise::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if sce(c, src, SC_T_THIRD_GOD).is_some() {
            sc::sc_start(c, Some(src), src, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        skill_unitsetting(c, &s, id, skill_lv, x, y, 0);
    }
}
