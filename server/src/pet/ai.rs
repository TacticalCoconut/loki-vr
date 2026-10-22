//! Pet AI (pet.cpp pet_ai_hard, pet_ai_sub_foreachclient, pet_ai_sub_hard, pet_randomwalk,
//! pet_ai_sub_hard_lootsearch, pet_lootitem_drop, pet_delay_item_drop) and the unit.cpp parts pets need:
//! unit_calc_pos, the BL_PET flavour of unit_remove_map, unit_free for BL_PET, unit_warp for a pet, the master's
//! teleport timer (unit_check_start_teleport_timer / unit_teleport_timer) and unit_movepos's pet teleport.

use spacetimedb::Table;

use super::data::{self, pet_ai_state, pet_delayed_drop, PetAiState, PetDelayedDrop, MIN_PETTHINKTIME, PET_INTIMATE_NONE};
use super::{pet_unlocktarget, pet_attack_support};
use crate::map::{self, CellChk, BL_ITEM, BL_PET};
use crate::mob::unit::{check_distance_bl, check_distance_blxy, distance_bl, Reach};
use crate::path::{self, DIRX, DIRY, MOVE_COST, MOVE_DIAGONAL_COST};
use crate::timer::{self, Func};
use crate::unit::{self, Unit, CLR_DEAD, CLR_OUTSIGHT, USW_RELEASE_TARGET};
use crate::world::{Ctx, Tick};

// ---------------------------------------------------------------------------------------------- the AI timer

/// pet.cpp do_init_pet: `add_timer_interval(gettick()+MIN_PETTHINKTIME, pet_ai_hard, 0, 0, MIN_PETTHINKTIME)`. The
/// module has no server start: the interval timer is created with the first pet that comes out (and again if it is
/// ever gone). Without pets pet_ai_hard does nothing, so only the timer's phase differs, which is arbitrary on
/// rAthena too.
pub fn ensure_ai_timer(c: &Ctx) {
    let s = c.db().pet_ai_state().key().find(0);
    if let Some(s) = &s {
        if s.timer != 0 && timer::get(c, s.timer).is_some() {
            return;
        }
    }
    let t = timer::add_interval(c, c.tick() + MIN_PETTHINKTIME, Func::PetAiHard, 0, 0, MIN_PETTHINKTIME);
    let row = PetAiState { key: 0, timer: t };
    if s.is_some() {
        c.db().pet_ai_state().key().update(row);
    } else {
        c.db().pet_ai_state().insert(row);
    }
}

/// pet.cpp pet_ai_hard (interval timer): map_foreachpc(pet_ai_sub_foreachclient, tick), in pc_db's order.
pub fn pet_ai_hard(c: &Ctx, tick: Tick) {
    use super::data::pet_data as _;
    use crate::pc::pc as _;
    if c.db().pet_data().count() == 0 {
        if let Some(s) = c.db().pet_ai_state().key().find(0) {
            if s.timer != 0 {
                timer::delete(c, s.timer);
                c.db().pet_ai_state().key().update(PetAiState { key: 0, timer: 0 });
            }
        }
        return;
    }
    let mut pcs: Vec<u32> = c.db().pc().iter().map(|p| p.account_id).collect();
    pcs.sort_by_key(|&aid| crate::party::idb_order(aid));
    for aid in pcs {
        pet_ai_sub_foreachclient(c, aid, tick);
    }
}

/// pet.cpp pet_ai_sub_foreachclient.
fn pet_ai_sub_foreachclient(c: &Ctx, aid: u32, tick: Tick) {
    if super::pet_id_of(c, aid) != 0 {
        if let Some(pd) = data::of_master(c, aid) {
            pet_ai_sub_hard(c, pd.id, aid, tick);
        }
    }
}

// ---------------------------------------------------------------------------------------------- walking

