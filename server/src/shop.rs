//! NPC shops (npc.cpp npc_buysellsel, npc_buylist, npc_selllist, npc_shop_discount; pc.cpp pc_modifybuyvalue,
//! pc_modifysellvalue, pc_can_sell_item; clif.cpp clif_npcbuysell, clif_buylist, clif_selllist,
//! clif_npc_buy_result, clif_npc_sell_result, clif_npc_market_open / purchase_ack and their parse functions).
//! The shops themselves are the world's `npc` rows (npc.rs npc_parse_shop: `shop` items, `shop_discount`).
//! Script-controlled shops (nd->master_nd, npcshopattach) come with that script command.

use spacetimedb::{table, Table};

use crate::clif::{self, Target};
use crate::db::items as itemdb;
use crate::inventory::*;
use crate::net::packets::*;
use crate::npc::{npc, Npc, NPCTYPE_ITEMSHOP, NPCTYPE_MARKETSHOP, NPCTYPE_POINTSHOP, NPCTYPE_SHOP};
use crate::pc;
use crate::unit;
use crate::world::Ctx;

/// conf/battle/items.conf `min_shop_buy: 1`, `min_shop_sell: 0`, `rental_item_novalue: no`, `allow_bound_sell: 0x0`,
/// misc.conf `hide_fav_sell: no`, items.conf `rental_transaction: yes`, exp.conf `shop_exp: 0`,
/// misc.conf `discount_item_point_shop: 0`.
pub fn min_shop_buy() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MIN_SHOP_BUY)
}
pub fn min_shop_sell() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MIN_SHOP_SELL)
}
pub fn rental_item_novalue() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::RENTAL_ITEM_NOVALUE) != 0
}
pub fn allow_bound_sell() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::ALLOW_BOUND_SELL)
}
pub fn hide_fav_sell() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::HIDE_FAV_SELL) != 0
}
pub fn rental_transaction() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::RENTAL_TRANSACTION) != 0
}
pub fn shop_exp() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::SHOP_EXP)
}
pub fn discount_item_point_shop() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::DISCOUNT_ITEM_POINT_SHOP)
}

/// clif.hpp e_purchase_result.
pub const PURCHASE_SUCCEED: u8 = 0;
pub const PURCHASE_FAIL_MONEY: u8 = 1;
pub const PURCHASE_FAIL_WEIGHT: u8 = 2;
pub const PURCHASE_FAIL_COUNT: u8 = 3;
pub const PURCHASE_FAIL_STOCK: u8 = 4;

/// The SQL `market` table (npc_market_tosql): a market shop's stock.
#[table(accessor = market)]
#[derive(Clone, Debug)]
pub struct Market {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    #[index(btree)]
    pub name: String,
    pub nameid: u32,
    pub price: i32,
    pub amount: i32,
    pub flag: u8,
}

/// pc.cpp pc_modifybuyvalue: Discount (MC_DISCOUNT) / Compulsion Discount (RG_COMPULSION).
pub fn pc_modifybuyvalue(c: &Ctx, aid: u32, orig_value: i32) -> i32 {
    use crate::db::skills::{MC_DISCOUNT, RG_COMPULSION};
    let mut rate1 = 0;
    let mut rate2 = 0;
    let skill = crate::skill::checkskill(c, aid, MC_DISCOUNT) as i32;
    if skill > 0 {
        rate1 = 5 + skill * 2 - if skill == 10 { 1 } else { 0 };
    }
    let skill = crate::skill::checkskill(c, aid, RG_COMPULSION) as i32;
    if skill > 0 {
        rate2 = 5 + skill * 4;
    }
    if rate1 < rate2 {
        rate1 = rate2;
    }
    let mut val = orig_value;
    if rate1 != 0 {
        val = (orig_value as f64 * (100 - rate1) as f64 / 100.0) as i32;
    }
    if val < min_shop_buy() {
        val = min_shop_buy();
    }
    val
}

/// pc.cpp pc_modifysellvalue: Overcharge (MC_OVERCHARGE).
pub fn pc_modifysellvalue(c: &Ctx, aid: u32, orig_value: i32) -> i32 {
    let mut rate = 0;
    let skill = crate::skill::checkskill(c, aid, crate::db::skills::MC_OVERCHARGE) as i32;
    if skill > 0 {
        rate = 5 + skill * 2 - if skill == 10 { 1 } else { 0 };
    }
    let mut val = orig_value;
    if rate != 0 {
        val = (orig_value as f64 * (100 + rate) as f64 / 100.0) as i32;
    }
    if val < min_shop_sell() {
        val = min_shop_sell();
    }
    val
}

