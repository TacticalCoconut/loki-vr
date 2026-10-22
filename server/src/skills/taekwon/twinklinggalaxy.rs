//! skills/taekwon/twinklinggalaxy.cpp: class SkillTwinklingGalaxy : public SkillImpl (SKE_TWINKLING_GALAXY).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skill::iface::skill_unitsetting;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, repeat_count};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillTwinklingGalaxy;

impl SkillImpl for SkillTwinklingGalaxy {
    fn skill_id(&self) -> u16 {
        SKE_TWINKLING_GALAXY
    }

    /// SkillTwinklingGalaxy::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        }
    }

    /// SkillTwinklingGalaxy::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let interval = skill_get_unit_interval(id);
        for i in 0..repeat_count(skill_get_time(id, skill_lv), interval) {
            skill_addtimerskill(c, src, tick + i as Tick * interval as Tick, 0, x, y, id, skill_lv, 0, *flag);
        }
        *flag |= 1;
        skill_unitsetting(c, src, id, skill_lv, x, y, 0);
    }

    /// SkillTwinklingGalaxy::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 250 + 600 * lv;
        *skillratio += pc_checkskill(c, src, SKE_SKY_MASTERY) * 3 * lv;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
