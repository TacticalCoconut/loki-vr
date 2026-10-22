//! map/vending.cpp (player shops: open, close, the item list, purchases with the vending tax, the store-search
//! hooks, the autotrade reopen and the SQL mirror), the vending packets of clif.cpp and their parsers, and the
//! Vending skill's own part (skills/merchant/skill_vending.cpp SkillVending::castendNoDamageId with the cart round
//! trip of intif.cpp that opens the shop window).
//!
//! State: sd->state.vending and sd->state.prevend stay the `pc.vending` / `pc.prevend` flags every other system
//! already tests; sd->vender_id, sd->vend_num + sd->vending[], sd->vended_id, sd->vend_skill_lv and
//! sd->state.pending_vending_ui are the [`PcVending`] row of an online character; sd->message (the shop title) is
//! shared with the buying store and lives in its row (buyingstore.rs `sd_message`). vending_db (the open shops by char
//! id) is the set of `pc` rows with `vending` set, in rAthena's idb iteration order where it is walked.
//!
//! rAthena mirrors every open shop into the SQL tables `vendings` / `vending_items` (its autotrade restore reads
//! them at the next start, the web service lists them): the tables [`Vendings`] / [`VendingItemRow`] here, written at
//! the same places. Deliberately different: vending_getuid's counter (a static rAthena starts again at 0 with every
//! map-server start) is a persistent counter, because the module's shops outlive what rAthena calls a restart and a
//! restarted counter would hand out the id of a shop that is still open (the `vendings` primary key).

use spacetimedb::{table, SpacetimeType, Table};

use crate::cart::cart_inventory as _;
use crate::clif::{self, Target};
use crate::db::items as itemdb;
use crate::inventory::{self as inv, MAX_CART, MAX_ZENY};
use crate::net::packets::*;
use crate::net::wire::{from_latin1, until_nul, Reader};
use crate::pc::{self, pc as _};
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// map.hpp MAX_VENDING, MESSAGE_SIZE.
pub const MAX_VENDING: usize = 12;
pub const MESSAGE_SIZE: usize = 80;
/// skill.hpp MC_VENDING.
pub const MC_VENDING: u16 = crate::db::skills::ids::MC_VENDING;

/// conf/battle/items.conf `vending_max_value: 1000000000`, `vending_over_max: yes`, `vending_tax: 500`,
/// `vending_tax_min: 100000000`, `buyer_name: yes`.
pub fn vending_max_value() -> u32 {
    crate::battle_config::get(crate::battle_config::Bc::VENDING_MAX_VALUE) as u32
}
pub fn vending_over_max() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::VENDING_OVER_MAX) != 0
}
pub fn vending_tax() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::VENDING_TAX)
}
pub fn vending_tax_min() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::VENDING_TAX_MIN)
}
pub fn buyer_name() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::BUYER_NAME) != 0
}
/// conf/map_athena.conf `save_settings: 4095` (CHARSAVE_VENDING 0x002).
pub const CHARSAVE_VENDING: bool = true;
/// status.hpp MANNER_NOROOM; script_constants MF_NOVENDING; map.hpp CELL_CHKNOVENDING.
const MANNER_NOROOM: i32 = 0x10;
pub const MF_NOVENDING: i16 = inv::MF_NOVENDING;

/// clif.hpp e_pc_purchase_result_frommc.
pub const PURCHASEMC_SUCCESS: u8 = 0;
pub const PURCHASEMC_NO_ZENY: u8 = 1;
pub const PURCHASEMC_OVERWEIGHT: u8 = 2;
pub const PURCHASEMC_OUT_OF_STOCK: u8 = 4;
pub const PURCHASEMC_TRADING: u8 = 5;
pub const PURCHASEMC_STORE_INCORRECT: u8 = 6;
pub const PURCHASEMC_NO_SALES_INFO: u8 = 7;
/// clif.hpp e_ack_openstore2.
pub const OPENSTORE2_SUCCESS: u8 = 0;
pub const OPENSTORE2_FAILED: u8 = 1;
pub const OPENSTORE2_NOVENDING: u8 = 3;
/// clif.hpp e_clif_messages MSI_MERCHANTSHOP_TOTA_LOVER_ZENY_ERR.
pub const MSI_MERCHANTSHOP_TOTA_LOVER_ZENY_ERR: u16 = 3826;

// ---------------------------------------------------------------------------------------------- state

/// vending.hpp struct s_vending: a cart index, the amount for sale, the price.
#[derive(SpacetimeType, Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct VendingItem {
    pub index: i16,
    pub amount: i16,
    pub value: u32,
}

/// sd->vender_id, sd->vending[0..vend_num] (vend_num is the length), sd->vended_id, sd->vend_skill_lv and
/// sd->state.pending_vending_ui of an online character.
#[table(accessor = pc_vending)]
#[derive(Clone, Debug)]
pub struct PcVending {
    #[primary_key]
    pub account_id: u32,
    pub vender_id: u32,
    pub vending: Vec<VendingItem>,
    /// The shop whose list the player has open (the vender id; clif_parse_PurchaseReq buys from it).
    pub vended_id: u32,
    pub vend_skill_lv: u16,
    pub pending_vending_ui: bool,
}

fn fresh(aid: u32) -> PcVending {
    PcVending { account_id: aid, vender_id: 0, vending: Vec::new(), vended_id: 0, vend_skill_lv: 0, pending_vending_ui: false }
}

/// The vending state of `aid` (a zeroed one when it has none yet).
pub fn vd(c: &Ctx, aid: u32) -> PcVending {
    c.db().pc_vending().account_id().find(aid).unwrap_or_else(|| fresh(aid))
}

fn put(c: &Ctx, v: &PcVending) {
    if c.db().pc_vending().account_id().find(v.account_id).is_some() {
        c.db().pc_vending().account_id().update(v.clone());
    } else {
        c.db().pc_vending().insert(v.clone());
    }
}

/// unit_free: the state goes with the session.
pub fn on_unit_free(c: &Ctx, aid: u32) {
    c.db().pc_vending().account_id().delete(aid);
    c.db().pc_cart_ids().account_id().delete(aid);
}

fn is_vending(c: &Ctx, aid: u32) -> bool {
    pc::get(c, aid).map(|p| p.vending).unwrap_or(false)
}

/// sd->vender_id (0 when the player has no shop).
pub fn vender_id(c: &Ctx, aid: u32) -> u32 {
    c.db().pc_vending().account_id().find(aid).map(|v| v.vender_id).unwrap_or(0)
}

fn set_prevend(c: &Ctx, aid: u32, on: bool) {
    if let Some(mut p) = pc::get(c, aid) {
        if p.prevend != on {
            p.prevend = on;
            pc::put(c, &p);
        }
    }
}

/// vending_getdb(): the open shops (account ids) in the iteration order of rAthena's idb keyed by char id.
pub fn vending_db(c: &Ctx) -> Vec<u32> {
    let mut v: Vec<(u32, u32)> = c.db().pc().iter().filter(|p| p.vending).map(|p| (p.char_id, p.account_id)).collect();
    v.sort_by_key(|&(cid, _)| crate::party::idb_order(cid));
    v.into_iter().map(|(_, aid)| aid).collect()
}

