//! Barter shops: npc.cpp BarterDatabase::loadingFinished's persisted stock (the SQL `barter` table; the NPCs are
//! created by the world build, npc.rs), npc_barter_purchase; clif.cpp clif_barter_open, clif_barter_extended_open,
//! clif_parse_barter_buy / clif_parse_barter_close and clif_parse_barter_extended_buy / _close.
//! The barters themselves are db/items/barter.rs (the NPC's exname is the barter's name).

use spacetimedb::{table, Table};

use crate::db::items::barter::{self as barterdb, Barter, BarterItem};
use crate::db::items as itemdb;
use crate::inventory::*;
use crate::net::packets::*;
use crate::npc::{npc, Npc, NPCTYPE_BARTER};
use crate::pc;
use crate::shop::clif_npc_buy_result;
use crate::world::Ctx;

/// clif.hpp e_purchase_result (the barter results).
const PURCHASE_SUCCEED: u8 = 0;
const PURCHASE_FAIL_MONEY: u8 = 1;
const PURCHASE_FAIL_WEIGHT: u8 = 2;
const PURCHASE_FAIL_COUNT: u8 = 3;
const PURCHASE_FAIL_EXCHANGE_FAILED: u8 = 11;
const PURCHASE_FAIL_STOCK_EMPTY: u8 = 13;
const PURCHASE_FAIL_GOODS: u8 = 14;

/// The SQL `barter` table (name, index, amount): the stock of stock-limited barter items.
#[table(accessor = barter_stock)]
#[derive(Clone, Debug)]
pub struct BarterStock {
    /// "<name>:<index>".
    #[primary_key]
    pub key: String,
    pub name: String,
    pub index: u16,
    pub amount: u32,
}

/// sd->state.barter_open / barter_extended_open: the `pc::UI_BARTER` / `pc::UI_BARTER_EXTENDED` bits of the player's
/// `ui_open`, so that pc_setpos clears them with the other window flags (pc.cpp:6958-6959) and pc_cant_act2 tests
/// them (pc.hpp:1160). They go with the `pc` row at map_quit.
struct BarterSession {
    barter_open: bool,
    barter_extended_open: bool,
}

fn key(name: &str, index: u16) -> String {
    format!("{name}:{index}")
}

fn session(c: &Ctx, aid: u32) -> BarterSession {
    let ui = pc::get(c, aid).map(|p| p.ui_open).unwrap_or(0);
    BarterSession { barter_open: ui & pc::UI_BARTER != 0, barter_extended_open: ui & pc::UI_BARTER_EXTENDED != 0 }
}

fn put_session(c: &Ctx, aid: u32, s: BarterSession) {
    let Some(mut p) = pc::get(c, aid) else { return };
    let ui = (p.ui_open & !(pc::UI_BARTER | pc::UI_BARTER_EXTENDED))
        | if s.barter_open { pc::UI_BARTER } else { 0 }
        | if s.barter_extended_open { pc::UI_BARTER_EXTENDED } else { 0 };
    if ui != p.ui_open {
        p.ui_open = ui;
        pc::put(c, &p);
    }
}

/// The current stock of a barter item (s_npc_barter_item::stock).
pub fn stock(c: &Ctx, name: &str, item: &BarterItem) -> u32 {
    c.db().barter_stock().key().find(key(name, item.index)).map(|r| r.amount).unwrap_or(item.stock)
}

/// `REPLACE INTO barter (name, index, amount) VALUES (…, '%hu')`: the amount is written as an unsigned short.
fn save_stock(c: &Ctx, name: &str, index: u16, amount: u32) {
    let row = BarterStock { key: key(name, index), name: name.to_string(), index, amount: amount as u16 as u32 };
    if c.db().barter_stock().key().find(&row.key).is_some() {
        c.db().barter_stock().key().update(row);
    } else {
        c.db().barter_stock().insert(row);
    }
}

/// BarterDatabase::loadingFinished's stock part, for a barter whose NPC was created: a stock-limited item takes
/// its previous amount from the table (or its database stock) and the table is refreshed; other items' rows go.
pub fn load_stock(c: &Ctx, barter: &Barter) {
    for it in &barter.items {
        if it.stock_limited {
            let amount = stock(c, &barter.name, it);
            save_stock(c, &barter.name, it.index, amount);
        } else {
            c.db().barter_stock().key().delete(key(&barter.name, it.index));
        }
    }
}