/// pc.cpp pc_can_sell_item.
pub fn pc_can_sell_item(c: &Ctx, aid: u32, item: &Item, shoptype: u8) -> bool {
    if !pc_can_give_items(c, aid) {
        return false;
    }
    if item.equip > 0 || item.amount < 0 {
        return false;
    }
    if hide_fav_sell() && item.favorite != 0 {
        return false; // Cannot sell favs (optional config)
    }
    if !rental_transaction() && item.expire_time != 0 {
        return false; // Cannot Sell Rental Items
    }
    if item.equip_switch != 0 {
        return false;
    }
    if itemdb::itemdb_ishatched_egg(item.card0, item.attribute) {
        return false;
    }
    // allow_bound_sell: 0x0 — the NPCTYPE_SHOP / NPCTYPE_ITEMSHOP exceptions for bound items never apply
    const ISR_SELLABLE: i32 = 0x2;
    if shoptype == NPCTYPE_ITEMSHOP && item.bound == 0 && itemdb::itemdb_search(item.nameid).flag.trade_restriction.sell && allow_bound_sell() & ISR_SELLABLE != 0 {
        return true;
    }
    if !itemdb::itemdb_isrestricted(item.nameid, &item.cards(), pc::group_level(c, aid), 0, itemdb::itemdb_cansell_sub) {
        return false;
    }
    if item.bound != 0 && !pc_can_give_bounded_items(c, aid) {
        return false; // Don't allow sale of bound items
    }
    true
}

/// npc.cpp npc_shop_discount.
pub fn npc_shop_discount(nd: &Npc) -> bool {
    match nd.subtype {
        NPCTYPE_ITEMSHOP => nd.shop_discount || discount_item_point_shop() & 1 != 0,
        NPCTYPE_POINTSHOP => nd.shop_discount || discount_item_point_shop() & 2 != 0,
        _ => nd.shop_discount,
    }
}

/// npc.cpp npc_checknear for a shop: the NPC exists, is on the player's map and within AREA_SIZE + 1 (class-less
/// NPCs are reachable from anywhere).
fn npc_checknear(c: &Ctx, aid: u32, id: u32) -> Option<Npc> {
    let nd = c.db().npc().id().find(id)?;
    let x = crate::script::npc_run::sdx(c, aid);
    let p = pc::get(c, aid)?;
    if x.using_fake_npc && p.npc_id == id {
        return Some(nd);
    }
    if nd.class < 0 {
        return Some(nd);
    }
    let bl = unit::get(c, id)?;
    let sd = unit::get(c, aid)?;
    let r = crate::conf::area_size();
    if bl.map != sd.map || bl.x < sd.x - r - 1 || bl.x > sd.x + r + 1 || bl.y < sd.y - r - 1 || bl.y > sd.y + r + 1 {
        return None;
    }
    Some(nd)
}

pub fn set_npc_shopid(c: &Ctx, aid: u32, id: u32) {
    if let Some(mut p) = pc::get(c, aid) {
        p.npc_shopid = id;
        pc::put(c, &p);
    }
}

// ---------------------------------------------------------------------------------------------- npc_click

/// npc.cpp npc_click's shop branches: NPCTYPE_SHOP opens the buy / sell choice, cash / item / point shops their
/// list, market shops their window (unless sold out). Returns false when the click is refused.
pub fn npc_click_shop(c: &Ctx, aid: u32, nd: &Npc) -> bool {
    let Some(sd) = unit::get(c, aid) else { return false };
    match nd.subtype {
        NPCTYPE_SHOP => npcbuysell(c, &sd, nd.id),
        crate::npc::NPCTYPE_CASHSHOP | NPCTYPE_ITEMSHOP | NPCTYPE_POINTSHOP => crate::cashshop::clif_cashshop_show(c, aid, nd),
        NPCTYPE_MARKETSHOP => {
            if !nd.shop.iter().any(|s| s.qty != 0) {
                // color_table[COLOR_RED]: msg 534 (Shop is out of stock! Come again later!)
                clif::messagecolor_target(c, &sd, crate::social::COLOR_RED, &crate::net::wire::latin1(crate::data::msg_txt(534)), false, Target::SelfOnly, None);
                return false;
            }
            set_npc_shopid(c, aid, nd.id);
            npc_market_open(c, aid, nd);
        }
        crate::npc::NPCTYPE_BARTER => {
            set_npc_shopid(c, aid, nd.id);
            crate::barter::open(c, aid, nd);
        }
        _ => {}
    }
    true
}

