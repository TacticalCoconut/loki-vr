//! map/searchstore.cpp (the store search window opened by catalog items through the `searchstores` script command)
//! and its clif.cpp packets, for buying stores (buyingstore.rs) and vending shops (vending.rs).
//!
//! sd->searchstore is the [`PcSearchStore`] row of an online character.

use spacetimedb::{table, SpacetimeType, Table};

use crate::clif::{self, Target};
use crate::db::items as itemdb;
use crate::net::packets::*;
use crate::net::wire::Reader;
use crate::pc;
use crate::unit;
use crate::world::Ctx;

/// searchstore.hpp SEARCHSTORE_RESULTS_PER_PAGE; conf/battle/misc.conf `searchstore_querydelay: 10`,
/// `searchstore_maxresults: 30`; conf/battle/feature.conf `feature.search_stores: on`; conf/battle/items.conf
/// `vending_max_value: 1000000000`.
pub const SEARCHSTORE_RESULTS_PER_PAGE: usize = 10;
pub fn searchstore_querydelay() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::SEARCHSTORE_QUERYDELAY) as i64
}
pub fn searchstore_maxresults() -> usize {
    crate::battle_config::get(crate::battle_config::Bc::SEARCHSTORE_MAXRESULTS) as usize
}
pub fn feature_search_stores() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_SEARCH_STORES) != 0
}
fn vending_max_value() -> u32 {
    crate::battle_config::get(crate::battle_config::Bc::VENDING_MAX_VALUE) as u32
}

/// searchstore.hpp e_searchstore_failure.
pub const SSI_FAILED_NOTHING_SEARCH_ITEM: u8 = 0;
pub const SSI_FAILED_OVER_MAXCOUNT: u8 = 1;
pub const SSI_FAILED_SEARCH_CNT: u8 = 2;
pub const SSI_FAILED_LIMIT_SEARCH_TIME: u8 = 3;
pub const SSI_FAILED_SSILIST_CLICK_TO_OPEN_STORE: u8 = 4;
/// searchstore.hpp e_searchstore_searchtype / e_searchstore_effecttype.
pub const SEARCHTYPE_VENDING: u8 = 0;
pub const SEARCHTYPE_BUYING_STORE: u8 = 1;
pub const SEARCHSTORE_EFFECT_NORMAL: u16 = 0;
pub const SEARCHSTORE_EFFECT_REMOTE: u16 = 1;
pub const SEARCHSTORE_EFFECT_MAX: u16 = 2;

/// searchstore.hpp struct s_search_store_info_item.
#[derive(SpacetimeType, Clone, Debug, PartialEq)]
pub struct SearchStoreItem {
    pub store_id: u32,
    pub account_id: u32,
    pub store_name: String,
    pub nameid: u32,
    pub amount: u16,
    pub price: i32,
    pub card: Vec<u32>,
    pub refine: u8,
    pub enchantgrade: u8,
}

/// searchstore.hpp struct s_search_store_search (the query).
pub struct SearchStoreSearch {
    pub itemlist: Vec<u32>,
    pub cardlist: Vec<u32>,
    pub min_price: u32,
    pub max_price: u32,
}

/// sd->searchstore.
#[table(accessor = pc_searchstore)]
#[derive(Clone, Debug)]
pub struct PcSearchStore {
    #[primary_key]
    pub account_id: u32,
    pub items: Vec<SearchStoreItem>,
    pub pages: u32,
    /// time_t of the next allowed query.
    pub nextquerytime: i64,
    pub uses: u16,
    pub kind: u8,
    pub effect: u16,
    pub mapid: i16,
    pub open: bool,
    pub remote_id: u32,
}

fn ss(c: &Ctx, aid: u32) -> PcSearchStore {
    c.db().pc_searchstore().account_id().find(aid).unwrap_or(PcSearchStore {
        account_id: aid,
        items: Vec::new(),
        pages: 0,
        nextquerytime: 0,
        uses: 0,
        kind: 0,
        effect: 0,
        mapid: 0,
        open: false,
        remote_id: 0,
    })
}

fn put(c: &Ctx, s: &PcSearchStore) {
    if c.db().pc_searchstore().account_id().find(s.account_id).is_some() {
        c.db().pc_searchstore().account_id().update(s.clone());
    } else {
        c.db().pc_searchstore().insert(s.clone());
    }
}

