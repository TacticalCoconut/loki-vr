//! map/buyingstore.cpp (buying stores: setup, create, close, open another player's, sell to it, the search hooks) and
//! its clif.cpp packets and parsers.
//!
//! sd->state.buyingstore stays the `pc.buyingstore` flag; sd->buyer_id, sd->buyingstore (items, zeny limit, slots) and
//! sd->message (the store name, shared with vending, [`sd_message`]) are the [`PcBuyingStore`] row of an online
//! character. rAthena mirrors every open store into the SQL tables `buyingstores` / `buyingstore_items` for its
//! autotrade restore (do_init_buyingstore_autotrade, buyingstore_reopen) and the web service: the tables and the
//! restore are autotrade.rs', written at rAthena's places here. buyingstore_db (open stores by char id) is the set of
//! `pc` rows with `buyingstore` set.

use spacetimedb::{table, SpacetimeType, Table};

use crate::clif::{self, Target};
use crate::db::items as itemdb;
use crate::inventory::{self as inv, MAX_INVENTORY};
use crate::net::packets::*;
use crate::net::wire::{from_latin1, until_nul, Reader};
use crate::pc::{self, pc as _};
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// buyingstore.hpp MAX_BUYINGSTORE_SLOTS; buyingstore.cpp BUYINGSTORE_MAX_PRICE / BUYINGSTORE_MAX_AMOUNT.
pub const MAX_BUYINGSTORE_SLOTS: usize = 5;
const BUYINGSTORE_MAX_PRICE: u32 = 99_990_000;
const BUYINGSTORE_MAX_AMOUNT: i32 = 9999;
/// map.hpp MESSAGE_SIZE.
const MESSAGE_SIZE: usize = 80;
/// conf/battle/feature.conf `feature.buying_store: on`; conf/battle/player.conf `major_overweight_rate: 90`;
/// conf/map_athena.conf `save_settings: 4095` (CHARSAVE_VENDING 0x002).
pub fn feature_buying_store() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_BUYING_STORE) != 0
}
fn major_overweight_rate() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::MAJOR_OVERWEIGHT_RATE) as u16
}
const CHARSAVE_VENDING: bool = true;
/// script_constants MF_NOBUYINGSTORE; status.hpp MANNER_NOROOM.
const MF_NOBUYINGSTORE: i16 = 75;
const MANNER_NOROOM: i32 = 0x10;

/// buyingstore.cpp e_buyingstore_failure.
pub const BUYINGSTORE_CREATE: u16 = 1;
pub const BUYINGSTORE_CREATE_OVERWEIGHT: u16 = 2;
pub const BUYINGSTORE_TRADE_BUYER_ZENY: i16 = 3;
pub const BUYINGSTORE_TRADE_BUYER_NO_ITEMS: i16 = 4;
pub const BUYINGSTORE_TRADE_SELLER_FAILED: u16 = 5;
pub const BUYINGSTORE_TRADE_SELLER_COUNT: u16 = 6;
pub const BUYINGSTORE_TRADE_SELLER_ZENY: u16 = 7;

/// buyingstore.hpp struct s_buyingstore_item.
#[derive(SpacetimeType, Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct BuyingStoreItem {
    pub price: i32,
    pub amount: u16,
    pub nameid: u32,
}

/// sd->buyer_id, sd->buyingstore, sd->message of an online character.
#[table(accessor = pc_buyingstore)]
#[derive(Clone, Debug)]
pub struct PcBuyingStore {
    #[primary_key]
    pub account_id: u32,
    pub buyer_id: u32,
    pub items: Vec<BuyingStoreItem>,
    pub zenylimit: i32,
    pub slots: u8,
    /// sd->message (the store's title).
    pub message: String,
}

fn fresh(aid: u32) -> PcBuyingStore {
    PcBuyingStore { account_id: aid, buyer_id: 0, items: vec![BuyingStoreItem::default(); MAX_BUYINGSTORE_SLOTS], zenylimit: 0, slots: 0, message: String::new() }
}

/// The buying store state of `aid` (a zeroed one when it has none yet).
pub fn bs(c: &Ctx, aid: u32) -> PcBuyingStore {
    c.db().pc_buyingstore().account_id().find(aid).unwrap_or_else(|| fresh(aid))
}

fn put(c: &Ctx, b: &PcBuyingStore) {
    if c.db().pc_buyingstore().account_id().find(b.account_id).is_some() {
        c.db().pc_buyingstore().account_id().update(b.clone());
    } else {
        c.db().pc_buyingstore().insert(b.clone());
    }
}

/// unit_free: the state goes with the session.
pub fn on_unit_free(c: &Ctx, aid: u32) {
    c.db().pc_buyingstore().account_id().delete(aid);
}

/// sd->message: the title of the player's buying store or vending shop (also Graffiti / Talkie Box's text).
pub fn sd_message(c: &Ctx, aid: u32) -> String {
    c.db().pc_buyingstore().account_id().find(aid).map(|b| b.message).unwrap_or_default()
}