/// clif.cpp clif_npcbuysell: ZC_SELECT_DEALTYPE.
pub fn npcbuysell(c: &Ctx, sd: &unit::Unit, npc_id: u32) {
    let p = PACKET_ZC_SELECT_DEALTYPE { packetType: PACKET_ZC_SELECT_DEALTYPE::ID as i16, npcId: npc_id };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

// ---------------------------------------------------------------------------------------------- buy / sell lists

/// npc.cpp npc_buysellsel: 0 buy, 1 sell.
pub fn npc_buysellsel(c: &Ctx, aid: u32, id: u32, kind: u8) -> i32 {
    let Some(nd) = npc_checknear(c, aid, id) else { return 1 };
    if nd.subtype != NPCTYPE_SHOP {
        log::error!("no such shop npc : {id}");
        if let Some(mut p) = pc::get(c, aid) {
            if p.npc_id == id {
                p.npc_id = 0;
                pc::put(c, &p);
            }
        }
        return 1;
    }
    if unit::get(c, id).map(|u| u.invisible).unwrap_or(nd.is_invisible) {
        return 1; // can't buy if npc is not visible (hack?)
    }
    let sd_char = unit::get(c, aid).map(|u| u.char_id).unwrap_or(0);
    if nd.owner_char != 0 && nd.owner_char != sd_char {
        return 1;
    }
    set_npc_shopid(c, aid, id);
    if kind == 0 {
        clif_buylist(c, aid, &nd);
    } else {
        clif_selllist(c, aid);
    }
    0
}

/// clif.cpp clif_buylist: ZC_PC_PURCHASE_ITEMLIST (with view sprite and location for 20211103).
pub fn clif_buylist(c: &Ctx, aid: u32, nd: &Npc) {
    let Some(sd) = unit::get(c, aid) else { return };
    let discount = npc_shop_discount(nd);
    let mut items = Vec::new();
    for s in &nd.shop {
        let val = s.value;
        let id = itemdb::itemdb_search(s.nameid);
        items.push(PACKET_ZC_PC_PURCHASE_ITEMLIST_sub {
            itemId: itemdb::client_nameid(s.nameid),
            price: val as u32,
            discountPrice: if discount { pc_modifybuyvalue(c, aid, val) as u32 } else { val as u32 },
            itemType: crate::clif_items::itemtype(s.nameid),
            viewSprite: id.look as u16,
            location: crate::pc_equip::pc_equippoint_sub(c, aid, id),
        });
    }
    let p = PACKET_ZC_PC_PURCHASE_ITEMLIST { packetType: PACKET_ZC_PC_PURCHASE_ITEMLIST::ID as i16, packetLength: 0, items };
    clif::send(c, &p.to_vec(), &sd, Target::SelfOnly);
}

/// clif.cpp clif_selllist: ZC_PC_SELL_ITEMLIST (what the shop would pay for each sellable item).
pub fn clif_selllist(c: &Ctx, aid: u32) {
    let Some(p) = pc::get(c, aid) else { return };
    if p.npc_shopid == 0 {
        return;
    }
    let Some(nd) = c.db().npc().id().find(p.npc_shopid) else { return };
    let Some(sd) = unit::get(c, aid) else { return };
    let mut items = Vec::new();
    for r in inv_list(c, aid) {
        if r.item.nameid == 0 {
            continue;
        }
        if !pc_can_sell_item(c, aid, &r.item, nd.subtype) {
            continue;
        }
        let price = if rental_item_novalue() && r.item.expire_time != 0 {
            0
        } else {
            let v = data_of(&r.item).value_sell as i32;
            if v < 0 {
                continue;
            }
            v
        };
        items.push(PACKET_ZC_PC_SELL_ITEMLIST_sub { index: crate::clif_items::client_index(r.idx as usize), price: price as u32, overcharge: pc_modifysellvalue(c, aid, price) as u32 });
    }
    let pk = PACKET_ZC_PC_SELL_ITEMLIST { packetType: PACKET_ZC_PC_SELL_ITEMLIST::ID as i16, packetLength: 0, items };
    clif::send(c, &pk.to_vec(), &sd, Target::SelfOnly);
}

/// clif.cpp clif_npc_buy_result: ZC_PC_PURCHASE_RESULT.
pub fn clif_npc_buy_result(c: &Ctx, aid: u32, result: u8) {
    let Some(sd) = unit::get(c, aid) else { return };
    let p = PACKET_ZC_PC_PURCHASE_RESULT { packetType: PACKET_ZC_PC_PURCHASE_RESULT::ID as i16, result };
    clif::send(c, &p.to_vec(), &sd, Target::SelfOnly);
}

/// clif.cpp clif_npc_sell_result: ZC_PC_SELL_RESULT (0x00cb).
pub fn clif_npc_sell_result(c: &Ctx, aid: u32, result: u8) {
    let Some(sd) = unit::get(c, aid) else { return };
    clif::send(c, &[0xcb, 0x00, result], &sd, Target::SelfOnly);
}

/// One requested purchase (s_npc_buy_list).
#[derive(Clone, Copy, Debug)]
pub struct BuyItem {
    pub nameid: u32,
    pub qty: i32,
}

/// npc.cpp npc_buylist: result code for clif_parse_NpcBuyListSend / clif_npc_market_purchase_ack.
pub fn npc_buylist(c: &Ctx, aid: u32, item_list: &mut [BuyItem]) -> u8 {
    let shopid = pc::get(c, aid).map(|p| p.npc_shopid).unwrap_or(0);
    let Some(nd) = npc_checknear(c, aid, shopid) else { return PURCHASE_FAIL_COUNT };
    if nd.subtype != NPCTYPE_SHOP && nd.subtype != NPCTYPE_MARKETSHOP {
        return PURCHASE_FAIL_COUNT;
    }
    if item_list.is_empty() {
        return PURCHASE_FAIL_COUNT;
    }
    let mut z: f64 = 0.0;
    let mut w: i64 = 0;
    let mut new_: i32 = 0;
    let master = crate::script::buildins::script2::npcctl2::shop_master(c, nd.id); // nd->master_nd
    let shop = &nd.shop;
    let mut market_index = vec![0usize; item_list.len()];
    for (i, e) in item_list.iter_mut().enumerate() {
        // find this entry in the shop's sell list (item_avail replacement: the view id)
        let Some(j) = shop.iter().position(|s| e.nameid == s.nameid || e.nameid == itemdb::itemdb_viewid(s.nameid)) else {
            return PURCHASE_FAIL_COUNT; // no such item in shop
        };
        if nd.subtype == NPCTYPE_MARKETSHOP {
            if shop[j].qty >= 0 && e.qty > shop[j].qty {
                return PURCHASE_FAIL_COUNT;
            }
            market_index[i] = j;
        }
        let mut amount = e.qty as u16 as i32;
        e.nameid = shop[j].nameid;
        let nameid = e.nameid;
        let mut value = shop[j].value;
        let Some(id) = itemdb::itemdb_exists(nameid) else { return PURCHASE_FAIL_COUNT };
        if !id.is_stackable() && amount > 1 {
            // Exploit? You can't buy more than 1 of equipment types o.O
            let name = unit::get(c, aid).map(|u| u.name).unwrap_or_default();
            log::warn!("Player {name} ({aid}) sent a hexed packet trying to buy {amount} of nonstackable item {nameid}!");
            amount = 1;
            e.qty = 1;
        }
        if master != 0 {
            continue; // Script-controlled shops decide by themselves, what can be bought and for what price.
        }
        match pc_checkadditem(c, aid, nameid, amount) {
            CHKADDITEM_EXIST => {}
            CHKADDITEM_NEW => new_ += id.inventory_slot_needed(amount),
            _ => return PURCHASE_FAIL_WEIGHT,
        }
        if npc_shop_discount(&nd) {
            value = pc_modifybuyvalue(c, aid, value);
        }
        z += value as f64 * amount as f64;
        w += itemdb::itemdb_weight(nameid) as i64 * amount as i64;
    }
    if master != 0 {
        // Script-based shops (npcshopattach, script2)
        crate::script::buildins::script2::npcctl2::npc_buylist_sub(c, aid, item_list, master);
        return PURCHASE_SUCCEED;
    }
    let zeny = status(c, aid).map(|ch| ch.zeny as f64).unwrap_or(0.0);
    if z > zeny {
        return PURCHASE_FAIL_MONEY; // Not enough Zeny
    }
    let Some(s) = st(c, aid) else { return PURCHASE_FAIL_COUNT };
    if w + s.weight as i64 > s.max_weight as i64 {
        return PURCHASE_FAIL_WEIGHT; // Too heavy
    }
    if (pc_inventoryblank(c, aid) as i32) < new_ {
        return PURCHASE_FAIL_COUNT; // Not enough space to store items
    }
    pc_payzeny(c, aid, z as i32 as i64, crate::map_log::LOG_TYPE_NPC, 0);
    for (i, e) in item_list.iter().enumerate() {
        let nameid = e.nameid;
        let amount = e.qty as u16 as i32;
        if nd.subtype == NPCTYPE_MARKETSHOP {
            let j = market_index[i];
            if let Some(mut n2) = c.db().npc().id().find(nd.id) {
                if n2.shop[j].qty >= 0 {
                    if amount > n2.shop[j].qty {
                        return PURCHASE_FAIL_COUNT;
                    }
                    let before = n2.shop.clone();
                    n2.shop[j].qty -= amount;
                    let entry = n2.shop[j].clone();
                    let exname = n2.exname.clone();
                    // the stock is the item list the shop's duplicates share (only this shop's row is saved)
                    crate::npc::shop_share(c, &n2, &before);
                    c.db().npc().id().update(n2);
                    npc_market_tosql(c, &exname, entry.nameid, entry.value, entry.qty, entry.flag);
                }
            }
        }
        if itemdb::itemdb_type(nameid) == itemdb::IT_PETEGG {
            // one egg per list entry whatever the amount (npc.cpp:2914-2915)
            crate::pet::pet_create_egg(c, aid, nameid);
        } else {
            let get_amt = if itemdb::itemdb_search(nameid).flag.guid { 1 } else { amount };
            let mut k = 0;
            while k < amount {
                let mut it = Item::new(nameid);
                it.identify = 1;
                pc_additem(c, aid, &mut it, get_amt, crate::map_log::LOG_TYPE_NPC);
                k += get_amt.max(1);
            }
        }
    }
    // custom merchant shop exp bonus (shop_exp: 0)
    let _ = shop_exp();
    PURCHASE_SUCCEED
}

/// npc.cpp npc_selllist: 0 = sold, 1 = refused.
pub fn npc_selllist(c: &Ctx, aid: u32, list: &[(u16, u16)]) -> u8 {
    let shopid = pc::get(c, aid).map(|p| p.npc_shopid).unwrap_or(0);
    let Some(nd) = npc_checknear(c, aid, shopid) else { return 1 };
    if nd.subtype != NPCTYPE_SHOP {
        return 1;
    }
    let master = crate::script::buildins::script2::npcctl2::shop_master(c, nd.id); // nd->master_nd
    let mut z: f64 = 0.0;
    // verify the sell list
    for &(index, amount) in list {
        let idx = index as i32 - 2;
        let amount = amount as i32;
        if idx >= MAX_INVENTORY as i32 || idx < 0 || amount < 0 {
            return 1;
        }
        let Some(r) = inv_get(c, aid, idx as usize) else { return 1 };
        if r.item.nameid == 0 || (r.item.amount as i32) < amount {
            return 1;
        }
        if master != 0 {
            continue; // Script-controlled shops decide by themselves, what can be sold and at what price.
        }
        if !pc_can_sell_item(c, aid, &r.item, nd.subtype) {
            return 1; // In official server, this illegal attempt the player will be disconnected
        }
        let value = if rental_item_novalue() && r.item.expire_time != 0 { 0 } else { pc_modifysellvalue(c, aid, data_of(&r.item).value_sell as i32) };
        z += value as f64 * amount as f64;
    }
    if master != 0 {
        // Script-controlled shops (npcshopattach, script2)
        return crate::script::buildins::script2::npcctl2::npc_selllist_sub(c, aid, list, master);
    }
    // delete items
    for &(index, amount) in list {
        let idx = index as i32 - 2;
        let Some(r) = inv_get(c, aid, idx as usize) else { return 1 }; // Forged packet, we do not care if he loses items
        if data_of(&r.item).r#type == itemdb::IT_PETEGG && r.item.card0 == itemdb::CARD0_PET {
            if crate::db::misc::pet::pet_db_search(r.item.nameid as i32, crate::db::misc::pet::PetItemType::Egg).is_some() {
                crate::pet::intif_delete_petdata(crate::pet::egg_pet_id(&r.item));
            }
        }
        if pc_delitem(c, aid, idx, amount as i32, 0, 6, crate::map_log::LOG_TYPE_NPC) != 0 {
            return 1;
        }
    }
    if z > MAX_ZENY as f64 {
        z = MAX_ZENY as f64;
    }
    pc_getzeny(c, aid, z as i32 as i64, crate::map_log::LOG_TYPE_NPC, 0);
    0
}