/// pet.cpp pet_randomwalk: a pet that may move walks to a random cell within 12 - move_fail_count (at least 5) cells
/// (20 tries); its next random walk comes MIN_RANDOMWALKTIME + the walk's duration + up to 1 s later. 1 = walked.
fn pet_randomwalk(c: &Ctx, pd_id: u32, tick: Tick) -> i32 {
    let Some(mut pd) = data::get(c, pd_id) else { return 0 };
    let Some(u) = unit::get(c, pd_id) else { return 0 };
    if pd.next_walktime - tick < 0 && unit::can_move(c, &u) {
        const RETRYCOUNT: i32 = 20;
        let d = (12 - pd.move_fail_count).max(5);
        for i in 0..RETRYCOUNT {
            let u = unit::get(c, pd_id).unwrap_or_else(|| u.clone());
            let x = u.x as i32 + c.rnd_value(-d, d);
            let y = u.y as i32 + c.rnd_value(-d, d);
            if map::getcell(c, u.map, x as i16, y as i16, CellChk::ChkPass) && unit::walktoxy(c, pd_id, x as i16, y as i16, 0) {
                pd.move_fail_count = 0;
                break;
            }
            if i + 1 >= RETRYCOUNT {
                pd.move_fail_count += 1;
                if pd.move_fail_count > 1000 {
                    log::warn!("Pet can't move. Holding position {}, class = {}", pd.id, pd.pet.class_);
                    pd.move_fail_count = 0;
                    data::put(c, &pd);
                    if let Some(mut u2) = unit::get(c, pd_id) {
                        u2.ud.canmove_tick = tick + 60000;
                        unit::save(c, &u2);
                    }
                    return 0;
                }
            }
        }
        let u = unit::get(c, pd_id).unwrap_or(u);
        let speed = pd.status.speed as i64;
        let mut cost: i64 = 0;
        for i in 0..u.ud.path_len as usize {
            let Some(&dir) = u.ud.path.get(i) else { break };
            if path::direction_diagonal(dir) {
                cost += speed * MOVE_DIAGONAL_COST as i64 / MOVE_COST as i64;
            } else {
                cost += speed;
            }
        }
        pd.next_walktime = tick + crate::mob::MIN_RANDOMWALKTIME + cost + c.rnd_value(0, 999) as i64;
        data::put(c, &pd);
        return 1;
    }
    0
}

