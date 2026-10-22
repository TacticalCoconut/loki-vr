//! map/clif.cpp: the item packets (inventory / cart / storage lists and changes, equip / use acks, floor items,
//! shops, equip switch, rentals, weight limit, ZC_MSG*). Layouts are the generated rAthena structs (net/packets.rs)
//! for PACKETVER 20211103.

use crate::clif::{self, Target};
use crate::db::items::{self as itemdb, ItemData};
use crate::inventory::*;
use crate::net::packets::*;
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// clif.hpp e_clif_messages (PACKETVER 20211103) used by the item code.
pub const MSI_CANT_GET_ITEM_BECAUSE_WEIGHT: u16 = 52;
pub const MSI_CANT_GET_ITEM_BECAUSE_COUNT: u16 = 220;
pub const MSI_CAN_NOT_EQUIP_ITEM: u16 = 372;
pub const MSI_CANT_USE_WHEN_SITDOWN: u16 = 663;
pub const MSI_CANNOT_PARTYCALL: u16 = 1221;
pub const MSI_NO_PARTYMEM_ON_THISMAP: u16 = 1222;
pub const MSI_IMPOSSIBLE_USEITEM_AREA: u16 = 1335;
pub const MSI_USESKILL_FAIL_MADOGEAR: u16 = 1435;
pub const MSI_USESKILL_FAIL_CART: u16 = 1519;
pub const MSI_FAIL_NEED_EQUIPPED_BOW: u16 = 1691;
pub const MSI_ITEM_REUSE_LIMIT_SECOND: u16 = 1862;
pub const MSI_BUSY: u16 = 1923;
pub const MSI_WRONG_BULLET: u16 = 2493;
pub const MSI_SWAP_EQUIPITEM_UNREGISTER_FIRST: u16 = 3015;
pub const MSI_PICKUP_FAILED_ITEMCREATE: u16 = 3022;

/// clif.cpp e_inventory_type.
pub const INVTYPE_INVENTORY: u8 = 0;
pub const INVTYPE_CART: u8 = 1;
pub const INVTYPE_STORAGE: u8 = 2;
pub const INVTYPE_GUILD_STORAGE: u8 = 3;

/// Packet ids that depend on PACKETVER (clif_packetdb.hpp / packets.hpp for 20211103).
const USE_ITEM_ACK_TYPE: i16 = 0x01c8;
const INVENTORYLIST_NORMAL_TYPE: i16 = 0x0b09;
const INVENTORYLIST_EQUIP_TYPE: i16 = 0x0b39;
/// packets.hpp MAX_INVENTORY_ITEM_PACKET_NORMAL / _EQUIP: (INT16_MAX - header) / entry size (header 5 bytes).
const MAX_ITEMLIST_NORMAL: usize = (i16::MAX as usize - 5) / NORMALITEM_INFO::SIZE;
const MAX_ITEMLIST_EQUIP: usize = (i16::MAX as usize - 5) / EQUIPITEM_INFO::SIZE;

fn sd_of(c: &Ctx, aid: u32) -> Option<Unit> {
    unit::get(c, aid)
}

fn send_self(c: &Ctx, sd: &Unit, b: &[u8]) {
    clif::send(c, b, sd, Target::SelfOnly);
}

/// clif.cpp client_index / client_storage_index.
pub fn client_index(server_index: usize) -> u16 {
    (server_index + 2) as u16
}
pub fn client_storage_index(server_index: usize) -> u16 {
    (server_index + 1) as u16
}

/// clif.cpp itemtype: pet eggs show as armor, shadow gear as weapon / armor.
pub fn itemtype(nameid: u32) -> u8 {
    let id = itemdb::itemdb_search(nameid);
    let t = id.r#type;
    if t == itemdb::IT_SHADOWGEAR {
        return if id.equip & EQP_SHADOW_WEAPON != 0 { itemdb::IT_WEAPON } else { itemdb::IT_ARMOR };
    }
    if t == itemdb::IT_PETEGG {
        itemdb::IT_ARMOR
    } else {
        t
    }
}

