//! map/cashshop.cpp (the cash shop window and its sales), the cash / item / point shop NPCs of npc.cpp
//! (npc_cashshop_buy, npc_cashshop_buylist, npc_cashshop_process_payment, npc_shop_currency_type), pc.cpp's cash and
//! Kafra points (pc_paycash, pc_getcash, the CASHPOINTS / KAFRAPOINTS / BANK_VAULT parameters) and their clif.cpp
//! packets.
//!
//! Account data: rAthena keeps `#CASHPOINTS`, `#KAFRAPOINTS` and `#BANKVAULT` as account registry variables
//! (acc_reg_num, the script engine's `acc_reg_num` table here) and caches them in sd->cashPoints / kafraPoints /
//! bank_vault at pc_reg_received; the cache is the [`PcPoints`] row of an online character (with the session flags
//! state.cashshop_open, state.sale_open, state.banking and status.cashshop_sent). Writes go to both, as rAthena's
//! pc_setaccountreg + sd field.

use spacetimedb::{table, Table};

use crate::clif::{self, Target};
use crate::db::items::{self as itemdb, cash};
use crate::inventory as inv;
use crate::map_log::*;
use crate::net::packets::*;
use crate::net::wire::{latin1, Reader};
use crate::npc::{npc as _, Npc, NPCTYPE_CASHSHOP, NPCTYPE_ITEMSHOP, NPCTYPE_POINTSHOP};
use crate::pc;
use crate::timer::{self, Func};
use crate::unit;
use crate::world::Ctx;

/// mmo.hpp MAX_CASHPOINT / MAX_KAFRAPOINT (INT_MAX), MAX_BANK_ZENY (SINT32_MAX).
pub const MAX_CASHPOINT: i32 = i32::MAX;
pub const MAX_KAFRAPOINT: i32 = i32::MAX;
pub const MAX_BANK_ZENY: i32 = i32::MAX;
/// pc.hpp CASHPOINT_VAR / KAFRAPOINT_VAR / BANK_VAULT_VAR.
pub const CASHPOINT_VAR: &str = "#CASHPOINTS";
pub const KAFRAPOINT_VAR: &str = "#KAFRAPOINTS";
pub const BANK_VAULT_VAR: &str = "#BANKVAULT";
/// map.hpp SP_BANK_VAULT / SP_CASHPOINTS / SP_KAFRAPOINTS.
pub const SP_BANK_VAULT: i64 = 127;
pub const SP_CASHPOINTS: i64 = 131;
pub const SP_KAFRAPOINTS: i64 = 132;
/// conf/battle/misc.conf `cashshop_show_points: no`; conf/battle/feature.conf `feature.banking: on`.
pub fn cashshop_show_points() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::CASHSHOP_SHOW_POINTS) != 0
}
pub fn feature_banking() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_BANKING) != 0
}
/// script_constants MF_NOCASHSHOP.
const MF_NOCASHSHOP: i16 = 70;
/// color_table[COLOR_LIGHT_GREEN] / [COLOR_RED] (clif.cpp do_init_clif: 0xB5FFB5 / 0xFF0000 stored as BGR).
const COLOR_LIGHT_GREEN: u32 = 0xb5ffb5;
const COLOR_RED: u32 = 0x0000ff;
/// clif.hpp BC_BLUE.
const BC_BLUE: i32 = 0x10;

/// cashshop.hpp CASHSHOP_BUY_RESULT.
pub const CASHSHOP_RESULT_SUCCESS: u16 = 0x0;
pub const CASHSHOP_RESULT_ERROR_SHORTTAGE_CASH: u16 = 0x2;
pub const CASHSHOP_RESULT_ERROR_UNKONWN_ITEM: u16 = 0x3;
pub const CASHSHOP_RESULT_ERROR_INVENTORY_WEIGHT: u16 = 0x4;
pub const CASHSHOP_RESULT_ERROR_INVENTORY_ITEMCNT: u16 = 0x5;
pub const CASHSHOP_RESULT_ERROR_PC_STATE: u16 = 0x6;
pub const CASHSHOP_RESULT_ERROR_OVER_PRODUCT_TOTAL_CNT: u16 = 0x7;
pub const CASHSHOP_RESULT_ERROR_RUNE_OVERCOUNT: u16 = 0x9;
pub const CASHSHOP_RESULT_ERROR_UNKNOWN: u16 = 0xb;

/// clif.hpp e_CASHSHOP_ACK.
pub const ERROR_TYPE_NONE: i32 = 0;
pub const ERROR_TYPE_NPC: i32 = 1;
pub const ERROR_TYPE_INVENTORY_WEIGHT: i32 = 3;
pub const ERROR_TYPE_EXCHANGE: i32 = 4;
pub const ERROR_TYPE_ITEM_ID: i32 = 5;
pub const ERROR_TYPE_MONEY: i32 = 6;
pub const ERROR_TYPE_PURCHASE_FAIL: i32 = 8;

/// cashshop.hpp e_sale_add_result.
pub const SALE_ADD_SUCCESS: u16 = 0;
pub const SALE_ADD_FAILED: u16 = 1;
pub const SALE_ADD_DUPLICATE: u16 = 2;

// ---------------------------------------------------------------------------------------------- account points

/// sd->cashPoints / kafraPoints / bank_vault (the account registry values as pc_reg_received read them) and the
/// session flags of the cash shop, the sale tool and the bank, for an online character.
#[table(accessor = pc_points)]
#[derive(Clone, Debug)]
pub struct PcPoints {
    #[primary_key]
    pub account_id: u32,
    pub cash_points: i32,
    pub kafra_points: i32,
    pub bank_vault: i32,
    /// sd->state.cashshop_open.
    pub cashshop_open: bool,
    /// sd->status.cashshop_sent (the list was sent this session).
    pub cashshop_sent: bool,
    /// sd->state.sale_open.
    pub sale_open: bool,
    /// sd->state.banking.
    pub banking: bool,
}

pub fn points(c: &Ctx, aid: u32) -> Option<PcPoints> {
    c.db().pc_points().account_id().find(aid)
}

pub fn put_points(c: &Ctx, p: &PcPoints) {
    if c.db().pc_points().account_id().find(p.account_id).is_some() {
        c.db().pc_points().account_id().update(p.clone());
    } else {
        c.db().pc_points().insert(p.clone());
    }
}

/// pc_readaccountreg / pc_readreg2 of a `#` number.
pub fn acc_reg_get(c: &Ctx, aid: u32, name: &str) -> i64 {
    use crate::script::vm::{CodeKey, Rt, State};
    let rt = Rt::new(c);
    let st = State::new(CodeKey::Func(String::new()), std::rc::Rc::new(Default::default()), 0, aid, 0);
    crate::script::reg::store_get_num(&rt, &st, &crate::script::reg::Store::Acc(aid), &name.to_ascii_lowercase(), 0)
}

/// pc_setaccountreg / pc_setreg2 of a `#` number (0 deletes the row, as rAthena drops the entry).
pub fn acc_reg_set(c: &Ctx, aid: u32, name: &str, v: i64) {
    use crate::script::vm::{CodeKey, Rt, State};
    let rt = Rt::new(c);
    let mut st = State::new(CodeKey::Func(String::new()), std::rc::Rc::new(Default::default()), 0, aid, 0);
    crate::script::reg::store_set_num(&rt, &mut st, &crate::script::reg::Store::Acc(aid), &name.to_ascii_lowercase(), 0, v);
}