/// pet.cpp pet_ai_sub_hard: the pet's decision every MIN_PETTHINKTIME: a pet without intimacy wanders; far from its
/// master it runs to him at half his walk time; next to him it keeps its walk speed, goes for its target (a monster
/// with attack / damage support, or a floor item when it loots) or stays within 3 cells of him.
fn pet_ai_sub_hard(c: &Ctx, pd_id: u32, aid: u32, tick: Tick) -> i32 {
    let Some(mut pd) = data::get(c, pd_id) else { return 0 };
    let Some(pu) = unit::get(c, pd_id) else { return 0 };
    let Some(sd) = unit::get(c, aid) else { return 0 };
    if !pu.on_map || !sd.on_map {
        return 0;
    }
    if tick - pd.last_thinktime < MIN_PETTHINKTIME {
        return 0;
    }
    pd.last_thinktime = tick;
    data::put(c, &pd);
    if pu.ud.attacktimer != 0 || pu.ud.skilltimer != 0 || pu.map != sd.map {
        return 0;
    }
    if pu.ud.walktimer != 0 && pu.ud.path_pos <= 2 {
        return 0; // No thinking when you just started to walk.
    }
    if pd.pet.intimate as i32 <= PET_INTIMATE_NONE {
        // Pet should just... well, random walk.
        pet_randomwalk(c, pd_id, tick);
        return 0;
    }
    let range3 = data::mob_db_of(c, &pd).map(|d| d.range3 as i32).unwrap_or(0);
    let range2 = data::mob_db_of(c, &pd).map(|d| d.range2 as i32).unwrap_or(0);
    if !check_distance_bl(&sd, &pu, range3) {
        // Master too far, chase.
        if pd.target_id != 0 {
            pet_unlocktarget(c, pd_id);
        }
        let Some(pu) = unit::get(c, pd_id) else { return 0 };
        if pu.ud.walktimer != 0 && pu.ud.target == sd.id {
            return 0; // Already walking to him
        }
        if tick - pu.ud.canmove_tick < 0 {
            return 0; // Can't move yet.
        }
        let Some(mut pd) = data::get(c, pd_id) else { return 0 };
        let master_speed = crate::status::data::sd_get(c, aid).map(|s| s.battle_status.speed).unwrap_or(crate::status::defs::DEFAULT_WALK_SPEED as u16);
        pd.status.speed = master_speed / 2;
        if pd.status.speed == 0 {
            pd.status.speed = 1;
        }
        data::put(c, &pd);
        if !crate::mob::unit::unit_walktobl(c, pd_id, aid, 3, 0) {
            pet_randomwalk(c, pd_id, tick);
        }
        return 0;
    }
    // Return speed to normal.
    let walk_speed = data::get_pet_walk_speed(c, &pd);
    if pd.status.speed != walk_speed {
        if pu.ud.walktimer != 0 {
            return 0; // Wait until the pet finishes walking back to master.
        }
        pd.status.speed = walk_speed;
        data::put(c, &pd);
        if let Some(mut u) = unit::get(c, pd_id) {
            u.ud.change_walk_target = true;
            unit::save(c, &u);
        }
    }
    let mut target: Option<Unit> = None;
    if pd.target_id != 0 {
        // map_id2bl: any object of that id, on a map or not
        target = unit::get(c, pd.target_id);
        let keep = match &target {
            Some(t) => {
                let pu = unit::get(c, pd_id).unwrap_or(pu.clone());
                pu.map == t.map && !crate::status::sc::status_isdead(c, t) && check_distance_bl(&pu, t, range3)
            }
            None => false,
        };
        if !keep {
            target = None;
            pet_unlocktarget(c, pd_id);
        }
    }
    let pd = data::get(c, pd_id).unwrap_or(pd);
    let pu = unit::get(c, pd_id).unwrap_or(pu);
    if target.is_none() {
        if let Some(loot) = &pd.loot {
            if loot.count() < loot.max && tick - pu.ud.canact_tick > 0 {
                // Use half the pet's range of sight.
                for bl in map::units_in_range(c, pu.map, pu.x, pu.y, (range2 / 2) as i16, BL_ITEM) {
                    pet_ai_sub_hard_lootsearch(c, pd_id, &bl, &mut target);
                }
            }
        }
    }
    let pd = data::get(c, pd_id).unwrap_or(pd);
    let pu = unit::get(c, pd_id).unwrap_or(pu);
    let Some(target) = target else {
        // Just walk around.
        if check_distance_bl(&sd, &pu, 3) {
            return 0; // Already next to master.
        }
        if pu.ud.walktimer != 0 && check_distance_blxy(&sd, pu.ud.to_x, pu.ud.to_y, 3) {
            return 0; // Already walking to him
        }
        unit_calc_pos(c, pd_id, sd.x as i32, sd.y as i32, sd.ud.dir);
        let Some(pu) = unit::get(c, pd_id) else { return 0 };
        if !unit::walktoxy(c, pd_id, pu.ud.to_x, pu.ud.to_y, 0) {
            pet_randomwalk(c, pd_id, tick);
        }
        return 0;
    };
    if pu.ud.target == target.id && (pu.ud.attacktimer != 0 || pu.ud.walktimer != 0) {
        return 0; //Target already locked.
    }
    if target.kind != BL_ITEM {
        // enemy targeted
        if !crate::battle::target::battle_check_range(c, &pu, &target, pd.status.rhw.range as i32) {
            // Chase
            if !crate::mob::unit::unit_walktobl(c, pd_id, target.id, pd.status.rhw.range as i32, 2) {
                pet_unlocktarget(c, pd_id); // Unreachable target.
            }
            return 0;
        }
        //Continuous attack.
        crate::battle::unit_attack::unit_attack(c, pd_id, pd.target_id, 1);
        let _ = pet_attack_support();
    } else {
        // Item Targeted, attempt loot
        if !check_distance_bl(&pu, &target, 1) {
            // Out of range
            if !crate::mob::unit::unit_walktobl(c, pd_id, target.id, 1, 1) {
                pet_unlocktarget(c, pd_id); // Unreachable target.
            }
            return 0;
        }
        let Some(fitem) = c.db().flooritem().id().find(target.id) else {
            pet_unlocktarget(c, pd_id);
            return 0;
        };
        let mut pd = pd;
        if let Some(loot) = pd.loot.as_mut() {
            if loot.count() < loot.max {
                loot.items.push(fitem.item.clone());
                // uint16 weight
                loot.weight = loot.weight.wrapping_add((crate::db::items::itemdb_weight(fitem.item.nameid).wrapping_mul(fitem.item.amount as u32)) as u16);
                data::put(c, &pd);
                crate::flooritem::map_clearflooritem(c, target.id);
            }
        }
        //Target is unlocked regardless of whether it was picked or not.
        pet_unlocktarget(c, pd_id);
    }
    0
}

