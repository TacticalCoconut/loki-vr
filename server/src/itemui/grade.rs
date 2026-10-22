//! Enchant grade: clif.cpp clif_enchantgrade_add, clif_parse_enchantgrade_add, clif_enchantgrade_result,
//! clif_enchantgrade_announce, clif_parse_enchantgrade_start, clif_parse_enchantgrade_close (the window is opened by
//! clif_ui_open(OUT_UI_ENCHANTGRADE): @enchantgradeui / `enchantgradeui`).

use crate::clif::{self, Target};
use crate::db::items::enchantgrade::{self as eg_db, EnchantgradeLevel, MAX_ENCHANTGRADE};
use crate::db::items::{self as itemdb, IT_ARMOR, IT_WEAPON};
use crate::inventory::{self as inv, MAX_INVENTORY, MAX_REFINE};
use crate::net::packets::*;
use crate::net::wire::Reader;
use crate::unit;
use crate::world::Ctx;

use super::*;

/// status.hpp e_enchantgrade_result.
pub const ENCHANTGRADE_UPGRADE_SUCCESS: i32 = 0;
pub const ENCHANTGRADE_UPGRADE_FAILED: i32 = 1;
pub const ENCHANTGRADE_UPGRADE_DOWNGRADE: i32 = 2;
pub const ENCHANTGRADE_UPGRADE_BREAK: i32 = 3;

fn send_self(c: &Ctx, aid: u32, b: &[u8]) {
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, b, &sd, Target::SelfOnly);
    }
}

/// clif.cpp clif_enchantgrade_add: ZC_GRADE_ENCHANT_MATERIAL_LIST for the item at `index` with its grade entry, or
/// the empty answer (index -1) without one.
pub fn clif_enchantgrade_add(c: &Ctx, aid: u32, sel: Option<(u16, &EnchantgradeLevel)>) {
    let mut p = PACKET_ZC_GRADE_ENCHANT_MATERIAL_LIST { PacketType: PACKET_ZC_GRADE_ENCHANT_MATERIAL_LIST::ID as i16, ..Default::default() };
    match sel {
        Some((index, gl)) => {
            let refine = slot(c, aid, index as usize).map(|r| r.item.refine).unwrap_or(0) as usize;
            p.index = crate::clif_items::client_index(index as usize) as i16;
            p.success_chance = gl.chances.get(refine).copied().unwrap_or(0) as i32 / 100;
            p.blessing_info = GRADE_ENCHANT_BLESSING {
                id: itemdb::client_nameid(gl.catalyst.item) as i32,
                amount: gl.catalyst.amount_per_step as i32,
                max_blessing: gl.catalyst.maximum_steps as i32,
                bonus: gl.catalyst.chance_increase as i32 / 100,
            };
            // Not displayed by client
            p.protect_itemid = 0;
            p.protect_amount = 0;
            for option in &gl.options {
                p.material_info.push(GRADE_ENCHANT_MATERIAL {
                    nameid: itemdb::client_nameid(option.item) as i32,
                    amount: option.amount as i32,
                    price: option.zeny as i32,
                    downgrade: (option.downgrade_amount > 0) as i32,
                    breakable: (option.breaking_rate > 0) as i8,
                });
            }
        }
        None => {
            p.index = -1;
            p.success_chance = 0;
            p.blessing_info = GRADE_ENCHANT_BLESSING { id: 0, amount: 0, max_blessing: 0, bonus: 0 };
            p.protect_itemid = 0;
            p.protect_amount = 0;
        }
    }
    send_self(c, aid, &p.to_vec());
}

/// enchantgrade_db.find(type)->levels.find(level): the grade table of the item's weapon / armor level.
/// Err(()) = no table for this type (no answer); Ok(None) = no grades for this level.
fn levels_of(id: &itemdb::ItemData) -> Result<Option<&'static [EnchantgradeLevel]>, ()> {
    let Some(eg) = eg_db::enchantgrade(id.r#type as u16) else { return Err(()) };
    let level = match id.r#type {
        IT_WEAPON => id.weapon_level,
        IT_ARMOR => id.armor_level,
        _ => 0,
    };
    Ok(eg.levels.iter().find(|(l, _)| *l == level).map(|(_, g)| g.as_slice()))
}

