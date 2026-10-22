//! skills/gunslinger/howlingmine.cpp: class SkillHowlingMine : public SkillImpl (RL_H_MINE), rAthena e985006.
//! (rAthena's TODO: Refactor to SkillImplRecursiveDamageSplash.)

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_PC, BL_SKILL};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_BURNING, SC_H_MINE};
use crate::status::defs::{ELE_FIRE, SCSTART_NOAVOID, SCSTART_NORATEDEF, SCSTART_NOTICKDEF};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHowlingMine;

/// `sd && sd->flicker` (flicker.rs).
fn sd_flicker(src: &Unit) -> bool {
    src.kind == BL_PC && super::flicker::flicker(src.id)
}

impl SkillImpl for SkillHowlingMine {
    fn skill_id(&self) -> u16 {
        RL_H_MINE
    }

    /// SkillHowlingMine::castendDamageId: a direct cast (no flag 1, no Flicker) hits and plants the mine (SC_H_MINE,
    /// val1 = the skill id, when the hit did damage); set off by Flicker, the enemies and skill units around the
    /// target are hit (flag | 1) and a mine this caster planted on the target turns into burning (10% per level); a
    /// splash hit (flag & 1) is a plain hit. Flicker's paths keep the requirements (flag |= 1).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 == 0 {
            // Direct attack
            if !sd_flicker(src) {
                if skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag) != 0 {
                    if let Some(t) = crate::unit::get(c, target.id) {
                        sc::status_change_start(c, Some(src), &t, SC_H_MINE, 10000, id as i32, 0, 0, 0, skill_get_time(id, skill_lv) as i64, SCSTART_NOAVOID | SCSTART_NOTICKDEF | SCSTART_NORATEDEF, 0);
                    }
                }
                return;
            }
            // Triggered by RL_FLICKER
            let f = *flag | BCT_ENEMY | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR | BL_SKILL, Wall::Config, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
            *flag |= 1; // Don't consume requirement
            if let Some(e) = sce(c, target, SC_H_MINE) {
                if e.val2 == src.id as i32 {
                    sc::status_change_end(c, target.id, SC_H_MINE, 0);
                    if let Some(t) = crate::unit::get(c, target.id) {
                        sc::sc_start4(c, Some(src), &t, SC_BURNING, 10 * skill_lv as i32, skill_lv as i32, 1000, src.id as i32, 0, skill_get_time2(id, skill_lv) as i64);
                    }
                }
            }
        } else {
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        }
        if sd_flicker(src) {
            *flag |= 1; // Don't consume requirement
        }
    }

    /// SkillHowlingMine::calculateSkillRatio: set off by Flicker 500 + 300% per level, a direct hit 200 + 200% per
    /// level.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        if sd_flicker(src) {
            // Flicker explosion damage: 500 + 300 * SkillLv
            *skillratio += -100 + 500 + 300 * skill_lv as i32;
        } else {
            // Direct trigger damage: 200 + 200 * SkillLv
            *skillratio += -100 + 200 + 200 * skill_lv as i32;
        }
    }

    /// SkillHowlingMine::modifyElement: force RL_H_MINE deals fire damage if activated by RL_FLICKER.
    fn modify_element(&self, _c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        if sd_flicker(src) {
            *element = ELE_FIRE;
        }
    }
}