// ---------------------------------------------------------------------------------------------- windows

/// npc_click / callshop for a barter NPC: the extended window when the barter needs it.
pub fn open(c: &Ctx, aid: u32, nd: &Npc) {
    let extended = barterdb::barter(&nd.exname).map(|b| b.extended).unwrap_or(false);
    if extended {
        clif_barter_extended_open(c, aid, nd);
    } else {
        clif_barter_open(c, aid, nd);
    }
}

/// clif.cpp clif_barter_open: ZC_NPC_BARTER_MARKET_ITEMINFO (one currency per item).
pub fn clif_barter_open(c: &Ctx, aid: u32, nd: &Npc) {
    let mut s = session(c, aid);
    let Some(barter) = barterdb::barter(&nd.exname) else { return };
    if nd.subtype != NPCTYPE_BARTER || barter.extended || s.barter_open {
        return;
    }
    s.barter_open = true;
    put_session(c, aid, s);
    let mut list = Vec::new();
    for it in &barter.items {
        let Some(id) = itemdb::itemdb_exists(it.nameid) else { continue };
        let mut e = PACKET_ZC_NPC_BARTER_MARKET_ITEMINFO_sub {
            nameid: itemdb::client_nameid(id.nameid),
            r#type: crate::clif_items::itemtype(id.nameid),
            amount: if it.stock_limited { stock(c, &barter.name, it) } else { u32::MAX }, // -1
            currencyNameid: 0,
            currencyAmount: 0,
            weight: id.weight,
            index: it.index as u32,
            viewSprite: id.look as u16,
            location: crate::pc_equip::pc_equippoint_sub(c, aid, id),
        };
        // Use a loop if someone did not start with index 0; it is a normal barter, cancel after first entry
        if let Some(r) = it.requirements.first() {
            e.currencyNameid = itemdb::client_nameid(r.nameid);
            e.currencyAmount = r.amount as u32;
        }
        list.push(e);
    }
    let p = PACKET_ZC_NPC_BARTER_MARKET_ITEMINFO { packetType: PACKET_ZC_NPC_BARTER_MARKET_ITEMINFO::ID as i16, packetLength: 0, list };
    if let Some(sd) = crate::unit::get(c, aid) {
        crate::clif::send(c, &p.to_vec(), &sd, crate::clif::Target::SelfOnly);
    }
}

/// clif.cpp clif_barter_extended_open: ZC_NPC_EXPANDED_BARTER_MARKET_ITEMINFO. Each item is written without the
/// struct's one-entry currency array, then its currencies (rAthena's MSVC workaround, the wire layout).
pub fn clif_barter_extended_open(c: &Ctx, aid: u32, nd: &Npc) {
    let mut s = session(c, aid);
    let Some(barter) = barterdb::barter(&nd.exname) else { return };
    if nd.subtype != NPCTYPE_BARTER || !barter.extended || s.barter_extended_open {
        return;
    }
    s.barter_extended_open = true;
    put_session(c, aid, s);
    let mut w: Vec<u8> = Vec::new();
    w.extend_from_slice(&(PACKET_ZC_NPC_EXPANDED_BARTER_MARKET_ITEMINFO::ID as i16).to_le_bytes());
    w.extend_from_slice(&0i16.to_le_bytes());
    w.extend_from_slice(&0i32.to_le_bytes());
    let mut items_count: i32 = 0;
    for it in &barter.items {
        let Some(id) = itemdb::itemdb_exists(it.nameid) else { continue };
        let amount = if it.stock_limited { stock(c, &barter.name, it) } else { u32::MAX }; // -1
        w.extend_from_slice(&itemdb::client_nameid(id.nameid).to_le_bytes());
        w.extend_from_slice(&(crate::clif_items::itemtype(id.nameid) as u16).to_le_bytes());
        w.extend_from_slice(&amount.to_le_bytes());
        w.extend_from_slice(&id.weight.to_le_bytes());
        w.extend_from_slice(&(it.index as u32).to_le_bytes());
        w.extend_from_slice(&it.price.to_le_bytes());
        w.extend_from_slice(&(id.look as u16).to_le_bytes());
        w.extend_from_slice(&crate::pc_equip::pc_equippoint_sub(c, aid, id).to_le_bytes());
        w.extend_from_slice(&(it.requirements.len() as u32).to_le_bytes());
        items_count += 1;
        for r in &it.requirements {
            let req = PACKET_ZC_NPC_EXPANDED_BARTER_MARKET_ITEMINFO_sub2 {
                nameid: r.nameid,
                refine_level: if r.refine >= 0 { r.refine as u16 } else { 0 },
                amount: r.amount as u32,
                r#type: crate::clif_items::itemtype(r.nameid) as u16,
            };
            req.write(&mut w);
        }
    }
    let len = w.len() as u16;
    w[2..4].copy_from_slice(&len.to_le_bytes());
    w[4..8].copy_from_slice(&items_count.to_le_bytes());
    if let Some(sd) = crate::unit::get(c, aid) {
        crate::clif::send(c, &w, &sd, crate::clif::Target::SelfOnly);
    }
}