use crate::flooritem::flooritem as _;

/// pet.cpp pet_ai_sub_hard_lootsearch: a floor item the pet may take (nobody's, or its master's) that it can reach
/// within its view range becomes its target when closer than the one it has. 1 = picked.
fn pet_ai_sub_hard_lootsearch(c: &Ctx, pd_id: u32, bl: &Unit, target: &mut Option<Unit>) -> i32 {
    let Some(mut pd) = data::get(c, pd_id) else { return 0 };
    let Some(pu) = unit::get(c, pd_id) else { return 0 };
    let Some(fitem) = c.db().flooritem().id().find(bl.id) else { return 0 };
    let sd_charid = fitem.first_get_charid;
    let master_charid = crate::pc::get(c, pd.master).map(|p| p.char_id).unwrap_or(0);
    if sd_charid != 0 && sd_charid != master_charid {
        return 0;
    }
    let range2 = data::mob_db_of(c, &pd).map(|d| d.range2 as i32).unwrap_or(0);
    let reach = crate::mob::unit::unit_can_reach_bl(c, &pu, bl, range2, 1);
    let closer = match target {
        None => true, //New target closer than previous one.
        Some(t) => !check_distance_bl(&pu, t, distance_bl(&pu, bl)),
    };
    if matches!(reach, Reach::Same | Reach::At(..)) && closer {
        *target = Some(bl.clone());
        pd.target_id = bl.id;
        data::put(c, &pd);
        return 1;
    }
    0
}

// ---------------------------------------------------------------------------------------------- loot

/// pet.cpp pet_lootitem_drop: the pet's loot goes to `sd` (items that do not fit, and everything when there is no
/// owner, drop 500 ms later where the pet stands); the pet does not loot for 10 s.
pub fn pet_lootitem_drop(c: &Ctx, pd_id: u32, sd: Option<u32>) {
    let Some(mut pd) = data::get(c, pd_id) else { return };
    let Some(loot) = pd.loot.clone() else { return };
    if loot.count() == 0 {
        return;
    }
    let Some(pu) = unit::get(c, pd_id) else { return };
    let mut items = Vec::new();
    for it in &loot.items {
        let mut it = it.clone();
        if let Some(aid) = sd {
            let amount = it.amount as i32;
            let flag = crate::inventory::pc_additem(c, aid, &mut it, amount, crate::map_log::LOG_TYPE_PICKDROP_PLAYER);
            if flag == crate::inventory::ADDITEM_SUCCESS {
                continue;
            }
            // Inform client about failure to add
            crate::clif_items::additem(c, aid, 0, 0, flag);
        }
        // Store the drop for later
        items.push(it);
    }
    //The smart thing to do is use pd->loot->max (thanks for pointing it out, Shinomori)
    if let Some(l) = pd.loot.as_mut() {
        l.items.clear();
        l.weight = 0;
    }
    data::put(c, &pd);
    if let Some(mut u) = unit::get(c, pd_id) {
        u.ud.canact_tick = c.tick() + 10000; //prevent picked up during 10*1000ms
        unit::save(c, &u);
    }
    if !items.is_empty() {
        let row = PetDelayedDrop { id: pd_id, m: pu.map, x: pu.x, y: pu.y, items };
        if c.db().pet_delayed_drop().id().find(pd_id).is_some() {
            c.db().pet_delayed_drop().id().update(row);
        } else {
            c.db().pet_delayed_drop().insert(row);
        }
        timer::add(c, c.tick() + 500, Func::PetDelayItemDrop, pd_id, 0);
    }
    let _ = &mut pd;
}