/// clif.cpp clif_addcards.
pub fn addcards(item: Option<&Item>) -> EQUIPSLOTINFO {
    let Some(it) = item else { return EQUIPSLOTINFO { card: [0; 4] } };
    if it.card0 == itemdb::CARD0_PET {
        // Pet intimacy / renamed flag
        return EQUIPSLOTINFO { card: [0, 0, it.card3 >> 1, it.card3 & 1] };
    }
    if it.card0 == itemdb::CARD0_FORGE || it.card0 == itemdb::CARD0_CREATE {
        return EQUIPSLOTINFO { card: it.cards() };
    }
    // (MAX_SLOTS is 4: the client gets the four cards)
    let mut card = [0u32; 4];
    for (k, e) in card.iter_mut().enumerate() {
        let v = it.card(k);
        *e = if v > 0 { itemdb::client_nameid(v) } else { 0 };
    }
    EQUIPSLOTINFO { card }
}

/// clif.cpp clif_add_random_options.
pub fn add_random_options(it: &Item) -> Vec<ItemOptions> {
    (0..MAX_ITEM_RDM_OPT)
        .map(|i| {
            let o = it.opt(i);
            if o.id != 0 {
                ItemOptions { index: o.id, value: o.value, param: o.param }
            } else {
                ItemOptions::default()
            }
        })
        .collect()
}

/// clif.cpp clif_item_equip.
pub fn item_equip(idx: u16, it: &Item, id: &ItemData, eqp_pos: u32) -> EQUIPITEM_INFO {
    EQUIPITEM_INFO {
        index: idx as i16,
        ITID: itemdb::client_nameid(it.nameid),
        r#type: itemtype(it.nameid),
        location: eqp_pos,
        WearState: it.equip,
        slot: addcards(Some(it)),
        HireExpireDate: it.expire_time as i32,
        bindOnEquipType: if it.bound != 0 { 2 } else if id.flag.bind_on_equip { 1 } else { 0 },
        wItemSpriteNumber: if id.equip & EQP_VISIBLE != 0 { id.look as u16 } else { 0 },
        option_count: 0, // Client ingores the value anyway and recalculates it
        option_data: add_random_options(it),
        RefiningLevel: it.refine,
        grade: it.enchantgrade,
        // Flag: IsIdentified 1, IsDamaged 2, PlaceETCTab 4
        Flag: (it.identify != 0) as u8 | ((it.attribute != 0) as u8) << 1 | ((it.favorite != 0) as u8) << 2,
    }
}

/// clif.cpp clif_item_normal.
pub fn item_normal(idx: u16, it: &Item, id: &ItemData) -> NORMALITEM_INFO {
    NORMALITEM_INFO {
        index: idx as i16,
        ITID: itemdb::client_nameid(it.nameid),
        r#type: itemtype(it.nameid),
        count: it.amount,
        WearState: id.equip,
        slot: addcards(Some(it)),
        HireExpireDate: it.expire_time as i32,
        // Flag: IsIdentified 1, PlaceETCTab 2
        Flag: (it.identify != 0) as u8 | ((it.favorite != 0) as u8) << 1,
    }
}

/// clif.cpp clif_inventoryStart: ZC_INVENTORY_START with the name (NUL included, at most 24 bytes).
fn inventory_start(c: &Ctx, sd: &Unit, kind: u8, name: &str) {
    let mut n: Vec<u8> = crate::net::wire::latin1(name);
    n.truncate(23);
    n.push(0);
    let mut w = Vec::with_capacity(5 + n.len());
    w.extend_from_slice(&(PACKET_ZC_INVENTORY_START::ID).to_le_bytes());
    w.extend_from_slice(&((5 + n.len()) as u16).to_le_bytes());
    w.push(kind);
    w.extend_from_slice(&n);
    send_self(c, sd, &w);
}

/// clif.cpp clif_inventoryEnd.
fn inventory_end(c: &Ctx, sd: &Unit, kind: u8) {
    let p = PACKET_ZC_INVENTORY_END { packetType: PACKET_ZC_INVENTORY_END::ID as i16, invType: kind, flag: 0 };
    send_self(c, sd, &p.to_vec());
}

fn itemlist_normal(c: &Ctx, sd: &Unit, kind: u8, list: &[NORMALITEM_INFO]) {
    let mut w = Vec::with_capacity(5 + list.len() * NORMALITEM_INFO::SIZE);
    w.extend_from_slice(&INVENTORYLIST_NORMAL_TYPE.to_le_bytes());
    w.extend_from_slice(&0u16.to_le_bytes());
    w.push(kind);
    for e in list {
        e.write(&mut w);
    }
    let n = w.len() as u16;
    w[2..4].copy_from_slice(&n.to_le_bytes());
    send_self(c, sd, &w);
}

