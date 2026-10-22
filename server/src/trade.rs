//! map/trade.cpp (player-to-player trade) + its clif.cpp packets and parsers.
//!
//! sd->state.trading stays the `pc.trading` flag (other systems read it through pc_cant_act / pc_istrading); the rest
//! of the trade state (sd->trade_partner, sd->deal, sd->state.deal_locked / isBoundTrading) is the [`PcTrade`] row of
//! an online character.

use spacetimedb::{table, SpacetimeType, Table};

use crate::clif::{self, Target};
use crate::db::items as itemdb;
use crate::inventory::{self as inv, Item, MAX_INVENTORY, MAX_ZENY};
use crate::net::packets::*;
use crate::net::wire::Reader;
use crate::pc::{self, pc as _};
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// trade.cpp TRADE_DISTANCE: max distance from traders to enable a trade to take place.
const TRADE_DISTANCE: i32 = 2;
/// conf/battle/items.conf `trade_count_stackable: yes`; conf/battle/gm.conf `ban_hack_trade: 5`;
/// conf/battle/player.conf `invite_request_check: yes`; conf/map_athena.conf `save_settings: 4095` (CHARSAVE_TRADE).
fn trade_count_stackable() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::TRADE_COUNT_STACKABLE) != 0
}
fn ban_hack_trade() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::BAN_HACK_TRADE)
}
fn invite_request_check() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::INVITE_REQUEST_CHECK) != 0
}
const CHARSAVE_TRADE: bool = true;
/// mmo.hpp MAX_AMOUNT.
const MAX_AMOUNT: i32 = 30000;
/// clif.hpp MSI_SWAP_EQUIPITEM_UNREGISTER_FIRST.
const MSI_SWAP_EQUIPITEM_UNREGISTER_FIRST: u16 = 3015;
/// script_constants MF_NOTRADE.
const MF_NOTRADE: i16 = 11;

/// trade.hpp enum e_ack_trade_response.
pub const TRADE_ACK_TOOFAR: u8 = 0;
pub const TRADE_ACK_CHARNOTEXIST: u8 = 1;
pub const TRADE_ACK_FAILED: u8 = 2;
pub const TRADE_ACK_ACCEPT: u8 = 3;
pub const TRADE_ACK_CANCEL: u8 = 4;
pub const TRADE_ACK_BUSY: u8 = 5;

/// clif.hpp enum e_exitem_add_result.
pub const EXITEM_ADD_SUCCEED: u8 = 0;
pub const EXITEM_ADD_FAILED_OVERWEIGHT: u8 = 1;
pub const EXITEM_ADD_FAILED_CLOSED: u8 = 2;
pub const EXITEM_ADD_FAILED_OVERCOUNT: u8 = 3;
pub const EXITEM_ADD_FAILED_EACHITEM_OVERCOUNT: u8 = 4;

/// One of sd->deal.item[10].
#[derive(SpacetimeType, Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct DealItem {
    pub index: i16,
    pub amount: i16,
}

/// sd->trade_partner, sd->deal, sd->state.deal_locked / isBoundTrading of an online character.
#[table(accessor = pc_trade)]
#[derive(Clone, Debug)]
pub struct PcTrade {
    #[primary_key]
    pub account_id: u32,
    /// trade_partner.id (an account id) / trade_partner.lv.
    pub partner_id: u32,
    pub partner_lv: u16,
    /// 1: clicked on OK, 2: clicked on TRADE.
    pub deal_locked: u8,
    pub is_bound_trading: u8,
    pub deal_item: Vec<DealItem>,
    pub deal_zeny: i32,
    pub deal_weight: i32,
    pub deal_inventory_space: i32,
}

fn fresh(aid: u32) -> PcTrade {
    PcTrade {
        account_id: aid,
        partner_id: 0,
        partner_lv: 0,
        deal_locked: 0,
        is_bound_trading: 0,
        deal_item: vec![DealItem::default(); 10],
        deal_zeny: 0,
        deal_weight: 0,
        deal_inventory_space: 0,
    }
}

/// The trade state of `aid` (a zeroed one when it has none yet).
pub fn tx(c: &Ctx, aid: u32) -> PcTrade {
    c.db().pc_trade().account_id().find(aid).unwrap_or_else(|| fresh(aid))
}

fn put(c: &Ctx, t: &PcTrade) {
    if c.db().pc_trade().account_id().find(t.account_id).is_some() {
        c.db().pc_trade().account_id().update(t.clone());
    } else {
        c.db().pc_trade().insert(t.clone());
    }
}

/// unit_free: the trade state goes with the session.
pub fn on_unit_free(c: &Ctx, aid: u32) {
    c.db().pc_trade().account_id().delete(aid);
}

fn set_trading(c: &Ctx, aid: u32, on: bool) {
    if let Some(mut p) = pc::get(c, aid) {
        if p.trading != on {
            p.trading = on;
            pc::put(c, &p);
        }
    }
}

fn trading(c: &Ctx, aid: u32) -> bool {
    pc::get(c, aid).map(|p| p.trading).unwrap_or(false)
}

/// map_id2sd: an online player (None for any other id).
fn id2sd(c: &Ctx, id: u32) -> Option<Unit> {
    if id == 0 || pc::get(c, id).is_none() {
        return None;
    }
    unit::get(c, id)
}

