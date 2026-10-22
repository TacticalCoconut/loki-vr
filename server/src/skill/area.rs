//! The area helpers of the skill code: skill.cpp's `skill_area_temp[8]`, skill_area_sub, skill_area_sub_count,
//! splash_target, and map.cpp's map_foreachinrange / map_foreachinallrange / map_foreachinshootrange /
//! map_foreachinarea / map_foreachinallarea / map_foreachinshootarea (map_foreachinrangeV / map_foreachinareaV) in
//! the form the skill code uses them: the units are collected first in rAthena's block order (map.rs
//! sort_block_order: every other kind block by block, then the monsters block by block), then the callback runs for
//! each one that is still on the map when its turn comes (`bl_list[i]->prev`), with the unit read fresh.

use std::cell::RefCell;

use crate::map::{self, CellChk, BL_CHAR, BL_MOB, BL_SKILL};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

thread_local! {
    /// skill.cpp `int32 skill_area_temp[8]`: [0] number of targets in the area, [1] the original target's id,
    /// [2] targets processed so far (skill_area_sub counts them with SD_SPLASH / SD_PREAMBLE), [4] / [5] a cast center,
    /// the rest per skill. A process-wide scratch array in rAthena (never reset); the module instance's here.
    static SKILL_AREA_TEMP: RefCell<[i32; 8]> = const { RefCell::new([0; 8]) };
}

/// skill_area_temp[i].
pub fn temp(i: usize) -> i32 {
    SKILL_AREA_TEMP.with(|t| t.borrow()[i])
}

/// skill_area_temp[i] = v.
pub fn set_temp(i: usize, v: i32) {
    SKILL_AREA_TEMP.with(|t| t.borrow_mut()[i] = v);
}

/// skill_area_temp[i] += v; returns the new value.
pub fn add_temp(i: usize, v: i32) -> i32 {
    SKILL_AREA_TEMP.with(|t| {
        let mut a = t.borrow_mut();
        a[i] = a[i].wrapping_add(v);
        a[i]
    })
}

/// skill.cpp splash_target: splash skills that cannot hit an ice wall when a player casts them.
pub fn splash_target(bl: &Unit) -> u16 {
    if bl.kind == BL_MOB {
        BL_SKILL | BL_CHAR
    } else {
        BL_CHAR
    }
}

/// The wall check of a map_foreach* variant.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Wall {
    /// map_foreachinallrange / map_foreachinallarea: no check.
    None,
    /// map_foreachinrange / map_foreachinarea: `battle_config.skill_wall_check > 0`.
    Config,
    /// map_foreachinshootrange / map_foreachinshootarea: always.
    Always,
}

impl Wall {
    fn on(self) -> bool {
        match self {
            Wall::None => false,
            Wall::Config => super::conf::skill_wall_check(),
            Wall::Always => true,
        }
    }
}

/// The ids map_foreachinrangeV collects around `center` (square range, clamped to the map; CIRCULAR_AREA is off in
/// config/core.hpp), with the wall check against the center, in rAthena's order.
pub fn collect_in_range(c: &Ctx, center: &Unit, range: i16, kinds: u16, wall: Wall) -> Vec<u32> {
    let wall_check = wall.on();
    let (m, cx, cy) = (center.map, center.x, center.y);
    map::units_in_area(c, m, cx - range, cy - range, cx + range, cy + range, kinds)
        .into_iter()
        .filter(|u| !wall_check || crate::path::path_search_long(m, cx, cy, u.x, u.y, CellChk::ChkWall))
        .map(|u| u.id)
        .collect()
}

/// The ids map_foreachinareaV collects in (x0, y0)-(x1, y1) (swapped when reversed, clamped to the map); the wall
/// check is against the area's center `x0 + (x1 - x0) / 2` computed after the clamping, as rAthena does.
pub fn collect_in_area(c: &Ctx, m: u16, x0: i16, y0: i16, x1: i16, y1: i16, kinds: u16, wall: Wall) -> Vec<u32> {
    let wall_check = wall.on();
    let (mut x0, mut y0, mut x1, mut y1) = (x0, y0, x1, y1);
    if x1 < x0 {
        std::mem::swap(&mut x0, &mut x1);
    }
    if y1 < y0 {
        std::mem::swap(&mut y0, &mut y1);
    }
    let Some(md) = crate::data::maps().get(m) else { return Vec::new() };
    let (x0c, y0c, x1c, y1c) = (x0.max(0), y0.max(0), x1.min(md.xs - 1), y1.min(md.ys - 1));
    let (cx, cy) = (x0c + (x1c - x0c) / 2, y0c + (y1c - y0c) / 2);
    map::units_in_area(c, m, x0c, y0c, x1c, y1c, kinds)
        .into_iter()
        .filter(|u| !wall_check || crate::path::path_search_long(m, cx, cy, u.x, u.y, CellChk::ChkWall))
        .map(|u| u.id)
        .collect()
}