// ---------------------------------------------------------------------------------------------- market shops

/// npc.cpp npc_market_clearfromsql (npc_market_delfromsql_(exname, 0, true)): DELETE FROM market WHERE name = exname.
pub fn npc_market_clearfromsql(c: &Ctx, exname: &str) {
    let ids: Vec<u64> = c.db().market().name().filter(&exname.to_string()).map(|m| m.id).collect();
    for id in ids {
        c.db().market().id().delete(id);
    }
}

/// npc.cpp npc_market_tosql: REPLACE INTO market (name, nameid, price, amount, flag).
pub fn npc_market_tosql(c: &Ctx, exname: &str, nameid: u32, price: i32, amount: i32, flag: u8) {
    let existing = c.db().market().name().filter(&exname.to_string()).find(|m| m.nameid == nameid);
    match existing {
        Some(m) => {
            c.db().market().id().update(Market { price, amount, flag, ..m });
        }
        None => {
            c.db().market().insert(Market { id: 0, name: exname.to_string(), nameid, price, amount, flag });
        }
    }
}

/// npc.cpp npc_market_delfromsql_: `DELETE FROM market WHERE name = exname` when `clear`, else also
/// `AND nameid = nameid LIMIT 1`.
pub fn npc_market_delfromsql(c: &Ctx, exname: &str, nameid: u32, clear: bool) {
    let rows: Vec<u64> = c.db().market().name().filter(&exname.to_string()).filter(|m| clear || m.nameid == nameid).map(|m| m.id).collect();
    for id in rows.into_iter().take(if clear { usize::MAX } else { 1 }) {
        c.db().market().id().delete(id);
    }
}