/// pc_can_use_command(sd, "trade", COMMAND_ATCOMMAND).
fn can_use_trade_command(c: &Ctx, aid: u32) -> bool {
    pc::get(c, aid).map(|p| crate::rules::can_use_command(p.group_id, "trade", false)).unwrap_or(false)
}

/// check_distance_bl(sd, tsd, TRADE_DISTANCE) on the same map.
fn too_far(a: &Unit, b: &Unit) -> bool {
    a.map != b.map || !crate::path::check_distance((a.x - b.x) as i32, (a.y - b.y) as i32, TRADE_DISTANCE)
}

/// The inventory as rAthena's struct item array (empty slots are zeroed items).
fn inventory_copy(c: &Ctx, aid: u32) -> Vec<Item> {
    inv::inv_array(c, aid).into_iter().map(|s| s.map(|r| r.item).unwrap_or_else(|| Item::new(0))).collect()
}

// ---------------------------------------------------------------------------------------------- trade.cpp

/// trade.cpp trade_traderequest: player `aid` initiates a trade with `target` (None: not online).
pub fn trade_traderequest(c: &Ctx, aid: u32, target: Option<u32>) {
    let Some(sd) = unit::get(c, aid) else { return };
    if crate::npc::mapflag(c, sd.map, MF_NOTRADE) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(272));
        return; // Can't trade in notrade mapflag maps.
    }
    let tsd = target.and_then(|t| id2sd(c, t));
    let Some(tsd) = tsd.filter(|t| t.id != aid) else {
        clif_traderesponse(c, aid, TRADE_ACK_CHARNOTEXIST);
        return;
    };
    let Some(tp) = pc::get(c, tsd.id) else { return };
    if tp.npc_id != 0 {
        // Trade fails if you are using an NPC.
        clif_traderesponse(c, aid, TRADE_ACK_FAILED);
        return;
    }
    if !invite_request_check() {
        // (guild / party / adoption invitations: only with invite_request_check off)
    }
    let mut st = tx(c, aid);
    if st.partner_id != 0 {
        // If a character tries to trade to another one then cancel the previous one
        if let Some(prev) = id2sd(c, st.partner_id) {
            let mut pt = tx(c, prev.id);
            pt.partner_id = 0;
            pt.partner_lv = 0;
            put(c, &pt);
            clif_tradecancelled(c, prev.id);
        } // Once cancelled then continue to the new one.
        st = tx(c, aid);
        st.partner_id = 0;
        st.partner_lv = 0;
        put(c, &st);
        clif_tradecancelled(c, aid);
    }
    if tx(c, tsd.id).partner_id != 0 {
        clif_traderesponse(c, aid, TRADE_ACK_FAILED); // person is in another trade
        return;
    }
    if !inv::pc_can_give_items(c, aid) || !inv::pc_can_give_items(c, tsd.id) {
        // check if both GMs are allowed to trade
        clif::displaymessage(c, &sd, crate::data::msg_txt(246)); // Your GM level doesn't authorize you to perform this action.
        clif_traderesponse(c, aid, TRADE_ACK_FAILED); // GM is not allowed to trade
        return;
    }
    // Players can not request trade from far away, unless they are allowed to use @trade.
    if !can_use_trade_command(c, aid) && too_far(&sd, &tsd) {
        clif_traderesponse(c, aid, TRADE_ACK_TOOFAR);
        return;
    }
    let lv = |id: u32| pc::character_of(c, id).map(|ch| ch.base_level).unwrap_or(0);
    let mut tt = tx(c, tsd.id);
    tt.partner_id = aid;
    tt.partner_lv = lv(aid);
    put(c, &tt);
    let mut st = tx(c, aid);
    st.partner_id = tsd.id;
    st.partner_lv = lv(tsd.id);
    put(c, &st);
    clif_traderequest(c, tsd.id, &sd.name);
}

/// trade.cpp trade_tradeack: the answer to a trade request (3 accept, 4 cancel; 0-2 are the server's).
pub fn trade_tradeack(c: &Ctx, aid: u32, kind: u8) {
    let st = tx(c, aid);
    if trading(c, aid) || st.partner_id == 0 {
        return; // Already trading or no partner set.
    }
    let clear = |id: u32, unlock: bool| {
        let mut t = tx(c, id);
        if unlock {
            t.deal_locked = 0;
        }
        t.partner_id = 0;
        t.partner_lv = 0;
        put(c, &t);
    };
    let Some(tsd) = id2sd(c, st.partner_id) else {
        clif_traderesponse(c, aid, TRADE_ACK_CHARNOTEXIST);
        clear(aid, false);
        return;
    };
    if trading(c, tsd.id) || tx(c, tsd.id).partner_id != aid {
        clif_traderesponse(c, aid, TRADE_ACK_FAILED);
        clear(aid, false);
        return; // Already trading or wrong partner.
    }
    if kind == TRADE_ACK_CANCEL {
        clif_traderesponse(c, tsd.id, TRADE_ACK_CANCEL);
        clif_traderesponse(c, aid, TRADE_ACK_CANCEL);
        clear(aid, true);
        clear(tsd.id, true);
        return;
    }
    if kind != TRADE_ACK_ACCEPT {
        return; // If client didn't send accept, it's a broken packet?
    }
    let Some(sd) = unit::get(c, aid) else { return };
    // Players can not request trade from far away, unless they are allowed to use @trade.
    // Check here as well since the original character could had warped.
    if !can_use_trade_command(c, aid) && too_far(&sd, &tsd) {
        clif_traderesponse(c, aid, TRADE_ACK_TOOFAR);
        clear(aid, false);
        clear(tsd.id, false);
        return;
    }
    // Check if you can start trade.
    let busy = |id: u32| pc::get(c, id).map(|p| p.npc_id != 0 || p.vending || p.buyingstore || p.storage_flag != 0).unwrap_or(true);
    if busy(aid) || busy(tsd.id) {
        clif_traderesponse(c, aid, TRADE_ACK_FAILED);
        clif_traderesponse(c, tsd.id, TRADE_ACK_FAILED);
        clear(aid, true);
        clear(tsd.id, true);
        return;
    }
    // Initiate trade
    set_trading(c, aid, true);
    set_trading(c, tsd.id, true);
    for id in [aid, tsd.id] {
        let mut t = tx(c, id);
        t.deal_item = vec![DealItem::default(); 10];
        t.deal_zeny = 0;
        t.deal_weight = 0;
        t.deal_inventory_space = 0;
        put(c, &t);
    }
    clif_traderesponse(c, tsd.id, kind);
    clif_traderesponse(c, aid, kind);
}

