//! map.cpp's area iterators as the unit code uses them: the block_list pointers a map_foreachin* call collects first
//! (in the block order map.rs gives), then the function called for each one still in the block list (`bl->prev`)
//! when its turn comes, read fresh, as rAthena's pointers see the current object.

use crate::map::{self, CellChk};
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// The ids map_foreachincell collects on (x, y) (map.cpp map_foreachincell: out-of-map cells give none).
pub fn ids_in_cell(c: &Ctx, m: u16, x: i16, y: i16, kinds: u16) -> Vec<u32> {
    let Some(md) = crate::data::maps().get(m) else { return Vec::new() };
    if x < 0 || y < 0 || x >= md.xs || y >= md.ys {
        return Vec::new();
    }
    map::units_in_area(c, m, x, y, x, y, kinds).into_iter().map(|u| u.id).collect()
}

/// The ids map_foreachinrangeV collects around `center` (a square: CIRCULAR_AREA is not defined), with the shootable
/// path check when `wall_check` (map_foreachinrange: battle_config.skill_wall_check; map_foreachinallrange: no check).
pub fn ids_in_range(c: &Ctx, center: &Unit, range: i16, kinds: u16, wall_check: bool) -> Vec<u32> {
    map::units_in_range(c, center.map, center.x, center.y, range, kinds)
        .into_iter()
        .filter(|u| !wall_check || crate::path::path_search_long(center.map, center.x, center.y, u.x, u.y, CellChk::ChkWall))
        .map(|u| u.id)
        .collect()
}

/// map_foreachinrange (with skill_wall_check).
pub fn ids_in_range_wc(c: &Ctx, center: &Unit, range: i16, kinds: u16) -> Vec<u32> {
    ids_in_range(c, center, range, kinds, super::conf::skill_wall_check())
}

/// map_foreachinallrange.
pub fn ids_in_allrange(c: &Ctx, center: &Unit, range: i16, kinds: u16) -> Vec<u32> {
    ids_in_range(c, center, range, kinds, false)
}

/// The ids map_foreachinareaV collects in (x0, y0)-(x1, y1); with `wall_check` a shootable path from the area's
/// centre is required (map_foreachinarea with skill_wall_check).
#[allow(clippy::too_many_arguments)]
pub fn ids_in_area(c: &Ctx, m: u16, x0: i16, y0: i16, x1: i16, y1: i16, kinds: u16, wall_check: bool) -> Vec<u32> {
    let (mut x0, mut y0, mut x1, mut y1) = (x0, y0, x1, y1);
    if x1 < x0 {
        std::mem::swap(&mut x0, &mut x1);
    }
    if y1 < y0 {
        std::mem::swap(&mut y0, &mut y1);
    }
    let Some(md) = crate::data::maps().get(m) else { return Vec::new() };
    let (cx0, cy0, cx1, cy1) = (x0.max(0), y0.max(0), x1.min(md.xs - 1), y1.min(md.ys - 1));
    let (cx, cy) = (cx0 + (cx1 - cx0) / 2, cy0 + (cy1 - cy0) / 2);
    map::units_in_area(c, m, x0, y0, x1, y1, kinds)
        .into_iter()
        .filter(|u| !wall_check || crate::path::path_search_long(m, cx, cy, u.x, u.y, CellChk::ChkWall))
        .map(|u| u.id)
        .collect()
}

/// Calls `f` for each collected id whose unit is still in the block list (`bl_list[i]->prev`), with its current row;
/// returns the sum of `f`'s results.
pub fn each(c: &Ctx, ids: &[u32], mut f: impl FnMut(&Unit) -> i32) -> i32 {
    let mut sum = 0;
    for &id in ids {
        let Some(u) = unit::get(c, id) else { continue };
        if !u.on_map {
            continue;
        }
        sum += f(&u);
    }
    sum
}