/// safestrncpy(sd->message, …) by another system (vending_openvending).
pub fn set_sd_message(c: &Ctx, aid: u32, message: &str) {
    let mut b = bs(c, aid);
    b.message = message.to_string();
    put(c, &b);
}

fn is_buying(c: &Ctx, aid: u32) -> bool {
    pc::get(c, aid).map(|p| p.buyingstore).unwrap_or(false)
}

fn set_buying(c: &Ctx, aid: u32, on: bool) {
    if let Some(mut p) = pc::get(c, aid) {
        p.buyingstore = on;
        pc::put(c, &p);
    }
}

/// buyingstore_getdb(): the open buying stores (by char id), in rAthena's idb iteration order.
pub fn buyingstore_db(c: &Ctx) -> Vec<u32> {
    let mut v: Vec<(u32, u32)> = c.db().pc().iter().filter(|p| p.buyingstore).map(|p| (p.char_id, p.account_id)).collect();
    v.sort_by_key(|&(cid, _)| crate::party::idb_order(cid));
    v.into_iter().map(|(_, aid)| aid).collect()
}

/// map_getcell(m, x, y, CELL_CHKNOBUYINGSTORE).
fn cell_nobuyingstore(c: &Ctx, sd: &Unit) -> bool {
    crate::map_cell::getcell(c, Some(sd.map), sd.x, sd.y, crate::map_cell::CELL_CHKNOBUYINGSTORE) != 0
}

/// sc SC_NOCHAT with MANNER_NOROOM (the manner system's mute of chat rooms and shops).
fn manner_noroom(c: &Ctx, aid: u32) -> bool {
    crate::status::sc::get_sce(c, aid, crate::status::consts::SC_NOCHAT).map(|e| e.val1 & MANNER_NOROOM != 0).unwrap_or(false)
}

fn say(c: &Ctx, sd: &Unit, n: u16) {
    clif::displaymessage(c, sd, crate::data::msg_txt(n));
}

// ---------------------------------------------------------------------------------------------- buyingstore.cpp

/// buyingstore.cpp buyingstore_setup: 0 the preparation window opens, 1 cannot open, 2 manner penalty, 3 map flag,
/// 4 cell.
pub fn buyingstore_setup(c: &Ctx, aid: u32, slots: u8) -> i8 {
    let Some(p) = pc::get(c, aid) else { return 1 };
    let Some(sd) = unit::get(c, aid) else { return 1 };
    if !feature_buying_store() || p.vending || p.buyingstore || p.trading || slots == 0 {
        return 1;
    }
    if manner_noroom(c, aid) {
        // custom: mute limitation
        return 2;
    }
    if crate::npc::mapflag(c, sd.map, MF_NOBUYINGSTORE) {
        // custom: no buyingstore maps
        say(c, &sd, 276); // "You can't open a shop on this map"
        return 3;
    }
    if cell_nobuyingstore(c, &sd) {
        // custom: no buyingstore cells
        say(c, &sd, 204); // "You can't open a shop on this cell."
        return 4;
    }
    let mut slots = slots;
    if slots as usize > MAX_BUYINGSTORE_SLOTS {
        log::warn!("buyingstore_setup: Requested {slots} slots, but server supports only {MAX_BUYINGSTORE_SLOTS} slots.");
        slots = MAX_BUYINGSTORE_SLOTS as u8;
    }
    let mut b = bs(c, aid);
    b.slots = slots;
    put(c, &b);
    clif_buyingstore_open(c, aid);
    0
}

/// buyingstore.cpp buyingstore_create: 0 success, 1 cannot open, 2 manner penalty, 3 map flag, 4 cell, 5 invalid
/// count / result / item, 6 cannot give items, 7 would be overweight.
pub fn buyingstore_create(c: &Ctx, aid: u32, zenylimit: i32, result: u8, storename: &str, itemlist: &[PACKET_CZ_REQ_OPEN_BUYING_STORE_sub]) -> i8 {
    buyingstore_create_at(c, aid, zenylimit, result, storename, itemlist, None)
}