/// vending.cpp vending_purchasereq / vending_openvending: chrif_save(sd, CSAVE_INVENTORY|CSAVE_CART) for a player
/// who stays (chrif.rs).
pub fn chrif_save_inventory_cart(c: &Ctx, aid: u32) {
    crate::chrif::chrif_save(c, aid, crate::chrif::CSAVE_INVENTORY | crate::chrif::CSAVE_CART);
}

/// map_getcell(m, x, y, CELL_CHKNOVENDING).
fn cell_novending(c: &Ctx, m: u16, x: i16, y: i16) -> bool {
    crate::map_cell::getcell(c, Some(m), x, y, crate::map_cell::CELL_CHKNOVENDING) != 0
}

/// sd->cart.u.items_cart[idx] as rAthena holds it (a zeroed item for an empty slot).
fn cart_item(c: &Ctx, aid: u32, idx: usize) -> inv::Item {
    crate::cart::cart_get(c, aid, idx).map(|r| r.item).unwrap_or_default()
}

// ---------------------------------------------------------------------------------------------- the cart's SQL ids

/// sd->cart.u.items_cart[i].id: the SQL id each cart slot had when the char server last sent the cart
/// (intif_parse_StorageReceived: at login, and when the Vending skill reloads it). rAthena never updates these in
/// the map server's copy when it saves (the char server assigns ids to new rows, the map server keeps 0 until the
/// next load), a new slot starts at 0 (pc_cart_additem: `items_cart[i].id = 0`) and a stack keeps its slot's id. The
/// module's cart rows carry the saved id (`save_id`) instead, so the loaded ids are remembered here per cart row: a
/// row that is not in the list (a slot made after the load) has id 0.
#[table(accessor = pc_cart_ids)]
#[derive(Clone, Debug)]
pub struct PcCartIds {
    #[primary_key]
    pub account_id: u32,
    pub ids: Vec<CartMemId>,
}

#[derive(SpacetimeType, Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct CartMemId {
    /// The `cart_inventory` row (its auto-increment id).
    pub row_id: u64,
    /// The SQL id the slot got with the load.
    pub id: u64,
}

/// intif_parse_StorageReceived(TABLE_CART)'s memcpy: the cart as loaded now fixes the slots' ids (called by
/// cart.rs cart_received, after char_memitemdata_from_sql's order is applied).
pub fn cart_ids_loaded(c: &Ctx, aid: u32) {
    let ids: Vec<CartMemId> = crate::cart::cart_list(c, aid).iter().map(|r| CartMemId { row_id: r.id, id: r.save_id }).collect();
    let row = PcCartIds { account_id: aid, ids };
    if c.db().pc_cart_ids().account_id().find(aid).is_some() {
        c.db().pc_cart_ids().account_id().update(row);
    } else {
        c.db().pc_cart_ids().insert(row);
    }
}

/// sd->cart.u.items_cart[idx].id (0 for an empty slot or a slot made since the load).
pub fn cart_mem_id(c: &Ctx, aid: u32, idx: usize) -> u64 {
    let Some(r) = crate::cart::cart_get(c, aid, idx) else { return 0 };
    let Some(ids) = c.db().pc_cart_ids().account_id().find(aid) else { return 0 };
    ids.ids.iter().find(|e| e.row_id == r.id).map(|e| e.id).unwrap_or(0)
}

// ---------------------------------------------------------------------------------------------- SQL mirror

/// rAthena's SQL table `vendings` (sql-files/main.sql): one row per open shop, read by the autotrade restore.
#[table(accessor = vendings)]
#[derive(Clone, Debug)]
pub struct Vendings {
    #[primary_key]
    pub id: u32,
    pub account_id: u32,
    pub char_id: u32,
    /// enum('F','M').
    pub sex: String,
    pub map: String,
    pub x: u16,
    pub y: u16,
    pub title: String,
    pub body_direction: u8,
    pub head_direction: u8,
    pub sit: u8,
    pub autotrade: i8,
}

/// rAthena's SQL table `vending_items` (primary key vending_id + index; the row id here is only a handle).
#[table(accessor = vending_items, index(accessor = by_vending, btree(columns = [vending_id])))]
#[derive(Clone, Debug)]
pub struct VendingItemRow {
    #[primary_key]
    #[auto_inc]
    pub row_id: u64,
    pub vending_id: u32,
    pub index: u16,
    pub cartinventory_id: u32,
    pub amount: u16,
    pub price: u32,
}

/// `DELETE FROM vending_items WHERE vending_id = id` + `DELETE FROM vendings WHERE id = id`.
fn sql_delete_shop(c: &Ctx, id: u32) {
    let rows: Vec<u64> = c.db().vending_items().by_vending().filter(id).map(|r| r.row_id).collect();
    for r in rows {
        c.db().vending_items().row_id().delete(r);
    }
    c.db().vendings().id().delete(id);
}

/// The rows of one shop in `index` order (`ORDER BY index ASC`).
pub fn sql_items_of(c: &Ctx, id: u32) -> Vec<VendingItemRow> {
    let mut v: Vec<VendingItemRow> = c.db().vending_items().by_vending().filter(id).collect();
    v.sort_by_key(|r| (r.index, r.row_id));
    v
}

/// The char's sex as the SQL rows store it (`sd.status.sex == SEX_FEMALE ? 'F' : 'M'`).
pub fn sex_char(sex: u8) -> String {
    if sex == 0 { "F".into() } else { "M".into() }
}

// ---------------------------------------------------------------------------------------------- vending.cpp

/// vending.cpp vending_getuid (see the module doc for the counter).
fn vending_getuid(c: &Ctx) -> u32 {
    crate::ids::next(c.rc, "vender_id", 1)
}

/// vending.cpp vending_closevending: the shop closes (SQL rows, state, the board goes from the area).
pub fn vending_closevending(c: &Ctx, aid: u32) {
    if !is_vending(c, aid) {
        return;
    }
    let mut v = vd(c, aid);
    sql_delete_shop(c, v.vender_id);
    if let Some(mut p) = pc::get(c, aid) {
        p.vending = false;
        pc::put(c, &p);
    }
    v.vender_id = 0;
    put(c, &v);
    if let Some(sd) = unit::get(c, aid) {
        clif_closevendingboard(c, &sd, None);
    }
    // idb_remove(vending_db, sd->status.char_id): the db is the `pc.vending` flag
}

/// vending.cpp vending_vendinglistreq: `aid` asks for the item list of `id`'s shop.
pub fn vending_vendinglistreq(c: &Ctx, aid: u32, id: u32) {
    if pc::get(c, aid).is_none() {
        return;
    }
    // map_id2sd(id)
    if pc::get(c, id).is_none() {
        return;
    }
    if !is_vending(c, id) {
        return; // not vending
    }
    if !inv::pc_can_give_items(c, aid) || !inv::pc_can_give_items(c, id) {
        // check if both GMs are allowed to trade
        if let Some(sd) = unit::get(c, aid) {
            clif::displaymessage(c, &sd, crate::data::msg_txt(246)); // Your GM level doesn't authorize you to perform this action.
        }
        return;
    }
    let mut v = vd(c, aid);
    v.vended_id = vd(c, id).vender_id; // register vending uid
    put(c, &v);
    clif_vendinglist(c, aid, id);
}