// ---------------------------------------------------------------------------------------------- purchase

struct Purchase<'a> {
    item: &'a BarterItem,
    amount: u32,
}

/// npc.cpp npc_barter_purchase.
fn npc_barter_purchase(c: &Ctx, aid: u32, barter: &Barter, purchases: &mut [Purchase]) -> u8 {
    let mut required_zeny: u64 = 0;
    let mut required_weight: u32 = 0;
    let mut reduced_weight: u32 = 0;
    let mut required_slots: u16 = 0;
    let mut required_items = [0u32; MAX_INVENTORY];
    let inv = inv_array(c, aid);
    let slot = |j: usize| inv.get(j).and_then(|r| r.as_ref()).map(|r| &r.item);
    for purchase in purchases.iter() {
        let Some(data) = itemdb::itemdb_exists(purchase.item.nameid) else { return PURCHASE_FAIL_EXCHANGE_FAILED };
        let amount = purchase.amount;
        if purchase.item.stock_limited && stock(c, &barter.name, purchase.item) < amount {
            return PURCHASE_FAIL_STOCK_EMPTY;
        }
        match pc_checkadditem(c, aid, purchase.item.nameid, amount as i32) {
            CHKADDITEM_OVERAMOUNT => return PURCHASE_FAIL_COUNT,
            CHKADDITEM_NEW => required_slots = required_slots.wrapping_add(data.inventory_slot_needed(amount as i32) as u16),
            _ => {}
        }
        required_zeny += purchase.item.price as u64 * amount as u64;
        required_weight = required_weight.wrapping_add(data.weight.wrapping_mul(amount));
        for requirement in &purchase.item.requirements {
            let Some(id) = itemdb::itemdb_exists(requirement.nameid) else { return PURCHASE_FAIL_EXCHANGE_FAILED };
            // the candidate slots: not equipped, not in the equip switch, not a hidden favorite, refine matching
            let usable = |it: &Item, refine: i32| {
                it.nameid == requirement.nameid
                    && it.equip == 0
                    && it.equip_switch == 0
                    && !(crate::shop::hide_fav_sell() && it.favorite != 0)
                    && (refine < 0 || it.refine as i32 == refine)
            };
            if itemdb::itemdb_isstackable2(id) {
                let mut found = false;
                for j in 0..MAX_INVENTORY {
                    let Some(it) = slot(j) else { continue };
                    if it.nameid != requirement.nameid {
                        continue;
                    }
                    if !usable(it, requirement.refine as i32) {
                        continue;
                    }
                    // Found a match, accumulate required amount
                    required_items[j] = required_items[j].wrapping_add(requirement.amount as u32 * amount);
                    // Check if there are still enough items available
                    if required_items[j] > it.amount as u32 {
                        return PURCHASE_FAIL_GOODS;
                    }
                    found = true;
                    break;
                }
                if !found {
                    return PURCHASE_FAIL_GOODS; // Required item not found
                }
            } else {
                for _ in 0..(requirement.amount as u32 * amount) {
                    let take = |required_items: &mut [u32; MAX_INVENTORY], refine: i32| -> bool {
                        for j in 0..MAX_INVENTORY {
                            let Some(it) = slot(j) else { continue };
                            if !usable(it, refine) {
                                continue;
                            }
                            // Found a match, since it is not stackable, check if it was already taken
                            if required_items[j] > 0 {
                                continue;
                            }
                            required_items[j] = 1; // Mark it as taken
                            return true;
                        }
                        false
                    };
                    if !take(&mut required_items, requirement.refine as i32) {
                        // Maybe the refine level did not match
                        if requirement.refine >= 0 {
                            // Try to find a higher refine level, going from the next lowest to the highest possible
                            let mut ok = false;
                            for refine in (requirement.refine as i32 + 1)..=MAX_REFINE {
                                if take(&mut required_items, refine) {
                                    ok = true;
                                    break;
                                }
                            }
                            if !ok {
                                return PURCHASE_FAIL_GOODS; // No matching entry found
                            }
                        } else {
                            return PURCHASE_FAIL_GOODS;
                        }
                    }
                }
            }
            reduced_weight = reduced_weight.wrapping_add(amount.wrapping_mul(requirement.amount as u32).wrapping_mul(id.weight));
        }
    }
    let zeny = status(c, aid).map(|ch| ch.zeny as u64).unwrap_or(0);
    // Check if there is enough Zeny
    if zeny < required_zeny {
        return PURCHASE_FAIL_MONEY;
    }
    // Check if there is enough Weight Limit
    let (weight, max_weight) = st(c, aid).map(|s| (s.weight, s.max_weight)).unwrap_or((0, 0));
    if weight.wrapping_add(required_weight).wrapping_sub(reduced_weight) > max_weight {
        return PURCHASE_FAIL_WEIGHT;
    }
    if pc_inventoryblank(c, aid) < required_slots {
        return PURCHASE_FAIL_COUNT;
    }
    for (i, &n) in required_items.iter().enumerate() {
        if n > 0 && pc_delitem(c, aid, i as i32, n as i32, 0, 0, crate::map_log::LOG_TYPE_BARTER) != 0 {
            return PURCHASE_FAIL_EXCHANGE_FAILED;
        }
    }
    if pc_payzeny(c, aid, required_zeny as i32 as i64, crate::map_log::LOG_TYPE_BARTER, 0) != 0 {
        return PURCHASE_FAIL_MONEY;
    }
    for purchase in purchases.iter() {
        let Some(data) = itemdb::itemdb_exists(purchase.item.nameid) else { return PURCHASE_FAIL_EXCHANGE_FAILED };
        if purchase.item.stock_limited {
            let left = stock(c, &barter.name, purchase.item).wrapping_sub(purchase.amount);
            save_stock(c, &barter.name, purchase.item.index, left);
        }
        if itemdb::itemdb_isstackable2(data) {
            let mut it = Item::new(purchase.item.nameid);
            it.identify = 1;
            if pc_additem(c, aid, &mut it, purchase.amount as i32, crate::map_log::LOG_TYPE_BARTER) != ADDITEM_SUCCESS {
                return PURCHASE_FAIL_EXCHANGE_FAILED;
            }
        } else if data.r#type == itemdb::IT_PETEGG {
            for _ in 0..purchase.amount {
                if !crate::pet::pet_create_egg(c, aid, purchase.item.nameid) {
                    return PURCHASE_FAIL_EXCHANGE_FAILED;
                }
            }
        } else {
            for _ in 0..purchase.amount {
                let mut it = Item::new(purchase.item.nameid);
                it.identify = 1;
                it.refine = purchase.item.refine as u8;
                if pc_additem(c, aid, &mut it, 1, crate::map_log::LOG_TYPE_BARTER) != ADDITEM_SUCCESS {
                    return PURCHASE_FAIL_EXCHANGE_FAILED;
                }
            }
        }
    }
    PURCHASE_SUCCEED
}