/// What impossible_trade_check found: its return value, and for an item hack the GM notices and whether the
/// connection is closed.
struct HackCheck {
    result: i32,
    gm_messages: Vec<String>,
    eof: bool,
}

/// trade.cpp impossible_trade_check: -1 zeny hack, 0 fine, 1 item hack (more items in the deal than the player has,
/// equipped items other than ammunition not counted). rAthena's intif_wis_message_to_gm notices travel through the
/// char server and set_eof closes the socket in the next socket loop, both after the caller's trade_tradecancel: the
/// caller does them afterwards ([`hack_followup`]).
fn impossible_trade_check(c: &Ctx, aid: u32) -> HackCheck {
    let mut out = HackCheck { result: 0, gm_messages: Vec::new(), eof: false };
    let st = tx(c, aid);
    let Some(ch) = pc::character_of(c, aid) else {
        out.result = 1;
        return out;
    };
    if st.deal_zeny > ch.zeny as i32 {
        crate::status::write_char_reg_num(c, aid, "ZENY_HACKER", 1);
        out.result = -1;
        return out;
    }
    // get inventory of player
    let mut inventory = inventory_copy(c, aid);
    // remove equipped items (they can not be trade)
    for it in inventory.iter_mut() {
        if it.nameid > 0 && it.equip != 0 && it.equip & inv::EQP_AMMO == 0 {
            *it = Item::new(0);
        }
    }
    // check items in player inventory
    for d in &st.deal_item {
        if d.amount == 0 {
            continue;
        }
        let index = d.index as usize;
        let have = inventory.get(index).map(|it| it.amount).unwrap_or(0);
        if have < d.amount {
            // if more than the player have -> hack
            let nameid = inventory.get(index).map(|it| it.nameid).unwrap_or(0);
            // Hack on trade: character '%s' (account: %d) try to trade more items that he has.
            out.gm_messages.push(crate::data::format_msg(crate::data::msg_txt(538), &[&ch.name, &aid]));
            // This player has %d of a kind of item (id: %u), and try to trade %d of them.
            out.gm_messages.push(crate::data::format_msg(crate::data::msg_txt(539), &[&have, &nameid, &d.amount]));
            // if we block / ban people (trade.cpp:217-228)
            let m3 = if ban_hack_trade() < 0 {
                crate::atcommand_atcmd::accounts::chrif_req_login_operation(
                    c,
                    -1,
                    &crate::net::wire::latin1(&ch.name),
                    crate::atcommand_atcmd::accounts::CHRIF_OP_LOGIN_BLOCK,
                    0,
                    0,
                    0,
                ); // type: 1 - block
                out.eof = true;
                crate::data::msg_txt(540).to_string() //  This player has been definitively blocked.
            } else if ban_hack_trade() > 0 {
                crate::atcommand_atcmd::accounts::chrif_req_login_operation(
                    c,
                    -1,
                    &crate::net::wire::latin1(&ch.name),
                    crate::atcommand_atcmd::accounts::CHRIF_OP_LOGIN_BAN,
                    ban_hack_trade() * 60,
                    0,
                    0,
                ); // type: 2 - ban (year, month, day, hour, minute, second)
                out.eof = true;
                crate::data::format_msg(crate::data::msg_txt(507), &[&ban_hack_trade()]) //  This player has been banned for %d minute(s).
            } else {
                crate::data::msg_txt(508).to_string() //  This player hasn't been banned (Ban option is disabled).
            };
            out.gm_messages.push(m3);
            out.result = 1;
            return out;
        }
        if let Some(it) = inventory.get_mut(index) {
            it.amount -= d.amount; // remove item from inventory
        }
    }
    out
}

/// The GM notices and the closed connection of an item hack, after the trade was cancelled.
fn hack_followup(c: &Ctx, aid: u32, h: HackCheck) {
    for m in &h.gm_messages {
        wis_to_gm(c, m);
    }
    if h.eof {
        crate::session::set_eof(c, aid); // forced to disconnect because of the hack
    }
}

