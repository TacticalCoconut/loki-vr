//! Unit movement commands: unitwalk / unitwalkto, unitwarp, unitattack / unitstopattack, pcfollow / pcstopfollow,
//! pushpc; and checkvending.
//! The unit.cpp pieces come from the mob agent (unit_can_reach_pos / _bl, unit_walktobl, unit_delay_walktobl_timer,
//! unit_warp for monsters) and the battle agent (unit_blown); unit_warp's player and NPC branches are here.

use super::unitctl::{script_mapid2sd, script_rid2bl, Bl};
use crate::map::{BL_MOB, BL_NPC, BL_PC};
use crate::mob::unit::{check_distance_bl, distance_bl, unit_can_reach_bl, unit_can_reach_pos, unit_walktobl};
use crate::npc::{npc, Npc};

use crate::pc_misc;
use crate::script::buildins::*;
use crate::script::vm::*;
use crate::timer::{self, Func};
use crate::unit;
use crate::world::{Ctx, Tick};

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "unitwalk" => unitwalk,
        "unitwarp" => unitwarp,
        "unitattack" => unitattack,
        "unitstopattack" => unitstopattack,
        "pcfollow" => pcfollow,
        "pcstopfollow" => pcstopfollow,
        "pushpc" => pushpc,
        "checkvending" => checkvending,
        _ => return None,
    })
}

/// script.cpp check_event: the old "Label" form (without ::On) is deprecated.
fn check_event(rt: &Rt, st: &State, evt: &[u8]) {
    if !evt.is_empty() && !evt.to_ascii_lowercase().windows(4).any(|w| w == b"::on") {
        log::warn!("NPC event parameter deprecated! Please use 'NPCNAME::OnEVENT' instead of '{}'.", lat(evt));
        reportsrc(rt, st);
    }
}

// ---------------------------------------------------------------------------------------------- unitwalk

/// unitwalk <unit id>,<x>,<y>{,"<event label>"} / unitwalkto <unit id>,<target id>{,"<event label>"}: the unit is
/// forced to walk there (unit_delay_walktoxy_timer / unit_delay_walktobl_timer 50 ms later "to avoid mismatches");
/// the event runs when it arrives (walk_done_event, unit_npc.rs). Pushes whether the destination can be reached.
/// rAthena quirk kept: `if (script_pushint(st, unit_can_reach_pos(...)))` tests push_val2's returned pointer, not the
/// value, so force_walk is set and the delayed walk is scheduled even when the answer is 0 — the unit then stays
/// "forced to walk" (refusing later unitwalk calls) until it arrives somewhere or npcstop / unitstopwalk clear it.
fn unitwalk(rt: &Rt, st: &mut State) -> i32 {
    let cmd = if st.funcname == "unitwalk" { "unitwalk" } else { "unitwalkto" };
    let Some(bl) = script_rid2bl(rt, st, 2, &format!("buildin_{cmd}")) else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let c = rt.c.unwrap();
    // Unit was already forced to walk. (A floating NPC has its unit_data in rAthena, never forced to walk.)
    if bl.unit().map(|u| u.ud.force_walk).unwrap_or(false) {
        st.pushint(0);
        log::warn!("buildin_{cmd}: Unit has already been forced to walk and not reached it's destination yet.");
        return SCRIPT_CMD_FAILURE;
    }
    if bl.kind() == BL_NPC {
        super::unitinfo::npc_status_calc_first_or_none(c, bl.id());
    }
    let mut off = 5;
    if cmd == "unitwalk" {
        let x = getnum(rt, st, 3);
        let y = getnum(rt, st, 4);
        // unit_can_reach_pos(bl, x, y, 0): a unit without a map (bl->m -1) finds no path
        let ok = match bl.unit() {
            Some(u) if u.on_map => unit_can_reach_pos(u.map, u.x, u.y, x as i16, y as i16, 0),
            _ => false,
        };
        st.pushint(ok as i64);
        // (always: see the quirk above)
        set_force_walk(c, bl.id());
        let data = ((x as i64) << 16) | (y as i64 & 0xFFFF);
        timer::add(c, c.tick() + 50, Func::UnitDelayWalk, bl.id(), data); // Need timer to avoid mismatches
    } else {
        let tid = getnum(rt, st, 3);
        let tbl = super::unitctl::map_id2bl(c, tid as u32);
        let Some(tbl) = tbl else {
            log::error!("buildin_unitwalk: Bad target destination.");
            st.pushint(0);
            return SCRIPT_CMD_FAILURE;
        };
        let ok = match (bl.unit(), tbl.unit()) {
            (Some(u), Some(t)) => unit_can_reach_bl(c, u, t, distance_bl(u, t) + 1, 0).ok(),
            _ => false,
        };
        st.pushint(ok as i64);
        // (always: see the quirk above)
        set_force_walk(c, bl.id());
        timer::add(c, c.tick() + 50, Func::UnitDelayWalktobl, bl.id(), tbl.id() as i64); // Need timer to avoid mismatches
        off = 4;
    }
    if bl.unit().is_some() && st.hasdata(off) {
        let done_label = getstr(rt, st, off);
        check_event(rt, st, &done_label);
        if let Some(mut u) = unit::get(c, bl.id()) {
            // safestrncpy(ud->walk_done_event, done_label, sizeof(ud->walk_done_event))
            let n = done_label.len().min(crate::script::npc_run::EVENT_NAME_LENGTH - 1);
            u.ud.walk_done_event = lat(&done_label[..n]);
            unit::save(c, &u);
        }
    }
    SCRIPT_CMD_SUCCESS
}

