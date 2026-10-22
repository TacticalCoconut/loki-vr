//! The cart (pc.cpp pc_cart_additem, pc_cart_delitem, pc_putitemtocart, pc_getitemfromcart, pc_cartitem_amount,
//! pc_setcart; clif.cpp clif_parse_PutItemToCart / GetItemFromCart / ChangeCart / SelectCart, clif_SelectCart) and the
//! `cart_inventory` table (rAthena `cart_inventory`). status_calc_cart_weight is the status system's (status/calc.rs).
//!
//! NEW_CARTS (PACKETVER >= 20120201): having a cart is the status change SC_PUSH_CART (val1 = cart type), saved with
//! the character's other status changes (status.rs on_quit_save / chrif_load_scdata).

use spacetimedb::{table, Table};

use crate::clif_items as ci;
use crate::db::items as itemdb;
use crate::inventory::*;
use crate::map_log::{LogPickType, LOG_TYPE_NONE, LOG_TYPE_OTHER};
use crate::pc;
use crate::unit;
use crate::world::Ctx;

/// mmo.hpp MAX_CARTS (PACKETVER >= 20191106).
pub const MAX_CARTS: i32 = 13;

/// rAthena `cart_inventory` table + the session index.
#[table(accessor = cart_inventory, index(accessor = by_char_idx, btree(columns = [char_id, idx])))]
#[derive(Clone, Debug)]
pub struct CartItem {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    #[index(btree)]
    pub char_id: u32,
    /// sd->cart.u.items_cart index (0 .. MAX_CART-1).
    pub idx: u16,
    pub save_id: u64,
    pub item: Item,
}

fn cid(c: &Ctx, aid: u32) -> Option<u32> {
    pc::get(c, aid).map(|p| p.char_id)
}

/// sd->cart.u.items_cart[idx].
pub fn cart_get(c: &Ctx, aid: u32, idx: usize) -> Option<CartItem> {
    if idx >= MAX_CART {
        return None;
    }
    let cid = cid(c, aid)?;
    c.db().cart_inventory().by_char_idx().filter((cid, idx as u16)).next()
}

pub fn cart_list(c: &Ctx, aid: u32) -> Vec<CartItem> {
    let Some(cid) = cid(c, aid) else { return Vec::new() };
    let mut v: Vec<CartItem> = c.db().cart_inventory().char_id().filter(cid).collect();
    v.sort_by_key(|r| r.idx);
    v
}

fn cart_put(c: &Ctx, r: &CartItem) {
    c.db().cart_inventory().id().update(r.clone());
}

/// intif_parse_StorageReceived(TABLE_CART): char_memitemdata_from_sql order, pc_check_available_item(ITMCHK_CART).
pub fn cart_received(c: &Ctx, aid: u32) {
    let Some(cid) = cid(c, aid) else { return };
    let mut rows: Vec<CartItem> = c.db().cart_inventory().char_id().filter(cid).collect();
    rows.sort_by_key(|r| (r.save_id == 0, r.save_id, r.idx, r.id));
    for (i, mut r) in rows.into_iter().enumerate() {
        if r.idx != i as u16 {
            r.idx = i as u16;
            c.db().cart_inventory().id().update(r);
        }
    }
    // the map server's copy of the cart now holds these SQL ids (sd->cart.u.items_cart[i].id, vending.rs)
    crate::vending::cart_ids_loaded(c, aid);
    pc_check_available_item(c, aid, ITMCHK_CART);
}

/// char_memitemdata_to_sql(TABLE_CART).
pub fn cart_tosql(c: &Ctx, char_id: u32) {
    let mut rows: Vec<CartItem> = c.db().cart_inventory().char_id().filter(char_id).collect();
    rows.sort_by_key(|r| r.idx);
    let items: Vec<(u64, u64, Item)> = rows.iter().map(|r| (r.id, r.save_id, r.item.clone())).collect();
    let assigned = memitemdata_to_sql(c, &items);
    for r in rows {
        if let Some(&sid) = assigned.get(&r.id) {
            if r.save_id != sid {
                c.db().cart_inventory().id().update(CartItem { save_id: sid, ..r });
            }
        }
    }
}