/// pet.cpp pet_delay_item_drop (timer, target = the pet's id): the stored drop list goes to the floor (flag 4: no
/// drop checks; no loot priority).
pub fn pet_delay_item_drop(c: &Ctx, id: u32) -> i32 {
    let Some(list) = c.db().pet_delayed_drop().id().find(id) else { return 0 };
    for it in &list.items {
        crate::flooritem::map_addflooritem(c, it, it.amount as i32, list.m, list.x, list.y, 0, 0, 0, 4, 0, false, None, 0);
    }
    c.db().pet_delayed_drop().id().delete(id);
    0
}

// ---------------------------------------------------------------------------------------------- unit.cpp for pets

/// unit.cpp unit_calc_pos: the cell two cells behind the master (against `dir`) the unit should walk to, else one cell
/// closer, else a random direction (12 tries), else the master's cell (rAthena sets `y = tx` there: kept). The cell goes
/// to ud.to_x / to_y; 1 = no reachable cell.
pub fn unit_calc_pos(c: &Ctx, id: u32, tx: i32, ty: i32, dir: u8) -> i32 {
    let Some(mut u) = unit::get(c, id) else { return 1 };
    if dir >= path::DIR_MAX {
        return 1;
    }
    u.ud.to_x = tx as i16;
    u.ud.to_y = ty as i16;
    unit::save(c, &u);
    let reach = |x: i32, y: i32| crate::mob::unit::unit_can_reach_pos(u.map, u.x, u.y, x as i16, y as i16, 0);
    // 2 cells from Master Position
    let mut dx = -(DIRX[dir as usize] as i32) * 2;
    let mut dy = -(DIRY[dir as usize] as i32) * 2;
    let mut x = tx + dx;
    let mut y = ty + dy;
    if !reach(x, y) {
        if dx > 0 {
            x -= 1;
        } else if dx < 0 {
            x += 1;
        }
        if dy > 0 {
            y -= 1;
        } else if dy < 0 {
            y += 1;
        }
        if !reach(x, y) {
            let mut i = 0;
            while i < 12 {
                let k = c.rnd_value(path::DIR_NORTH as i32, path::DIR_NORTHEAST as i32) as usize; // Pick a Random Dir
                dx = -(DIRX[k] as i32) * 2;
                dy = -(DIRY[k] as i32) * 2;
                x = tx + dx;
                y = ty + dy;
                if reach(x, y) {
                    break;
                } else {
                    if dx > 0 {
                        x -= 1;
                    } else if dx < 0 {
                        x += 1;
                    }
                    if dy > 0 {
                        y -= 1;
                    } else if dy < 0 {
                        y += 1;
                    }
                    if reach(x, y) {
                        break;
                    }
                }
                i += 1;
            }
            if i == 12 {
                x = tx;
                y = tx; // Exactly Master Position
                if !reach(x, y) {
                    return 1;
                }
            }
        }
    }
    if let Some(mut u) = unit::get(c, id) {
        u.ud.to_x = x as i16;
        u.ud.to_y = y as i16;
        unit::save(c, &u);
    }
    0
}

