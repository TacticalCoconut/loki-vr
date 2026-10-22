//! map/clif.cpp "AUCTION SYSTEM" (clif_Auction_* senders, clif_parse_Auction_* handlers) and map/intif.cpp's auction
//! calls (intif_Auction_* requests, intif_parse_Auction_* answers): the map server's half of the auction system.
//!
//! sd->auction (the inventory slot being registered) is the [`PcAuction`] row of an online character (no row: index 0,
//! amount 0, as the zeroed session starts). The char server's half is int_auction.rs; requests go through intif.rs's
//! queue and their answers arrive after the function that asked (int_auction.rs [`AuctionInter`]).
//!
//! `feature.auction` (conf/battle/feature.conf:26) is off, and battle_adjust_conf turns ANY conf value off for
//! PACKETVER >= 20141112 (battle.cpp:9026-9031), so on this client version the auction can only be switched on at run
//! time, by battle_set_value (@setbattleflag / the setbattleflag script command), which stores the value without
//! battle_adjust_conf (battle.cpp:8918-8934). The three settings are read from the run-time battle_config
//! (battle_config/mod.rs, port wave I); a conf read (@reloadbattleconf, a world build) switches the auction off again.
//!
//! Deliberately different: CheckForCharServer() is never true (the char server is this module), so
//! intif_Auction_* always send (return 1).

use spacetimedb::{table, Table};

use crate::battle_config::{self as bc, Bc};
use crate::clif::{self, Target};
use crate::db::items::{self as itemdb, IT_ARMOR, IT_CARD, IT_ETC, IT_PETARMOR, IT_SHADOWGEAR, IT_WEAPON};
use crate::int_auction::{AuctionData, AuctionInter, ITEM_NAME_LENGTH};
use crate::int_mail::{strncpy, NAME_LENGTH};
use crate::intif::{self, Inter};
use crate::inventory::{self as inv, MAX_INVENTORY};
use crate::map_log::LOG_TYPE_AUCTION;
use crate::net::packets::*;
use crate::net::wire::{put_str, Reader};
use crate::pc;
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// conf/map_athena.conf `save_settings: 4095` has CHARSAVE_AUCTION (0x020: "After successfully submitting an item for
/// auction", map.hpp:1128).
const CHARSAVE_AUCTION: bool = true;

/// Wave G kept the auction's three settings here when battle_set_value changed them. Since port wave I every
/// battle_config member lives in battle_config/mod.rs's tables; this one is no longer read or written. It stays
/// declared only so that a database published before wave I can be updated in place (SpacetimeDB refuses an update
/// that removes a table); drop it with the next change that needs fresh data anyway.
#[table(accessor = battle_config_auction)]
#[derive(Clone, Debug)]
pub struct BattleConfigAuction {
    #[primary_key]
    pub name: String,
    pub value: i32,
}

/// map_session_data::s_auction of an online character.
#[table(accessor = pc_auction)]
#[derive(Clone, Debug)]
pub struct PcAuction {
    #[primary_key]
    pub account_id: u32,
    pub index: i32,
    pub amount: i32,
}

// ---------------------------------------------------------------------------------------------- battle_config

/// battle_config.feature_auction.
pub fn feature_auction(_c: &Ctx) -> i32 {
    bc::get(Bc::FEATURE_AUCTION)
}

/// battle_config.auction_feeperhour.
pub fn auction_feeperhour(_c: &Ctx) -> i32 {
    bc::get(Bc::AUCTION_FEEPERHOUR)
}

/// battle_config.auction_maximumprice.
pub fn auction_maximumprice(_c: &Ctx) -> i32 {
    bc::get(Bc::AUCTION_MAXIMUMPRICE)
}

// ---------------------------------------------------------------------------------------------- session

/// sd->auction.index / sd->auction.amount.
fn sd_auction(c: &Ctx, aid: u32) -> (i32, i32) {
    c.db().pc_auction().account_id().find(aid).map(|a| (a.index, a.amount)).unwrap_or((0, 0))
}