/// unit_free: the state goes with the session.
pub fn on_unit_free(c: &Ctx, aid: u32) {
    c.db().pc_searchstore().account_id().delete(aid);
}

/// searchstore_hasstore.
fn searchstore_hasstore(c: &Ctx, aid: u32, kind: u8) -> bool {
    let Some(p) = pc::get(c, aid) else { return false };
    match kind {
        SEARCHTYPE_VENDING => p.vending,
        SEARCHTYPE_BUYING_STORE => p.buyingstore,
        _ => false,
    }
}

/// searchstore_getstoreid.
fn searchstore_getstoreid(c: &Ctx, aid: u32, kind: u8) -> u32 {
    match kind {
        SEARCHTYPE_VENDING => crate::vending::vender_id(c, aid),
        SEARCHTYPE_BUYING_STORE => crate::buyingstore::bs(c, aid).buyer_id,
        _ => 0,
    }
}

/// searchstore_getsearchfunc for SEARCHTYPE_VENDING: vending.cpp vending_search (vending.rs).
fn vending_search(c: &Ctx, aid: u32, nameid: u32) -> bool {
    crate::vending::vending_search(c, aid, nameid)
}

/// searchstore_getsearchallfunc for SEARCHTYPE_VENDING: vending.cpp vending_searchall (vending.rs).
fn vending_searchall(c: &Ctx, aid: u32, s: &SearchStoreSearch, results: &mut Vec<SearchStoreItem>) -> bool {
    crate::vending::vending_searchall(c, aid, s, results)
}

/// searchstore.cpp searchstore_open: the window opens with `uses` searches.
pub fn searchstore_open(c: &Ctx, aid: u32, uses: u16, effect: u16, mapid: i16) -> bool {
    let mut s = ss(c, aid);
    if s.open {
        return false;
    }
    s.open = true;
    s.uses = uses;
    s.effect = effect;
    s.mapid = mapid;
    put(c, &s);
    clif_open_search_store_info(c, aid);
    true
}

/// searchstore.cpp searchstore_query.
pub fn searchstore_query(c: &Ctx, aid: u32, kind: u8, min_price: u32, max_price: u32, itemlist: &[u32], cardlist: &[u32]) {
    let mut s = ss(c, aid);
    if !s.open {
        return;
    }
    if kind != SEARCHTYPE_VENDING && kind != SEARCHTYPE_BUYING_STORE {
        log::error!("searchstore_query: Unknown search type {kind} (account_id={aid}).");
        return;
    }
    let querytime = c.tick() / 1000;
    if s.nextquerytime > querytime {
        clif_search_store_info_failed(c, aid, SSI_FAILED_LIMIT_SEARCH_TIME);
        return;
    }
    if s.uses == 0 {
        clif_search_store_info_failed(c, aid, SSI_FAILED_SEARCH_CNT);
        return;
    }
    // uses counter must be updated before validating the next search
    s.uses -= 1;
    s.kind = kind;
    s.nextquerytime = querytime + searchstore_querydelay();
    put(c, &s);
    // drop previous results
    searchstore_clear(c, aid);
    // validate lists
    for &id in itemlist {
        if itemdb::itemdb_exists(id).is_none() {
            log::warn!("searchstore_query: Client resolved item {id} is not known.");
            clif_search_store_info_failed(c, aid, SSI_FAILED_NOTHING_SEARCH_ITEM);
            clif_search_store_info_ack(c, aid); // update uses
            return;
        }
    }
    for &id in cardlist {
        if itemdb::itemdb_exists(id).is_none() {
            log::warn!("searchstore_query: Client resolved card {id} is not known.");
            clif_search_store_info_failed(c, aid, SSI_FAILED_NOTHING_SEARCH_ITEM);
            clif_search_store_info_ack(c, aid); // update uses
            return;
        }
    }
    let (min_price, max_price) = if max_price < min_price { (max_price, min_price) } else { (min_price, max_price) };
    // search
    let query = SearchStoreSearch { itemlist: itemlist.to_vec(), cardlist: cardlist.to_vec(), min_price, max_price };
    let stores: Vec<u32> = if kind == SEARCHTYPE_VENDING {
        // vending_getdb(): the vendors
        crate::vending::vending_db(c)
    } else {
        crate::buyingstore::buyingstore_db(c)
    };
    let mapid = s.mapid;
    let mut results: Vec<SearchStoreItem> = Vec::new();
    for pl in stores {
        if pl == aid {
            continue; // skip own shop, if any
        }
        // Skip stores that are not in the map defined by the search
        if mapid != 0 && unit::get(c, pl).map(|u| u.map as i16 != mapid).unwrap_or(true) {
            continue;
        }
        let more = if kind == SEARCHTYPE_VENDING { vending_searchall(c, pl, &query, &mut results) } else { crate::buyingstore::buyingstore_searchall(c, pl, &query, &mut results) };
        if !more {
            // exceeded result size
            clif_search_store_info_failed(c, aid, SSI_FAILED_OVER_MAXCOUNT);
            break;
        }
    }
    let mut s = ss(c, aid);
    s.items = results;
    put(c, &s);
    if !s.items.is_empty() {
        // present results
        clif_search_store_info_ack(c, aid);
        // one page displayed
        let mut s = ss(c, aid);
        s.pages += 1;
        put(c, &s);
    } else {
        // cleanup
        searchstore_clear(c, aid);
        // notify of failure (must go before updating uses)
        clif_search_store_info_failed(c, aid, SSI_FAILED_NOTHING_SEARCH_ITEM);
        // update uses
        clif_search_store_info_ack(c, aid);
    }
}