/// vending.cpp vending_calc_tax: the vending tax on a sale (`zeny -= zeny * (vending_tax / 10000.)` from
/// vending_tax_min up), in the double arithmetic rAthena uses.
pub fn vending_calc_tax(zeny: f64) -> f64 {
    if vending_tax() != 0 && zeny >= vending_tax_min() as f64 {
        zeny - zeny * (vending_tax() as f64 / 10000.)
    } else {
        zeny
    }
}

/// C's `(int32)` conversion of a double (truncation toward zero; out-of-range values saturate here, which x86-64's
/// cvttsd2si would turn into INT_MIN: the amounts rAthena lets through never reach that range).
fn to_i32(z: f64) -> i32 {
    z as i32
}

/// vending.cpp vending_purchasereq: `aid` buys `data` = (amount, index) pairs (the client's values: index is the
/// cart index + 2) from `vender`'s shop `uid`.
pub fn vending_purchasereq(c: &Ctx, aid: u32, vender: u32, uid: u32, data: &[(u16, u16)], count: i32) {
    let Some(sd) = unit::get(c, aid) else { return };
    // map_id2sd(aid) of the vender
    let (Some(vsd), Some(vp)) = (unit::get(c, vender), pc::get(c, vender)) else { return };
    if !vp.vending || vsd.id == sd.id {
        return; // invalid shop
    }
    let vv = vd(c, vender);
    if vv.vender_id != uid {
        // shop has changed
        clif_buyvending(c, &sd, 0, 0, PURCHASEMC_STORE_INCORRECT); // store information was incorrect
        return;
    }
    if !crate::searchstore::searchstore_queryremote(c, aid, vender) && (sd.map != vsd.map || !crate::path::check_distance((sd.x - vsd.x) as i32, (sd.y - vsd.y) as i32, crate::conf::area_size() as i32)) {
        return; // shop too far away
    }
    crate::searchstore::searchstore_clearremote(c, aid);
    let vend_num = vv.vending.len();
    if count < 1 || count as usize > MAX_VENDING || count as usize > vend_num {
        return; // invalid amount of purchased items
    }
    let count = count as usize;
    let blank = inv::pc_inventoryblank(c, aid) as i32; //number of free cells in the buyer's inventory
    // duplicate item in vending to check hacker with multiple packets
    let mut vending = vv.vending.clone();
    let (Some(ch), Some(vch)) = (pc::character_of(c, aid), pc::character_of(c, vender)) else { return };
    let (Some(st), Some(_)) = (inv::st(c, aid), inv::st(c, vender)) else { return };
    let mut vend_list = [0usize; MAX_VENDING];
    let mut new_ = 0i32;
    // some checks
    let mut z = 0f64; // zeny counter
    let mut w: i32 = 0; // weight counter
    for i in 0..count {
        let amount = data[i].0 as i16;
        let idx = (data[i].1 as i16).wrapping_sub(2);
        if amount <= 0 {
            return;
        }
        // check of item index in the cart
        if idx < 0 || idx as usize >= MAX_CART {
            return;
        }
        let Some(j) = vv.vending.iter().position(|e| e.index == idx) else {
            return; //picked non-existing item
        };
        vend_list[i] = j;
        z += vv.vending[j].value as f64 * amount as f64;
        if z > ch.zeny as f64 || z < 0. || z > MAX_ZENY as f64 {
            clif_buyvending(c, &sd, idx as u16, amount as u16, PURCHASEMC_NO_ZENY); // you don't have enough zeny
            return;
        }
        if z + vch.zeny as f64 > MAX_ZENY as f64 {
            clif_buyvending(c, &sd, idx as u16, vv.vending[j].amount as u16, PURCHASEMC_OUT_OF_STOCK); // too much zeny = overflow
            return;
        }
        let citem = cart_item(c, vender, idx as usize);
        w = (w as u32).wrapping_add(itemdb::itemdb_weight(citem.nameid).wrapping_mul(amount as i32 as u32)) as i32;
        if (w as u32).wrapping_add(st.weight) > st.max_weight {
            clif_buyvending(c, &sd, idx as u16, amount as u16, PURCHASEMC_OVERWEIGHT);
            return;
        }
        //Check to see if cart/vend info is in sync.
        if vending[j].amount > citem.amount {
            vending[j].amount = citem.amount;
        }
        // if they try to add packets (example: get twice or more 2 apples if marchand has only 3 apples).
        // here, we check cumulative amounts
        if vending[j].amount < amount {
            // send more quantity is not a hack (an other player can have buy items just before)
            clif_buyvending(c, &sd, idx as u16, vv.vending[j].amount as u16, PURCHASEMC_OUT_OF_STOCK);
            return;
        }
        vending[j].amount -= amount;
        match inv::pc_checkadditem(c, aid, citem.nameid, amount as i32) {
            inv::CHKADDITEM_EXIST => {} //We'd add this item to the existing one (in buyers inventory)
            inv::CHKADDITEM_NEW => {
                new_ += 1;
                if new_ > blank {
                    return; //Buyer has no space in his inventory
                }
            }
            _ => return, //too many items
        }
    }
    inv::pc_payzeny(c, aid, to_i32(z) as i64, crate::map_log::LOG_TYPE_VENDING, crate::map_log::char_id_of(c, vender));
    crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_SPEND_ZENY, &[to_i32(z)]);
    let z_taxed = vending_calc_tax(z);
    inv::pc_getzeny(c, vender, to_i32(z_taxed) as i64, crate::map_log::LOG_TYPE_VENDING, crate::map_log::char_id_of(c, aid));
    let buyer_cid = ch.id;
    for i in 0..count {
        let amount = data[i].0 as i16;
        let idx = (data[i].1 as i16).wrapping_sub(2);
        // vending item
        if let Some(r) = crate::cart::cart_get(c, vender, idx as usize) {
            let mut it = r.item.clone();
            inv::pc_additem(c, aid, &mut it, amount as i32, crate::map_log::LOG_TYPE_VENDING);
            // pc_additem may have given the vender's cart item a unique id (it gets &vsd->cart.u.items_cart[idx])
            if it.unique_id != r.item.unique_id {
                if let Some(mut r2) = crate::cart::cart_get(c, vender, idx as usize) {
                    r2.item.unique_id = it.unique_id;
                    c.db().cart_inventory().id().update(r2);
                }
            }
        }
        let mut v = vd(c, vender);
        let l = vend_list[i];
        v.vending[l].amount -= amount;
        let left = v.vending[l].amount;
        let value = v.vending[l].value;
        let vender_id = v.vender_id;
        put(c, &v);
        let z = value as f64 * amount as f64;
        let cid = cart_mem_id(c, vender, idx as usize) as u32;
        if left != 0 {
            // UPDATE `vending_items` SET `amount` = … WHERE `vending_id` = … and `cartinventory_id` = …
            let rows: Vec<VendingItemRow> = c.db().vending_items().by_vending().filter(vender_id).filter(|r| r.cartinventory_id == cid).collect();
            for mut r in rows {
                r.amount = left as u16;
                c.db().vending_items().row_id().update(r);
            }
        } else {
            // DELETE FROM `vending_items` WHERE `vending_id` = … and `cartinventory_id` = …
            let rows: Vec<u64> = c.db().vending_items().by_vending().filter(vender_id).filter(|r| r.cartinventory_id == cid).map(|r| r.row_id).collect();
            for r in rows {
                c.db().vending_items().row_id().delete(r);
            }
        }
        crate::cart::pc_cart_delitem(c, vender, idx as i32, amount as i32, 0, crate::map_log::LOG_TYPE_VENDING);
        let z = vending_calc_tax(z);
        clif_vendingreport(c, vender, idx as u16, amount as u16, buyer_cid, to_i32(z));
        //print buyer's name
        if buyer_name() {
            let temp = crate::data::format_msg(crate::data::msg_txt(265), &[&sd.name]); // %s has bought your item(s).
            if let Some(vsd) = unit::get(c, vender) {
                clif::messagecolor_target(c, &vsd, crate::social::COLOR_LIGHT_GREEN, &crate::net::wire::latin1(&temp), false, Target::SelfOnly, None);
            }
        }
    }
    // compact the vending list
    let mut v = vd(c, vender);
    v.vending.retain(|e| e.amount != 0);
    put(c, &v);
    //Always save BOTH: customer (buyer) and vender
    if CHARSAVE_VENDING {
        chrif_save_inventory_cart(c, aid);
        chrif_save_inventory_cart(c, vender);
    }
    //check for @AUTOTRADE users [durf]
    if crate::autotrade::autotrade(c, vender) != 0 {
        //see if there is anything left in the shop
        if !vd(c, vender).vending.iter().any(|e| e.amount > 0) {
            //Close Vending (this was automatically done by the client, we have to do it manually for autovenders) [Skotlex]
            vending_closevending(c, vender);
            pc::quit(c, vender); //They have no reason to stay around anymore, do they?
        }
    }
}