fn set_sd_auction(c: &Ctx, aid: u32, index: i32, amount: i32) {
    let row = PcAuction { account_id: aid, index, amount };
    if c.db().pc_auction().account_id().find(aid).is_some() {
        c.db().pc_auction().account_id().update(row);
    } else {
        c.db().pc_auction().insert(row);
    }
}

/// `sd->auction.amount = 0`.
fn clear_sd_auction_amount(c: &Ctx, aid: u32) {
    let (index, amount) = sd_auction(c, aid);
    if amount != 0 {
        set_sd_auction(c, aid, index, 0);
    }
}

/// unit_free: sd->auction goes with the session.
pub fn on_unit_free(c: &Ctx, aid: u32) {
    c.db().pc_auction().account_id().delete(aid);
}

fn sd_of(c: &Ctx, aid: u32) -> Option<Unit> {
    unit::get(c, aid)
}

fn send_self(c: &Ctx, aid: u32, b: &[u8]) {
    if let Some(sd) = sd_of(c, aid) {
        clif::send(c, b, &sd, Target::SelfOnly);
    }
}

fn to_char(m: AuctionInter) {
    intif::send(Inter::Auction(m));
}

/// map_charid2sd → the account id.
fn charid2aid(c: &Ctx, char_id: u32) -> Option<u32> {
    use crate::pc::pc as _;
    c.db().pc().char_id().find(char_id).map(|p| p.account_id)
}

// ---------------------------------------------------------------------------------------------- clif senders

/// clif.cpp clif_Auction_openwindow: ZC_AUCTION_WINDOWS (0x025f <type>.L, 0 = open). Nothing while a storage, a
/// vending / buying store or a trade is open, and nothing with the feature off.
pub fn clif_auction_openwindow(c: &Ctx, aid: u32) {
    let Some(p) = pc::get(c, aid) else { return };
    if p.storage_flag != 0 || p.vending || p.buyingstore || p.trading {
        return;
    }
    if feature_auction(c) == 0 {
        return;
    }
    let mut w = Vec::with_capacity(6);
    w.extend_from_slice(&0x025fu16.to_le_bytes());
    w.extend_from_slice(&0u32.to_le_bytes());
    send_self(c, aid, &w);
}

/// clif.cpp clif_Auction_results: ZC_AUCTION_ITEM_REQ_SEARCH (0x0252 <packet len>.W <pages>.L <count>.L { <auction
/// id>.L <seller name>.24B <name id>.W <type>.L <amount>.W <identified>.B <damaged>.B <refine>.B <card1>.W <card2>.W
/// <card3>.W <card4>.W <now price>.L <max price>.L <buyer name>.24B <delete time>.L }*). The item id and the cards are
/// 16-bit fields (WFIFOW of the 32-bit ids).
pub fn clif_auction_results(c: &Ctx, aid: u32, count: i16, pages: i16, auctions: &[AuctionData]) {
    let n = (count.max(0) as usize).min(auctions.len());
    let mut w = Vec::with_capacity(12 + n * 83);
    w.extend_from_slice(&0x0252u16.to_le_bytes());
    w.extend_from_slice(&((12 + count as i32 * 83) as u16).to_le_bytes());
    w.extend_from_slice(&(pages as i32).to_le_bytes());
    w.extend_from_slice(&(count as i32).to_le_bytes());
    for auction in auctions.iter().take(n) {
        w.extend_from_slice(&auction.auction_id.to_le_bytes());
        put_str(&mut w, &auction.seller_name, NAME_LENGTH);
        w.extend_from_slice(&(itemdb::client_nameid(auction.item.nameid) as u16).to_le_bytes());
        w.extend_from_slice(&(auction.kind as i32).to_le_bytes());
        w.extend_from_slice(&auction.item.amount.to_le_bytes()); // Always 1
        w.push(auction.item.identify);
        w.push(auction.item.attribute);
        w.push(auction.item.refine);
        for i in 0..4 {
            w.extend_from_slice(&(auction.item.card(i) as u16).to_le_bytes());
        }
        w.extend_from_slice(&auction.price.to_le_bytes());
        w.extend_from_slice(&auction.buynow.to_le_bytes());
        put_str(&mut w, &auction.buyer_name, NAME_LENGTH);
        w.extend_from_slice(&(auction.timestamp as u32).to_le_bytes());
    }
    send_self(c, aid, &w);
}