/// pc.cpp pc_reg_received's cash shop and bank part (and pc_authok's `sd->status.cashshop_sent = false`).
pub fn pc_reg_received(c: &Ctx, aid: u32) {
    let row = PcPoints {
        account_id: aid,
        cash_points: acc_reg_get(c, aid, CASHPOINT_VAR) as i32,
        kafra_points: acc_reg_get(c, aid, KAFRAPOINT_VAR) as i32,
        bank_vault: if feature_banking() { acc_reg_get(c, aid, BANK_VAULT_VAR) as i32 } else { 0 },
        cashshop_open: false,
        cashshop_sent: false,
        sale_open: false,
        banking: false,
    };
    put_points(c, &row);
}

/// unit_free: the cache goes with the session (the registry rows stay).
pub fn on_unit_free(c: &Ctx, aid: u32) {
    c.db().pc_points().account_id().delete(aid);
}

fn say_color(c: &Ctx, aid: u32, color: u32, text: &str) {
    if let Some(sd) = unit::get(c, aid) {
        clif::messagecolor_target(c, &sd, color, &latin1(text), false, Target::SelfOnly, None);
    }
}

/// pc.cpp pc_paycash: `price` total points, `points` of them Kafra points. -1 when there are not enough points,
/// otherwise cash + points. (With price 0 and points > 0, as @points uses it, cash is negative: the cash points go
/// up by what the Kafra points go down — rAthena's arithmetic.) `type_`: the cash log's type (log.rs log_cash).
pub fn pc_paycash(c: &Ctx, aid: u32, price: i32, points_: i32, type_: LogPickType) -> i32 {
    let Some(mut p) = points(c, aid) else { return -1 };
    let points_ = points_.clamp(0, MAX_KAFRAPOINT); // prevent command UB
    let cash = price.wrapping_sub(points_);
    if p.cash_points < cash || p.kafra_points < points_ {
        log::error!("pc_paycash: Not enough points (cash={}, kafra={}) to cover the price (cash={cash}, kafra={points_}) (account_id={aid}).", p.cash_points, p.kafra_points);
        return -1;
    }
    if cash != 0 {
        p.cash_points = p.cash_points.wrapping_sub(cash);
        acc_reg_set(c, aid, CASHPOINT_VAR, p.cash_points as i64);
        crate::map_log::log_cash(c, aid, type_, LOG_CASH_TYPE_CASH, cash.wrapping_neg());
    }
    if points_ != 0 {
        p.kafra_points -= points_;
        acc_reg_set(c, aid, KAFRAPOINT_VAR, p.kafra_points as i64);
        crate::map_log::log_cash(c, aid, type_, LOG_CASH_TYPE_KAFRA, -points_);
    }
    put_points(c, &p);
    if cashshop_show_points() {
        // Used %d kafra points and %d cash points. %d kafra and %d cash points remaining.
        let out = crate::data::format_msg(crate::data::msg_txt(504), &[&points_, &cash, &p.kafra_points, &p.cash_points]);
        say_color(c, aid, COLOR_LIGHT_GREEN, &out);
    }
    cash.wrapping_add(points_)
}

/// pc.cpp pc_getcash: gives cash points, or (only when no cash points are given) Kafra points; returns the amount
/// given, -1 when neither is positive. `type_`: the cash log's type (log.rs log_cash).
pub fn pc_getcash(c: &Ctx, aid: u32, cash: i32, points_: i32, type_: LogPickType) -> i32 {
    let Some(mut p) = points(c, aid) else { return -1 };
    let mut cash = cash.clamp(0, MAX_CASHPOINT); // prevent command UB
    let mut points_ = points_.clamp(0, MAX_KAFRAPOINT); // prevent command UB
    if cash > 0 {
        if cash > MAX_CASHPOINT - p.cash_points {
            log::warn!("pc_getcash: Cash point overflow (cash={cash}, have cash={}, account_id={aid}).", p.cash_points);
            cash = MAX_CASHPOINT - p.cash_points;
        }
        p.cash_points += cash;
        acc_reg_set(c, aid, CASHPOINT_VAR, p.cash_points as i64);
        put_points(c, &p);
        crate::map_log::log_cash(c, aid, type_, LOG_CASH_TYPE_CASH, cash);
        if cashshop_show_points() {
            let out = crate::data::format_msg(crate::data::msg_txt(505), &[&cash, &p.cash_points]);
            say_color(c, aid, COLOR_LIGHT_GREEN, &out);
        }
        return cash;
    }
    if points_ > 0 {
        if points_ > MAX_KAFRAPOINT - p.kafra_points {
            log::warn!("pc_getcash: Kafra point overflow (points={points_}, have points={}, account_id={aid}).", p.kafra_points);
            points_ = MAX_KAFRAPOINT - p.kafra_points;
        }
        p.kafra_points += points_;
        acc_reg_set(c, aid, KAFRAPOINT_VAR, p.kafra_points as i64);
        put_points(c, &p);
        crate::map_log::log_cash(c, aid, type_, LOG_CASH_TYPE_KAFRA, points_);
        if cashshop_show_points() {
            let out = crate::data::format_msg(crate::data::msg_txt(506), &[&points_, &p.kafra_points]);
            say_color(c, aid, COLOR_LIGHT_GREEN, &out);
        }
        return points_;
    }
    -1 // shouldn't happen but just in case
}

/// pc_readparam's SP_BANK_VAULT / SP_CASHPOINTS / SP_KAFRAPOINTS (None for any other parameter).
pub fn readparam(c: &Ctx, aid: u32, typ: i64) -> Option<i64> {
    if !matches!(typ, SP_BANK_VAULT | SP_CASHPOINTS | SP_KAFRAPOINTS) {
        return None;
    }
    let p = points(c, aid)?;
    Some(match typ {
        SP_BANK_VAULT => p.bank_vault as i64,
        SP_CASHPOINTS => p.cash_points as i64,
        _ => p.kafra_points as i64,
    })
}

/// pc_setparam's SP_BANK_VAULT / SP_CASHPOINTS / SP_KAFRAPOINTS cases (None for any other parameter; they return
/// without clif_updatestatus).
pub fn setparam(c: &Ctx, aid: u32, typ: i64, val: i32) -> Option<bool> {
    if !matches!(typ, SP_BANK_VAULT | SP_CASHPOINTS | SP_KAFRAPOINTS) {
        return None;
    }
    let Some(mut p) = points(c, aid) else { return Some(false) };
    if val < 0 {
        return Some(false);
    }
    // sd->status.char_id (the bank log's src_id); !sd->state.connect_new: the registers loaded at login set the points
    // without a log
    let (char_id, connect_new) = crate::pc::get(c, aid).map(|s| (s.char_id, s.connect_new)).unwrap_or((0, false));
    match typ {
        SP_BANK_VAULT => {
            crate::map_log::log_zeny(c, aid, LOG_TYPE_BANK, char_id, p.bank_vault.wrapping_sub(val.clamp(0, MAX_BANK_ZENY)).wrapping_neg());
            p.bank_vault = val.clamp(0, MAX_BANK_ZENY);
            acc_reg_set(c, aid, BANK_VAULT_VAR, p.bank_vault as i64);
        }
        SP_CASHPOINTS => {
            if !connect_new {
                crate::map_log::log_cash(c, aid, LOG_TYPE_SCRIPT, LOG_CASH_TYPE_CASH, p.cash_points.wrapping_sub(val.clamp(0, MAX_CASHPOINT)).wrapping_neg());
            }
            p.cash_points = val.clamp(0, MAX_CASHPOINT);
            acc_reg_set(c, aid, CASHPOINT_VAR, p.cash_points as i64);
        }
        _ => {
            if !connect_new {
                crate::map_log::log_cash(c, aid, LOG_TYPE_SCRIPT, LOG_CASH_TYPE_KAFRA, p.kafra_points.wrapping_sub(val.clamp(0, MAX_KAFRAPOINT)).wrapping_neg());
            }
            p.kafra_points = val.clamp(0, MAX_KAFRAPOINT);
            acc_reg_set(c, aid, KAFRAPOINT_VAR, p.kafra_points as i64);
        }
    }
    put_points(c, &p);
    Some(true)
}

