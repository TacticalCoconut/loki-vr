//! The unit.cpp functions monsters need that the core unit code (`crate::unit`) does not have yet: unit_walktobl
//! (+ unit_walktobl_sub, unit_delay_walktobl_timer), unit_update_chase, unit_can_reach_pos / unit_can_reach_bl,
//! unit_get_walkpath_time, unit_stop_walking_soon with unit_data::update_pos / getpos (exact sub-cell positions),
//! unit_warp, unit_counttargeted, the BL_MOB branches of unit_walktoxy_timer / unit_walktoxy_nextcell /
//! unit_walktoxy_sub (the hooks the core walking code calls), and unit_remove_map / unit_free for BL_MOB; plus
//! npc.cpp npc_touch_areanpc2 (monsters in OnTouchNPC areas / warps). unit_attack and the other combat parts are
//! the battle agent's (`battle_iface`).

use super::battle_iface as battle;
use super::{get, put, MobData, AI_NONE};
use crate::map::{self, CellChk, BL_ITEM, BL_MOB};
use crate::path::{self, DIRX, DIRY, MOVE_COST, MOVE_DIAGONAL_COST};
use crate::timer::{self, Func};
use crate::unit::{self, Unit, CLR_DEAD, USW_FIXPOS, USW_FORCE_STOP, USW_NONE, USW_RELEASE_TARGET};
use crate::world::{Ctx, Tick};

/// unit.hpp MIN_POS_INTERVAL: how long unit_data::update_pos keeps its exact coordinates.
pub const MIN_POS_INTERVAL: Tick = 20;

// ---------------------------------------------------------------------------------------------- reach

/// unit.cpp unit_can_reach_pos: a path exists (CELL_CHKNOREACH) from (sx, sy) to (x, y) on map m.
pub fn unit_can_reach_pos(m: u16, sx: i16, sy: i16, x: i16, y: i16, easy: i32) -> bool {
    if sx == x && sy == y {
        return true; // Same place
    }
    path::path_search(m, sx, sy, x, y, easy, CellChk::ChkNoReach).is_some()
}

/// The answer of unit_can_reach_bl: false, true without touching x / y (same cell), or true with the cell next to
/// the target it found. `Fail(cell)` is a failed path search after x / y were already written.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Reach {
    No,
    Fail(i16, i16),
    Same,
    At(i16, i16),
}

impl Reach {
    pub fn ok(self) -> bool {
        matches!(self, Reach::Same | Reach::At(..))
    }
}

/// unit.cpp unit_can_reach_bl: can `bl` walk next to `tbl` (within `range` cells when > 0)?
pub fn unit_can_reach_bl(c: &Ctx, bl: &Unit, tbl: &Unit, range: i32, easy: i32) -> Reach {
    if bl.map != tbl.map {
        return Reach::No;
    }
    if bl.x == tbl.x && bl.y == tbl.y {
        return Reach::Same;
    }
    if range > 0 && !path::check_distance((bl.x - tbl.x) as i32, (bl.y - tbl.y) as i32, range) {
        return Reach::No;
    }
    // It judges whether it can adjoin or not.
    let mut dx = (tbl.x - bl.x).signum();
    let mut dy = (tbl.y - bl.y).signum();
    if map::getcell(c, tbl.map, tbl.x - dx, tbl.y - dy, CellChk::ChkNoPass) {
        // Look for a suitable cell to place in.
        let mut i = 0usize;
        while i < 8 && map::getcell(c, tbl.map, tbl.x - DIRX[i], tbl.y - DIRY[i], CellChk::ChkNoPass) {
            i += 1;
        }
        if i == 8 {
            return Reach::No; // No valid cells.
        }
        dx = DIRX[i];
        dy = DIRY[i];
    }
    let (x, y) = (tbl.x - dx, tbl.y - dy);
    let Some(wpd) = path::path_search(bl.map, bl.x, bl.y, x, y, easy, CellChk::ChkNoReach) else {
        return Reach::Fail(x, y);
    };
    // OFFICIAL_WALKPATH: more than 14 cells only in a straight, unobstructed line (NPCs disregard it)
    if bl.kind != map::BL_NPC && wpd.path.len() > 14 && !path::path_search_long(bl.map, bl.x, bl.y, x, y, CellChk::ChkNoPass) {
        return Reach::Fail(x, y);
    }
    Reach::At(x, y)
}

/// mob.cpp mob_can_reach.
pub fn mob_can_reach(c: &Ctx, md: &Unit, bl: &Unit, range: i32) -> bool {
    unit_can_reach_bl(c, md, bl, range, 0).ok()
}

/// path.hpp distance_bl.
pub fn distance_bl(a: &Unit, b: &Unit) -> i32 {
    path::distance((a.x - b.x) as i32, (a.y - b.y) as i32)
}

/// path.hpp check_distance_bl.
pub fn check_distance_bl(a: &Unit, b: &Unit, d: i32) -> bool {
    path::check_distance((a.x - b.x) as i32, (a.y - b.y) as i32, d)
}

/// path.hpp check_distance_blxy.
pub fn check_distance_blxy(a: &Unit, x: i16, y: i16, d: i32) -> bool {
    path::check_distance((a.x - x) as i32, (a.y - y) as i32, d)
}

// ---------------------------------------------------------------------------------------------- walking to a unit

