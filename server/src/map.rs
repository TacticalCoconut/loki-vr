//! map/map.cpp: cells, directions, and the block-list queries (foreachinarea / foreachinmovearea / count_oncell)
//! answered from the `unit` table's (map, x) index instead of rAthena's 8×8 block grid.

use crate::data::{self, MapData};
use crate::unit::{unit, Unit};
use crate::world::Ctx;

/// enum bl_type (map.hpp).
pub const BL_NUL: u16 = 0x000;
pub const BL_PC: u16 = 0x001;
pub const BL_MOB: u16 = 0x002;
pub const BL_PET: u16 = 0x004;
pub const BL_HOM: u16 = 0x008;
pub const BL_MER: u16 = 0x010;
pub const BL_ITEM: u16 = 0x020;
pub const BL_SKILL: u16 = 0x040;
pub const BL_NPC: u16 = 0x080;
pub const BL_CHAT: u16 = 0x100;
pub const BL_ELEM: u16 = 0x200;
pub const BL_ALL: u16 = 0xFFF;
pub const BL_CHAR: u16 = BL_PC | BL_MOB | BL_HOM | BL_MER | BL_ELEM;
pub const ALL_KINDS: [u16; 10] = [BL_PC, BL_MOB, BL_PET, BL_HOM, BL_MER, BL_ITEM, BL_SKILL, BL_NPC, BL_CHAT, BL_ELEM];

/// enum cell_chk (map.hpp), the checks path/unit code uses.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum CellChk {
    GetType,
    ChkWall,
    ChkWater,
    ChkCliff,
    ChkPass,
    ChkReach,
    ChkNoPass,
    ChkNoReach,
    ChkStack,
    ChkNpc,
}

/// map.cpp map_gat2cell: (walkable, shootable, water).
pub(crate) fn gat2cell(gat: u8) -> (bool, bool, bool) {
    match gat {
        0 | 2 | 4 | 6 => (true, true, false),
        1 => (false, false, false),
        3 => (true, true, true),
        5 => (false, true, false),
        _ => (false, false, false),
    }
}

/// map.cpp map_getcellp. CELL_NOSTACK is not defined in rAthena's default build, so stacking never blocks here.
/// NPC touch cells (CELL_CHKNPC) are answered by npc.rs, which owns the NPC areas.
pub fn getcellp(m: &MapData, x: i16, y: i16, chk: CellChk) -> bool {
    // Intentionally overrides the last row and column (map.cpp:3329).
    if x < 0 || x >= m.xs - 1 || y < 0 || y >= m.ys - 1 {
        return chk == CellChk::ChkNoPass;
    }
    // the cell as a script or skill changed it (map_setcell: map_cell.rs), else the map cache's gat type
    let (walkable, shootable, water) = crate::map_cell::terrain(m, x, y);
    match chk {
        CellChk::GetType => false,
        CellChk::ChkWall => !walkable && !shootable,
        CellChk::ChkWater => water,
        CellChk::ChkCliff => !walkable && shootable,
        CellChk::ChkPass | CellChk::ChkReach => walkable,
        CellChk::ChkNoPass | CellChk::ChkNoReach => !walkable,
        CellChk::ChkStack => false,
        CellChk::ChkNpc => false,
    }
}

/// map.cpp map_getcell for a map id; CELL_CHKNPC consults the NPC touch areas.
pub fn getcell(c: &Ctx, m: u16, x: i16, y: i16, chk: CellChk) -> bool {
    if chk == CellChk::ChkNpc {
        return crate::npc::has_touch_area_at(c, m, x, y);
    }
    match data::maps().get(m) {
        Some(md) => getcellp(md, x, y, chk),
        None => false,
    }
}

