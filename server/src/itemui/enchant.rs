//! The enchant window (item_enchant.yml): clif.cpp clif_enchantwindow_open, clif_enchantwindow_result,
//! clif_parse_enchant_basecheck, clif_parse_enchantwindow_general / _perfect / _upgrade / _reset / _close.
//! As in rAthena, an enchant changes the item's card slot without any item packet: the client only gets
//! ZC_RESPONSE_ENCHANT.

use crate::clif::{self, Target};
use crate::db::items::enchant::{self as enchant_db, ItemEnchant};
use crate::db::items::enchantgrade::MAX_ENCHANTGRADE;
use crate::inventory::{self as inv, Item, MAX_INVENTORY, MAX_SLOTS};
use crate::net::packets::*;
use crate::net::wire::Reader;
use crate::unit;
use crate::world::Ctx;

use super::*;

/// clif.hpp e_clif_messages MSI_ENCHANT_FAILED_OVER_WEIGHT, MSI_ENCHANT_SUCCESS, MSI_ENCHANT_FAILED.
pub const MSI_ENCHANT_FAILED_OVER_WEIGHT: u16 = 3837;
pub const MSI_ENCHANT_SUCCESS: i32 = 3857;
pub const MSI_ENCHANT_FAILED: i32 = 3858;

/// clif.cpp clif_enchantwindow_open: refused over 70% weight (MSI_ENCHANT_FAILED_OVER_WEIGHT), else ZC_UI_OPEN_V3
/// (OUT_UI_ENCHANT, the enchant group) and item_enchant_index = the group.
pub fn clif_enchantwindow_open(c: &Ctx, aid: u32, client_lua_index: u64) {
    let Some(sd) = unit::get(c, aid) else { return };
    // Hardcoded clientside check
    let w = inv::st(c, aid).map(|s| inv::pc_getpercentweight(&s, 0)).unwrap_or(0);
    if w >= 70 {
        crate::clif_items::msg_color(c, &sd, MSI_ENCHANT_FAILED_OVER_WEIGHT, COLOR_RED);
        update(c, aid, |s| s.item_enchant_index = 0);
        return;
    }
    let p = PACKET_ZC_UI_OPEN_V3 { packetType: PACKET_ZC_UI_OPEN_V3::ID as i16, r#type: OUT_UI_ENCHANT, data: client_lua_index };
    clif::send(c, &p.to_vec(), &sd, Target::SelfOnly);
    update(c, aid, |s| s.item_enchant_index = client_lua_index);
}

/// clif.cpp clif_enchantwindow_result: ZC_RESPONSE_ENCHANT (the window closes: item_enchant_index = 0).
pub fn clif_enchantwindow_result(c: &Ctx, aid: u32, success: bool, enchant: u32) {
    let p = PACKET_ZC_RESPONSE_ENCHANT {
        PacketType: PACKET_ZC_RESPONSE_ENCHANT::ID as i16,
        msgId: if success { MSI_ENCHANT_SUCCESS } else { MSI_ENCHANT_FAILED },
        ITID: enchant,
    };
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, &p.to_vec(), &sd, Target::SelfOnly);
    }
    update(c, aid, |s| s.item_enchant_index = 0);
}

/// clif.cpp clif_parse_enchant_basecheck.
fn basecheck(it: &Item, enchant: &ItemEnchant) -> bool {
    if it.equip != 0 || it.equip_switch != 0 || it.attribute != 0 {
        return false;
    }
    if !enchant.target_item_ids.contains(&it.nameid) {
        return false;
    }
    if (it.refine as u16) < enchant.minimum_refine {
        return false;
    }
    if (it.enchantgrade as u16) < enchant.minimum_enchantgrade {
        return false;
    }
    if !enchant.allow_random_options && it.options().iter().any(|o| o.id != 0) {
        return false;
    }
    true
}

