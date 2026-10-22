//! skills/taekwon/starburst.cpp: class SkillStarBurst : public SkillImpl (SKE_STAR_BURST).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::attack::skill_attack;
use crate::skill::clif::{clif_skill_fail, fail::USESKILL_FAIL_LEVEL};
use crate::skill::iface::{skill_castend_pos2, skill_unitsetting};
use crate::skill::timerskill::{skill_timerskill as _, SkillTimerskill};
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::SkillImpl;
use crate::skillunit::{data, group::has_ud, skill_delunitgroup};
use crate::status::sc;
use crate::status::sc_ext::distance_xy;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillStarBurst;

impl SkillImpl for SkillStarBurst {
    fn skill_id(&self) -> u16 {
        SKE_STAR_BURST
    }

    /// SkillStarBurst::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        } else {
            // unit_data* ud = unit_bl2ud(src); if (ud != nullptr)
            if has_ud(src.kind) {
                // for (sug : ud->skillunits), in vector order (the loop returns at its first change of the vector)
                for sug in data::groups_of(c, src.id) {
                    if sug.skill_id != SKE_TWINKLING_GALAXY {
                        continue;
                    }
                    // skill_unit* su = sug->unit: the group's first unit (rAthena reads a dead first unit's cell too; its
                    // row is gone here, but a Twinkling Galaxy group has one unit, whose death ends the group)
                    let Some(su) = data::units_of(c, sug.group_id).into_iter().find(|u| u.idx == 0) else { continue };
                    // Check if it is too far away
                    if distance_xy(target.x, target.y, su.bl.x, su.bl.y) > skill_get_unit_range(sug.skill_id, sug.skill_lv) {
                        continue;
                    }
                    // std::shared_ptr<s_skill_unit_group> sg = su->group;
                    let sg = su.group_id;
                    // ud->skilltimerskill[0..MAX_SKILLTIMERSKILL] of the group's skill: delete_timer(…, skill_timerskill),
                    // ers_free, nullptr (slot order)
                    let mut rows: Vec<SkillTimerskill> = c.db().skill_timerskill().by_src().filter(src.id).collect();
                    rows.sort_by_key(|r| r.slot);
                    for r in rows {
                        if r.skill_id != sug.skill_id {
                            continue;
                        }
                        crate::timer::delete(c, r.timer);
                        c.db().skill_timerskill().id().delete(r.id);
                    }
                    skill_delunitgroup(c, sg);
                    let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                    let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                    sc::sc_start2(c, Some(&s), &t, skill_get_sc(id) as i32, 100, skill_lv as i32, src.id as i32, skill_get_time2(id, skill_lv) as i64);
                    let s = unit::get(c, src.id).unwrap_or(s);
                    let t = unit::get(c, target.id).unwrap_or(t);
                    skill_castend_pos2(c, &s, t.x, t.y, id, skill_lv, tick, 0);
                    return;
                }
            }
            if src.kind == BL_PC {
                clif_skill_fail(c, src, id, USESKILL_FAIL_LEVEL, 0, 0);
            }
            *flag |= SKILL_NOCONSUME_REQ;
        }
    }

    /// SkillStarBurst::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1;
        skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }

    /// SkillStarBurst::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 500 + 400 * lv;
        *skillratio += pc_checkskill(c, src, SKE_SKY_MASTERY) * 3 * lv;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
