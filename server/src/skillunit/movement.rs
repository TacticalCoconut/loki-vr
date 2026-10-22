//! skill.cpp skill_unit_move / skill_unit_move_sub (characters stepping on and off units), skill_unit_move_unit and
//! skill_unit_move_unit_group (units that move: knocked-back traps, dances that follow their dancer), map.cpp
//! map_find_skill_unit_oncell, and the BL_CHAR parts of map.cpp map_moveblock that belong to skill units.

use std::cell::RefCell;

use crate::battle::target::battle_check_target;
use crate::db::skills::{self as sk, *};
use crate::map::{BL_CHAR, BL_PC, BL_SKILL};
use crate::status::consts::*;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

use super::consts::*;
use super::data::{self, SkillUnit};
use super::{effect, group, setting, util};

thread_local! {
    /// skill.cpp `static std::vector<int16> skill_unit_cell`: the skill ids a moving character leaves (filled by the
    /// flag-2 half of map_moveblock's skill_unit_move pair, read by the flag-3 half of the same call).
    static SKILL_UNIT_CELL: RefCell<Vec<i16>> = const { RefCell::new(Vec::new()) };
}

fn cell_push(id: u16) {
    SKILL_UNIT_CELL.with(|v| v.borrow_mut().push(id as i16));
}

/// util::vector_erase_if_exists: the first equal element goes.
fn cell_erase(id: i32) {
    SKILL_UNIT_CELL.with(|v| {
        let mut v = v.borrow_mut();
        if let Some(i) = v.iter().position(|&x| x as i32 == id) {
            v.remove(i);
        }
    });
}

/// skill.cpp skill_unit_move_sub: one unit on `target`'s cell. flag &1: onplace (else onout); &2: remember / forget
/// the skill ids left (the two halves of a map_moveblock); &4: onleft.
fn skill_unit_move_sub(c: &Ctx, bl: &Unit, target_id: u32, tick: Tick, flag: i32) -> i32 {
    let Some(unit) = data::get_unit(c, bl.id) else { return 0 };
    let Some(target) = unit::get(c, target_id) else { return 0 };
    if !unit.alive || !target.on_map {
        return 0;
    }
    let Some(group) = data::skill_id2group(c, unit.group_id) else { return 0 };
    if flag & 1 != 0 && (group.skill_id == PF_SPIDERWEB || group.skill_id == GN_THORNS_TRAP) {
        return 0; // Fiberlock is never supposed to trigger on skill_unit_move. [Inkfish]
    }
    let dissonance = setting::skill_dance_switch(c, unit.id(), false);
    let Some(group) = data::skill_id2group(c, unit.group_id) else { return 0 };
    //Necessary in case the group is deleted after calling on_place/on_out [Skotlex]
    let skill_id = group.skill_id;
    if group.interval != -1 && !skill_get_unit_flag(skill_id, &[UF_DUALMODE]) && skill_id != BD_LULLABY {
        //Lullaby is the exception, bugreport:411
        //Non-dualmode unit skills with a timer don't trigger when walking, so just return
        if dissonance {
            setting::skill_dance_switch(c, unit.id(), true);
            let result = effect::skill_unit_onout(c, unit.id(), &target, tick);
            // This activates the 20 seconds countdown from leaving the song/dance due to dissonance
            if result > 0 {
                effect::skill_unit_onleft(c, result as u16, &target, tick);
            }
        }
        return 0;
    }
    //Target-type check.
    if !(group.bl_flag & target.kind as i32 != 0 && battle_check_target(c, &unit.bl, &target, group.target_flag) > 0) {
        // Ensemble check to see if the caster or the partner went out/in of the area
        let partner = sc::get_sce(c, target.id, SC_DANCING).map(|e| group.src_id as i32 == e.val4).unwrap_or(false);
        if group.song_dance & 0x2 != 0 && (group.src_id == target.id || partner) {
            if flag & 1 != 0 {
                if flag & 2 != 0 {
                    //Clear this skill id.
                    cell_erase(skill_id as i32);
                }
            } else if flag & 2 != 0 {
                //Store this skill id.
                cell_push(skill_id);
            }
            if flag & 4 != 0 {
                effect::skill_unit_onleft(c, skill_id, &target, tick);
            }
        }
        if dissonance {
            setting::skill_dance_switch(c, unit.id(), true);
        }
        0
    } else {
        if flag & 1 != 0 {
            let result = effect::skill_unit_onplace(c, unit.id(), &target, tick);
            if flag & 2 != 0 && result > 0 {
                //Clear skill ids we have stored in onout.
                cell_erase(result);
            }
        } else {
            let result = effect::skill_unit_onout(c, unit.id(), &target, tick);
            if flag & 2 != 0 && result > 0 {
                //Store this unit id.
                cell_push(skill_id);
            }
        }
        //TODO: Normally, this is dangerous since the unit and group could be freed
        //inside the onout/onplace functions. Currently it is safe because we know song/dance
        //cells do not get deleted within them. [Skotlex]
        if dissonance {
            setting::skill_dance_switch(c, unit.id(), true);
        }
        if flag & 4 != 0 {
            effect::skill_unit_onleft(c, skill_id, &target, tick);
        }
        1
    }
}