/// buyingstore_create with rAthena's `struct s_autotrader *at`: the autotrader being restored (its direction and
/// sitting go into the SQL row, autotrade.rs), None when the player opens the store.
pub fn buyingstore_create_at(c: &Ctx, aid: u32, zenylimit: i32, result: u8, storename: &str, itemlist: &[PACKET_CZ_REQ_OPEN_BUYING_STORE_sub], at: Option<&crate::autotrade::Autotrader>) -> i8 {
    let count = itemlist.len();
    if result == 0 || count == 0 {
        // canceled, or no items
        return 5;
    }
    let Some(p) = pc::get(c, aid) else { return 1 };
    let Some(sd) = unit::get(c, aid) else { return 1 };
    let Some(ch) = pc::character_of(c, aid) else { return 1 };
    let mut b = bs(c, aid);
    if !feature_buying_store() || inv::pc_istrading(&p) || b.slots == 0 || count > b.slots as usize || zenylimit <= 0 || zenylimit > ch.zeny as i32 || storename.is_empty() {
        // disabled or invalid input
        b.slots = 0;
        put(c, &b);
        clif_buyingstore_open_failed(c, aid, BUYINGSTORE_CREATE, 0);
        return 1;
    }
    if !inv::pc_can_give_items(c, aid) {
        // custom: GM is not allowed to buy (give zeny)
        b.slots = 0;
        put(c, &b);
        say(c, &sd, 246); // Your GM level doesn't authorize you to perform this action.
        clif_buyingstore_open_failed(c, aid, BUYINGSTORE_CREATE, 0);
        return 6;
    }
    if manner_noroom(c, aid) {
        return 2;
    }
    if crate::npc::mapflag(c, sd.map, MF_NOBUYINGSTORE) {
        say(c, &sd, 276); // "You can't open a shop on this map"
        return 3;
    }
    if cell_nobuyingstore(c, &sd) {
        say(c, &sd, 204); // "You can't open a shop on this cell."
        return 4;
    }
    let Some(st) = inv::st(c, aid) else { return 1 };
    let mut weight: u32 = st.weight;
    let gmlv = pc::group_level(c, aid);
    // check item list
    let mut i = 0usize;
    while i < count {
        let item = &itemlist[i];
        // invalid input
        let Some(id) = itemdb::itemdb_exists(item.itemId).filter(|_| item.amount != 0) else { break };
        // invalid price: unlike vending, items cannot be bought at 0 Zeny
        if item.price == 0 || item.price > BUYINGSTORE_MAX_PRICE {
            break;
        }
        // restrictions: allowed and no character-bound items
        if !id.flag.buyingstore || !itemdb::itemdb_cantrade_sub(id, gmlv, gmlv) {
            break;
        }
        let idx = inv::pc_search_inventory(c, aid, item.itemId);
        // At least one must be owned
        if idx < 0 {
            break;
        }
        // too many items of same kind
        let have = inv::inv_get(c, aid, idx as usize).map(|r| r.item.amount as i32).unwrap_or(0);
        if have + item.amount as i32 > BUYINGSTORE_MAX_AMOUNT {
            break;
        }
        // duplicate check. as the client does this too, only malicious intent should be caught here
        if i > 0 && b.items[..i].iter().any(|e| e.nameid == item.itemId) {
            log::warn!("buyingstore_create: Found duplicate item on buying list (nameid={}, amount={}, account_id={aid}).", item.itemId, item.amount);
            break;
        }
        weight = weight.wrapping_add(id.weight.wrapping_mul(item.amount as u32));
        b.items[i] = BuyingStoreItem { nameid: item.itemId, amount: item.amount, price: item.price as i32 };
        i += 1;
    }
    if i != count {
        // invalid item/amount/price
        b.slots = 0;
        put(c, &b);
        clif_buyingstore_open_failed(c, aid, BUYINGSTORE_CREATE, 0);
        return 5;
    }
    if crate::status::calc::pc_getpercentweight(&st, weight) >= major_overweight_rate() {
        // not able to carry all wanted items without getting overweight (90%)
        b.slots = 0;
        put(c, &b);
        clif_buyingstore_open_failed(c, aid, BUYINGSTORE_CREATE_OVERWEIGHT, weight);
        return 7;
    }
    // success
    set_buying(c, aid, true);
    b.buyer_id = crate::ids::next(c.rc, "buyer_id", 1);
    b.zenylimit = zenylimit;
    b.slots = i as u8; // store actual amount of items
    b.message = storename.chars().take(MESSAGE_SIZE - 1).collect();
    put(c, &b);
    // INSERT INTO `buyingstores` … / INSERT INTO `buyingstore_items` … (autotrade.rs)
    crate::autotrade::buyingstore_sql_insert(c, aid, at);
    clif_buyingstore_myitemlist(c, aid);
    clif_buyingstore_entry(c, aid, None);
    0
}

/// buyingstore.cpp buyingstore_close.
pub fn buyingstore_close(c: &Ctx, aid: u32) {
    if !is_buying(c, aid) {
        return;
    }
    // DELETE FROM `buyingstore_items` / `buyingstores` (autotrade.rs)
    crate::autotrade::buyingstore_sql_delete(c, bs(c, aid).buyer_id);
    set_buying(c, aid, false);
    let mut b = bs(c, aid);
    b.buyer_id = 0;
    b.items = vec![BuyingStoreItem::default(); MAX_BUYINGSTORE_SLOTS];
    b.zenylimit = 0;
    b.slots = 0;
    put(c, &b);
    // notify other players
    clif_buyingstore_disappear_entry(c, aid, None);
}