/// The shop the player has open, if it is a barter of the right kind.
fn open_barter(c: &Ctx, aid: u32, extended: bool) -> Option<&'static Barter> {
    let p = pc::get(c, aid)?;
    let s = session(c, aid);
    // No shop open
    if p.npc_shopid == 0 || (!extended && !s.barter_open) || (extended && !s.barter_extended_open) {
        return None;
    }
    // Unknown shop / not a barter
    let nd = c.db().npc().id().find(p.npc_shopid)?;
    if nd.subtype != NPCTYPE_BARTER {
        return None;
    }
    let barter = barterdb::barter(&nd.exname)?;
    // An extended barter through the normal window (or the other way round)
    if barter.extended != extended {
        return None;
    }
    Some(barter)
}

/// The purchase list checks of clif_parse_barter_buy / _extended_buy: every shop index valid and used once, no
/// target item twice.
fn purchases<'a>(barter: &'a Barter, list: &[(u32, u32)]) -> Option<Vec<Purchase<'a>>> {
    let mut out = Vec::with_capacity(list.len());
    for (i, &(shop_index, amount)) in list.iter().enumerate() {
        // util::map_find( barter->items, (uint16)shopIndex )
        let item = barter.item(shop_index as u16)?;
        for &(shop_index2, _) in &list[i + 1..] {
            if shop_index == shop_index2 {
                return None; // Same shop index
            }
            let item2 = barter.item(shop_index2 as u16)?;
            if item.nameid == item2.nameid {
                return None; // Same target item id
            }
        }
        out.push(Purchase { item, amount });
    }
    Some(out)
}