fn set_force_walk(c: &Ctx, id: u32) {
    if let Some(mut u) = unit::get(c, id) {
        u.ud.force_walk = true;
        unit::save(c, &u);
    }
}

// ---------------------------------------------------------------------------------------------- unitwarp

/// unitwarp(<unit id>,"<map name>"|"this",<x>,<y>): unit_warp with CLR_OUTSIGHT. Pushes unit_warp's result, which is
/// 0 on success (rAthena pushes it as it is).
fn unitwarp(rt: &Rt, st: &mut State) -> i32 {
    let mapname = getstr(rt, st, 3);
    let x = getnum(rt, st, 4) as i16;
    let y = getnum(rt, st, 5) as i16;
    let Some(bl) = script_rid2bl(rt, st, 2, "buildin_unitwarp") else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let c = rt.c.unwrap();
    let map_idx: i32 = if mapname == b"this" {
        // bl->m: a homunculus off the map (vaporized, dead) keeps its map, so unit_warp answers 1 (homun agent)
        bl.unit().filter(|u| u.on_map || u.kind == crate::map::BL_HOM).map(|u| u.map as i32).unwrap_or(-1)
    } else {
        crate::data::maps().id_of(&lat(&mapname)).map(|m| m as i32).unwrap_or(-1)
    };
    if map_idx >= 0 {
        st.pushint(unit_warp(c, &bl, map_idx, x, y, unit::CLR_OUTSIGHT) as i64);
    } else {
        st.pushint(0);
    }
    SCRIPT_CMD_SUCCESS
}