/// buyingstore.cpp buyingstore_open: the item list of `account_id`'s store for `aid`.
pub fn buyingstore_open(c: &Ctx, aid: u32, account_id: u32) {
    let Some(p) = pc::get(c, aid) else { return };
    if !feature_buying_store() || inv::pc_istrading(&p) {
        return; // not allowed to sell
    }
    let Some(sd) = unit::get(c, aid) else { return };
    if !inv::pc_can_give_items(c, aid) {
        say(c, &sd, 246); // Your GM level doesn't authorize you to perform this action.
        return;
    }
    if !is_buying(c, account_id) {
        return; // not online or not buying
    }
    let Some(pl) = unit::get(c, account_id) else { return };
    if !crate::searchstore::searchstore_queryremote(c, aid, account_id) && (sd.map != pl.map || !crate::path::check_distance((sd.x - pl.x) as i32, (sd.y - pl.y) as i32, crate::conf::area_size() as i32)) {
        return; // out of view range
    }
    // success
    clif_buyingstore_itemlist(c, aid, account_id);
}

/// buyingstore.cpp buyingstore_trade: `aid` sells the listed items to `account_id`'s store `buyer_id`.
pub fn buyingstore_trade(c: &Ctx, aid: u32, account_id: u32, buyer_id: u32, itemlist: &[PACKET_CZ_REQ_TRADE_BUYING_STORE_sub]) {
    let count = itemlist.len();
    if count == 0 {
        return; // nothing to do
    }
    let Some(p) = pc::get(c, aid) else { return };
    let Some(sd) = unit::get(c, aid) else { return };
    if !feature_buying_store() || inv::pc_istrading(&p) {
        clif_buyingstore_trade_failed_seller(c, aid, BUYINGSTORE_TRADE_SELLER_FAILED, 0);
        return;
    }
    if !inv::pc_can_give_items(c, aid) {
        say(c, &sd, 246); // Your GM level doesn't authorize you to perform this action.
        clif_buyingstore_trade_failed_seller(c, aid, BUYINGSTORE_TRADE_SELLER_FAILED, 0);
        return;
    }
    let pl_b = bs(c, account_id);
    let pl = unit::get(c, account_id);
    let Some(pl) = pl.filter(|_| is_buying(c, account_id) && pl_b.buyer_id == buyer_id) else {
        // not online, not buying or not same store
        clif_buyingstore_trade_failed_seller(c, aid, BUYINGSTORE_TRADE_SELLER_FAILED, 0);
        return;
    };
    if !crate::searchstore::searchstore_queryremote(c, aid, account_id) && (sd.map != pl.map || !crate::path::check_distance((sd.x - pl.x) as i32, (sd.y - pl.y) as i32, crate::conf::area_size() as i32)) {
        // out of view range
        clif_buyingstore_trade_failed_seller(c, aid, BUYINGSTORE_TRADE_SELLER_FAILED, 0);
        return;
    }
    crate::searchstore::searchstore_clearremote(c, aid);
    let Some(pl_ch) = pc::character_of(c, account_id) else { return };
    let Some(pl_st) = inv::st(c, account_id) else { return };
    let mut pl_b = bs(c, account_id);
    // buyer lost zeny in the mean time? fix the limit
    if (pl_ch.zeny as i32) < pl_b.zenylimit {
        pl_b.zenylimit = pl_ch.zeny as i32;
        put(c, &pl_b);
    }
    let mut weight: u32 = pl_st.weight;
    let mut zeny: i32 = 0;
    let gmlv = pc::group_level(c, aid);
    let pl_gmlv = pc::group_level(c, account_id);
    let slots = pl_b.slots as usize;
    // check item list
    for i in 0..count {
        let item = &itemlist[i];
        // duplicate check. as the client does this too, only malicious intent should be caught here
        if itemlist[..i].iter().any(|k| k.index == item.index) {
            log::warn!("buyingstore_trade: Found duplicate item on selling list (nameid={}, amount={}, account_id={aid}).", item.itemId, item.amount);
            clif_buyingstore_trade_failed_seller(c, aid, BUYINGSTORE_TRADE_SELLER_FAILED, item.itemId);
            return;
        }
        let index = item.index as i32 - 2;
        // invalid input
        let slot = if index >= 0 && (index as usize) < MAX_INVENTORY { inv::inv_get(c, aid, index as usize) } else { None };
        let Some(slot) = slot.filter(|r| r.item.nameid == item.itemId && r.item.amount as i32 >= item.amount as i32) else {
            clif_buyingstore_trade_failed_seller(c, aid, BUYINGSTORE_TRADE_SELLER_FAILED, item.itemId);
            return;
        };
        let it = &slot.item;
        // non-tradable item
        if it.expire_time != 0
            || (it.bound != 0 && !inv::pc_can_give_bounded_items(c, aid))
            || !itemdb::itemdb_isrestricted(it.nameid, &it.cards(), gmlv, pl_gmlv, itemdb::itemdb_cantrade_sub)
            || it.cards() != [0u32; 4]
        {
            clif_buyingstore_trade_failed_seller(c, aid, BUYINGSTORE_TRADE_SELLER_FAILED, item.itemId);
            return;
        }
        let listidx = pl_b.items[..slots].iter().position(|e| e.nameid == item.itemId);
        // there is no such item or the buyer has already bought all of them
        let Some(listidx) = listidx.filter(|&l| pl_b.items[l].amount != 0) else {
            clif_buyingstore_trade_failed_seller(c, aid, BUYINGSTORE_TRADE_SELLER_FAILED, item.itemId);
            return;
        };
        // buyer does not need that much of the item
        if pl_b.items[listidx].amount < item.amount {
            clif_buyingstore_trade_failed_seller(c, aid, BUYINGSTORE_TRADE_SELLER_COUNT, item.itemId);
            return;
        }
        // buyer does not have enough space for this item
        if inv::pc_checkadditem(c, account_id, item.itemId, item.amount as i32) == inv::CHKADDITEM_OVERAMOUNT {
            clif_buyingstore_trade_failed_seller(c, aid, BUYINGSTORE_TRADE_SELLER_FAILED, item.itemId);
            return;
        }
        // normally this is not supposed to happen, as the total weight is checked upon creation, but the buyer could
        // have gained items
        let w = inv::data_of(it).weight;
        if (item.amount as u32).wrapping_mul(w) > pl_st.max_weight.wrapping_sub(weight) {
            clif_buyingstore_trade_failed_seller(c, aid, BUYINGSTORE_TRADE_SELLER_FAILED, item.itemId);
            return;
        }
        weight = weight.wrapping_add((item.amount as u32).wrapping_mul(w));
        // buyer does not have enough zeny
        if (item.amount as i32).wrapping_mul(pl_b.items[listidx].price) > pl_b.zenylimit.wrapping_sub(zeny) {
            clif_buyingstore_trade_failed_seller(c, aid, BUYINGSTORE_TRADE_SELLER_ZENY, item.itemId);
            return;
        }
        zeny = zeny.wrapping_add((item.amount as i32).wrapping_mul(pl_b.items[listidx].price));
    }
    // process item list
    for item in itemlist {
        let mut pl_b = bs(c, account_id);
        let Some(listidx) = pl_b.items[..slots].iter().position(|e| e.nameid == item.itemId) else { continue };
        let zeny = (item.amount as i32).wrapping_mul(pl_b.items[listidx].price);
        let index = item.index as i32 - 2;
        // move item (pc_additem receives the seller's slot itself)
        if let Some(r) = inv::inv_get(c, aid, index as usize) {
            let mut it = r.item.clone();
            inv::pc_additem(c, account_id, &mut it, item.amount as i32, crate::map_log::LOG_TYPE_BUYING_STORE);
            if it.unique_id != r.item.unique_id {
                if let Some(mut r2) = inv::inv_get(c, aid, index as usize) {
                    r2.item.unique_id = it.unique_id;
                    inv::inv_put(c, &r2);
                }
            }
        }
        inv::pc_delitem(c, aid, index, item.amount as i32, 1, 0, crate::map_log::LOG_TYPE_BUYING_STORE);
        pl_b.items[listidx].amount -= item.amount;
        // UPDATE / DELETE of the `buyingstore_items` row (autotrade.rs)
        crate::autotrade::buyingstore_sql_item(c, pl_b.buyer_id, listidx, pl_b.items[listidx].amount);
        put(c, &pl_b);
        // pay up
        inv::pc_payzeny(c, account_id, zeny as i64, crate::map_log::LOG_TYPE_BUYING_STORE, crate::map_log::char_id_of(c, aid));
        inv::pc_getzeny(c, aid, zeny as i64, crate::map_log::LOG_TYPE_BUYING_STORE, crate::map_log::char_id_of(c, account_id));
        let mut pl_b = bs(c, account_id);
        pl_b.zenylimit -= zeny;
        put(c, &pl_b);
        // notify clients
        let price = pl_b.items[listidx].price;
        clif_buyingstore_delete_item(c, aid, index, item.amount, price);
        let seller_cid = pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
        clif_buyingstore_update_item(c, account_id, item.itemId, item.amount, seller_cid, zeny);
    }
    if CHARSAVE_VENDING {
        chrif_save_normal_inventory(c, aid);
        chrif_save_normal_inventory(c, account_id);
    }
    // check whether or not there is still something to buy
    let pl_b = bs(c, account_id);
    if !pl_b.items[..slots].iter().any(|e| e.amount != 0) {
        // everything was bought
        clif_buyingstore_trade_failed_buyer(c, account_id, BUYINGSTORE_TRADE_BUYER_NO_ITEMS);
    } else if pl_b.zenylimit == 0 {
        // zeny limit reached
        clif_buyingstore_trade_failed_buyer(c, account_id, BUYINGSTORE_TRADE_BUYER_ZENY);
    } else {
        // continue buying: UPDATE `buyingstores` SET `limit` (autotrade.rs)
        crate::autotrade::buyingstore_sql_limit(c, pl_b.buyer_id, pl_b.zenylimit);
        return;
    }
    // cannot continue buying
    buyingstore_close(c, account_id);
    // remove auto-trader
    if crate::autotrade::autotrade(c, account_id) != 0 {
        pc::quit(c, account_id);
    }
}