/// npc.cpp struct s_npc_market: one market's rows as npc_market_fromsql read them (NPCMarketDB entry).
pub struct MarketStock {
    pub exname: String,
    pub list: Vec<crate::npc::ShopItem>,
}

/// npc.cpp npc_market_fromsql: the `market` table read before the NPC files load (`SELECT name, nameid, price,
/// amount, flag FROM market ORDER BY name`). Rows of one name come in primary key order (name, nameid) here: SQL
/// leaves their order open, and it only decides where npc_market_checkall appends script-added items.
pub fn npc_market_fromsql(c: &Ctx) -> Vec<MarketStock> {
    let mut rows: Vec<Market> = c.db().market().iter().collect();
    rows.sort_by(|a, b| (a.name.as_str(), a.nameid).cmp(&(b.name.as_str(), b.nameid)));
    let mut out: Vec<MarketStock> = Vec::new();
    let mut count = 0u32;
    for r in rows {
        // strdb_get / strdb_put: the market exists from its first row on, even when that row is skipped
        if out.last().map(|m| m.exname != r.name).unwrap_or(true) {
            out.push(MarketStock { exname: r.name.clone(), list: Vec::new() });
        }
        let Some(id) = itemdb::itemdb_exists(r.nameid) else {
            log::warn!("npc_market_fromsql: Invalid sell item in table 'market' (id '{}').", r.nameid);
            continue;
        };
        let mut value = r.price;
        if (value as f64) * 0.75 < id.value_sell as f64 * 1.24 {
            // Exploit possible: you can buy and sell back with profit
            log::warn!(
                "npc_market_fromsql: Item {} [{}] discounted buying price ({}->{}) is less than overcharged selling price ({}->{}) in table 'market'. Assigning to current sell value.",
                id.name, r.nameid, value, (value as f64 * 0.75) as i32, id.value_sell, (id.value_sell as f64 * 1.24) as i32
            );
            value = id.value_sell as i32;
        }
        out.last_mut().unwrap().list.push(crate::npc::ShopItem { nameid: r.nameid, value, qty: r.amount, flag: r.flag });
        count += 1;
    }
    log::info!("Done loading '{count}' entries for '{}' NPC Markets from 'market' table.", out.len());
    out
}

