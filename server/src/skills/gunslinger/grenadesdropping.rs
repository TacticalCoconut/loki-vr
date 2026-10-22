//! skills/gunslinger/grenadesdropping.cpp: class SkillGrenadesDropping : public SkillImpl (NW_GRENADES_DROPPING),
//! rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface::skill_unitsetting;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, repeat_count};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGrenadesDropping;

impl SkillImpl for SkillGrenadesDropping {
    fn skill_id(&self) -> u16 {
        NW_GRENADES_DROPPING
    }

    /// SkillGrenadesDropping::castendPos2: the unit on a random cell of the splash square around (x, y) (x rolled
    /// before y, uint16 coordinates), then a timerskill at (x, y) every unit interval, the first right now (i from 0
    /// to skill_get_time / interval inclusive).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let splash = skill_get_splash(id, skill_lv) as u16 as i32;
        let tmpx = c.rnd_value(x as i32 - splash, x as i32 + splash) as u16;
        let tmpy = c.rnd_value(y as i32 - splash, y as i32 + splash) as u16;
        skill_unitsetting(c, src, id, skill_lv, tmpx as i16, tmpy as i16, *flag);
        let interval = skill_get_unit_interval(id);
        for i in 0..=repeat_count(skill_get_time(id, skill_lv), interval) {
            let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
            skill_addtimerskill(c, &s, tick + i as i64 * interval as i64, 0, x, y, id, skill_lv, 0, *flag);
        }
    }

    /// SkillGrenadesDropping::calculateSkillRatio: 550 + 850% per level, + 30% per Grenade Mastery level, + 5 x CON,
    /// RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 550 + 850 * skill_lv as i32;
        *skillratio += pc_checkskill(c, src, NW_GRENADE_MASTERY) * 30;
        *skillratio += 5 * sstatus.con as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillGrenadesDropping::modifyElement: Night Watch Grenade Fragment elementals.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        super::grenade_fragment_element(c, src, element);
    }
}
