//! skills/ninja/infiltrate.cpp: class SkillInfiltrate : public SkillImpl (SS_SHIMIRU), rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_PC, BL_SKILL};
use crate::path::{DIR_EAST, DIR_NORTH, DIR_NORTHEAST, DIR_SOUTH, DIR_WEST};
use crate::skill::area::map_foreachincell;
use crate::skill::attack::skill_attack;
use crate::skill::clif::{clif_skill_fail, fail::USESKILL_FAIL_TARGET_SHADOW_SPACE};
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillInfiltrate;

impl SkillImpl for SkillInfiltrate {
    fn skill_id(&self) -> u16 {
        SS_SHIMIRU
    }

    /// SkillInfiltrate::calculateSkillRatio: 700% per level + 5 x CON, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 700 * skill_lv as i32;
        *skillratio += 5 * sstatus.con as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillInfiltrate::castendDamageId: a caster not on the target's cell moves to the cell behind the enemy target
    /// (unit_movepos easy 2), which turns around; if it cannot, the cast fails (USESKILL_FAIL_TARGET_SHADOW_SPACE).
    /// Then each of the caster's unit groups (`ud->skillunits`) looks for a cell at the caster's offset from its unit
    /// that holds no character and no mirage unit (up to 1000 random steps of -1..1 per axis, one step per try), and a
    /// Shinkirou mirage group moves by that offset; then the hit and the caster's status (skill_get_time2).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if !crate::mob::unit::check_distance_bl(src, target, 0) {
            let mut dir = crate::map::calc_dir(src, target.x, target.y);
            let x: i16 = if dir > DIR_NORTH && dir < DIR_SOUTH {
                -1
            } else if dir > DIR_SOUTH {
                1
            } else {
                0
            };
            let y: i16 = if dir > DIR_WEST && dir < DIR_EAST {
                -1
            } else if dir == DIR_NORTHEAST || dir < DIR_WEST {
                1
            } else {
                0
            };
            if crate::battle::target::battle_check_target(c, src, target, BCT_ENEMY) > 0 && unit::movepos(c, src.id, target.x + x, target.y + y, 2, true) {
                // Display movement + animation.
                dir = if dir < 4 { dir + 4 } else { dir - 4 }; // change direction [Celest]
                if let Some(mut t) = unit::get(c, target.id) {
                    unit::setdir(c, &mut t, dir, true);
                    unit::save(c, &t);
                }
                if let Some(s) = unit::get(c, src.id) {
                    crate::battle::clif::clif_blown(c, &s);
                }
            } else {
                if src.kind == BL_PC {
                    clif_skill_fail(c, src, id, USESKILL_FAIL_TARGET_SHADOW_SPACE, 0, 0);
                }
                return;
            }
        }
        // if (ud == nullptr) return; (every caster of this skill has unit_data)
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        for sug in crate::skillunit::data::groups_of(c, s.id) {
            // skill_unit* su = sug->unit (the group's unit array; its first unit)
            let Some(su) = crate::skillunit::data::units_of(c, sug.group_id).into_iter().find(|u| u.idx == 0) else { continue };
            let (sux, suy) = (su.bl.x, su.bl.y);
            let mut dx = s.x - sux;
            let mut dy = s.y - suy;
            for _count in 0..1000 {
                if map_foreachincell(c, s.map, sux + dx, suy + dy, BL_CHAR | BL_SKILL, super::skill_shimiru_check_cell) == 0 {
                    break;
                }
                dx += (c.rnd() % 3) as i16 - 1;
                dy += (c.rnd() % 3) as i16 - 1;
            }
            if sug.skill_id == SS_SHINKIROU {
                crate::skillunit::movement::skill_unit_move_unit_group(c, sug.group_id, s.map, dx, dy);
            }
        }
        let s = unit::get(c, src.id).unwrap_or(s);
        let t = unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
        skill_attack(c, skill_get_type(id), &s, &s, &t, id, skill_lv, tick, *flag);
        let s = unit::get(c, src.id).unwrap_or(s);
        sc::sc_start(c, Some(&s), &s, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
    }
}
