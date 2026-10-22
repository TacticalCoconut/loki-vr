//! unit.cpp for mercenaries (BL_MER) and elementals (BL_ELEM): unit_remove_map's arm (the can-act tick follows the
//! can-move tick; a companion whose contract / summon time is over is freed unless its master is logging out),
//! unit_free (saved while it has time left, else deleted at the char server and the master's id cleared; cooldowns,
//! contract / summon timer, skill units, status changes), unit_warp, the master teleport timer
//! (unit_check_start_teleport_timer / unit_teleport_timer), unit_remove_map_pc / unit_free_pc's companion part and
//! pc_setpos's (the companions follow their master to the new map).

use super::Comp;
use crate::map::{self, CellChk, BL_ELEM, BL_MER};
use crate::timer::{self, Func};
use crate::unit::{self, Unit, CLR_DEAD, CLR_OUTSIGHT, CLR_RESPAWN, CLR_TELEPORT};
use crate::world::Ctx;

/// mercenary_get_lifetime / elemental_get_lifetime.
pub fn lifetime(c: &Ctx, x: &Comp) -> i64 {
    match x {
        Comp::Mer(md) => crate::mercenary::mercenary_get_lifetime(c, md),
        Comp::Elem(ed) => crate::elemental::elemental_get_lifetime(c, ed),
    }
}

/// unit.cpp unit_remove_map's BL_MER / BL_ELEM arm, run by unit::remove_map after the status part:
/// `ud->canact_tick = ud->canmove_tick`, and a companion without time left is taken off the map and freed (unless its
/// master is logging out: unit_free saves it then). Some(false): freed (unit_remove_map returns 0); None: go on.
pub fn remove_map_arm(c: &Ctx, u: &mut Unit, clrtype: u8) -> Option<bool> {
    u.ud.canact_tick = u.ud.canmove_tick;
    let x = super::get(c, u.id)?;
    let master_logging_out = crate::pc::get(c, x.master()).map(|p| !p.active).unwrap_or(false);
    if lifetime(c, &x) <= 0 && !master_logging_out {
        crate::clif::clearunit_area(c, u, clrtype);
        u.on_map = false; // map_delblock
        unit::save(c, u);
        unit_free(c, u.id, CLR_OUTSIGHT);
        return Some(false);
    }
    None
}