/// clif.cpp clif_Auction_setitem: ZC_ACK_AUCTION_ADD_ITEM (0x0256 <index>.W <result>.B; 0 = success, 1 = failure).
fn clif_auction_setitem(c: &Ctx, aid: u32, index: i32, fail: bool) {
    let mut w = Vec::with_capacity(5);
    w.extend_from_slice(&0x0256u16.to_le_bytes());
    w.extend_from_slice(&(index as u16).to_le_bytes());
    w.push(fail as u8);
    send_self(c, aid, &w);
}

/// clif.cpp clif_Auction_message: ZC_AUCTION_RESULT (0x0250 <result>.B). 0 = You have failed to bid into the auction,
/// 1 = You have successfully bid in the auction, 2 = The auction has been canceled, 3 = An auction with at least one
/// bidder cannot be canceled, 4 = You cannot register more than 5 items in an auction at a time, 5 = You do not have
/// enough Zeny to pay the Auction Fee, 6 = You have won the auction, 7 = You have failed to win the auction, 8 = You
/// do not have enough Zeny, 9 = You cannot place more than 5 bids at a time.
pub fn clif_auction_message(c: &Ctx, aid: u32, flag: u8) {
    send_self(c, aid, &[0x50, 0x02, flag]);
}

/// clif.cpp clif_Auction_close: ZC_AUCTION_ACK_MY_SELL_STOP (0x025e <result>.W; 0 = You have ended the auction, 1 =
/// You cannot end the auction, 2 = Auction ID is incorrect), which rAthena sends under the id 0x025d with 0x025e's
/// length of 4 bytes ("BUG: The client identifies this packet as 0x25d (CZ_AUCTION_REQ_MY_SELL_STOP)",
/// clif.cpp:17064).
pub fn clif_auction_close(c: &Ctx, aid: u32, flag: u8) {
    let mut w = Vec::with_capacity(4);
    w.extend_from_slice(&0x025du16.to_le_bytes());
    w.extend_from_slice(&(flag as u16).to_le_bytes());
    send_self(c, aid, &w);
}

// ---------------------------------------------------------------------------------------------- clif parsers

/// net/mod.rs's dispatch for the auction packets (false: not one of them).
pub fn parse(c: &Ctx, aid: u32, func: &str, pkt: &[u8]) -> bool {
    match func {
        "clif_parse_Auction_cancelreg" => clif_parse_auction_cancelreg(c, aid),
        "clif_parse_Auction_setitem" => clif_parse_auction_setitem(c, aid, pkt),
        "clif_parse_Auction_register" => clif_parse_auction_register(c, aid, pkt),
        "clif_parse_Auction_cancel" => clif_parse_auction_cancel(c, aid, pkt),
        "clif_parse_Auction_close" => clif_parse_auction_close(c, aid, pkt),
        "clif_parse_Auction_bid" => clif_parse_auction_bid(c, aid, pkt),
        "clif_parse_Auction_search" => clif_parse_auction_search(c, aid, pkt),
        "clif_parse_Auction_buysell" => clif_parse_auction_buysell(c, aid, pkt),
        _ => return false,
    }
    true
}

/// `RFIFOx(fd, packet_db[RFIFOW(fd,0)].pos[i])`: a reader at the packet's i-th field offset.
fn at_pos<'a>(pkt: &'a [u8], i: usize) -> Option<Reader<'a>> {
    let cmd = u16::from_le_bytes([*pkt.first()?, *pkt.get(1)?]);
    let off = *packet_pos(cmd).get(i)? as usize;
    Some(Reader::at(pkt, off))
}

/// clif.cpp clif_parse_Auction_cancelreg (CZ_AUCTION_CREATE 0x024b <type>.W; the type is not read): the item that was
/// set for a new auction shows in the inventory again. (Also called by the search / list requests and by the answers
/// to a bid and a close: "FIXME: Leeching off a parse function".)
pub fn clif_parse_auction_cancelreg(c: &Ctx, aid: u32) {
    let (index, amount) = sd_auction(c, aid);
    if amount > 0 {
        crate::clif_items::additem(c, aid, index, amount, 0);
    }
    clear_sd_auction_amount(c, aid);
}