/// unit.cpp unit_walktobl: chase `tbl` (flag &1 easy path, &2 attack on arrival within `range`). The unit is
/// saved; returns whether it started walking (or will once its walk delay is over).
pub fn unit_walktobl(c: &Ctx, id: u32, tbl_id: u32, range: i32, flag: i32) -> bool {
    let Some(mut u) = unit::get(c, id) else { return false };
    let Some(tbl) = unit::get(c, tbl_id) else { return false };
    if u.mode as i32 & crate::db::mobs::MD_CANMOVE != crate::db::mobs::MD_CANMOVE {
        return false;
    }
    let mut flag = flag;
    match unit_can_reach_bl(c, &u, &tbl, distance_bl(&u, &tbl) + 1, flag & 1) {
        Reach::No | Reach::Fail(..) => {
            u.ud.to_x = u.x;
            u.ud.to_y = u.y;
            u.ud.target_to = 0;
            unit::save(c, &u);
            return false;
        }
        Reach::At(x, y) => {
            u.ud.to_x = x;
            u.ud.to_y = y;
        }
        Reach::Same => {}
    }
    if range == 0 {
        // Should walk on the same cell as target (for looters)
        u.ud.to_x = tbl.x;
        u.ud.to_y = tbl.y;
        // Because of the change of target position the easy walkpath could fail
        flag &= !1;
    }
    u.ud.walk_easy = flag & 1 != 0;
    u.ud.target_to = tbl.id;
    u.ud.chaserange = range; // Note that if flag&2, this SHOULD be attack-range
    u.ud.attack_continue = flag & 2 != 0; // Chase to attack.
    unit::stop_attack(c, &mut u); // Sets target to 0
    // SC_CONFUSION randomises the target position (map_random_dir: status changes of phase 4)
    if u.ud.walktimer != 0 {
        u.ud.change_walk_target = true;
        unit::save(c, &u);
        // New target, make sure a monster is still in chase state
        if u.kind == BL_MOB && u.ud.attack_continue {
            if let Some(mut md) = get(c, id) {
                super::mob_setstate(&mut md, super::MSS_RUSH);
                put(c, &md);
            }
        }
        return true;
    }
    if u.ud.canmove_tick - c.tick() > 0 {
        // Can't move, wait a bit before invoking the movement.
        timer::add(c, u.ud.canmove_tick + 1, Func::UnitWalktoblSub, id, u.ud.target_to as i64);
        unit::save(c, &u);
        return true;
    }
    if !unit_can_move(c, &u) {
        unit::save(c, &u);
        return false;
    }
    let ok = walktoxy_sub(c, &mut u);
    unit::save(c, &u);
    ok
}

/// unit.cpp unit_walktobl_sub (timer): starts the chase once the walk delay is over.
pub fn unit_walktobl_sub(c: &Ctx, tick: Tick, id: u32, data: i64) {
    let Some(mut u) = unit::get(c, id) else { return };
    if u.ud.walktimer == 0 && u.ud.target_to != 0 && u.ud.target_to as i64 == data {
        if u.ud.canmove_tick - tick > 0 {
            // Keep waiting?
            timer::add(c, u.ud.canmove_tick + 1, Func::UnitWalktoblSub, id, data);
        } else if unit_can_move(c, &u) {
            walktoxy_sub(c, &mut u);
            unit::save(c, &u);
        }
    }
}

/// unit.cpp unit_delay_walktobl_timer.
pub fn unit_delay_walktobl_timer(c: &Ctx, id: u32, data: i64) {
    let Some(u) = unit::get(c, id) else { return };
    if !u.on_map || unit::get(c, data as u32).is_none() {
        return;
    }
    unit_walktobl(c, id, data as u32, 0, 0);
    if let Some(mut u) = unit::get(c, id) {
        u.ud.target_to = 0;
        unit::save(c, &u);
    }
}

/// unit.cpp unit_walktoxy_sub through the core walking code, with its BL_MOB part (see [`walktoxy_sub_mob`]).
fn walktoxy_sub(c: &Ctx, u: &mut Unit) -> bool {
    unit::walktoxy_sub(c, u)
}

/// unit.cpp unit_can_move (unit::can_move, with the monster's icewall rule).
pub fn unit_can_move(c: &Ctx, u: &Unit) -> bool {
    unit::can_move(c, u)
}

/// unit.cpp unit_get_walkpath_time: how long the current walk path takes.
pub fn unit_get_walkpath_time(u: &Unit) -> Tick {
    let speed = unit::speed(u);
    let mut time = 0;
    for i in 0..u.ud.path_len as usize {
        let Some(&d) = u.ud.path.get(i) else { break };
        if path::direction_diagonal(d) {
            time += speed * MOVE_DIAGONAL_COST as i64 / MOVE_COST as i64;
        } else {
            time += speed;
        }
    }
    time
}

// ---------------------------------------------------------------------------------------------- exact position

/// unit.cpp unit_data::update_pos: the exact coordinates of a walking unit (kept in the monster row for
/// MIN_POS_INTERVAL ms, as rAthena caches them in unit_data::pos).
pub fn update_pos(c: &Ctx, md: &mut MobData, u: &Unit, tick: Tick) {
    // Check if coordinates are still up-to-date
    if tick - md.pos_tick < MIN_POS_INTERVAL {
        return;
    }
    md.pos_x = u.x;
    md.pos_y = u.y;
    md.pos_sx = 8;
    md.pos_sy = 8;
    md.pos_tick = tick;
    if u.ud.path_pos >= u.ud.path_len {
        return;
    }
    if u.ud.walktimer == 0 || u.ud.walktimer == unit::CLIF_WALK_TIMER {
        return;
    }
    let Some((due, data)) = timer::get(c, u.ud.walktimer) else { return };
    let Some(&dir) = u.ud.path.get(u.ud.path_pos as usize) else { return };
    // Get how much percent we traversed on the timer
    let cell_percent = 1.0 - ((due - tick) as f64 / data as f64);
    if cell_percent > 0.0 && cell_percent < 1.0 {
        // Set subcell coordinates according to timer: a value between 8 and 39
        md.pos_sx = (24.0 + DIRX[dir as usize] as f64 * 16.0 * cell_percent) as u8;
        md.pos_sy = (24.0 + DIRY[dir as usize] as f64 * 16.0 * cell_percent) as u8;
        // 16-31 reflect sub position 0-15 on the current cell, 8-15 sub position 8-15 at -1 main coordinate,
        // 32-39 sub position 0-7 at +1 main coordinate
        if md.pos_sx < 16 || md.pos_sy < 16 || md.pos_sx > 31 || md.pos_sy > 31 {
            if md.pos_sx < 16 {
                md.pos_x -= 1;
            }
            if md.pos_sy < 16 {
                md.pos_y -= 1;
            }
            if md.pos_sx > 31 {
                md.pos_x += 1;
            }
            if md.pos_sy > 31 {
                md.pos_y += 1;
            }
        }
        md.pos_sx %= 16;
        md.pos_sy %= 16;
    } else if cell_percent >= 1.0 {
        // Assume exactly one cell moved
        md.pos_x += DIRX[dir as usize];
        md.pos_y += DIRY[dir as usize];
    }
}

/// unit.cpp unit_data::getpos: exact x, y, sx, sy.
pub fn getpos(c: &Ctx, md: &mut MobData, u: &Unit, tick: Tick) -> (i16, i16, u8, u8) {
    update_pos(c, md, u, tick);
    (md.pos_x, md.pos_y, md.pos_sx, md.pos_sy)
}