fn itemlist_equip(c: &Ctx, sd: &Unit, kind: u8, list: &[EQUIPITEM_INFO]) {
    let mut w = Vec::with_capacity(5 + list.len() * EQUIPITEM_INFO::SIZE);
    w.extend_from_slice(&INVENTORYLIST_EQUIP_TYPE.to_le_bytes());
    w.extend_from_slice(&0u16.to_le_bytes());
    w.push(kind);
    for e in list {
        e.write(&mut w);
    }
    let n = w.len() as u16;
    w[2..4].copy_from_slice(&n.to_le_bytes());
    send_self(c, sd, &w);
}

/// clif.cpp clif_inventorylist: start, the stackable items, the ammo mark, the equipment, end.
/// (rAthena also runs storage_sortitem over the first MAX_INVENTORY storage slots here; client_sort_storage is off.)
pub fn inventorylist(c: &Ctx, aid: u32) {
    let Some(sd) = sd_of(c, aid) else { return };
    inventory_start(c, &sd, INVTYPE_INVENTORY, "");
    let mut normal = Vec::new();
    let mut equip = Vec::new();
    for r in inv_list(c, aid) {
        let id = data_of(&r.item);
        if !id.is_stackable() {
            equip.push(item_equip(client_index(r.idx as usize), &r.item, id, crate::pc_equip::pc_equippoint(c, aid, &r.item)));
            if equip.len() == MAX_ITEMLIST_EQUIP {
                itemlist_equip(c, &sd, INVTYPE_INVENTORY, &equip);
                equip.clear();
            }
        } else {
            normal.push(item_normal(client_index(r.idx as usize), &r.item, id));
            if normal.len() == MAX_ITEMLIST_NORMAL {
                itemlist_normal(c, &sd, INVTYPE_INVENTORY, &normal);
                normal.clear();
            }
        }
    }
    if !normal.is_empty() {
        itemlist_normal(c, &sd, INVTYPE_INVENTORY, &normal);
    }
    if st(c, aid).map(|s| s.equip_index[EQI_AMMO] >= 0).unwrap_or(false) {
        arrowequip(c, aid);
    }
    if !equip.is_empty() {
        itemlist_equip(c, &sd, INVTYPE_INVENTORY, &equip);
    }
    inventory_end(c, &sd, INVTYPE_INVENTORY);
}

/// clif.cpp clif_equiplist ("TODO: implement again => only send equip part").
pub fn equiplist(c: &Ctx, aid: u32) {
    inventorylist(c, aid);
}

/// clif.cpp clif_additem: ZC_ITEM_PICKUP_ACK (`fail` != 0: an empty entry with the failure code).
pub fn additem(c: &Ctx, aid: u32, n: i32, amount: i32, fail: u8) {
    let Some(sd) = sd_of(c, aid) else { return };
    if !crate::pc::is_active_session(c, aid) {
        return;
    }
    let mut p = PACKET_ZC_ITEM_PICKUP_ACK::default();
    if fail == 0 {
        if n < 0 || n as usize >= MAX_INVENTORY {
            return;
        }
        let Some(r) = inv_get(c, aid, n as usize) else { return };
        let it = &r.item;
        let id = data_of(it);
        p.nameid = itemdb::client_nameid(it.nameid);
        p.IsIdentified = (it.identify != 0) as u8;
        p.IsDamaged = (it.attribute != 0) as u8;
        p.refiningLevel = it.refine;
        p.slot = addcards(Some(it));
        p.location = crate::pc_equip::pc_equippoint(c, aid, it);
        p.r#type = itemtype(it.nameid);
        p.HireExpireDate = it.expire_time as i32;
        p.bindOnEquipType = if it.bound != 0 && !id.is_stackable() { 2 } else if id.flag.bind_on_equip { 1 } else { 0 };
        p.option_data = add_random_options(it);
        p.favorite = (it.favorite != 0) as u8;
        p.look = id.look as u16;
        p.grade = it.enchantgrade;
    } else {
        p.option_data = vec![ItemOptions::default(); 5];
    }
    p.PacketType = PACKET_ZC_ITEM_PICKUP_ACK::ID as i16;
    p.Index = (n as u16).wrapping_add(2);
    p.count = amount as u16;
    p.result = fail;
    send_self(c, &sd, &p.to_vec());
}