/// pc_check_available_item for ITMCHK_CART.
pub fn check_available(c: &Ctx, aid: u32) {
    let Some(sd) = unit::get(c, aid) else { return };
    for r in cart_list(c, aid) {
        let nameid = r.item.nameid;
        if !itemdb::itemdb_available(nameid) {
            crate::clif::displaymessage(c, &sd, &crate::data::format_msg(crate::data::msg_txt(710), &[&nameid]));
            log::warn!("Removed invalid/disabled item (ID: {nameid}, amount: {}) from cart (char_id: {}).", r.item.amount, r.char_id);
            pc_cart_delitem(c, aid, r.idx as i32, r.item.amount as i32, 0, LOG_TYPE_OTHER);
            continue;
        }
        if r.item.unique_id == 0 && !itemdb::itemdb_isstackable(nameid) {
            let uid = pc_generate_unique_id(c, aid);
            if let Some(mut r2) = cart_get(c, aid, r.idx as usize) {
                r2.item.unique_id = uid;
                cart_put(c, &r2);
            }
        }
    }
}

// ---------------------------------------------------------------------------------------------- add / delete

/// pc.cpp pc_cart_additem. `log_type`: the item log's type (log.rs log_pick_pc).
pub fn pc_cart_additem(c: &Ctx, aid: u32, item: &Item, amount: i32, log_type: LogPickType) -> u8 {
    if item.nameid == 0 || amount <= 0 {
        return ADDITEM_INVALID;
    }
    if itemdb::itemdb_ishatched_egg(item.card0, item.attribute) {
        return ADDITEM_INVALID;
    }
    let data = itemdb::itemdb_search(item.nameid);
    if data.stack.cart && amount > data.stack.amount as i32 {
        return ADDITEM_STACKLIMIT;
    }
    let gmlv = pc::group_level(c, aid);
    if !itemdb::itemdb_isrestricted(item.nameid, &item.cards(), gmlv, 0, itemdb::itemdb_cancartstore_sub) || (item.bound > BOUND_ACCOUNT && !pc_can_give_bounded_items(c, aid)) {
        if let Some(sd) = unit::get(c, aid) {
            crate::clif::displaymessage(c, &sd, crate::data::msg_txt(264));
        }
        return ADDITEM_INVALID;
    }
    let Some(s) = st(c, aid) else { return ADDITEM_INVALID };
    let w = data.weight.wrapping_mul(amount as u32);
    if w as u64 + s.cart_weight as u64 > s.cart_weight_max as u64 {
        return ADDITEM_OVERWEIGHT;
    }
    let list = cart_list(c, aid);
    let mut found: Option<CartItem> = None;
    if data.is_stackable() && item.expire_time == 0 {
        found = list.iter().find(|r| r.item.nameid == item.nameid && r.item.bound == item.bound && r.item.unique_id == item.unique_id && r.item.cards() == item.cards()).cloned();
    }
    let i;
    if let Some(mut r) = found {
        // item already in cart, stack it
        if amount > MAX_AMOUNT - r.item.amount as i32 || (data.stack.cart && amount > data.stack.amount as i32 - r.item.amount as i32) {
            return ADDITEM_OVERAMOUNT; // no slot
        }
        r.item.amount += amount as i16;
        r.item.favorite = 0;
        r.item.equip_switch = 0;
        i = r.idx as usize;
        cart_put(c, &r);
        ci::cart_additem(c, aid, i as i32, amount);
    } else {
        // item not stackable or not present, add it
        let used: std::collections::HashSet<u16> = list.iter().map(|r| r.idx).collect();
        let Some(k) = (0..MAX_CART).find(|k| !used.contains(&(*k as u16))) else { return ADDITEM_OVERAMOUNT };
        let mut it = item.clone();
        it.amount = amount as i16;
        it.favorite = 0; // clear
        it.equip_switch = 0;
        let char_id = cid(c, aid).unwrap_or(0);
        c.db().cart_inventory().insert(CartItem { id: 0, char_id, idx: k as u16, save_id: 0, item: it });
        if let Some(mut s2) = st(c, aid) {
            s2.cart_num += 1;
            put_st(c, &s2);
        }
        i = k;
        ci::cart_additem(c, aid, i as i32, amount);
    }
    if let Some(r) = cart_get(c, aid, i) {
        crate::map_log::log_pick_pc(c, aid, log_type, amount, &r.item);
    }
    if let Some(mut s2) = st(c, aid) {
        s2.cart_weight = s2.cart_weight.wrapping_add(w);
        put_st(c, &s2);
    }
    updatestatus(c, aid, SP_CARTINFO);
    ADDITEM_SUCCESS
}