/// npc.cpp npc_market_checkall: after the NPC files loaded (and wrote their own stock, npc_parse_shop), the stock
/// read by npc_market_fromsql goes back into the market shops and the table.
pub fn npc_market_checkall(c: &Ctx, markets: &[MarketStock]) {
    if markets.is_empty() {
        return;
    }
    log::info!("Checking '{}' NPC Markets...", markets.len());
    for m in markets {
        npc_market_checkall_sub(c, m);
    }
    log::info!("Done checking '{}' NPC Markets.", markets.len());
}

/// npc.cpp npc_market_checkall_sub.
fn npc_market_checkall_sub(c: &Ctx, market: &MarketStock) {
    let Some(mut nd) = crate::script::npc_run::npc_name2id(c, &market.exname) else {
        log::info!("npc_market_checkall_sub: NPC '{}' not found, removing...", market.exname);
        npc_market_delfromsql(c, &market.exname, 0, true);
        return;
    };
    if nd.subtype != NPCTYPE_MARKETSHOP || nd.shop.is_empty() {
        log::error!("npc_market_checkall_sub: NPC '{}' is not proper for market, removing...", nd.exname);
        npc_market_delfromsql(c, &nd.exname, 0, true);
        return;
    }
    if market.list.is_empty() {
        return;
    }
    // what the shops sharing this list see: the in-place changes up to the first appended item (shop_sharers)
    let before = nd.shop.clone();
    let mut shared: Option<Vec<crate::npc::ShopItem>> = Some(nd.shop.clone());
    for list in &market.list {
        if itemdb::itemdb_exists(list.nameid).is_none() {
            log::error!("npc_market_checkall_sub: NPC '{}' sells invalid item '{}', deleting...", nd.exname, list.nameid);
            npc_market_delfromsql(c, &nd.exname, list.nameid, false);
            continue;
        }
        // Reloading stock from `market` table
        if let Some(j) = nd.shop.iter().position(|s| s.nameid == list.nameid) {
            nd.shop[j].value = list.value;
            if nd.shop[j].qty > -1 {
                nd.shop[j].qty = list.qty;
            }
            nd.shop[j].flag = list.flag;
            let s = nd.shop[j].clone();
            if let Some(v) = shared.as_mut() {
                *v = nd.shop.clone();
            }
            npc_market_tosql(c, &nd.exname, s.nameid, s.value, s.qty, s.flag);
            continue;
        }
        if list.flag & 1 != 0 {
            // Item added by npcshopitem/npcshopadditem, add new entry. (rAthena tests `qty > -1` on the new, not yet
            // written entry of the RECREATEd list; the stored stock is taken.)
            if let Some(v) = shared.take() {
                let mut view = nd.clone();
                view.shop = v;
                crate::npc::shop_share(c, &view, &before);
            }
            nd.shop.push(crate::npc::ShopItem { nameid: list.nameid, value: list.value, qty: list.qty, flag: list.flag });
            npc_market_tosql(c, &nd.exname, list.nameid, list.value, list.qty, list.flag);
        } else {
            // Removing "out-of-date" entry
            log::error!("npc_market_checkall_sub: NPC '{}' does not sell item {} (qty {}), deleting...", nd.exname, list.nameid, list.qty);
            npc_market_delfromsql(c, &nd.exname, list.nameid, false);
        }
    }
    if let Some(v) = shared {
        let mut view = nd.clone();
        view.shop = v;
        crate::npc::shop_share(c, &view, &before);
    }
    c.db().npc().id().update(nd);
}