/// chrif_save(sd, CSAVE_NORMAL | CSAVE_INVENTORY) (chrif.rs).
fn chrif_save_normal_inventory(c: &Ctx, aid: u32) {
    crate::chrif::chrif_save(c, aid, crate::chrif::CSAVE_NORMAL | crate::chrif::CSAVE_INVENTORY);
}

/// buyingstore.cpp buyingstore_search: whether `aid`'s store still buys `nameid`.
pub fn buyingstore_search(c: &Ctx, aid: u32, nameid: u32) -> bool {
    if !is_buying(c, aid) {
        return false; // not buying
    }
    let b = bs(c, aid);
    b.items[..b.slots as usize].iter().any(|e| e.nameid == nameid && e.amount != 0)
}

/// buyingstore.cpp buyingstore_searchall: the store's items matching a search go into the searcher's results;
/// false when the result set is full (the search stops).
pub fn buyingstore_searchall(c: &Ctx, aid: u32, s: &crate::searchstore::SearchStoreSearch, results: &mut Vec<crate::searchstore::SearchStoreItem>) -> bool {
    if !is_buying(c, aid) {
        return true; // not buying
    }
    let b = bs(c, aid);
    for &nameid in &s.itemlist {
        let Some(it) = b.items[..b.slots as usize].iter().find(|e| e.nameid == nameid && e.amount != 0) else { continue };
        if s.min_price != 0 && s.min_price > it.price as u32 {
            continue; // too low price
        }
        if s.max_price != 0 && s.max_price < it.price as u32 {
            continue; // too high price
        }
        // (s->card_count: ignore cards, as there cannot be any)
        // Check if the result set is full
        if results.len() >= crate::searchstore::searchstore_maxresults() {
            return false;
        }
        results.push(crate::searchstore::SearchStoreItem {
            store_id: b.buyer_id,
            account_id: aid,
            store_name: b.message.clone(),
            nameid: it.nameid,
            amount: it.amount,
            price: it.price,
            card: vec![0; 4],
            refine: 0,
            enchantgrade: 0,
        });
    }
    true
}