/// unit.cpp unit_warp for any unit: players through pc_setpos after the checks, NPCs here (npc_remove_map, then
/// map_addnpc / npc_setcells at the new place), monsters with the mob agent's port. 0 success, 1 refused, 2 no cell,
/// 3 remove failed, 4 add failed (or pc_setpos's e_setpos).
pub fn unit_warp(c: &Ctx, bl: &Bl, m: i32, x: i16, y: i16, clrtype: u8) -> i32 {
    // if(bl->prev == nullptr || !ud) return 1;
    let Some(u) = bl.unit().filter(|u| u.on_map).cloned() else { return 1 };
    if u.kind == BL_MOB {
        return crate::mob::unit::unit_warp(c, u.id, m, x, y, clrtype);
    }
    if u.kind == crate::map::BL_PET {
        // the same unit_warp with the pet flavour of unit_remove_map (pet/ai.rs)
        return crate::pet::ai::unit_warp(c, u.id, m, x, y, clrtype);
    }
    if u.kind == crate::map::BL_HOM {
        // the same unit_warp with the homunculus flavour of unit_remove_map (homunculus/unit.rs)
        return crate::homunculus::unit::unit_warp(c, u.id, m, x, y, clrtype);
    }
    if crate::companion::is_kind(u.kind) {
        // mercenaries and elementals (companion/unit.rs)
        return crate::companion::unit::unit_warp(c, u.id, m, x, y, clrtype);
    }
    if clrtype == unit::CLR_DEAD {
        // Type 1 is invalid, since you shouldn't warp a bl with the "death" animation, it messes up with
        // unit_remove_map! [Skotlex]
        return 1;
    }
    let m = if m < 0 { u.map } else { m as u16 };
    if u.kind == BL_PC && crate::npc::mapflag(c, u.map, crate::npc::MF_NOTELEPORT) {
        return 1;
    }
    let (mut x, mut y) = (x, y);
    if x < 0 || y < 0 {
        // Random map position.
        match crate::mob::spawn::map_search_freecell(c, None, m as i32, x, y, -1, -1, 1, 50) {
            Some((nx, ny)) => {
                x = nx;
                y = ny;
            }
            None => {
                log::warn!("unit_warp failed. Unit Id:{}/Type:{}, target position map {m} at [{x},{y}]", u.id, u.kind);
                return 2;
            }
        }
    } else if u.kind != BL_NPC && crate::map::getcell(c, m, x, y, crate::map::CellChk::ChkNoReach) {
        // Invalid target cell
        log::warn!("unit_warp: Specified non-walkable target cell: {m} at [{x},{y}]");
        match crate::mob::spawn::map_search_freecell(c, None, m as i32, x, y, 4, 4, 1, 50) {
            Some((nx, ny)) => {
                x = nx;
                y = ny;
            }
            None => {
                // Can't find a nearby cell
                log::warn!("unit_warp failed. Unit Id:{}/Type:{}, target position map {m} at [{x},{y}]", u.id, u.kind);
                return 2;
            }
        }
    }
    if u.kind == BL_PC {
        // Use pc_setpos
        return match crate::pc::setpos(c, u.id, m, x, y, clrtype) {
            crate::pc::SetPos::Ok => 0,
            crate::pc::SetPos::MapIndex => 1,
            crate::pc::SetPos::Autotrade => 3, // SETPOS_AUTOTRADE
        };
    }
    if u.kind != BL_NPC {
        // the other kinds have no unit_data (unit_bl2ud answers nullptr: unit.cpp:1433-1436 returns 1)
        return 1;
    }
    // unit_remove_map: stop walking / attacking, the map-change status ends, then (BL_NPC) npc_remove_map
    if !npc_unit_remove_map(c, u.id) {
        return 3;
    }
    if u.map != m && crate::skillunit::conf::clear_unit_onwarp() & BL_NPC != 0 {
        crate::skillunit::skill_clear_unitgroup(c, u.id); // (clear_skills_on_warp 15 leaves NPCs out)
    }
    let Some(mut u) = unit::get(c, u.id) else { return 3 };
    u.x = x;
    u.y = y;
    u.ud.to_x = x;
    u.ud.to_y = y;
    u.map = m;
    unit::save(c, &u);
    if let Some(nd) = c.db().npc().id().find(u.id) {
        c.db().npc().id().update(Npc { map: m as i32, x, y, ..nd });
    }
    // map_addnpc(m, nd); npc_setcells(nd) (the touch area follows the npc row)
    map_addnpc(c, u.id);
    if !unit::addblock(c, &mut u) {
        return 4; // error on adding bl to map
    }
    crate::clif::spawn(c, &u, false);
    crate::skillunit::skill_unit_move_id(c, u.id, c.tick(), 1);
    0
}

/// unit.cpp unit_remove_map_ for an NPC: the generic part (walking, attack, step action, can-act ticks, the statuses
/// that end on a map change), then npc_remove_map. False when it was not on a map.
fn npc_unit_remove_map(c: &Ctx, id: u32) -> bool {
    let Some(mut u) = unit::get(c, id) else { return false };
    if !u.on_map {
        return false; // Already removed?
    }
    if u.ud.walktimer != 0 {
        unit::stop_walking(c, &mut u, unit::USW_RELEASE_TARGET, 0);
    }
    // (unit_skillcastcancel while casting: skills, phase 4)
    if u.ud.target != 0 || u.ud.attacktimer != 0 {
        unit::stop_attack(c, &mut u);
    }
    if u.ud.stepaction || u.ud.steptimer != 0 {
        unit::stop_stepaction(c, &mut u);
    }
    // Do not reset can-act delay. [Skotlex]
    u.ud.attackabletime = c.tick();
    u.ud.canmove_tick = c.tick();
    unit::save(c, &u);
    remove_statuses_on_change_map(c, id);
    // npc_remove_map: CLR_RESPAWN to the area, npc_unsetcells, map_delblock, out of mapdata->npc
    let Some(nd) = c.db().npc().id().find(id) else { return false };
    if nd.map < 0 {
        return false; // Not assigned to a map.
    }
    crate::npc::remove_map(c, &nd);
    true
}