/// The common start of the four requests: the group must be the open one, the slot filled; returns the item, its
/// server index and the enchant group.
fn selected(c: &Ctx, aid: u32, group: i64, client_index: i16) -> Option<(crate::inventory::InventoryItem, usize, &'static ItemEnchant)> {
    if state(c, aid).item_enchant_index != group as u64 {
        return None;
    }
    let index = server_index(client_index as u16);
    if index as usize >= MAX_INVENTORY {
        return None;
    }
    let r = slot(c, aid, index as usize)?;
    let enchant = enchant_db::item_enchant(group as u64)?;
    Some((r, index as usize, enchant))
}

/// The first free slot in the group's Order.
fn next_slot(it: &Item, enchant: &ItemEnchant) -> u16 {
    for &s in &enchant.order {
        if it.card(s as usize) == 0 {
            return s;
        }
    }
    MAX_SLOTS as u16
}

/// Checks (pc_search_inventory, amount) every material and returns the std::unordered_map<uint16, uint16>
/// index -> amount as (key, amount) in insertion order; None when one is missing.
fn materials_of(c: &Ctx, aid: u32, list: &[(u32, u16)]) -> Option<Vec<(u16, u16)>> {
    let mut m: Vec<(u16, u16)> = Vec::new();
    for &(nameid, amount) in list {
        let idx = inv::pc_search_inventory(c, aid, nameid);
        if idx < 0 {
            return None;
        }
        let have = slot(c, aid, idx as usize).map(|r| r.item.amount).unwrap_or(0);
        if (have as i32) < amount as i32 {
            return None;
        }
        match m.iter_mut().find(|(i, _)| *i == idx as u16) {
            Some(e) => e.1 = amount,
            None => m.push((idx as u16, amount)),
        }
    }
    Some(m)
}

/// Deletes the materials in the unordered_map's iteration order; false when a deletion fails.
fn delete_materials(c: &Ctx, aid: u32, m: &[(u16, u16)]) -> bool {
    let keys: Vec<u16> = m.iter().map(|(k, _)| *k).collect();
    for k in umap_order(&keys) {
        let amount = m.iter().find(|(i, _)| *i == k).map(|(_, a)| *a).unwrap_or(0);
        if inv::pc_delitem(c, aid, k as i32, amount as i32, 0, 0, crate::map_log::LOG_TYPE_ENCHANT) != 0 {
            return false;
        }
    }
    true
}

fn zeny(c: &Ctx, aid: u32) -> u32 {
    crate::pc::character_of(c, aid).map(|ch| ch.zeny).unwrap_or(0)
}

