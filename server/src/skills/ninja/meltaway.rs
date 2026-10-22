//! skills/ninja/meltaway.cpp: class SkillMeltAway : public SkillImpl (SS_TOKEDASU), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::attack::{skill_blown, BLOWN_DONT_SEND_PACKET, BLOWN_IGNORE_NO_KNOCKBACK};
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMeltAway;

impl SkillImpl for SkillMeltAway {
    fn skill_id(&self) -> u16 {
        SS_TOKEDASU
    }

    /// SkillMeltAway::calculateSkillRatio: 700% per level + 5 x CON, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 700 * skill_lv as i32;
        *skillratio += 5 * sstatus.con as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillMeltAway::castendDamageId: the splash hit (flag & 1).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        super::splash_hit(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillMeltAway::castendPos2: the square around (x, y) is hit (map_foreachinallarea, flag | 1), the caster's
    /// status (skill_get_time2), then the caster faces (x, y) and leaps back in that direction (skill_blown without
    /// its packet, then clif_blown).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        super::pos_splash(c, id, src, x, y, skill_lv, tick, *flag);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        sc::sc_start(c, Some(&s), &s, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
        if let Some(mut s) = unit::get(c, src.id) {
            let dir = crate::map::calc_dir_xy(s.x, s.y, x, y, unit::getdir(&s));
            unit::setdir(c, &mut s, dir, true);
            unit::save(c, &s);
        }
        let s = unit::get(c, src.id).unwrap_or(s);
        skill_blown(c, &s, &s, skill_get_blewcount(id, skill_lv), unit::getdir(&s) as i8, BLOWN_IGNORE_NO_KNOCKBACK | BLOWN_DONT_SEND_PACKET);
        if let Some(s) = unit::get(c, src.id) {
            crate::battle::clif::clif_blown(c, &s);
        }
    }
}