/// unit_remove_map's `status_db.removeByStatusFlag(bl, { SCF_REMOVEONCHANGEMAP })` and the other ends for a non-player
/// unit (Cloaking, Cloaking Exceed, a self Gospel, an infinite Provoke).
fn remove_statuses_on_change_map(c: &Ctx, id: u32) {
    use crate::status::consts::*;
    use crate::status::sc;
    if sc::load(c, id).empty() {
        return;
    }
    for s in crate::db::misc::status::status_db().iter() {
        if sc::has_sce(c, id, s.type_) && s.has_flag(SCF_REMOVEONCHANGEMAP) {
            sc::status_change_end(c, id, s.type_, 0);
        }
    }
    sc::status_change_end(c, id, SC_CLOAKING, 0);
    sc::status_change_end(c, id, SC_CLOAKINGEXCEED, 0);
    if sc::get_sce(c, id, SC_GOSPEL).map(|e| e.val4 == crate::mob::battle_iface::BCT_SELF).unwrap_or(false) {
        sc::status_change_end(c, id, SC_GOSPEL, 0);
    }
    if sc::get_sce(c, id, SC_PROVOKE).map(|e| e.val4 == 1).unwrap_or(false) {
        sc::status_change_end(c, id, SC_PROVOKE, 0); // End infinite provoke to prevent exploit
    }
}

/// map.cpp map_addnpc: NPCs with a trigger area are grouped at the front of mapdata->npc, warps first
/// (0 < npc_num_warp < npc_num_area < npc_num); the others only count. The module keeps the trigger-area section as
/// the npc rows' touch_order (1-based).
fn map_addnpc(c: &Ctx, id: u32) {
    let Some(nd) = c.db().npc().id().find(id) else { return };
    if nd.map < 0 {
        return;
    }
    let (xs, ys) = match nd.subtype {
        crate::npc::NPCTYPE_WARP | crate::npc::NPCTYPE_SCRIPT => (nd.xs, nd.ys),
        _ => (-1, -1),
    };
    if xs < 0 || ys < 0 {
        return; // mapdata->npc[npc_num] = nd: outside the trigger-area section
    }
    let mut area: Vec<Npc> = crate::npc::touch_npcs(c, nd.map as u16).into_iter().filter(|n| n.id != id).collect();
    let num_warp = area.iter().filter(|n| n.subtype == crate::npc::NPCTYPE_WARP).count();
    if nd.subtype == crate::npc::NPCTYPE_WARP {
        // npc[npc_num_area] = npc[npc_num_warp]; npc[npc_num_warp] = nd: the first script moves to the end
        if num_warp < area.len() {
            let first_script = area[num_warp].clone();
            area[num_warp] = nd.clone();
            area.push(first_script);
        } else {
            area.push(nd.clone());
        }
    } else {
        area.push(nd.clone());
    }
    for (k, n) in area.into_iter().enumerate() {
        let order = k as u32 + 1;
        if let Some(cur) = c.db().npc().id().find(n.id) {
            if cur.touch_order != order {
                c.db().npc().id().update(Npc { touch_order: order, ..cur });
            }
        }
    }
}

// ---------------------------------------------------------------------------------------------- unitattack

/// clif.hpp e_damage_type DMG_NORMAL / DMG_REPEAT: the action types unitattack gives clif_parse_ActionRequest_sub.
const DMG_NORMAL: u8 = 0;
const DMG_REPEAT: u8 = 7;

/// unitattack's `actiontype > 0 ? DMG_REPEAT : DMG_NORMAL` for a player (a single attack, or a continuous one).
pub fn unitattack_action(actiontype: i32) -> u8 {
    if actiontype > 0 {
        DMG_REPEAT
    } else {
        DMG_NORMAL
    }
}