/// clif.cpp clif_parse_enchantwindow_general (CZ_REQUEST_RANDOM_ENCHANT 0x0b9b): a random enchant of the item's
/// grade in the next free enchant slot.
pub fn clif_parse_enchantwindow_general(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_REQUEST_RANDOM_ENCHANT::read(&mut Reader::new(pkt)) else { return };
    let Some((r, index, enchant)) = selected(c, aid, p.enchant_group, p.index) else { return };
    if !basecheck(&r.item, enchant) {
        return;
    }
    let slot_no = next_slot(&r.item, enchant);
    if slot_no as usize == MAX_SLOTS {
        return;
    }
    if slot_no < inv::data_of(&r.item).slots {
        return;
    }
    let Some(enchant_slot) = enchant.slot(slot_no) else { return };
    let Some(for_grade) = enchant_slot.normal.enchants.iter().find(|e| e.enchantgrade == r.item.enchantgrade as u16) else {
        if let Some(sd) = unit::get(c, aid) {
            // Enchanting is not possible for your item's enchant grade.
            clif::messagecolor_target(c, &sd, COLOR_RED, &crate::net::wire::latin1(crate::data::msg_txt(829)), false, Target::SelfOnly, None);
        }
        clif_enchantwindow_result(c, aid, false, 0);
        return;
    };
    if zeny(c, aid) < enchant_slot.normal.zeny {
        return;
    }
    let Some(materials) = materials_of(c, aid, &enchant_slot.normal.materials) else { return };
    if inv::pc_payzeny(c, aid, enchant_slot.normal.zeny as i64, crate::map_log::LOG_TYPE_ENCHANT, 0) != 0 {
        return;
    }
    if !delete_materials(c, aid, &materials) {
        return;
    }
    let mut chance: u32 = enchant_slot.normal.chance;
    for i in 0..=MAX_ENCHANTGRADE {
        chance = chance.wrapping_add(enchant_slot.normal.enchantgrade_chance_increase.iter().find(|(g, _)| *g == i).map(|(_, v)| *v).unwrap_or(0));
    }
    if chance < 100000 && c.rnd_value(0, 100000) as u32 > chance {
        clif_enchantwindow_result(c, aid, false, 0);
        return;
    }
    let Some(mut r) = slot(c, aid, index) else { return };
    // Log removal of item
    crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_ENCHANT, -1, &r.item);
    let maximum = 3 * enchant_slot.normal.enchants.len();
    let list = &for_grade.enchants;
    let mut chosen: Option<u32> = None;
    for _ in 0..maximum {
        // util::umap_random: a random position of the map's iteration
        let e = &list[c.rnd_value(0, list.len() as i32 - 1) as usize];
        if (c.rnd_value(0, 10000) as u32) < e.chance {
            chosen = Some(e.item_id);
            break;
        }
    }
    let item_id = match chosen {
        Some(i) => i,
        None => list[c.rnd_value(0, list.len() as i32 - 1) as usize].item_id,
    };
    r.item.set_card(slot_no as usize, item_id);
    inv::inv_put(c, &r);
    // Log retrieving the item again -> with the new enchant
    crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_ENCHANT, 1, &r.item);
    clif_enchantwindow_result(c, aid, true, item_id);
}

/// clif.cpp clif_parse_enchantwindow_perfect (CZ_REQUEST_PERFECT_ENCHANT 0x0b9c): the chosen enchant, for its price.
pub fn clif_parse_enchantwindow_perfect(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_REQUEST_PERFECT_ENCHANT::read(&mut Reader::new(pkt)) else { return };
    let Some((r, index, enchant)) = selected(c, aid, p.enchant_group, p.index) else { return };
    if !basecheck(&r.item, enchant) {
        return;
    }
    let slot_no = next_slot(&r.item, enchant);
    if slot_no as usize == MAX_SLOTS {
        return;
    }
    if slot_no < inv::data_of(&r.item).slots {
        return;
    }
    let Some(enchant_slot) = enchant.slot(slot_no) else { return };
    let Some(perfect) = enchant_slot.perfect.iter().find(|e| e.item_id == p.ITID) else { return };
    if zeny(c, aid) < perfect.zeny {
        return;
    }
    let Some(materials) = materials_of(c, aid, &perfect.materials) else { return };
    if inv::pc_payzeny(c, aid, perfect.zeny as i64, crate::map_log::LOG_TYPE_ENCHANT, 0) != 0 {
        return;
    }
    if !delete_materials(c, aid, &materials) {
        return;
    }
    let Some(mut r) = slot(c, aid, index) else { return };
    // Log removal of item
    crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_ENCHANT, -1, &r.item);
    r.item.set_card(slot_no as usize, perfect.item_id);
    inv::inv_put(c, &r);
    // Log retrieving the item again -> with the new enchant
    crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_ENCHANT, 1, &r.item);
    clif_enchantwindow_result(c, aid, true, perfect.item_id);
}