/// unit.cpp unit_free for a mercenary / an elemental: off the map (if on it); saved while it has time left, else
/// deleted at the char server and the master's mer_id / ele_id cleared; the master loses sd->md / sd->ed; the
/// mercenary's cooldowns (skill_blockmerc_clear) and the contract / summon timer end; its skill units and status
/// changes go; then the object itself (map_deliddb / map_freeblock). Returns 0.
pub fn unit_free(c: &Ctx, id: u32, clrtype: u8) -> i32 {
    if unit::get(c, id).map(|u| u.on_map).unwrap_or(false) {
        // Players are supposed to logout with a "warp" effect.
        unit::remove_map(c, id, clrtype);
    }
    // (unit_remove_map's arm may have freed it already)
    let Some(x) = super::get(c, id) else { return 0 };
    match x {
        Comp::Mer(md) => {
            let sd = crate::pc::get(c, md.master).map(|_| md.master);
            if crate::mercenary::mercenary_get_lifetime(c, &md) > 0 {
                crate::mercenary::mercenary_save(c, md.id);
            } else {
                crate::mercenary::intif_mercenary_delete(md.mercenary.mercenary_id);
                if let Some(aid) = sd {
                    crate::mercenary::data::set_mer_id(c, aid, 0);
                }
            }
            // (if( sd ) sd->md = nullptr: the row goes below)
            crate::mercenary::skill::skill_blockmerc_clear(c, id); // Clear all skill cooldown related
            crate::mercenary::mercenary_contract_stop(c, id);
            // md->master = nullptr: status_change_clear below finds no master (its status_calc_bl_ sends nothing)
            if let Some(mut md) = crate::mercenary::data::get(c, id) {
                md.master = 0;
                crate::mercenary::data::put(c, &md);
            }
        }
        Comp::Elem(ed) => {
            let sd = crate::pc::get(c, ed.master).map(|_| ed.master);
            if crate::elemental::elemental_get_lifetime(c, &ed) > 0 {
                crate::elemental::elemental_save(c, ed.id);
            } else {
                crate::elemental::intif_elemental_delete(ed.elemental.elemental_id);
                if let Some(aid) = sd {
                    crate::elemental::data::set_ele_id(c, aid, 0);
                }
            }
            // (if( sd ) sd->ed = nullptr: the row goes below)
            crate::elemental::elemental_summon_stop(c, id);
            // ed->master = nullptr: status_change_clear below finds no master (its status_calc_bl_ sends nothing)
            if let Some(mut ed) = crate::elemental::data::get(c, id) {
                ed.master = 0;
                crate::elemental::data::put(c, &ed);
            }
        }
    }
    crate::skillunit::skill_clear_unitgroup(c, id);
    crate::status::sc::status_change_clear(c, id, 1);
    // ud->hatEffects.clear(), map_deliddb, map_freeblock
    crate::pc_misc::set_hat_effects(c, id, Vec::new());
    if let Some(x) = super::get(c, id) {
        if x.masterteleport_timer() != 0 {
            // (a timer of a freed object finds nothing: map_id2bl fails)
            timer::delete(c, x.masterteleport_timer());
        }
    }
    crate::status::sc::free_unit(c, id);
    crate::skillunit::data::on_unit_freed(c, id);
    crate::skill::timerskill::free_unit(c, id);
    crate::skill::unit::free_unit(c, id);
    if let Some(u) = unit::get(c, id) {
        crate::unit::freed_keep(u);
    }
    {
        use crate::elemental::data::elemental_data;
        use crate::mercenary::data::mercenary_data;
        c.db().mercenary_data().id().delete(id);
        c.db().elemental_data().id().delete(id);
    }
    {
        use crate::unit::unit as _;
        c.db().unit().id().delete(id);
    }
    0
}

/// unit.cpp unit_warp for a mercenary / an elemental: a random or checked cell, off the map (unit_remove_map), then on
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

/// unit.cpp unit_check_start_teleport_timer for the companion `id` (BL_MER: MAX_MER_DISTANCE, BL_ELEM:
/// MAX_ELEDISTANCE): out of range of its master, a 3 s timer warps it back unless the master comes back; in range, a
/// running timer is cancelled.
pub fn unit_check_start_teleport_timer(c: &Ctx, id: u32) {
    let Some(mut x) = super::get(c, id) else { return };
    let max_dist = match x.kind() {
        BL_ELEM => crate::elemental::data::MAX_ELEDISTANCE,
        BL_MER => crate::mercenary::data::MAX_MER_DISTANCE,
        _ => return,
    };
    let (Some(msd), Some(sbl)) = (unit::get(c, x.master()), unit::get(c, id)) else { return };
    if !crate::mob::unit::check_distance_bl(&msd, &sbl, max_dist) {
        if x.masterteleport_timer() == 0 {
            let t = timer::add(c, c.tick() + 3000, Func::CompanionTeleportTimer, id, max_dist as i64);
            x.set_masterteleport_timer(t);
            super::put(c, &x);
        }
    } else {
        if x.masterteleport_timer() != 0 {
            timer::delete(c, x.masterteleport_timer());
        }
        x.set_masterteleport_timer(0); // Cancel recall
        super::put(c, &x);
    }
}

