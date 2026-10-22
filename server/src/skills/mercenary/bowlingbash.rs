//! skills/mercenary/mercenary_bowlingbash.cpp: class SkillMercenaryBowlingBash : public WeaponSkillImpl
//! (MS_BOWLINGBASH), and skill.cpp's `DBMap* bowling_db` (its only user in RENEWAL: the Knight's renewal class does
//! not compile the old chain). Unlike the Knight's renewal class, the mercenary's keeps the old "gutter line" chain:
//! the target is pushed square by square away from the caster and every unit it runs into is bowled on in a random
//! direction (a recursive skill_castend_damage_id with flag + 1).

use std::cell::RefCell;
use std::collections::HashSet;

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::CellChk;
use crate::path::{DIRX, DIRY};
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::attack::{skill_blown, BLOWN_NONE};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::SD_ANIMATION;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// battle_config.bowling_bash_area (conf/battle/skill.conf `bowling_bash_area: 0`: the gutter line system).
pub fn bowling_bash_area() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::BOWLING_BASH_AREA)
}

thread_local! {
    /// skill.cpp `DBMap* bowling_db` (int32 id -> block_list*): the units a Bowling Bash chain already hit. A
    /// process-wide map in rAthena, cleared by each chain's first call; the module instance's here (only membership
    /// is ever read).
    static BOWLING_DB: RefCell<HashSet<u32>> = RefCell::new(HashSet::new());
}

pub struct SkillMercenaryBowlingBash;

impl SkillImpl for SkillMercenaryBowlingBash {
    fn skill_id(&self) -> u16 {
        MS_BOWLINGBASH
    }

    /// SkillMercenaryBowlingBash::modifyDamageData: no knockback from the hit itself (the chain moves the target).
    fn modify_damage_data(&self, _c: &Ctx, dmg: &mut Damage, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        dmg.blewcount = 0;
    }

    /// SkillMercenaryBowlingBash::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 40 * skill_lv as i32;
    }

    /// SkillMercenaryBowlingBash::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let f = *flag;
        let (sx, sy) = (src.x as i32, src.y as i32);
        // Chain effect and check range gets reduction by recursive depth, as this can reach 0, we don't use blowcount
        let cnt = (skill_lv as i32 - (f & 0xFFF) + 1) / 2;
        // Determine the Bowling Bash area depending on configuration
        let (min_x, max_x, min_y, max_y);
        if bowling_bash_area() == 0 {
            // Gutter line system (C's % keeps the sign of the dividend, as Rust's)
            let mut mx = (sx - cnt) - (sx - cnt) % 40;
            if mx < 0 {
                mx = 0;
            }
            let mut my = (sy - cnt) - (sy - cnt) % 40;
            if my < 0 {
                my = 0;
            }
            (min_x, max_x, min_y, max_y) = (mx, mx + 39, my, my + 39);
        } else if bowling_bash_area() == 1 {
            // Gutter line system without demi gutter bug
            let mx = sx - sx % 40;
            let my = sy - sy % 40;
            (min_x, max_x, min_y, max_y) = (mx, mx + 39, my, my + 39);
        } else {
            // Area around caster
            (min_x, max_x, min_y, max_y) = (sx - bowling_bash_area(), sx + bowling_bash_area(), sy - bowling_bash_area(), sy + bowling_bash_area());
        }
        let inside = |u: &Unit| !((u.x as i32) < min_x || u.x as i32 > max_x || (u.y as i32) < min_y || u.y as i32 > max_y);
        // Initialization, break checks, direction
        let dir: usize;
        if f & 0xFFF > 0 {
            // Ignore monsters outside area
            if !inside(target) {
                return;
            }
            // Ignore monsters already in list
            if BOWLING_DB.with(|db| db.borrow().contains(&target.id)) {
                return;
            }
            // Random direction
            dir = (c.rnd() % 8) as usize;
        } else {
            // Create an empty list of already hit targets
            BOWLING_DB.with(|db| db.borrow_mut().clear());
            // Direction is walkpath
            dir = ((unit::getdir(src) as usize) + 4) % 8;
        }
        // Add current target to the list of already hit targets
        BOWLING_DB.with(|db| db.borrow_mut().insert(target.id));
        // Keep moving target in direction square by square
        let m = target.map;
        let mut tx = target.x as i32;
        let mut ty = target.y as i32;
        // (the target as rAthena's pointer sees it: its position after each push)
        let fresh = |c: &Ctx| unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
        for _ in 0..cnt {
            // Target coordinates (get changed even if knockback fails)
            tx -= DIRX[dir] as i32;
            ty -= DIRY[dir] as i32;
            // If target cell is a wall then break
            if crate::map::getcell(c, m, tx as i16, ty as i16, CellChk::ChkWall) {
                break;
            }
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            skill_blown(c, &s, &fresh(c), 1, dir as i8, BLOWN_NONE);
            // Splash around target cell, but only cells inside area; we first have to check the area is not negative
            let (x0, y0, x1, y1) = (min_x.max(tx - 1), min_y.max(ty - 1), max_x.min(tx + 1), max_y.min(ty + 1));
            if x0 <= x1 && y0 <= y1 {
                let (x0, y0, x1, y1) = (x0 as i16, y0 as i16, x1 as i16, y1 as i16);
                let kinds = area::splash_target(src);
                let fc = f | BCT_ENEMY;
                let count = foreach_in_area(c, m, x0, y0, x1, y1, kinds, Wall::None, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, fc, area::skill_area_sub_count));
                if count != 0 {
                    // Recursive call
                    let fr = (f | BCT_ENEMY) + 1;
                    foreach_in_area(c, m, x0, y0, x1, y1, kinds, Wall::None, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, fr, skill_castend_damage_id));
                    // Self-collision
                    let t = fresh(c);
                    if inside(&t) {
                        let sflag = if f & 0xFFF > 0 { SD_ANIMATION | count } else { count };
                        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                        weapon_castend_damage_id(c, id, &s, &t, skill_lv, tick, sflag);
                    }
                    break;
                }
            }
        }
        // Original hit or chain hit depending on flag
        let sflag = if f & 0xFFF > 0 { SD_ANIMATION } else { 0 };
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        weapon_castend_damage_id(c, id, &s, &fresh(c), skill_lv, tick, sflag);
    }
}