/// skill.cpp skill_unit_move: `bl` moved (flag &1 onplace, else onout; &2 the two-call form of a move; &4 a forced
/// onleft, e.g. on death). `bl` is the unit's current block_list (its position is the cell looked at).
pub fn skill_unit_move(c: &Ctx, bl: &Unit, tick: Tick, flag: i32) -> i32 {
    if !bl.on_map {
        return 0;
    }
    if flag & 2 != 0 && flag & 1 == 0 {
        //Onout, clear data
        SKILL_UNIT_CELL.with(|v| v.borrow_mut().clear());
    }
    let ids = util::ids_in_cell(c, bl.map, bl.x, bl.y, BL_SKILL);
    util::each(c, &ids, |u| skill_unit_move_sub(c, u, bl.id, tick, flag));
    if flag & 2 != 0 && flag & 1 != 0 {
        //Onplace, check any skill units you have left.
        let left: Vec<i16> = SKILL_UNIT_CELL.with(|v| v.borrow().clone());
        let fresh = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
        for it in left {
            effect::skill_unit_onleft(c, it as u16, &fresh, tick);
        }
    }
    0
}

/// skill_unit_move for a unit id (callers that hold no row), at its current cell.
pub fn skill_unit_move_id(c: &Ctx, bl_id: u32, tick: Tick, flag: i32) -> i32 {
    match unit::get(c, bl_id) {
        Some(bl) => skill_unit_move(c, &bl, tick, flag),
        None => 0,
    }
}

/// skill.cpp skill_unit_move_unit: a single unit moved to (dx, dy) (a knocked-back trap).
pub fn skill_unit_move_unit(c: &Ctx, bl: &Unit, dx: i16, dy: i16) {
    let tick = c.tick();
    if bl.kind != BL_SKILL {
        return;
    }
    let Some(su) = data::get_unit(c, bl.id) else { return };
    if !su.alive {
        return;
    }
    if let Some(g) = data::skill_id2group(c, su.group_id) {
        if skill_get_unit_flag(g.skill_id, &[UF_ENSEMBLE]) {
            return; //Ensembles may not be moved around.
        }
    }
    if !su.bl.on_map {
        let mut u = su.bl.clone();
        u.x = dx;
        u.y = dy;
        unit::save(c, &u);
        return;
    }
    map_moveblock(c, bl.id, dx, dy, tick);
    let Some(su) = data::get_unit(c, bl.id) else { return };
    let bl_flag = data::skill_id2group(c, su.group_id).map(|g| g.bl_flag).unwrap_or(0);
    effect::foreachincell_unit_effect(c, &su, su.bl.map, su.bl.x, su.bl.y, bl_flag, tick, 1);
    if let Some(su) = data::get_unit(c, bl.id) {
        super::clif::skill_getareachar_skillunit_visibilty(c, &su, crate::clif::Target::Area);
    }
}