/// intif_wis_message_to_gm(wisp_server_name, PC_PERM_RECEIVE_HACK_INFO, msg): every online player with the
/// permission gets the whisper (mapif_parse_WisToGM on each map server).
fn wis_to_gm(c: &Ctx, msg: &str) {
    let gms: Vec<u32> = c.db().pc().iter().filter(|p| !p.disconnected && crate::pc::has_permission(c, p.account_id, crate::rules::PC_PERM_RECEIVE_HACK_INFO)).map(|p| p.account_id).collect();
    for id in gms {
        if let Some(u) = unit::get(c, id) {
            clif::wis_message(c, &u, crate::rules::WISP_SERVER_NAME, None, msg, 0);
        }
    }
}

/// trade.cpp trade_check: zeny limits and room for every item on both sides (0 error, 1 success).
fn trade_check(c: &Ctx, aid: u32, tid: u32) -> bool {
    let (Some(ch), Some(tch)) = (pc::character_of(c, aid), pc::character_of(c, tid)) else { return false };
    let (sdeal, tdeal) = (tx(c, aid), tx(c, tid));
    let (sz, tz) = (ch.zeny as i64, tch.zeny as i64);
    // check zeny value against hackers (Zeny was already checked on time of adding, but you never know when you lost
    // some zeny since then.
    if sdeal.deal_zeny as i64 > sz || tz > MAX_ZENY - sdeal.deal_zeny as i64 {
        return false;
    }
    if tdeal.deal_zeny as i64 > tz || sz > MAX_ZENY - tdeal.deal_zeny as i64 {
        return false;
    }
    // get inventory of player
    let mut inventory = inventory_copy(c, aid);
    let mut inventory2 = inventory_copy(c, tid);
    // one direction of the loop: `from`'s deal item into `to`
    fn move_one(from: &mut [Item], to: &mut [Item], n: usize, amount: i16) -> bool {
        if amount > from[n].amount {
            return false; // Quantity Exploit?
        }
        let data = itemdb::itemdb_search(from[n].nameid);
        let mut i = MAX_INVENTORY;
        if itemdb::itemdb_isstackable2(data) {
            // Stackable item.
            for k in 0..MAX_INVENTORY {
                if to[k].nameid == from[n].nameid && to[k].cards() == from[n].cards() {
                    if to[k].amount as i32 + amount as i32 > MAX_AMOUNT {
                        return false;
                    }
                    to[k].amount += amount;
                    from[n].amount -= amount;
                    i = k;
                    break;
                }
            }
        }
        if i == MAX_INVENTORY {
            // look for an empty slot.
            let Some(k) = to.iter().position(|it| it.nameid == 0) else { return false };
            to[k] = from[n].clone();
            to[k].amount = amount;
            from[n].amount -= amount;
        }
        true
    }
    // check free slot in both inventory
    for trade_i in 0..10 {
        let d = sdeal.deal_item[trade_i];
        if d.amount != 0 {
            let n = d.index as usize;
            if n >= MAX_INVENTORY || !move_one(&mut inventory, &mut inventory2, n, d.amount) {
                return false;
            }
        }
        let d = tdeal.deal_item[trade_i];
        if d.amount == 0 {
            continue;
        }
        let n = d.index as usize;
        if n >= MAX_INVENTORY || !move_one(&mut inventory2, &mut inventory, n, d.amount) {
            return false;
        }
    }
    true
}