/// clif.cpp clif_parse_Auction_setitem (CZ_AUCTION_ADD_ITEM 0x024c <index>.W <count>.L).
fn clif_parse_auction_setitem(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(idx) = at_pos(pkt, 0).and_then(|mut r| r.u16()) else { return };
    let Some(amount) = at_pos(pkt, 1).and_then(|mut r| r.u32()) else { return };
    let idx = idx as i32 - 2;
    let amount = amount as i32; // Always 1
    if feature_auction(c) == 0 {
        return;
    }
    clear_sd_auction_amount(c, aid);
    let name = || pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
    if idx < 0 || idx >= MAX_INVENTORY as i32 {
        log::warn!("Character {} trying to set invalid item index in auctions.", name());
        return;
    }
    let slot = inv::inv_get(c, aid, idx as usize);
    // By client, amount is always set to 1. Maybe this is a future implementation.
    if amount != 1 || amount > slot.as_ref().map(|r| r.item.amount as i32).unwrap_or(0) {
        log::warn!("Character {} trying to set invalid amount in auctions.", name());
        return;
    }
    // (an empty slot has amount 0 and left above)
    let Some(slot) = slot else { return };
    let it = &slot.item;
    if let Some(id) = itemdb::itemdb_exists(it.nameid) {
        if !matches!(id.r#type, IT_ARMOR | IT_PETARMOR | IT_WEAPON | IT_CARD | IT_ETC | IT_SHADOWGEAR) {
            // Consumable or pets are not allowed
            clif_auction_setitem(c, aid, idx, true);
            return;
        }
    }
    if !inv::pc_can_give_items(c, aid)
        || it.expire_time != 0
        || it.identify == 0
        || (it.bound != 0 && !inv::pc_can_give_bounded_items(c, aid))
        || !itemdb::itemdb_available(it.nameid)
        || !itemdb::itemdb_isrestricted(it.nameid, &it.cards(), pc::group_level(c, aid), 0, itemdb::itemdb_canauction_sub)
    {
        // Quest Item or something else
        clif_auction_setitem(c, aid, idx, true);
        return;
    }
    set_sd_auction(c, aid, idx, amount);
    clif_auction_setitem(c, aid, idx + 2, false);
}

/// clif.cpp clif_parse_Auction_register (CZ_AUCTION_ADD 0x024d <now money>.L <max money>.L <delete hour>.W).
fn clif_parse_auction_register(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_AUCTION_ADD::read(&mut Reader::new(pkt)) else { return };
    if feature_auction(c) == 0 {
        return;
    }
    let Some(ch) = pc::character_of(c, aid) else { return };
    let mut auction = AuctionData { price: p.now_money as i32, buynow: p.max_money as i32, hours: p.hours, ..Default::default() };
    let (index, amount) = sd_auction(c, aid);
    // Invalid Situations...
    if amount < 1 {
        log::warn!("Character {} trying to register auction without item.", ch.name);
        return;
    }
    if auction.price >= auction.buynow {
        log::warn!("Character {} trying to alter auction prices.", ch.name);
        return;
    }
    if auction.hours < 1 || auction.hours > 48 {
        log::warn!("Character {} trying to enter an invalid time for auction.", ch.name);
        return;
    }
    // Auction checks...
    let slot = inv::inv_get(c, aid, index.max(0) as usize);
    if slot.as_ref().map(|r| r.item.bound != 0).unwrap_or(false) && !inv::pc_can_give_bounded_items(c, aid) {
        if let Some(sd) = sd_of(c, aid) {
            clif::displaymessage(c, &sd, crate::data::msg_txt(293)); // This bounded item cannot be traded to that character.
        }
        clif_auction_message(c, aid, 2); // The auction has been canceled
        return;
    }
    let fee = (auction.hours as i32).wrapping_mul(auction_feeperhour(c));
    if (ch.zeny as i32) < fee {
        clif_auction_message(c, aid, 5); // You do not have enough zeny to pay the Auction Fee.
        return;
    }
    let maximumprice = auction_maximumprice(c);
    if auction.buynow > maximumprice {
        // Zeny Limits
        auction.buynow = maximumprice;
        if auction.price >= auction.buynow {
            auction.price = auction.buynow - 1;
        }
    }
    auction.auction_id = 0;
    auction.seller_id = ch.id;
    auction.seller_name = strncpy(&ch.name, NAME_LENGTH);
    auction.buyer_id = 0;
    auction.buyer_name = String::new();
    let Some(slot) = slot.filter(|r| r.item.nameid != 0 && r.item.amount as i32 >= amount) else {
        clif_auction_message(c, aid, 2); // The auction has been canceled
        return;
    };
    let Some(id) = itemdb::itemdb_exists(slot.item.nameid) else {
        // Just in case
        clif_auction_message(c, aid, 2); // The auction has been canceled
        return;
    };
    auction.item_name = strncpy(&id.ename, ITEM_NAME_LENGTH);
    auction.kind = id.r#type as i16;
    auction.item = slot.item.clone();
    auction.item.amount = 1;
    auction.timestamp = 0;
    if !intif_auction_register(auction) {
        clif_auction_message(c, aid, 4); // No Char Server? lets say something to the client
    } else {
        let zeny = fee;
        inv::pc_delitem(c, aid, index, amount, 1, 6, LOG_TYPE_AUCTION);
        clear_sd_auction_amount(c, aid);
        inv::pc_payzeny(c, aid, zeny as i64, LOG_TYPE_AUCTION, 0);
    }
}