/// map.cpp map_calc_dir_xy (knockback_left: yes → a unit on the same cell faces east).
pub fn calc_dir_xy(srcx: i16, srcy: i16, x: i16, y: i16, _srcdir: u8) -> u8 {
    use crate::path::*;
    let dx = x as i32 - srcx as i32;
    let dy = y as i32 - srcy as i32;
    if dx == 0 && dy == 0 {
        DIR_EAST
    } else if dx >= 0 && dy >= 0 {
        if dx >= dy * 3 {
            DIR_EAST
        } else if dx * 3 < dy {
            DIR_NORTH
        } else {
            DIR_NORTHEAST
        }
    } else if dx >= 0 && dy <= 0 {
        if dx >= -dy * 3 {
            DIR_EAST
        } else if dx * 3 < -dy {
            DIR_SOUTH
        } else {
            DIR_SOUTHEAST
        }
    } else if dx <= 0 && dy <= 0 {
        if dx * 3 >= dy {
            DIR_SOUTH
        } else if dx < dy * 3 {
            DIR_WEST
        } else {
            DIR_SOUTHWEST
        }
    } else if -dx * 3 <= dy {
        DIR_NORTH
    } else if -dx > dy * 3 {
        DIR_WEST
    } else {
        DIR_NORTHWEST
    }
}

pub fn calc_dir(src: &Unit, x: i16, y: i16) -> u8 {
    calc_dir_xy(src.x, src.y, x, y, src.ud.dir)
}

/// map.hpp BLOCK_SIZE: the block grid rAthena's area queries walk.
pub const BLOCK_SIZE: i16 = 8;

/// The order of a unit in its block's list. rAthena keeps each 8×8 block's units in a linked list and map_addblock
/// puts a unit at its head (monsters in a list of their own, block_mob); map_moveblock re-adds a unit only when it
/// crosses into another block. A block's units are therefore met newest first, and `seq` records when the unit last
/// went to the head of its list. A unit without a row (none since it was placed) counts as the oldest.
#[spacetimedb::table(accessor = block_seq)]
pub struct BlockSeq {
    #[primary_key]
    pub id: u32,
    pub seq: u64,
}

thread_local! {
    /// Per-call cache of `block_seq` entries so repeated area sorts in the same reducer call do not re-query the
    /// host table for every unit (405k lookups in the 50-player load test). Reset by `unit::freed_reset` (`world::run`).
    static BLOCK_SEQ_CACHE: std::cell::RefCell<std::collections::HashMap<u32, u64>> =
        std::cell::RefCell::new(std::collections::HashMap::new());
}

pub fn block_seq_reset() {
    BLOCK_SEQ_CACHE.with(|m| m.borrow_mut().clear());
}

pub fn block_seq_removed(id: u32) {
    BLOCK_SEQ_CACHE.with(|m| m.borrow_mut().remove(&id));
    crate::unit::bump_mob_pos_gen();
}

/// map_addblock / map_moveblock into another block: the unit goes to the head of its block's list.
pub fn block_added(c: &Ctx, id: u32) {
    use spacetimedb::Table;
    let seq = crate::ids::next(c.rc, "block_seq", 1) as u64;
    if c.db().block_seq().id().find(id).is_some() {
        c.db().block_seq().id().update(BlockSeq { id, seq });
    } else {
        c.db().block_seq().insert(BlockSeq { id, seq });
    }
    BLOCK_SEQ_CACHE.with(|m| {
        m.borrow_mut().insert(id, seq);
    });
    crate::unit::bump_mob_pos_gen();
}