/// One entry of the item list vending_openvending receives (`{<index>.w <amount>.w <value>.l}`, raw).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct OpenEntry {
    pub index: u16,
    pub amount: u16,
    pub value: u32,
}

/// vending.cpp vending_openvending: 0 success, 1 cannot open (dead, not prevend, trading, over the zeny limit), 2 no
/// cart / skill, 3 count, 5 an item that cannot be sold / no item. `at`: the autotrader being restored (its direction
/// and sitting go into the SQL row), None when the player opens the shop.
pub fn vending_openvending(c: &Ctx, aid: u32, message: &str, data: &[OpenEntry], at: Option<&crate::autotrade::Autotrader>) -> i8 {
    let (Some(sd), Some(p)) = (unit::get(c, aid), pc::get(c, aid)) else { return 1 };
    if pc::isdead(c, aid) || !p.prevend || inv::pc_istrading(&p) {
        return 1; // can't open vendings lying dead || didn't use via the skill (wpe/hack) || can't have 2 shops at once
    }
    let vending_skill_lvl = crate::skill::checkskill(c, aid, MC_VENDING) as usize;
    let fail = |c: &Ctx| {
        crate::skill::skill_fail(c, &sd, MC_VENDING, 0);
        set_prevend(c, aid, false);
        crate::itemui::set_workinprogress(c, aid, crate::itemui::WIP_DISABLE_NONE);
        clif_openvending_ack(c, &sd, OPENSTORE2_FAILED);
    };
    // skill level and cart check
    if vending_skill_lvl == 0 || !inv::pc_iscarton(c, aid) {
        fail(c);
        return 2;
    }
    let count = data.len();
    // check number of items in shop
    if count < 1 || count > MAX_VENDING || count > 2 + vending_skill_lvl {
        // invalid item count
        fail(c);
        return 3;
    }
    if CHARSAVE_VENDING {
        // Avoid invalid data from saving
        chrif_save_inventory_cart(c, aid);
    }
    // filter out invalid items
    let gmlv = pc::group_level(c, aid);
    let mut list: Vec<VendingItem> = Vec::with_capacity(count);
    let mut total: i64 = 0;
    for e in data.iter() {
        let index = (e.index as i16).wrapping_sub(2); // offset adjustment (client says that the first cart position is 2)
        let amount = e.amount as i16;
        let value = e.value;
        if index < 0 || index as usize >= MAX_CART {
            continue; // invalid position
        }
        let it = cart_item(c, aid, index as usize);
        if crate::cart::pc_cartitem_amount(c, aid, index as usize, amount as i32) < 0 // invalid item or insufficient quantity
            //NOTE: official server does not do any of the following checks!
            || it.identify == 0 // unidentified item
            || it.attribute == 1 // broken item
            || it.expire_time != 0 // It should not be in the cart but just in case
            || (it.bound != 0 && !inv::pc_can_give_bounded_items(c, aid)) // can't trade account bound items and has no permission
            || !itemdb::itemdb_isrestricted(it.nameid, &it.cards(), gmlv, gmlv, itemdb::itemdb_cantrade_sub)
        // untradeable item
        {
            continue;
        }
        let v = value.min(vending_max_value());
        list.push(VendingItem { index, amount, value: v });
        total += v as i64 * amount as i64;
    }
    let i = list.len();
    // check if the total value of the items plus the current zeny is over the limit
    let zeny = pc::character_of(c, aid).map(|ch| ch.zeny as i64).unwrap_or(0);
    if !vending_over_max() && zeny + total > MAX_ZENY {
        // PACKETVER >= 20200819
        crate::clif_items::msg_color(c, &sd, MSI_MERCHANTSHOP_TOTA_LOVER_ZENY_ERR, crate::social::COLOR_RED);
        fail(c);
        return 1;
    }
    if i != count {
        clif::displaymessage(c, &sd, crate::data::msg_txt(266)); //"Some of your items cannot be vended and were removed from the shop."
        fail(c); // custom reply packet
        return 5;
    }
    if i == 0 {
        // no valid item found
        fail(c); // custom reply packet
        return 5;
    }
    let mut p = pc::get(c, aid).unwrap_or(p);
    p.prevend = false;
    p.vending = true;
    pc::put(c, &p);
    crate::itemui::set_workinprogress(c, aid, crate::itemui::WIP_DISABLE_NONE);
    let mut v = vd(c, aid);
    v.vender_id = vending_getuid(c);
    v.vending = list;
    put(c, &v);
    // safestrncpy(sd.message, message, MESSAGE_SIZE)
    let msg: String = message.chars().take(MESSAGE_SIZE - 1).collect();
    crate::buyingstore::set_sd_message(c, aid, &msg);
    // INSERT INTO `vendings` … / INSERT INTO `vending_items` …
    let Some(sd) = unit::get(c, aid) else { return 0 };
    let ch = pc::character_of(c, aid);
    let autotrade = crate::autotrade::autotrade(c, aid) as i8;
    c.db().vendings().insert(Vendings {
        id: v.vender_id,
        account_id: aid,
        char_id: p.char_id,
        sex: sex_char(ch.as_ref().map(|ch| ch.sex).unwrap_or(1)),
        map: crate::data::maps().get(sd.map).map(|d| d.name.clone()).unwrap_or_default(),
        x: sd.x as u16,
        y: sd.y as u16,
        title: msg.clone(),
        body_direction: at.map(|a| a.dir).unwrap_or(sd.ud.dir),
        head_direction: at.map(|a| a.head_dir).unwrap_or(sd.head_dir),
        sit: at.map(|a| a.sit).unwrap_or((sd.vd.dead_sit == 2) as u8),
        autotrade,
    });
    for (j, e) in v.vending.iter().enumerate() {
        c.db().vending_items().insert(VendingItemRow {
            row_id: 0,
            vending_id: v.vender_id,
            index: j as u16,
            cartinventory_id: cart_mem_id(c, aid, e.index as usize) as u32,
            amount: e.amount as u16,
            price: e.value,
        });
    }
    clif_openvending(c, aid);
    clif_showvendingboard(c, &sd, None);
    // idb_put(vending_db, sd.status.char_id, &sd): the `pc.vending` flag
    0
}

