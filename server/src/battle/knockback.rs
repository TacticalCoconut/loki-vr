//! Knockback: unit.cpp unit_blown / unit_blown_immune and path.cpp path_blownpos (skill_blown, the skills' wrapper
//! around them, is phase 4). clif_blown / clif_slide are in battle/clif.rs.

use crate::map::{self, CellChk, BL_MOB, BL_PC, BL_SKILL};
use crate::status::defs::MD_KNOCKBACKIMMUNE;
use crate::unit::{self, Unit, USW_NONE};
use crate::world::Ctx;

/// unit.hpp enum e_unit_blown.
pub const UB_KNOCKABLE: i32 = 0;
pub const UB_NO_KNOCKBACK_MAP: i32 = 1;
pub const UB_MD_KNOCKBACK_IMMUNE: i32 = 2;
pub const UB_TARGET_BASILICA: i32 = 3;
pub const UB_TARGET_NO_KNOCKBACK: i32 = 4;
pub const UB_TARGET_TRAP: i32 = 5;

/// skill.hpp e_skill_blown BLOWN_DONT_SEND_PACKET.
pub const BLOWN_DONT_SEND_PACKET: i32 = 0x01;

/// path.cpp path_blownpos: the reachable cell `count` cells from (x0, y0) in direction (dx, dy) (path_blown_halt:
/// yes stops at the first blocked cell).
pub fn path_blownpos(c: &Ctx, m: u16, x0: i16, y0: i16, dx: i16, dy: i16, count: i32) -> Option<(i16, i16)> {
    let md = crate::data::maps().get(m)?;
    let (mut x0, mut y0, mut dx, mut dy, mut count) = (x0, y0, dx, dy, count);
    if count > 25 {
        // Cap to prevent too much processing...?
        log::warn!("path_blownpos: count too many {count} !");
        count = 25;
    }
    if !(-1..=1).contains(&dx) || !(-1..=1).contains(&dy) {
        log::error!("path_blownpos: illegal dx={dx} or dy={dy} !");
        dx = dx.signum();
        dy = dy.signum();
    }
    while count > 0 && (dx != 0 || dy != 0) {
        if !map::getcellp(md, x0 + dx, y0 + dy, CellChk::ChkPass) {
            if super::conf::path_blown_halt() {
                break;
            }
            // attempt partial movement
            let fx = dx != 0 && map::getcellp(md, x0 + dx, y0, CellChk::ChkPass);
            let fy = dy != 0 && map::getcellp(md, x0, y0 + dy, CellChk::ChkPass);
            if fx && fy {
                if c.rnd_chance(50, 100) {
                    dx = 0;
                } else {
                    dy = 0;
                }
            }
            if !fx {
                dx = 0;
            }
            if !fy {
                dy = 0;
            }
        }
        x0 += dx;
        y0 += dy;
        count -= 1;
    }
    Some((x0, y0))
}

/// unit.cpp unit_blown_immune. flag 0x1 offensive, 0x2 knockback (not a stop), 0x4 boss attack, 0x8 ignore the
/// target player's bNoKnockback.
pub fn unit_blown_immune(c: &Ctx, bl: &Unit, flag: u8) -> i32 {
    if flag & 0x1 != 0
        && (crate::npc::mapflag_gvg2(c, bl.map) || crate::npc::mapflag(c, bl.map, crate::npc::MF_BATTLEGROUND))
        && (flag & 0x2 != 0 || super::conf::skill_trap_type() & 0x1 == 0)
    {
        return UB_NO_KNOCKBACK_MAP; // No knocking back in WoE / BG
    }
    match bl.kind {
        BL_MOB => {
            // Immune can't be knocked back
            if flag & 0x1 != 0 && super::bl::has_mode(c, bl, MD_KNOCKBACKIMMUNE) && (flag & 0x2 != 0 || super::conf::skill_trap_type() & 0x2 == 0) {
                return UB_MD_KNOCKBACK_IMMUNE;
            }
        }
        BL_PC => {
            // (Basilica: pre-renewal only) Target has special_state.no_knockback (equip)
            let no_kb = crate::status::data::sd_get(c, bl.id).map(|s| s.special_state.no_knockback).unwrap_or(false);
            if flag & (0x1 | 0x2) != 0 && flag & 0x8 == 0 && no_kb {
                return UB_TARGET_NO_KNOCKBACK;
            }
        }
        BL_SKILL => {
            // Trap cannot be knocked back
            let g = crate::skillunit::group_of_unit(c, bl.id);
            if g.map(|g| crate::db::skills::skill_get_unit_flag(g.skill_id, &[crate::db::skills::UF_NOKNOCKBACK])).unwrap_or(false) {
                return UB_TARGET_TRAP;
            }
        }
        _ => {}
    }
    UB_KNOCKABLE
}

/// unit.cpp unit_blown: pushes a unit `count` cells toward (dx, dy) with only the map cells checked. Returns the
/// cells it moved.
pub fn unit_blown(c: &Ctx, id: u32, dx: i16, dy: i16, count: i32, flag: i32) -> i32 {
    if count == 0 {
        return 0;
    }
    let Some(mut u) = unit::get(c, id) else { return 0 };
    let Some((nx, ny)) = path_blownpos(c, u.map, u.x, u.y, dx, dy, count) else { return 0 };
    if u.kind != BL_SKILL {
        unit::stop_walking(c, &mut u, USW_NONE, 0);
    }
    if u.kind == BL_PC {
        unit::stop_stepaction(c, &mut u); // Stop stepaction when knocked back
        u.ud.to_x = nx;
        u.ud.to_y = ny;
    }
    unit::save(c, &u);
    let (dx, dy) = (nx - u.x, ny - u.y);
    if dx != 0 || dy != 0 {
        let kinds = if u.kind == BL_PC { map::BL_ALL } else { BL_PC };
        for other in map::units_in_move_area(c, &u, crate::conf::area_size(), dx, dy, kinds) {
            crate::clif::outsight(c, &other, &u);
        }
        if u.kind == BL_SKILL {
            // a knockback group moves as a whole, a single unit alone (skillunit's port)
            match crate::skillunit::group_of_unit(c, u.id) {
                Some(g) if crate::db::skills::skill_get_unit_flag(g.skill_id, &[crate::db::skills::UF_KNOCKBACKGROUP]) => {
                    crate::skillunit::skill_unit_move_unit_group(c, g.group_id, u.map, dx, dy)
                }
                _ => crate::skillunit::skill_unit_move_unit(c, &u, nx, ny),
            }
            if let Some(fresh) = unit::get(c, id) {
                u = fresh;
            }
        } else {
            unit::moveblock(c, &mut u, nx, ny, c.tick());
            unit::save(c, &u);
        }
        for other in map::units_in_move_area(c, &u, crate::conf::area_size(), -dx, -dy, kinds) {
            crate::clif::insight(c, &other, &u);
        }
        if flag & BLOWN_DONT_SEND_PACKET == 0 {
            super::clif::clif_blown(c, &u);
        }
        if u.kind == BL_PC {
            if map::getcell(c, u.map, u.x, u.y, CellChk::ChkNpc) {
                crate::npc::touch_area_allnpc(c, u.id, u.map, u.x, u.y);
            } else {
                crate::pc::clear_areanpc(c, u.id);
            }
        }
    }
    crate::path::distance(dx as i32, dy as i32)
}