/// unit.cpp unit_remove_map for a pet (unit::remove_map hands BL_PET here): the pet stops (walk, attack, step
/// action), and leaves the map; a pet that lost its intimacy runs away for good (freed), unless its master is logging
/// out. Pets have no status changes (status_get_sc is nullptr for BL_PET). False = not on a map, or freed.
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
    // (unit_skillcastcancel while casting: pet skills, next wave)
    //Clear target even if there is no timer
    if u.ud.target != 0 || u.ud.attacktimer != 0 {
        unit::stop_attack(c, &mut u);
    }
    //Clear stepaction even if there is no timer
    if u.ud.stepaction || u.ud.steptimer != 0 {
        unit::stop_stepaction(c, &mut u);
    }
    // Do not reset can-act delay. [Skotlex]
    u.ud.attackabletime = c.tick();
    u.ud.canmove_tick = c.tick();
    unit::save(c, &u);
    if let Some(pd) = data::get(c, id) {
        let master_logging_out = crate::pc::get(c, pd.master).map(|p| !p.active).unwrap_or(false);
        if pd.pet.intimate as i32 <= PET_INTIMATE_NONE && !master_logging_out {
            // If logging out, this is deleted on unit_free
            crate::clif::clearunit_area(c, &u, clrtype);
            u.on_map = false; // map_delblock
            unit::save(c, &u);
            unit_free(c, id, CLR_OUTSIGHT);
            return false;
        }
    }
    // (skill_unit_move(bl, tick, 4) / skill_cleartimerskill: ground skill units, phase 4)
    crate::clif::clearunit_area(c, &u, clrtype);
    u.on_map = false; // map_delblock
    unit::save(c, &u);
    true
}

/// unit.cpp unit_free for a pet: off the map (if on it), its autobonuses, hunger timer and support bonuses go, the
/// pet is saved (or deleted when it lost its intimacy: the owner's pet id goes too), the owner loses sd->pd, and the
/// pet's rows go (map_deliddb / map_freeblock).
pub fn unit_free(c: &Ctx, id: u32, clrtype: u8) -> i32 {
    if unit::get(c, id).map(|u| u.on_map).unwrap_or(false) {
        unit_remove_map(c, id, clrtype);
    }
    let Some(pd) = data::get(c, id) else {
        // freed meanwhile (unit_remove_map's runaway branch)
        return 0;
    };
    let aid = pd.master;
    super::bonus::pet_delautobonus(c, aid, 1, false);
    super::bonus::pet_delautobonus(c, aid, 2, false);
    super::bonus::pet_delautobonus(c, aid, 3, false);
    super::pet_hungry_timer_delete(c, id);
    super::pet_clear_support_bonuses(c, aid);
    let Some(pd) = data::get(c, id) else { return 0 };
    if pd.pet.intimate as i32 > PET_INTIMATE_NONE {
        super::intif_save_petdata(pd.pet.account_id, &pd.pet);
    } else {
        // Remove pet.
        super::intif_delete_petdata(pd.pet.pet_id);
        if crate::pc::get(c, aid).is_some() {
            super::set_pet_id(c, aid, 0);
        }
    }
    // sd->pd = nullptr; pd->master = nullptr; skill_clear_unitgroup / status_change_clear (pets have neither)
    if pd.masterteleport_timer != 0 {
        timer::delete(c, pd.masterteleport_timer);
    }
    // ud->hatEffects.clear(), map_deliddb, map_freeblock
    crate::pc_misc::set_hat_effects(c, id, Vec::new());
    c.db().pet_data().id().delete(id);
    c.db().unit().id().delete(id);
    0
}

use crate::unit::unit as _;
use data::pet_data as _;

/// unit.cpp unit_warp for a pet (unit_teleport_timer, the unitwarp script command): a random or checked cell, off the
/// map (the pet flavour of unit_remove_map), then on the new cell with its spawn packets. 0 success, 1 refused, 2 no
/// cell, 3 removal failed, 4 map_addblock failed.
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
    if !unit_remove_map(c, id, clrtype) {
        return 3;
    }
    // (clear_unit_onwarp has BL_PET: skill_clear_unitgroup — pets own no skill units)
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
    crate::skillunit::skill_unit_move(c, &u, c.tick(), 1);
    0
}