/// vending.cpp vending_search: whether `aid`'s shop sells `nameid`.
pub fn vending_search(c: &Ctx, aid: u32, nameid: u32) -> bool {
    if !is_vending(c, aid) {
        return false; // not vending
    }
    vd(c, aid).vending.iter().any(|e| cart_item(c, aid, e.index as usize).nameid == nameid)
}

/// vending.cpp vending_searchall: the shop's items matching the search go into the searcher's results; false when
/// the result set is full (the search stops).
pub fn vending_searchall(c: &Ctx, aid: u32, s: &crate::searchstore::SearchStoreSearch, results: &mut Vec<crate::searchstore::SearchStoreItem>) -> bool {
    if !is_vending(c, aid) {
        return true; // not vending
    }
    let v = vd(c, aid);
    let message = crate::buyingstore::sd_message(c, aid);
    for &want in &s.itemlist {
        let Some(e) = v.vending.iter().find(|e| cart_item(c, aid, e.index as usize).nameid == want) else {
            continue; // not found
        };
        let it = cart_item(c, aid, e.index as usize);
        if s.min_price != 0 && s.min_price > e.value {
            continue; // too low price
        }
        if s.max_price != 0 && s.max_price < e.value {
            continue; // too high price
        }
        if !s.cardlist.is_empty() {
            // check cards
            if itemdb::itemdb_isspecial(it.card0) {
                continue; // something, that is not a carded
            }
            let slot = itemdb::itemdb_slots(it.nameid) as usize;
            let mut cidx = 0usize;
            while cidx < slot && it.card(cidx) != 0 {
                if s.cardlist.contains(&it.card(cidx)) {
                    break; // found
                }
                cidx += 1;
            }
            if cidx == slot || it.card(cidx) == 0 {
                continue; // no card match
            }
        }
        // Check if the result set is full
        if results.len() >= crate::searchstore::searchstore_maxresults() {
            return false;
        }
        results.push(crate::searchstore::SearchStoreItem {
            store_id: v.vender_id,
            account_id: aid,
            store_name: message.clone(),
            nameid: it.nameid,
            amount: e.amount as u16,
            price: e.value as i32,
            card: it.cards().to_vec(),
            refine: it.refine,
            enchantgrade: it.enchantgrade,
        });
    }
    true
}

/// vending.cpp vending_update: the shop's position, direction, sitting and autotrade flag in `vendings`.
pub fn vending_update(c: &Ctx, aid: u32) {
    let Some(sd) = unit::get(c, aid) else { return };
    let id = vender_id(c, aid);
    let Some(mut row) = c.db().vendings().id().find(id) else { return };
    row.map = crate::data::maps().get(sd.map).map(|d| d.name.clone()).unwrap_or_default();
    row.x = sd.x as u16;
    row.y = sd.y as u16;
    row.body_direction = sd.ud.dir;
    row.head_direction = sd.head_dir;
    row.sit = (sd.vd.dead_sit == 2) as u8;
    row.autotrade = crate::autotrade::autotrade(c, aid) as i8;
    c.db().vendings().id().update(row);
}

// ---------------------------------------------------------------------------------------------- the Vending skill

/// skills/merchant/skill_vending.cpp SkillVending::castendNoDamageId (MC_VENDING): the shop preparation starts. A
/// cart item the map server holds without an SQL id makes it save the cart and wait for the char server to send it
/// back before the window opens (intif_storage_save → intif_parse_StorageSaved → intif_storage_request →
/// intif_parse_StorageReceived, [`VendingInter`]); otherwise ZC_OPENSTORE opens at once.
///
/// For the skill pipeline: the Vending class's castendNoDamageId (skills/merchant, skillcore / the merchant family)
/// calls this with the caster; skill_isNotOk's MC_VENDING case is skillcore's (skill/condition.rs).
pub fn skill_vending_castend_nodamage_id(c: &Ctx, src: u32, skill_lv: u16) {
    let Some(sd) = unit::get(c, src) else { return };
    if sd.kind != crate::map::BL_PC {
        return;
    }
    // Prevent vending of GMs with unnecessary Level to trade/drop. [Skotlex]
    if !inv::pc_can_give_items(c, src) {
        crate::skill::skill_fail(c, &sd, MC_VENDING, 0);
        return;
    }
    set_prevend(c, src, true);
    crate::itemui::set_workinprogress(c, src, crate::itemui::WIP_DISABLE_ALL);
    let mut v = vd(c, src);
    v.vend_skill_lv = skill_lv;
    let unsaved = (0..MAX_CART).any(|i| crate::cart::cart_get(c, src, i).map(|r| r.item.nameid != 0).unwrap_or(false) && cart_mem_id(c, src, i) == 0);
    if unsaved {
        // Save the cart before opening the vending UI
        v.pending_vending_ui = true;
        put(c, &v);
        crate::intif::send(crate::intif::Inter::Vending(VendingInter::StorageSaveCart { account_id: src }));
    } else {
        // Instantly open the vending UI
        v.pending_vending_ui = false;
        put(c, &v);
        clif_openvendingreq(c, &sd, 2 + skill_lv);
    }
}