/// clif.cpp clif_parse_barter_close (CZ_NPC_BARTER_MARKET_CLOSE).
pub fn parse_barter_close(c: &Ctx, aid: u32) {
    let mut s = session(c, aid);
    if s.barter_open {
        if let Some(mut p) = pc::get(c, aid) {
            p.npc_shopid = 0;
            pc::put(c, &p);
        }
        s.barter_open = false;
        put_session(c, aid, s);
    }
}

/// clif.cpp clif_parse_barter_extended_close (CZ_NPC_EXPANDED_BARTER_MARKET_CLOSE).
pub fn parse_barter_extended_close(c: &Ctx, aid: u32) {
    let mut s = session(c, aid);
    if s.barter_extended_open {
        if let Some(mut p) = pc::get(c, aid) {
            p.npc_shopid = 0;
            pc::put(c, &p);
        }
        s.barter_extended_open = false;
        put_session(c, aid, s);
    }
}

/// clif.cpp clif_parse_barter_buy (CZ_NPC_BARTER_MARKET_PURCHASE: itemId, amount, invIndex, shopIndex).
pub fn parse_barter_buy(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(barter) = open_barter(c, aid, false) else { return };
    let Some(p) = PACKET_CZ_NPC_BARTER_MARKET_PURCHASE::read(&mut crate::net::wire::Reader::new(pkt)) else { return };
    let entries = (p.packetLength as i32 - PACKET_CZ_NPC_BARTER_MARKET_PURCHASE::SIZE as i32).max(0) as usize / PACKET_CZ_NPC_BARTER_MARKET_PURCHASE_sub::SIZE;
    // Empty purchase list
    if entries == 0 {
        return;
    }
    let list: Vec<(u32, u32)> = p.list.iter().take(entries).map(|e| (e.shopIndex, e.amount)).collect();
    let Some(mut purchases) = purchases(barter, &list) else { return };
    let res = npc_barter_purchase(c, aid, barter, &mut purchases);
    clif_npc_buy_result(c, aid, res);
}

/// clif.cpp clif_parse_barter_extended_buy (CZ_NPC_EXPANDED_BARTER_MARKET_PURCHASE: itemId, shopIndex, amount).
pub fn parse_barter_extended_buy(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(barter) = open_barter(c, aid, true) else { return };
    let Some(p) = PACKET_CZ_NPC_EXPANDED_BARTER_MARKET_PURCHASE::read(&mut crate::net::wire::Reader::new(pkt)) else { return };
    let entries = (p.packetLength as i32 - PACKET_CZ_NPC_EXPANDED_BARTER_MARKET_PURCHASE::SIZE as i32).max(0) as usize / PACKET_CZ_NPC_EXPANDED_BARTER_MARKET_PURCHASE_sub::SIZE;
    if entries == 0 {
        return;
    }
    let list: Vec<(u32, u32)> = p.list.iter().take(entries).map(|e| (e.shopIndex, e.amount)).collect();
    let Some(mut purchases) = purchases(barter, &list) else { return };
    let res = npc_barter_purchase(c, aid, barter, &mut purchases);
    clif_npc_buy_result(c, aid, res);
}