/// trade.cpp trade_tradeadditem: an item (server index) goes into the trade window.
pub fn trade_tradeadditem(c: &Ctx, aid: u32, index: i16, amount: i16) {
    let st = tx(c, aid);
    if !trading(c, aid) || st.deal_locked > 0 {
        return; // Can't add stuff.
    }
    let Some(tsd) = id2sd(c, st.partner_id) else {
        trade_tradecancel(c, aid);
        return;
    };
    let Some(sd) = unit::get(c, aid) else { return };
    if amount == 0 {
        // Why do this.. ~.~ just send an ack, the item won't display on the trade window.
        clif_tradeitemok(c, aid, -2, EXITEM_ADD_SUCCEED); // We pass -2 which will becomes 0 in clif_tradeitemok (Official behavior)
        return;
    }
    // Item checks...
    if index < 0 || index as usize >= MAX_INVENTORY {
        return;
    }
    let slot = inv::inv_get(c, aid, index as usize);
    let have = slot.as_ref().map(|r| r.item.amount).unwrap_or(0);
    if amount < 0 || amount > have {
        return;
    }
    let Some(slot) = slot else { return };
    let item = slot.item.clone();
    let src_lv = pc::group_level(c, aid);
    let dst_lv = pc::group_level(c, tsd.id);
    let cantrade = itemdb::itemdb_isrestricted(item.nameid, &item.cards(), src_lv, dst_lv, itemdb::itemdb_cantrade_sub);
    let partner = pc::character_of(c, aid).map(|ch| ch.partner_id).unwrap_or(0);
    let is_partner = partner != 0 && pc::charid2sd(c, partner).map(|u| u.id == tsd.id).unwrap_or(false);
    let canpartner = itemdb::itemdb_isrestricted(item.nameid, &item.cards(), src_lv, dst_lv, itemdb::itemdb_canpartnertrade_sub);
    if !cantrade && (!is_partner || !canpartner) {
        // Can't trade / can't partner-trade
        clif::displaymessage(c, &sd, crate::data::msg_txt(260));
        clif_tradeitemok(c, aid, index as i32, EXITEM_ADD_FAILED_CLOSED);
        return;
    }
    if itemdb::itemdb_ishatched_egg(item.card0, item.attribute) {
        return;
    }
    if item.expire_time != 0 {
        // Rental System
        clif::displaymessage(c, &sd, crate::data::msg_txt(260));
        clif_tradeitemok(c, aid, index as i32, EXITEM_ADD_FAILED_CLOSED);
        return;
    }
    let guild = |id: u32| pc::character_of(c, id).map(|ch| ch.guild_id).unwrap_or(0);
    if ((item.bound == inv::BOUND_ACCOUNT || item.bound > inv::BOUND_GUILD) || (item.bound == inv::BOUND_GUILD && guild(aid) != guild(tsd.id))) && !inv::pc_can_give_bounded_items(c, aid) {
        // Item Bound
        clif::displaymessage(c, &sd, crate::data::msg_txt(293));
        clif_tradeitemok(c, aid, index as i32, EXITEM_ADD_FAILED_CLOSED);
        return;
    }
    if item.equip_switch != 0 {
        crate::clif_items::msg(c, &sd, MSI_SWAP_EQUIPITEM_UNREGISTER_FIRST);
        return;
    }
    let mut st = tx(c, aid);
    if item.bound != 0 {
        st.is_bound_trading |= 1 << item.bound;
        put(c, &st);
    }
    // Locate a trade position
    let Some(trade_i) = st.deal_item.iter().position(|d| d.index == index || d.amount == 0) else {
        // No space left: the client does not allow to add more than 10 items, and will show an error message.
        return;
    };
    let add_item = inv::pc_checkadditem(c, tsd.id, item.nameid, amount as i32);
    // Fail to add the item if is stackable and adding the traded amount will exceed the maximum
    if add_item == inv::CHKADDITEM_OVERAMOUNT {
        clif_tradeitemok(c, aid, index as i32, EXITEM_ADD_FAILED_EACHITEM_OVERCOUNT);
        return;
    }
    // Determines whether the item should be counted when checking for inventory space.
    let count_stackable = trade_count_stackable() || add_item != inv::CHKADDITEM_EXIST;
    // Fail to add the item if the inventory will be full
    if count_stackable && (inv::pc_inventoryblank(c, tsd.id) as i32) < st.deal_inventory_space + 1 {
        clif_tradeitemok(c, aid, index as i32, EXITEM_ADD_FAILED_OVERCOUNT);
        return;
    }
    let weight = inv::data_of(&item).weight as i32;
    let mut amount = amount;
    let mut trade_weight = weight.wrapping_mul(amount as i32);
    let ts = inv::st(c, tsd.id);
    let (tw, tmax) = ts.map(|s| (s.weight as i64, s.max_weight as i64)).unwrap_or((0, 0));
    if tw + st.deal_weight as i64 + trade_weight as i64 > tmax {
        // fail to add item -- the player was over weighted.
        clif_tradeitemok(c, aid, index as i32, EXITEM_ADD_FAILED_OVERWEIGHT);
        return;
    }
    if st.deal_item[trade_i].index == index {
        // The same item as before is being readjusted.
        if st.deal_item[trade_i].amount as i32 + amount as i32 > item.amount as i32 {
            // packet deal exploit check
            amount = item.amount - st.deal_item[trade_i].amount;
            trade_weight = weight.wrapping_mul(amount as i32);
        }
        st.deal_item[trade_i].amount += amount;
    } else {
        // New deal item
        st.deal_item[trade_i].index = index;
        st.deal_item[trade_i].amount = amount;
    }
    st.deal_weight += trade_weight;
    if count_stackable {
        st.deal_inventory_space += 1;
    }
    put(c, &st);
    clif_tradeitemok(c, aid, index as i32, EXITEM_ADD_SUCCEED); // Return the index as it was received
    clif_tradeadditem(c, aid, tsd.id, index as i32 + 2, amount as i32);
}

/// trade.cpp trade_tradeaddzeny.
pub fn trade_tradeaddzeny(c: &Ctx, aid: u32, amount: i32) {
    let st = tx(c, aid);
    if !trading(c, aid) || st.deal_locked > 0 {
        return; // Can't add stuff.
    }
    let Some(tsd) = id2sd(c, st.partner_id) else {
        trade_tradecancel(c, aid);
        return;
    };
    let z = pc::character_of(c, aid).map(|ch| ch.zeny as i64).unwrap_or(0);
    let tz = pc::character_of(c, tsd.id).map(|ch| ch.zeny as i64).unwrap_or(0);
    if amount < 0 || amount as i64 > z || amount as i64 > MAX_ZENY - tz {
        // invalid values, no appropriate packet for it => abort
        trade_tradecancel(c, aid);
        return;
    }
    let mut st = tx(c, aid);
    st.deal_zeny = amount;
    put(c, &st);
    clif_tradeadditem(c, aid, tsd.id, 0, amount);
}