/// clif.cpp clif_parse_enchantwindow_upgrade (CZ_REQUEST_UPGRADE_ENCHANT 0x0b9d): replaces the enchant in `slot` by
/// its upgrade.
pub fn clif_parse_enchantwindow_upgrade(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_REQUEST_UPGRADE_ENCHANT::read(&mut Reader::new(pkt)) else { return };
    let Some((r, index, enchant)) = selected(c, aid, p.enchant_group, p.index) else { return };
    if !basecheck(&r.item, enchant) {
        return;
    }
    let slot_no = p.slot as u16;
    if slot_no as usize >= MAX_SLOTS {
        return;
    }
    if slot_no < inv::data_of(&r.item).slots {
        return;
    }
    let current = r.item.card(slot_no as usize);
    if current == 0 {
        return;
    }
    let Some(enchant_slot) = enchant.slot(slot_no) else { return };
    let Some(upgrade) = enchant_slot.upgrade.iter().find(|e| e.enchant_item_id == current) else { return };
    if zeny(c, aid) < upgrade.zeny {
        return;
    }
    let Some(materials) = materials_of(c, aid, &upgrade.materials) else { return };
    if inv::pc_payzeny(c, aid, upgrade.zeny as i64, crate::map_log::LOG_TYPE_ENCHANT, 0) != 0 {
        return;
    }
    if !delete_materials(c, aid, &materials) {
        return;
    }
    let Some(mut r) = slot(c, aid, index) else { return };
    // Log removal of item
    crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_ENCHANT, -1, &r.item);
    r.item.set_card(slot_no as usize, upgrade.upgrade_item_id);
    inv::inv_put(c, &r);
    // Log retrieving the item again -> with the new enchant
    crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_ENCHANT, 1, &r.item);
    clif_enchantwindow_result(c, aid, true, upgrade.upgrade_item_id);
}

/// clif.cpp clif_parse_enchantwindow_reset (CZ_REQUEST_RESET_ENCHANT 0x0b9e): removes every enchant (the slots after
/// the item's own card slots). A group without a reset chance takes the price and answers nothing (as rAthena).
pub fn clif_parse_enchantwindow_reset(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_REQUEST_RESET_ENCHANT::read(&mut Reader::new(pkt)) else { return };
    if state(c, aid).item_enchant_index != p.enchant_group as u64 {
        return;
    }
    let index = server_index(p.index as u16);
    if index as usize >= MAX_INVENTORY {
        return;
    }
    let Some(r) = slot(c, aid, index as usize) else { return };
    let it = &r.item;
    if it.equip != 0 || it.equip_switch != 0 || it.attribute != 0 {
        return;
    }
    let Some(enchant) = enchant_db::item_enchant(p.enchant_group as u64) else { return };
    if !enchant.target_item_ids.contains(&it.nameid) {
        return;
    }
    if (it.refine as u16) < enchant.minimum_refine || (it.enchantgrade as u16) < enchant.minimum_enchantgrade {
        return;
    }
    let slots = inv::data_of(it).slots as usize;
    if !(slots..MAX_SLOTS).any(|i| it.card(i) != 0) {
        return;
    }
    if zeny(c, aid) < enchant.reset.zeny {
        return;
    }
    let Some(materials) = materials_of(c, aid, &enchant.reset.materials) else { return };
    if inv::pc_payzeny(c, aid, enchant.reset.zeny as i64, crate::map_log::LOG_TYPE_ENCHANT, 0) != 0 {
        return;
    }
    if !delete_materials(c, aid, &materials) {
        return;
    }
    let chance = enchant.reset.chance;
    if chance == 0 {
        return;
    }
    if chance < 100000 && c.rnd_value(0, 100000) as u32 > chance {
        clif_enchantwindow_result(c, aid, false, 0);
        return;
    }
    let Some(mut r) = slot(c, aid, index as usize) else { return };
    // Log removal of item
    crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_ENCHANT, -1, &r.item);
    for i in slots..MAX_SLOTS {
        r.item.set_card(i, 0);
    }
    inv::inv_put(c, &r);
    // Log retrieving the item again -> with the new enchant
    crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_ENCHANT, 1, &r.item);
    clif_enchantwindow_result(c, aid, true, 0);
}

/// clif.cpp clif_parse_enchantwindow_close (CZ_CLOSE_UI_ENCHANT 0x0ba0).
pub fn clif_parse_enchantwindow_close(c: &Ctx, aid: u32) {
    update(c, aid, |s| s.item_enchant_index = 0);
}
