//! Item merge (stackable items split by unique ids): clif.cpp clif_merge_item_ack, clif_merge_item_has_pair,
//! clif_merge_item_check, clif_merge_item_open, clif_parse_merge_item_req, clif_parse_merge_item_cancel; the
//! `mergeitem2` script command's merge is in buildins.rs.

use crate::clif::{self, Target};
use crate::db::items::{self as itemdb, ItemData, IT_CASH};
use crate::inventory::{self as inv, Item, MAX_AMOUNT, MAX_INVENTORY};
use crate::net::packets::*;
use crate::net::wire::Reader;
use crate::unit;
use crate::world::Ctx;

use super::*;

/// clif.hpp e_clif_messages MSI_NOT_EXIST_MERGE_ITEM (PACKETVER 20211103).
pub const MSI_NOT_EXIST_MERGE_ITEM: u16 = 2183;
/// clif.hpp MERGE_ITEM_ACK.
pub const MERGE_ITEM_SUCCESS: u8 = 0;
pub const MERGE_ITEM_FAILED_NOT_MERGE: u8 = 1;
pub const MERGE_ITEM_FAILED_MAX_COUNT: u8 = 2;

fn send_self(c: &Ctx, aid: u32, b: &[u8]) {
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, b, &sd, Target::SelfOnly);
    }
}

fn msg(c: &Ctx, aid: u32, id: u16) {
    if let Some(sd) = unit::get(c, aid) {
        crate::clif_items::msg(c, &sd, id);
    }
}

/// clif.cpp clif_merge_item_ack: ZC_ACK_MERGE_ITEM (the index is a client index only on success).
pub fn clif_merge_item_ack(c: &Ctx, aid: u32, kind: u8, index: u16, count: u16) {
    let p = PACKET_ZC_ACK_MERGE_ITEM {
        packetType: PACKET_ZC_ACK_MERGE_ITEM::ID as i16,
        index: if kind == MERGE_ITEM_SUCCESS { crate::clif_items::client_index(index as usize) as i16 } else { index as i16 },
        amount: count as i16,
        reason: kind,
    };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_merge_item_has_pair: another slot with the same nameid and bound, not byte-identical.
fn has_pair(list: &[inv::InventoryItem], it: &Item) -> bool {
    list.iter().any(|r| it.nameid == r.item.nameid && it.bound == r.item.bound && r.item != *it)
}

/// clif.cpp clif_merge_item_check: a stackable non-cash item without special cards, with a unique id.
fn merge_check(id: &ItemData, it: &Item) -> bool {
    id.r#type != IT_CASH && itemdb::itemdb_isstackable2(id) && !itemdb::itemdb_isspecial(it.card0) && it.unique_id != 0
}

/// clif.cpp clif_merge_item_open: ZC_MERGE_ITEM_OPEN with every mergeable slot that has a pair, or
/// MSI_NOT_EXIST_MERGE_ITEM when fewer than two.
pub fn clif_merge_item_open(c: &Ctx, aid: u32) {
    let list = inv::inv_list(c, aid);
    let mut p = PACKET_ZC_MERGE_ITEM_OPEN { packetType: PACKET_ZC_MERGE_ITEM_OPEN::ID as i16, ..Default::default() };
    for r in &list {
        if !merge_check(inv::data_of(&r.item), &r.item) {
            continue;
        }
        if has_pair(&list, &r.item) {
            p.items.push(PACKET_ZC_MERGE_ITEM_OPEN_sub { index: crate::clif_items::client_index(r.idx as usize) as i16 });
        }
    }
    // No item need to be merged
    if p.items.len() < 2 {
        msg(c, aid, MSI_NOT_EXIST_MERGE_ITEM);
        return;
    }
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_parse_merge_item_req (CZ_REQ_MERGE_ITEM 0x096e): the other slots' amounts join the first slot;
/// their slots are emptied (clif_delitem each, in the unordered_set's order). The weight does not change.
pub fn clif_parse_merge_item_req(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_REQ_MERGE_ITEM::read(&mut Reader::new(pkt)) else { return };
    let count = (p.packetLength as i32 - 4) / 2;
    // No item need to be merged
    if count < 2 {
        msg(c, aid, MSI_NOT_EXIST_MERGE_ITEM);
        return;
    }
    let idx_main = server_index(p.indices[0]);
    if idx_main as usize >= MAX_INVENTORY {
        return;
    }
    let Some(main) = slot(c, aid, idx_main as usize) else {
        // clif_merge_item_check( nullptr, … ) is false
        msg(c, aid, MSI_NOT_EXIST_MERGE_ITEM);
        return;
    };
    let main_id = inv::data_of(&main.item);
    if !merge_check(main_id, &main.item) {
        msg(c, aid, MSI_NOT_EXIST_MERGE_ITEM);
        return;
    }
    // Ensure each index only comes once (std::unordered_set<uint16>)
    let mut indices: Vec<u16> = Vec::new();
    for i in 1..count as usize {
        let idx = server_index(p.indices[i]);
        if idx as usize >= MAX_INVENTORY {
            return;
        }
        let Some(r) = slot(c, aid, idx as usize) else { return };
        let id = inv::data_of(&r.item);
        // Check if it is the same item
        if id.nameid != main_id.nameid {
            return;
        }
        if !merge_check(id, &r.item) {
            msg(c, aid, MSI_NOT_EXIST_MERGE_ITEM);
            return;
        }
        if !indices.contains(&idx) {
            indices.push(idx);
        }
    }
    if indices.is_empty() {
        msg(c, aid, MSI_NOT_EXIST_MERGE_ITEM);
        return;
    }
    let mut total_amount: u32 = main.item.amount as u32;
    for &idx in &indices {
        total_amount = total_amount.wrapping_add(slot(c, aid, idx as usize).map(|r| r.item.amount as u32).unwrap_or(0));
    }
    let mut stack = main_id.stack.amount;
    if stack == 0 {
        stack = MAX_AMOUNT as u16;
    }
    if total_amount >= stack as u32 {
        clif_merge_item_ack(c, aid, MERGE_ITEM_FAILED_MAX_COUNT, 0, 0);
        return;
    }
    // Merrrrge!!!!
    for idx in umap_order(&indices) {
        let Some(r) = slot(c, aid, idx as usize) else { continue };
        let amount = r.item.amount as u16;
        crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_MERGE_ITEM, -(amount as i32), &r.item);
        // memset the slot, inventory_data[idx] = nullptr
        c.db().inventory().id().delete(r.id);
        crate::clif_items::delitem(c, aid, idx as i32, amount as i32, 0);
    }
    if let Some(mut m) = slot(c, aid, idx_main as usize) {
        m.item.amount = total_amount as i16;
        inv::inv_put(c, &m);
    }
    clif_merge_item_ack(c, aid, MERGE_ITEM_SUCCESS, idx_main, total_amount as u16);
}

use crate::inventory::inventory as _;

/// clif.cpp clif_parse_merge_item_cancel (CZ_CANCEL_MERGE_ITEM 0x0974): nothing to do.
pub fn clif_parse_merge_item_cancel(_c: &Ctx, _aid: u32) {}
