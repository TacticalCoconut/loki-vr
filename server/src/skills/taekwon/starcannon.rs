//! skills/taekwon/starcannon.cpp: class SkillStarCannon : public SkillImpl (SKE_STAR_CANNON).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skill::iface::skill_unitsetting;
use crate::skill::timerskill::{skill_addtimerskill, skill_timerskill as _, SkillTimerskill};
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, repeat_count};
use crate::skills::SkillImpl;
use crate::skillunit::{data, group::has_ud, skill_delunitgroup};
use crate::status::sc_ext::distance_xy;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillStarCannon;

impl SkillImpl for SkillStarCannon {
    fn skill_id(&self) -> u16 {
        SKE_STAR_CANNON
    }

    /// SkillStarCannon::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        }
    }

    /// SkillStarCannon::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // unit_data* ud = unit_bl2ud(src); if (ud == nullptr) return;
        if !has_ud(src.kind) {
            return;
        }
        // for (sug : ud->skillunits): the range-for walks the vector by position up to the end it had when the loop
        // began, and the body changes the vector without leaving the loop: skill_delunitgroup erases the matching
        // group (the following ones move down one place) and skill_unitsetting pushes the Star Cannon group at the end
        // (same size again, no reallocation). So the group right after a matched one is skipped and the new group is
        // met at the end. ud->skillunits is read again at each position to walk it the same way; a vector that ended up
        // shorter than the loop's end (rAthena reads past it) stops the walk.
        let end = data::groups_of(c, src.id).len();
        let mut pos = 0;
        while pos < end {
            let Some(sug) = data::groups_of(c, src.id).into_iter().nth(pos) else { break };
            pos += 1;
            if sug.skill_id != SKE_TWINKLING_GALAXY {
                continue;
            }
            // skill_unit* su = sug->unit: the group's first unit (rAthena reads a dead first unit's cell too; its row is
            // gone here, but a Twinkling Galaxy group has one unit, whose death ends the group)
            let Some(su) = data::units_of(c, sug.group_id).into_iter().find(|u| u.idx == 0) else { continue };
            if distance_xy(x, y, su.bl.x, su.bl.y) > skill_get_unit_range(sug.skill_id, sug.skill_lv) {
                continue;
            }
            // std::shared_ptr<s_skill_unit_group> sg = su->group;
            let sg = su.group_id;
            // ud->skilltimerskill[0..MAX_SKILLTIMERSKILL] of SKE_TWINKLING_GALAXY: delete_timer(…, skill_timerskill),
            // ers_free, nullptr (slot order)
            let mut rows: Vec<SkillTimerskill> = c.db().skill_timerskill().by_src().filter(src.id).collect();
            rows.sort_by_key(|r| r.slot);
            for r in rows {
                if r.skill_id != SKE_TWINKLING_GALAXY {
                    continue;
                }
                crate::timer::delete(c, r.timer);
                c.db().skill_timerskill().id().delete(r.id);
            }
            skill_delunitgroup(c, sg);
            let interval = skill_get_unit_interval(id);
            for i in 0..repeat_count(skill_get_time(id, skill_lv), interval) {
                skill_addtimerskill(c, src, tick + i as Tick * interval as Tick, 0, x, y, id, skill_lv, 0, *flag);
            }
            *flag |= 1;
            skill_unitsetting(c, src, id, skill_lv, x, y, 0);
        }
    }

    /// SkillStarCannon::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 150 + 650 * lv;
        *skillratio += pc_checkskill(c, src, SKE_SKY_MASTERY) * 5 * lv;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