/// skill.cpp skill_unit_move_unit_group: the whole group moves by (dx, dy) on map `m` (a dance following its dancer,
/// a knockback group).
pub fn skill_unit_move_unit_group(c: &Ctx, group_id: i32, m: u16, dx: i16, dy: i16) {
    let tick = c.tick();
    let Some(group) = data::skill_id2group(c, group_id) else { return };
    if group.unit_count <= 0 {
        return;
    }
    if skill_get_unit_flag(group.skill_id, &[UF_ENSEMBLE]) {
        return; //Ensembles may not be moved around.
    }
    // group->unit[0..unit_count]: the live slots (the dead ones are skipped by every loop below)
    let slots: Vec<SkillUnit> = data::units_of(c, group_id).into_iter().filter(|u| u.alive).collect();
    let n = slots.len();
    let mut m_flag = vec![0i32; n];
    //    m_flag
    //		0: Neither of the following (skill_unit_onplace & skill_unit_onout are needed)
    //		1: Unit will move to a slot that had another unit of the same group (skill_unit_onplace not needed)
    //		2: Another unit from same group will end up positioned on this unit (skill_unit_onout not needed)
    //		3: Both 1+2.
    for i in 0..n {
        let unit1 = &slots[i];
        if unit1.bl.map != m {
            continue;
        }
        for unit2 in &slots {
            if unit1.bl.x + dx == unit2.bl.x && unit1.bl.y + dy == unit2.bl.y {
                m_flag[i] |= 0x1;
            }
            if unit1.bl.x - dx == unit2.bl.x && unit1.bl.y - dy == unit2.bl.y {
                m_flag[i] |= 0x2;
            }
        }
    }
    let mut j = 0usize;
    for i in 0..n {
        let id1 = slots[i].id();
        let Some(unit1) = data::get_live_unit(c, id1) else { continue };
        let bl_flag = data::skill_id2group(c, group_id).map(|g| g.bl_flag).unwrap_or(group.bl_flag);
        let song_dance = data::skill_id2group(c, group_id).map(|g| g.song_dance).unwrap_or(group.song_dance);
        if m_flag[i] & 0x2 == 0 {
            if song_dance & 0x1 != 0 {
                //Cancel dissonance effect.
                setting::skill_dance_overlap(c, id1, OVERLAP_REMOVE);
            }
            if let Some(u) = data::get_unit(c, id1) {
                effect::foreachincell_unit_effect(c, &u, u.bl.map, u.bl.x, u.bl.y, bl_flag, tick, 4);
            }
        }
        //Move Cell using "smart" criteria (avoid useless moving around)
        match m_flag[i] {
            0 => {
                //Cell moves independently, safely move it.
                if let Some(u) = unit::get(c, id1) {
                    for other in crate::map::units_in_move_area(c, &u, crate::conf::area_size(), dx, dy, BL_PC) {
                        crate::clif::outsight(c, &other, &u);
                    }
                    map_moveblock(c, id1, u.x + dx, u.y + dy, tick);
                }
            }
            1 => {
                //Cell moves unto another cell, look for a replacement cell that won't collide
                //and has no cell moving into it (flag == 2)
                while j < n {
                    let alive_j = data::get_live_unit(c, slots[j].id());
                    if m_flag[j] != 2 || alive_j.is_none() {
                        j += 1;
                        continue;
                    }
                    //Move to where this cell would had moved.
                    let unit2 = alive_j.unwrap();
                    let u = unit::get(c, id1).unwrap_or(unit1.bl.clone());
                    let dx2 = unit2.bl.x + dx - u.x;
                    let dy2 = unit2.bl.y + dy - u.y;
                    for other in crate::map::units_in_move_area(c, &u, crate::conf::area_size(), dx2, dy2, BL_PC) {
                        crate::clif::outsight(c, &other, &u);
                    }
                    map_moveblock(c, id1, unit2.bl.x + dx, unit2.bl.y + dy, tick);
                    j += 1; //Skip this cell as we have used it.
                    break;
                }
            }
            _ => {} //Don't move the cell as a cell will end on this tile anyway.
        }
        if m_flag[i] & 0x2 == 0 {
            //We only moved the cell in 0-1
            if song_dance & 0x1 != 0 {
                //Check for dissonance effect.
                setting::skill_dance_overlap(c, id1, OVERLAP_SET);
            }
            if let Some(u) = data::get_unit(c, id1) {
                super::clif::skill_getareachar_skillunit_visibilty(c, &u, crate::clif::Target::Area);
                effect::foreachincell_unit_effect(c, &u, u.bl.map, u.bl.x, u.bl.y, bl_flag, tick, 1);
            }
        }
    }
}

/// map.cpp map_find_skill_unit_oncell: a live unit of `skill_id` on (x, y) of `target`'s map other than `out_unit`
/// (flag &1: only one that may target `target`).
pub fn map_find_skill_unit_oncell(c: &Ctx, target: &Unit, x: i16, y: i16, skill_id: u16, out_unit: Option<u32>, flag: i32) -> Option<u32> {
    let md = crate::data::maps().get(target.map)?;
    if x < 0 || y < 0 || x >= md.xs || y >= md.ys {
        return None;
    }
    for bl in crate::map::units_in_area(c, target.map, x, y, x, y, BL_SKILL) {
        if bl.x != x || bl.y != y || bl.kind != BL_SKILL {
            continue;
        }
        let Some(unit) = data::get_unit(c, bl.id) else { continue };
        if Some(unit.id()) == out_unit || !unit.alive {
            continue;
        }
        let Some(g) = data::skill_id2group(c, unit.group_id) else { continue };
        if g.skill_id != skill_id {
            continue;
        }
        if flag & 1 == 0 || battle_check_target(c, &unit.bl, target, g.target_flag) > 0 {
            return Some(unit.id());
        }
    }
    None
}