/// BUILDIN_FUNC(unitattack) (script.cpp:20113-20161): unitattack(<unit id>,"<target name>"|<target id>{,<action
/// type>}). The target is the player of that name (map_nick2sd, no partial match) or the object of that id
/// (map_id2bl); none: false and SCRIPT_CMD_FAILURE. A player attacks through clif_parse_ActionRequest_sub (continuous
/// for an action type > 0) and the answer is whether it now targets it (ud.target); a monster or a pet gets the
/// target as its target_id and chases it (unit_walktobl, range 65025, flag 2 = attack on arrival: its answer is
/// pushed); another unit type: "Unsupported source unit type", false and SCRIPT_CMD_FAILURE.
fn unitattack(rt: &Rt, st: &mut State) -> i32 {
    let Some(unit_bl) = script_rid2bl(rt, st, 2, "buildin_unitattack") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let c = rt.c.unwrap(); // script_rid2bl found the unit: there is a world
    let target_bl = if isstring(rt, st, 3) {
        let name = lat(&getstr(rt, st, 3));
        crate::pc::nick2sd(c, &name, false).map(Bl::Unit)
    } else {
        let id = getnum(rt, st, 3);
        super::unitctl::map_id2bl(c, id as u32)
    };
    let Some(target_bl) = target_bl else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let actiontype = if st.hasdata(4) { getnum(rt, st, 4) } else { 0 };
    let tid = target_bl.id();
    match unit_bl.kind() {
        BL_PC => {
            let aid = unit_bl.id();
            // clif_parse_ActionRequest_sub(*sd, …, target_bl->id, gettick()): the module's reads gettick itself
            crate::parse::action_request_sub(c, aid, unitattack_action(actiontype), tid as i32);
            let target = unit::get(c, aid).map(|u| u.ud.target).unwrap_or(0);
            st.pushint((target == tid) as i64);
            return SCRIPT_CMD_SUCCESS;
        }
        BL_MOB => {
            if let Some(mut md) = crate::mob::get(c, unit_bl.id()) {
                md.target_id = tid;
                crate::mob::put(c, &md);
            }
        }
        crate::map::BL_PET => {
            if let Some(mut pd) = crate::pet::data::get(c, unit_bl.id()) {
                pd.target_id = tid;
                crate::pet::data::put(c, &pd);
            }
        }
        t => {
            log::error!("buildin_unitattack: Unsupported source unit type {t}.");
            st.pushint(0);
            return SCRIPT_CMD_FAILURE;
        }
    }
    // unit_walktobl(unit_bl, target_bl, 65025, 2) (a target without a unit row — a floating NPC — is on no map:
    // unit_can_reach_bl fails there too)
    st.pushint(unit_walktobl(c, unit_bl.id(), tid, 65025, 2) as i64);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(unitstopattack) (script.cpp:20166-20178): unitstopattack <unit id>; — unit_stop_attack (the target is
/// cleared, the attack timer deleted); a monster also forgets its target_id. A floating NPC has no unit row here: its
/// unit_data never attacks, so there is nothing to stop.
fn unitstopattack(rt: &Rt, st: &mut State) -> i32 {
    if let Some(Bl::Unit(mut u)) = script_rid2bl(rt, st, 2, "buildin_unitstopattack") {
        let c = rt.c.unwrap();
        unit::stop_attack(c, &mut u);
        unit::save(c, &u);
        if u.kind == BL_MOB {
            if let Some(mut md) = crate::mob::get(c, u.id) {
                md.target_id = 0;
                crate::mob::put(c, &md);
            }
        }
    }
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- follow

/// pcfollow <account id>,<target id>: pc_follow.
fn pcfollow(rt: &Rt, st: &mut State) -> i32 {
    if let Some(aid) = script_mapid2sd(rt, st, 2, "buildin_pcfollow") {
        let target = getnum(rt, st, 3);
        pc_follow(rt.c.unwrap(), aid, target);
    }
    SCRIPT_CMD_SUCCESS
}

/// pcstopfollow <account id>: pc_stop_following.
fn pcstopfollow(rt: &Rt, st: &mut State) -> i32 {
    if let Some(aid) = script_mapid2sd(rt, st, 2, "buildin_pcstopfollow") {
        pc_stop_following(rt.c.unwrap(), aid);
    }
    SCRIPT_CMD_SUCCESS
}

/// pc.cpp pc_follow: the player follows `target_id` (any unit) with pc_follow_timer. 1 when there is no such unit.
pub fn pc_follow(c: &Ctx, aid: u32, target_id: i32) -> i32 {
    if super::unitctl::map_id2bl(c, target_id as u32).is_none() {
        return 1;
    }
    if pc_misc::pcm(c, aid).followtimer != 0 {
        pc_stop_following(c, aid);
    }
    let mut m = pc_misc::pcm(c, aid);
    m.followtarget = target_id;
    pc_misc::put_pcm(c, &m);
    follow_step(c, aid, c.tick());
    0
}

/// pc.cpp pc_stop_following.
pub fn pc_stop_following(c: &Ctx, aid: u32) -> i32 {
    let mut m = pc_misc::pcm(c, aid);
    if m.followtimer != 0 {
        timer::delete(c, m.followtimer);
        m.followtimer = 0;
    }
    m.followtarget = -1;
    pc_misc::put_pcm(c, &m);
    if let Some(mut u) = unit::get(c, aid) {
        u.ud.target_to = 0;
        unit::stop_walking(c, &mut u, unit::USW_FIXPOS, 0);
        unit::save(c, &u);
    }
    0
}

/// pc.cpp pc_follow_timer (Func::PcFollow, target = account id).
pub fn pc_follow_timer(c: &Ctx, tid: u64, tick: Tick, aid: u32) {
    if crate::pc::get(c, aid).is_none() {
        return;
    }
    let mut m = pc_misc::pcm(c, aid);
    if m.followtimer != tid {
        log::error!("pc_follow_timer {} != {}", m.followtimer, tid);
        m.followtimer = 0;
        pc_misc::put_pcm(c, &m);
        return;
    }
    m.followtimer = 0;
    pc_misc::put_pcm(c, &m);
    follow_step(c, aid, tick);
}

/// The body of pc_follow_timer (pc_follow calls it directly with INVALID_TIMER).
fn follow_step(c: &Ctx, aid: u32, tick: Tick) {
    let m = pc_misc::pcm(c, aid);
    let tbl = super::unitctl::map_id2bl(c, m.followtarget as u32);
    if tbl.is_none() || crate::pc::isdead(c, aid) {
        pc_stop_following(c, aid);
        return;
    }
    let Some(sd) = unit::get(c, aid) else { return };
    // either player or target is currently detached from map blocks (could be teleporting), but still connected to
    // this map, so we'll just increment the timer and check back later
    if let Some(t) = tbl.as_ref().and_then(|b| b.unit()).filter(|t| t.on_map) {
        if sd.on_map && sd.ud.skilltimer == 0 && sd.ud.attacktimer == 0 && sd.ud.walktimer == 0 {
            if sd.map == t.map && unit_can_reach_bl(c, &sd, t, crate::conf::area_size() as i32, 0).ok() {
                if !check_distance_bl(&sd, t, 5) {
                    unit_walktobl(c, aid, t.id, 5, 0);
                }
            } else {
                crate::pc::setpos(c, aid, t.map, t.x, t.y, unit::CLR_TELEPORT);
            }
        }
    }
    // increase time a bit to loosen up map's load
    let tid = timer::add(c, tick + 1000, Func::PcFollow, aid, 0);
    let mut m = pc_misc::pcm(c, aid);
    m.followtimer = tid;
    pc_misc::put_pcm(c, &m);
}

// ---------------------------------------------------------------------------------------------- pushpc

/// pushpc <direction>,<cells>: the attached player is knocked back (unit_blown, BLOWN_NONE). Negative cells push
/// "backwards": rAthena turns around with direction_opposite, which returns a bool (path.cpp), so the direction
/// becomes 1 (north-west), or 0 (north) when it was south.
fn pushpc(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_pushpc") else { return SCRIPT_CMD_SUCCESS };
    let mut dir = getnum(rt, st, 2) as u8;
    let mut cells = getnum(rt, st, 3);
    if dir >= crate::path::DIR_MAX {
        log::warn!("buildin_pushpc: Invalid direction {dir} specified.");
        reportsrc(rt, st);
        dir %= crate::path::DIR_MAX; // trim spin-over
    }
    if cells == 0 {
        return SCRIPT_CMD_SUCCESS; // zero distance
    } else if cells < 0 {
        // pushing backwards: dir = direction_opposite(dir) (a bool)
        dir = ((dir + crate::path::DIR_MAX / 2) % crate::path::DIR_MAX != 0) as u8;
        cells = -cells;
    }
    let dx = crate::path::DIRX[dir as usize];
    let dy = crate::path::DIRY[dir as usize];
    crate::battle::knockback::unit_blown(rt.c.unwrap(), aid, dx, dy, cells, 0); // BLOWN_NONE
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- checkvending

/// checkvending({"<player name>"}): 1 vending, 4 a buying store, | 2 autotrading (autotrade.rs).
fn checkvending(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = nick2sd(rt, st, 2, "buildin_checkvending") else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let p = crate::pc::get(rt.c.unwrap(), aid);
    let mut ret = 0i64;
    if p.as_ref().map(|p| p.vending).unwrap_or(false) {
        ret = 1;
    } else if p.as_ref().map(|p| p.buyingstore).unwrap_or(false) {
        ret = 4;
    }
    if crate::autotrade::autotrade(rt.c.unwrap(), aid) != 0 {
        ret |= 2;
    }
    st.pushint(ret);
    SCRIPT_CMD_SUCCESS
}