/// The cart round trip of the Vending skill on the inter link (map/intif.cpp ↔ char/int_storage.cpp).
#[derive(Clone, Debug)]
pub enum VendingInter {
    /// 0x3011 intif_storage_save(sd, &sd->cart) (map → char): mapif_parse_StorageSave writes the cart.
    StorageSaveCart { account_id: u32 },
    /// 0x388b mapif_storage_saved (char → map): intif_parse_StorageSaved for TABLE_CART.
    StorageSavedCart { account_id: u32 },
    /// 0x3010 intif_storage_request(sd, TABLE_CART, 0, STOR_MODE_ALL) (map → char): mapif_parse_StorageLoad.
    StorageRequestCart { account_id: u32 },
    /// 0x388a mapif_storage_received (char → map): intif_parse_StorageReceived for TABLE_CART.
    StorageReceivedCart { account_id: u32 },
    /// 0x2b26 chrif_authreq(sd, true) of an autotrader being restored (map → char), answered by
    /// chmapif_parse_reqauth's autotrade branch with 0x2afd → chrif_authok (autotrade.rs).
    AutotradeAuth { account_id: u32, char_id: u32, autotrade: u8 },
}

/// intif.rs's dispatch of [`VendingInter`].
pub fn intif_dispatch(c: &Ctx, m: VendingInter) {
    match m {
        VendingInter::StorageSaveCart { account_id } => {
            // char_memitemdata_to_sql(TABLE_CART) for the character, then the ack
            if let Some(p) = pc::get(c, account_id) {
                crate::cart::cart_tosql(c, p.char_id);
            }
            crate::intif::send(crate::intif::Inter::Vending(VendingInter::StorageSavedCart { account_id }));
        }
        VendingInter::StorageSavedCart { account_id } => {
            // intif_parse_StorageSaved: TABLE_CART — `if (sd && sd->state.prevend) intif_storage_request(...)`
            if pc::get(c, account_id).map(|p| p.prevend).unwrap_or(false) {
                crate::intif::send(crate::intif::Inter::Vending(VendingInter::StorageRequestCart { account_id }));
            }
        }
        VendingInter::StorageRequestCart { account_id } => {
            // storage_fromsql(TABLE_CART) and mapif_storage_received
            crate::intif::send(crate::intif::Inter::Vending(VendingInter::StorageReceivedCart { account_id }));
        }
        VendingInter::StorageReceivedCart { account_id } => intif_parse_storage_received_cart(c, account_id),
        VendingInter::AutotradeAuth { account_id, char_id, autotrade } => crate::autotrade::chrif_authok_autotrade(c, account_id, char_id, autotrade),
    }
}

/// intif.cpp intif_parse_StorageReceived for TABLE_CART after a login is complete: the cart is replaced by the char
/// server's copy (char_memitemdata_from_sql order: cart.rs cart_received, which also fixes the slots' SQL ids and
/// runs pc_check_available_item(ITMCHK_CART)); a player preparing a shop gets the cart list again and the shop
/// window (the autotrader's branch of this function is the restore's, autotrade.rs).
fn intif_parse_storage_received_cart(c: &Ctx, aid: u32) {
    let Some(p) = pc::get(c, aid) else {
        log::error!("intif_parse_StorageReceived: No player online for receiving inventory/cart/storage data (AID: {aid})");
        return;
    };
    crate::cart::cart_received(c, aid);
    if crate::autotrade::autotrade(c, aid) != 0 {
        // (only the restore's load reaches this branch: autotrade.rs does it where the module loads the character)
        return;
    }
    if p.prevend {
        crate::clif_items::clearcart(c, aid);
        crate::clif_items::cartlist(c, aid);
        // Only open the vending UI, if it has not been opened already
        let mut v = vd(c, aid);
        if v.pending_vending_ui {
            if let Some(sd) = unit::get(c, aid) {
                clif_openvendingreq(c, &sd, v.vend_skill_lv + 2);
            }
            v.pending_vending_ui = false;
            put(c, &v);
        }
    }
}

// ---------------------------------------------------------------------------------------------- hooks of other systems

/// clif.cpp clif_getareachar_pc's `else if (dstsd->state.vending) clif_showvendingboard(*dstsd, SELF, sd)` (the
/// caller tests that the player has no chat room first).
pub fn getareachar_pc(c: &Ctx, sd: &Unit, dstsd: &Unit) -> bool {
    if !is_vending(c, dstsd.id) {
        return false;
    }
    clif_showvendingboard(c, dstsd, Some(sd));
    true
}

/// clif.cpp clif_outsight's `if (sd->state.vending) clif_closevendingboard(*bl, SELF, tsd)`.
pub fn outsight(c: &Ctx, bl: &Unit, tsd: &Unit) {
    if is_vending(c, bl.id) {
        clif_closevendingboard(c, bl, Some(tsd));
    }
}

/// clif.cpp clif_parse_LoadEndAck's `if (sd->state.vending) { clif_openvending(*sd); clif_showvendingboard(*sd); }`
/// ("show we have a vending", after a warp of a vender).
pub fn load_end_ack(c: &Ctx, aid: u32) {
    if !is_vending(c, aid) {
        return;
    }
    clif_openvending(c, aid);
    if let Some(sd) = unit::get(c, aid) {
        clif_showvendingboard(c, &sd, None);
    }
}

/// clif.cpp clif_refresh's `if (sd->state.vending) clif_openvending(*sd)` (for @refresh).
pub fn clif_refresh(c: &Ctx, aid: u32) {
    if is_vending(c, aid) {
        clif_openvending(c, aid);
    }
}

/// pc.cpp pc_setpos's vending parts on a map change: a shop on a map with the novending flag closes (msg 276).
pub fn pc_setpos_changemap(c: &Ctx, aid: u32, m: u16) {
    if is_vending(c, aid) && crate::npc::mapflag(c, m, MF_NOVENDING) {
        if let Some(sd) = unit::get(c, aid) {
            clif::displaymessage(c, &sd, crate::data::msg_txt(276)); // "You can't open a shop on this map"
        }
        vending_closevending(c, aid);
    }
}

/// pc.cpp pc_setpos's check of the arrival cell: a shop on a novending cell closes (msg 204).
pub fn pc_setpos_cell(c: &Ctx, aid: u32, m: u16, x: i16, y: i16) {
    if is_vending(c, aid) && cell_novending(c, m, x, y) {
        if let Some(sd) = unit::get(c, aid) {
            clif::displaymessage(c, &sd, crate::data::msg_txt(204)); // "You can't open a shop on this cell."
        }
        vending_closevending(c, aid);
    }
}

/// pc.cpp pc_setpos's end: `if (sd->state.vending) vending_update(*sd)`.
pub fn pc_setpos_end(c: &Ctx, aid: u32) {
    if is_vending(c, aid) {
        vending_update(c, aid);
    }
}

// ---------------------------------------------------------------------------------------------- clif