/// clif.cpp clif_parse_enchantgrade_add (CZ_GRADE_ENCHANT_SELECT_EQUIPMENT 0x0b59).
pub fn clif_parse_enchantgrade_add(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !state(c, aid).enchantgrade_open {
        return;
    }
    let Some(p) = PACKET_CZ_GRADE_ENCHANT_SELECT_EQUIPMENT::read(&mut Reader::new(pkt)) else { return };
    let index = server_index(p.index as u16);
    if index as usize >= MAX_INVENTORY {
        return;
    }
    let Some(r) = slot(c, aid, index as usize) else { return };
    let id = inv::data_of(&r.item);
    // Unsupported item type - no answer, because client should have actually prevented this request
    let Ok(levels) = levels_of(id).map_err(|_| ()) else { return };
    // Item can't be enhanced
    if !id.flag.gradable {
        return;
    }
    // Cannot upgrade this weapon or armor level
    let Some(grades) = levels else {
        clif_enchantgrade_add(c, aid, None);
        return;
    };
    // Cannot increase enchantgrade any further - no answer, because client should have actually prevented this request
    let Some(gl) = grades.iter().find(|g| g.grade == r.item.enchantgrade as u16) else { return };
    clif_enchantgrade_add(c, aid, Some((index, gl)));
}