/// clif.cpp clif_npc_market_open: ZC_NPC_MARKET_OPEN (sets sd->state.trading).
pub fn npc_market_open(c: &Ctx, aid: u32, nd: &Npc) {
    let Some(mut p) = pc::get(c, aid) else { return };
    if p.trading {
        return;
    }
    let Some(sd) = unit::get(c, aid) else { return };
    let mut list = Vec::new();
    for s in &nd.shop {
        if s.nameid == 0 {
            continue;
        }
        let Some(id) = itemdb::itemdb_exists(s.nameid) else { continue };
        list.push(PACKET_ZC_NPC_MARKET_OPEN_sub {
            nameid: itemdb::client_nameid(s.nameid),
            r#type: crate::clif_items::itemtype(s.nameid),
            price: s.value as u32,
            qty: s.qty as u32,
            weight: id.weight as u16,
            location: crate::pc_equip::pc_equippoint_sub(c, aid, id),
        });
    }
    let pk = PACKET_ZC_NPC_MARKET_OPEN { packetType: PACKET_ZC_NPC_MARKET_OPEN::ID as i16, packetLength: 0, list };
    clif::send(c, &pk.to_vec(), &sd, Target::SelfOnly);
    p.trading = true;
    pc::put(c, &p);
}

/// clif.cpp clif_npc_market_purchase_ack: ZC_NPC_MARKET_PURCHASE_RESULT (20211103: result 0 = ok, -1 = failed).
pub fn npc_market_purchase_ack(c: &Ctx, aid: u32, res: u8, list: &[BuyItem]) {
    let Some(p) = pc::get(c, aid) else { return };
    let Some(nd) = c.db().npc().id().find(p.npc_shopid) else { return };
    let Some(sd) = unit::get(c, aid) else { return };
    let mut out = Vec::new();
    if res == PURCHASE_SUCCEED {
        for e in list {
            let Some(j) = nd.shop.iter().position(|s| s.nameid == e.nameid) else { continue };
            out.push(PACKET_ZC_NPC_MARKET_PURCHASE_RESULT_sub { ITID: itemdb::client_nameid(e.nameid), qty: e.qty as u16, price: nd.shop[j].value as u32 });
        }
    }
    let pk = PACKET_ZC_NPC_MARKET_PURCHASE_RESULT {
        PacketType: PACKET_ZC_NPC_MARKET_PURCHASE_RESULT::ID as i16,
        PacketLength: 0,
        result: if res == PURCHASE_SUCCEED { 0 } else { 0xffff },
        list: out,
    };
    clif::send(c, &pk.to_vec(), &sd, Target::SelfOnly);
}