/// clif.cpp clif_openvendingreq: ZC_OPENSTORE (0x012d <num>.W), the shop creation window with `num` slots.
pub fn clif_openvendingreq(c: &Ctx, sd: &Unit, num: u16) {
    let p = PACKET_ZC_OPENSTORE { packetType: PACKET_ZC_OPENSTORE::ID as i16, num };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// clif.cpp clif_showvendingboard: ZC_STORE_ENTRY (0x0131) to the area without the owner (`tbl` None), or to one
/// player coming into view.
pub fn clif_showvendingboard(c: &Ctx, sd: &Unit, tbl: Option<&Unit>) {
    let p = PACKET_ZC_STORE_ENTRY { packetType: PACKET_ZC_STORE_ENTRY::ID as i16, makerAID: sd.id, storeName: crate::buyingstore::sd_message(c, sd.id) };
    match tbl {
        None => clif::send(c, &p.to_vec(), sd, Target::AreaWos),
        Some(t) => clif::send(c, &p.to_vec(), t, Target::SelfOnly),
    }
}

/// clif.cpp clif_closevendingboard: ZC_DISAPPEAR_ENTRY (0x0132), area (without the owner) or one player.
pub fn clif_closevendingboard(c: &Ctx, bl: &Unit, tbl: Option<&Unit>) {
    let p = PACKET_ZC_DISAPPEAR_ENTRY { packetType: PACKET_ZC_DISAPPEAR_ENTRY::ID as i16, GID: bl.id as i32 };
    match tbl {
        None => clif::send(c, &p.to_vec(), bl, Target::AreaWos),
        Some(t) => clif::send(c, &p.to_vec(), t, Target::SelfOnly),
    }
}

/// clif.cpp clif_vendinglist: ZC_PC_PURCHASE_ITEMLIST_FROMMC2 (0x0b3d at this PACKETVER) of `vsd`'s shop for `sd`.
pub fn clif_vendinglist(c: &Ctx, sd: u32, vsd: u32) {
    let Some(to) = unit::get(c, sd) else { return };
    let v = vd(c, vsd);
    let mut items = Vec::new();
    for e in &v.vending {
        let index = e.index as usize;
        let it = cart_item(c, vsd, index);
        let data = itemdb::itemdb_search(it.nameid);
        items.push(PACKET_ZC_PC_PURCHASE_ITEMLIST_FROMMC_sub {
            price: e.value,
            amount: e.amount as u16,
            index: crate::clif_items::client_index(index) as i16,
            itemType: crate::clif_items::itemtype(it.nameid),
            itemId: itemdb::client_nameid(it.nameid),
            identified: it.identify,
            damaged: it.attribute,
            slot: crate::clif_items::addcards(Some(&it)),
            option_data: crate::clif_items::add_random_options(&it),
            location: crate::pc_equip::pc_equippoint_sub(c, sd, data),
            viewSprite: data.look as u16,
            refine: it.refine,
            grade: it.enchantgrade,
        });
    }
    let p = PACKET_ZC_PC_PURCHASE_ITEMLIST_FROMMC { packetType: PACKET_ZC_PC_PURCHASE_ITEMLIST_FROMMC::ID as i16, packetLength: 0, AID: vsd, venderId: v.vender_id, items };
    clif::send(c, &p.to_vec(), &to, Target::SelfOnly);
}

/// clif.cpp clif_buyvending: ZC_PC_PURCHASE_RESULT_FROMMC (0x0135 <index>.W <amount>.W <result>.B), a purchase
/// failure (`index` is the server's cart index).
pub fn clif_buyvending(c: &Ctx, sd: &Unit, index: u16, amount: u16, result: u8) {
    let p = PACKET_ZC_PC_PURCHASE_RESULT_FROMMC { packetType: PACKET_ZC_PC_PURCHASE_RESULT_FROMMC::ID as i16, index: index.wrapping_add(2), amount, result };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// clif.cpp clif_openvending_ack: ZC_ACK_OPENSTORE2 (0x0a28 <result>.B, PACKETVER >= 20141022).
pub fn clif_openvending_ack(c: &Ctx, sd: &Unit, result: u8) {
    let p = PACKET_ZC_ACK_OPENSTORE2 { packetType: PACKET_ZC_ACK_OPENSTORE2::ID as i16, result };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// clif.cpp clif_openvending: ZC_PC_PURCHASE_MYITEMLIST (0x0b40 at this PACKETVER), the vender's own list, then
/// ZC_ACK_OPENSTORE2 success.
pub fn clif_openvending(c: &Ctx, aid: u32) {
    let Some(sd) = unit::get(c, aid) else { return };
    let v = vd(c, aid);
    let mut items = Vec::new();
    for e in &v.vending {
        let index = e.index as usize;
        let it = cart_item(c, aid, index);
        items.push(PACKET_ZC_PC_PURCHASE_MYITEMLIST_sub {
            price: e.value,
            index: crate::clif_items::client_index(index) as i16,
            amount: e.amount,
            itemType: crate::clif_items::itemtype(it.nameid),
            itemId: itemdb::client_nameid(it.nameid),
            identified: it.identify,
            damaged: it.attribute,
            slot: crate::clif_items::addcards(Some(&it)),
            option_data: crate::clif_items::add_random_options(&it),
            refine: it.refine,
            grade: it.enchantgrade,
        });
    }
    let p = PACKET_ZC_PC_PURCHASE_MYITEMLIST { packetType: PACKET_ZC_PC_PURCHASE_MYITEMLIST::ID as i16, packetLength: 0, AID: aid, items };
    clif::send(c, &p.to_vec(), &sd, Target::SelfOnly);
    clif_openvending_ack(c, &sd, OPENSTORE2_SUCCESS);
}

/// clif.cpp clif_vendingreport: ZC_DELETEITEM_FROM_MCSTORE2 (0x09e5 <index>.W <amount>.W <GID>.L <Date>.L
/// <zeny>.L, PACKETVER >= 20141016): the vender is told of a sale (Date = time(nullptr)).
pub fn clif_vendingreport(c: &Ctx, aid: u32, index: u16, amount: u16, char_id: u32, zeny: i32) {
    let Some(sd) = unit::get(c, aid) else { return };
    let p = PACKET_ZC_DELETEITEM_FROM_MCSTORE {
        packetType: PACKET_ZC_DELETEITEM_FROM_MCSTORE::ID as i16,
        index: index.wrapping_add(2),
        amount,
        buyerCID: char_id,
        date: (c.tick() / 1000) as u32,
        zeny,
    };
    clif::send(c, &p.to_vec(), &sd, Target::SelfOnly);
}

// ---------------------------------------------------------------------------------------------- parsers

/// clif.cpp clif_parse_CloseVending (CZ_REQ_CLOSESTORE 0x012e).
pub fn parse_close_vending(c: &Ctx, aid: u32) {
    vending_closevending(c, aid);
}

/// clif.cpp clif_parse_VendingListReq (CZ_REQ_BUY_FROMMC 0x0130 <account id>.L).
pub fn parse_vending_list_req(c: &Ctx, aid: u32, pkt: &[u8]) {
    if pc::get(c, aid).map(|p| p.npc_id != 0).unwrap_or(true) {
        return; // using an NPC
    }
    let pos = packet_pos(u16::from_le_bytes([pkt[0], pkt[1]]));
    let id = Reader::at(pkt, pos.first().copied().unwrap_or(2) as usize).u32().unwrap_or(0);
    vending_vendinglistreq(c, aid, id);
}

/// The purchase list of CZ_PC_PURCHASE_ITEMLIST_FROMMC(2): `(p->packetLength - sizeof(*p)) / sizeof(p->list[0])`
/// entries (the size_t arithmetic turned into int32, as rAthena passes it) and the (amount, index) pairs present.
fn purchase_list(pkt: &[u8], header: usize) -> (i32, Vec<(u16, u16)>) {
    let len = if pkt.len() >= 4 { i16::from_le_bytes([pkt[2], pkt[3]]) } else { 0 };
    let count = ((len as i64 as u64).wrapping_sub(header as u64) / 4) as i32;
    let mut list = Vec::new();
    let mut o = header;
    while o + 4 <= pkt.len() {
        list.push((u16::from_le_bytes([pkt[o], pkt[o + 1]]), u16::from_le_bytes([pkt[o + 2], pkt[o + 3]])));
        o += 4;
    }
    (count, list)
}

/// clif.cpp clif_parse_PurchaseReq (CZ_PC_PURCHASE_ITEMLIST_FROMMC 0x0134): buys from the shop whose list is open.
pub fn parse_purchase_req(c: &Ctx, aid: u32, pkt: &[u8]) {
    let vender = Reader::at(pkt, 4).u32().unwrap_or(0);
    let (count, list) = purchase_list(pkt, PACKET_CZ_PC_PURCHASE_ITEMLIST_FROMMC::SIZE);
    let vended_id = vd(c, aid).vended_id;
    purchase(c, aid, vender, vended_id, &list, count);
    // whether it fails or not, the buy window is closed
    clear_vended_id(c, aid);
}

/// clif.cpp clif_parse_PurchaseReq2 (CZ_PC_PURCHASE_ITEMLIST_FROMMC2 0x0801): buys from the shop `UniqueID`.
pub fn parse_purchase_req2(c: &Ctx, aid: u32, pkt: &[u8]) {
    let vender = Reader::at(pkt, 4).u32().unwrap_or(0);
    let uid = Reader::at(pkt, 8).u32().unwrap_or(0);
    let (count, list) = purchase_list(pkt, PACKET_CZ_PC_PURCHASE_ITEMLIST_FROMMC2::SIZE);
    purchase(c, aid, vender, uid, &list, count);
    // whether it fails or not, the buy window is closed
    clear_vended_id(c, aid);
}

/// vending_purchasereq with the list as long as rAthena reads it (`count` entries: never more than the packet
/// holds once the checks pass, since count <= vend_num <= MAX_VENDING and the framing gives the packet its length).
fn purchase(c: &Ctx, aid: u32, vender: u32, uid: u32, list: &[(u16, u16)], count: i32) {
    if count >= 1 && (count as usize) > list.len() {
        // a length field larger than the bytes present cannot pass clif_parse's framing; nothing to buy
        return;
    }
    vending_purchasereq(c, aid, vender, uid, list, count);
}

fn clear_vended_id(c: &Ctx, aid: u32) {
    if let Some(mut v) = c.db().pc_vending().account_id().find(aid) {
        if v.vended_id != 0 {
            v.vended_id = 0;
            c.db().pc_vending().account_id().update(v);
        }
    }
}

/// clif.cpp clif_parse_OpenVending (CZ_REQ_OPENSTORE 0x012f / CZ_REQ_OPENSTORE2 0x01b2): the shop preparation
/// window is confirmed (or cancelled). The field offsets are clif_packetdb.hpp's (0x012f: 2, 4, 0, 84 — the
/// generated packet_pos stops at the 0 — and 0x01b2: 2, 4, 84, 85).
pub fn parse_open_vending(c: &Ctx, aid: u32, pkt: &[u8]) {
    let cmd = u16::from_le_bytes([pkt[0], pkt[1]]);
    let pos: [usize; 4] = if cmd == 0x012f { [2, 4, 0, 84] } else { [2, 4, 84, 85] };
    let mut len = Reader::at(pkt, pos[0]).i16().unwrap_or(0);
    let raw = pkt.get(pos[1]..).unwrap_or(&[]);
    // the message is the C string at pos[1] (the 80-byte field; safestrncpy keeps at most 79 characters)
    let message = from_latin1(until_nul(raw));
    if cmd == 0x012f {
        // (CZ_REQ_OPENSTORE)
        len = len.wrapping_sub(84);
    } else {
        // (CZ_REQ_OPENSTORE2)
        len = len.wrapping_sub(85);
        let flag = pkt.get(pos[2]).copied().unwrap_or(0) != 0;
        if !flag {
            set_prevend(c, aid, false);
            crate::itemui::set_workinprogress(c, aid, crate::itemui::WIP_DISABLE_NONE);
        }
    }
    let Some(sd) = unit::get(c, aid) else { return };
    if crate::status::sc::get_sce(c, aid, crate::status::consts::SC_NOCHAT).map(|e| e.val1 & MANNER_NOROOM != 0).unwrap_or(false) {
        return;
    }
    if crate::npc::mapflag(c, sd.map, MF_NOVENDING) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(276)); // "You can't open a shop on this map"
        return;
    }
    if cell_novending(c, sd.map, sd.x, sd.y) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(204)); // "You can't open a shop on this cell."
        return;
    }
    if raw.first().copied().unwrap_or(0) == 0 {
        return; // invalid input
    }
    // len / 8 entries from pos[3] (int16 division: a negative length gives a count below 1, which
    // vending_openvending refuses at its count check, as it does an empty list)
    let n = len / 8;
    let mut data = Vec::new();
    if n > 0 {
        let mut r = Reader::at(pkt, pos[3]);
        for _ in 0..n {
            let (Some(index), Some(amount), Some(value)) = (r.u16(), r.u16(), r.u32()) else { break };
            data.push(OpenEntry { index, amount, value });
        }
    }
    vending_openvending(c, aid, &message, &data, None);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn tax_applies_from_the_minimum() {
        assert_eq!(vending_calc_tax(99_999_999.0), 99_999_999.0);
        // 100,000,000 * (1 - 0.05)
        assert_eq!(to_i32(vending_calc_tax(100_000_000.0)), 95_000_000);
        assert_eq!(to_i32(vending_calc_tax(123_456_789.0)), 117_283_949);
    }

    #[test]
    fn purchase_list_count_follows_the_length_field() {
        // 0x0801 with two entries
        let mut p = vec![0x01, 0x08, 20, 0, 1, 0, 0, 0, 2, 0, 0, 0];
        p.extend_from_slice(&[3, 0, 4, 0, 5, 0, 6, 0]);
        let (n, l) = purchase_list(&p, 12);
        assert_eq!(n, 2);
        assert_eq!(l, vec![(3, 4), (5, 6)]);
        // a length below the header: (size_t) underflow, then int32 truncation, as rAthena computes it
        let p = vec![0x01, 0x08, 4, 0];
        let (n, _) = purchase_list(&p, 12);
        assert_eq!(n, ((4u64.wrapping_sub(12)) / 4) as i32);
        assert!(n < 1);
    }
}