/// searchstore.cpp searchstore_querynext.
fn searchstore_querynext(s: &PcSearchStore) -> bool {
    !s.items.is_empty() && (s.items.len() - 1) / SEARCHSTORE_RESULTS_PER_PAGE > s.pages as usize
}

/// searchstore.cpp searchstore_next.
pub fn searchstore_next(c: &Ctx, aid: u32) {
    let s = ss(c, aid);
    if !s.open || s.items.len() <= s.pages as usize * SEARCHSTORE_RESULTS_PER_PAGE {
        return; // nothing (more) to display
    }
    // present results
    clif_search_store_info_ack(c, aid);
    // one more page displayed
    let mut s = ss(c, aid);
    s.pages += 1;
    put(c, &s);
}

/// searchstore.cpp searchstore_clear.
pub fn searchstore_clear(c: &Ctx, aid: u32) {
    searchstore_clearremote(c, aid);
    let mut s = ss(c, aid);
    s.items.clear();
    s.pages = 0;
    put(c, &s);
}

/// searchstore.cpp searchstore_close (also unit_remove_map's call).
pub fn searchstore_close(c: &Ctx, aid: u32) {
    if ss(c, aid).open {
        searchstore_clear(c, aid);
        let mut s = ss(c, aid);
        s.uses = 0;
        s.open = false;
        put(c, &s);
    }
}

/// searchstore.cpp searchstore_click: a result is clicked (show the store's position, or open it remotely).
pub fn searchstore_click(c: &Ctx, aid: u32, account_id: u32, store_id: u32, nameid: u32) {
    let s = ss(c, aid);
    if !s.open || s.items.is_empty() {
        return;
    }
    searchstore_clearremote(c, aid);
    if !s.items.iter().any(|it| it.store_id == store_id && it.account_id == account_id && it.nameid == nameid) {
        // no such result, crafted
        log::warn!("searchstore_click: Received request with item {nameid} of account {account_id}, which is not part of current result set (account_id={aid}).");
        clif_search_store_info_failed(c, aid, SSI_FAILED_SSILIST_CLICK_TO_OPEN_STORE);
        return;
    }
    if pc::get(c, account_id).is_none() {
        // no longer online
        clif_search_store_info_failed(c, aid, SSI_FAILED_SSILIST_CLICK_TO_OPEN_STORE);
        return;
    }
    if !searchstore_hasstore(c, account_id, s.kind) || searchstore_getstoreid(c, account_id, s.kind) != store_id {
        // no longer vending/buying or not same shop
        clif_search_store_info_failed(c, aid, SSI_FAILED_SSILIST_CLICK_TO_OPEN_STORE);
        return;
    }
    let still = if s.kind == SEARCHTYPE_VENDING { vending_search(c, account_id, nameid) } else { crate::buyingstore::buyingstore_search(c, account_id, nameid) };
    if !still {
        // item no longer being sold/bought
        clif_search_store_info_failed(c, aid, SSI_FAILED_SSILIST_CLICK_TO_OPEN_STORE);
        return;
    }
    match s.effect {
        SEARCHSTORE_EFFECT_NORMAL => {
            // display coords
            let (Some(sd), Some(pl)) = (unit::get(c, aid), unit::get(c, account_id)) else { return };
            if sd.map != pl.map {
                clif_search_store_info_click_ack(c, aid, -1, -1); // not on same map, wipe previous marker
            } else {
                clif_search_store_info_click_ack(c, aid, pl.x, pl.y);
            }
        }
        SEARCHSTORE_EFFECT_REMOTE => {
            // open remotely: to bypass range checks
            let mut s2 = ss(c, aid);
            s2.remote_id = account_id;
            put(c, &s2);
            match s.kind {
                SEARCHTYPE_VENDING => crate::vending::vending_vendinglistreq(c, aid, account_id),
                _ => crate::buyingstore::buyingstore_open(c, aid, account_id),
            }
        }
        e => log::error!("searchstore_click: Unknown search store effect {e} (account_id={aid})."),
    }
}