/// unit.cpp unit_check_start_teleport_timer for the pet of player `aid` (unit_walktoxy of the master): a pet more than
/// AREA_SIZE away from its master is warped to him in 3 s unless he comes back.
pub fn unit_check_start_teleport_timer(c: &Ctx, aid: u32) {
    let Some(mut pd) = data::of_master(c, aid) else { return };
    let Some(pu) = unit::get(c, pd.id) else { return };
    let Some(msd) = unit::get(c, aid) else { return };
    let max_dist = crate::conf::area_size() as i32;
    if !check_distance_bl(&msd, &pu, max_dist) {
        if pd.masterteleport_timer == 0 {
            pd.masterteleport_timer = timer::add(c, c.tick() + 3000, Func::UnitTeleportTimer, pd.id, max_dist as i64);
            data::put(c, &pd);
        }
    } else {
        if pd.masterteleport_timer != 0 {
            timer::delete(c, pd.masterteleport_timer);
        }
        pd.masterteleport_timer = 0; // Cancel recall
        data::put(c, &pd);
    }
}

/// unit.cpp unit_teleport_timer (target = the pet's id, data = the distance): the pet is still too far from its master:
/// unit_warp to him (CLR_TELEPORT).
pub fn unit_teleport_timer(c: &Ctx, tid: u64, id: u32, data: i64) {
    let Some(mut pd) = data::get(c, id) else { return };
    if pd.masterteleport_timer != tid {
        return;
    }
    pd.masterteleport_timer = 0;
    data::put(c, &pd);
    let (Some(msd), Some(pu)) = (unit::get(c, pd.master), unit::get(c, id)) else { return };
    if !check_distance_bl(&msd, &pu, data as i32) {
        unit_warp(c, id, msd.map as i32, msd.x, msd.y, unit::CLR_TELEPORT);
    }
}

/// unit.cpp unit_movepos's pet part (unit.cpp:1220-1236): after the master's instant move, a friendly pet that cannot
/// walk after him (no path, with checkpath off) or is out of his sight is moved to his cell (clif_slide).
pub fn unit_movepos_pet(c: &Ctx, aid: u32, checkpath: bool, dst_x: i16, dst_y: i16) {
    if super::pet_id_of(c, aid) == 0 {
        return;
    }
    let Some(pd) = data::of_master(c, aid) else { return };
    if pd.pet.intimate as i32 <= PET_INTIMATE_NONE {
        return;
    }
    let (Some(sd), Some(pbl)) = (unit::get(c, aid), unit::get(c, pd.id)) else { return };
    // Check if pet needs to be teleported. [Skotlex]
    let mut flag = 0;
    if !checkpath && path::path_search(pbl.map, pbl.x, pbl.y, dst_x, dst_y, 0, CellChk::ChkNoPass).is_none() {
        flag = 1;
    } else if !check_distance_bl(&sd, &pbl, crate::conf::area_size() as i32) {
        // Too far, teleport.
        flag = 2;
    }
    if flag != 0 {
        unit::movepos(c, pd.id, sd.x, sd.y, 0, false);
        if let Some(pbl) = unit::get(c, pd.id) {
            crate::battle::clif::clif_slide(c, &pbl, pbl.x, pbl.y);
        }
    }
}

/// unit.cpp unit_unattackable for a pet: pet_unlocktarget.
pub fn unit_unattackable(c: &Ctx, id: u32) {
    if unit::get(c, id).map(|u| u.kind == BL_PET).unwrap_or(false) {
        pet_unlocktarget(c, id);
    }
}

#[cfg(test)]
mod tests {
    #[test]
    fn calc_pos_offsets() {
        // unit_calc_pos: 2 cells from the master against his direction (dirx / diry of path.cpp)
        use crate::path::{DIRX, DIRY};
        assert_eq!((-(DIRX[0] as i32) * 2, -(DIRY[0] as i32) * 2), (0, -2)); // facing north: behind is south
        assert_eq!((-(DIRX[4] as i32) * 2, -(DIRY[4] as i32) * 2), (0, 2));
    }
}