/// pc.cpp pc_cart_delitem (type != 0: no packets). `log_type`: the item log's type (log.rs log_pick_pc).
pub fn pc_cart_delitem(c: &Ctx, aid: u32, n: i32, amount: i32, kind: i32, log_type: LogPickType) {
    if n < 0 {
        return;
    }
    let Some(mut r) = cart_get(c, aid, n as usize) else { return };
    if r.item.nameid == 0 || (r.item.amount as i32) < amount {
        return;
    }
    crate::map_log::log_pick_pc(c, aid, log_type, -amount, &r.item);
    r.item.amount -= amount as i16;
    if let Some(mut s) = st(c, aid) {
        s.cart_weight = s.cart_weight.wrapping_sub(itemdb::itemdb_weight(r.item.nameid).wrapping_mul(amount as u32));
        if r.item.amount <= 0 {
            s.cart_num = s.cart_num.saturating_sub(1);
        }
        put_st(c, &s);
    }
    if r.item.amount <= 0 {
        c.db().cart_inventory().id().delete(r.id);
    } else {
        cart_put(c, &r);
    }
    if kind == 0 {
        ci::cart_delitem(c, aid, n, amount);
        updatestatus(c, aid, SP_CARTINFO);
    }
}

/// pc.cpp pc_putitemtocart.
pub fn pc_putitemtocart(c: &Ctx, aid: u32, idx: i32, amount: i32) {
    if idx < 0 || idx as usize >= MAX_INVENTORY {
        return;
    }
    let Some(r) = inv_get(c, aid, idx as usize) else { return };
    let Some(p) = pc::get(c, aid) else { return };
    if r.item.nameid == 0 || amount < 1 || (r.item.amount as i32) < amount || p.vending || p.prevend {
        return;
    }
    if r.item.equip_switch != 0 {
        if let Some(sd) = unit::get(c, aid) {
            ci::msg(c, &sd, ci::MSI_SWAP_EQUIPITEM_UNREGISTER_FIRST);
        }
        return;
    }
    let flag = pc_cart_additem(c, aid, &r.item, amount, LOG_TYPE_NONE);
    if flag == ADDITEM_SUCCESS {
        pc_delitem(c, aid, idx, amount, 0, 5, LOG_TYPE_NONE);
    } else {
        ci::cart_additem_ack(c, aid, if flag == ADDITEM_OVERAMOUNT { 1 } else { 0 });
        ci::additem(c, aid, idx, amount, 0);
        ci::delitem(c, aid, idx, amount, 0);
    }
}

/// pc.cpp pc_cartitem_amount: what remains after taking `amount` (-1: nothing there).
pub fn pc_cartitem_amount(c: &Ctx, aid: u32, idx: usize, amount: i32) -> i32 {
    match cart_get(c, aid, idx) {
        Some(r) if r.item.nameid != 0 && r.item.amount != 0 => r.item.amount as i32 - amount,
        _ => -1,
    }
}