/// searchstore.cpp searchstore_queryremote: `aid` opened `account_id`'s store remotely.
pub fn searchstore_queryremote(c: &Ctx, aid: u32, account_id: u32) -> bool {
    let s = ss(c, aid);
    s.open && !s.items.is_empty() && s.remote_id == account_id
}

/// searchstore.cpp searchstore_clearremote.
pub fn searchstore_clearremote(c: &Ctx, aid: u32) {
    if let Some(mut s) = c.db().pc_searchstore().account_id().find(aid) {
        if s.remote_id != 0 {
            s.remote_id = 0;
            put(c, &s);
        }
    }
}

// ---------------------------------------------------------------------------------------------- clif

fn send_self(c: &Ctx, aid: u32, b: &[u8]) {
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, b, &sd, Target::SelfOnly);
    }
}

/// clif.cpp clif_search_store_info_ack: ZC_SEARCH_STORE_INFO_ACK (0x0b64), one page.
fn clif_search_store_info_ack(c: &Ctx, aid: u32) {
    if !feature_search_stores() {
        return;
    }
    let s = ss(c, aid);
    let start = s.pages as usize * SEARCHSTORE_RESULTS_PER_PAGE;
    let end = s.items.len().min(start + SEARCHSTORE_RESULTS_PER_PAGE);
    let mut items = Vec::new();
    for it in s.items.iter().take(end).skip(start) {
        // make-up an item for clif_addcards
        let mut made = crate::inventory::Item::new(it.nameid);
        for (j, &cd) in it.card.iter().enumerate().take(4) {
            made.set_card(j, cd);
        }
        made.amount = it.amount as i16;
        items.push(PACKET_ZC_SEARCH_STORE_INFO_ACK_sub {
            storeId: it.store_id,
            AID: it.account_id,
            shopName: it.store_name.clone(),
            itemId: itemdb::client_nameid(it.nameid),
            itemType: crate::clif_items::itemtype(it.nameid),
            price: it.price as u32,
            amount: it.amount,
            slot: crate::clif_items::addcards(Some(&made)),
            option_data: crate::clif_items::add_random_options(&made),
            refine: it.refine,
            grade: it.enchantgrade,
        });
    }
    let p = PACKET_ZC_SEARCH_STORE_INFO_ACK {
        packetType: PACKET_ZC_SEARCH_STORE_INFO_ACK::ID as i16,
        packetLength: (PACKET_ZC_SEARCH_STORE_INFO_ACK::SIZE + items.len() * PACKET_ZC_SEARCH_STORE_INFO_ACK_sub::SIZE) as i16,
        firstPage: (s.pages == 0) as u8,
        nextPage: searchstore_querynext(&s) as u8,
        usesCount: s.uses.min(u8::MAX as u16) as u8,
        items,
    };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_search_store_info_failed: ZC_SEARCH_STORE_INFO_FAILED (0x0837).
fn clif_search_store_info_failed(c: &Ctx, aid: u32, reason: u8) {
    if !feature_search_stores() {
        return;
    }
    let p = PACKET_ZC_SEARCH_STORE_INFO_FAILED { packetType: PACKET_ZC_SEARCH_STORE_INFO_FAILED::ID as i16, reason };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_open_search_store_info: ZC_OPEN_SEARCH_STORE_INFO (0x083a).
fn clif_open_search_store_info(c: &Ctx, aid: u32) {
    if !feature_search_stores() {
        return;
    }
    let s = ss(c, aid);
    let p = PACKET_ZC_OPEN_SEARCH_STORE_INFO { packetType: PACKET_ZC_OPEN_SEARCH_STORE_INFO::ID as i16, effect: s.effect, remainingUses: s.uses.min(u8::MAX as u16) as u8 };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_search_store_info_click_ack: ZC_SSILIST_ITEM_CLICK_ACK (0x083d).
fn clif_search_store_info_click_ack(c: &Ctx, aid: u32, x: i16, y: i16) {
    if !feature_search_stores() {
        return;
    }
    let p = PACKET_ZC_SSILIST_ITEM_CLICK_ACK { packetType: PACKET_ZC_SSILIST_ITEM_CLICK_ACK::ID as i16, x, y };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_parse_SearchStoreInfo (CZ_SEARCH_STORE_INFO 0x0835).
pub fn parse_search_store_info(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !feature_search_stores() {
        return;
    }
    let Some(p) = PACKET_CZ_SEARCH_STORE_INFO::read(&mut Reader::new(&pkt[..pkt.len().min(PACKET_CZ_SEARCH_STORE_INFO::SIZE)])) else { return };
    let len = p.packetLength as u16 as usize;
    // minimum packet length
    if len < PACKET_CZ_SEARCH_STORE_INFO::SIZE {
        log::error!("clif_parse_SearchStoreInfo: Malformed packet (expected length={}, length={len}, account_id={aid}).", PACKET_CZ_SEARCH_STORE_INFO::SIZE);
        return;
    }
    // check, if there is enough data for the claimed count of items
    let packet_len = len - PACKET_CZ_SEARCH_STORE_INFO::SIZE;
    if packet_len % PACKET_CZ_SEARCH_STORE_INFO_item::SIZE != 0 {
        log::error!("clif_parse_SearchStoreInfo: Unexpected item list size {packet_len} (account_id={aid}).");
        return;
    }
    let count = packet_len / PACKET_CZ_SEARCH_STORE_INFO_item::SIZE;
    if count < p.itemsCount as usize + p.cardsCount as usize {
        log::error!("clif_parse_SearchStoreInfo: Malformed packet (expected count={}, count={count}, account_id={aid}).", p.itemsCount as usize + p.cardsCount as usize);
        return;
    }
    if p.searchType > SEARCHTYPE_BUYING_STORE {
        log::error!("clif_parse_SearchStoreInfo: Invalid search type {} (account_id={aid}).", p.searchType);
        return;
    }
    if p.minPrice > vending_max_value() {
        log::error!("clif_parse_SearchStoreInfo: Invalid min price {} (account_id={aid}).", p.minPrice);
        return;
    }
    if p.maxPrice > vending_max_value() {
        log::error!("clif_parse_SearchStoreInfo: Invalid max price {} (account_id={aid}).", p.maxPrice);
        return;
    }
    let mut r = Reader::at(pkt, PACKET_CZ_SEARCH_STORE_INFO::SIZE);
    let mut ids = Vec::with_capacity(count);
    for _ in 0..count {
        ids.push(r.u32().unwrap_or(0));
    }
    let n = p.itemsCount as usize;
    let m = p.cardsCount as usize;
    searchstore_query(c, aid, p.searchType, p.minPrice, p.maxPrice, &ids[..n], &ids[n..n + m]);
}

/// clif.cpp clif_parse_SearchStoreInfoNextPage (0x0838).
pub fn parse_search_store_info_next_page(c: &Ctx, aid: u32) {
    if !feature_search_stores() {
        return;
    }
    searchstore_next(c, aid);
}

/// clif.cpp clif_parse_CloseSearchStoreInfo (0x083b).
pub fn parse_close_search_store_info(c: &Ctx, aid: u32) {
    if !feature_search_stores() {
        return;
    }
    searchstore_close(c, aid);
}

/// clif.cpp clif_parse_SearchStoreInfoListItemClick (CZ_SSILIST_ITEM_CLICK 0x083c).
pub fn parse_search_store_info_list_item_click(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !feature_search_stores() {
        return;
    }
    let Some(p) = PACKET_CZ_SSILIST_ITEM_CLICK::read(&mut Reader::new(pkt)) else { return };
    searchstore_click(c, aid, p.AID, p.storeId, p.itemId);
}