/// clif.cpp clif_dropitem: ZC_ITEM_THROW_ACK.
pub fn dropitem(c: &Ctx, aid: u32, index: i32, amount: i32) {
    let Some(sd) = sd_of(c, aid) else { return };
    let p = PACKET_ZC_ITEM_THROW_ACK { packetType: PACKET_ZC_ITEM_THROW_ACK::ID as i16, index: (index + 2) as u16, count: amount as u16 };
    send_self(c, &sd, &p.to_vec());
}

/// clif.cpp clif_delitem: ZC_DELETE_ITEM_FROM_BODY.
pub fn delitem(c: &Ctx, aid: u32, index: i32, amount: i32, reason: i16) {
    let Some(sd) = sd_of(c, aid) else { return };
    let p = PACKET_ZC_DELETE_ITEM_FROM_BODY { packetType: PACKET_ZC_DELETE_ITEM_FROM_BODY::ID as i16, deleteType: reason, index: (index + 2) as u16, count: amount as i16 };
    send_self(c, &sd, &p.to_vec());
}

/// clif.cpp clif_useitemack: ZC_USE_ITEM_ACK2 (to the area when it worked).
pub fn useitemack(c: &Ctx, aid: u32, index: i32, amount: i32, ok: bool) {
    let Some(sd) = sd_of(c, aid) else { return };
    if !crate::pc::is_active_session(c, aid) {
        return;
    }
    if index < 0 || index as usize >= MAX_INVENTORY {
        return;
    }
    let Some(r) = inv_get(c, aid, index as usize) else { return };
    let p = PACKET_ZC_USE_ITEM_ACK { packetType: USE_ITEM_ACK_TYPE, index: (index + 2) as i16, itemId: itemdb::client_nameid(r.item.nameid), AID: aid, amount: amount as i16, result: ok as u8 };
    clif::send(c, &p.to_vec(), &sd, if ok { Target::Area } else { Target::SelfOnly });
}

/// clif.cpp clif_equipitemack: ZC_ACK_WEAR_EQUIP_V5.
pub fn equipitemack(c: &Ctx, aid: u32, flag: u8, index: i32, pos: u32) {
    let Some(sd) = sd_of(c, aid) else { return };
    let sprite = if flag == ITEM_EQUIP_ACK_OK {
        inv_get(c, aid, index.max(0) as usize).map(|r| data_of(&r.item)).filter(|id| id.equip & EQP_VISIBLE != 0).map(|id| id.look as u16).unwrap_or(0)
    } else {
        0
    };
    let p = PACKET_ZC_REQ_WEAR_EQUIP_ACK { PacketType: PACKET_ZC_REQ_WEAR_EQUIP_ACK::ID as i16, index: (index + 2) as u16, wearLocation: pos, wItemSpriteNumber: sprite, result: flag };
    send_self(c, &sd, &p.to_vec());
}

/// clif.cpp clif_unequipitemack: ZC_ACK_TAKEOFF_EQUIP_V5 (PACKETVER >= 20110824: 0 = success).
pub fn unequipitemack(c: &Ctx, aid: u32, server_index: u16, pos: u32, success: bool) {
    let Some(sd) = sd_of(c, aid) else { return };
    let p = PACKET_ZC_REQ_TAKEOFF_EQUIP_ACK { packetType: PACKET_ZC_REQ_TAKEOFF_EQUIP_ACK::ID, index: server_index.wrapping_add(2), wearLocation: pos, flag: (!success) as u8 };
    send_self(c, &sd, &p.to_vec());
}

/// clif.cpp clif_arrowequip: the EFST_CLIENT_ONLY_EQUIP_ARROW icon, then ZC_EQUIP_ARROW.
pub fn arrowequip(c: &Ctx, aid: u32) {
    let Some(sd) = sd_of(c, aid) else { return };
    const EFST_CLIENT_ONLY_EQUIP_ARROW: u16 = 695;
    crate::pc::status_icon(c, &sd, EFST_CLIENT_ONLY_EQUIP_ARROW, true);
    let idx = st(c, aid).map(|s| s.equip_index[EQI_AMMO]).unwrap_or(-1);
    let p = PACKET_ZC_EQUIP_ARROW { packetType: PACKET_ZC_EQUIP_ARROW::ID as i16, index: (idx + 2) as u16 };
    send_self(c, &sd, &p.to_vec());
}