/// map.cpp map_moveblock for code outside a walk (unit effects, trap snares): the unit row is loaded, moved and
/// saved. (unit.rs moveblock does the work, including the skill unit hooks below for characters.)
pub fn map_moveblock(c: &Ctx, bl_id: u32, x: i16, y: i16, tick: Tick) {
    let Some(mut u) = unit::get(c, bl_id) else { return };
    unit::moveblock(c, &mut u, x, y, tick);
    unit::save(c, &u);
}

/// map.cpp map_moveblock's BL_CHAR part before the move: `skill_unit_move(bl, tick, 2)` (onout, remembering the
/// skill ids left). `bl` is the character at its old cell; it is saved first, as the unit functions read the rows.
pub fn moveblock_before(c: &Ctx, bl: &Unit, tick: Tick) {
    if bl.kind & BL_CHAR == 0 || !bl.on_map {
        return;
    }
    unit::save(c, bl);
    skill_unit_move(c, bl, tick, 2);
    // the status changes a move ends (status2)
    crate::status::sc_ext::moveblock_before(c, bl.id);
}

/// map.cpp map_moveblock's BL_CHAR part after the move: `skill_unit_move(bl, tick, 3)` (onplace, then onleft for the
/// groups left), then the unit groups that follow the character (a dance's group; SC_WARM / SC_BANDING /
/// SC_NEUTRALBARRIER_MASTER / SC_STEALTHFIELD_MASTER; a guild master's auras). SC_PROPERTYWALK's new units (Fire /
/// Electric Walk) come with that status (status2 / skill families). `bl` is the saved character at its new cell.
pub fn moveblock_after(c: &Ctx, bl: &Unit, x0: i16, y0: i16, tick: Tick) {
    if bl.kind & BL_CHAR == 0 {
        return;
    }
    // x1, y1: the cell map_moveblock was asked for (the groups move by x1-x0, y1-y0 even when the onplace below warped
    // the character elsewhere, e.g. a Warp Portal's pc_setpos); skill_unit_move returns at once for a character that
    // is off the map by now, the group moves still run (map.cpp:583-627 do not check bl->prev)
    let (x1, y1) = (bl.x, bl.y);
    skill_unit_move(c, bl, tick, 3);
    let Some(bl) = unit::get(c, bl.id) else { return };
    // Shadow Form Target Moving (status2; x1, y1 stay the cell asked for, as map.cpp's parameters)
    crate::status::sc_ext::moveblock_shadowform_target(c, &bl);
    let Some(bl) = unit::get(c, bl.id) else { return };
    let scs = sc::load(c, bl.id);
    if scs.empty() {
        return;
    }
    let (dx, dy) = (x1 - x0, y1 - y0);
    if let Some(e) = scs.get(SC_DANCING) {
        skill_unit_move_unit_group(c, e.val2, bl.map, dx, dy);
    } else {
        // SC_CLOAKING's wall check (status2)
        crate::status::sc_ext::moveblock_cloaking(c, &bl);
        if let Some(e) = scs.get(SC_WARM) {
            skill_unit_move_unit_group(c, e.val4, bl.map, dx, dy);
        }
        if let Some(e) = scs.get(SC_BANDING) {
            skill_unit_move_unit_group(c, e.val4, bl.map, dx, dy);
        }
        if let Some(e) = scs.get(SC_NEUTRALBARRIER_MASTER) {
            skill_unit_move_unit_group(c, e.val2, bl.map, dx, dy);
        } else if let Some(e) = scs.get(SC_STEALTHFIELD_MASTER) {
            skill_unit_move_unit_group(c, e.val2, bl.map, dx, dy);
        }
        // SC__SHADOWFORM's distance check and SC_PROPERTYWALK's units (status2)
        crate::status::sc_ext::moveblock_after(c, &bl, x0, y0);
    }
    /* Guild Aura Moving */
    if bl.kind == BL_PC && crate::guild::gx(c, bl.id).gmaster_flag {
        for (t, v) in [(SC_LEADERSHIP, 4), (SC_GLORYWOUNDS, 4), (SC_SOULCOLD, 4), (SC_HAWKEYES, 4)] {
            if let Some(e) = scs.get(t) {
                let gid = if v == 4 { e.val4 } else { e.val2 };
                skill_unit_move_unit_group(c, gid, bl.map, dx, dy);
            }
        }
    }
}

#[allow(unused_imports)]
use {group as _, sk as _};
