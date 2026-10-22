//! unit.cpp for BL_HOM: unit_remove_map's homunculus arm, unit_free's homunculus arm, unit_warp, and the master
//! teleport timer (unit_check_start_teleport_timer / unit_teleport_timer). The generic parts of unit_remove_map are
//! crate::unit::remove_map's, which hands the BL_HOM arm to [`remove_map_hom`].

use super::data::*;
use crate::map::{self, CellChk};
use crate::mob::unit::check_distance_bl;
use crate::timer::{self, Func};
use crate::unit::{self, Unit, CLR_DEAD, CLR_OUTSIGHT};
use crate::world::Ctx;


/// unit_remove_map(hd, clrtype) (crate::unit::remove_map runs the generic part and [`remove_map_hom`]).
pub fn unit_remove_map(c: &Ctx, id: u32, clrtype: u8) -> bool {
    unit::remove_map(c, id, clrtype)
}

/// unit.cpp unit_remove_map_'s BL_HOM arm (unit.cpp:3758-3769): "It appears HOM do reset the can-act tick", and a
/// homunculus without intimacy (unless its master is logging out) vanishes and is freed. False = freed (the caller
/// returns 0), `u` is saved either way.
pub fn remove_map_hom(c: &Ctx, u: &mut Unit, clrtype: u8) -> bool {
    u.ud.canact_tick = u.ud.canmove_tick; // It appears HOM do reset the can-act tick.
    unit::save(c, u);
    let Some(hd) = get(c, u.id) else { return true };
    let master_logging_out = crate::pc::get(c, hd.master).map(|p| !p.active).unwrap_or(false);
    if hd.homunculus.intimacy == 0 && !master_logging_out {
        // If logging out, this is deleted on unit_free
        crate::clif::clearunit_area(c, u, clrtype);
        u.on_map = false; // map_delblock
        unit::save(c, u);
        unit_free(c, u.id, CLR_OUTSIGHT);
        return false;
    }
    true
}

/// unit.cpp unit_free for a homunculus: off the map (if on it), the hunger timer stops, the homunculus is saved (or
/// deleted when it has no intimacy left: the master's hom_id goes and SC_HOMUN_TIME ends), its cooldowns end, the
/// master loses sd->hd, its ground skills and status changes go, then the rows (map_deliddb / map_freeblock).
pub fn unit_free(c: &Ctx, id: u32, clrtype: u8) -> i32 {
    if unit::get(c, id).map(|u| u.on_map).unwrap_or(false) {
        unit::remove_map(c, id, clrtype);
    }
    let Some(hd) = get(c, id) else {
        // freed meanwhile (unit_remove_map's no-intimacy branch)
        return 0;
    };
    let aid = hd.master;
    let sd = crate::pc::get(c, aid).is_some();
    super::hom_hungry_timer_delete(c, id);
    let Some(hd) = get(c, id) else { return 0 };
    if hd.homunculus.intimacy > 0 {
        super::hom_save(c, id);
    } else {
        crate::int_homun::intif_homunculus_requestdelete(hd.homunculus.hom_id);
        if sd {
            super::set_hom_id(c, aid, 0);
        }
        // RENEWAL: status_change_end(sd, SC_HOMUN_TIME) (sd may be nullptr: status_change_end returns at once)
        if sd {
            crate::status::sc::status_change_end(c, aid, crate::status::consts::SC_HOMUN_TIME, 0);
        }
    }
    super::cooldown::skill_blockhomun_clear(c, id); // Clear all skill cooldown related
    // if( sd ) sd->hd = nullptr; hd->master = nullptr; (the row goes below; its status changes end unattached)
    detach(id);
    crate::skillunit::skill_clear_unitgroup(c, id);
    crate::status::sc::status_change_clear(c, id, 1);
    let Some(hd) = get(c, id) else { return 0 };
    if hd.masterteleport_timer != 0 {
        timer::delete(c, hd.masterteleport_timer);
    }
    // ud->hatEffects.clear(), map_deliddb, map_freeblock
    crate::pc_misc::set_hat_effects(c, id, Vec::new());
    crate::status::sc::free_unit(c, id);
    crate::skillunit::data::on_unit_freed(c, id);
    crate::skill::timerskill::free_unit(c, id);
    crate::skill::unit::free_unit(c, id);
    timer::delete_for_target(c, id);
    if let Some(u) = unit::get(c, id) {
        unit::freed_keep(u);
    }
    c.db().hom_data().id().delete(id);
    c.db().unit().id().delete(id);
    forget_detached(id);
    0
}