/// Sorts units the way rAthena's area loops meet them: blocks row by row (y block outer, x block inner), a block's
/// units newest first; `mobs_per_block` false = every other unit first and then the monsters (map_foreachinareaV,
/// map_foreachinrangeV), true = a block's other units and then its monsters (map_foreachinmovearea).
fn sort_block_order(c: &Ctx, units: &mut Vec<Unit>, mobs_per_block: bool) {
    crate::prof::count("sort_units", units.len() as u64);
    let seq = |id: u32| {
        if let Some(s) = BLOCK_SEQ_CACHE.with(|m| m.borrow().get(&id).copied()) {
            return s;
        }
        let s = c.db().block_seq().id().find(id).map(|r| r.seq).unwrap_or(0);
        BLOCK_SEQ_CACHE.with(|m| {
            m.borrow_mut().insert(id, s);
        });
        s
    };
    // the units move into the sorted list (no copies: a unit row is large)
    type Key = ((u8, i16, i16, u8), std::cmp::Reverse<u64>, usize);
    let mut keyed: Vec<(Key, Unit)> = std::mem::take(units)
        .into_iter()
        .enumerate()
        .map(|(i, u)| {
            let mob = (u.kind == BL_MOB) as u8;
            let (by, bx) = (u.y / BLOCK_SIZE, u.x / BLOCK_SIZE);
            let k = if mobs_per_block { (0, by, bx, mob) } else { (mob, by, bx, 0) };
            ((k, std::cmp::Reverse(seq(u.id)), i), u)
        })
        .collect();
    keyed.sort_by(|a, b| a.0.cmp(&b.0));
    *units = keyed.into_iter().map(|(_, u)| u).collect();
}

/// Units of the given kinds with x in [x0, x1] and y in [y0, y1] on map m, in the block list (bl->prev != nullptr).
/// map.cpp map_foreachinallarea's selection (clamped to the map), in its order.
#[cfg_attr(feature = "loadprof", track_caller)]
pub fn units_in_area(c: &Ctx, m: u16, x0: i16, y0: i16, x1: i16, y1: i16, kinds: u16) -> Vec<Unit> {
    let mut out = area_unsorted(c, m, x0, y0, x1, y1, kinds);
    sort_block_order(c, &mut out, false);
    out
}

#[cfg_attr(feature = "loadprof", track_caller)]
fn area_unsorted(c: &Ctx, m: u16, x0: i16, y0: i16, x1: i16, y1: i16, kinds: u16) -> Vec<Unit> {
    let Some(md) = data::maps().get(m) else { return Vec::new() };
    let (mut x0, mut y0, mut x1, mut y1) = (x0, y0, x1, y1);
    if x1 < x0 {
        std::mem::swap(&mut x0, &mut x1);
    }
    if y1 < y0 {
        std::mem::swap(&mut y0, &mut y1);
    }
    let x0 = x0.max(0);
    let y0 = y0.max(0);
    let x1 = x1.min(md.xs - 1);
    let y1 = y1.min(md.ys - 1);
    if x0 > x1 || y0 > y1 {
        return Vec::new();
    }
    let mut out = Vec::new();
    crate::prof::count("area", 1);
    crate::prof::site(std::panic::Location::caller());
    // One index range per kind asked for (map, kind, x): a search for floor items no longer reads every player,
    // monster and NPC of the x band (a looter monster near N players searched N times per AI tick: load test,
    // server/ARCHITECTURE.md). The order of this list does not matter: every caller sorts it by block and block
    // order (sort_block_order: a unit's seq is unique, a unit without one is a placed MVP tomb, and those tie only
    // with each other, in x order within one kind as before) or only counts it.
    for b in 0..16u16 {
        let k = 1u16 << b;
        if kinds & k == 0 {
            continue;
        }
        for u in c.db().unit().by_map_kind_x().filter((m, k, x0..=x1)) {
            crate::prof::count("area_rows", 1);
            crate::unit::cache_mob(&u);
            if u.on_map && u.y >= y0 && u.y <= y1 {
                out.push(u);
            }
        }
    }
    out
}

/// Units within `range` (square) of (x, y).
#[cfg_attr(feature = "loadprof", track_caller)]
pub fn units_in_range(c: &Ctx, m: u16, x: i16, y: i16, range: i16, kinds: u16) -> Vec<Unit> {
    units_in_area(c, m, x - range, y - range, x + range, y + range, kinds)
}