/// Runs `f` on each collected unit that is still on the map, read fresh; returns the sum of what `f` returned
/// (map_foreach*'s returnCount).
pub fn run_each(c: &Ctx, ids: Vec<u32>, mut f: impl FnMut(&Ctx, &Unit) -> i32) -> i32 {
    let mut n = 0;
    for id in ids {
        let Some(u) = unit::get(c, id) else { continue };
        if !u.on_map {
            continue; // func() may delete this bl_list[] slot, checking for prev ensures it wasn't queued for deletion.
        }
        n += f(c, &u);
    }
    n
}

/// map_foreachinrange (Wall::Config) / map_foreachinallrange (Wall::None) / map_foreachinshootrange (Wall::Always).
pub fn foreach_in_range(c: &Ctx, center: &Unit, range: i16, kinds: u16, wall: Wall, f: impl FnMut(&Ctx, &Unit) -> i32) -> i32 {
    let ids = collect_in_range(c, center, range, kinds, wall);
    run_each(c, ids, f)
}

/// map_foreachinarea (Wall::Config) / map_foreachinallarea (Wall::None) / map_foreachinshootarea (Wall::Always).
#[allow(clippy::too_many_arguments)]
pub fn foreach_in_area(c: &Ctx, m: u16, x0: i16, y0: i16, x1: i16, y1: i16, kinds: u16, wall: Wall, f: impl FnMut(&Ctx, &Unit) -> i32) -> i32 {
    let ids = collect_in_area(c, m, x0, y0, x1, y1, kinds, wall);
    run_each(c, ids, f)
}

/// skill.cpp `typedef int32 (*SkillFunc)(block_list *, block_list *, int32, int32, t_tick, int32)`:
/// (src, target, skill_id, skill_lv, tick, flag).
pub type SkillFunc = fn(&Ctx, &Unit, &Unit, u16, u16, Tick, i32) -> i32;

/// skill.cpp skill_area_sub: `func(src, bl, …)` for a target that passes battle_check_target(src, bl, flag); the
/// first one of an SD_PREAMBLE area gets the dummy damage packet first, SD_SPLASH / SD_PREAMBLE areas count their
/// targets in skill_area_temp[2].
#[allow(clippy::too_many_arguments)]
pub fn skill_area_sub(c: &Ctx, bl: &Unit, src: &Unit, skill_id: u16, skill_lv: u16, tick: Tick, flag: i32, func: SkillFunc) -> i32 {
    // the source as it is now (an earlier target's effect may have changed it)
    let src = unit::get(c, src.id).unwrap_or_else(|| src.clone());
    if crate::battle::target::battle_check_target(c, &src, bl, flag) > 0 {
        // several splash skills need this initial dummy packet to display correctly
        if flag & super::SD_PREAMBLE != 0 && temp(2) == 0 {
            let amotion = crate::status::status_get_status_data(c, &src).amotion as i32;
            super::clif::clif_skill_damage(c, &src, bl, tick, amotion, 0, super::clif::DMGVAL_IGNORE, 1, skill_id, skill_lv as i32, crate::battle::DMG_SINGLE);
        }
        if flag & (super::SD_SPLASH | super::SD_PREAMBLE) != 0 {
            add_temp(2, 1);
        }
        return func(c, &src, bl, skill_id, skill_lv, tick, flag);
    }
    0
}

/// skill.cpp skill_area_sub_count: counts a target.
pub fn skill_area_sub_count(_c: &Ctx, _src: &Unit, _target: &Unit, _skill_id: u16, _skill_lv: u16, _tick: Tick, _flag: i32) -> i32 {
    1
}

// map.cpp's other area walks and skill.cpp skill_attack_area, ported in the class files that first needed them
// (skills/npc, skills/swordman); reachable here for every family.
#[allow(unused_imports)]
pub(crate) use crate::skills::npc::acidbreath::{map_foreachinpath, skill_attack_area};
#[allow(unused_imports)]
pub(crate) use crate::skills::npc::npccursedcircle::map_forcountinrange;
#[allow(unused_imports)]
pub(crate) use crate::skills::swordman::brandishspear::map_foreachindir;
#[allow(unused_imports)]
pub(crate) use crate::skills::swordman::spearstab::map_foreachincell;