/// pet.cpp pet_create_egg for an item from a shop (the fix agent's pet.rs): false when the item is no pet egg or the
/// egg cannot be made, and the caller then gives the item itself.
fn pet_create_egg(c: &Ctx, aid: u32, nameid: u32) -> bool {
    crate::pet::pet_create_egg(c, aid, nameid)
}

// ---------------------------------------------------------------------------------------------- cash shop window

fn send_self(c: &Ctx, aid: u32, b: &[u8]) {
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, b, &sd, Target::SelfOnly);
    }
}

/// clif.cpp clif_cashshop_open: ZC_SE_CASHSHOP_OPEN (0x0b6e) with the points and the tab.
fn clif_cashshop_open(c: &Ctx, aid: u32, tab: u32) {
    let p = points(c, aid);
    let pk = PACKET_ZC_SE_CASHSHOP_OPEN {
        packetType: PACKET_ZC_SE_CASHSHOP_OPEN::ID as i16,
        cashPoints: p.as_ref().map(|p| p.cash_points as u32).unwrap_or(0),
        kafraPoints: p.as_ref().map(|p| p.kafra_points as u32).unwrap_or(0),
        tab,
    };
    send_self(c, aid, &pk.to_vec());
}

/// clif.cpp clif_parse_cashshop_open_request (CZ_SE_CASHSHOP_OPEN2 0x0b6d <tab>.L).
pub fn parse_cashshop_open_request(c: &Ctx, aid: u32, pkt: &[u8]) {
    let tab = Reader::at(pkt, 2).u32().unwrap_or(0);
    let Some(sd) = unit::get(c, aid) else { return };
    if crate::npc::mapflag(c, sd.map, MF_NOCASHSHOP) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(451)); // Cash Shop is disabled on this map.
        return;
    }
    if let Some(mut p) = points(c, aid) {
        p.cashshop_open = true;
        put_points(c, &p);
    }
    // Set npc_shopid when using cash shop from "cash shop" button [Aelys|Susu] bugreport:96 (-1)
    crate::shop::set_npc_shopid(c, aid, u32::MAX);
    clif_cashshop_open(c, aid, tab);
}

/// clif.cpp clif_parse_cashshop_close (CZ_SE_CASHSHOP_CLOSE 0x084a).
pub fn parse_cashshop_close(c: &Ctx, aid: u32) {
    if let Some(mut p) = points(c, aid) {
        p.cashshop_open = false;
        put_points(c, &p);
    }
    // Reset npc_shopid when using cash shop from "cash shop" button [Aelys|Susu] bugreport:96
    crate::shop::set_npc_shopid(c, aid, 0);
}

/// clif.cpp clif_parse_CashShopReqTab (CZ_REQ_SE_CASH_TAB_CODE 0x0846): its answer is only compiled for
/// 20110222 <= PACKETVER < 20120000, so at this PACKETVER the request does nothing.
pub fn parse_cash_shop_req_tab(_c: &Ctx, _aid: u32) {}

/// cash_shop_db's iteration order (an std::unordered_map<uint16, …>): datagen writes the tabs in the order libstdc++
/// iterates the map (tools/datagen_mods/items.py UMap; for Loki's shop 7, 6, 5, 4, 2, 1, 0, as the oracle sends them).
pub fn cash_tabs_in_map_order() -> Vec<&'static cash::CashItemTab> {
    cash::cash_shop_db().iter().collect()
}

/// clif.cpp clif_cashshop_list: one ZC_ACK_SCHEDULER_CASHITEM (0x08ca) per filled tab (split before INT16_MAX).
fn clif_cashshop_list(c: &Ctx, aid: u32) {
    for tab in cash_tabs_in_map_order() {
        // Skip empty tabs, the client only expects filled ones
        if tab.items.is_empty() {
            continue;
        }
        let flush = |items: &Vec<PACKET_ZC_ACK_SCHEDULER_CASHITEM_sub>| {
            let p = PACKET_ZC_ACK_SCHEDULER_CASHITEM {
                packetType: PACKET_ZC_ACK_SCHEDULER_CASHITEM::ID as i16,
                packetLength: (PACKET_ZC_ACK_SCHEDULER_CASHITEM::SIZE + items.len() * PACKET_ZC_ACK_SCHEDULER_CASHITEM_sub::SIZE) as i16,
                count: items.len() as i16,
                tabNum: tab.tab as i16,
                items: items.clone(),
            };
            send_self(c, aid, &p.to_vec());
        };
        let mut items: Vec<PACKET_ZC_ACK_SCHEDULER_CASHITEM_sub> = Vec::new();
        for it in &tab.items {
            items.push(PACKET_ZC_ACK_SCHEDULER_CASHITEM_sub { itemId: itemdb::client_nameid(it.nameid), price: it.price });
            let len = PACKET_ZC_ACK_SCHEDULER_CASHITEM::SIZE + items.len() * PACKET_ZC_ACK_SCHEDULER_CASHITEM_sub::SIZE;
            if len + PACKET_ZC_ACK_SCHEDULER_CASHITEM_sub::SIZE >= i16::MAX as usize {
                // Send current data, start a new packet
                flush(&items);
                items.clear();
            }
        }
        flush(&items);
    }
}

/// clif.cpp clif_parse_cashshop_list_request (CZ_SE_CASHSHOP_LIST_REQ 0x08c9): the list once per session, then
/// the running sales.
pub fn parse_cashshop_list_request(c: &Ctx, aid: u32) {
    let Some(p) = points(c, aid) else { return };
    if !p.cashshop_sent {
        clif_cashshop_list(c, aid);
        sale_notify_login(c, aid);
        if let Some(mut p) = points(c, aid) {
            p.cashshop_sent = true;
            put_points(c, &p);
        }
    }
}

/// clif.cpp clif_cashshop_result: ZC_SE_PC_BUY_CASHITEM_RESULT (0x0849) with the points after the purchase.
fn clif_cashshop_result(c: &Ctx, aid: u32, item_id: u32, result: u16) {
    let p = points(c, aid);
    let pk = PACKET_ZC_SE_PC_BUY_CASHITEM_RESULT {
        packetType: PACKET_ZC_SE_PC_BUY_CASHITEM_RESULT::ID as i16,
        itemId: if item_id != 0 { itemdb::client_nameid(item_id) } else { 0 },
        result,
        cashPoints: p.as_ref().map(|p| p.cash_points as u32).unwrap_or(0),
        kafraPoints: p.as_ref().map(|p| p.kafra_points as u32).unwrap_or(0),
    };
    send_self(c, aid, &pk.to_vec());
}

/// clif.cpp clif_parse_cashshop_buy (CZ_SE_PC_BUY_CASHITEM_LIST 0x0848).
pub fn parse_cashshop_buy(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_SE_PC_BUY_CASHITEM_LIST::read(&mut Reader::new(pkt)) else { return };
    let s_itl = PACKET_CZ_SE_PC_BUY_CASHITEM_LIST_sub::SIZE;
    let len = p.packetLength as u16 as usize;
    let want = PACKET_CZ_SE_PC_BUY_CASHITEM_LIST::SIZE + p.count as usize * s_itl;
    if len < PACKET_CZ_SE_PC_BUY_CASHITEM_LIST::SIZE || len != want {
        log::warn!("Player {aid} sent incorrect cash shop buy packet (len {len}:{want})!");
        return;
    }
    let mut items = Vec::with_capacity(p.count as usize);
    let mut r = Reader::at(pkt, PACKET_CZ_SE_PC_BUY_CASHITEM_LIST::SIZE);
    for _ in 0..p.count {
        let Some(it) = PACKET_CZ_SE_PC_BUY_CASHITEM_LIST_sub::read(&mut r) else { return };
        items.push(it);
    }
    cashshop_buylist(c, aid, p.kafraPoints, &items);
}