/// unit.cpp unit_warp for a homunculus (hom_call, unit_teleport_timer, the unitwarp script command): a random or
/// checked cell, off the map, the ground skills cleared on a map change (clear_skills_on_warp has BL_HOM), then on
/// the new cell with its spawn packets and skill_unit_move. 0 success, 1 refused, 2 no cell, 3 removal failed, 4
/// map_addblock failed.
pub fn unit_warp(c: &Ctx, id: u32, m: i32, x: i16, y: i16, clrtype: u8) -> i32 {
    let Some(u) = unit::get(c, id) else { return 1 };
    if !u.on_map {
        return 1;
    }
    if clrtype == CLR_DEAD {
        // Type 1 is invalid, since you shouldn't warp a bl with the "death" animation, it messes up with
        // unit_remove_map! [Skotlex]
        return 1;
    }
    let m = if m < 0 { u.map } else { m as u16 };
    let (mut x, mut y) = (x, y);
    if x < 0 || y < 0 {
        // Random map position.
        match crate::mob::spawn::map_search_freecell(c, None, m as i32, x, y, -1, -1, 1, 50) {
            Some((nx, ny)) => {
                x = nx;
                y = ny;
            }
            None => {
                log::warn!("unit_warp failed. Unit Id:{id}/Type:{}, target position map {m} at [{x},{y}]", u.kind);
                return 2;
            }
        }
    } else if map::getcell(c, m, x, y, CellChk::ChkNoReach) {
        // Invalid target cell
        log::warn!("unit_warp: Specified non-walkable target cell: {m} at [{x},{y}]");
        match crate::mob::spawn::map_search_freecell(c, None, m as i32, x, y, 4, 4, 1, 50) {
            Some((nx, ny)) => {
                x = nx;
                y = ny;
            }
            None => {
                // Can't find a nearby cell
                log::warn!("unit_warp failed. Unit Id:{id}/Type:{}, target position map {m} at [{x},{y}]", u.kind);
                return 2;
            }
        }
    }
    if !unit::remove_map(c, id, clrtype) {
        return 3;
    }
    if unit::get(c, id).map(|u| u.map != m && crate::skillunit::conf::clear_unit_onwarp() & u.kind != 0).unwrap_or(false) {
        crate::skillunit::skill_clear_unitgroup(c, id);
    }
    let Some(mut u) = unit::get(c, id) else { return 3 };
    u.x = x;
    u.ud.to_x = x;
    u.y = y;
    u.ud.to_y = y;
    u.map = m;
    if !unit::addblock(c, &mut u) {
        return 4; //error on adding bl to map
    }
    crate::clif::spawn(c, &u, false);
    crate::skillunit::skill_unit_move_id(c, id, c.tick(), 1);
    0
}

/// unit.cpp unit_check_start_teleport_timer for the homunculus of player `aid` (unit_walktoxy of the master, unit.cpp:
/// 909-910): a homunculus more than AREA_SIZE away from its master is warped to him in 3 s unless he comes back.
pub fn unit_check_start_teleport_timer(c: &Ctx, aid: u32) {
    let Some(mut hd) = of_master(c, aid) else { return };
    let Some(hu) = unit::get(c, hd.id) else { return };
    let Some(msd) = unit::get(c, aid) else { return };
    let max_dist = crate::conf::area_size() as i32;
    if !check_distance_bl(&msd, &hu, max_dist) {
        if hd.masterteleport_timer == 0 {
            hd.masterteleport_timer = timer::add(c, c.tick() + 3000, Func::UnitTeleportTimer, hd.id, max_dist as i64);
            put(c, &hd);
        }
    } else {
        if hd.masterteleport_timer != 0 {
            timer::delete(c, hd.masterteleport_timer);
        }
        hd.masterteleport_timer = 0; // Cancel recall
        put(c, &hd);
    }
}

/// unit.cpp unit_teleport_timer for a homunculus (target = its block id, data = the distance): still too far from its
/// master: unit_warp to him (CLR_TELEPORT). False when `id` is not a homunculus (the pet's timer).
pub fn unit_teleport_timer(c: &Ctx, tid: u64, id: u32, data: i64) -> bool {
    let Some(mut hd) = get(c, id) else { return false };
    if hd.masterteleport_timer != tid {
        return true;
    }
    hd.masterteleport_timer = 0;
    put(c, &hd);
    let (Some(msd), Some(hu)) = (unit::get(c, hd.master), unit::get(c, id)) else { return true };
    if crate::pc::get(c, hd.master).is_some() && !check_distance_bl(&msd, &hu, data as i32) {
        unit_warp(c, id, msd.map as i32, msd.x, msd.y, unit::CLR_TELEPORT);
    }
    true
}

use crate::unit::unit as _;