/// trade.cpp trade_tradeok: the 'Ok' button.
pub fn trade_tradeok(c: &Ctx, aid: u32) {
    let mut st = tx(c, aid);
    if st.deal_locked != 0 || !trading(c, aid) {
        return;
    }
    let Some(tsd) = id2sd(c, st.partner_id) else {
        trade_tradecancel(c, aid);
        return;
    };
    st.deal_locked = 1;
    put(c, &st);
    clif_tradeitemok(c, aid, -2, EXITEM_ADD_SUCCEED); // We pass -2 which will becomes 0 in clif_tradeitemok (Official behavior)
    clif_tradedeal_lock(c, aid, false);
    clif_tradedeal_lock(c, tsd.id, true);
}

/// trade.cpp trade_tradecancel: 'Cancel' pressed (or the trade was force-cancelled by the code); the deal's items and
/// zeny go back (only virtually: they never left).
pub fn trade_tradecancel(c: &Ctx, aid: u32) {
    let mut st = tx(c, aid);
    let target = id2sd(c, st.partner_id).map(|u| u.id);
    st.is_bound_trading = 0;
    put(c, &st);
    if !trading(c, aid) {
        // Not trade accepted
        if let Some(t) = target {
            let mut tt = tx(c, t);
            tt.partner_id = 0;
            tt.partner_lv = 0;
            put(c, &tt);
            clif_tradecancelled(c, t);
        }
        let mut st = tx(c, aid);
        st.partner_id = 0;
        st.partner_lv = 0;
        put(c, &st);
        clif_tradecancelled(c, aid);
        return;
    }
    give_back(c, aid);
    let mut st = tx(c, aid);
    st.deal_locked = 0;
    st.partner_id = 0;
    st.partner_lv = 0;
    put(c, &st);
    set_trading(c, aid, false);
    clif_tradecancelled(c, aid);
    let Some(t) = target else { return };
    give_back(c, t);
    let mut tt = tx(c, t);
    tt.deal_locked = 0;
    tt.partner_id = 0;
    tt.partner_lv = 0;
    put(c, &tt);
    set_trading(c, t, false);
    clif_tradecancelled(c, t);
}

/// trade_tradecancel's "give items back (only virtual)" and zeny refresh for one side.
fn give_back(c: &Ctx, aid: u32) {
    let mut st = tx(c, aid);
    for d in st.deal_item.iter_mut() {
        if d.amount == 0 {
            continue;
        }
        crate::clif_items::additem(c, aid, d.index as i32, d.amount as i32, 0);
        d.index = 0;
        d.amount = 0;
    }
    if st.deal_zeny != 0 {
        inv::updatestatus(c, aid, inv::SP_ZENY);
        st.deal_zeny = 0;
    }
    put(c, &st);
}

/// trade.cpp trade_tradecommit: the 'Trade' button; when both sides pressed it, the deal is checked and executed.
pub fn trade_tradecommit(c: &Ctx, aid: u32) {
    let mut st = tx(c, aid);
    if !trading(c, aid) || st.deal_locked == 0 {
        return; // Locked should be 1 (pressed ok) before you can press trade.
    }
    let Some(tsd) = id2sd(c, st.partner_id) else {
        trade_tradecancel(c, aid);
        return;
    };
    let tid = tsd.id;
    st.deal_locked = 2;
    put(c, &st);
    if tx(c, tid).deal_locked < 2 {
        return; // Not yet time for trading.
    }
    // Now is a good time (to save on resources) to check that the trade can indeed be made and it's not exploitable.
    // check exploit (trade more items that you have)
    let h = impossible_trade_check(c, aid);
    if h.result != 0 {
        trade_tradecancel(c, aid);
        hack_followup(c, aid, h);
        return;
    }
    // check exploit (trade more items that you have)
    let h = impossible_trade_check(c, tid);
    if h.result != 0 {
        trade_tradecancel(c, tid);
        hack_followup(c, tid, h);
        return;
    }
    // check for full inventory (can not add traded items)
    if !trade_check(c, aid, tid) {
        // check the both players
        trade_tradecancel(c, aid);
        return;
    }
    // trade is accepted and correct.
    for trade_i in 0..10 {
        move_item(c, aid, tid, trade_i);
        move_item(c, tid, aid, trade_i);
    }
    let sz = tx(c, aid).deal_zeny;
    if sz != 0 {
        inv::pc_payzeny(c, aid, sz as i64, crate::map_log::LOG_TYPE_TRADE, crate::map_log::char_id_of(c, tid));
        inv::pc_getzeny(c, tid, sz as i64, crate::map_log::LOG_TYPE_TRADE, crate::map_log::char_id_of(c, aid));
        let mut s = tx(c, aid);
        s.deal_zeny = 0;
        put(c, &s);
    }
    let tz = tx(c, tid).deal_zeny;
    if tz != 0 {
        inv::pc_payzeny(c, tid, tz as i64, crate::map_log::LOG_TYPE_TRADE, crate::map_log::char_id_of(c, aid));
        inv::pc_getzeny(c, aid, tz as i64, crate::map_log::LOG_TYPE_TRADE, crate::map_log::char_id_of(c, tid));
        let mut t = tx(c, tid);
        t.deal_zeny = 0;
        put(c, &t);
    }
    for id in [aid, tid] {
        let mut t = tx(c, id);
        t.deal_locked = 0;
        t.partner_id = 0;
        t.partner_lv = 0;
        t.is_bound_trading = 0;
        put(c, &t);
        set_trading(c, id, false);
    }
    clif_tradecompleted(c, aid);
    clif_tradecompleted(c, tid);
    // save both player to avoid crash: they always have no advantage/disadvantage between the 2 players
    if CHARSAVE_TRADE {
        chrif_save_inventory_cart(c, aid);
        chrif_save_inventory_cart(c, tid);
    }
}