// ---------------------------------------------------------------------------------------------- parse

/// clif.cpp clif_parse_NpcBuySellSelected (CZ_ACK_SELECT_DEALTYPE).
pub fn parse_buy_sell_selected(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_ACK_SELECT_DEALTYPE::read(&mut crate::net::wire::Reader::new(pkt)) else { return };
    if pc::get(c, aid).map(|p| p.trading).unwrap_or(true) {
        return;
    }
    npc_buysellsel(c, aid, p.GID, p.r#type);
}

/// clif.cpp clif_parse_NpcBuyListSend (CZ_PC_PURCHASE_ITEMLIST).
pub fn parse_buy_list_send(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_PC_PURCHASE_ITEMLIST::read(&mut crate::net::wire::Reader::new(pkt)) else { return };
    let Some(pd) = pc::get(c, aid) else { return };
    let result = if pd.trading || pd.npc_shopid == 0 {
        PURCHASE_FAIL_MONEY
    } else {
        let mut items: Vec<BuyItem> = p.items.iter().map(|i| BuyItem { nameid: i.itemId, qty: i.amount as i32 }).collect();
        npc_buylist(c, aid, &mut items)
    };
    set_npc_shopid(c, aid, 0); // Clear shop data.
    clif_npc_buy_result(c, aid, result);
}

/// clif.cpp clif_parse_NpcSellListSend (CZ_PC_SELL_ITEMLIST).
pub fn parse_sell_list_send(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_PC_SELL_ITEMLIST::read(&mut crate::net::wire::Reader::new(pkt)) else { return };
    let Some(pd) = pc::get(c, aid) else { return };
    let fail = if pd.trading || pd.npc_shopid == 0 {
        1
    } else {
        let list: Vec<(u16, u16)> = p.sellList.iter().map(|s| (s.index, s.amount)).collect();
        npc_selllist(c, aid, &list)
    };
    set_npc_shopid(c, aid, 0); // Clear shop data.
    clif_npc_sell_result(c, aid, fail);
}

/// clif.cpp clif_parse_NPCShopClosed (CZ_NPC_TRADE_QUIT).
pub fn parse_shop_closed(c: &Ctx, aid: u32) {
    set_npc_shopid(c, aid, 0);
}

/// clif.cpp clif_parse_NPCMarketClosed.
pub fn parse_market_closed(c: &Ctx, aid: u32) {
    if let Some(mut p) = pc::get(c, aid) {
        p.npc_shopid = 0;
        p.trading = false;
        pc::put(c, &p);
    }
}

/// clif.cpp clif_parse_NPCMarketPurchase (CZ_NPC_MARKET_PURCHASE).
pub fn parse_market_purchase(c: &Ctx, aid: u32, pkt: &[u8]) {
    if pc::get(c, aid).map(|p| p.npc_shopid == 0).unwrap_or(true) {
        return;
    }
    let Some(p) = PACKET_CZ_NPC_MARKET_PURCHASE::read(&mut crate::net::wire::Reader::new(pkt)) else { return };
    let mut items: Vec<BuyItem> = p.list.iter().map(|i| BuyItem { nameid: i.ITID, qty: i.qty }).collect();
    let res = npc_buylist(c, aid, &mut items);
    npc_market_purchase_ack(c, aid, res, &items);
}