/// cashshop.cpp cashshop_buylist: checks every item (tab, price, sale, room, weight), takes the points, then gives
/// the items (one result packet per item given).
pub fn cashshop_buylist(c: &Ctx, aid: u32, kafrapoints: u32, item_list: &[PACKET_CZ_SE_PC_BUY_CASHITEM_LIST_sub]) -> bool {
    let Some(pcd) = pc::get(c, aid) else { return false };
    if pcd.trading {
        clif_cashshop_result(c, aid, 0, CASHSHOP_RESULT_ERROR_PC_STATE);
        return false;
    }
    let mut totalcash: u32 = 0;
    let mut totalweight: u32 = 0;
    let mut new_: i32 = 0;
    for it in item_list {
        let nameid = it.itemId;
        let quantity = it.amount;
        let tab = it.tab;
        if tab >= cash::CASHSHOP_TAB_MAX {
            clif_cashshop_result(c, aid, nameid, CASHSHOP_RESULT_ERROR_UNKNOWN);
            return false;
        }
        let Some(cash_item) = cash::find_item_in_tab(tab, nameid) else {
            clif_cashshop_result(c, aid, nameid, CASHSHOP_RESULT_ERROR_UNKONWN_ITEM);
            return false;
        };
        let Some(id) = itemdb::itemdb_exists(nameid) else {
            clif_cashshop_result(c, aid, nameid, CASHSHOP_RESULT_ERROR_UNKONWN_ITEM);
            return false;
        };
        if quantity > 99 {
            // Client blocks buying more than 99 items of the same type at the same time, this means someone forged a
            // packet with a higher quantity
            clif_cashshop_result(c, aid, nameid, CASHSHOP_RESULT_ERROR_UNKNOWN);
            return false;
        }
        if tab == cash::CASHSHOP_TAB_SALE {
            let Some(sale) = sale_find_item(c, nameid, true) else {
                // Client tried to buy an item from sale that was not even on sale
                clif_cashshop_result(c, aid, nameid, CASHSHOP_RESULT_ERROR_UNKNOWN);
                return false;
            };
            if sale.amount < quantity {
                // Client tried to buy a higher quantity than is available
                clif_cashshop_result(c, aid, nameid, CASHSHOP_RESULT_ERROR_UNKNOWN);
                // Maybe he did not get refreshed in time -> do it now
                clif_sale_amount(c, &sale, Some(aid));
                return false;
            }
        }
        match inv::pc_checkadditem(c, aid, nameid, quantity as i32) {
            inv::CHKADDITEM_NEW => new_ += id.inventory_slot_needed(quantity as i32),
            inv::CHKADDITEM_OVERAMOUNT => {
                clif_cashshop_result(c, aid, nameid, CASHSHOP_RESULT_ERROR_OVER_PRODUCT_TOTAL_CNT);
                return false;
            }
            _ => {}
        }
        totalcash = totalcash.wrapping_add(cash_item.price.wrapping_mul(quantity));
        totalweight = totalweight.wrapping_add(itemdb::itemdb_weight(nameid).wrapping_mul(quantity));
    }
    let Some(s) = inv::st(c, aid) else { return false };
    if totalweight.wrapping_add(s.weight) > s.max_weight {
        clif_cashshop_result(c, aid, 0, CASHSHOP_RESULT_ERROR_INVENTORY_WEIGHT);
        return false;
    } else if (inv::pc_inventoryblank(c, aid) as i32) < new_ {
        clif_cashshop_result(c, aid, 0, CASHSHOP_RESULT_ERROR_INVENTORY_ITEMCNT);
        return false;
    }
    if pc_paycash(c, aid, totalcash as i32, kafrapoints as i32, LOG_TYPE_CASH) <= 0 {
        clif_cashshop_result(c, aid, 0, CASHSHOP_RESULT_ERROR_SHORTTAGE_CASH);
        return false;
    }
    for it in item_list {
        let nameid = it.itemId;
        let quantity = it.amount;
        let tab = it.tab;
        let Some(id) = itemdb::itemdb_exists(nameid) else { continue };
        // uint16 get_amt = quantity
        let mut get_amt: u32 = quantity & 0xffff;
        if id.flag.guid || !itemdb::itemdb_isstackable2(id) {
            get_amt = 1;
        }
        let mut sale = None;
        if tab == cash::CASHSHOP_TAB_SALE {
            let Some(sl) = sale_find_item(c, nameid, true) else {
                clif_cashshop_result(c, aid, nameid, CASHSHOP_RESULT_ERROR_UNKNOWN);
                return false;
            };
            if sl.amount < quantity {
                clif_cashshop_result(c, aid, nameid, CASHSHOP_RESULT_ERROR_UNKNOWN);
                clif_sale_amount(c, &sl, Some(aid));
                return false;
            }
            sale = Some(sl);
        }
        let mut j: u32 = 0;
        while j < quantity {
            if !pet_create_egg(c, aid, nameid) {
                let mut item_tmp = inv::Item::new(nameid);
                item_tmp.identify = 1;
                match inv::pc_additem(c, aid, &mut item_tmp, get_amt as i32, LOG_TYPE_CASH) {
                    inv::ADDITEM_OVERWEIGHT => {
                        clif_cashshop_result(c, aid, nameid, CASHSHOP_RESULT_ERROR_INVENTORY_WEIGHT);
                        return false;
                    }
                    inv::ADDITEM_OVERITEM => {
                        clif_cashshop_result(c, aid, nameid, CASHSHOP_RESULT_ERROR_INVENTORY_ITEMCNT);
                        return false;
                    }
                    inv::ADDITEM_OVERAMOUNT => {
                        clif_cashshop_result(c, aid, nameid, CASHSHOP_RESULT_ERROR_OVER_PRODUCT_TOTAL_CNT);
                        return false;
                    }
                    inv::ADDITEM_STACKLIMIT => {
                        clif_cashshop_result(c, aid, nameid, CASHSHOP_RESULT_ERROR_RUNE_OVERCOUNT);
                        return false;
                    }
                    _ => {}
                }
            }
            clif_cashshop_result(c, aid, nameid, CASHSHOP_RESULT_SUCCESS);
            if let Some(sl) = sale.as_mut() {
                let new_amount = sl.amount.wrapping_sub(get_amt);
                if new_amount == 0 {
                    sale_remove_item(c, sl.nameid);
                } else {
                    sl.amount = new_amount;
                    if let Some(row) = c.db().cash_sale().nameid().find(sl.nameid) {
                        c.db().cash_sale().nameid().update(CashSale { amount: new_amount, ..row });
                    }
                    clif_sale_amount(c, sl, None);
                }
            }
            if get_amt == 0 {
                break; // (a uint16 quantity of 0 never advances: rAthena would loop forever)
            }
            j += get_amt;
        }
    }
    true
}

// ---------------------------------------------------------------------------------------------- NPC cash / item / point shops