/// clif.cpp clif_arrow_fail: ZC_ACTION_FAILURE.
pub fn arrow_fail(c: &Ctx, aid: u32, kind: u16) {
    let Some(sd) = sd_of(c, aid) else { return };
    let p = PACKET_ZC_ACTION_FAILURE { packetType: PACKET_ZC_ACTION_FAILURE::ID as i16, r#type: kind };
    send_self(c, &sd, &p.to_vec());
}

/// clif.cpp clif_notify_bindOnEquip.
pub fn notify_bind_on_equip(c: &Ctx, aid: u32, index: i16) {
    let Some(sd) = sd_of(c, aid) else { return };
    let p = PACKET_ZC_NOTIFY_BIND_ON_EQUIP { packetType: PACKET_ZC_NOTIFY_BIND_ON_EQUIP::ID as i16, index: index + 2 };
    send_self(c, &sd, &p.to_vec());
}

/// clif.cpp clif_rental_time: ZC_CASH_TIME_COUNTER.
pub fn rental_time(c: &Ctx, aid: u32, nameid: u32, seconds: i32) {
    let Some(sd) = sd_of(c, aid) else { return };
    let p = PACKET_ZC_CASH_TIME_COUNTER { packetType: PACKET_ZC_CASH_TIME_COUNTER::ID as i16, itemId: itemdb::client_nameid(nameid), seconds: seconds as u32 };
    send_self(c, &sd, &p.to_vec());
}

/// clif.cpp clif_rental_expired: ZC_CASH_ITEM_DELETE.
pub fn rental_expired(c: &Ctx, aid: u32, index: i32, nameid: u32) {
    let Some(sd) = sd_of(c, aid) else { return };
    let p = PACKET_ZC_CASH_ITEM_DELETE { packetType: PACKET_ZC_CASH_ITEM_DELETE::ID as i16, index: (index + 2) as u16, itemId: itemdb::client_nameid(nameid) };
    send_self(c, &sd, &p.to_vec());
}

// ---------------------------------------------------------------------------------------------- equip switch

/// clif.cpp clif_equipswitch_list: 0x0a9b { index, position }*.
pub fn equipswitch_list(c: &Ctx, aid: u32) {
    let Some(sd) = sd_of(c, aid) else { return };
    let Some(s) = st(c, aid) else { return };
    let mut w = vec![0x9b, 0x0a, 0, 0];
    let mut position = 0u32;
    for i in 0..EQI_MAX {
        let index = s.equip_switch_index[i];
        if index >= 0 && position & EQUIP_BITMASK[i] == 0 {
            let es = inv_get(c, aid, index as usize).map(|r| r.item.equip_switch).unwrap_or(0);
            w.extend_from_slice(&((index + 2) as u16).to_le_bytes());
            w.extend_from_slice(&es.to_le_bytes());
            position |= es;
        }
    }
    let n = w.len() as u16;
    w[2..4].copy_from_slice(&n.to_le_bytes());
    send_self(c, &sd, &w);
}

/// clif.cpp clif_equipswitch_remove: 0x0a9a.
pub fn equipswitch_remove(c: &Ctx, aid: u32, index: u16, pos: u32, failed: bool) {
    let Some(sd) = sd_of(c, aid) else { return };
    let mut w = Vec::with_capacity(10);
    w.extend_from_slice(&0x0a9au16.to_le_bytes());
    w.extend_from_slice(&index.wrapping_add(2).to_le_bytes());
    w.extend_from_slice(&pos.to_le_bytes());
    w.extend_from_slice(&(failed as u16).to_le_bytes());
    send_self(c, &sd, &w);
}

/// clif.cpp clif_equipswitch_add: 0x0a98 (PACKETVER > 20170502: the flag is a word).
pub fn equipswitch_add(c: &Ctx, aid: u32, index: u16, pos: u32, flag: u8) {
    let Some(sd) = sd_of(c, aid) else { return };
    let mut w = Vec::with_capacity(10);
    w.extend_from_slice(&0x0a98u16.to_le_bytes());
    w.extend_from_slice(&index.wrapping_add(2).to_le_bytes());
    w.extend_from_slice(&pos.to_le_bytes());
    w.extend_from_slice(&(flag as u16).to_le_bytes());
    send_self(c, &sd, &w);
}

/// clif.cpp clif_equipswitch_reply: 0x0a9d.
pub fn equipswitch_reply(c: &Ctx, aid: u32, failed: bool) {
    let Some(sd) = sd_of(c, aid) else { return };
    let mut w = Vec::with_capacity(4);
    w.extend_from_slice(&0x0a9du16.to_le_bytes());
    w.extend_from_slice(&(failed as u16).to_le_bytes());
    send_self(c, &sd, &w);
}

// ---------------------------------------------------------------------------------------------- messages

/// clif.cpp clif_msg: ZC_MSG.
pub fn msg(c: &Ctx, sd: &Unit, msg_id: u16) {
    let p = PACKET_ZC_MSG { packetType: PACKET_ZC_MSG::ID as i16, msgId: msg_id };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_msg_value: ZC_MSG_VALUE (0x07e2).
pub fn msg_value(c: &Ctx, sd: &Unit, msg_id: u16, value: i32) {
    let p = PACKET_ZC_MSG_VALUE { packetType: 0x07e2, message: msg_id, value };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_msg_color: ZC_MSG_COLOR.
pub fn msg_color(c: &Ctx, sd: &Unit, msg_id: u16, color: u32) {
    let p = PACKET_ZC_MSG_COLOR { PacketType: PACKET_ZC_MSG_COLOR::ID, MessageId: msg_id, MessageColor: color };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_skill_teleportmessage: ZC_NOTIFY_MAPINFO.
pub fn skill_teleportmessage(c: &Ctx, sd: &Unit, result: i16) {
    let p = PACKET_ZC_NOTIFY_MAPINFO { packetType: PACKET_ZC_NOTIFY_MAPINFO::ID as i16, r#type: result };
    send_self(c, sd, &p.to_vec());
}

// ---------------------------------------------------------------------------------------------- cart

/// clif.cpp clif_cartcount: ZC_NOTIFY_CARTITEM_COUNTINFO.
pub fn cartcount(c: &Ctx, aid: u32) {
    let Some(sd) = sd_of(c, aid) else { return };
    let Some(s) = st(c, aid) else { return };
    let p = PACKET_ZC_NOTIFY_CARTITEM_COUNTINFO {
        PacketType: PACKET_ZC_NOTIFY_CARTITEM_COUNTINFO::ID as i16,
        curCount: s.cart_num as i16,
        maxCount: MAX_CART as i16,
        curWeight: s.cart_weight as i32,
        maxWeight: s.cart_weight_max as i32,
    };
    send_self(c, &sd, &p.to_vec());
}

/// clif.cpp clif_cartlist.
pub fn cartlist(c: &Ctx, aid: u32) {
    let Some(sd) = sd_of(c, aid) else { return };
    inventory_start(c, &sd, INVTYPE_CART, "");
    let mut normal = Vec::new();
    let mut equip = Vec::new();
    for r in crate::cart::cart_list(c, aid) {
        let id = itemdb::itemdb_search(r.item.nameid);
        if !id.is_stackable() {
            equip.push(item_equip(client_index(r.idx as usize), &r.item, id, id.equip));
        } else {
            normal.push(item_normal(client_index(r.idx as usize), &r.item, id));
        }
    }
    if !normal.is_empty() {
        itemlist_normal(c, &sd, INVTYPE_CART, &normal);
    }
    if !equip.is_empty() {
        itemlist_equip(c, &sd, INVTYPE_CART, &equip);
    }
    inventory_end(c, &sd, INVTYPE_CART);
}

/// clif.cpp clif_clearcart: ZC_CARTOFF.
pub fn clearcart(c: &Ctx, aid: u32) {
    let Some(sd) = sd_of(c, aid) else { return };
    let p = PACKET_ZC_CARTOFF { packetType: PACKET_ZC_CARTOFF::ID as i16 };
    send_self(c, &sd, &p.to_vec());
}

/// clif.cpp clif_cart_additem: ZC_ADD_ITEM_TO_CART.
pub fn cart_additem(c: &Ctx, aid: u32, n: i32, amount: i32) {
    let Some(sd) = sd_of(c, aid) else { return };
    if !crate::pc::is_active_session(c, aid) {
        return;
    }
    if n < 0 || n as usize >= MAX_CART {
        return;
    }
    let Some(r) = crate::cart::cart_get(c, aid, n as usize) else { return };
    let it = &r.item;
    let p = PACKET_ZC_ADD_ITEM_TO_CART {
        packetType: PACKET_ZC_ADD_ITEM_TO_CART::ID as i16,
        index: client_index(n as usize) as i16,
        amount,
        itemId: itemdb::client_nameid(it.nameid),
        itemType: itemdb::itemdb_type(it.nameid),
        identified: it.identify,
        damaged: it.attribute,
        slot: addcards(Some(it)),
        option_data: add_random_options(it),
        refine: it.refine,
        grade: it.enchantgrade,
    };
    send_self(c, &sd, &p.to_vec());
}

/// clif.cpp clif_cart_delitem: ZC_DELETE_ITEM_FROM_CART.
pub fn cart_delitem(c: &Ctx, aid: u32, index: i32, amount: i32) {
    let Some(sd) = sd_of(c, aid) else { return };
    let p = PACKET_ZC_DELETE_ITEM_FROM_CART { packetType: PACKET_ZC_DELETE_ITEM_FROM_CART::ID as i16, index: (index + 2) as u16, amount };
    send_self(c, &sd, &p.to_vec());
}

/// clif.cpp clif_cart_additem_ack: ZC_ACK_ADDITEM_TO_CART (0 weight, 1 count).
pub fn cart_additem_ack(c: &Ctx, aid: u32, flag: u8) {
    let Some(sd) = sd_of(c, aid) else { return };
    let p = PACKET_ZC_ACK_ADDITEM_TO_CART { packetType: PACKET_ZC_ACK_ADDITEM_TO_CART::ID as i16, result: flag };
    send_self(c, &sd, &p.to_vec());
}

// ---------------------------------------------------------------------------------------------- storage

/// clif.cpp clif_storagelist: the storage window's contents (storage indexes are 1-based).
pub fn storagelist(c: &Ctx, aid: u32, items: &[(usize, Item)], storename: &str) {
    let Some(sd) = sd_of(c, aid) else { return };
    inventory_start(c, &sd, INVTYPE_STORAGE, storename);
    let mut normal = Vec::new();
    let mut equip = Vec::new();
    for (i, it) in items {
        if it.nameid == 0 {
            continue;
        }
        let id = itemdb::itemdb_search(it.nameid);
        if !id.is_stackable() {
            equip.push(item_equip(client_storage_index(*i), it, id, crate::pc_equip::pc_equippoint_sub(c, aid, id)));
            if equip.len() == MAX_ITEMLIST_EQUIP {
                itemlist_equip(c, &sd, INVTYPE_STORAGE, &equip);
                equip.clear();
            }
        } else {
            normal.push(item_normal(client_storage_index(*i), it, id));
            if normal.len() == MAX_ITEMLIST_NORMAL {
                itemlist_normal(c, &sd, INVTYPE_STORAGE, &normal);
                normal.clear();
            }
        }
    }
    if !normal.is_empty() {
        itemlist_normal(c, &sd, INVTYPE_STORAGE, &normal);
    }
    if !equip.is_empty() {
        itemlist_equip(c, &sd, INVTYPE_STORAGE, &equip);
    }
    inventory_end(c, &sd, INVTYPE_STORAGE);
}

/// clif.cpp clif_updatestorageamount: ZC_NOTIFY_STOREITEM_COUNTINFO.
pub fn updatestorageamount(c: &Ctx, aid: u32, amount: u16, max_amount: u16) {
    let Some(sd) = sd_of(c, aid) else { return };
    let p = PACKET_ZC_NOTIFY_STOREITEM_COUNTINFO { packetType: PACKET_ZC_NOTIFY_STOREITEM_COUNTINFO::ID as i16, amount, max_amount };
    send_self(c, &sd, &p.to_vec());
}

/// clif.cpp clif_storageitemadded: ZC_ADD_ITEM_TO_STORE.
pub fn storageitemadded(c: &Ctx, aid: u32, it: &Item, index: usize, amount: i32) {
    let Some(sd) = sd_of(c, aid) else { return };
    if !crate::pc::is_active_session(c, aid) {
        return;
    }
    let p = PACKET_ZC_ADD_ITEM_TO_STORE {
        packetType: PACKET_ZC_ADD_ITEM_TO_STORE::ID as i16,
        index: client_storage_index(index) as i16,
        amount,
        itemId: itemdb::client_nameid(it.nameid),
        itemType: itemtype(it.nameid),
        identified: it.identify,
        damaged: it.attribute,
        slot: addcards(Some(it)),
        option_data: add_random_options(it),
        refine: it.refine,
        grade: it.enchantgrade,
    };
    send_self(c, &sd, &p.to_vec());
}

/// clif.cpp clif_storageitemremoved: ZC_DELETE_ITEM_FROM_STORE.
pub fn storageitemremoved(c: &Ctx, aid: u32, index: i32, amount: u32) {
    let Some(sd) = sd_of(c, aid) else { return };
    let p = PACKET_ZC_DELETE_ITEM_FROM_STORE { packetType: PACKET_ZC_DELETE_ITEM_FROM_STORE::ID as i16, index: (index + 1) as u16, amount };
    send_self(c, &sd, &p.to_vec());
}

/// clif.cpp clif_storageclose: ZC_CLOSE_STORE.
pub fn storageclose(c: &Ctx, aid: u32) {
    let Some(sd) = sd_of(c, aid) else { return };
    let p = PACKET_ZC_CLOSE_STORE { packetType: PACKET_ZC_CLOSE_STORE::ID as i16 };
    send_self(c, &sd, &p.to_vec());
}

// ---------------------------------------------------------------------------------------------- refine / effects

/// clif.hpp e_ack_itemrefining.
pub const ITEMREFINING_SUCCESS: u16 = 0;
pub const ITEMREFINING_FAILURE: u16 = 1;
pub const ITEMREFINING_DOWNGRADE: u16 = 2;
/// clif.hpp e_notify_effect.
pub const NOTIFYEFFECT_REFINE_FAILURE: u32 = 2;
pub const NOTIFYEFFECT_REFINE_SUCCESS: u32 = 3;

/// clif.cpp clif_refine: ZC_ACK_ITEMREFINING (the refine is the slot's current one).
pub fn refine(c: &Ctx, aid: u32, index: usize, result: u16) {
    let Some(sd) = sd_of(c, aid) else { return };
    let value = inv_get(c, aid, index).map(|r| r.item.refine).unwrap_or(0);
    let p = PACKET_ZC_ACK_ITEMREFINING { packetType: PACKET_ZC_ACK_ITEMREFINING::ID as i16, result, index: client_index(index), value: value as u16 };
    send_self(c, &sd, &p.to_vec());
}


/// clif.cpp clif_solved_charname: ZC_ACK_REQNAME_BYGID (flag 3 with a name, 2 without; PACKETVER >= 20180221).
pub fn solved_charname(c: &Ctx, sd: &Unit, charid: u32, name: &str) {
    let p = PACKET_ZC_ACK_REQNAME_BYGID { packetType: PACKET_ZC_ACK_REQNAME_BYGID::ID as i16, flag: if name.is_empty() { 2 } else { 3 }, CID: charid, name: name.to_string() };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_produceeffect: the player's own name, then ZC_ACK_REQMAKINGITEM.
pub fn produceeffect(c: &Ctx, aid: u32, flag: i16, nameid: u32) {
    let Some(sd) = sd_of(c, aid) else { return };
    let (cid, name) = crate::pc::character_of(c, aid).map(|ch| (ch.id, ch.name.clone())).unwrap_or_default();
    solved_charname(c, &sd, cid, &name);
    let p = PACKET_ZC_ACK_REQMAKINGITEM { packetType: PACKET_ZC_ACK_REQMAKINGITEM::ID as i16, result: flag, itemId: itemdb::client_nameid(nameid) };
    send_self(c, &sd, &p.to_vec());
}

/// clif.cpp clif_party_show_picker: ZC_ITEM_PICKUP_PARTY to the party members on the map (PARTY_SAMEMAP_WOS).
pub fn party_show_picker(c: &Ctx, aid: u32, it: &Item) {
    let Some(sd) = sd_of(c, aid) else { return };
    let id = itemdb::itemdb_search(it.nameid);
    let p = PACKET_ZC_ITEM_PICKUP_PARTY {
        packetType: PACKET_ZC_ITEM_PICKUP_PARTY::ID as i16,
        AID: aid,
        itemId: itemdb::client_nameid(it.nameid),
        identified: it.identify,
        damaged: it.attribute,
        slot: addcards(Some(it)),
        location: id.equip as u16, // equip location
        itemType: itemtype(id.nameid), // item type
        refine: it.refine,
        grade: it.enchantgrade,
    };
    crate::party::send(c, &p.to_vec(), &sd, crate::party::PTarget::PartySamemapWos);
}