/// map.cpp map_foreachinmovearea's selection: the strip of `center`'s area (before it moves by dx,dy) that the move
/// leaves (call with the pre-move position) or enters (call with the post-move position and -dx,-dy), in its order
/// (per block: the other units, then the monsters).
#[cfg_attr(feature = "loadprof", track_caller)]
pub fn units_in_move_area(c: &Ctx, center: &Unit, range: i16, dx: i16, dy: i16, kinds: u16) -> Vec<Unit> {
    if range == 0 || (dx == 0 && dy == 0) {
        return Vec::new();
    }
    let Some(md) = data::maps().get(center.map) else { return Vec::new() };
    let (mut x0, mut x1, mut y0, mut y1) = (center.x - range, center.x + range, center.y - range, center.y + range);
    let mut out = if dx == 0 || dy == 0 {
        if dx == 0 {
            if dy < 0 {
                y0 = y1 + dy + 1;
            } else {
                y1 = y0 + dy - 1;
            }
        } else if dx < 0 {
            x0 = x1 + dx + 1;
        } else {
            x1 = x0 + dx - 1;
        }
        area_unsorted(c, center.map, x0, y0, x1, y1, kinds)
    } else {
        x0 = x0.max(0);
        y0 = y0.max(0);
        x1 = x1.min(md.xs - 1);
        y1 = y1.min(md.ys - 1);
        area_unsorted(c, center.map, x0, y0, x1, y1, kinds)
            .into_iter()
            .filter(|u| (dx > 0 && u.x < x0 + dx) || (dx < 0 && u.x > x1 + dx) || (dy > 0 && u.y < y0 + dy) || (dy < 0 && u.y > y1 + dy))
            .collect()
    };
    sort_block_order(c, &mut out, true);
    out
}

/// map.cpp map_count_oncell: on-map units of `kinds` on the cell. NPCs that are hidden (OPTION_HIDE), off-map or
/// someone's dynamic NPC are not counted (cloaked ones are). flag & 1: only units that are not walking.
pub fn count_oncell(c: &Ctx, m: u16, x: i16, y: i16, kinds: u16, flag: i32) -> usize {
    let Some(md) = data::maps().get(m) else { return 0 };
    if x < 0 || y < 0 || x >= md.xs || y >= md.ys {
        return 0;
    }
    area_unsorted(c, m, x, y, x, y, kinds)
        .iter()
        .filter(|u| {
            if u.kind == BL_NPC && (u.sc.option & crate::unit::OPTION_HIDE != 0 || u.npc_owner_char != 0) {
                return false;
            }
            flag & 1 == 0 || u.ud.walktimer == 0
        })
        .count()
}

/// map.cpp map_nearby_freecell: the cell itself if free, else the first free passable neighbour in one of two
/// direction orders picked at random. Returns the cell, or None.
pub fn nearby_freecell(c: &Ctx, m: u16, x: i16, y: i16, kinds: u16, flag: i32) -> Option<(i16, i16)> {
    use crate::path::*;
    if count_oncell(c, m, x, y, kinds, flag) == 0 {
        return Some((x, y));
    }
    const ORDERS: [[u8; 8]; 2] = [
        [DIR_NORTHEAST, DIR_EAST, DIR_SOUTHEAST, DIR_SOUTH, DIR_NORTH, DIR_SOUTHWEST, DIR_WEST, DIR_NORTHWEST],
        [DIR_SOUTHWEST, DIR_WEST, DIR_NORTHWEST, DIR_NORTH, DIR_SOUTH, DIR_NORTHEAST, DIR_EAST, DIR_SOUTHEAST],
    ];
    let order = ORDERS[c.rnd_value(0, 1) as usize];
    for d in order {
        let (tx, ty) = (x + DIRX[d as usize], y + DIRY[d as usize]);
        if count_oncell(c, m, tx, ty, kinds, flag) == 0 && getcell(c, m, tx, ty, CellChk::ChkPass) {
            return Some((tx, ty));
        }
    }
    None
}

/// All players on a map (map_foreachinmap(..., BL_PC)).
pub fn players_on_map(c: &Ctx, m: u16) -> Vec<Unit> {
    c.db().unit().by_map_kind().filter((m, BL_PC)).filter(|u| u.on_map).collect()
}