/// npc.cpp npc_shop_currency_type: [what the player has of the shop's currency, Kafra points]; `display` announces
/// the currency of item / point shops (a BC_BLUE broadcast to the player).
pub fn npc_shop_currency_type(c: &Ctx, aid: u32, nd: Option<&Npc>, display: bool) -> [i32; 2] {
    let (cash_pts, kafra) = points(c, aid).map(|p| (p.cash_points, p.kafra_points)).unwrap_or((0, 0));
    let Some(nd) = nd else {
        // Assume it's Cash Shop through the button
        return [cash_pts, kafra];
    };
    let mut cost = [0i32, 0i32];
    match nd.subtype {
        NPCTYPE_CASHSHOP => cost = [cash_pts, kafra],
        NPCTYPE_ITEMSHOP => {
            let mut total: i32 = 0;
            if let Some(id) = itemdb::itemdb_exists(nd.itemshop_nameid) {
                if display {
                    let out = crate::data::format_msg(crate::data::msg_txt(714), &[&id.ename, &id.nameid]); // Item Shop List: %s (%u)
                    let b = latin1(&out);
                    if let Some(sd) = unit::get(c, aid) {
                        clif::broadcast(c, Some(&sd), &b, b.len() + 1, BC_BLUE, Target::SelfOnly);
                    }
                }
                for r in inv::inv_list(c, aid) {
                    if r.item.amount > 0 && r.item.nameid == id.nameid && crate::shop::pc_can_sell_item(c, aid, &r.item, nd.subtype) {
                        total += r.item.amount as i32;
                    }
                }
            }
            cost[0] = total;
        }
        NPCTYPE_POINTSHOP => {
            if display {
                let out = crate::data::format_msg(crate::data::msg_txt(715), &[&nd.pointshop_str]); // Point Shop List: '%s'
                let b = latin1(&out);
                if let Some(sd) = unit::get(c, aid) {
                    clif::broadcast(c, Some(&sd), &b, b.len() + 1, BC_BLUE, Target::SelfOnly);
                }
            }
            cost[0] = pc_readreg2(c, aid, &nd.pointshop_str) as i32;
        }
        _ => {}
    }
    cost
}

/// pc.cpp pc_readreg2: a number variable by name (`@`, `#`, `##` or a permanent character variable).
fn pc_readreg2(c: &Ctx, aid: u32, name: &str) -> i64 {
    use crate::script::vm::{CodeKey, RegRef, Rt, State};
    let rt = Rt::new(c);
    let mut st = State::new(CodeKey::Func(String::new()), std::rc::Rc::new(Default::default()), 0, aid, 0);
    crate::script::reg::read_num(&rt, &mut st, Some(aid), &latin1(name), 0, &RegRef::None)
}

/// script.cpp set_reg_num(nullptr, sd, add_str(name), name, value, nullptr) (a parameter goes through pc_setparam).
fn set_reg_num(c: &Ctx, aid: u32, name: &str, v: i64) -> bool {
    use crate::script::vm::{add_str, CodeKey, RegRef, Rt, State};
    let rt = Rt::new(c);
    let mut st = State::new(CodeKey::Func(String::new()), std::rc::Rc::new(Default::default()), 0, aid, 0);
    let n = latin1(name);
    let id = add_str(&n);
    crate::script::reg::set_reg_num(&rt, &mut st, Some(aid), id, 0, &n, v, &RegRef::None)
}

/// npc.cpp npc_cashshop_process_payment: cash points (NPCTYPE_CASHSHOP), an item (NPCTYPE_ITEMSHOP) or a variable
/// (NPCTYPE_POINTSHOP) pays `price`, `points` of it in Kafra points.
fn npc_cashshop_process_payment(c: &Ctx, aid: u32, nd: &Npc, price: i32, points_: i32) -> i32 {
    let cost = npc_shop_currency_type(c, aid, Some(nd), false);
    match nd.subtype {
        NPCTYPE_CASHSHOP => {
            if cost[1] < points_ || cost[0] < price - points_ {
                return ERROR_TYPE_MONEY;
            }
            if pc_paycash(c, aid, price, points_, LOG_TYPE_NPC) <= 0 {
                return ERROR_TYPE_MONEY;
            }
        }
        NPCTYPE_ITEMSHOP => {
            let Some(id) = itemdb::itemdb_exists(nd.itemshop_nameid) else {
                log::warn!("Failed to find sellitem {} for itemshop NPC '{}'!", nd.itemshop_nameid, nd.exname);
                return ERROR_TYPE_PURCHASE_FAIL;
            };
            let mut delete_amount = price;
            if cost[1] < points_ || cost[0] < price - points_ {
                let out = crate::data::format_msg(crate::data::msg_txt(712), &[&id.ename, &id.nameid]); // You do not have enough %s (%u).
                say_color(c, aid, COLOR_RED, &out);
                return ERROR_TYPE_PURCHASE_FAIL;
            }
            for i in 0..inv::MAX_INVENTORY {
                if delete_amount <= 0 {
                    break;
                }
                let Some(r) = inv::inv_get(c, aid, i) else { continue };
                if r.item.nameid != nd.itemshop_nameid {
                    continue;
                }
                if !crate::shop::pc_can_sell_item(c, aid, &r.item, nd.subtype) {
                    continue;
                }
                let amount = (r.item.amount as i32).min(delete_amount);
                if inv::pc_delitem(c, aid, i as i32, amount, 0, 0, LOG_TYPE_NPC) != 0 {
                    log::warn!("Failed to delete item {} at itemshop NPC '{}'!", nd.itemshop_nameid, nd.exname);
                    return ERROR_TYPE_PURCHASE_FAIL;
                }
                delete_amount -= amount;
            }
            if delete_amount > 0 {
                log::error!("Item {} is not enough as payment at itemshop NPC '{}' (AID={aid})!", nd.itemshop_nameid, nd.exname);
                return ERROR_TYPE_PURCHASE_FAIL;
            }
        }
        NPCTYPE_POINTSHOP => {
            if cost[1] < points_ || cost[0] < price - points_ {
                let out = crate::data::format_msg(crate::data::msg_txt(713), &[&nd.pointshop_str]); // You do not have enough '%s'.
                say_color(c, aid, COLOR_RED, &out);
                return ERROR_TYPE_PURCHASE_FAIL;
            }
            let left = cost[0] - (price - points_);
            if !set_reg_num(c, aid, &nd.pointshop_str, left as i64) {
                return ERROR_TYPE_PURCHASE_FAIL;
            }
            let out = crate::data::format_msg(crate::data::msg_txt(716), &[&nd.pointshop_str, &left]); // Your '%s' is now: %d
            say_color(c, aid, COLOR_LIGHT_GREEN, &out);
        }
        _ => {}
    }
    ERROR_TYPE_NONE
}

/// map_id2bl(sd->npc_shopid) as the shop NPC (None for the cash shop button's -1 or anything that is not an NPC).
fn shop_npc(c: &Ctx, aid: u32) -> Option<Npc> {
    let id = pc::get(c, aid)?.npc_shopid;
    if id == 0 || id == u32::MAX {
        return None;
    }
    c.db().npc().id().find(id)
}