/// unit.cpp unit_stop_walking_soon: shortens the walk path to end after 0.5-1.5 cells and sends the move with
/// the exact sub-cell position. The unit is saved.
pub fn unit_stop_walking_soon(c: &Ctx, id: u32, tick: Tick) {
    let Some(mut u) = unit::get(c, id) else { return };
    let (ox, oy) = (u.x, u.y); // Remember original x and y coordinates
    let mut path_remain: i32 = 1; // Remaining path to walk
    let mut shortened = false;
    if u.ud.path_pos as i32 + 1 >= u.ud.path_len as i32 {
        // Less than 1 cell left to walk so no need to shorten the path
        path_remain = u.ud.path_len as i32 - u.ud.path_pos as i32;
    } else {
        // Set coordinates to exact coordinates
        if let Some(mut md) = get(c, id) {
            let (x, y, sx, sy) = getpos(c, &mut md, &u, tick);
            put(c, &md);
            u.x = x;
            u.y = y;
            u.ud.sx = sx;
            u.ud.sy = sy;
        }
        // If x or y already changed, we need to move one more cell
        if ox != u.x || oy != u.y {
            path_remain = 2;
        }
        // Shorten walkpath
        if (u.ud.path_pos as i32 + path_remain) < u.ud.path_len as i32 {
            u.ud.path_len = (u.ud.path_pos as i32 + path_remain) as u8;
            shortened = true;
        }
    }
    // Make sure to_x and to_y match the walk path even if not shortened in case they were modified
    u.ud.to_x = ox;
    u.ud.to_y = oy;
    for i in 0..path_remain.max(0) {
        if let Some(&d) = u.ud.path.get(u.ud.path_pos as usize + i as usize) {
            u.ud.to_x += DIRX[d as usize];
            u.ud.to_y += DIRY[d as usize];
        }
    }
    // To prevent sending a pointless walk command
    u.ud.change_walk_target = false;
    // Send movement packet with calculated coordinates and subcoordinates; only need to send if shortened
    if shortened {
        crate::clif::r#move(c, &u);
    }
    // Reset coordinates
    u.x = ox;
    u.y = oy;
    u.ud.sx = 8;
    u.ud.sy = 8;
    unit::save(c, &u);
}

/// unit.cpp unit_counttargeted.
pub fn unit_counttargeted(u: &Unit) -> i32 {
    u.ud.target_count as i32
}

// ---------------------------------------------------------------------------------------------- chase

/// unit.cpp unit_update_chase: a chasing unit (target_to) attacks when the target is in range, drops the chase when
/// the target is gone or out of sight (monsters unlock their target, or warp-chase), or refreshes the chase path.
/// `fullcheck` false only checks for the attack. Returns whether the chase path was updated / the walk stopped
/// (true) or the current movement can continue (false). Works on the stored rows.
pub fn unit_update_chase(c: &Ctx, id: u32, tick: Tick, fullcheck: bool) -> bool {
    let Some(u) = unit::get(c, id) else { return true };
    let tbl = if u.ud.target_to != 0 { unit::get(c, u.ud.target_to) } else { None };
    // Reached destination, start attacking
    if let Some(t) = tbl.as_ref().filter(|t| t.kind != BL_ITEM && t.map == u.map && u.ud.path_pos > 0 && check_distance_bl(&u, t, u.ud.chaserange)) {
        let mut stop_flag = USW_FIXPOS | USW_RELEASE_TARGET;
        if u.ud.attack_continue {
            stop_flag = battle::unit_attack(c, id, t.id, u.ud.attack_continue as i32);
        }
        // Stop, unless the attack was skipped
        if stop_flag != USW_NONE {
            // We need to make sure the walkpath is cleared here so a monster doesn't continue walking in case it
            // unlocked its target
            if let Some(mut u) = unit::get(c, id) {
                unit::stop_walking(c, &mut u, stop_flag | USW_FORCE_STOP, 0);
                unit::save(c, &u);
            }
            return true;
        }
    } else if tbl.is_none() || (fullcheck && !status_check_visibility(c, &u, tbl.as_ref().unwrap(), u.kind == BL_MOB)) {
        // Cancel chase. Looted items will have no tbl but target ID is still set, that's why we need to check for
        // the ID here
        if u.ud.target_to != 0 && u.kind == BL_MOB {
            if let Some(t) = &tbl {
                match super::ai::mob_warpchase(c, id, Some(t)) {
                    1 => return true,  // Do warp chase
                    2 => return false, // Continue moving to warp
                    _ => {}
                }
            }
            // Make sure monsters properly unlock their target, but still continue movement
            super::ai::mob_unlocktarget(c, id, tick);
            return false;
        }
        if let Some(mut u) = unit::get(c, id) {
            unit::stop_walking(c, &mut u, USW_FIXPOS | USW_FORCE_STOP | USW_RELEASE_TARGET, 0);
            unit::save(c, &u);
        }
        return true;
    } else if fullcheck && u.ud.path_pos > 0 && u.ud.canmove_tick - tick <= 0 {
        // Update chase path. We call this only when we know there is no walk delay to prevent it pre-planning a
        // chase. If the call here fails, the unit should continue its current path
        let t = tbl.unwrap();
        if unit_walktobl(c, id, t.id, u.ud.chaserange, u.ud.walk_easy as i32 | if u.ud.attack_continue { 2 } else { 0 }) {
            return true;
        }
    }
    false
}

/// unit.cpp unit_walktoxy_nextcell, the BL_MOB part before the step is armed: monsters check for a walk / chase
/// skill (mob/skill.rs mobskill_use) and, when chasing, update the chase one cell before the end of the path. Called
/// by the core `unit::walktoxy_nextcell` with the unit saved; Some(result) ends the core function with that result.
pub fn nextcell_mob(c: &Ctx, id: u32, send_move: &mut bool, tick: Tick) -> Option<bool> {
    let u = unit::get(c, id)?;
    let md = get(c, id)?;
    // Walk skills are triggered regardless of target due to the idle-walk mob state. But avoid triggering when
    // already reached the end of the walkpath. Monsters use walk/chase skills every second, but we only get here
    // every "speed" ms. To make sure we check one skill per second on average, we substract half the speed as ms
    if !u.ud.force_walk
        && tick - md.last_skillcheck > super::MOB_SKILL_INTERVAL - md.status.speed as Tick / 2
        && super::skill::mobskill_use(c, id, tick, -1, 0)
    {
        use crate::db::skills::{NPC_EMOTION, NPC_EMOTION_ON, NPC_SPEEDUP};
        // (a monster the skill removed from the world walks no further)
        let (Some(u), Some(md)) = (unit::get(c, id), get(c, id)) else { return Some(true) };
        if (u.ud.skill_id != NPC_SPEEDUP || md.trickcasting == 0) // Stop only when trickcasting expired
            && u.ud.skill_id != NPC_EMOTION && u.ud.skill_id != NPC_EMOTION_ON // NPC_EMOTION doesn't make the monster stop
            && md.skillstate != super::MSS_WALK // Walk skills are supposed to be used while walking
        {
            // Skill used, abort walking. Fix position as walk has been cancelled.
            crate::clif::fixpos(c, &u);
            // Movement was initialized, so we need to return true even though it was stopped. Monsters only start
            // moving or drop target when they stop using chase skills that stop them
            return Some(true);
        }
        // Resend walk packet for proper Self Destruction display
        *send_move = true;
    }
    let u = unit::get(c, id)?;
    if u.ud.target_to != 0 {
        // Monsters update their chase path one cell before reaching their final destination
        let fullcheck = u.ud.path_pos as i32 == u.ud.path_len as i32 - 1;
        if unit_update_chase(c, id, tick, fullcheck) {
            return Some(true);
        }
    }
    None
}