/// conf/battle/misc.conf `map_edge_size: 15`.
pub fn map_edge_size() -> i16 {
    crate::battle_config::get(crate::battle_config::Bc::MAP_EDGE_SIZE) as i16
}

/// map.cpp map_search_freecell with no source unit (flag & 1: search around (x, y) on map `m`; flag & 2 needs a
/// source and is not supported here; flag & 4 only matters with no_spawn_on_player > 0, which is 0). 50 tries.
/// Some(cell) on success; None leaves the caller's coordinates as they were (rAthena restores them).
pub fn search_freecell(c: &Ctx, m: u16, x: i16, y: i16, rx: i16, ry: i16, flag: i32) -> Option<(i16, i16)> {
    debug_assert!(flag & 1 != 0 && flag & 2 == 0, "map_search_freecell: Incorrect usage");
    let (bx, by) = (x, y);
    if rx == 0 && ry == 0 {
        return getcell(c, m, bx, by, CellChk::ChkReach).then_some((bx, by));
    }
    let md = data::maps().get(m)?;
    let edge = map_edge_size();
    let edge_valid = edge.min(5);
    let mut tries = 50;
    while tries > 0 {
        tries -= 1;
        // rnd_value on int32 (bx ± rx may leave the int16 range)
        let cx = if rx >= 0 { c.rnd_value(bx as i32 - rx as i32, bx as i32 + rx as i32) as i16 } else { c.rnd_value(edge as i32, (md.xs - edge - 1) as i32) as i16 };
        let cy = if ry >= 0 { c.rnd_value(by as i32 - ry as i32, by as i32 + ry as i32) as i16 } else { c.rnd_value(edge as i32, (md.ys - edge - 1) as i32) as i16 };
        if cx == bx && cy == by {
            continue;
        }
        if cx < edge_valid || cx > md.xs - edge_valid || cy < edge_valid || cy > md.ys - edge_valid {
            continue;
        }
        if getcellp(md, cx, cy, CellChk::ChkReach) {
            return Some((cx, cy));
        }
    }
    None
}

/// map.cpp's `last_object_id` (map_get_new_object_id's cursor).
#[spacetimedb::table(accessor = object_id_state)]
pub struct ObjectIdState {
    #[primary_key]
    pub key: u8,
    pub last_object_id: u32,
}

/// map.hpp MIN_FLOORITEM / MAX_FLOORITEM (START_ACCOUNT_NUM).
pub const MIN_FLOORITEM: u32 = 2;
pub const MAX_FLOORITEM: u32 = 2_000_000;

/// map.cpp map_get_new_object_id: the next free id in [MIN_FLOORITEM, MAX_FLOORITEM) after the last one handed out
/// (floor items, chat rooms, skill units…). "Free" = no object with it in id_db: no `unit` row, no chat room. 0 when
/// every id is taken.
pub fn get_new_object_id(c: &Ctx) -> u32 {
    use spacetimedb::Table;
    let last = c.db().object_id_state().key().find(0).map(|r| r.last_object_id).unwrap_or(MIN_FLOORITEM - 1);
    let taken = |i: u32| c.db().unit().id().find(i).is_some() || c.db().chat_room().id().find(i).is_some();
    let mut i = last + 1;
    while i != last {
        if i == MAX_FLOORITEM {
            i = MIN_FLOORITEM;
        }
        if !taken(i) {
            break;
        }
        i += 1;
    }
    if i == last {
        log::error!("map_addobject: no free object id!");
        return 0;
    }
    // update cursor
    if c.db().object_id_state().key().find(0).is_some() {
        c.db().object_id_state().key().update(ObjectIdState { key: 0, last_object_id: i });
    } else {
        c.db().object_id_state().insert(ObjectIdState { key: 0, last_object_id: i });
    }
    i
}

use crate::chat::chat_room as _;