/// pc.cpp pc_getitemfromcart.
pub fn pc_getitemfromcart(c: &Ctx, aid: u32, idx: i32, amount: i32) -> bool {
    if idx < 0 || idx as usize >= MAX_CART {
        return false;
    }
    let Some(r) = cart_get(c, aid, idx as usize) else { return false };
    let Some(p) = pc::get(c, aid) else { return false };
    if r.item.nameid == 0 || amount < 1 || (r.item.amount as i32) < amount || p.vending || p.prevend {
        return false;
    }
    let mut it = r.item.clone();
    let flag = pc_additem(c, aid, &mut it, amount, LOG_TYPE_NONE);
    // pc_additem may have given the cart item a unique id
    if it.unique_id != r.item.unique_id {
        if let Some(mut r2) = cart_get(c, aid, idx as usize) {
            r2.item.unique_id = it.unique_id;
            cart_put(c, &r2);
        }
    }
    if flag == ADDITEM_SUCCESS {
        pc_cart_delitem(c, aid, idx, amount, 0, LOG_TYPE_NONE);
    } else {
        ci::cart_delitem(c, aid, idx, amount);
        ci::additem(c, aid, idx, amount, flag);
        ci::cart_additem(c, aid, idx, amount);
    }
    true
}

// ---------------------------------------------------------------------------------------------- the cart itself

/// pc.cpp pc_setcart (NEW_CARTS): 0 removes the cart, 1..MAX_CARTS is a cart type.
pub fn pc_setcart(c: &Ctx, aid: u32, kind: i32) -> bool {
    if !(0..=MAX_CARTS).contains(&kind) {
        return false; // Never trust the values sent by the client! [Skotlex]
    }
    if crate::skill::checkskill(c, aid, crate::db::skills::MC_PUSHCART) == 0 && kind != 0 {
        return false; // Push cart is required
    }
    match kind {
        0 => {
            if !pc_iscarton(c, aid) {
                return false;
            }
            crate::status::sc::status_change_end(c, aid, crate::status::consts::SC_PUSH_CART, 0);
            ci::clearcart(c, aid);
        }
        _ => {
            if !pc_iscarton(c, aid) {
                // first time, so fill cart data
                ci::cartlist(c, aid);
                status_calc_cart_weight(c, aid, CALCWT_ITEM | CALCWT_MAXBONUS | CALCWT_CARTSTATE);
            }
            updatestatus(c, aid, SP_CARTINFO);
            if let Some(sd) = unit::get(c, aid) {
                crate::status::sc::sc_start(c, Some(&sd), &sd, crate::status::consts::SC_PUSH_CART, 100, kind, 0);
            }
        }
    }
    if crate::skill::checkskill(c, aid, crate::db::skills::MC_PUSHCART) < 10 {
        crate::status::calc_pc(c, aid, crate::status::SCO_NONE); // Recalc speed penalty.
    }
    true
}

/// clif.cpp clif_SelectCart (ZC_SELECTCART 0x097f, PACKETVER >= 20150826; MC_CARTDECORATE): the cart looks the
/// player may pick, `8 + carts` bytes: <len>.W <account id>.L and the styles 10, 11, 12 ("Right now we have 10-12").
/// Written straight to the player's own connection (WFIFOSET on sd->fd).
pub fn clif_select_cart(c: &Ctx, aid: u32) {
    let w = select_cart_packet(aid);
    if let Some(sd) = unit::get(c, aid) {
        crate::clif::send(c, &w, &sd, crate::clif::Target::SelfOnly);
    }
}

/// clif_SelectCart's bytes.
fn select_cart_packet(aid: u32) -> Vec<u8> {
    let carts: usize = 3;
    let mut w: Vec<u8> = Vec::with_capacity(8 + carts);
    w.extend_from_slice(&0x097fu16.to_le_bytes());
    w.extend_from_slice(&((8 + carts) as u16).to_le_bytes());
    w.extend_from_slice(&aid.to_le_bytes()); // sd->status.account_id
    // Right now we have 10-12, tested it you can also enable selection for all cart styles here(1-12)
    for i in 0..carts {
        w.push(10 + i as u8);
    }
    w
}

// ---------------------------------------------------------------------------------------------- parse

fn packet_u16(pkt: &[u8], off: usize) -> Option<u16> {
    pkt.get(off..off + 2).map(|b| u16::from_le_bytes([b[0], b[1]]))
}
fn packet_i32(pkt: &[u8], off: usize) -> Option<i32> {
    pkt.get(off..off + 4).map(|b| i32::from_le_bytes([b[0], b[1], b[2], b[3]]))
}