/// npc.cpp npc_cashshop_buylist (clients 2010-11-16 and newer): returns the clif_cashshop_ack value. `item_list` is
/// (nameid, qty) and is updated as rAthena updates it (item_avail replacement, non-stackable quantity).
pub fn npc_cashshop_buylist(c: &Ctx, aid: u32, points_: i32, item_list: &mut [(u32, i32)]) -> i32 {
    let nd = shop_npc(c, aid);
    let Some(nd) = nd.filter(|nd| matches!(nd.subtype, NPCTYPE_CASHSHOP | NPCTYPE_ITEMSHOP | NPCTYPE_POINTSHOP)) else { return ERROR_TYPE_NPC };
    if pc::get(c, aid).map(|p| p.trading).unwrap_or(false) {
        return ERROR_TYPE_EXCHANGE;
    }
    let mut new_: i32 = 0;
    let mut w: i32 = 0;
    let mut vt: i32 = 0; // Global Value
    let master = crate::script::buildins::script2::npcctl2::shop_master(c, nd.id); // nd->master_nd
    // Validating Process
    for it in item_list.iter_mut() {
        let (nameid, amount) = *it;
        let Some(id) = itemdb::itemdb_exists(nameid).filter(|_| amount > 0) else { return ERROR_TYPE_ITEM_ID };
        let Some(j) = nd.shop.iter().position(|s| s.nameid == nameid || itemdb::itemdb_viewid(s.nameid) == nameid) else { return ERROR_TYPE_ITEM_ID };
        if nd.shop[j].value <= 0 {
            return ERROR_TYPE_ITEM_ID;
        }
        let nameid = nd.shop[j].nameid; // item_avail replacement
        it.0 = nameid;
        let mut amount = amount;
        if !itemdb::itemdb_isstackable2(id) && amount > 1 {
            log::warn!("Player {aid} sent a hexed packet trying to buy {amount} of nonstackable item {nameid}!");
            amount = 1;
            it.1 = 1;
        }
        if master != 0 {
            continue; // Script-controlled shops decide by themselves, what can be bought and for what price.
        }
        match inv::pc_checkadditem(c, aid, nameid, amount) {
            inv::CHKADDITEM_NEW => new_ += id.inventory_slot_needed(amount),
            inv::CHKADDITEM_OVERAMOUNT => return ERROR_TYPE_INVENTORY_WEIGHT,
            _ => {}
        }
        vt = vt.wrapping_add(nd.shop[j].value.wrapping_mul(amount));
        w = w.wrapping_add((itemdb::itemdb_weight(nameid) as i32).wrapping_mul(amount));
    }
    if master != 0 {
        let blist: Vec<crate::shop::BuyItem> = item_list.iter().map(|&(nameid, qty)| crate::shop::BuyItem { nameid, qty }).collect();
        crate::script::buildins::script2::npcctl2::npc_buylist_sub(c, aid, &blist, master);
        return ERROR_TYPE_NONE;
    }
    let Some(s) = inv::st(c, aid) else { return ERROR_TYPE_NPC };
    if w as i64 + s.weight as i64 > s.max_weight as i64 {
        return ERROR_TYPE_INVENTORY_WEIGHT;
    }
    if (inv::pc_inventoryblank(c, aid) as i32) < new_ {
        return ERROR_TYPE_INVENTORY_WEIGHT;
    }
    let points_ = points_.min(vt);
    let res = npc_cashshop_process_payment(c, aid, &nd, vt, points_);
    if res != ERROR_TYPE_NONE {
        return res;
    }
    // Delivery Process
    for &(nameid, amount) in item_list.iter() {
        if !pet_create_egg(c, aid, nameid) {
            let mut item_tmp = inv::Item::new(nameid);
            item_tmp.identify = 1;
            let mut get_amt: u16 = amount as u16;
            if itemdb::itemdb_search(nameid).flag.guid {
                get_amt = 1;
            }
            let mut j = 0;
            while j < amount {
                inv::pc_additem(c, aid, &mut item_tmp, get_amt as i32, LOG_TYPE_NPC);
                if get_amt == 0 {
                    break;
                }
                j += get_amt as i32;
            }
        }
    }
    ERROR_TYPE_NONE
}

/// npc.cpp npc_cashshop_buy (clients 2010-11-15 and older; the client cannot reach it at this PACKETVER): one item.
pub fn npc_cashshop_buy(c: &Ctx, aid: u32, nameid: u32, amount: i32, points_: i32) -> i32 {
    if amount <= 0 {
        return ERROR_TYPE_ITEM_ID;
    }
    if points_ < 0 {
        return ERROR_TYPE_MONEY;
    }
    let Some(nd) = shop_npc(c, aid).filter(|nd| matches!(nd.subtype, NPCTYPE_CASHSHOP | NPCTYPE_ITEMSHOP | NPCTYPE_POINTSHOP)) else { return ERROR_TYPE_NPC };
    if pc::get(c, aid).map(|p| p.trading).unwrap_or(false) {
        return ERROR_TYPE_EXCHANGE;
    }
    let Some(id) = itemdb::itemdb_exists(nameid) else { return ERROR_TYPE_ITEM_ID };
    let Some(i) = nd.shop.iter().position(|s| s.nameid == nameid || itemdb::itemdb_viewid(s.nameid) == nameid) else { return ERROR_TYPE_ITEM_ID };
    if nd.shop[i].value <= 0 {
        return ERROR_TYPE_ITEM_ID;
    }
    let nameid = nd.shop[i].nameid; // item_avail replacement
    let mut amount = amount;
    if !itemdb::itemdb_isstackable2(id) && amount > 1 {
        log::warn!("Player {aid} sent a hexed packet trying to buy {amount} of nonstackable item {nameid}!");
        amount = 1;
    }
    match inv::pc_checkadditem(c, aid, nameid, amount) {
        inv::CHKADDITEM_NEW => {
            if (inv::pc_inventoryblank(c, aid) as i32) < id.inventory_slot_needed(amount) {
                return ERROR_TYPE_INVENTORY_WEIGHT;
            }
        }
        inv::CHKADDITEM_OVERAMOUNT => return ERROR_TYPE_INVENTORY_WEIGHT,
        _ => {}
    }
    let w = id.weight as i64 * amount as i64;
    let Some(s) = inv::st(c, aid) else { return ERROR_TYPE_NPC };
    if w + s.weight as i64 > s.max_weight as i64 {
        return ERROR_TYPE_INVENTORY_WEIGHT;
    }
    if nd.shop[i].value as f64 * amount as f64 > i32::MAX as f64 {
        log::warn!("npc_cashshop_buy: Item '{}' ({nameid}) price overflow attempt!", id.name);
        return ERROR_TYPE_ITEM_ID;
    }
    let price = nd.shop[i].value * amount;
    let points_ = points_.min(price);
    let res = npc_cashshop_process_payment(c, aid, &nd, price, points_);
    if res != ERROR_TYPE_NONE {
        return res;
    }
    if !pet_create_egg(c, aid, nameid) {
        let mut item_tmp = inv::Item::new(nameid);
        item_tmp.identify = 1;
        let get_amt = if id.flag.guid { 1 } else { amount as u16 as i32 };
        let mut j = 0;
        while j < amount {
            inv::pc_additem(c, aid, &mut item_tmp, get_amt, LOG_TYPE_NPC);
            if get_amt == 0 {
                break;
            }
            j += get_amt;
        }
    }
    ERROR_TYPE_NONE
}