/// unit.cpp unit_walktoxy_sub, BL_MOB part: "Set mobstate here already as chase skills can be used on the first
/// frame of movement" — a monster chasing to attack is in rush state.
pub fn walktoxy_sub_mob(c: &Ctx, u: &Unit) {
    if u.ud.attack_continue {
        if let Some(mut md) = get(c, u.id) {
            super::mob_setstate(&mut md, super::MSS_RUSH);
            put(c, &md);
        }
    }
}

/// unit.cpp unit_walktoxy_timer, BL_MOB parts after a successful step: the escape state ends at the destination,
/// walktoxy_fail_count resets, OnTouchNPC / warps under the new cell (npc_touch_areanpc2). Returns true when the
/// monster was warped or killed there (the step handling stops).
pub fn after_step_mob(c: &Ctx, id: u32, at_destination: bool) -> bool {
    let Some(mut md) = get(c, id) else { return true };
    let Some(u) = unit::get(c, id) else { return true };
    if at_destination {
        // Remove any possible escape states present for mobs once they stopped moving.
        md.can_escape = false;
    }
    // Movement was successful, reset walktoxy_fail_count
    md.walktoxy_fail_count = 0;
    if map::getcell(c, u.map, u.x, u.y, CellChk::ChkNpc) {
        put(c, &md);
        if npc_touch_areanpc2(c, id) {
            return true; // Warped
        }
    } else {
        md.areanpc_id = 0;
        put(c, &md);
    }
    false
}

/// unit.cpp unit_walktoxy_timer: `if (!ud->stepaction && ud->target_to > 0) { if (unit_update_chase(*bl, tick,
/// true)) return 0; }` once the path is done. Returns true when the chase took over.
pub fn chase_after_path(c: &Ctx, id: u32, tick: Tick) -> bool {
    let Some(u) = unit::get(c, id) else { return true };
    if !u.ud.stepaction && u.ud.target_to > 0 {
        // Update target trajectory.
        return unit_update_chase(c, id, tick, true);
    }
    false
}

/// npc.cpp npc_touch_areanpc2: a monster on an NPC touch cell uses the warp (mob_warp&1) or runs the NPC's
/// OnTouchNPC event once. Returns true when it was warped or killed.
pub fn npc_touch_areanpc2(c: &Ctx, id: u32) -> bool {
    let Some(u) = unit::get(c, id) else { return true };
    let Some(md) = get(c, id) else { return true };
    let (x, y) = (u.x, u.y);
    for nd in crate::npc::touch_npcs(c, u.map) {
        let Some(nu) = unit::get(c, nd.id) else { continue };
        if nd.is_invisible || nu.sc.option & unit::OPTION_CLOAK != 0 {
            continue;
        }
        if nd.owner_char != 0 {
            continue;
        }
        let (xs, ys) = match nd.subtype {
            crate::npc::NPCTYPE_WARP => {
                if crate::conf::mob_warp() & 1 == 0 {
                    continue;
                }
                (nd.xs, nd.ys)
            }
            crate::npc::NPCTYPE_SCRIPT => (nd.xs, nd.ys),
            _ => continue, // Keep Searching
        };
        if xs < 0 || ys < 0 {
            continue;
        }
        if x >= nd.x - xs && x <= nd.x + xs && y >= nd.y - ys && y <= nd.y + ys {
            // In the npc touch area
            match nd.subtype {
                crate::npc::NPCTYPE_WARP => {
                    if nd.warp_map < 0 {
                        // Cannot Warp between map servers
                    } else if unit_warp(c, id, nd.warp_map, nd.warp_x, nd.warp_y, unit::CLR_OUTSIGHT) == 0 {
                        return true; // Warped
                    }
                }
                _ => {
                    if nd.id == md.areanpc_id {
                        // Already touch this NPC
                    } else {
                        let name = format!("{}::{}", nd.exname, crate::script::npc_run::ONTOUCHNPC_EVENT_NAME);
                        let ev = c.db().npc_event().name().find(&name);
                        if let Some(ev) = ev.filter(|e| c.db().npc().id().find(e.npc_id).is_some()) {
                            let mut md = md.clone();
                            md.areanpc_id = nd.id;
                            put(c, &md);
                            crate::script::npc_run::run_npc(c, ev.npc_id, ev.pos, id);
                            if get(c, id).is_none() {
                                return true; // Not Warped, but killed
                            }
                        }
                    }
                }
            }
            return false;
        }
    }
    false
}

use crate::script::npc_run::npc_event as _;

// ---------------------------------------------------------------------------------------------- warp

