//! Item reform (item_reform.yml): clif.cpp clif_item_reform_open, clif_parse_item_reform_close,
//! clif_item_reform_result, clif_parse_item_reform_start. The reformed item keeps its slot; as in rAthena the weight
//! is not recalculated for the new item id.

use crate::clif::{self, Target};
use crate::db::items::randomopt;
use crate::db::items::reform as reform_db;
use crate::inventory::{self as inv, ItemOption, MAX_INVENTORY, MAX_ITEM_RDM_OPT, MAX_REFINE, MAX_SLOTS};
use crate::net::packets::*;
use crate::net::wire::Reader;
use crate::unit;
use crate::world::Ctx;

use super::*;

/// clif.cpp clif_item_reform_open: ZC_OPEN_REFORM_UI (the item id as is), then item_reform = item.
pub fn clif_item_reform_open(c: &Ctx, aid: u32, item: u32) {
    let p = PACKET_ZC_OPEN_REFORM_UI { PacketType: PACKET_ZC_OPEN_REFORM_UI::ID as i16, ITID: item as i32 };
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, &p.to_vec(), &sd, Target::SelfOnly);
    }
    update(c, aid, |s| s.item_reform = item);
}

/// clif.cpp clif_parse_item_reform_close (CZ_CLOSE_REFORM_UI 0x0b90).
pub fn clif_parse_item_reform_close(c: &Ctx, aid: u32) {
    update(c, aid, |s| s.item_reform = 0);
}

/// clif.cpp clif_item_reform_result: ZC_ITEM_REFORM_ACK; the client closes the window on success (item_reform = 0).
pub fn clif_item_reform_result(c: &Ctx, aid: u32, index: u16, result: u8) {
    let p = PACKET_ZC_ITEM_REFORM_ACK {
        PacketType: PACKET_ZC_ITEM_REFORM_ACK::ID as i16,
        index: crate::clif_items::client_index(index as usize) as i16,
        result: result as i8,
    };
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, &p.to_vec(), &sd, Target::SelfOnly);
    }
    if result == 0 {
        update(c, aid, |s| s.item_reform = 0);
    }
}

/// s_random_opt_group::apply on an item (the module's randomopt apply with the reducer's rnd_value).
pub fn apply_random_option_group(c: &Ctx, group: u16, it: &mut inv::Item) {
    let Some(g) = randomopt::random_option_group(group) else { return };
    let opts = g.apply(&mut |a, b| c.rnd_value(a, b));
    let mut v = vec![ItemOption::default(); MAX_ITEM_RDM_OPT];
    for (i, o) in opts.iter().enumerate() {
        v[i] = ItemOption { id: o.id, value: o.value, param: o.param as u8 };
    }
    it.option = v;
}

/// clif.cpp clif_parse_item_reform_start (CZ_ITEM_REFORM 0x0b91): turns the selected base item into the ticket's
/// result item (materials, the ticket when the window came from it, random options, refine change, slots, grade).
pub fn clif_parse_item_reform_start(c: &Ctx, aid: u32, pkt: &[u8]) {
    let st = state(c, aid);
    // Not opened
    if st.item_reform == 0 {
        return;
    }
    let Some(p) = PACKET_CZ_ITEM_REFORM::read(&mut Reader::new(pkt)) else { return };
    // Item mismatch
    if p.ITID as u32 != st.item_reform {
        return;
    }
    let index = server_index(p.index as u16);
    if index as usize >= MAX_INVENTORY {
        return;
    }
    let idx = index as usize;
    let Some(r) = slot(c, aid, idx) else { return };
    let Some(reform) = reform_db::item_reform(st.item_reform) else { return };
    let Some(base) = reform.base_item(r.item.nameid) else { return };
    let it = &r.item;
    // If target item is not identified / equipped
    if it.identify == 0 || it.equip != 0 {
        return;
    }
    // Check minimum / maximum refine requirement
    if (it.refine as u16) < base.minimum_refine || it.refine as u16 > base.maximum_refine {
        return;
    }
    // If no cards are allowed
    if !base.cards_allowed && (0..MAX_SLOTS).any(|i| it.card(i) != 0) {
        return;
    }
    // If random options are required
    if base.required_random_options > 0 {
        let n = (0..MAX_ITEM_RDM_OPT).rev().find(|&i| it.opt(i).id != 0).map(|i| i as i32 + 1).unwrap_or(0);
        if n < base.required_random_options as i32 {
            return;
        }
    }
    // std::unordered_map<uint16, uint16> materials: check that they all exist
    let mut materials: Vec<(u16, u16)> = Vec::new();
    for &(nameid, amount) in &base.materials {
        let mi = inv::pc_search_inventory(c, aid, nameid);
        if mi < 0 {
            return;
        }
        let have = slot(c, aid, mi as usize).map(|r| r.item.amount as i32).unwrap_or(0);
        if have < amount as i32 {
            return;
        }
        match materials.iter_mut().find(|(i, _)| *i == mi as u16) {
            Some(e) => e.1 = amount,
            None => materials.push((mi as u16, amount)),
        }
    }
    // Remove the material
    let keys: Vec<u16> = materials.iter().map(|(k, _)| *k).collect();
    for k in umap_order(&keys) {
        let amount = materials.iter().find(|(i, _)| *i == k).map(|(_, a)| *a).unwrap_or(0);
        if inv::pc_delitem(c, aid, k as i32, amount as i32, 0, 0, crate::map_log::LOG_TYPE_REFORM) != 0 {
            return;
        }
    }
    // If triggered from item
    let s = inv::st(c, aid);
    if let Some(s) = s {
        if s.itemid == st.item_reform && inv::pc_delitem(c, aid, s.itemindex as i32, 1, 0, 0, crate::map_log::LOG_TYPE_REFORM) != 0 {
            return;
        }
    }
    let Some(mut r) = slot(c, aid, idx) else { return };
    // Log removal of item
    crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_REFORM, -1, &r.item);
    // Visually remove it from the client
    crate::clif_items::delitem(c, aid, idx as i32, 1, 0);
    // Apply the random options
    if let Some(g) = base.random_option_group {
        apply_random_option_group(c, g, &mut r.item);
    }
    // Change the refine rate if needed
    if base.refine_change != 0 {
        r.item.refine = (r.item.refine as i32 + base.refine_change as i32).clamp(0, MAX_REFINE) as u8;
    }
    // Remove all cards and socket enchants
    if base.clear_slots {
        for i in 0..MAX_SLOTS {
            r.item.set_card(i, 0);
        }
    }
    // Remove the current enchantgrade
    if base.remove_enchantgrade {
        r.item.enchantgrade = 0;
    }
    // Finally change the item id (inventory_data[index] follows the new id)
    r.item.nameid = base.result_item_id;
    inv::inv_put(c, &r);
    // Log retrieving the item again -> with the new options
    crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_REFORM, 1, &r.item);
    // Make it visible for the client again
    crate::clif_items::additem(c, aid, idx as i32, 1, 0);
    clif_item_reform_result(c, aid, index, 0);
}