/// clif.cpp clif_cashshop_show: ZC_PC_CASH_POINT_ITEMLIST (0x0287) of a cash / item / point shop NPC.
pub fn clif_cashshop_show(c: &Ctx, aid: u32, nd: &Npc) {
    crate::shop::set_npc_shopid(c, aid, nd.id);
    let cost = npc_shop_currency_type(c, aid, Some(nd), true);
    let items: Vec<PACKET_ZC_PC_CASH_POINT_ITEMLIST_sub> = nd
        .shop
        .iter()
        .map(|s| {
            let id = itemdb::itemdb_search(s.nameid);
            PACKET_ZC_PC_CASH_POINT_ITEMLIST_sub {
                price: s.value as u32,
                discountPrice: s.value as u32, // Discount Price
                itemType: crate::clif_items::itemtype(id.nameid),
                itemId: itemdb::client_nameid(id.nameid),
            }
        })
        .collect();
    let p = PACKET_ZC_PC_CASH_POINT_ITEMLIST {
        packetType: PACKET_ZC_PC_CASH_POINT_ITEMLIST::ID as i16,
        packetLength: (PACKET_ZC_PC_CASH_POINT_ITEMLIST::SIZE + items.len() * PACKET_ZC_PC_CASH_POINT_ITEMLIST_sub::SIZE) as i16,
        cashPoints: cost[0] as u32,
        kafraPoints: cost[1] as u32,
        items,
    };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_cashshop_ack: ZC_PC_CASH_POINT_UPDATE (0x0289 <cash point>.L <kafra point>.L <error>.W).
pub fn clif_cashshop_ack(c: &Ctx, aid: u32, error: i32) {
    let nd = shop_npc(c, aid);
    let cost = npc_shop_currency_type(c, aid, nd.as_ref(), false);
    let mut w = Vec::with_capacity(12);
    w.extend_from_slice(&0x0289u16.to_le_bytes());
    w.extend_from_slice(&cost[0].to_le_bytes());
    w.extend_from_slice(&cost[1].to_le_bytes());
    w.extend_from_slice(&(error as u16).to_le_bytes());
    send_self(c, aid, &w);
}

/// clif.cpp clif_parse_npccashshop_buy (CZ_PC_BUY_CASH_POINT_ITEM 0x0288 for PACKETVER >= 20101116: <len>.W
/// <kafra points>.L <count>.W { <amount>.W <name id>.L }*).
pub fn parse_npccashshop_buy(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = pc::get(c, aid) else { return };
    if p.trading || p.npc_shopid == 0 {
        clif_cashshop_ack(c, aid, 1);
        return;
    }
    let Some(b) = PACKET_CZ_PC_BUY_CASH_POINT_ITEM::read(&mut Reader::new(pkt)) else { return };
    let s_itl = PACKET_CZ_PC_BUY_CASH_POINT_ITEM_sub::SIZE;
    let len = b.packetLength as u16 as usize;
    if len < PACKET_CZ_PC_BUY_CASH_POINT_ITEM::SIZE || len != PACKET_CZ_PC_BUY_CASH_POINT_ITEM::SIZE + b.count as usize * s_itl {
        log::warn!("Player {aid} sent incorrect cash shop buy packet (len {len})!");
        return;
    }
    let mut r = Reader::at(pkt, PACKET_CZ_PC_BUY_CASH_POINT_ITEM::SIZE);
    let mut items: Vec<(u32, i32)> = Vec::with_capacity(b.count as usize);
    for _ in 0..b.count {
        let Some(it) = PACKET_CZ_PC_BUY_CASH_POINT_ITEM_sub::read(&mut r) else { return };
        items.push((it.itemId, it.amount as i32));
    }
    let res = npc_cashshop_buylist(c, aid, b.kafraPoints as i32, &mut items);
    clif_cashshop_ack(c, aid, res);
}

// ---------------------------------------------------------------------------------------------- sales

/// cashshop.hpp struct sale_item_data: rAthena's `sales` SQL table (map server) and sale_items array, in array order
/// (`order`); the start / end timers are timer ids (0 = INVALID_TIMER).
#[table(accessor = cash_sale)]
#[derive(Clone, Debug)]
pub struct CashSale {
    #[primary_key]
    pub nameid: u32,
    pub start: i64,
    pub end: i64,
    pub amount: u32,
    pub timer_start: u64,
    pub timer_end: u64,
    pub order: u64,
}

fn now(c: &Ctx) -> i64 {
    c.tick() / 1000
}

fn sales(c: &Ctx) -> Vec<CashSale> {
    let mut v: Vec<CashSale> = c.db().cash_sale().iter().collect();
    v.sort_by_key(|s| s.order);
    v
}

/// cashshop.cpp sale_find_item: the sale of an item (`onsale`: only while it runs and has stock).
pub fn sale_find_item(c: &Ctx, nameid: u32, onsale: bool) -> Option<CashSale> {
    let s = sales(c).into_iter().find(|s| s.nameid == nameid)?;
    if !onsale {
        return Some(s);
    }
    let t = now(c);
    // The sale is in the future / was in the past / the amount has been used up already
    if s.start > t || s.end < t || s.amount == 0 {
        return None;
    }
    Some(s)
}

/// cashshop.cpp sale_start_timer (timer target = the item id).
pub fn sale_start_timer(c: &Ctx, nameid: u32) {
    let Some(mut s) = c.db().cash_sale().nameid().find(nameid) else { return };
    clif_sale_start(c, &s, None);
    clif_sale_amount(c, &s, None);
    // Clear the start timer
    if s.timer_start != 0 {
        timer::delete(c, s.timer_start);
        s.timer_start = 0;
    }
    // Init sale end
    let due = c.tick() + (s.end - now(c)) as u32 as i64 * 1000;
    s.timer_end = timer::add(c, due, Func::SaleEndTimer, nameid, 0);
    c.db().cash_sale().nameid().update(s);
}

/// cashshop.cpp sale_end_timer.
pub fn sale_end_timer(c: &Ctx, tid: u64, nameid: u32) {
    let Some(mut s) = c.db().cash_sale().nameid().find(nameid) else { return };
    // Remove the timer so the sale end is not sent out again
    if s.timer_end != tid && s.timer_end != 0 {
        timer::delete(c, s.timer_end);
    }
    s.timer_end = 0;
    c.db().cash_sale().nameid().update(s.clone());
    clif_sale_end(c, &s, None);
    sale_remove_item(c, nameid);
}

/// cashshop.cpp sale_add_item.
pub fn sale_add_item(c: &Ctx, nameid: u32, count: i32, from: i64, to: i64) -> u16 {
    // Check if the item exists in the sales tab
    if cash::find_item_in_tab(cash::CASHSHOP_TAB_SALE, nameid).is_none() {
        return SALE_ADD_FAILED;
    }
    // Adding a sale in the past is not possible
    if from < now(c) {
        return SALE_ADD_FAILED;
    }
    // The end has to be after the start
    if from >= to {
        return SALE_ADD_FAILED;
    }
    // Amount has to be positive - this should be limited from the client too
    if count == 0 {
        return SALE_ADD_FAILED;
    }
    // Check if a sale of this item already exists
    if sale_find_item(c, nameid, false).is_some() {
        return SALE_ADD_DUPLICATE;
    }
    let order = crate::ids::next(c.rc, "cash_sale_order", 1) as u64;
    let due = c.tick() + (from - now(c)) as u32 as i64 * 1000;
    let timer_start = timer::add(c, due, Func::SaleStartTimer, nameid, 0);
    c.db().cash_sale().insert(CashSale { nameid, start: from, end: to, amount: count as u32, timer_start, timer_end: 0, order });
    SALE_ADD_SUCCESS
}

/// cashshop.cpp sale_remove_item: false when the item has no sale.
pub fn sale_remove_item(c: &Ctx, nameid: u32) -> bool {
    let Some(s) = sale_find_item(c, nameid, false) else { return false };
    if s.timer_start != 0 {
        timer::delete(c, s.timer_start);
    }
    // Check if the sale is currently running
    if s.timer_end != 0 {
        timer::delete(c, s.timer_end);
        // Notify all clients that the sale has ended
        clif_sale_end(c, &s, None);
    }
    c.db().cash_sale().nameid().delete(nameid);
    true
}

/// cashshop.cpp sale_notify_login: the running sales (those with an end timer) for a player opening the shop.
pub fn sale_notify_login(c: &Ctx, aid: u32) {
    for s in sales(c) {
        if s.timer_end != 0 {
            clif_sale_start(c, &s, Some(aid));
            clif_sale_amount(c, &s, Some(aid));
        }
    }
}

/// clif_send to one player (SELF) or to every client (ALL_CLIENT without a source).
fn send_to(c: &Ctx, to: Option<u32>, b: &[u8]) {
    match to {
        Some(aid) => send_self(c, aid, b),
        None => clif::send_all(c, b),
    }
}

/// clif.cpp clif_sale_start: ZC_NOTIFY_BARGAIN_SALE_SELLING (remaining time in s).
fn clif_sale_start(c: &Ctx, s: &CashSale, to: Option<u32>) {
    let p = PACKET_ZC_NOTIFY_BARGAIN_SALE_SELLING { packetType: PACKET_ZC_NOTIFY_BARGAIN_SALE_SELLING::ID as i16, itemId: itemdb::client_nameid(s.nameid), remainingTime: (s.end - now(c)) as u32 };
    send_to(c, to, &p.to_vec());
}

/// clif.cpp clif_sale_end: ZC_NOTIFY_BARGAIN_SALE_CLOSE.
fn clif_sale_end(c: &Ctx, s: &CashSale, to: Option<u32>) {
    let p = PACKET_ZC_NOTIFY_BARGAIN_SALE_CLOSE { packetType: PACKET_ZC_NOTIFY_BARGAIN_SALE_CLOSE::ID as i16, itemId: itemdb::client_nameid(s.nameid) };
    send_to(c, to, &p.to_vec());
}

/// clif.cpp clif_sale_amount: ZC_ACK_COUNT_BARGAIN_SALE_ITEM.
fn clif_sale_amount(c: &Ctx, s: &CashSale, to: Option<u32>) {
    let p = PACKET_ZC_ACK_COUNT_BARGAIN_SALE_ITEM { packetType: PACKET_ZC_ACK_COUNT_BARGAIN_SALE_ITEM::ID as i16, itemId: itemdb::client_nameid(s.nameid), amount: s.amount };
    send_to(c, to, &p.to_vec());
}

/// clif.cpp clif_parse_sale_refresh (0x09c3 <account id>.L <item id>.W), read through
/// PACKET_CZ_REQ_CASH_BARGAIN_SALE_ITEM_INFO as rAthena does: AID from bytes 4-7 and the item id from 8-11.
pub fn parse_sale_refresh(c: &Ctx, aid: u32, pkt: &[u8]) {
    let p_aid = Reader::at(pkt, 4).u32().unwrap_or(0);
    let item_id = Reader::at(pkt, 8).u32().unwrap_or(0);
    if p_aid != aid {
        return;
    }
    let Some(s) = sale_find_item(c, item_id, true) else { return };
    clif_sale_amount(c, &s, Some(aid));
}

/// clif.cpp clif_sale_open: ZC_OPEN_BARGAIN_SALE_TOOL (0x09b5), once.
pub fn clif_sale_open(c: &Ctx, aid: u32) {
    let Some(mut p) = points(c, aid) else { return };
    if p.sale_open {
        return;
    }
    p.sale_open = true;
    put_points(c, &p);
    send_self(c, aid, &0x09b5u16.to_le_bytes());
}

/// clif.cpp clif_parse_sale_open (0x09b4 <account id>.L, /limitedsale): runs `@limitedsale`.
pub fn parse_sale_open(c: &Ctx, aid: u32, pkt: &[u8]) {
    if Reader::at(pkt, 2).u32().unwrap_or(0) != aid {
        return;
    }
    crate::atcommand::is_atcommand(c, aid, "@limitedsale", 1);
}

/// clif.cpp clif_sale_close: ZC_CLOSE_BARGAIN_SALE_TOOL (0x09bd).
fn clif_sale_close(c: &Ctx, aid: u32) {
    let Some(mut p) = points(c, aid) else { return };
    if !p.sale_open {
        return;
    }
    p.sale_open = false;
    put_points(c, &p);
    send_self(c, aid, &0x09bdu16.to_le_bytes());
}

/// clif.cpp clif_parse_sale_close (0x09bc <account id>.L).
pub fn parse_sale_close(c: &Ctx, aid: u32, pkt: &[u8]) {
    if Reader::at(pkt, 2).u32().unwrap_or(0) != aid {
        return;
    }
    clif_sale_close(c, aid);
}

/// clif.cpp clif_sale_search_reply: ZC_ACK_CASH_BARGAIN_SALE_ITEM_INFO.
fn clif_sale_search_reply(c: &Ctx, aid: u32, item: Option<&cash::CashItem>) {
    let id = PACKET_ZC_ACK_CASH_BARGAIN_SALE_ITEM_INFO::ID as i16;
    let p = match item {
        Some(it) => PACKET_ZC_ACK_CASH_BARGAIN_SALE_ITEM_INFO { packetType: id, result: 0, itemId: itemdb::client_nameid(it.nameid), price: it.price },
        None => PACKET_ZC_ACK_CASH_BARGAIN_SALE_ITEM_INFO { packetType: id, result: 1, itemId: 0, price: 0 },
    };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_parse_sale_search (0x09ac <length>.W <account id>.L <item name>.?B).
pub fn parse_sale_search(c: &Ctx, aid: u32, pkt: &[u8]) {
    if Reader::at(pkt, 4).u32().unwrap_or(0) != aid {
        return;
    }
    if !points(c, aid).map(|p| p.sale_open).unwrap_or(false) {
        return;
    }
    // safestrncpy(item_name, RFIFOCP(fd, 8), min(RFIFOW(fd, 2) - 7, ITEM_NAME_LENGTH))
    let n = (Reader::at(pkt, 2).u16().unwrap_or(0) as i32 - 7).clamp(0, 50) as usize;
    let raw = pkt.get(8..).unwrap_or(&[]);
    let raw = &raw[..raw.len().min(n.saturating_sub(1))];
    let item_name = crate::net::wire::from_latin1(crate::net::wire::until_nul(raw));
    // not found
    let Some(id) = itemdb::itemdb_searchname(&item_name) else {
        clif_sale_search_reply(c, aid, None);
        return;
    };
    clif_sale_search_reply(c, aid, cash::find_item_in_tab(cash::CASHSHOP_TAB_SALE, id.nameid));
}

/// clif.cpp clif_sale_add_reply (0x09af <result>.W).
fn clif_sale_add_reply(c: &Ctx, aid: u32, result: u16) {
    let mut w = 0x09afu16.to_le_bytes().to_vec();
    w.extend_from_slice(&result.to_le_bytes());
    send_self(c, aid, &w);
}

/// clif.cpp clif_parse_sale_add (0x09ae / 0x0a3d, read as PACKET_CZ_REQ_APPLY_BARGAIN_SALE_ITEM).
pub fn parse_sale_add(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_REQ_APPLY_BARGAIN_SALE_ITEM::read(&mut Reader::new(pkt)) else { return };
    if p.AID != aid {
        return;
    }
    if !points(c, aid).map(|p| p.sale_open).unwrap_or(false) {
        return;
    }
    let end_time = p.startTime as i64 + p.hours as i64 * 60 * 60;
    let r = sale_add_item(c, p.itemId, p.amount as i32, p.startTime as i64, end_time);
    clif_sale_add_reply(c, aid, r);
}

/// clif.cpp clif_sale_remove_reply (0x09b1 <result>.W).
fn clif_sale_remove_reply(c: &Ctx, aid: u32, failed: bool) {
    let mut w = 0x09b1u16.to_le_bytes().to_vec();
    w.extend_from_slice(&(failed as u16).to_le_bytes());
    send_self(c, aid, &w);
}

/// clif.cpp clif_parse_sale_remove (0x09b0 <account id>.L <item id>).
pub fn parse_sale_remove(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_REQ_REMOVE_BARGAIN_SALE_ITEM::read(&mut Reader::new(pkt)) else { return };
    if p.AID != aid {
        return;
    }
    if !points(c, aid).map(|p| p.sale_open).unwrap_or(false) {
        return;
    }
    let ok = sale_remove_item(c, p.itemId);
    clif_sale_remove_reply(c, aid, !ok);
}

#[cfg(test)]
mod tests {
    #[test]
    fn tabs_in_libstdcxx_order() {
        // Loki's item_cash import loads New, Hot, Limited, Permanent, Scrolls, Consumables, Other (0 1 2 4 5 6 7); the
        // oracle's range-for over the unordered_map sends them from the last one inserted
        let order: Vec<u16> = super::cash_tabs_in_map_order().iter().map(|t| t.tab).collect();
        assert_eq!(order, vec![7, 6, 5, 4, 2, 1, 0]);
    }
}