/// buyingstore.cpp buyingstore_update: the SQL mirror's position update (autotrade.rs; buyingstore_reopen,
/// do_init_buyingstore_autotrade and buyingstore_autotrader_* are there too).
pub fn buyingstore_update(c: &Ctx, aid: u32) {
    crate::autotrade::buyingstore_update(c, aid);
}

/// pc_setpos's buying store checks: on a map change to a nobuyingstore map the store closes (msg 276); on a
/// nobuyingstore cell it closes (msg 204). Called with the target map and cell.
pub fn pc_setpos_checks(c: &Ctx, aid: u32, changemap: bool, m: u16) {
    if !is_buying(c, aid) {
        return;
    }
    let Some(sd) = unit::get(c, aid) else { return };
    if changemap && crate::npc::mapflag(c, m, MF_NOBUYINGSTORE) {
        say(c, &sd, 276); // "You can't open a shop on this map"
        buyingstore_close(c, aid);
    }
}

// ---------------------------------------------------------------------------------------------- clif

fn send_self(c: &Ctx, aid: u32, b: &[u8]) {
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, b, &sd, Target::SelfOnly);
    }
}

/// clif.cpp clif_buyingstore_open: ZC_OPEN_BUYING_STORE (0x0810 <slots>.B).
fn clif_buyingstore_open(c: &Ctx, aid: u32) {
    let mut w = 0x0810u16.to_le_bytes().to_vec();
    w.push(bs(c, aid).slots);
    send_self(c, aid, &w);
}

/// clif.cpp clif_buyingstore_open_failed: ZC_FAILED_OPEN_BUYING_STORE_TO_BUYER (0x0812 <result>.W <weight>.L).
fn clif_buyingstore_open_failed(c: &Ctx, aid: u32, result: u16, weight: u32) {
    let mut w = 0x0812u16.to_le_bytes().to_vec();
    w.extend_from_slice(&result.to_le_bytes());
    w.extend_from_slice(&weight.to_le_bytes());
    send_self(c, aid, &w);
}