/// One entry of trade_tradecommit's loop: `from`'s deal item `trade_i` goes to `to` (pc_additem with the inventory
/// slot itself, then pc_delitem type 1 reason 6 — the client already took the item away — or clif_additem back).
fn move_item(c: &Ctx, from: u32, to: u32, trade_i: usize) {
    let mut st = tx(c, from);
    let d = st.deal_item[trade_i];
    if d.amount == 0 {
        return;
    }
    let n = d.index as usize;
    if let Some(r) = inv::inv_get(c, from, n) {
        let mut it = r.item.clone();
        let flag = inv::pc_additem(c, to, &mut it, d.amount as i32, crate::map_log::LOG_TYPE_TRADE);
        // pc_additem may give the source slot a unique id (it receives &sd->inventory.u.items_inventory[n])
        if it.unique_id != r.item.unique_id {
            if let Some(mut r2) = inv::inv_get(c, from, n) {
                r2.item.unique_id = it.unique_id;
                inv::inv_put(c, &r2);
            }
        }
        if flag == 0 {
            inv::pc_delitem(c, from, n as i32, d.amount as i32, 1, 6, crate::map_log::LOG_TYPE_TRADE);
        } else {
            crate::clif_items::additem(c, from, n as i32, d.amount as i32, 0);
        }
    } else {
        // (an empty slot: pc_additem refuses a nameid 0 item, the client gets its item back)
        crate::clif_items::additem(c, from, n as i32, d.amount as i32, 0);
    }
    st = tx(c, from);
    st.deal_item[trade_i] = DealItem::default();
    put(c, &st);
}

/// chrif_save(sd, CSAVE_INVENTORY | CSAVE_CART) (chrif.rs).
fn chrif_save_inventory_cart(c: &Ctx, aid: u32) {
    crate::chrif::chrif_save(c, aid, crate::chrif::CSAVE_INVENTORY | crate::chrif::CSAVE_CART);
}

/// party.cpp party_trade_bound_cancel / guild.cpp guild_trade_bound_cancel (BOUND_ITEMS): a trade with a party-
/// (guild-) bound item is cancelled when the player leaves the party (guild). `bound` is BOUND_PARTY or BOUND_GUILD.
pub fn trade_bound_cancel(c: &Ctx, aid: u32, bound: u8) {
    if tx(c, aid).is_bound_trading & (1 << bound) != 0 {
        trade_tradecancel(c, aid);
    }
}

/// unit.cpp unit_remove_map: `if(sd->trade_partner.id > 0) trade_tradecancel(sd)`.
pub fn on_remove_map(c: &Ctx, aid: u32) {
    if tx(c, aid).partner_id > 0 {
        trade_tradecancel(c, aid);
    }
}

// ---------------------------------------------------------------------------------------------- clif senders

fn send_self(c: &Ctx, aid: u32, b: &[u8]) {
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, b, &sd, Target::SelfOnly);
    }
}