/// unit.cpp unit_warp for a non-player unit: moves it to (x, y) on map m (m < 0: its own map; x or y < 0: a random
/// cell). Returns 0 success, 1 refused, 2 no cell, 3 remove failed, 4 add failed.
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
    if u.kind == BL_MOB {
        let master = get(c, id).map(|md| md.master_id).unwrap_or(0);
        if crate::npc::mapflag(c, u.map, super::MF_MONSTER_NOTELEPORT) && master == 0 {
            return 1;
        }
        if m != u.map && crate::npc::mapflag(c, m, super::MF_NOBRANCH) && crate::conf::mob_warp() & 4 != 0 && master == 0 {
            return 1;
        }
    } else if u.kind == map::BL_PC {
        // players go through pc_setpos
        log::error!("mob::unit::unit_warp called for a player ({id})");
        return 1;
    }
    let (mut x, mut y) = (x, y);
    if x < 0 || y < 0 {
        // Random map position.
        match super::spawn::map_search_freecell(c, None, m as i32, x, y, -1, -1, 1, 50) {
            Some((nx, ny)) => {
                x = nx;
                y = ny;
            }
            None => {
                log::warn!("unit_warp failed. Unit Id:{id}/Type:{}, target position map {m} at [{x},{y}]", u.kind);
                return 2;
            }
        }
    } else if u.kind != map::BL_NPC && map::getcell(c, m, x, y, CellChk::ChkNoReach) {
        // Invalid target cell
        log::warn!("unit_warp: Specified non-walkable target cell: {m} at [{x},{y}]");
        match super::spawn::map_search_freecell(c, None, m as i32, x, y, 4, 4, 1, 50) {
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
    if !unit_remove_map(c, id, clrtype) {
        return 3;
    }
    // clear_skills_on_warp (battle_config.clear_unit_onwarp & bl->type on a map change): skill_clear_unitgroup
    if unit::get(c, id).map(|u| u.map != m && crate::skillunit::conf::clear_unit_onwarp() & u.kind != 0).unwrap_or(false) {
        crate::skillunit::skill_clear_unitgroup(c, id);
    }
    let Some(mut u) = unit::get(c, id) else { return 3 };
    u.x = x;
    u.y = y;
    u.ud.to_x = x;
    u.ud.to_y = y;
    u.map = m;
    if u.kind == BL_MOB && crate::mob::conf::slave_stick_with_master() {
        // If slaves are set to stick with their master they should drop target if recalled at range
        if let Some(mut md) = get(c, id) {
            if md.target_id != 0 {
                let far = unit::get(c, md.target_id).map(|t| !check_distance_bl(&u, &t, crate::conf::area_size() as i32)).unwrap_or(true);
                if far {
                    md.target_id = 0;
                    put(c, &md);
                }
            }
        }
    }
    if !unit::addblock(c, &mut u) {
        return 4; // error on adding bl to map
    }
    crate::clif::spawn(c, &u, false);
    crate::skillunit::skill_unit_move_id(c, id, c.tick(), 1);
    let Some(u) = unit::get(c, id) else { return 0 };
    if !crate::mob::conf::slave_stick_with_master() && u.kind == BL_MOB && super::spawn::mob_countslave(c, &u) > 0 {
        super::spawn::mob_warpslave(c, &u, super::MOB_SLAVEDISTANCE);
    }
    0
}

// ---------------------------------------------------------------------------------------------- remove / free

/// unit.cpp unit_remove_map for a monster: stops it, ends the statuses that end on a map change, forgets its
/// attacker / spotted log, and takes it out of the block list (a dead monster's vanish packet comes from mob_dead
/// through clif_clearunit_delayed, so it gets none here). Returns false when it was not on a map.
pub fn unit_remove_map(c: &Ctx, id: u32, clrtype: u8) -> bool {
    let Some(mut u) = unit::get(c, id) else { return false };
    if !u.on_map {
        return false; // Already removed?
    }
    if u.ud.walktimer != 0 {
        unit::stop_walking(c, &mut u, USW_RELEASE_TARGET, 0);
    }
    if clrtype == CLR_DEAD {
        u.ud.blockedmove = true;
    }
    if u.ud.skilltimer != 0 {
        unit::save(c, &u);
        crate::skill::unit::unit_skillcastcancel(c, id, 0);
        u = unit::get(c, id).unwrap_or(u);
    }
    // Clear target even if there is no timer
    if u.ud.target != 0 || u.ud.attacktimer != 0 {
        unit::stop_attack(c, &mut u);
    }
    // Clear stepaction even if there is no timer
    if u.ud.stepaction || u.ud.steptimer != 0 {
        unit::stop_stepaction(c, &mut u);
    }
    // Do not reset can-act delay. [Skotlex]
    u.ud.attackabletime = c.tick();
    u.ud.canmove_tick = c.tick();
    unit::save(c, &u);
    // map-change/warp dispells: StatusDatabase::removeByStatusFlag(bl, { SCF_REMOVEONCHANGEMAP }), then for
    // non-players Cloaking / Cloaking Exceed, a self Gospel, an infinite Provoke
    remove_statuses_on_change_map(c, id);
    // BL_MOB
    if let Some(mut md) = get(c, id) {
        // Drop previous target mob_slave_keep_target: no.
        if !crate::mob::conf::mob_slave_keep_target() {
            md.target_id = 0;
        }
        // When a monster is removed from map, its spotted log is cleared
        md.spotted_log = vec![0; super::DAMAGELOG_SIZE];
        md.attacked_id = 0;
        super::mob_setstate(&mut md, super::MSS_IDLE);
        put(c, &md);
    }
    // skill_unit_move(bl, tick, 4) (skillunit); skill_cleartimerskill (skill/timerskill.rs)
    crate::skillunit::skill_unit_move_id(c, id, c.tick(), 4);
    if let Some(u) = unit::get(c, id) {
        crate::skill::timerskill::skill_cleartimerskill(c, &u);
    }
    let Some(mut u) = unit::get(c, id) else { return false };
    // BL_MOB is handled by mob_dead unless the monster is not dead.
    let dead = get(c, id).map(|md| md.status.hp == 0).unwrap_or(false);
    if !dead {
        crate::clif::clearunit_area(c, &u, clrtype);
    }
    u.on_map = false; // map_delblock
    unit::save(c, &u);
    true
}

/// StatusDatabase::removeByStatusFlag(bl, {SCF_REMOVEONCHANGEMAP}) and the other status ends of unit_remove_map for
/// a non-player unit.
fn remove_statuses_on_change_map(c: &Ctx, id: u32) {
    use crate::status::consts::*;
    use crate::status::sc;
    let scl = sc::load(c, id);
    if scl.empty() {
        return;
    }
    for s in crate::db::misc::status::status_db().iter() {
        if sc::has_sce(c, id, s.type_) && s.has_flag(SCF_REMOVEONCHANGEMAP) {
            sc::status_change_end(c, id, s.type_, 0);
        }
    }
    // Ensure the bl is a PC; if so, we'll handle the removal of cloaking and cloaking exceed later
    sc::status_change_end(c, id, SC_CLOAKING, 0);
    sc::status_change_end(c, id, SC_CLOAKINGEXCEED, 0);
    if let Some(e) = sc::get_sce(c, id, SC_GOSPEL) {
        if e.val4 == crate::mob::battle_iface::BCT_SELF {
            sc::status_change_end(c, id, SC_GOSPEL, 0);
        }
    }
    if let Some(e) = sc::get_sce(c, id, SC_PROVOKE) {
        if e.val4 == 1 {
            sc::status_change_end(c, id, SC_PROVOKE, 0); // End infinite provoke to prevent exploit
        }
    }
}

/// unit.cpp unit_free for a monster: off the map (if on it), its timers and spawn bookkeeping, status changes, clone
/// data and MVP tomb, then the object itself (map_deliddb / map_freeblock). Returns 0.
pub fn unit_free(c: &Ctx, id: u32, clrtype: u8) -> i32 {
    if unit::get(c, id).map(|u| u.on_map).unwrap_or(false) {
        // Players are supposed to logout with a "warp" effect.
        unit_remove_map(c, id, clrtype);
    }
    let Some(mut md) = get(c, id) else {
        crate::unit::delete_mob(c, id);
        return 0;
    };
    // mob_free_dynamic_viewdata
    md.vd_changed = false;
    if md.spawn_timer != 0 {
        timer::delete(c, md.spawn_timer);
        md.spawn_timer = 0;
    }
    if md.deletetimer != 0 {
        timer::delete(c, md.deletetimer);
        md.deletetimer = 0;
    }
    md.lootitems.clear();
    // md->guardian_data: the guardian leaves its castle slot (woe/guardian.rs)
    crate::woe::guardian::unit_free(c, id);
    md.guardian = false;
    if md.spawn != 0 {
        if let Some(mut sp) = c.db().spawn_data().id().find(md.spawn) {
            sp.active = sp.active.saturating_sub(1);
            if !sp.dynamic {
                // Permanently remove the mob
                sp.num = sp.num.saturating_sub(1);
                if sp.num == 0 {
                    // Last freed mob is responsible for deallocating the group's spawn data.
                    c.db().spawn_data().id().delete(sp.id);
                } else {
                    c.db().spawn_data().id().update(sp);
                }
            } else {
                c.db().spawn_data().id().update(sp);
            }
        }
    }
    md.base_status = None;
    put(c, &md);
    crate::skillunit::skill_clear_unitgroup(c, id);
    let Some(mut md) = get(c, id) else { return 0 };
    crate::status::sc::status_change_clear(c, id, 1);
    if super::rtdb::mob_is_clone(c, md.mob_id) != 0 {
        super::spawn::mob_clone_delete(c, &mut md);
    }
    if md.tomb_nid != 0 {
        super::dead::mvptomb_destroy(c, &mut md);
    }
    // map_deliddb / map_freeblock
    crate::status::sc::free_unit(c, id);
    crate::skillunit::data::on_unit_freed(c, id); // ud->skillunittick goes with the unit_data
    // the unit_data skill rows (timerskills, shadow scars; their timers die with the unit)
    crate::skill::timerskill::free_unit(c, id);
    crate::skill::unit::free_unit(c, id);
    // map_freeblock under map_freeblock_lock: rAthena keeps the object until the lock ends, so a caller holding its
    // pointer (skill_attack after the killing blow) still reads it; crate::unit::get_or_freed / crate::mob::get_or_freed
    // give that last state for the rest of this call
    if let Some(u) = unit::get(c, id) {
        crate::unit::freed_keep(u);
    }
    crate::mob::freed_keep(md.clone());
    crate::mob::delete_row(c, id);
    crate::unit::delete_mob(c, id);
    0
}

use crate::npc::spawn_data as _;
use crate::npc::npc as _;

// ---------------------------------------------------------------------------------------------- status checks

/// status.cpp status_check_skilluse: can `src` use `skill_id` (0: a normal attack) on `target`, given both units'
/// statuses and options? flag 0: trying to use; 1: cast done; 2: skill already pulled off. `src` None checks only
/// the target (ground skills).
pub fn status_check_skilluse(c: &Ctx, src: Option<&Unit>, target: Option<&Unit>, skill_id: u16, flag: i32) -> bool {
    use crate::db::skills::{self as sk, ids::*, CastType, INF2_ALLOWWHENHIDDEN, INF2_TARGETHIDDEN, INF2_TARGETMANHOLE, INF_SUPPORT_SKILL};
    use crate::status::consts::*;
    use crate::status::sc;
    let status = match src {
        Some(s) => {
            if s.kind != map::BL_PC && crate::status::sc::status_isdead(c, s) {
                return false;
            }
            crate::status::status_get_status_data(c, s)
        }
        None => crate::status::dummy_status(),
    };
    let has_mode = |m: i32| status.mode & m == m;
    let scl = src.map(|s| sc::load(c, s.id)).unwrap_or_default();
    let tsc = target.map(|t| sc::load(c, t.id)).unwrap_or_default();
    let src_cant = |scs: u32| src.map(|s| sc::cant(c, s.id, scs)).unwrap_or(false);
    let src_option = src.map(|s| s.sc.option).unwrap_or(0);
    let src_opt1 = src.map(|s| s.sc.opt1).unwrap_or(0);
    if skill_id == 0 {
        // Normal attack checks.
        if src.is_some() && src_cant(SCS_NOATTACK) {
            return false;
        }
        // This mode is only needed for melee attacking.
        if !has_mode(crate::db::mobs::MD_CANATTACK) {
            return false;
        }
        // Dead state is not checked for skills as some skills can be used on dead characters, said checks are
        // left to skill.cpp [Skotlex]
        if let Some(t) = target {
            if sc::status_isdead(c, t) {
                return false;
            }
        }
    }
    match skill_id {
        GN_WALLOFTHORN => {
            if let Some(t) = target {
                if sc::status_isdead(c, t) {
                    return false;
                }
            }
        }
        AL_TELEPORT | ALL_ODINS_POWER => {
            // Should fail when used on top of Land Protector [Skotlex] (status.cpp:2078-2085; the cell flag skillunit sets)
            if let Some(s) = src {
                if crate::map_cell::getcell(c, Some(s.map), s.x, s.y, crate::map_cell::CELL_CHKLANDPROTECTOR) != 0
                    && !has_mode(crate::db::mobs::MD_STATUSIMMUNE)
                    && (s.kind != map::BL_PC || crate::skill::state::get(c, s.id).skillitem as i32 != skill_id as i32)
                {
                    return false;
                }
            }
        }
        SC_MANHOLE => {
            // Skill is disabled against special racial grouped monsters (GvG and Battleground)
            if let Some(t) = target {
                if t.kind == BL_MOB {
                    if let Some(tmd) = get(c, t.id) {
                        let r2 = super::status::status_get_race2(c, &tmd);
                        if r2.contains(&super::RC2_GVG) || r2.contains(&super::RC2_BATTLEFIELD) {
                            return false;
                        }
                    }
                }
            }
        }
        _ => {}
    }
    if src.is_some() && !scl.empty() {
        if scl.has(SC_ALL_RIDING) {
            return false; // You can't use skills while in the new mounts
        }
        if flag == 1 && !has_mode(crate::db::mobs::MD_STATUSIMMUNE) && ((scl.has(SC_ASH) && c.rnd() % 2 != 0) || (scl.has(SC_KYOMU) && c.rnd() % 100 < 25)) {
            // Volcanic Ash has a 50% chance of causing skills to fail; Kyomu has a 25% chance of causing skills fail.
            if let Some(s) = src.filter(|s| s.kind == map::BL_PC) {
                crate::skill::clif::skill_fail(c, s, skill_id);
            }
            return false;
        }
        if skill_id > 0 && src_opt1 != 0 && src_opt1 != crate::status::defs::OPT1_STONEWAIT && src_opt1 != crate::status::defs::OPT1_BURNING && skill_id != RK_REFRESH && skill_id != SU_GROOMING && skill_id != SR_GENTLETOUCH_CURE {
            // Stuned/Frozen/etc
            if flag != 1 {
                return false; // Can't cast, casted stuff can't damage.
            }
            if sk::skill_get_casttype(skill_id) == CastType::Damage {
                return false; // Damage spells stop casting.
            }
        }
        if (scl.has(SC_TRICKDEAD) && skill_id != NV_TRICKDEAD)
            || (scl.has(SC_AUTOCOUNTER) && flag == 0 && skill_id != 0)
            || (scl.has(SC_GOSPEL) && scl.v4(SC_GOSPEL) == battle::BCT_SELF && skill_id != PA_GOSPEL)
            || (scl.has(SC_SUHIDE) && skill_id != SU_HIDE)
        {
            return false;
        }
        if scl.has(SC_BLADESTOP) {
            // switch (val1) with fallthrough from case 5 down to case 2; any other val1 (above 5 too) is `default`
            let v = scl.v1(SC_BLADESTOP);
            let ok = (2..=5).contains(&v) && ((v >= 5 && skill_id == MO_EXTREMITYFIST) || (v >= 4 && skill_id == MO_CHAINCOMBO) || (v >= 3 && skill_id == MO_INVESTIGATE) || skill_id == MO_FINGEROFFENSIVE);
            if !ok {
                return false;
            }
        }
        // Only do skill specific checks here, normal attacks should have been checked already
        if scl.has(SC_DANCING) && flag != 2 && skill_id != 0 {
            if crate::db::skills::skill_db().find(skill_id).is_none() {
                return false;
            }
            let is_pc = src.map(|s| s.kind == map::BL_PC).unwrap_or(false);
            if is_pc && ((WA_SWING_DANCE..=WM_UNLIMITED_HUMMING_VOICE).contains(&skill_id) || skill_id == WM_FRIGG_SONG) {
                // Lvl 5 Lesson or higher allow you use 3rd job skills while dancing.
                let aid = src.map(|s| s.id).unwrap_or(0);
                if crate::skill::checkskill(c, aid, WM_LESSON) < 5 {
                    return false;
                }
            } else if !sk::skill_get_inf2(skill_id, &[crate::db::skills::INF2_ALLOWWHENPERFORMING]) {
                // Skills that can be used in dancing state
                return false;
            }
        }
        // Do not block item-casted skills.
        let item_casted = src.map(|s| s.kind == map::BL_PC && crate::skill::state::get(c, s.id).skillitem == skill_id as i16).unwrap_or(false);
        if skill_id != 0 && !item_casted {
            // Skills blocked through status changes...
            let block = |t: i32| scl.has(t) && src.map(|s| crate::skill::misc::skill_block_check(c, s, t, skill_id) != 0).unwrap_or(false);
            if flag == 0
                && ((src_cant(SCS_NOCAST) && skill_id != RK_REFRESH && skill_id != SU_GROOMING && skill_id != SR_GENTLETOUCH_CURE)
                    || (scl.has(SC_MARIONETTE) && skill_id != CG_MARIONETTE)
                    || (scl.has(SC_MARIONETTE2) && skill_id == CG_MARIONETTE)
                    || block(SC_ANKLE)
                    || block(SC_STASIS)
                    || block(SC_BITE)
                    || block(SC_NOVAEXPLOSING)
                    || block(SC_GRAVITYCONTROL)
                    || block(SC_KAGEHUMI)
                    || (scl.has(SC_ENSEMBLEFATIGUE) && skill_id != CG_SPECIALSINGER))
            {
                return false;
            }
            // Skill blocking.
            if (scl.has(SC_VOLCANO) && skill_id == WZ_ICEWALL)
                || (scl.has(SC_HERMODE) && sk::skill_get_inf(skill_id) & INF_SUPPORT_SKILL as i32 != 0)
                || (scl.has(SC_NOCHAT) && scl.v1(SC_NOCHAT) & super::MANNER_NOSKILL != 0)
            {
                return false;
            }
        }
        if src_option != 0 {
            // We do not check it for non-players here as hide will not stop monsters from scanning for new targets
            if src_option & unit::OPTION_HIDE != 0 && src.map(|s| s.kind) == Some(map::BL_PC) && (skill_id == 0 || !sk::skill_get_inf2(skill_id, &[INF2_ALLOWWHENHIDDEN])) {
                return false;
            }
            if src_option & unit::OPTION_CHASEWALK != 0 && skill_id != ST_CHASEWALK {
                return false;
            }
        }
    }
    let Some(t) = target else { return true }; // No further checking needed.
    if src.map(|s| s.id) == Some(t.id) {
        return true;
    }
    if !tsc.empty() {
        if skill_id == 0 && tsc.has(SC_TRICKDEAD) {
            return false;
        }
        if (skill_id == WZ_STORMGUST || skill_id == WZ_FROSTNOVA || skill_id == NJ_HYOUSYOURAKU || skill_id == NPC_STORMGUST2) && tsc.has(SC_FREEZE) {
            return false;
        }
        if skill_id == PR_LEXAETERNA && (tsc.has(SC_FREEZE) || tsc.has(SC_STONE)) {
            return false;
        }
        if tsc.has(SC__MANHOLE) && !sk::skill_get_inf2(skill_id, &[INF2_TARGETMANHOLE]) {
            return false;
        }
    }
    // If targetting, cloak+hide protect you, otherwise only hiding does.
    let mut hide_flag = if flag != 0 { unit::OPTION_HIDE } else { unit::OPTION_HIDE | unit::OPTION_CLOAK | unit::OPTION_CHASEWALK };
    // Skill that can hit hidden target
    if sk::skill_get_inf2(skill_id, &[INF2_TARGETHIDDEN]) {
        hide_flag &= !unit::OPTION_HIDE;
    }
    let toption = t.sc.option;
    match t.kind {
        map::BL_PC => {
            let is_boss = src.map(|s| crate::status::status_get_status_data(c, s).class_ as i32 == crate::status::defs::CLASS_BOSS).unwrap_or(false);
            let is_detect = has_mode(crate::db::mobs::MD_DETECTOR);
            if toption & unit::OPTION_INVISIBLE != 0 {
                return false; // pc_isinvisible
            }
            let perfect_hiding = crate::status::data::sd_get(c, t.id).map(|sd| sd.special_state.perfect_hiding).unwrap_or(false);
            if toption & hide_flag != 0 && !is_boss && (perfect_hiding || !is_detect) {
                return false;
            }
            if (tsc.has(SC_CLOAKINGEXCEED) || tsc.has(SC_NEWMOON)) && !is_boss && (perfect_hiding || is_detect) {
                return false; // Works against insect and demon but not against bosses
            }
            if tsc.has(SC__FEINTBOMB) && (is_boss || is_detect) {
                return false; // Works against all
            }
            if (tsc.has(SC_CAMOUFLAGE) || tsc.has(SC_STEALTHFIELD) || tsc.has(SC_SUHIDE)) && !(is_boss || is_detect) && (skill_id == 0 || (flag == 0 && src.is_some())) {
                return false; // Insect, demon, and boss can detect
            }
        }
        BL_ITEM => {
            // Allow targetting of items to pick'em up (or in the case of mobs, to loot them).
            return has_mode(crate::db::mobs::MD_LOOTER);
        }
        map::BL_HOM | map::BL_MER | map::BL_ELEM => {
            // Can't use support skills on Homunculus (only Master/Self) (status.cpp:2265-2266; hom_setting 0x3D)
            if t.kind == map::BL_HOM && skill_id != 0 && crate::homunculus::hom_setting() & crate::homunculus::data::HOMSET_NO_SUPPORT_SKILL != 0 && sk::skill_get_inf(skill_id) & INF_SUPPORT_SKILL as i32 != 0 && src.map(|s| s.id) != Some(crate::battle::target::battle_get_master(c, t).id) {
                return false;
            }
            // status.cpp:2267-2268
            let from_master = src.map(|s| crate::battle::target::battle_get_master(c, t).id == s.id).unwrap_or(false);
            if t.kind == map::BL_MER && (skill_id == sk::PR_ASPERSIO || (sk::SA_FLAMELAUNCHER..=sk::SA_SEISMICWEAPON).contains(&skill_id)) && !from_master {
                return false; // Can't use Weapon endow skills on Mercenary (only Master)
            }
            if skill_id == sk::AM_POTIONPITCHER && (t.kind == map::BL_MER || t.kind == map::BL_ELEM) {
                return false; // Can't use Potion Pitcher on Mercenaries
            }
            if tsc.has(SC_ELEMENTAL_VEIL) && !src.map(|s| crate::status::status_get_status_data(c, s).class_ as i32 == crate::status::defs::CLASS_BOSS).unwrap_or(false) && !has_mode(crate::db::mobs::MD_DETECTOR) {
                return false;
            }
            if toption & hide_flag != 0 && !has_mode(crate::db::mobs::MD_DETECTOR) {
                return false;
            }
        }
        _ => {
            // Check for chase-walk/hiding/cloaking opponents.
            if toption & hide_flag != 0 && !has_mode(crate::db::mobs::MD_DETECTOR) {
                return false;
            }
        }
    }
    true
}

/// status.cpp status_check_visibility: can `src` see `target` (within its view range: a monster's chase range)?
pub fn status_check_visibility(c: &Ctx, src: &Unit, target: &Unit, checkblind: bool) -> bool {
    use crate::status::consts::*;
    use crate::status::sc;
    let mut view_range = match src.kind {
        BL_MOB => get(c, src.id).and_then(|md| super::db(c, &md).map(|d| d.range3 as i32)).unwrap_or(crate::conf::area_size() as i32),
        // BL_PET: pd->db->range2 (pet/data.rs)
        map::BL_PET => crate::pet::data::get(c, src.id).and_then(|pd| crate::pet::data::mob_db_of(c, &pd).map(|d| d.range2 as i32)).unwrap_or(crate::conf::area_size() as i32),
        _ => crate::conf::area_size() as i32,
    };
    if checkblind && sc::has_sce(c, src.id, SC_BLIND) {
        view_range = 1;
    }
    if src.map != target.map || !check_distance_bl(src, target, view_range) {
        return false;
    }
    if src.kind == map::BL_NPC {
        return true; // NPCs don't care for the rest
    }
    let tsc = sc::load(c, target.id);
    let st = crate::status::status_get_status_data(c, src);
    let is_boss = st.class_ as i32 == crate::status::defs::CLASS_BOSS;
    let is_detector = st.mode & crate::db::mobs::MD_DETECTOR == crate::db::mobs::MD_DETECTOR;
    let toption = target.sc.option;
    match target.kind {
        map::BL_PC => {
            let perfect_hiding = crate::status::data::sd_get(c, target.id).map(|sd| sd.special_state.perfect_hiding).unwrap_or(false);
            if (toption & (unit::OPTION_HIDE | unit::OPTION_CLOAK | unit::OPTION_CHASEWALK) != 0 || tsc.has(SC_CAMOUFLAGE) || tsc.has(SC_STEALTHFIELD) || tsc.has(SC_SUHIDE)) && !is_boss && (perfect_hiding || !is_detector) {
                return false;
            }
            if (tsc.has(SC_CLOAKINGEXCEED) || tsc.has(SC_NEWMOON)) && !is_boss && (perfect_hiding || is_detector) {
                return false;
            }
            if tsc.has(SC__FEINTBOMB) && !is_boss && !is_detector {
                return false;
            }
        }
        map::BL_ELEM => {
            if tsc.has(SC_ELEMENTAL_VEIL) && !is_boss && !is_detector {
                return false;
            }
        }
        _ => {
            if (toption & (unit::OPTION_HIDE | unit::OPTION_CLOAK | unit::OPTION_CHASEWALK) != 0 || tsc.has(SC_CAMOUFLAGE) || tsc.has(SC_STEALTHFIELD) || tsc.has(SC_SUHIDE)) && !is_boss && !is_detector {
                return false;
            }
        }
    }
    true
}

/// A unit that is not a monster can still be the special-AI summon's master: DEFAULT_ENEMY_TYPE needs the monster.
pub fn is_summon(md: &MobData) -> bool {
    md.special_ai != AI_NONE
}