/// unit_walktoxy's "Start timer to recall summon" for the master `aid`: sd->md, then sd->ed (the homunculus and the
/// pet follow in unit.rs).
pub fn master_walks(c: &Ctx, aid: u32) {
    if let Some(md) = crate::mercenary::data::of_master(c, aid) {
        unit_check_start_teleport_timer(c, md.id);
    }
    if let Some(ed) = crate::elemental::data::of_master(c, aid) {
        unit_check_start_teleport_timer(c, ed.id);
    }
}

/// unit.cpp unit_teleport_timer for a companion (Func::CompanionTeleportTimer; target = the companion, data = the
/// distance): still too far from its master: unit_warp to him (CLR_TELEPORT).
pub fn unit_teleport_timer(c: &Ctx, tid: u64, id: u32, data: i64) {
    let Some(mut x) = super::get(c, id) else { return };
    if x.masterteleport_timer() != tid {
        return;
    }
    x.set_masterteleport_timer(0);
    super::put(c, &x);
    let (Some(msd), Some(bl)) = (unit::get(c, x.master()), unit::get(c, id)) else { return };
    if !crate::mob::unit::check_distance_bl(&msd, &bl, data as i32) {
        unit_warp(c, id, msd.map as i32, msd.x, msd.y, CLR_TELEPORT);
    }
}

/// unit.cpp unit_remove_map_pc's companion part (after the player, the pet and the homunculus): sd->md, then sd->ed;
/// CLR_RESPAWN / CLR_TELEPORT become CLR_OUTSIGHT for them.
pub fn unit_remove_map_pc(c: &Ctx, aid: u32, clrtype: u8) {
    let clrtype = if clrtype == CLR_RESPAWN || clrtype == CLR_TELEPORT { CLR_OUTSIGHT } else { clrtype };
    if let Some(md) = crate::mercenary::data::of_master(c, aid) {
        unit::remove_map(c, md.id, clrtype);
    }
    if let Some(ed) = crate::elemental::data::of_master(c, aid) {
        unit::remove_map(c, ed.id, clrtype);
    }
}

/// unit.cpp unit_free_pc's companion part (after the pet and the homunculus, before the player): sd->md, then sd->ed.
pub fn unit_free_pc(c: &Ctx, aid: u32) {
    if let Some(md) = crate::mercenary::data::of_master(c, aid) {
        unit_free(c, md.id, CLR_OUTSIGHT);
    }
    if let Some(ed) = crate::elemental::data::of_master(c, aid) {
        unit_free(c, ed.id, CLR_OUTSIGHT);
    }
    // An elemental deleted while its master was logging out (elemental_delete: sd->ed = nullptr, unit_remove_map keeps
    // it) stays in rAthena's memory, unreachable, with no packet or char-server request ever again. Deliberately
    // different: its rows go with the master, as the table keys elementals by their master (his next summon needs
    // the key).
    use crate::elemental::data::elemental_data;
    use crate::unit::unit as _;
    if let Some(ed) = c.db().elemental_data().master().find(aid).filter(|ed| ed.detached) {
        crate::status::sc::free_unit(c, ed.id);
        c.db().elemental_data().id().delete(ed.id);
        c.db().unit().id().delete(ed.id);
    }
}

/// pc.cpp pc_setpos's companion part (pc.cpp:7159-7172): the mercenary, then the elemental, take the master's map, cell
/// (as their walk target too) and direction; they are put back on the map at the master's LoadEndAck.
pub fn pc_setpos(c: &Ctx, aid: u32, m: u16, x: i16, y: i16) {
    let dir = unit::get(c, aid).map(|u| u.ud.dir).unwrap_or(0);
    let ids = [crate::mercenary::data::of_master(c, aid).map(|m| m.id), crate::elemental::data::of_master(c, aid).map(|e| e.id)];
    for id in ids.into_iter().flatten() {
        if let Some(mut u) = unit::get(c, id) {
            u.map = m;
            u.x = x;
            u.ud.to_x = x;
            u.y = y;
            u.ud.to_y = y;
            u.ud.dir = dir;
            unit::save(c, &u);
        }
    }
}