/// clif.cpp clif_traderequest: ZC_REQ_EXCHANGE_ITEM2 (0x01f4) to the requested player.
fn clif_traderequest(c: &Ctx, aid: u32, name: &str) {
    let st = tx(c, aid);
    let p = PACKET_ZC_REQ_EXCHANGE_ITEM { packetType: PACKET_ZC_REQ_EXCHANGE_ITEM::ID, requesterName: name.to_string(), targetId: st.partner_id, targetLv: st.partner_lv };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_traderesponse: ZC_ACK_EXCHANGE_ITEM2 (0x01f5) with the current partner.
pub fn clif_traderesponse(c: &Ctx, aid: u32, result: u8) {
    let st = tx(c, aid);
    let p = PACKET_ZC_ACK_EXCHANGE_ITEM { packetType: PACKET_ZC_ACK_EXCHANGE_ITEM::ID, result, targetId: st.partner_id, targetLv: st.partner_lv };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_tradeadditem: ZC_ADD_EXCHANGE_ITEM (0x0b42) to the partner; `index` is the client's index (0: zeny).
fn clif_tradeadditem(c: &Ctx, aid: u32, tid: u32, index: i32, amount: i32) {
    if !pc::is_active_session(c, tid) {
        return;
    }
    let mut p = PACKET_ZC_ADD_EXCHANGE_ITEM { option_data: vec![ItemOptions::default(); 5], ..Default::default() };
    if index != 0 {
        let idx = index - 2;
        let Some(r) = (if idx >= 0 && (idx as usize) < MAX_INVENTORY { inv::inv_get(c, aid, idx as usize) } else { None }) else { return };
        let it = &r.item;
        let data = inv::data_of(it);
        p.itemId = itemdb::client_nameid(it.nameid);
        p.itemType = data.r#type;
        p.identified = it.identify;
        p.damaged = it.attribute;
        p.refine = it.refine;
        p.slot = crate::clif_items::addcards(Some(it));
        p.option_data = crate::clif_items::add_random_options(it);
        p.location = crate::pc_equip::pc_equippoint_sub(c, aid, data);
        p.look = data.look as u16;
        p.grade = it.enchantgrade;
    }
    p.packetType = PACKET_ZC_ADD_EXCHANGE_ITEM::ID as i16;
    p.amount = amount;
    send_self(c, tid, &p.to_vec());
}

/// clif.cpp clif_tradeitemok: ZC_ACK_ADD_EXCHANGE_ITEM (0x00ea), the server index as the client's.
fn clif_tradeitemok(c: &Ctx, aid: u32, index: i32, result: u8) {
    let p = PACKET_ZC_ACK_ADD_EXCHANGE_ITEM { packetType: PACKET_ZC_ACK_ADD_EXCHANGE_ITEM::ID as i16, index: (index + 2) as u16, result };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_tradedeal_lock: ZC_CONCLUDE_EXCHANGE_ITEM (0 self, 1 the other player).
fn clif_tradedeal_lock(c: &Ctx, aid: u32, who: bool) {
    let p = PACKET_ZC_CONCLUDE_EXCHANGE_ITEM { packetType: PACKET_ZC_CONCLUDE_EXCHANGE_ITEM::ID as i16, who: who as u8 };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_tradecancelled: ZC_CANCEL_EXCHANGE_ITEM.
fn clif_tradecancelled(c: &Ctx, aid: u32) {
    let p = PACKET_ZC_CANCEL_EXCHANGE_ITEM { packetType: PACKET_ZC_CANCEL_EXCHANGE_ITEM::ID as i16 };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_tradecompleted: ZC_EXEC_EXCHANGE_ITEM (0 = success).
fn clif_tradecompleted(c: &Ctx, aid: u32) {
    let p = PACKET_ZC_EXEC_EXCHANGE_ITEM { packetType: PACKET_ZC_EXEC_EXCHANGE_ITEM::ID as i16, result: 0 };
    send_self(c, aid, &p.to_vec());
}

// ---------------------------------------------------------------------------------------------- clif parsers

fn pos_reader(pkt: &[u8], i: usize) -> Reader<'_> {
    let cmd = u16::from_le_bytes([pkt[0], pkt[1]]);
    let pos = packet_pos(cmd);
    Reader::at(pkt, pos.get(i).copied().unwrap_or(2) as usize)
}

/// clif.cpp clif_parse_TradeRequest (CZ_REQ_EXCHANGE_ITEM 0x00e4 <account id>.L).
pub fn parse_trade_request(c: &Ctx, aid: u32, pkt: &[u8]) {
    let t_id = pos_reader(pkt, 0).u32().unwrap_or(0);
    let t_sd = id2sd(c, t_id);
    let Some(p) = pc::get(c, aid) else { return };
    let Some(sd) = unit::get(c, aid) else { return };
    if p.chat_id == 0 && pc::cant_act(&p, sd.sc.opt1) {
        return; // You can trade while in a chatroom.
    }
    if let Some(t) = &t_sd {
        // @noask [LuzZza]
        if crate::social::sx(c, t.id).noask {
            crate::social::clif_noask_sub(c, &sd, t, 393); // Autorejected trade request from %s.
            return;
        }
        if pc::get(c, t.id).map(|tp| tp.mail_writing).unwrap_or(false) {
            // Fake trading
            clif_traderesponse(c, aid, TRADE_ACK_BUSY);
            return;
        }
    }
    if crate::conf::basic_skill_check() && crate::skill::checkskill(c, aid, 1) < 1 && crate::skill::checkskill(c, aid, 5018) < 1 {
        crate::skill::skill_fail(c, &sd, 1, 0);
        return;
    }
    trade_traderequest(c, aid, t_sd.map(|u| u.id));
}

/// clif.cpp clif_parse_TradeAck (CZ_ACK_EXCHANGE_ITEM 0x00e6 <result>.B).
pub fn parse_trade_ack(c: &Ctx, aid: u32, pkt: &[u8]) {
    let kind = pos_reader(pkt, 0).u8().unwrap_or(0);
    trade_tradeack(c, aid, kind);
}

/// clif.cpp clif_parse_TradeAddItem (CZ_ADD_EXCHANGE_ITEM 0x00e8 <index>.W <amount>.L): index 0 is zeny.
pub fn parse_trade_add_item(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_ADD_EXCHANGE_ITEM::read(&mut Reader::new(pkt)) else { return };
    if p.index == 0 {
        trade_tradeaddzeny(c, aid, p.amount);
    } else {
        trade_tradeadditem(c, aid, (p.index as i32 - 2) as i16, p.amount as i16);
    }
}

/// clif.cpp clif_parse_TradeOk (CZ_CONCLUDE_EXCHANGE_ITEM 0x00eb).
pub fn parse_trade_ok(c: &Ctx, aid: u32) {
    trade_tradeok(c, aid);
}

/// clif.cpp clif_parse_TradeCancel (CZ_CANCEL_EXCHANGE_ITEM 0x00ed).
pub fn parse_trade_cancel(c: &Ctx, aid: u32) {
    trade_tradecancel(c, aid);
}

/// clif.cpp clif_parse_TradeCommit (CZ_EXEC_EXCHANGE_ITEM 0x00ef).
pub fn parse_trade_commit(c: &Ctx, aid: u32) {
    trade_tradecommit(c, aid);
}