/// clif.cpp clif_parse_Auction_cancel (CZ_AUCTION_ADD_CANCEL 0x024e <auction id>.L).
fn clif_parse_auction_cancel(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(auction_id) = at_pos(pkt, 0).and_then(|mut r| r.u32()) else { return };
    let Some(p) = pc::get(c, aid) else { return };
    intif_auction_cancel(p.char_id, auction_id);
}

/// clif.cpp clif_parse_Auction_close (CZ_AUCTION_REQ_MY_SELL_STOP 0x025d <auction id>.L).
fn clif_parse_auction_close(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(auction_id) = at_pos(pkt, 0).and_then(|mut r| r.u32()) else { return };
    let Some(p) = pc::get(c, aid) else { return };
    intif_auction_close(p.char_id, auction_id);
}

/// clif.cpp clif_parse_Auction_bid (CZ_AUCTION_BUY 0x024f <auction id>.L <money>.L): the zeny is paid first and
/// comes back with the char server's answer when the bid fails.
fn clif_parse_auction_bid(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_AUCTION_BUY::read(&mut Reader::new(pkt)) else { return };
    if !inv::pc_can_give_items(c, aid) {
        if let Some(sd) = sd_of(c, aid) {
            clif::displaymessage(c, &sd, crate::data::msg_txt(246)); // Your GM level doesn't authorize you to perform this action.
        }
        return;
    }
    // Check if char server is down (bugreport:1138): CheckForCharServer() is never true here
    // pc_payzeny(sd, int32 zeny, …) of the packet's uint32
    if inv::pc_payzeny(c, aid, (p.money as i32) as i64, LOG_TYPE_AUCTION, 0) != 0 {
        clif_auction_message(c, aid, 8); // You do not have enough zeny
        return;
    }
    let Some(ch) = pc::character_of(c, aid) else { return };
    intif_auction_bid(ch.id, &ch.name, p.auction_id, p.money as i32);
}