/// clif.cpp clif_buyingstore_myitemlist: ZC_MYITEMLIST_BUYING_STORE (0x0813).
fn clif_buyingstore_myitemlist(c: &Ctx, aid: u32) {
    let b = bs(c, aid);
    let items: Vec<PACKET_ZC_MYITEMLIST_BUYING_STORE_sub> = b.items[..b.slots as usize]
        .iter()
        .map(|e| PACKET_ZC_MYITEMLIST_BUYING_STORE_sub { price: e.price as u32, amount: e.amount, itemType: crate::clif_items::itemtype(e.nameid), itemId: itemdb::client_nameid(e.nameid) })
        .collect();
    let p = PACKET_ZC_MYITEMLIST_BUYING_STORE {
        packetType: PACKET_ZC_MYITEMLIST_BUYING_STORE::ID as i16,
        packetLength: (PACKET_ZC_MYITEMLIST_BUYING_STORE::SIZE + items.len() * PACKET_ZC_MYITEMLIST_BUYING_STORE_sub::SIZE) as i16,
        AID: aid,
        zenyLimit: b.zenylimit as u32,
        items,
    };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_buyingstore_entry: ZC_BUYING_STORE_ENTRY (0x0814) to the area (without the owner), or to one
/// player coming into view (`to`).
pub fn clif_buyingstore_entry(c: &Ctx, aid: u32, to: Option<&Unit>) {
    let Some(sd) = unit::get(c, aid) else { return };
    let p = PACKET_ZC_BUYING_STORE_ENTRY { packetType: PACKET_ZC_BUYING_STORE_ENTRY::ID as i16, makerAID: aid, storeName: bs(c, aid).message };
    match to {
        None => clif::send(c, &p.to_vec(), &sd, Target::AreaWos),
        Some(t) => clif::send(c, &p.to_vec(), t, Target::SelfOnly),
    }
}

/// clif.cpp clif_buyingstore_disappear_entry: ZC_DISAPPEAR_BUYING_STORE_ENTRY (0x0816), area or one player.
pub fn clif_buyingstore_disappear_entry(c: &Ctx, aid: u32, to: Option<&Unit>) {
    let Some(sd) = unit::get(c, aid) else { return };
    let p = PACKET_ZC_DISAPPEAR_BUYING_STORE_ENTRY { packetType: PACKET_ZC_DISAPPEAR_BUYING_STORE_ENTRY::ID as i16, makerAID: aid };
    match to {
        None => clif::send(c, &p.to_vec(), &sd, Target::AreaWos),
        Some(t) => clif::send(c, &p.to_vec(), t, Target::SelfOnly),
    }
}

/// clif.cpp clif_buyingstore_itemlist: ZC_ACK_ITEMLIST_BUYING_STORE (0x0818) of `pl`'s store for `aid`.
fn clif_buyingstore_itemlist(c: &Ctx, aid: u32, pl: u32) {
    let b = bs(c, pl);
    let items: Vec<PACKET_ZC_ACK_ITEMLIST_BUYING_STORE_sub> = b.items[..b.slots as usize]
        .iter()
        .map(|e| PACKET_ZC_ACK_ITEMLIST_BUYING_STORE_sub { price: e.price as u32, amount: e.amount, itemType: crate::clif_items::itemtype(e.nameid), itemId: itemdb::client_nameid(e.nameid) })
        .collect();
    let p = PACKET_ZC_ACK_ITEMLIST_BUYING_STORE {
        packetType: PACKET_ZC_ACK_ITEMLIST_BUYING_STORE::ID as i16,
        packetLength: (PACKET_ZC_ACK_ITEMLIST_BUYING_STORE::SIZE + items.len() * PACKET_ZC_ACK_ITEMLIST_BUYING_STORE_sub::SIZE) as i16,
        AID: pl,
        storeId: b.buyer_id,
        zenyLimit: b.zenylimit as u32,
        items,
    };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_buyingstore_trade_failed_buyer: ZC_FAILED_TRADE_BUYING_STORE_TO_BUYER (0x081a <result>.W).
fn clif_buyingstore_trade_failed_buyer(c: &Ctx, aid: u32, result: i16) {
    let mut w = 0x081au16.to_le_bytes().to_vec();
    w.extend_from_slice(&result.to_le_bytes());
    send_self(c, aid, &w);
}

/// clif.cpp clif_buyingstore_update_item: ZC_UPDATE_ITEM_FROM_BUYING_STORE2 (0x09e6, PACKETVER >= 20141016).
fn clif_buyingstore_update_item(c: &Ctx, aid: u32, nameid: u32, amount: u16, char_id: u32, zeny: i32) {
    let p = PACKET_ZC_UPDATE_ITEM_FROM_BUYING_STORE {
        packetType: 0x09e6,
        itemId: itemdb::client_nameid(nameid),
        amount,
        zeny: zeny as u32,
        zenyLimit: bs(c, aid).zenylimit as u32,
        charId: char_id, // GID
        updateTime: (c.tick() / 1000) as u32,
    };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_buyingstore_delete_item: ZC_ITEM_DELETE_BUYING_STORE (0x081c <index>.W <amount>.W <price>.L),
/// instead of clif_delitem.
fn clif_buyingstore_delete_item(c: &Ctx, aid: u32, index: i32, amount: u16, price: i32) {
    let mut w = 0x081cu16.to_le_bytes().to_vec();
    w.extend_from_slice(&((index + 2) as u16).to_le_bytes());
    w.extend_from_slice(&amount.to_le_bytes());
    w.extend_from_slice(&price.to_le_bytes()); // price per item, client calculates total Zeny by itself
    send_self(c, aid, &w);
}

/// clif.cpp clif_buyingstore_trade_failed_seller: ZC_FAILED_TRADE_BUYING_STORE_TO_SELLER (0x0824).
fn clif_buyingstore_trade_failed_seller(c: &Ctx, aid: u32, result: u16, nameid: u32) {
    let p = PACKET_ZC_FAILED_TRADE_BUYING_STORE_TO_SELLER { packetType: PACKET_ZC_FAILED_TRADE_BUYING_STORE_TO_SELLER::ID as i16, result, itemId: itemdb::client_nameid(nameid) };
    send_self(c, aid, &p.to_vec());
}

// ---------------------------------------------------------------------------------------------- parsers

/// clif.cpp clif_parse_ReqOpenBuyingStore (CZ_REQ_OPEN_BUYING_STORE 0x0811).
pub fn parse_req_open_buying_store(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_REQ_OPEN_BUYING_STORE::read(&mut Reader::new(&pkt[..pkt.len().min(PACKET_CZ_REQ_OPEN_BUYING_STORE::SIZE)])) else { return };
    let len = p.packetLength as u16 as usize;
    // minimum packet length
    if len < PACKET_CZ_REQ_OPEN_BUYING_STORE::SIZE {
        log::error!("clif_parse_ReqOpenBuyingStore: Malformed packet (expected length={}, length={len}, account_id={aid}).", PACKET_CZ_REQ_OPEN_BUYING_STORE::SIZE);
        return;
    }
    let Some(sd) = unit::get(c, aid) else { return };
    if crate::npc::mapflag(c, sd.map, MF_NOBUYINGSTORE) {
        say(c, &sd, 276); // "You can't open a shop on this map"
        return;
    }
    if cell_nobuyingstore(c, &sd) {
        say(c, &sd, 204); // "You can't open a shop on this cell."
        return;
    }
    // safestrncpy(storename, p->storeName, sizeof(storename))
    let raw = pkt.get(9..9 + MESSAGE_SIZE).unwrap_or(&[]);
    let storename = from_latin1(until_nul(&raw[..raw.len().min(MESSAGE_SIZE - 1)]));
    // so that buyingstore_create knows, how many elements it has access to
    let packet_len = len - PACKET_CZ_REQ_OPEN_BUYING_STORE::SIZE;
    if packet_len % PACKET_CZ_REQ_OPEN_BUYING_STORE_sub::SIZE != 0 {
        log::error!("clif_parse_ReqOpenBuyingStore: Unexpected item list size {packet_len} (account_id={aid}).");
        return;
    }
    let mut r = Reader::at(pkt, PACKET_CZ_REQ_OPEN_BUYING_STORE::SIZE);
    let mut items = Vec::new();
    for _ in 0..packet_len / PACKET_CZ_REQ_OPEN_BUYING_STORE_sub::SIZE {
        let Some(it) = PACKET_CZ_REQ_OPEN_BUYING_STORE_sub::read(&mut r) else { return };
        items.push(it);
    }
    buyingstore_create(c, aid, p.zenyLimit as i32, p.result, &storename, &items);
}

/// clif.cpp clif_parse_ReqCloseBuyingStore (CZ_REQ_CLOSE_BUYING_STORE 0x0815).
pub fn parse_req_close_buying_store(c: &Ctx, aid: u32) {
    buyingstore_close(c, aid);
}

/// clif.cpp clif_parse_ReqClickBuyingStore (CZ_REQ_CLICK_TO_BUYING_STORE 0x0817 <account id>.L).
pub fn parse_req_click_buying_store(c: &Ctx, aid: u32, pkt: &[u8]) {
    let pos = packet_pos(u16::from_le_bytes([pkt[0], pkt[1]]));
    let account_id = Reader::at(pkt, pos.first().copied().unwrap_or(2) as usize).u32().unwrap_or(0);
    buyingstore_open(c, aid, account_id);
}

/// clif.cpp clif_parse_ReqTradeBuyingStore (CZ_REQ_TRADE_BUYING_STORE 0x0819).
pub fn parse_req_trade_buying_store(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_REQ_TRADE_BUYING_STORE::read(&mut Reader::new(&pkt[..pkt.len().min(PACKET_CZ_REQ_TRADE_BUYING_STORE::SIZE)])) else { return };
    let len = p.packetLength as u16 as usize;
    if len < PACKET_CZ_REQ_TRADE_BUYING_STORE::SIZE {
        log::error!("clif_parse_ReqTradeBuyingStore: Malformed packet (length={len}, account_id={aid}).");
        return;
    }
    let packet_len = len - PACKET_CZ_REQ_TRADE_BUYING_STORE::SIZE;
    if packet_len % PACKET_CZ_REQ_TRADE_BUYING_STORE_sub::SIZE != 0 {
        log::error!("clif_parse_ReqTradeBuyingStore: Unexpected item list size {packet_len} (account_id={aid}, buyer_id={}).", p.AID);
        return;
    }
    let mut r = Reader::at(pkt, PACKET_CZ_REQ_TRADE_BUYING_STORE::SIZE);
    let mut items = Vec::new();
    for _ in 0..packet_len / PACKET_CZ_REQ_TRADE_BUYING_STORE_sub::SIZE {
        let Some(it) = PACKET_CZ_REQ_TRADE_BUYING_STORE_sub::read(&mut r) else { return };
        items.push(it);
    }
    buyingstore_trade(c, aid, p.AID, p.storeId, &items);
}