/// clif.cpp clif_enchantgrade_result: ZC_GRADE_ENCHANT_ACK with the slot's grade as it is now (0 for a deleted item).
pub fn clif_enchantgrade_result(c: &Ctx, aid: u32, index: u16, result: i32) {
    let grade = slot(c, aid, index as usize).map(|r| r.item.enchantgrade).unwrap_or(0);
    let p = PACKET_ZC_GRADE_ENCHANT_ACK {
        PacketType: PACKET_ZC_GRADE_ENCHANT_ACK::ID as i16,
        index: crate::clif_items::client_index(index as usize) as i16,
        grade: grade as i16,
        result,
    };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_enchantgrade_announce: ZC_GRADE_ENCHANT_BROADCAST_RESULT to every client.
pub fn clif_enchantgrade_announce(c: &Ctx, aid: u32, item: &inv::Item, success: bool) {
    let Some(sd) = unit::get(c, aid) else { return };
    let name = crate::pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
    let p = PACKET_ZC_GRADE_ENCHANT_BROADCAST_RESULT {
        packetType: PACKET_ZC_GRADE_ENCHANT_BROADCAST_RESULT::ID as i16,
        name,
        itemId: itemdb::client_nameid(item.nameid),
        grade: item.enchantgrade as i16,
        status: success as i8,
    };
    clif::send(c, &p.to_vec(), &sd, Target::AllClient);
}

/// clif.cpp clif_parse_enchantgrade_start (CZ_GRADE_ENCHANT_REQUEST 0x0b5b): pays the option's materials (and the
/// catalyst steps), the zeny, then rolls: success +1 grade and refine 0; failure breaks, downgrades or only fails.
pub fn clif_parse_enchantgrade_start(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !state(c, aid).enchantgrade_open {
        return;
    }
    let Some(p) = PACKET_CZ_GRADE_ENCHANT_REQUEST::read(&mut Reader::new(pkt)) else { return };
    let index = server_index(p.index as u16);
    if index as usize >= MAX_INVENTORY {
        return;
    }
    let idx = index as usize;
    let Some(r) = slot(c, aid, idx) else { return };
    let id = inv::data_of(&r.item);
    // Unsupported item type - no answer
    let Ok(levels) = levels_of(id) else { return };
    // Cannot upgrade this weapon or armor level - no answer
    let Some(grades) = levels else { return };
    // Cannot increase enchantgrade any further - no answer
    let Some(gl) = grades.iter().find(|g| g.grade == r.item.enchantgrade as u16) else { return };
    let mut total_chance: u16 = gl.chances.get(r.item.refine as usize).copied().unwrap_or(0);
    // No chance to increase the enchantgrade
    if total_chance == 0 {
        return;
    }
    // Unknown option id - no answer
    let Some(option) = gl.option(p.material_index as u16) else { return };
    // Not enough zeny
    let zeny = crate::pc::character_of(c, aid).map(|ch| ch.zeny).unwrap_or(0);
    if zeny < option.zeny {
        return;
    }
    // uint16 steps = min( p->blessing_amount, maximumSteps ) (cbasetypes min on int32)
    let steps = p.blessing_amount.min(gl.catalyst.maximum_steps as i32) as u16;
    // std::unordered_map<uint16, uint16> requiredItems: inventory index -> amount
    let mut required: Vec<(u16, u16)> = Vec::new();
    let amount_of = |i: i16| slot(c, aid, i as usize).map(|r| r.item.amount as i32).unwrap_or(0);
    if p.blessing_flag != 0 {
        if gl.catalyst.item == option.item {
            // If the catalysator item is the same as the option item build the sum of amounts
            let amount = (gl.catalyst.amount_per_step as i32 * steps as i32 + option.amount as i32) as u16;
            let i = inv::pc_search_inventory(c, aid, gl.catalyst.item);
            if i < 0 || amount_of(i) < amount as i32 {
                return;
            }
            required.push((i as u16, amount));
        } else {
            let amount = (gl.catalyst.amount_per_step as i32 * steps as i32) as u16;
            // Check catalysator item
            let i = inv::pc_search_inventory(c, aid, gl.catalyst.item);
            if i < 0 || amount_of(i) < amount as i32 {
                return;
            }
            required.push((i as u16, amount));
            // Check option item
            let i = inv::pc_search_inventory(c, aid, option.item);
            if i < 0 || amount_of(i) < option.amount as i32 {
                return;
            }
            set_or_push(&mut required, i as u16, option.amount);
        }
        total_chance = total_chance.wrapping_add((steps as i32 * gl.catalyst.chance_increase as i32) as u16);
    } else {
        // Check option item
        let i = inv::pc_search_inventory(c, aid, option.item);
        if i < 0 || amount_of(i) < option.amount as i32 {
            return;
        }
        required.push((i as u16, option.amount));
    }
    // All items should be there, start deleting (in the unordered_map's order)
    let keys: Vec<u16> = required.iter().map(|(k, _)| *k).collect();
    for k in umap_order(&keys) {
        let amount = required.iter().find(|(i, _)| *i == k).map(|(_, a)| *a).unwrap_or(0);
        if inv::pc_delitem(c, aid, k as i32, amount as i32, 0, 0, crate::map_log::LOG_TYPE_ENCHANTGRADE) != 0 {
            return;
        }
    }
    if inv::pc_payzeny(c, aid, option.zeny as i64, crate::map_log::LOG_TYPE_ENCHANTGRADE, 0) > 0 {
        return;
    }
    if c.rnd() % 10000 < total_chance as u32 {
        let Some(mut r) = slot(c, aid, idx) else { return };
        // Log removal of item
        crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_ENCHANTGRADE, -1, &r.item);
        // Increase enchantgrade; on successful enchantgrade increase the refine is reset
        r.item.enchantgrade = (r.item.enchantgrade as u16 + 1).min(MAX_ENCHANTGRADE) as u8;
        r.item.refine = 0;
        inv::inv_put(c, &r);
        // Log retrieving the item again -> with the new refine and enchantgrade
        crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_ENCHANTGRADE, 1, &r.item);
        clif_enchantgrade_result(c, aid, index, ENCHANTGRADE_UPGRADE_SUCCESS);
        // Check if it has to be announced
        if gl.announce_success {
            clif_enchantgrade_announce(c, aid, &r.item, true);
        }
    } else {
        let Some(mut r) = slot(c, aid, idx) else { return };
        // Check if it has to be announced (has to be done before deleting the item from inventory)
        if gl.announce_fail {
            clif_enchantgrade_announce(c, aid, &r.item, false);
        }
        if option.breaking_rate > 0 && c.rnd() % 10000 < option.breaking_rate as u32 {
            // Delete the item
            inv::pc_delitem(c, aid, idx as i32, 1, 0, 0, crate::map_log::LOG_TYPE_ENCHANTGRADE);
            clif_enchantgrade_result(c, aid, index, ENCHANTGRADE_UPGRADE_BREAK);
        } else if option.downgrade_amount > 0 {
            // Log removal of item
            crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_ENCHANTGRADE, -1, &r.item);
            // Decrease refine level
            r.item.refine = (r.item.refine as i32 - option.downgrade_amount as i32).clamp(0, MAX_REFINE) as u8;
            inv::inv_put(c, &r);
            // Log retrieving the item again -> with the new refine
            crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_ENCHANTGRADE, 1, &r.item);
            clif_enchantgrade_result(c, aid, index, ENCHANTGRADE_UPGRADE_DOWNGRADE);
        } else {
            // Only show failure, but dont do anything
            clif_enchantgrade_result(c, aid, index, ENCHANTGRADE_UPGRADE_FAILED);
        }
    }
}

/// `map[k] = v` on a vector that keeps first-insertion order of the keys.
fn set_or_push(v: &mut Vec<(u16, u16)>, k: u16, a: u16) {
    match v.iter_mut().find(|(i, _)| *i == k) {
        Some(e) => e.1 = a,
        None => v.push((k, a)),
    }
}

/// clif.cpp clif_parse_enchantgrade_close (CZ_GRADE_ENCHANT_CLOSE_UI 0x0b5c).
pub fn clif_parse_enchantgrade_close(c: &Ctx, aid: u32) {
    update(c, aid, |s| s.enchantgrade_open = false);
}