/// clif.cpp clif_parse_PutItemToCart (CZ_MOVE_ITEM_FROM_BODY_TO_CART).
pub fn parse_put_item_to_cart(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = pc::get(c, aid) else { return };
    let Some(sd) = unit::get(c, aid) else { return };
    if pc_istrading(&p) || !pc_iscarton(c, aid) || pc_cant_act2(&p, sd.sc.opt1) {
        return;
    }
    if crate::npc::mapflag(c, sd.map, MF_NOUSECART) {
        return;
    }
    let (Some(index), Some(count)) = (packet_u16(pkt, 2), packet_i32(pkt, 4)) else { return };
    pc_putitemtocart(c, aid, index as i16 as i32 - 2, count);
}

/// clif.cpp clif_parse_GetItemFromCart (CZ_MOVE_ITEM_FROM_CART_TO_BODY).
pub fn parse_get_item_from_cart(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = pc::get(c, aid) else { return };
    let Some(sd) = unit::get(c, aid) else { return };
    if !pc_iscarton(c, aid) || pc_cant_act2(&p, sd.sc.opt1) {
        return;
    }
    if crate::npc::mapflag(c, sd.map, MF_NOUSECART) {
        return;
    }
    let (Some(index), Some(amount)) = (packet_u16(pkt, 2), packet_i32(pkt, 4)) else { return };
    pc_getitemfromcart(c, aid, index.wrapping_sub(2) as i32, amount);
}

/// clif.cpp clif_parse_ChangeCart (CZ_REQ_CHANGECART).
pub fn parse_change_cart(c: &Ctx, aid: u32, pkt: &[u8]) {
    if crate::skill::checkskill(c, aid, crate::db::skills::MC_CHANGECART) < 1 {
        return;
    }
    let Some(p) = pc::get(c, aid) else { return };
    if p.npc_id != 0 || crate::itemui::pc_hasprogress(c, aid, crate::itemui::WIP_DISABLE_SKILLITEM) {
        // RENEWAL
        if let Some(sd) = unit::get(c, aid) {
            ci::msg(c, &sd, ci::MSI_BUSY);
        }
        return;
    }
    let pos = crate::net::packets::packet_pos(u16::from_le_bytes([pkt[0], pkt[1]]));
    let off = pos.first().copied().unwrap_or(2) as usize;
    let kind = packet_u16(pkt, off).unwrap_or(0) as i32;
    let lv = status(c, aid).map(|ch| ch.base_level as i32).unwrap_or(0);
    if (kind == 13 && lv > 100) || (kind == 9 && lv > 130) || (kind == 8 && lv > 120) || (kind == 7 && lv > 110) || (kind == 6 && lv > 100) || (kind == 5 && lv > 90) || (kind == 4 && lv > 80) || (kind == 3 && lv > 65) || (kind == 2 && lv > 40) || kind == 1 {
        pc_setcart(c, aid, kind);
    }
}

/// clif.cpp clif_parse_SelectCart (CZ_SELECTCART, PACKETVER >= 20150826).
pub fn parse_select_cart(c: &Ctx, aid: u32, pkt: &[u8]) {
    if crate::skill::checkskill(c, aid, crate::db::skills::MC_CARTDECORATE) < 1 || packet_i32(pkt, 2).map(|v| v as u32) != Some(aid) {
        return;
    }
    let kind = pkt.get(6).copied().unwrap_or(0) as i32;
    if !(10..=12).contains(&kind) {
        return;
    }
    pc_setcart(c, aid, kind);
}

#[cfg(test)]
mod tests {
    /// clif_SelectCart: 097f <len>.W <account id>.L 0a 0b 0c.
    #[test]
    fn select_cart_bytes() {
        assert_eq!(super::select_cart_packet(2000001), vec![0x7f, 0x09, 11, 0, 0x81, 0x84, 0x1e, 0x00, 10, 11, 12]);
    }
}