/// clif.cpp clif_parse_Auction_search (CZ_AUCTION_ITEM_SEARCH 0x0251 <search type>.W <auction id>.L <search text>.24B
/// <page number>.W; 0 = armor, 1 = weapon, 2 = card, 3 = misc, 4 = name search, 5 = auction id search — which the
/// char server reads as a maximum price).
fn clif_parse_auction_search(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_AUCTION_ITEM_SEARCH::read(&mut Reader::new(pkt)) else { return };
    if feature_auction(c) == 0 {
        return;
    }
    clif_parse_auction_cancelreg(c, aid);
    let Some(sd) = pc::get(c, aid) else { return };
    // safestrncpy( search_text, p->text, sizeof( search_text ) )
    let search_text = strncpy(&p.text, NAME_LENGTH);
    intif_auction_requestlist(sd.char_id, p.r#type as i16, p.auction_id as i32, &search_text, p.page);
}

/// clif.cpp clif_parse_Auction_buysell (CZ_AUCTION_REQ_MY_INFO 0x025c <type>.W; 0 = sell (own auctions), 1 = buy
/// (own bids)).
fn clif_parse_auction_buysell(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(kind) = at_pos(pkt, 0).and_then(|mut r| r.u16()) else { return };
    // int16 type = RFIFOW(…) + 6
    let kind = (kind as i32 + 6) as i16;
    if feature_auction(c) == 0 {
        return;
    }
    clif_parse_auction_cancelreg(c, aid);
    let Some(sd) = pc::get(c, aid) else { return };
    intif_auction_requestlist(sd.char_id, kind, 0, "", 1);
}

// ---------------------------------------------------------------------------------------------- intif.cpp

/// intif.cpp intif_Auction_requestlist (0x3050): the page is an int16 written with WFIFOW.
fn intif_auction_requestlist(char_id: u32, kind: i16, price: i32, searchtext: &str, page: u16) -> bool {
    to_char(AuctionInter::RequestList { char_id, kind, price, searchtext: strncpy(searchtext, NAME_LENGTH), page });
    true
}

/// intif.cpp intif_parse_Auction_results (0x3850).
pub fn intif_parse_auction_results(c: &Ctx, char_id: u32, count: i16, pages: i16, auctions: &[AuctionData]) {
    let Some(aid) = charid2aid(c, char_id) else { return };
    clif_auction_results(c, aid, count, pages, auctions);
}

/// intif.cpp intif_Auction_register (0x3051).
fn intif_auction_register(auction: AuctionData) -> bool {
    to_char(AuctionInter::Register { auction });
    true
}

/// intif.cpp intif_parse_Auction_register (0x3851): the auction exists (its id), or the seller already has five and
/// gets the item and the fee back.
pub fn intif_parse_auction_register(c: &Ctx, mut auction: AuctionData) {
    let Some(aid) = charid2aid(c, auction.seller_id) else { return };
    if auction.auction_id > 0 {
        clif_auction_message(c, aid, 1); // Confirmation Packet ??
        if CHARSAVE_AUCTION {
            crate::chrif::chrif_save(c, aid, crate::chrif::CSAVE_INVENTORY);
        }
    } else {
        let zeny = (auction.hours as i32).wrapping_mul(auction_feeperhour(c));
        clif_auction_message(c, aid, 4);
        let amount = auction.item.amount as i32;
        inv::pc_additem(c, aid, &mut auction.item, amount, LOG_TYPE_AUCTION);
        inv::pc_getzeny(c, aid, zeny as i64, LOG_TYPE_AUCTION, 0);
    }
}

/// intif.cpp intif_Auction_cancel (0x3052).
fn intif_auction_cancel(char_id: u32, auction_id: u32) -> bool {
    to_char(AuctionInter::Cancel { char_id, auction_id });
    true
}

/// intif.cpp intif_parse_Auction_cancel (0x3852).
pub fn intif_parse_auction_cancel(c: &Ctx, char_id: u32, result: u8) {
    let Some(aid) = charid2aid(c, char_id) else { return };
    match result {
        0 => clif_auction_message(c, aid, 2),
        1 => clif_auction_close(c, aid, 2),
        2 => clif_auction_close(c, aid, 1),
        3 => clif_auction_message(c, aid, 3),
        _ => {}
    }
}

/// intif.cpp intif_Auction_close (0x3053).
fn intif_auction_close(char_id: u32, auction_id: u32) -> bool {
    to_char(AuctionInter::Close { char_id, auction_id });
    true
}

/// intif.cpp intif_parse_Auction_close (0x3853).
pub fn intif_parse_auction_close(c: &Ctx, char_id: u32, result: u8) {
    let Some(aid) = charid2aid(c, char_id) else { return };
    clif_auction_close(c, aid, result);
    if result == 0 {
        // FIXME: Leeching off a parse function
        clif_parse_auction_cancelreg(c, aid);
        intif_auction_requestlist(char_id, 6, 0, "", 1);
    }
}

/// intif.cpp intif_Auction_bid (0x3055).
fn intif_auction_bid(char_id: u32, name: &str, auction_id: u32, bid: i32) -> bool {
    to_char(AuctionInter::Bid { char_id, auction_id, bid, name: strncpy(name, NAME_LENGTH) });
    true
}

/// intif.cpp intif_parse_Auction_bid (0x3855): the result, the zeny that comes back, and after a successful bid the
/// list of the character's own bids.
pub fn intif_parse_auction_bid(c: &Ctx, char_id: u32, bid: i32, result: u8) {
    let Some(aid) = charid2aid(c, char_id) else { return };
    clif_auction_message(c, aid, result);
    if bid > 0 {
        inv::pc_getzeny(c, aid, bid as i64, LOG_TYPE_AUCTION, 0);
    }
    if result == 1 {
        // To update the list, display your buy list
        clif_parse_auction_cancelreg(c, aid);
        intif_auction_requestlist(char_id, 7, 0, "", 1);
    }
}

/// intif.cpp intif_parse_Auction_message (0x3854): 'You have won the auction' and 'You failed to won the auction'.
pub fn intif_parse_auction_message(c: &Ctx, char_id: u32, result: u8) {
    let Some(aid) = charid2aid(c, char_id) else { return };
    clif_auction_message(c, aid, result);
}

#[cfg(test)]
mod tests {
    use super::*;

    /// battle.cpp:8633-8634, 8693: the three settings as battle_data has them, and what the conf gives.
    #[test]
    fn battle_data_matches_the_conf() {
        bc::reset_for_test();
        let d = |k: Bc| &bc::BATTLE_DATA[k.0 as usize];
        assert_eq!((d(Bc::AUCTION_FEEPERHOUR).name, d(Bc::AUCTION_FEEPERHOUR).min, d(Bc::AUCTION_FEEPERHOUR).max), ("auction_feeperhour", 0, i32::MAX));
        assert_eq!((d(Bc::AUCTION_MAXIMUMPRICE).name, d(Bc::AUCTION_MAXIMUMPRICE).min, d(Bc::AUCTION_MAXIMUMPRICE).max), ("auction_maximumprice", 0, crate::inventory::MAX_ZENY as i32));
        assert_eq!((d(Bc::FEATURE_AUCTION).name, d(Bc::FEATURE_AUCTION).min, d(Bc::FEATURE_AUCTION).max), ("feature.auction", 0, 2));
        assert_eq!((bc::get(Bc::AUCTION_FEEPERHOUR), bc::get(Bc::AUCTION_MAXIMUMPRICE), bc::get(Bc::FEATURE_AUCTION)), (12000, 500_000_000, 0));
        assert_eq!(bc::find("FEATURE.AUCTION"), Some(Bc::FEATURE_AUCTION.0 as usize));
        assert!(bc::find("feature.auctions").is_none());
    }

    /// The offsets clif_parse_Auction_setitem / _cancel / _close / _buysell read (clif_packetdb.hpp:629-632, 702, 708).
    #[test]
    fn packet_positions() {
        let setitem = [0x4c, 0x02, 0x05, 0x00, 0x01, 0x00, 0x00, 0x00];
        assert_eq!(at_pos(&setitem, 0).and_then(|mut r| r.u16()), Some(5));
        assert_eq!(at_pos(&setitem, 1).and_then(|mut r| r.u32()), Some(1));
        let cancel = [0x4e, 0x02, 0x2a, 0x00, 0x00, 0x00];
        assert_eq!(at_pos(&cancel, 0).and_then(|mut r| r.u32()), Some(42));
        let close = [0x5d, 0x02, 0x2b, 0x00, 0x00, 0x00];
        assert_eq!(at_pos(&close, 0).and_then(|mut r| r.u32()), Some(43));
        let buysell = [0x5c, 0x02, 0x01, 0x00];
        assert_eq!(at_pos(&buysell, 0).and_then(|mut r| r.u16()), Some(1));
        // a cut packet reads nothing
        assert_eq!(at_pos(&[0x4c, 0x02, 0x05], 0).and_then(|mut r| r.u16()), None);
    }
}
