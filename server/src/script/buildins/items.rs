//! Script commands of the items system (script.cpp BUILDIN_FUNCs): getitem / getitem2-4 / getitembound(2-4),
//! rentitem(2-4), getnameditem, grouprandomitem, makeitem(2-4), delitem(2-4) / cart- / storage- / guildstorage-,
//! delitemidx, countitem(2-4) / cartcountitem(2) / storagecountitem(2) / guildstoragecountitem(2) /
//! rentalcountitem(2-4), checkweight(2), getinventorylist, the equipment queries and refining commands
//! (getequipid, getequipuniqueid, getequipname, getbrokenid, repair, repairall, getequipisequiped,
//! getequipisenableref, getequiprefinerycnt, getequipweaponlv, getequiparmorlv, getequippercentrefinery,
//! successrefitem, failedrefitem, downrefitem, delequip, breakequip, getequipcardcnt, successremovecards,
//! failedremovecards, getequipcardid, getequiprefinecost, getequiptradability, getenchantgrade, getitempos),
//! getitemname, getitemslots, getiteminfo, clearitem, nude, checkequipedcard, isequippedcnt, isequipped, cardscnt,
//! getrefine, unequip, equip, autoequip, getrandgroupitem, getgroupitem, consumeitem, countbound,
//! enable_items / disable_items, itemheal, percentheal, checkcart, setcart, openstorage, openstorage2, the shop commands
//! callshop / npcshopadditem / npcshopupdate (npcshopattach and its OnBuyItem / OnSellItem events are not
//! ported: script-controlled shops); and getskilllv /
//! basicskillcheck / vip_status (commands of other systems that the Kafra storage service needs: F_KafSet,
//! F_CanOpenStorage).
//!
//! The guild storage variants work on guild_storage.rs (guild2storage / guild2storage2, storage_guild_delitem);
//! guildopenstorage(_log) are in buildins/guild.rs. Item logs go through log.rs (log_pick_pc). Pet eggs go through
//! pet.rs (pet_create_egg, intif_delete_petdata).


use super::*;
use crate::clif_items as ci;
use crate::db::items::{self as itemdb, ItemData, CARD0_CREATE, CARD0_PET};
use crate::inventory::{self as inv, Item, ItemOption, EQI_MAX, EQUIP_BITMASK, MAX_INVENTORY, MAX_ITEM_RDM_OPT, MAX_REFINE, MAX_SLOTS};
use crate::pc_equip;
use crate::script::npc_run::{self, put_sdx, sdx};
use crate::script::reg;
use crate::script::vm::*;
use crate::world::Ctx;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "getitem" => getitem,
        "getitem2" => getitem2,
        "rentitem" => rentitem,
        "rentitem2" => rentitem2,
        "getnameditem" => getnameditem,
        "grouprandomitem" => grouprandomitem,
        "makeitem" => makeitem,
        "makeitem2" => makeitem2,
        "delitem" => delitem,
        "delitem2" => delitem2,
        "delitemidx" => delitemidx,
        "countitem" => countitem,
        "cartcountitem" => cartcountitem,
        "storagecountitem" => storagecountitem,
        "guildstoragecountitem" => guildstoragecountitem,
        "rentalcountitem" => rentalcountitem,
        "checkweight" => checkweight,
        "checkweight2" => checkweight2,
        "getinventorylist" => getinventorylist,
        "getequipid" => getequipid,
        "getequipuniqueid" => getequipuniqueid,
        "getequipname" => getequipname,
        "getbrokenid" => getbrokenid,
        "repair" => repair,
        "repairall" => repairall,
        "getequipisequiped" => getequipisequiped,
        "getequipisenableref" => getequipisenableref,
        "getequiprefinerycnt" => getequiprefinerycnt,
        "getequipweaponlv" => getequipweaponlv,
        "getequiparmorlv" => getequiparmorlv,
        "getequippercentrefinery" => getequippercentrefinery,
        "successrefitem" => successrefitem,
        "failedrefitem" => failedrefitem,
        "downrefitem" => downrefitem,
        "delequip" => delequip,
        "breakequip" => breakequip,
        "getequipcardcnt" => getequipcardcnt,
        "successremovecards" => successremovecards,
        "failedremovecards" => failedremovecards,
        "getequipcardid" => getequipcardid,
        "getitemname" => getitemname,
        "getitemslots" => getitemslots,
        "getiteminfo" => getiteminfo,
        "clearitem" => clearitem,
        "nude" => nude,
        "checkequipedcard" => checkequipedcard,
        "isequippedcnt" => isequippedcnt,
        "isequipped" => isequipped,
        "cardscnt" => cardscnt,
        "getrefine" => getrefine,
        "unequip" => unequip,
        "equip" => equip,
        "autoequip" => autoequip,
        "getrandgroupitem" => getrandgroupitem,
        "getgroupitem" => getgroupitem,
        "consumeitem" => consumeitem,
        "countbound" => countbound,
        "enableitemuse" => enableitemuse,
        "disableitemuse" => disableitemuse,
        "checkcart" => checkcart,
        "setcart" => setcart,
        "openstorage" => openstorage,
        "openstorage2" => openstorage2,
        "getequiprefinecost" => getequiprefinecost,
        "getequiptradability" => getequiptradability,
        "getenchantgrade" => getenchantgrade,
        "getitempos" => getitempos,
        "getskilllv" => getskilllv,
        "basicskillcheck" => basicskillcheck,
        "vip_status" => vip_status,
        "callshop" => callshop,
        "npcshopadditem" => npcshopadditem,
        "npcshopupdate" => npcshopupdate,
        _ => return None,
    })
}

// ---------------------------------------------------------------------------------------------- helpers

/// itemdb.hpp UNKNOWN_ITEM_ID.
const UNKNOWN_ITEM_ID: i64 = 512;
/// mmo.hpp BOUND_MAX.
const BOUND_MAX: i32 = 5;
/// mmo.hpp ENCHANTGRADE_NONE / MAX_ENCHANTGRADE (RENEWAL).
const ENCHANTGRADE_NONE: i32 = 0;
const MAX_ENCHANTGRADE: i32 = 4;
/// pc.hpp EQI_COMPOUND_ON.
const EQI_COMPOUND_ON: i32 = -1;
/// mmo.hpp ITEM_NAME_LENGTH.
const ITEM_NAME_LENGTH: usize = 50;
/// status.hpp e_refine_cost_type REFINE_COST_MAX, e_refine_parameter.
const REFINE_COST_MAX: i32 = 3;
const REFINE_MATERIAL_ID: i32 = 0;
const REFINE_ZENY_COST: i32 = 1;
/// storage.hpp storage_type (the default: TABLE_INVENTORY).
const TABLE_INVENTORY: u8 = 0;
const TABLE_CART: u8 = 2;
const TABLE_STORAGE: u8 = 3;
const TABLE_GUILD_STORAGE: u8 = 4;
/// chrif.cpp CheckForCharServer: true when the char-server is down. The char-server is part of this database.
const CHAR_SERVER_DOWN: bool = false;

/// script.hpp e_item_info (ITEMINFO_*).
const ITEMINFO_BUY: i32 = 0;
const ITEMINFO_SELL: i32 = 1;
const ITEMINFO_TYPE: i32 = 2;
const ITEMINFO_MAXCHANCE: i32 = 3;
const ITEMINFO_GENDER: i32 = 4;
const ITEMINFO_LOCATIONS: i32 = 5;
const ITEMINFO_WEIGHT: i32 = 6;
const ITEMINFO_ATTACK: i32 = 7;
const ITEMINFO_DEFENSE: i32 = 8;
const ITEMINFO_RANGE: i32 = 9;
const ITEMINFO_SLOT: i32 = 10;
const ITEMINFO_VIEW: i32 = 11;
const ITEMINFO_EQUIPLEVELMIN: i32 = 12;
const ITEMINFO_WEAPONLEVEL: i32 = 13;
const ITEMINFO_ALIASNAME: i32 = 14;
const ITEMINFO_EQUIPLEVELMAX: i32 = 15;
const ITEMINFO_MAGICATTACK: i32 = 16;
const ITEMINFO_ID: i32 = 17;
const ITEMINFO_AEGISNAME: i32 = 18;
const ITEMINFO_ARMORLEVEL: i32 = 19;
const ITEMINFO_SUBTYPE: i32 = 20;

fn last_char(s: &str) -> char {
    s.chars().last().unwrap_or('\0')
}

/// script_mapid2sd_: the player whose map (block) id is argument `loc`, or the attached one.
fn mapid2sd(rt: &Rt, st: &mut State, loc: i32, func: &str) -> Option<u32> {
    if st.hasdata(loc) {
        let id = getnum(rt, st, loc);
        let ok = rt.c.map(|c| crate::pc::get(c, id as u32).is_some()).unwrap_or(false);
        if !ok {
            log::error!("{func}: Player with map id '{id}' is not found.");
            return None;
        }
        Some(id as u32)
    } else {
        rid2sd(rt, st, func)
    }
}

/// The item of argument `i`: by name (item_db.searchname) when it is a string, else by id (item_db.find).
/// Returns the item and the argument as text (for the error messages that print script_getstr).
fn item_arg(rt: &Rt, st: &mut State, i: i32) -> Option<&'static ItemData> {
    if isstring(rt, st, i) {
        let name = lat(&getstr(rt, st, i));
        itemdb::itemdb_searchname(&name)
    } else {
        itemdb::itemdb_exists(getnum(rt, st, i) as u32)
    }
}

/// pc_setreg(sd, reference_uid(add_str(name), idx), v).
fn setreg(rt: &Rt, st: &mut State, aid: u32, name: &str, idx: u32, v: i64) {
    let n = crate::net::wire::latin1(name);
    let id = add_str(&n);
    reg::set_reg_num(rt, st, Some(aid), id, idx, &n, v, &RegRef::None);
}

/// pc.hpp equip_index_check.
fn equip_index_check(num: i32) -> bool {
    num >= 0 && (num as usize) < EQI_MAX
}

/// `pc_checkequip(sd, equip_bitmask[num])` when `num` passes equip_index_check, else -1.
fn checkequip(c: &Ctx, aid: u32, num: i32) -> i16 {
    if equip_index_check(num) {
        pc_equip::pc_checkequip(c, aid, EQUIP_BITMASK[num as usize], false)
    } else {
        -1
    }
}

/// The item in inventory slot `i` (None: empty slot, like inventory_data[i] == nullptr).
fn inv_item(c: &Ctx, aid: u32, i: i32) -> Option<Item> {
    if i < 0 || i as usize >= MAX_INVENTORY {
        return None;
    }
    inv::inv_get(c, aid, i as usize).map(|r| r.item)
}

fn set_inv_item(c: &Ctx, aid: u32, i: i32, it: &Item) {
    if let Some(mut r) = inv::inv_get(c, aid, i as usize) {
        r.item = it.clone();
        inv::inv_put(c, &r);
    }
}

/// status.cpp current_equip_item_index (the item whose bonus / OnEquip / OnUnequip script runs; status/calc.rs).
fn current_equip_item_index(_c: &Ctx) -> i16 {
    crate::status::calc::current_equip_item_index()
}

fn equip_index(c: &Ctx, aid: u32) -> Vec<i16> {
    inv::st(c, aid).map(|s| s.equip_index).unwrap_or_else(|| vec![-1; EQI_MAX])
}

/// script.cpp script_getitem_randomoption: the random options of an item from three script arrays (id, value,
/// param) starting at argument `offset`.
pub(crate) fn getitem_randomoption(rt: &Rt, st: &mut State, sd: &mut Option<u32>, it: &mut Item, funcname: &str, offset: i32) -> bool {
    let datas = [st.data(offset).clone(), st.data(offset + 1).clone(), st.data(offset + 2).clone()];
    let what = ["option id", "option value", "option param"];
    let mut refs = Vec::new();
    for (d, w) in datas.iter().zip(what) {
        let (id, idx, rf) = match d {
            Data::Name(id, idx, rf) => (*id, *idx, rf.clone()),
            _ => (0, 0, RegRef::None),
        };
        let name = if d.is_reference() { name_of(id) } else { Vec::new() };
        let var = lat(&name);
        // Check if the variable requires a player
        if not_server_variable(name.first().copied().unwrap_or(0)) && sd.is_none() {
            *sd = rid2sd(rt, st, &format!("buildin_{funcname}"));
            if sd.is_none() {
                log::error!("buildin_{funcname}: variable \"{var}\" was not a server variable, but no player was attached.");
                return false;
            }
        }
        if !d.is_reference() || reg::store_for(rt, st, *sd, &name, &rf).is_none() {
            log::error!("buildin_{funcname}: The {w} parameter is not an array.");
            return false;
        }
        if is_string_variable(&name) {
            log::error!("buildin_{funcname}: The array {var} is not numeric type.");
            return false;
        }
        refs.push((id, idx, rf, name));
    }
    let n = reg::highest_key(rt, st, *sd, &refs[0].3, &refs[0].2);
    let mut opts = it.options();
    for i in 0..(n as usize).min(MAX_ITEM_RDM_OPT) {
        let id = get_val2_num(rt, st, refs[0].0, refs[0].1 + i as u32, &refs[0].2);
        let value = get_val2_num(rt, st, refs[1].0, refs[1].1 + i as u32, &refs[1].2);
        let param = get_val2_num(rt, st, refs[2].0, refs[2].1 + i as u32, &refs[2].2);
        opts[i] = ItemOption { id: id as i16, value: value as i16, param: param as i8 as u8 };
    }
    it.option = opts.to_vec();
    true
}

/// pet.cpp pet_create_egg (pet.rs: the egg arrives with the char server's answer).
fn pet_create_egg(c: &Ctx, aid: u32, nameid: u32) -> bool {
    crate::pet::pet_create_egg(c, aid, nameid)
}

/// The weapon / armor / pet egg / other rules of getitem2, rentitem2 and makeitem2 on (identify, refine, attribute).
fn fix_iden_ref_attr(id: &ItemData, iden: &mut i32, refine: &mut i32, attr: &mut i32) {
    if id.r#type == itemdb::IT_WEAPON || id.r#type == itemdb::IT_ARMOR || id.r#type == itemdb::IT_SHADOWGEAR {
        if *refine > MAX_REFINE {
            *refine = MAX_REFINE;
        }
    } else if id.r#type == itemdb::IT_PETEGG {
        *iden = 1;
        *refine = 0;
    } else {
        *iden = 1;
        *refine = 0;
        *attr = 0;
    }
}

// ---------------------------------------------------------------------------------------------- getting items

/// getitem <item id>,<amount>{,<account ID>}; getitembound <item id>,<amount>,<type>{,<account ID>}.
fn getitem(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let command = st.funcname.clone();
    let id = if isstring(rt, st, 2) {
        let name = lat(&getstr(rt, st, 2));
        match itemdb::itemdb_searchname(&name) {
            Some(d) => d,
            None => {
                log::error!("buildin_getitem: Nonexistant item {name} requested.");
                return SCRIPT_CMD_FAILURE; // No item created.
            }
        }
    } else {
        let nameid = getnum(rt, st, 2) as u32;
        match itemdb::itemdb_exists(nameid) {
            Some(d) => d,
            None => {
                log::error!("buildin_getitem: Nonexistant item {nameid} requested.");
                return SCRIPT_CMD_FAILURE; // No item created.
            }
        }
    };
    // <amount> (uint16: a negative amount is a big one)
    let amount = getnum(rt, st, 3) as u16;
    if amount == 0 {
        return SCRIPT_CMD_SUCCESS; // return if amount <=0, skip the useles iteration
    }
    let mut it = Item::new(id.nameid);
    it.identify = 1;
    it.bound = 0;
    let sd = if command == "getitembound" {
        let bound = getnum(rt, st, 4) as i8 as i32;
        if bound < 0 || bound >= BOUND_MAX {
            log::error!("script_getitembound: Not a correct bound type! Type={bound}");
            return SCRIPT_CMD_FAILURE;
        }
        let sd = mapid2sd(rt, st, 5, "buildin_getitem");
        it.bound = bound as u8;
        sd
    } else {
        mapid2sd(rt, st, 4, "buildin_getitem")
    };
    let Some(aid) = sd else { return SCRIPT_CMD_SUCCESS }; // no target
    // Check if it's stackable.
    let get_count: i32 = if !itemdb::itemdb_isstackable2(id) { 1 } else { amount as i32 };
    let mut i = 0i32;
    while i < amount as i32 {
        // if not pet egg
        if !pet_create_egg(c, aid, id.nameid) {
            let flag = inv::pc_additem(c, aid, &mut it, get_count, crate::map_log::LOG_TYPE_SCRIPT);
            if flag != inv::ADDITEM_SUCCESS {
                ci::additem(c, aid, 0, 0, flag);
                log::error!("buildin_getitem: Failed to add the item to player.");
                return SCRIPT_CMD_FAILURE;
            }
        }
        i += get_count;
    }
    SCRIPT_CMD_SUCCESS
}

/// getitem2 / getitem3 / getitem4 / getitembound2 / getitembound3 / getitembound4.
fn getitem2(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let command = st.funcname.clone();
    let mut bound: i32 = 0;
    let mut offset = 0;
    let mut grade_offset = 0;
    let sd = if command.starts_with("getitembound") {
        let mut aid_pos = 12;
        bound = getnum(rt, st, 11) as i8 as i32;
        if bound < 0 || bound >= BOUND_MAX {
            log::error!("script_getitembound2: Not a correct bound type! Type={bound}");
            return SCRIPT_CMD_FAILURE;
        }
        if last_char(&command) == '3' {
            offset = 12;
            aid_pos = 15;
        } else if last_char(&command) == '4' {
            grade_offset = 12;
            offset = 13;
            aid_pos = 16;
        }
        mapid2sd(rt, st, aid_pos, "buildin_getitem2")
    } else {
        let mut aid_pos = 11;
        if command.eq_ignore_ascii_case("getitem3") {
            offset = 11;
            aid_pos = 14;
        }
        if command.eq_ignore_ascii_case("getitem4") {
            grade_offset = 11;
            offset = 12;
            aid_pos = 15;
        }
        mapid2sd(rt, st, aid_pos, "buildin_getitem2")
    };
    let Some(aid) = sd else { return SCRIPT_CMD_SUCCESS }; // no target
    let data = if isstring(rt, st, 2) {
        let name = lat(&getstr(rt, st, 2));
        match itemdb::itemdb_searchname(&name) {
            Some(d) => d,
            None => {
                log::error!("buildin_getitem2: Nonexistant item {name} requested (by conv_str).");
                return SCRIPT_CMD_FAILURE; // No item created.
            }
        }
    } else {
        let nameid = getnum(rt, st, 2) as u32;
        match itemdb::itemdb_exists(nameid) {
            Some(d) => d,
            None => {
                log::error!("buildin_getitem2: Nonexistant item {nameid} requested (by conv_num).");
                return SCRIPT_CMD_FAILURE; // No item created.
            }
        }
    };
    let amount = getnum(rt, st, 3);
    let mut iden = getnum(rt, st, 4);
    let mut refine = getnum(rt, st, 5);
    let mut attr = getnum(rt, st, 6);
    let c1 = getnum(rt, st, 7) as u32;
    let c2 = getnum(rt, st, 8) as u32;
    let c3 = getnum(rt, st, 9) as u32;
    let c4 = getnum(rt, st, 10) as u32;
    fix_iden_ref_attr(data, &mut iden, &mut refine, &mut attr);
    let mut it = Item::new(data.nameid);
    it.identify = iden as u8;
    it.refine = refine as u8;
    it.attribute = attr as u8;
    it.card0 = c1;
    it.card1 = c2;
    it.card2 = c3;
    it.card3 = c4;
    it.bound = bound as u8;
    if offset != 0 {
        let mut sdo = Some(aid);
        if !getitem_randomoption(rt, st, &mut sdo, &mut it, &command, offset) {
            return SCRIPT_CMD_FAILURE;
        }
        if grade_offset > 0 {
            let grade = getnum(rt, st, grade_offset);
            if !(ENCHANTGRADE_NONE..=MAX_ENCHANTGRADE).contains(&grade) {
                log::error!("buildin_getitem2: Not a correct grade! Grade={grade}");
                return SCRIPT_CMD_FAILURE;
            }
            it.enchantgrade = grade as u8;
        }
    }
    // Check if it's stackable.
    let get_count = if !itemdb::itemdb_isstackable2(data) { 1 } else { amount };
    let mut i = 0;
    while i < amount {
        // if not pet egg
        if !pet_create_egg(c, aid, data.nameid) {
            let flag = inv::pc_additem(c, aid, &mut it, get_count, crate::map_log::LOG_TYPE_SCRIPT);
            if flag != inv::ADDITEM_SUCCESS {
                ci::additem(c, aid, 0, 0, flag);
                log::error!("buildin_getitem2: Failed to add the item to player.");
                return SCRIPT_CMD_FAILURE;
            }
        }
        i += get_count;
    }
    SCRIPT_CMD_SUCCESS
}

/// rentitem <item id>,<seconds>{,<account_id>}.
fn rentitem(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = accid2sd(rt, st, 4, "buildin_rentitem") else { return SCRIPT_CMD_FAILURE };
    let nameid = if isstring(rt, st, 2) {
        let name = lat(&getstr(rt, st, 2));
        match itemdb::itemdb_searchname(&name) {
            Some(d) => d.nameid,
            None => {
                log::error!("buildin_rentitem: Nonexistant item {name} requested.");
                return SCRIPT_CMD_FAILURE;
            }
        }
    } else {
        let nameid = getnum(rt, st, 2) as u32;
        if itemdb::itemdb_exists(nameid).is_none() {
            log::error!("buildin_rentitem: Nonexistant item {nameid} requested.");
            return SCRIPT_CMD_FAILURE;
        }
        nameid
    };
    let seconds = getnum(rt, st, 3);
    let mut it = Item::new(nameid);
    it.identify = 1;
    it.expire_time = (c.tick() / 1000 + seconds as i64) as u32;
    it.bound = 0;
    let flag = inv::pc_additem(c, aid, &mut it, 1, crate::map_log::LOG_TYPE_SCRIPT);
    if flag != 0 {
        ci::additem(c, aid, 0, 0, flag);
        return SCRIPT_CMD_FAILURE;
    }
    SCRIPT_CMD_SUCCESS
}

/// rentitem2 / rentitem3 / rentitem4.
fn rentitem2(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let funcname = st.funcname.clone();
    let aid_pos = match last_char(&funcname) {
        '3' => 14,
        '4' => 15,
        _ => 11,
    };
    let Some(aid) = accid2sd(rt, st, aid_pos, "buildin_rentitem2") else { return SCRIPT_CMD_FAILURE };
    let id = if isstring(rt, st, 2) {
        let name = lat(&getstr(rt, st, 2));
        match itemdb::itemdb_searchname(&name) {
            Some(d) => d,
            None => {
                log::error!("buildin_rentitem2: Nonexistant item {name} requested.");
                return SCRIPT_CMD_FAILURE;
            }
        }
    } else {
        let nameid = getnum(rt, st, 2) as u32;
        match itemdb::itemdb_exists(nameid) {
            Some(d) => d,
            None => {
                log::error!("buildin_rentitem2: Nonexistant item {nameid} requested.");
                return SCRIPT_CMD_FAILURE;
            }
        }
    };
    let seconds = getnum(rt, st, 3);
    let mut iden = getnum(rt, st, 4);
    let mut refine = getnum(rt, st, 5);
    let mut attr = getnum(rt, st, 6);
    fix_iden_ref_attr(id, &mut iden, &mut refine, &mut attr);
    let c1 = getnum(rt, st, 7) as u32;
    let c2 = getnum(rt, st, 8) as u32;
    let c3 = getnum(rt, st, 9) as u32;
    let c4 = getnum(rt, st, 10) as u32;
    let mut it = Item::new(id.nameid);
    it.identify = iden as u8;
    it.refine = refine as u8;
    it.attribute = attr as u8;
    it.card0 = c1;
    it.card1 = c2;
    it.card2 = c3;
    it.card3 = c4;
    it.expire_time = (c.tick() / 1000 + seconds as i64) as u32;
    it.bound = 0;
    let mut sdo = Some(aid);
    if last_char(&funcname) == '3' {
        if !getitem_randomoption(rt, st, &mut sdo, &mut it, &funcname, 11) {
            return SCRIPT_CMD_FAILURE;
        }
    } else if last_char(&funcname) == '4' {
        if !getitem_randomoption(rt, st, &mut sdo, &mut it, &funcname, 12) {
            return SCRIPT_CMD_FAILURE;
        }
        let grade = getnum(rt, st, 11);
        if !(ENCHANTGRADE_NONE..=MAX_ENCHANTGRADE).contains(&grade) {
            log::error!("buildin_rentitem2: Not a correct grade! Grade={grade}");
            return SCRIPT_CMD_FAILURE;
        }
        it.enchantgrade = grade as u8;
    }
    let flag = inv::pc_additem(c, aid, &mut it, 1, crate::map_log::LOG_TYPE_SCRIPT);
    if flag != 0 {
        ci::additem(c, aid, 0, 0, flag);
        return SCRIPT_CMD_FAILURE;
    }
    SCRIPT_CMD_SUCCESS
}

/// getnameditem <item id>,<character name / char id>: one item with a name inscribed (CARD0_CREATE).
fn getnameditem(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = rid2sd(rt, st, "buildin_getnameditem") else {
        // Player not attached!
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let nameid = if isstring(rt, st, 2) {
        let name = lat(&getstr(rt, st, 2));
        match itemdb::itemdb_searchname(&name) {
            Some(d) => d.nameid,
            None => {
                st.pushint(0);
                return SCRIPT_CMD_SUCCESS;
            }
        }
    } else {
        getnum(rt, st, 2) as u32
    };
    if itemdb::itemdb_exists(nameid).is_none() {
        // Even though named stackable items "could" be risky, they are required for certain quests.
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    }
    let tsd = if isstring(rt, st, 3) {
        let name = lat(&getstr(rt, st, 3));
        crate::pc::nick2sd(c, &name, false)
    } else {
        crate::pc::charid2sd(c, getnum(rt, st, 3) as u32)
    };
    let Some(tsd) = tsd else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let mut it = Item::new(nameid);
    it.amount = 1;
    it.identify = 1;
    it.card0 = CARD0_CREATE; // we don't use 255! because for example SIGNED WEAPON shouldn't get TOP10 BS Fame bonus [Lupus]
    it.card2 = tsd.char_id & 0xffff;
    it.card3 = tsd.char_id >> 16;
    if inv::pc_additem(c, aid, &mut it, 1, crate::map_log::LOG_TYPE_SCRIPT) != 0 {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS; // Failed to add item, we will not drop if they don't fit
    }
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}

/// grouprandomitem <group_num>{,<sub_group>}: a random item id of an item group.
fn grouprandomitem(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let mut sub_group: i32 = 1;
    if st.hasdata(3) {
        sub_group = getnum(rt, st, 3);
    }
    let group = getnum(rt, st, 2);
    match crate::itemgroup::get_random_entry(c, group as u16, sub_group as u8, itemdb::group::GROUP_ALGORITHM_USEDB) {
        Some(e) => {
            st.pushint(e.nameid as i64);
            SCRIPT_CMD_SUCCESS
        }
        None => {
            log::error!("buildin_grouprandomitem: Invalid item group with group_id '{group}', sub_group '{sub_group}'.");
            st.pushint(UNKNOWN_ITEM_ID);
            SCRIPT_CMD_FAILURE
        }
    }
}

/// The map of makeitem's "<map name>" ("this": the attached player's map).
fn makeitem_map(rt: &Rt, st: &mut State, mapname: &[u8]) -> Result<Option<u16>, ()> {
    if mapname == b"this" {
        let Some(aid) = rid2sd(rt, st, "buildin_makeitem") else { return Err(()) }; // Failed...
        Ok(rt.c.and_then(|c| crate::unit::get(c, aid)).map(|u| u.map))
    } else {
        Ok(crate::data::maps().id_of(&lat(mapname)))
    }
}

/// makeitem <item id>,<amount>,"<map name>",<X>,<Y>{,<canShowEffect>}.
fn makeitem(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let mut flag = 0;
    let nameid = if isstring(rt, st, 2) {
        let name = lat(&getstr(rt, st, 2));
        match itemdb::itemdb_searchname(&name) {
            Some(d) => d.nameid,
            None => {
                log::error!("buildin_makeitem: Unknown item {name}");
                return SCRIPT_CMD_FAILURE;
            }
        }
    } else {
        let val = getnum(rt, st, 2);
        let nameid = if val < 0 {
            flag = 1;
            (-(val as i64)) as u32
        } else {
            val as u32
        };
        if itemdb::itemdb_exists(nameid).is_none() {
            log::error!("buildin_makeitem: Unknown item id {nameid}");
            return SCRIPT_CMD_FAILURE;
        }
        nameid
    };
    let amount = getnum(rt, st, 3) as u16;
    let mapname = getstr(rt, st, 4);
    let x = getnum(rt, st, 5) as u16;
    let y = getnum(rt, st, 6) as u16;
    let can_show_effect = st.hasdata(7) && getnum(rt, st, 7) != 0;
    let m = match makeitem_map(rt, st, &mapname) {
        Ok(m) => m,
        Err(()) => return SCRIPT_CMD_SUCCESS,
    };
    let mut it = Item::new(nameid);
    it.identify = if flag == 0 { 1 } else { itemdb::itemdb_isidentified(nameid) as u8 };
    match m {
        Some(m) => {
            crate::flooritem::map_addflooritem(c, &it, amount as i32, m, x as i16, y as i16, 0, 0, 0, 4, 0, can_show_effect, None, 0);
        }
        None => log::error!("buildin_makeitem: unknown map '{}'", lat(&mapname)),
    }
    SCRIPT_CMD_SUCCESS
}

/// makeitem2 / makeitem3 / makeitem4.
fn makeitem2(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let funcname = st.funcname.clone();
    let nameid = if isstring(rt, st, 2) {
        let name = lat(&getstr(rt, st, 2));
        match itemdb::itemdb_searchname(&name) {
            Some(d) => d.nameid,
            None => {
                log::error!("buildin_{funcname}: Unknown item {name}");
                return SCRIPT_CMD_FAILURE;
            }
        }
    } else {
        let nameid = getnum(rt, st, 2) as u32;
        if itemdb::itemdb_exists(nameid).is_none() {
            log::error!("buildin_{funcname}: Unknown item id {nameid}");
            return SCRIPT_CMD_FAILURE;
        }
        nameid
    };
    let amount = getnum(rt, st, 3);
    let mapname = getstr(rt, st, 4);
    let x = getnum(rt, st, 5);
    let y = getnum(rt, st, 6);
    let m = match makeitem_map(rt, st, &mapname) {
        Ok(m) => m,
        Err(()) => return SCRIPT_CMD_SUCCESS,
    };
    let id = itemdb::itemdb_search(nameid);
    let mut it = Item::new(nameid);
    // char iden / ref / attr
    let mut iden = getnum(rt, st, 7) as i8;
    let mut refine = getnum(rt, st, 8) as i8;
    let mut attr = getnum(rt, st, 9) as i8;
    if id.r#type == itemdb::IT_WEAPON || id.r#type == itemdb::IT_ARMOR || id.r#type == itemdb::IT_SHADOWGEAR {
        if refine as i32 > MAX_REFINE {
            refine = MAX_REFINE as i8;
        }
    } else if id.r#type == itemdb::IT_PETEGG {
        iden = 1;
        refine = 0;
    } else {
        iden = 1;
        refine = 0;
        attr = 0;
    }
    it.identify = iden as u8;
    it.refine = refine as u8;
    it.attribute = attr as u8;
    it.card0 = getnum(rt, st, 10) as u32;
    it.card1 = getnum(rt, st, 11) as u32;
    it.card2 = getnum(rt, st, 12) as u32;
    it.card3 = getnum(rt, st, 13) as u32;
    let mut can_show_effect = false;
    if last_char(&funcname) == '3' || last_char(&funcname) == '4' {
        let mut offset = 14;
        if last_char(&funcname) == '4' {
            offset = 15;
            let grade = getnum(rt, st, 14);
            if !(ENCHANTGRADE_NONE..=MAX_ENCHANTGRADE).contains(&grade) {
                log::error!("buildin_{funcname}: Not a correct grade! Grade={grade}");
                return SCRIPT_CMD_FAILURE;
            }
            it.enchantgrade = grade as u8;
        }
        let mut sdo = None;
        if !getitem_randomoption(rt, st, &mut sdo, &mut it, &funcname, offset) {
            return SCRIPT_CMD_FAILURE;
        }
        if st.hasdata(offset + 3) && getnum(rt, st, offset + 3) != 0 {
            can_show_effect = getnum(rt, st, offset + 3) != 0;
        }
    } else if st.hasdata(14) && getnum(rt, st, 14) != 0 {
        can_show_effect = getnum(rt, st, 14) != 0;
    }
    match m {
        Some(m) => {
            crate::flooritem::map_addflooritem(c, &it, amount, m, x as i16, y as i16, 0, 0, 0, 4, 0, can_show_effect, None, 0);
        }
        None => log::error!("buildin_{funcname}: unknown map '{}'", lat(&mapname)),
    }
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- deleting items

fn table_of(command: &str) -> u8 {
    if command.starts_with("cart") {
        TABLE_CART
    } else if command.starts_with("storage") {
        TABLE_STORAGE
    } else if command.starts_with("guildstorage") {
        TABLE_GUILD_STORAGE
    } else {
        TABLE_INVENTORY
    }
}

fn table_size(loc: u8) -> usize {
    match loc {
        TABLE_CART => inv::MAX_CART,
        TABLE_STORAGE => crate::storage::MAX_STORAGE,
        TABLE_GUILD_STORAGE => crate::guild_storage::MAX_GUILD_STORAGE,
        _ => MAX_INVENTORY,
    }
}

fn table_item(c: &Ctx, aid: u32, loc: u8, i: usize) -> Option<Item> {
    match loc {
        TABLE_CART => crate::cart::cart_get(c, aid, i).map(|r| r.item),
        TABLE_STORAGE => crate::storage::storage_get(c, aid, i).map(|r| r.item),
        TABLE_GUILD_STORAGE => crate::guild_storage::slot_item(c, crate::guild::guild_id_of(c, aid), i),
        _ => inv::inv_get(c, aid, i).map(|r| r.item),
    }
}

/// storage.cpp guild2storage2(sd->status.guild_id) != nullptr: the map server has the guild's storage.
fn guild_storage_loaded(c: &Ctx, aid: u32) -> bool {
    crate::guild_storage::guild2storage2(c, crate::guild::guild_id_of(c, aid)).is_some()
}

/// script.cpp buildin_delitem_delete.
fn delitem_delete(c: &Ctx, aid: u32, idx: usize, amount: &mut i32, loc: u8, delete_items: bool) {
    let Some(itm) = table_item(c, aid, loc, idx) else { return };
    let delamount = (*amount).min(itm.amount as i32);
    if delete_items {
        if itemdb::itemdb_type(itm.nameid) == itemdb::IT_PETEGG && itm.card0 == CARD0_PET {
            // delete associated pet
            crate::pet::intif_delete_petdata(crate::pet::egg_pet_id(&itm));
        }
        match loc {
            TABLE_CART => crate::cart::pc_cart_delitem(c, aid, idx as i32, delamount, 0, crate::map_log::LOG_TYPE_SCRIPT),
            TABLE_STORAGE => {
                crate::storage::storage_delitem(c, aid, idx, delamount);
                // log_pick_pc(sd, LOG_TYPE_SCRIPT, -delamount, itm): `itm` points at the storage slot, which
                // storage_delitem has just changed (zeroed when the stack is gone: nameid 0 is never logged)
                let itm = table_item(c, aid, loc, idx).unwrap_or_default();
                crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_SCRIPT, -delamount, &itm);
            }
            TABLE_GUILD_STORAGE => {
                // gstor->lock = true; storage_guild_delitem; log_pick_pc(sd, LOG_TYPE_SCRIPT, -delamount, itm);
                // storage_guild_storageclose(sd); gstor->lock = false (the log reads the slot after the deletion,
                // as for TABLE_STORAGE; closing the storage does not change it)
                crate::guild_storage::script_delitem(c, aid, idx, delamount);
                let itm = table_item(c, aid, loc, idx).unwrap_or_default();
                crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_SCRIPT, -delamount, &itm);
            }
            _ => {
                inv::pc_delitem(c, aid, idx as i32, delamount, 0, 0, crate::map_log::LOG_TYPE_SCRIPT);
            }
        }
    }
    *amount -= delamount;
}

fn same_options(a: &Item, b: &Item) -> bool {
    (0..MAX_ITEM_RDM_OPT).all(|j| a.opt(j) == b.opt(j))
}

/// script.cpp buildin_delitem_search. exact_match: 0x1 identify / attribute / grade / cards, 0x2 random options.
fn delitem_search(c: &Ctx, aid: u32, it: &mut Item, exact_match: u8, loc: u8) -> bool {
    let mut delete_items = false;
    // prefer always non-equipped items
    it.equip = 0;
    // when searching for nameid only, prefer additionally
    if exact_match == 0 {
        // non-refined items
        it.refine = 0;
        // card-less items
        for i in 0..MAX_SLOTS {
            it.set_card(i, 0);
        }
    }
    let size = table_size(loc);
    loop {
        let mut important: u16 = 0;
        let mut amount = it.amount as i32;
        // 1st pass -- less important items / exact match
        let mut i = 0;
        while amount != 0 && i < size {
            let idx = i;
            i += 1;
            let Some(itm) = table_item(c, aid, loc, idx) else { continue };
            if itm.nameid == 0 || itm.nameid != it.nameid {
                continue; // wrong/invalid item
            }
            if itm.equip != it.equip || itm.refine != it.refine {
                important = important.wrapping_add(1); // not matching attributes
                continue;
            }
            if exact_match != 0 {
                if exact_match & 0x1 != 0 && (itm.identify != it.identify || itm.attribute != it.attribute || itm.enchantgrade != it.enchantgrade || itm.cards() != it.cards()) {
                    continue; // not matching exact attributes
                }
                if exact_match & 0x2 != 0 && !same_options(&itm, it) {
                    continue;
                }
            } else if itemdb::itemdb_type(itm.nameid) == itemdb::IT_PETEGG {
                if itm.card0 == CARD0_PET && CHAR_SERVER_DOWN {
                    continue; // pet which cannot be deleted
                }
            } else if itm.cards() != it.cards() {
                important = important.wrapping_add(1); // named/carded item
                continue;
            }
            // count / delete item
            delitem_delete(c, aid, idx, &mut amount, loc, delete_items);
        }
        // 2nd pass -- any matching item
        if amount != 0 && important != 0 {
            let mut i = 0;
            while amount != 0 && i < size {
                let idx = i;
                i += 1;
                let Some(itm) = table_item(c, aid, loc, idx) else { continue };
                if itm.nameid == 0 || itm.nameid != it.nameid {
                    continue;
                }
                if itemdb::itemdb_type(itm.nameid) == itemdb::IT_PETEGG && itm.card0 == CARD0_PET && CHAR_SERVER_DOWN {
                    continue; // pet which cannot be deleted
                }
                if exact_match != 0 {
                    if exact_match & 0x1 != 0 && (itm.refine != it.refine || itm.identify != it.identify || itm.attribute != it.attribute || itm.enchantgrade != it.enchantgrade || itm.cards() != it.cards()) {
                        continue; // not matching attributes
                    }
                    if exact_match & 0x2 != 0 && !same_options(&itm, it) {
                        continue;
                    }
                }
                // count / delete item
                delitem_delete(c, aid, idx, &mut amount, loc, delete_items);
            }
        }
        if amount != 0 {
            return false; // not enough items
        } else if delete_items {
            return true; // we are done with the work
        } else {
            delete_items = true; // get rid of the items now
        }
    }
}

/// The end of a failed delitem: the script ends and its dialog closes.
fn delitem_fail(rt: &Rt, st: &mut State, aid: u32, command: &str, it: &Item) -> i32 {
    log::error!("buildin_{command}: failed to delete {} items (AID={aid} item_id={}).", it.amount, it.nameid);
    st.state = END;
    st.mes_active = false;
    if let Some(c) = rt.c {
        if let Some(sd) = crate::unit::get(c, aid) {
            crate::clif::scriptclose(c, &sd, st.oid);
        }
    }
    SCRIPT_CMD_FAILURE
}

/// delitem / cartdelitem / storagedelitem / guildstoragedelitem <item id>,<amount>{,<account id>}.
fn delitem(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let command = st.funcname.clone();
    let loc = table_of(&command);
    let Some(aid) = accid2sd(rt, st, 4, "buildin_delitem") else {
        // In any case cancel script execution
        st.state = END;
        return SCRIPT_CMD_SUCCESS;
    };
    if loc == TABLE_CART && !inv::pc_iscarton(c, aid) {
        let cid = crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
        log::error!("buildin_cartdelitem: player doesn't have cart (CID={cid}).");
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    }
    if loc == TABLE_GUILD_STORAGE && (!guild_storage_loaded(c, aid) || crate::pc::get(c, aid).map(|p| p.storage_flag != 0).unwrap_or(true)) {
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    }
    let mut it = Item::new(0);
    if isstring(rt, st, 2) {
        let name = lat(&getstr(rt, st, 2));
        match itemdb::itemdb_searchname(&name) {
            Some(d) => it.nameid = d.nameid, // "<item name>"
            None => {
                log::error!("buildin_{command}: unknown item \"{name}\".");
                st.state = END;
                return SCRIPT_CMD_FAILURE;
            }
        }
    } else {
        it.nameid = getnum(rt, st, 2) as u32; // <item id>
        if itemdb::itemdb_exists(it.nameid).is_none() {
            log::error!("buildin_{command}: unknown item \"{}\".", it.nameid);
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
    }
    it.amount = getnum(rt, st, 3) as i16;
    if it.amount <= 0 {
        return SCRIPT_CMD_SUCCESS; // nothing to do
    }
    if delitem_search(c, aid, &mut it, 0, loc) {
        return SCRIPT_CMD_SUCCESS; // success
    }
    delitem_fail(rt, st, aid, &command, &it)
}

/// delitem2 / delitem3 / delitem4 and the cart / storage / guild storage delitem2.
fn delitem2(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let command = st.funcname.clone();
    let loc = table_of(&command);
    let aid_pos = match last_char(&command) {
        '3' => 14,
        '4' => 15,
        _ => 11,
    };
    let Some(aid) = accid2sd(rt, st, aid_pos, "buildin_delitem2") else {
        // In any case cancel script execution
        st.state = END;
        return SCRIPT_CMD_SUCCESS;
    };
    if loc == TABLE_CART && !inv::pc_iscarton(c, aid) {
        let cid = crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
        log::error!("buildin_cartdelitem: player doesn't have cart (CID={cid}).");
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    }
    if loc == TABLE_GUILD_STORAGE && (!guild_storage_loaded(c, aid) || crate::pc::get(c, aid).map(|p| p.storage_flag != 0).unwrap_or(true)) {
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    }
    let mut it = Item::new(0);
    if isstring(rt, st, 2) {
        let name = lat(&getstr(rt, st, 2));
        match itemdb::itemdb_searchname(&name) {
            Some(d) => it.nameid = d.nameid,
            None => {
                log::error!("buildin_{command}: unknown item \"{name}\".");
                st.state = END;
                return SCRIPT_CMD_FAILURE;
            }
        }
    } else {
        it.nameid = getnum(rt, st, 2) as u32;
        if itemdb::itemdb_exists(it.nameid).is_none() {
            log::error!("buildin_{command}: unknown item \"{}\".", it.nameid);
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
    }
    it.amount = getnum(rt, st, 3) as i16;
    it.identify = getnum(rt, st, 4) as u8;
    it.refine = getnum(rt, st, 5) as u8;
    it.attribute = getnum(rt, st, 6) as u8;
    it.card0 = getnum(rt, st, 7) as u32;
    it.card1 = getnum(rt, st, 8) as u32;
    it.card2 = getnum(rt, st, 9) as u32;
    it.card3 = getnum(rt, st, 10) as u32;
    let mut flag: u8 = 0x1;
    if last_char(&command) == '3' || last_char(&command) == '4' {
        let mut offset = 11;
        if last_char(&command) == '4' {
            offset = 12;
            let grade = getnum(rt, st, 11);
            if !(ENCHANTGRADE_NONE..=MAX_ENCHANTGRADE).contains(&grade) {
                log::error!("buildin_{command}: Not a correct grade! Grade={grade}");
                return SCRIPT_CMD_FAILURE;
            }
            it.enchantgrade = grade as u8;
        }
        let mut sdo = Some(aid);
        if !getitem_randomoption(rt, st, &mut sdo, &mut it, &command, offset) {
            return SCRIPT_CMD_FAILURE;
        }
        flag |= 0x2;
    }
    if it.amount <= 0 {
        return SCRIPT_CMD_SUCCESS; // nothing to do
    }
    if delitem_search(c, aid, &mut it, flag, loc) {
        return SCRIPT_CMD_SUCCESS; // success
    }
    delitem_fail(rt, st, aid, &command, &it)
}

/// delitemidx <index>{,<amount>{,<char id>}}.
fn delitemidx(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 4, "buildin_delitemidx") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let idx = getnum(rt, st, 2);
    if idx < 0 || idx as usize >= MAX_INVENTORY {
        log::warn!("buildin_delitemidx: Index {idx} is out of the range 0-{}.", MAX_INVENTORY - 1);
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    }
    let Some(itm) = inv_item(c, aid, idx) else {
        let (name, cid) = crate::pc::character_of(c, aid).map(|ch| (ch.name, ch.id)).unwrap_or_default();
        log::warn!("buildin_delitemidx: No item can be deleted from index {idx} of player {name} (AID: {aid}, CID: {cid}).");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let amount = if st.hasdata(3) { getnum(rt, st, 3) } else { itm.amount as i32 };
    if amount > 0 {
        let ok = inv::pc_delitem(c, aid, idx, amount, 0, 0, crate::map_log::LOG_TYPE_SCRIPT) == 0;
        st.pushint(ok as i64);
    } else {
        st.pushint(0);
    }
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- counting items

/// script.cpp script_countitem_sub over `items` (the occupied slots). `expanded`: 1 the attributes / cards, 2 the
/// random options, 4 the enchant grade.
fn countitem_sub(rt: &Rt, st: &mut State, items: &[Item], id: &ItemData, expanded: i32, sd: Option<u32>, rental: bool) -> i32 {
    let mut count: i32 = 0;
    if expanded == 0 {
        let nameid = id.nameid;
        for itm in items {
            if itm.nameid == 0 || itm.amount < 1 {
                continue;
            }
            if itm.nameid == nameid && ((rental && itm.expire_time > 0) || (!rental && itm.expire_time == 0)) {
                count += itm.amount as i32;
            }
        }
        return count;
    }
    let mut it = Item::new(id.nameid);
    let mut offset = 10;
    it.identify = getnum(rt, st, 3) as u8;
    it.refine = getnum(rt, st, 4) as u8;
    it.attribute = getnum(rt, st, 5) as u8;
    it.card0 = getnum(rt, st, 6) as u32;
    it.card1 = getnum(rt, st, 7) as u32;
    it.card2 = getnum(rt, st, 8) as u32;
    it.card3 = getnum(rt, st, 9) as u32;
    if expanded & 4 != 0 {
        it.enchantgrade = getnum(rt, st, 10) as u8;
        offset = 11;
    }
    if expanded & 2 != 0 {
        if sd.is_none() {
            log::error!("buildin_countitem3: Player not attached.");
            return -1;
        }
        let mut sdo = sd;
        if !getitem_randomoption(rt, st, &mut sdo, &mut it, "countitem3", offset) {
            return -1;
        }
    }
    for itm in items {
        if itm.nameid == 0 || itm.amount < 1 {
            continue;
        }
        if itm.nameid != it.nameid || itm.identify != it.identify || itm.refine != it.refine || itm.attribute != it.attribute || itm.enchantgrade != it.enchantgrade {
            continue;
        }
        if (!rental && itm.expire_time > 0) || (rental && itm.expire_time == 0) {
            continue;
        }
        if itm.cards() != it.cards() {
            continue;
        }
        if expanded & 2 != 0 && !same_options(itm, &it) {
            continue;
        }
        count += itm.amount as i32;
    }
    count
}

/// (expanded, account id argument) of the countitem family by the command's last character.
fn count_variant(command: &str, allow34: bool) -> (i32, i32) {
    match last_char(command) {
        '2' => (1, 10),
        '3' if allow34 => (3, 13),
        '4' if allow34 => (7, 14),
        _ => (0, 3),
    }
}

fn invalid_item(rt: &Rt, st: &mut State, command: &str) -> i32 {
    // returns string, regardless of what it was
    let s = lat(&getstr(rt, st, 2));
    log::error!("buildin_{command}: Invalid item '{s}'.");
    st.pushint(0);
    SCRIPT_CMD_FAILURE
}

fn push_count(st: &mut State, count: i32) -> i32 {
    if count < 0 {
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    st.pushint(count as i64);
    SCRIPT_CMD_SUCCESS
}

fn inventory_items(c: &Ctx, aid: u32) -> Vec<Item> {
    inv::inv_list(c, aid).into_iter().map(|r| r.item).collect()
}

/// countitem / countitem2 / countitem3 / countitem4.
fn countitem(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let command = st.funcname.clone();
    let (expanded, aid_pos) = count_variant(&command, true);
    let Some(aid) = accid2sd(rt, st, aid_pos, "buildin_countitem") else { return SCRIPT_CMD_FAILURE };
    let Some(id) = item_arg(rt, st, 2) else { return invalid_item(rt, st, &command) };
    let items = inventory_items(c, aid);
    let count = countitem_sub(rt, st, &items, id, expanded, Some(aid), false);
    push_count(st, count)
}

/// cartcountitem / cartcountitem2.
fn cartcountitem(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let command = st.funcname.clone();
    let (expanded, aid_pos) = count_variant(&command, false);
    let Some(aid) = accid2sd(rt, st, aid_pos, "buildin_cartcountitem") else { return SCRIPT_CMD_FAILURE };
    if !inv::pc_iscarton(c, aid) {
        let cid = crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
        log::error!("buildin_{command}: Player doesn't have cart (CID:{cid}).");
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    }
    let Some(id) = item_arg(rt, st, 2) else { return invalid_item(rt, st, &command) };
    let items: Vec<Item> = crate::cart::cart_list(c, aid).into_iter().map(|r| r.item).collect();
    let count = countitem_sub(rt, st, &items, id, expanded, None, false);
    push_count(st, count)
}

/// storagecountitem / storagecountitem2.
fn storagecountitem(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let command = st.funcname.clone();
    let (expanded, aid_pos) = count_variant(&command, false);
    let Some(aid) = accid2sd(rt, st, aid_pos, "buildin_storagecountitem") else { return SCRIPT_CMD_FAILURE };
    let Some(id) = item_arg(rt, st, 2) else { return invalid_item(rt, st, &command) };
    if crate::pc::get(c, aid).map(|p| p.storage_flag != 0).unwrap_or(false) {
        st.pushint(-1);
        return SCRIPT_CMD_SUCCESS;
    }
    let items: Vec<Item> = crate::storage::storage_list(c, aid).into_iter().map(|r| r.item).collect();
    let count = countitem_sub(rt, st, &items, id, expanded, None, false);
    push_count(st, count)
}

/// guildstoragecountitem / guildstoragecountitem2: counts in the guild storage (guild2storage, which creates an empty
/// one for a loaded guild), -1 while the player has a storage open; the storage is locked for the count and then
/// closed for the player (storage_guild_storageclose: ZC_CLOSE_STORE even though it was not open).
fn guildstoragecountitem(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let command = st.funcname.clone();
    let (expanded, aid_pos) = count_variant(&command, false);
    let Some(aid) = accid2sd(rt, st, aid_pos, "buildin_guildstoragecountitem") else { return SCRIPT_CMD_FAILURE };
    let Some(id) = item_arg(rt, st, 2) else { return invalid_item(rt, st, &command) };
    let gid = crate::guild::guild_id_of(c, aid);
    let flag = crate::pc::get(c, aid).map(|p| p.storage_flag).unwrap_or(0);
    if crate::guild_storage::guild2storage(c, gid).is_none() || flag != 0 {
        st.pushint(-1);
        return SCRIPT_CMD_SUCCESS;
    }
    crate::guild_storage::set_lock(c, gid, true);
    let items = crate::guild_storage::slots_array_of(c, gid);
    let count = countitem_sub(rt, st, &items, id, expanded, None, false);
    crate::guild_storage::storage_guild_storageclose(c, aid);
    crate::guild_storage::set_lock(c, gid, false);
    push_count(st, count)
}

/// rentalcountitem / rentalcountitem2 / rentalcountitem3 / rentalcountitem4.
fn rentalcountitem(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let command = st.funcname.clone();
    let (expanded, aid_pos) = count_variant(&command, true);
    let Some(aid) = accid2sd(rt, st, aid_pos, "buildin_rentalcountitem") else { return SCRIPT_CMD_FAILURE };
    let Some(id) = item_arg(rt, st, 2) else { return invalid_item(rt, st, &command) };
    let items = inventory_items(c, aid);
    let count = countitem_sub(rt, st, &items, id, expanded, Some(aid), true);
    push_count(st, count)
}

/// countbound {<type>{,<char_id>}}: @bound_items / @bound_amount, returns how many.
fn countbound(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 3, "buildin_countbound") else { return SCRIPT_CMD_FAILURE };
    let kind = getnum(rt, st, 2);
    let mut k = 0u32;
    for r in inv::inv_list(c, aid) {
        let it = &r.item;
        if it.nameid > 0 && ((kind == 0 && it.bound != 0) || (kind != 0 && it.bound as i32 == kind)) {
            setreg(rt, st, aid, "@bound_items", k, it.nameid as i64);
            setreg(rt, st, aid, "@bound_amount", k, it.amount as i64);
            k += 1;
        }
    }
    st.pushint(k as i64);
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- weight

/// checkweight(<item id>,<amount>{,<item id>,<amount>…}): 1 if everything fits (weight, stacks, slots).
fn checkweight(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = rid2sd(rt, st, "buildin_checkweight") else { return SCRIPT_CMD_SUCCESS };
    let nbargs = st.lastdata() + 1;
    if nbargs % 2 != 0 {
        log::error!("buildin_checkweight: Invalid nb of args should be a multiple of 2.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    }
    let slots = inv::pc_inventoryblank(c, aid) as i32; // nb of empty slot
    let (sweight, smax) = inv::st(c, aid).map(|s| (s.weight, s.max_weight)).unwrap_or((0, 0));
    let mut weight: u32 = 0;
    let mut amount2: u16 = 0;
    let mut i = 2;
    while i < nbargs {
        let Some(id) = item_arg(rt, st, i) else {
            let s = lat(&getstr(rt, st, i));
            log::error!("buildin_checkweight: Invalid item '{s}'."); // returns string, regardless of what it was
            st.pushint(0);
            return SCRIPT_CMD_FAILURE;
        };
        let nameid = id.nameid;
        let amount = getnum(rt, st, i + 1) as u16;
        if amount < 1 {
            log::error!("buildin_checkweight: Invalid amount '{amount}'.");
            st.pushint(0);
            return SCRIPT_CMD_FAILURE;
        }
        weight = weight.wrapping_add(itemdb::itemdb_weight(nameid).wrapping_mul(amount as u32)); // total weight for all chk
        if weight.wrapping_add(sweight) > smax {
            // too heavy
            st.pushint(0);
            return SCRIPT_CMD_SUCCESS;
        }
        match inv::pc_checkadditem(c, aid, nameid, amount as i32) {
            inv::CHKADDITEM_EXIST => {} // item is already in inventory, but there is still space for the requested amount
            inv::CHKADDITEM_NEW => {
                if itemdb::itemdb_isstackable(nameid) {
                    // stackable
                    amount2 = amount2.wrapping_add(1);
                } else {
                    // non-stackable
                    amount2 = amount2.wrapping_add(amount);
                }
                if slots < amount2 as i32 {
                    st.pushint(0);
                    return SCRIPT_CMD_SUCCESS;
                }
            }
            inv::CHKADDITEM_OVERAMOUNT => {
                st.pushint(0);
                return SCRIPT_CMD_SUCCESS;
            }
            _ => {}
        }
        i += 2;
    }
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}

/// checkweight2(<item id array>,<amount array>).
fn checkweight2(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = rid2sd(rt, st, "buildin_checkweight2") else { return SCRIPT_CMD_SUCCESS };
    let data_it = st.data(2).clone();
    let data_nb = st.data(3).clone();
    let (Data::Name(id_it, idx_it, ref_it), Data::Name(id_nb, idx_nb, ref_nb)) = (data_it, data_nb) else {
        log::error!("buildin_checkweight2: parameter not a variable");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE; // not a variable
    };
    let name_it = name_of(id_it);
    let name_nb = name_of(id_nb);
    if is_string_variable(&name_it) || is_string_variable(&name_nb) {
        log::error!("buildin_checkweight2: illegal type, need int");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE; // not supported
    }
    let nb_it = reg::highest_key(rt, st, Some(aid), &name_it, &ref_it);
    let nb_nb = reg::highest_key(rt, st, Some(aid), &name_nb, &ref_nb);
    let mut fail = false;
    if nb_it != nb_nb {
        log::error!("buildin_checkweight2: Size mistmatch: nb_it={nb_it}, nb_nb={nb_nb}");
        fail = true;
    }
    let slots = inv::pc_inventoryblank(c, aid) as i32;
    let (sweight, smax) = inv::st(c, aid).map(|s| (s.weight, s.max_weight)).unwrap_or((0, 0));
    let mut weight: i32 = 0;
    let mut amount2: u16 = 0;
    for i in 0..nb_it {
        let nameid = get_val2_num(rt, st, id_it, idx_it + i, &ref_it) as u32;
        let amount = get_val2_num(rt, st, id_nb, idx_nb + i, &ref_nb) as u16;
        if fail {
            continue; // cpntonie to depop rest
        }
        if itemdb::itemdb_exists(nameid).is_none() {
            log::error!("buildin_checkweight2: Invalid item '{nameid}'.");
            fail = true;
            continue;
        }
        if amount == 0 {
            log::error!("buildin_checkweight2: Invalid amount '{amount}'.");
            fail = true;
            continue;
        }
        weight = weight.wrapping_add(itemdb::itemdb_weight(nameid).wrapping_mul(amount as u32) as i32);
        if (weight as u32).wrapping_add(sweight) > smax {
            fail = true;
            continue;
        }
        match inv::pc_checkadditem(c, aid, nameid, amount as i32) {
            inv::CHKADDITEM_EXIST => {} // item is already in inventory, but there is still space for the requested amount
            inv::CHKADDITEM_NEW => {
                if itemdb::itemdb_isstackable(nameid) {
                    amount2 = amount2.wrapping_add(1);
                } else {
                    amount2 = amount2.wrapping_add(amount);
                }
                if slots < amount2 as i32 {
                    fail = true;
                }
            }
            inv::CHKADDITEM_OVERAMOUNT => fail = true,
            _ => {}
        }
    }
    st.pushint(if fail { 0 } else { 1 });
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- inventory list

/// getinventorylist {<char_id>}: the @inventorylist_* arrays and @inventorylist_count.
fn getinventorylist(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 2, "buildin_getinventorylist") else { return SCRIPT_CMD_FAILURE };
    let mut j = 0u32;
    for r in inv::inv_list(c, aid) {
        let it = &r.item;
        if it.nameid == 0 || it.amount <= 0 {
            continue;
        }
        setreg(rt, st, aid, "@inventorylist_id", j, it.nameid as i64);
        setreg(rt, st, aid, "@inventorylist_idx", j, r.idx as i64);
        setreg(rt, st, aid, "@inventorylist_amount", j, it.amount as i64);
        setreg(rt, st, aid, "@inventorylist_equip", j, it.equip as i64);
        setreg(rt, st, aid, "@inventorylist_refine", j, it.refine as i64);
        setreg(rt, st, aid, "@inventorylist_identify", j, it.identify as i64);
        setreg(rt, st, aid, "@inventorylist_attribute", j, it.attribute as i64);
        for k in 0..MAX_SLOTS {
            setreg(rt, st, aid, &format!("@inventorylist_card{}", k + 1), j, it.card(k) as i64);
        }
        setreg(rt, st, aid, "@inventorylist_expire", j, it.expire_time as i64);
        setreg(rt, st, aid, "@inventorylist_bound", j, it.bound as i64);
        setreg(rt, st, aid, "@inventorylist_enchantgrade", j, it.enchantgrade as i64);
        for k in 0..MAX_ITEM_RDM_OPT {
            let o = it.opt(k);
            setreg(rt, st, aid, &format!("@inventorylist_option_id{}", k + 1), j, o.id as i64);
            setreg(rt, st, aid, &format!("@inventorylist_option_value{}", k + 1), j, o.value as i64);
            setreg(rt, st, aid, &format!("@inventorylist_option_parameter{}", k + 1), j, o.param as i8 as i64);
        }
        let tradable = inv::pc_can_trade_item(c, aid, it);
        setreg(rt, st, aid, "@inventorylist_tradable", j, tradable as i64);
        setreg(rt, st, aid, "@inventorylist_favorite", j, it.favorite as i64);
        j += 1;
    }
    setreg(rt, st, aid, "@inventorylist_count", 0, j as i64);
    SCRIPT_CMD_SUCCESS
}

/// clearitem {<char_id>}: deletes the whole inventory.
fn clearitem(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 2, "buildin_clearitem") else { return SCRIPT_CMD_FAILURE };
    for i in 0..MAX_INVENTORY {
        if let Some(it) = inv_item(c, aid, i as i32) {
            if it.amount != 0 {
                inv::pc_delitem(c, aid, i as i32, it.amount as i32, 0, 0, crate::map_log::LOG_TYPE_SCRIPT);
            }
        }
    }
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- equipment

/// getequipid({<equipment slot>,<char_id>}).
fn getequipid(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 3, "buildin_getequipid") else {
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    let num = if st.hasdata(2) { getnum(rt, st, 2) } else { EQI_COMPOUND_ON };
    let i = if num == EQI_COMPOUND_ON {
        current_equip_item_index(c)
    } else if equip_index_check(num) {
        // get inventory position of item
        checkequip(c, aid, num)
    } else {
        log::error!("buildin_getequipid: Unknown equip index '{num}'");
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    match inv_item(c, aid, i as i32) {
        Some(it) => st.pushint(it.nameid as i64),
        None => st.pushint(-1),
    }
    SCRIPT_CMD_SUCCESS
}

/// getequipuniqueid(<equipment slot>{,<char_id>}).
fn getequipuniqueid(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 3, "buildin_getequipuniqueid") else {
        st.pushstr(Vec::new());
        return SCRIPT_CMD_FAILURE;
    };
    let num = getnum(rt, st, 2);
    if !equip_index_check(num) {
        st.pushstr(Vec::new());
        return SCRIPT_CMD_FAILURE;
    }
    let i = checkequip(c, aid, num);
    if i < 0 {
        st.pushstr(Vec::new());
        return SCRIPT_CMD_FAILURE;
    }
    let uid = inv_item(c, aid, i as i32).map(|it| it.unique_id).unwrap_or(0);
    st.pushstr(uid.to_string().into_bytes());
    SCRIPT_CMD_SUCCESS
}

/// getequipname(<equipment slot>{,<char_id>}): the item's (English) name.
fn getequipname(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 3, "buildin_getequipname") else {
        st.pushstr(Vec::new());
        return SCRIPT_CMD_FAILURE;
    };
    let num = getnum(rt, st, 2);
    if !equip_index_check(num) {
        st.pushstr(Vec::new());
        return SCRIPT_CMD_SUCCESS;
    }
    let i = checkequip(c, aid, num);
    match inv_item(c, aid, i as i32) {
        Some(it) if i >= 0 => st.pushstr(crate::net::wire::latin1(&inv::data_of(&it).ename)),
        _ => st.pushstr(Vec::new()),
    }
    SCRIPT_CMD_SUCCESS
}

fn is_broken(it: &Item) -> bool {
    it.attribute == 1 && !itemdb::itemdb_ishatched_egg(it.card0, it.attribute)
}

/// getbrokenid(<number>{,<char_id>}): the id of the n-th broken item.
fn getbrokenid(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 3, "buildin_getbrokenid") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let num = getnum(rt, st, 2);
    let mut id = 0u32;
    let mut brokencounter = 0;
    for r in inv::inv_list(c, aid) {
        if is_broken(&r.item) {
            brokencounter += 1;
            if num == brokencounter {
                id = r.item.nameid;
                break;
            }
        }
    }
    st.pushint(id as i64);
    SCRIPT_CMD_SUCCESS
}

/// repair <num>{,<char_id>}: repairs the n-th broken item.
fn repair(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 3, "buildin_repair") else { return SCRIPT_CMD_FAILURE };
    let num = getnum(rt, st, 2);
    let mut repaircounter = 0;
    for mut r in inv::inv_list(c, aid) {
        if is_broken(&r.item) {
            repaircounter += 1;
            if num == repaircounter {
                r.item.attribute = 0;
                inv::inv_put(c, &r);
                ci::equiplist(c, aid);
                ci::produceeffect(c, aid, 0, r.item.nameid);
                if let Some(sd) = crate::unit::get(c, aid) {
                    crate::status::clif::misceffect(c, &sd, ci::NOTIFYEFFECT_REFINE_SUCCESS as i32);
                }
                break;
            }
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// repairall {<char_id>}.
fn repairall(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 2, "buildin_repairall") else { return SCRIPT_CMD_FAILURE };
    let mut repaircounter = 0;
    for mut r in inv::inv_list(c, aid) {
        if r.item.nameid != 0 && is_broken(&r.item) {
            r.item.attribute = 0;
            inv::inv_put(c, &r);
            ci::produceeffect(c, aid, 0, r.item.nameid);
            repaircounter += 1;
        }
    }
    if repaircounter != 0 {
        if let Some(sd) = crate::unit::get(c, aid) {
            crate::status::clif::misceffect(c, &sd, ci::NOTIFYEFFECT_REFINE_SUCCESS as i32);
        }
        ci::equiplist(c, aid);
    }
    SCRIPT_CMD_SUCCESS
}

/// getequipisequiped <pos>{,<char_id>}.
fn getequipisequiped(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let num = getnum(rt, st, 2);
    let Some(aid) = charid2sd(rt, st, 3, "buildin_getequipisequiped") else { return SCRIPT_CMD_FAILURE };
    let i = checkequip(c, aid, num);
    st.pushint((i >= 0) as i64);
    SCRIPT_CMD_SUCCESS
}

/// getequipisenableref(<equipment slot>{,<char_id>}): refinable (not NoRefine, not a rental).
fn getequipisenableref(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let num = getnum(rt, st, 2);
    let Some(aid) = charid2sd(rt, st, 3, "buildin_getequipisenableref") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let i = checkequip(c, aid, num);
    let ok = i >= 0 && inv_item(c, aid, i as i32).map(|it| !inv::data_of(&it).flag.no_refine && it.expire_time == 0).unwrap_or(false);
    st.pushint(ok as i64);
    SCRIPT_CMD_SUCCESS
}

/// getequiprefinerycnt(<equipment slot>{,<char_id>}).
fn getequiprefinerycnt(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let num = getnum(rt, st, 2);
    let Some(aid) = charid2sd(rt, st, 3, "buildin_getequiprefinerycnt") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let i = checkequip(c, aid, num);
    let r = if i >= 0 { inv_item(c, aid, i as i32).map(|it| it.refine as i64).unwrap_or(0) } else { 0 };
    st.pushint(r);
    SCRIPT_CMD_SUCCESS
}

/// getequipweaponlv / getequiparmorlv: the slot of `num` (EQI_COMPOUND_ON: the item whose script runs).
/// Ok(index) or Err(()) when the command fails (0 pushed).
fn level_slot(rt: &Rt, st: &mut State, func: &str, strict: bool) -> Result<(u32, i16), ()> {
    let num = if st.hasdata(2) { getnum(rt, st, 2) } else { EQI_COMPOUND_ON };
    let Some(aid) = charid2sd(rt, st, 3, func) else {
        st.pushint(0);
        return Err(());
    };
    let Some(c) = rt.c else { return Err(()) };
    let i = if num == EQI_COMPOUND_ON {
        let cur = current_equip_item_index(c);
        if cur == -1 {
            st.pushint(0);
            return Err(());
        }
        cur
    } else if equip_index_check(num) {
        checkequip(c, aid, num)
    } else if strict {
        log::error!("buildin_getequiparmorlv: unsupported equip index {num}.");
        st.pushint(0);
        return Err(());
    } else {
        -1
    };
    Ok((aid, i))
}

/// getequipweaponlv({<equipment slot>{,<char_id>}}).
fn getequipweaponlv(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Ok((aid, i)) = level_slot(rt, st, "buildin_getequipweaponlv", false) else { return SCRIPT_CMD_FAILURE };
    match inv_item(c, aid, i as i32).map(|it| inv::data_of(&it)) {
        Some(d) if d.r#type == itemdb::IT_WEAPON => st.pushint(d.weapon_level as i64),
        _ => st.pushint(0),
    }
    SCRIPT_CMD_SUCCESS
}

/// getequiparmorlv({<equipment slot>{,<char_id>}}).
fn getequiparmorlv(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Ok((aid, i)) = level_slot(rt, st, "buildin_getequiparmorlv", true) else { return SCRIPT_CMD_FAILURE };
    match inv_item(c, aid, i as i32).map(|it| inv::data_of(&it)) {
        Some(d) if d.r#type == itemdb::IT_ARMOR => st.pushint(d.armor_level as i64),
        _ => st.pushint(0),
    }
    SCRIPT_CMD_SUCCESS
}

/// getequippercentrefinery(<equipment slot>{,<enriched>,<char_id>}): the refine chance (refine db).
fn getequippercentrefinery(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let num = getnum(rt, st, 2);
    let enriched = st.hasdata(3) && getnum(rt, st, 3) != 0;
    let Some(aid) = charid2sd(rt, st, 4, "buildin_getequippercentrefinery") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let i = checkequip(c, aid, num);
    let it = if i >= 0 { inv_item(c, aid, i as i32).filter(|it| it.nameid != 0) } else { None };
    let Some(it) = it else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let Some(info) = itemdb::refine::find_level_info(inv::data_of(&it), it.refine) else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let kind = if enriched { itemdb::refine::REFINE_COST_ENRICHED } else { itemdb::refine::REFINE_COST_NORMAL };
    match info.cost(kind) {
        Some(cost) => st.pushint((cost.chance / 100) as i64),
        None => st.pushint(0),
    }
    SCRIPT_CMD_SUCCESS
}

/// `char refine` arithmetic: refine + delta in a C char, then cap_value(…, 0, MAX_REFINE).
fn refine_add(refine: u8, delta: i32) -> u8 {
    let r = (refine as i8 as i32 + delta) as i8 as i32;
    r.clamp(0, MAX_REFINE) as u8
}

/// successrefitem <equipment slot>{,<count>{,<char_id>}}.
fn successrefitem(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let pos = getnum(rt, st, 2);
    let Some(aid) = charid2sd(rt, st, 4, "buildin_successrefitem") else {
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    let up = if st.hasdata(3) { getnum(rt, st, 3) as i16 } else { 1 };
    let i = checkequip(c, aid, pos);
    if let Some(mut it) = inv_item(c, aid, i as i32) {
        let ep = it.equip;
        //Logs items, got from (N)PC scripts [Lupus]
        crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_SCRIPT, -1, &it);
        if it.refine as i32 >= MAX_REFINE {
            st.pushint(MAX_REFINE as i64);
            return SCRIPT_CMD_SUCCESS;
        }
        it.refine = refine_add(it.refine, up as i32);
        set_inv_item(c, aid, i as i32, &it);
        pc_equip::pc_unequipitem(c, aid, i as i32, 2); // status calc will happen in pc_equipitem() below
        ci::refine(c, aid, i as usize, ci::ITEMREFINING_SUCCESS);
        ci::delitem(c, aid, i as i32, 1, 3);
        //Logs items, got from (N)PC scripts [Lupus]
        if let Some(now) = inv_item(c, aid, i as i32) {
            crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_SCRIPT, 1, &now);
        }
        ci::additem(c, aid, i as i32, 1, 0);
        pc_equip::pc_equipitem(c, aid, i, ep, false);
        if let Some(sd) = crate::unit::get(c, aid) {
            crate::status::clif::misceffect(c, &sd, ci::NOTIFYEFFECT_REFINE_SUCCESS as i32);
        }
        if let Some(now) = inv_item(c, aid, i as i32) {
            let d = inv::data_of(&now);
            if d.r#type == itemdb::IT_WEAPON {
                crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_ENCHANT_SUCCESS, &[d.weapon_level as i32, now.refine as i32]);
            }
            // Fame point system [DracoRPG]: a weapon the character forged reaches blacksmith_fame_refine_threshold (10)
            let forger = (now.card2 & 0xffff) | ((now.card3 & 0xffff) << 16);
            if now.refine as i32 == crate::fame::blacksmith_fame_refine_threshold()
                && now.card0 == itemdb::CARD0_FORGE
                && crate::pc::get(c, aid).map(|p| p.char_id) == Some(forger)
                && d.r#type == itemdb::IT_WEAPON
            {
                match d.weapon_level {
                    1 => { crate::fame::pc_addfame(c, aid, crate::fame::fame_refine_lv1()); } // +1 fame point
                    2 => { crate::fame::pc_addfame(c, aid, crate::fame::fame_refine_lv2()); } // +25 fame point
                    3 => { crate::fame::pc_addfame(c, aid, crate::fame::fame_refine_lv3()); } // +1000 fame point
                    _ => {}
                }
            }
        }
        let refine = inv_item(c, aid, i as i32).map(|it| it.refine).unwrap_or(it.refine);
        st.pushint(refine as i64);
        return SCRIPT_CMD_SUCCESS;
    }
    let cid = crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
    log::error!("buildin_successrefitem: No item equipped at pos {pos} (CID={cid}/AID={aid}).");
    st.pushint(-1);
    SCRIPT_CMD_FAILURE
}

/// failedrefitem <equipment slot>{,<char_id>}: the item is destroyed.
fn failedrefitem(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let pos = getnum(rt, st, 2);
    let Some(aid) = charid2sd(rt, st, 3, "buildin_failedrefitem") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let i = checkequip(c, aid, pos);
    if let Some(mut it) = inv_item(c, aid, i as i32) {
        it.refine = 0;
        set_inv_item(c, aid, i as i32, &it);
        pc_equip::pc_unequipitem(c, aid, i as i32, 3); // recalculate bonus
        ci::refine(c, aid, i as usize, ci::ITEMREFINING_FAILURE);
        inv::pc_delitem(c, aid, i as i32, 1, 0, 2, crate::map_log::LOG_TYPE_SCRIPT);
        if let Some(sd) = crate::unit::get(c, aid) {
            crate::status::clif::misceffect(c, &sd, ci::NOTIFYEFFECT_REFINE_FAILURE as i32);
        }
        crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_ENCHANT_FAIL, &[1]);
        st.pushint(1);
        return SCRIPT_CMD_SUCCESS;
    }
    let cid = crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
    log::error!("buildin_failedrefitem: No item equipped at pos {pos} (CID={cid}/AID={aid}).");
    st.pushint(0);
    SCRIPT_CMD_FAILURE
}

/// downrefitem <equipment slot>{,<count>{,<char_id>}}.
fn downrefitem(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 4, "buildin_downrefitem") else {
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    let pos = getnum(rt, st, 2);
    let down = if st.hasdata(3) { getnum(rt, st, 3) as i16 } else { 1 };
    let i = checkequip(c, aid, pos);
    if let Some(it) = inv_item(c, aid, i as i32) {
        let ep = it.equip;
        //Logs items, got from (N)PC scripts [Lupus]
        crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_SCRIPT, -1, &it);
        pc_equip::pc_unequipitem(c, aid, i as i32, 2); // status calc will happen in pc_equipitem() below
        if let Some(mut it) = inv_item(c, aid, i as i32) {
            it.refine = refine_add(it.refine, -(down as i32));
            set_inv_item(c, aid, i as i32, &it);
        }
        ci::refine(c, aid, i as usize, ci::ITEMREFINING_DOWNGRADE);
        ci::delitem(c, aid, i as i32, 1, 3);
        //Logs items, got from (N)PC scripts [Lupus]
        if let Some(now) = inv_item(c, aid, i as i32) {
            crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_SCRIPT, 1, &now);
        }
        ci::additem(c, aid, i as i32, 1, 0);
        pc_equip::pc_equipitem(c, aid, i, ep, false);
        if let Some(sd) = crate::unit::get(c, aid) {
            crate::status::clif::misceffect(c, &sd, ci::NOTIFYEFFECT_REFINE_FAILURE as i32);
        }
        let refine = inv_item(c, aid, i as i32).map(|it| it.refine).unwrap_or(0);
        crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_ENCHANT_FAIL, &[refine as i32]);
        st.pushint(refine as i64);
        return SCRIPT_CMD_SUCCESS;
    }
    let cid = crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
    log::error!("buildin_downrefitem: No item equipped at pos {pos} (CID={cid}/AID={aid}).");
    st.pushint(-1);
    SCRIPT_CMD_FAILURE
}

/// delequip <equipment slot>{,<char_id>}.
fn delequip(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let pos = getnum(rt, st, 2);
    let Some(aid) = charid2sd(rt, st, 3, "buildin_delequip") else {
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    };
    let i = checkequip(c, aid, pos);
    if i < 0 {
        let cid = crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
        log::error!("buildin_delequip: No item equipped at pos {pos} (CID={cid}/AID={aid}).");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    pc_equip::pc_unequipitem(c, aid, i as i32, 3); // recalculate bonus
    let ret = inv::pc_delitem(c, aid, i as i32, 1, 0, 2, crate::map_log::LOG_TYPE_SCRIPT) == 0;
    st.pushint(ret as i64);
    SCRIPT_CMD_SUCCESS
}

/// breakequip <equipment slot>{,<char_id>}.
fn breakequip(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let pos = getnum(rt, st, 2);
    let Some(aid) = charid2sd(rt, st, 3, "buildin_breakequip") else { return SCRIPT_CMD_FAILURE };
    let i = checkequip(c, aid, pos);
    if let Some(mut it) = inv_item(c, aid, i as i32) {
        it.attribute = 1;
        set_inv_item(c, aid, i as i32, &it);
        pc_equip::pc_unequipitem(c, aid, i as i32, 3);
        ci::equiplist(c, aid);
        st.pushint(1);
        return SCRIPT_CMD_SUCCESS;
    }
    let cid = crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
    log::error!("buildin_breakequip: No item equipped at pos {pos} (CID={cid}/AID={aid}).");
    st.pushint(0);
    SCRIPT_CMD_FAILURE
}

// ---------------------------------------------------------------------------------------------- cards

fn card_count(it: &Item, data: &ItemData) -> Vec<(usize, u32)> {
    (0..(data.slots as usize).min(MAX_SLOTS)).map(|j| (j, it.card(j))).filter(|&(_, cd)| cd != 0 && itemdb::itemdb_type(cd) == itemdb::IT_CARD).collect()
}

/// getequipcardcnt(<equipment slot>).
fn getequipcardcnt(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = rid2sd(rt, st, "buildin_getequipcardcnt") else { return SCRIPT_CMD_SUCCESS };
    let num = getnum(rt, st, 2);
    let i = checkequip(c, aid, num);
    let Some(it) = inv_item(c, aid, i as i32) else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    if itemdb::itemdb_isspecial(it.card0) {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    }
    st.pushint(card_count(&it, inv::data_of(&it)).len() as i64);
    SCRIPT_CMD_SUCCESS
}

/// A copy of an item without its cards (the rest of successremovecards / failedremovecards type 1).
fn without_cards(it: &Item, data: &ItemData) -> Item {
    let mut t = Item::new(it.nameid);
    t.identify = 1;
    t.refine = it.refine;
    t.attribute = it.attribute;
    t.expire_time = it.expire_time;
    t.bound = it.bound;
    t.enchantgrade = it.enchantgrade;
    for j in data.slots as usize..MAX_SLOTS {
        t.set_card(j, it.card(j));
    }
    t.option = it.options().to_vec();
    t
}

/// successremovecards <slot>: the cards go back to the inventory, the item stays without them.
fn successremovecards(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = rid2sd(rt, st, "buildin_successremovecards") else { return SCRIPT_CMD_SUCCESS };
    let num = getnum(rt, st, 2);
    let i = checkequip(c, aid, num);
    let Some(it) = inv_item(c, aid, i as i32) else { return SCRIPT_CMD_SUCCESS };
    if itemdb::itemdb_isspecial(it.card0) {
        return SCRIPT_CMD_SUCCESS;
    }
    let data = inv::data_of(&it);
    let mut cardflag = false;
    for (_, cd) in card_count(&it, data).into_iter().rev() {
        // extract this card from the item
        cardflag = true;
        let mut t = Item::new(cd);
        t.identify = 1;
        let flag = inv::pc_additem(c, aid, &mut t, 1, crate::map_log::LOG_TYPE_SCRIPT);
        // get back the card in inventory
        if flag != inv::ADDITEM_SUCCESS {
            ci::additem(c, aid, 0, 0, flag);
            log::error!("buildin_successremovecards: Failed to add the item to player.");
            return SCRIPT_CMD_FAILURE;
        }
    }
    // if card was removed, replace item with no card
    if cardflag {
        let cur = inv_item(c, aid, i as i32).unwrap_or(it);
        let mut t = without_cards(&cur, data);
        inv::pc_delitem(c, aid, i as i32, 1, 0, 3, crate::map_log::LOG_TYPE_SCRIPT);
        let flag = inv::pc_additem(c, aid, &mut t, 1, crate::map_log::LOG_TYPE_SCRIPT);
        if flag != inv::ADDITEM_SUCCESS {
            ci::additem(c, aid, 0, 0, flag);
            log::error!("buildin_successremovecards: Failed to add the item to player.");
            return SCRIPT_CMD_FAILURE;
        }
        if let Some(sd) = crate::unit::get(c, aid) {
            crate::status::clif::misceffect(c, &sd, ci::NOTIFYEFFECT_REFINE_SUCCESS as i32);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// failedremovecards <slot>,<type>: 0 destroys item and cards, 1 keeps the item, 2 keeps the cards.
fn failedremovecards(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = rid2sd(rt, st, "buildin_failedremovecards") else { return SCRIPT_CMD_SUCCESS };
    let num = getnum(rt, st, 2);
    let typefail = getnum(rt, st, 3);
    let i = checkequip(c, aid, num);
    let Some(it) = inv_item(c, aid, i as i32) else { return SCRIPT_CMD_SUCCESS };
    if itemdb::itemdb_isspecial(it.card0) {
        return SCRIPT_CMD_SUCCESS;
    }
    let data = inv::data_of(&it);
    let mut cardflag = false;
    for (_, cd) in card_count(&it, data).into_iter().rev() {
        cardflag = true;
        if typefail == 2 {
            // add cards to inventory, clear
            let mut t = Item::new(cd);
            t.identify = 1;
            let flag = inv::pc_additem(c, aid, &mut t, 1, crate::map_log::LOG_TYPE_SCRIPT);
            if flag != inv::ADDITEM_SUCCESS {
                ci::additem(c, aid, 0, 0, flag);
                log::error!("failedremovecards: Failed to add the item to player.");
                return SCRIPT_CMD_FAILURE;
            }
        }
    }
    if cardflag {
        if typefail == 0 || typefail == 2 {
            // destroy the item
            inv::pc_delitem(c, aid, i as i32, 1, 0, 2, crate::map_log::LOG_TYPE_SCRIPT);
        } else if typefail == 1 {
            // destroy the card
            let cur = inv_item(c, aid, i as i32).unwrap_or(it);
            let mut t = without_cards(&cur, data);
            inv::pc_delitem(c, aid, i as i32, 1, 0, 2, crate::map_log::LOG_TYPE_SCRIPT);
            let flag = inv::pc_additem(c, aid, &mut t, 1, crate::map_log::LOG_TYPE_SCRIPT);
            if flag != inv::ADDITEM_SUCCESS {
                ci::additem(c, aid, 0, 0, flag);
                log::error!("failedremovecards: Failed to add the item to player.");
                return SCRIPT_CMD_FAILURE;
            }
        }
        if let Some(sd) = crate::unit::get(c, aid) {
            crate::status::clif::misceffect(c, &sd, ci::NOTIFYEFFECT_REFINE_FAILURE as i32);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// getequipcardid(<equipment slot>,<card slot>).
fn getequipcardid(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = rid2sd(rt, st, "buildin_getequipcardid") else { return SCRIPT_CMD_SUCCESS };
    let num = getnum(rt, st, 2);
    let slot = getnum(rt, st, 3);
    let i = checkequip(c, aid, num);
    let v = if i >= 0 && (0..4).contains(&slot) { inv_item(c, aid, i as i32).map(|it| it.card(slot as usize)).unwrap_or(0) } else { 0 };
    st.pushint(v as i64);
    SCRIPT_CMD_SUCCESS
}

/// checkequipedcard(<card id>): is the card in any item of the inventory?
fn checkequipedcard(rt: &Rt, st: &mut State) -> i32 {
    if let (Some(c), Some(aid)) = (rt.c, rid2sd(rt, st, "buildin_checkequipedcard")) {
        let card = getnum(rt, st, 2) as u32;
        for r in inv::inv_list(c, aid) {
            let it = &r.item;
            if it.nameid > 0 && it.amount != 0 {
                if itemdb::itemdb_isspecial(it.card0) {
                    continue;
                }
                if it.cards().contains(&card) {
                    st.pushint(1);
                    return SCRIPT_CMD_SUCCESS;
                }
            }
        }
    }
    st.pushint(0);
    SCRIPT_CMD_SUCCESS
}

/// isequippedcnt(<id>{,<id>…}): how many of these items / cards are worn.
fn isequippedcnt(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = rid2sd(rt, st, "buildin_isequippedcnt") else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let mut ret: i32 = 0;
    let total = st.lastdata();
    let mut list: Vec<i32> = vec![0; total.max(0) as usize];
    let eq = equip_index(c, aid);
    for i in 2..=total {
        let id = getnum(rt, st, i);
        if id <= 0 {
            continue;
        }
        if list.contains(&id) {
            continue;
        }
        list.push(id);
        for (j, &index) in eq.iter().enumerate().take(EQI_MAX) {
            if index < 0 {
                continue;
            }
            if pc_equip::pc_is_same_equip_index(j, &eq, index) {
                continue;
            }
            let Some(it) = inv_item(c, aid, index as i32) else { continue };
            if itemdb::itemdb_type(id as u32) != itemdb::IT_CARD {
                // No card. Count amount in inventory.
                if it.nameid == id as u32 {
                    ret += it.amount as i32;
                }
            } else {
                // Count cards.
                if itemdb::itemdb_isspecial(it.card0) {
                    continue; // No cards
                }
                ret += it.cards().iter().filter(|&&cd| cd == id as u32).count() as i32;
            }
        }
    }
    st.pushint(ret as i64);
    SCRIPT_CMD_SUCCESS
}

/// isequipped(<id>{,<id>…}): are all of these worn? Cards used for a set are marked in sd->bonus.setitem_hash
/// so that they cannot count for another set (status data: the in-calculation copy while status_calc_pc runs the
/// item scripts; status_calc_pc zeroes sd->bonus).
fn isequipped(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = rid2sd(rt, st, "buildin_isequipped") else {
        // If the player is not attached it is a script error anyway... but better prevent the map server from crashing...
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let (orig_hash, orig_hash2) = crate::status::data::sd_get(c, aid).map(|sd| (sd.bonus.setitem_hash, sd.bonus.setitem_hash2)).unwrap_or((0, 0));
    let (mut hash1, mut hash2) = (orig_hash, orig_hash2);
    let eq = equip_index(c, aid);
    let mut ret: i32 = -1;
    let mut id: i32 = 1;
    let mut i: i32 = 0;
    while id != 0 {
        let mut flag = 0;
        if st.hasdata(i + 2) {
            id = getnum(rt, st, i + 2);
        } else {
            id = 0;
        }
        let this_i = i;
        i += 1;
        if id <= 0 {
            continue;
        }
        for j in 0..EQI_MAX {
            let index = eq.get(j).copied().unwrap_or(-1);
            if index < 0 {
                continue;
            }
            // rAthena passes the argument number here, not the equip index
            if pc_equip::pc_is_same_equip_index(this_i as usize, &eq, index) {
                continue;
            }
            let Some(it) = inv_item(c, aid, index as i32) else { continue };
            if itemdb::itemdb_type(id as u32) != itemdb::IT_CARD {
                if it.nameid != id as u32 {
                    continue;
                }
                flag = 1;
                break;
            } else {
                // Cards
                if itemdb::itemdb_isspecial(it.card0) {
                    continue;
                }
                for k in 0..MAX_SLOTS {
                    // New hash system which should support up to 4 slots on any equipment. [Skotlex]
                    if it.card(k) != id as u32 {
                        continue;
                    }
                    let shift = ((if j < 5 { j } else { j - 5 }) * 4 + k) as u32;
                    let hash = 1u32.wrapping_shl(shift);
                    // check if card is already used by another set
                    if (if j < 5 { hash1 } else { hash2 }) & hash != 0 {
                        continue;
                    }
                    // We have found a match
                    flag = 1;
                    // Set hash so this card cannot be used by another
                    if j < 5 {
                        hash1 |= hash;
                    } else {
                        hash2 |= hash;
                    }
                    break;
                }
            }
            if flag != 0 {
                break; // Card found
            }
        }
        if ret == -1 {
            ret = flag;
        } else {
            ret &= flag;
        }
        if ret == 0 {
            break;
        }
    }
    if ret == 0 {
        // When check fails, restore original hash values. [Skotlex]
        hash1 = orig_hash;
        hash2 = orig_hash2;
    }
    if (hash1, hash2) != (orig_hash, orig_hash2) {
        crate::status::data::with_sd(c, aid, |sd| {
            sd.bonus.setitem_hash = hash1;
            sd.bonus.setitem_hash2 = hash2;
        });
    }
    st.pushint(ret as i64);
    SCRIPT_CMD_SUCCESS
}

/// cardscnt(<id>{,<id>…}): the items / cards in the item whose script runs (current_equip_item_index).
fn cardscnt(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = rid2sd(rt, st, "buildin_cardscnt") else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let mut ret: i32 = 0;
    let mut id: i32 = 1;
    let mut i = 0;
    while id != 0 {
        if st.hasdata(i + 2) {
            id = getnum(rt, st, i + 2);
        } else {
            id = 0;
        }
        i += 1;
        if id <= 0 {
            continue;
        }
        let index = current_equip_item_index(c); // we get CURRENT WEAPON inventory index from status.cpp [Lupus]
        if index < 0 {
            continue;
        }
        let Some(it) = inv_item(c, aid, index as i32) else { continue };
        if itemdb::itemdb_type(id as u32) != itemdb::IT_CARD {
            if it.nameid == id as u32 {
                ret += it.amount as i32;
            }
        } else {
            if itemdb::itemdb_isspecial(it.card0) {
                continue;
            }
            ret += it.cards().iter().filter(|&&cd| cd == id as u32).count() as i32;
        }
    }
    st.pushint(ret as i64);
    SCRIPT_CMD_SUCCESS
}

/// getrefine(): the refine of the item whose script runs.
fn getrefine(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    if let Some(aid) = rid2sd(rt, st, "buildin_getrefine") {
        let cur = current_equip_item_index(c);
        if cur == -1 {
            st.pushint(0);
            return SCRIPT_CMD_FAILURE;
        }
        st.pushint(inv_item(c, aid, cur as i32).map(|it| it.refine as i64).unwrap_or(0));
    } else {
        st.pushint(0);
    }
    SCRIPT_CMD_SUCCESS
}

/// unequip <equipment slot>{,<char_id>}.
fn unequip(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 3, "buildin_unequip") else { return SCRIPT_CMD_FAILURE };
    let pos = getnum(rt, st, 2);
    if equip_index_check(pos) {
        let i = checkequip(c, aid, pos);
        if i >= 0 {
            pc_equip::pc_unequipitem(c, aid, i as i32, 1 | 2);
            st.pushint(1);
            return SCRIPT_CMD_SUCCESS;
        }
    }
    let cid = crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
    log::error!("buildin_unequip: No item equipped at pos {pos} (CID={cid}/AID={aid}).");
    st.pushint(0);
    SCRIPT_CMD_FAILURE
}

/// equip <item id>{,<char_id>}: equips the first item of that id.
fn equip(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 3, "buildin_equip") else { return SCRIPT_CMD_FAILURE };
    let nameid = getnum(rt, st, 2) as u32;
    if let Some(id) = itemdb::itemdb_exists(nameid) {
        let slot = inv::inv_list(c, aid).into_iter().find(|r| r.item.nameid == nameid);
        if let Some(r) = slot {
            pc_equip::pc_equipitem(c, aid, r.idx as i16, id.equip, false);
            st.pushint(1);
            return SCRIPT_CMD_SUCCESS;
        }
    }
    log::error!("buildin_equip: Item {nameid} cannot be equipped");
    st.pushint(0);
    SCRIPT_CMD_FAILURE
}

/// autoequip <item id>,<option>: the item is equipped as soon as it is obtained.
fn autoequip(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let nameid = getnum(rt, st, 2) as u32;
    let flag = getnum(rt, st, 3);
    let Some(id) = itemdb::itemdb_exists(nameid) else {
        log::error!("buildin_autoequip: Invalid item '{nameid}'.");
        return SCRIPT_CMD_FAILURE;
    };
    if !itemdb::itemdb_isequip2(id) {
        log::error!("buildin_autoequip: Item '{nameid}' cannot be equipped.");
        return SCRIPT_CMD_FAILURE;
    }
    inv::set_itemdb_autoequip(c, nameid, flag > 0);
    SCRIPT_CMD_SUCCESS
}

/// nude {<char_id>}: takes everything off.
fn nude(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 2, "buildin_nude") else { return SCRIPT_CMD_FAILURE };
    let mut calcflag = false;
    for i in 0..EQI_MAX {
        let idx = equip_index(c, aid).get(i).copied().unwrap_or(-1);
        if idx >= 0 {
            calcflag = true;
            pc_equip::pc_unequipitem(c, aid, idx as i32, 2);
        }
    }
    if calcflag {
        crate::status::calc_pc(c, aid, crate::status::SCO_NONE);
    }
    SCRIPT_CMD_SUCCESS
}

/// getequiprefinecost(<equipment slot>,<type>,<information>{,<char id>}).
fn getequiprefinecost(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let slot = getnum(rt, st, 2);
    let kind = getnum(rt, st, 3);
    let info = getnum(rt, st, 4);
    let Some(aid) = charid2sd(rt, st, 5, "buildin_getequiprefinecost") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    if !(0..REFINE_COST_MAX).contains(&kind) {
        st.pushint(-1);
        return SCRIPT_CMD_SUCCESS;
    }
    let i = checkequip(c, aid, slot);
    let Some(it) = inv_item(c, aid, i as i32) else {
        st.pushint(-1);
        return SCRIPT_CMD_SUCCESS;
    };
    let Some(level_info) = itemdb::refine::find_level_info(inv::data_of(&it), it.refine) else {
        st.pushint(-1);
        return SCRIPT_CMD_SUCCESS;
    };
    let Some(cost) = level_info.cost(kind as u16) else {
        st.pushint(-1);
        return SCRIPT_CMD_SUCCESS;
    };
    match info {
        REFINE_MATERIAL_ID => st.pushint(cost.nameid as i64),
        REFINE_ZENY_COST => st.pushint(cost.zeny as i64),
        _ => {
            st.pushint(-1);
            return SCRIPT_CMD_FAILURE;
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// getequiptradability(<equipment slot>{,<char id>}).
fn getequiptradability(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let num = getnum(rt, st, 2);
    let Some(aid) = charid2sd(rt, st, 3, "buildin_getequiptradability") else { return SCRIPT_CMD_FAILURE };
    if !equip_index_check(num) {
        log::error!("buildin_getequiptradability: Unknown equip index '{num}'");
        return SCRIPT_CMD_FAILURE;
    }
    let i = checkequip(c, aid, num);
    let v = match inv_item(c, aid, i as i32) {
        Some(it) if i >= 0 => inv::pc_can_trade_item(c, aid, &it),
        _ => false,
    };
    st.pushint(v as i64);
    SCRIPT_CMD_SUCCESS
}

/// getenchantgrade({<equipment slot>{,<char id>}}).
fn getenchantgrade(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 3, "buildin_getenchantgrade") else {
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    let position = if st.hasdata(2) { getnum(rt, st, 2) } else { EQI_COMPOUND_ON };
    let index = if position == EQI_COMPOUND_ON {
        current_equip_item_index(c)
    } else if equip_index_check(position) {
        checkequip(c, aid, position)
    } else {
        log::error!("buildin_getenchantgrade: Unknown equip index '{position}'");
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    match inv_item(c, aid, index as i32) {
        Some(it) if it.nameid != 0 => st.pushint(it.enchantgrade as i64),
        _ => st.pushint(-1),
    }
    SCRIPT_CMD_SUCCESS
}

/// getitempos(): the equip position of the item whose script runs.
fn getitempos(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = rid2sd(rt, st, "buildin_getitempos") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let cur = current_equip_item_index(c);
    if cur == -1 {
        log::error!("buildin_getitempos: Invalid usage detected. This command should only be used inside item scripts.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    }
    st.pushint(inv_item(c, aid, cur as i32).map(|it| it.equip as i64).unwrap_or(0));
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- item info

/// getitemname(<item id / name>): the English name, or "null".
fn getitemname(rt: &Rt, st: &mut State) -> i32 {
    match item_arg(rt, st, 2) {
        None => st.pushstr(b"null".to_vec()),
        Some(d) => {
            let mut n = crate::net::wire::latin1(&d.ename);
            n.truncate(ITEM_NAME_LENGTH - 1);
            st.pushstr(n);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// getitemslots(<item id>): the card slots, or -1.
fn getitemslots(rt: &Rt, st: &mut State) -> i32 {
    let item_id = getnum(rt, st, 2) as u32;
    match itemdb::itemdb_exists(item_id) {
        Some(d) => st.pushint(d.slots as i64),
        None => st.pushint(-1),
    }
    SCRIPT_CMD_SUCCESS
}

/// getiteminfo(<item id / name>,<type>).
fn getiteminfo(rt: &Rt, st: &mut State) -> i32 {
    let kind = getnum(rt, st, 3);
    let Some(d) = item_arg(rt, st, 2) else {
        if kind != ITEMINFO_AEGISNAME {
            st.pushint(-1);
        } else {
            st.pushstr(Vec::new());
        }
        return SCRIPT_CMD_SUCCESS;
    };
    let v: i64 = match kind {
        ITEMINFO_BUY => d.value_buy as i64,
        ITEMINFO_SELL => d.value_sell as i64,
        ITEMINFO_TYPE => d.r#type as i64,
        // item_data::maxchance: the monsters' drop rates (mob.cpp), -1 for items the NPC shops sell (npc.cpp)
        ITEMINFO_MAXCHANCE => match rt.c {
            Some(c) => crate::npc::item_maxchance(c, d.nameid) as i64,
            None => crate::db::mobs::mob_item_drop_info().find(d.nameid).map(|x| x.maxchance).unwrap_or(0) as i64,
        },
        ITEMINFO_GENDER => d.sex as i64,
        ITEMINFO_LOCATIONS => d.equip as i64,
        ITEMINFO_WEIGHT => d.weight as i64,
        ITEMINFO_ATTACK => d.atk as i64,
        ITEMINFO_DEFENSE => d.def as i64,
        ITEMINFO_RANGE => d.range as i64,
        ITEMINFO_SLOT => d.slots as i64,
        ITEMINFO_VIEW => {
            if d.r#type == itemdb::IT_WEAPON || d.r#type == itemdb::IT_AMMO {
                d.subtype as i64 // keep old compatibility
            } else {
                d.look as i64
            }
        }
        ITEMINFO_EQUIPLEVELMIN => d.elv as i64,
        ITEMINFO_WEAPONLEVEL => {
            if d.r#type == itemdb::IT_WEAPON {
                d.weapon_level as i64
            } else {
                0
            }
        }
        ITEMINFO_ARMORLEVEL => {
            if d.r#type == itemdb::IT_ARMOR {
                d.armor_level as i64
            } else {
                0
            }
        }
        ITEMINFO_ALIASNAME => d.view_id as i64,
        ITEMINFO_EQUIPLEVELMAX => d.elvmax as i64,
        ITEMINFO_MAGICATTACK => d.matk as i64, // RENEWAL
        ITEMINFO_ID => d.nameid as i64,
        ITEMINFO_AEGISNAME => {
            st.pushstr(crate::net::wire::latin1(&d.name));
            return SCRIPT_CMD_SUCCESS;
        }
        ITEMINFO_SUBTYPE => d.subtype as i64,
        _ => -1,
    };
    st.pushint(v);
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- item groups

/// getrandgroupitem <group_id>{,<quantity>{,<sub_group>{,<identify>{,<char_id>}}}}.
fn getrandgroupitem(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 6, "buildin_getrandgroupitem") else { return SCRIPT_CMD_SUCCESS };
    let group = getnum(rt, st, 2) as u16;
    if group == 0 {
        let g = getnum(rt, st, 2);
        log::error!("buildin_getrandgroupitem: Invalid group id ({g})!");
        return SCRIPT_CMD_FAILURE;
    }
    let mut qty: u16 = 0;
    let mut sub_group: u8 = 1;
    let mut identify: i32 = 0;
    if st.hasdata(3) {
        qty = getnum(rt, st, 3) as u16;
    }
    if st.hasdata(4) {
        sub_group = getnum(rt, st, 4) as u8;
    }
    if st.hasdata(5) {
        identify = getnum(rt, st, 5);
    }
    let Some(entry) = crate::itemgroup::get_random_entry(c, group, sub_group, itemdb::group::GROUP_ALGORITHM_USEDB) else {
        log::error!("buildin_getrandgroupitem: Unable to find a random entry in group {group} for sub group {sub_group}.");
        return SCRIPT_CMD_FAILURE;
    };
    let mut it = Item::new(entry.nameid);
    it.identify = if identify != 0 { 1 } else { itemdb::itemdb_isidentified(entry.nameid) as u8 };
    if qty == 0 {
        qty = entry.amount;
    }
    // Check if it's stackable.
    let get_count: i32 = if !itemdb::itemdb_isstackable(entry.nameid) {
        it.amount = 1;
        qty as i32
    } else {
        it.amount = qty as i16;
        1
    };
    if (inv::pc_inventoryblank(c, aid) as i32) < get_count {
        log::error!("buildin_getrandgroupitem: Not enough free space in inventory.");
        return SCRIPT_CMD_FAILURE;
    }
    for _ in 0..get_count {
        // if not pet egg
        if !pet_create_egg(c, aid, entry.nameid) {
            let amount = it.amount as i32;
            let flag = inv::pc_additem(c, aid, &mut it, amount, crate::map_log::LOG_TYPE_SCRIPT);
            if flag != inv::ADDITEM_SUCCESS {
                ci::additem(c, aid, 0, 0, flag);
                log::error!("buildin_getrandgroupitem: Failed to add the item to player.");
                return SCRIPT_CMD_FAILURE;
            }
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// getgroupitem <group_id>{,<identify>{,<char_id>}}: the items of an item group.
fn getgroupitem(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let group_id = getnum(rt, st, 2);
    let Some(aid) = charid2sd(rt, st, 4, "buildin_getgroupitem") else { return SCRIPT_CMD_FAILURE };
    let identify = st.hasdata(3) && getnum(rt, st, 3) != 0;
    if crate::itemgroup::pc_get_itemgroup(c, group_id as u16, identify, aid) != 0 {
        log::error!("buildin_getgroupitem: Invalid group id '{group_id}' specified.");
        return SCRIPT_CMD_FAILURE;
    }
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- using items

/// consumeitem <item id / name>{,<char_id>}: runs the item's use script as if it was used.
fn consumeitem(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 3, "buildin_consumeitem") else { return SCRIPT_CMD_FAILURE };
    let data = if isstring(rt, st, 2) {
        let name = lat(&getstr(rt, st, 2));
        match itemdb::itemdb_searchname(&name) {
            Some(d) => d,
            None => {
                log::error!("buildin_consumeitem: Nonexistant item {name} requested.");
                return SCRIPT_CMD_FAILURE;
            }
        }
    } else {
        let nameid = getnum(rt, st, 2) as u32;
        match itemdb::itemdb_exists(nameid) {
            Some(d) => d,
            None => {
                log::error!("buildin_consumeitem: Nonexistant item {nameid} requested.");
                return SCRIPT_CMD_FAILURE;
            }
        }
    };
    // Save the old script the player was attached to (this one, when it is the player's) and the old item id.
    let previous = sdx(c, aid).st;
    let previous_itemid = inv::st(c, aid).map(|s| s.itemid).unwrap_or(0);
    let running_is_previous = previous != 0 && previous == st.id;
    let mut previous_st = if previous != 0 && !running_is_previous { crate::script::state::load(c, previous) } else { None };
    // Detach the player from the current script
    if running_is_previous {
        npc_run::script_detach_rid(c, st);
    } else if let Some(pst) = previous_st.as_mut() {
        npc_run::script_detach_rid(c, pst);
        crate::script::state::save(c, pst);
    }
    // Set the item id to the item id of the script that will be executed
    if let Some(mut s) = inv::st(c, aid) {
        s.itemid = data.nameid;
        inv::put_st(c, &s);
    }
    inv::run_item_script(c, aid, data.nameid, inv::ItemScript::Use, 0);
    let cur = sdx(c, aid).st;
    if cur != 0 && cur != st.id {
        npc_run::script_free_state_id(c, cur);
        let mut x = sdx(c, aid);
        x.st = 0;
        put_sdx(c, &x);
    }
    // If an old script is present: because of detach the RID was removed, so restore it and reattach
    if running_is_previous {
        st.rid = aid;
        npc_run::script_attach_state(c, st);
    } else if previous_st.is_some() {
        if let Some(mut pst) = crate::script::state::load(c, previous) {
            pst.rid = aid;
            npc_run::script_attach_state(c, &mut pst);
            crate::script::state::save(c, &pst);
        }
    }
    // Restore the old item id
    if let Some(mut s) = inv::st(c, aid) {
        s.itemid = previous_itemid;
        inv::put_st(c, &s);
    }
    SCRIPT_CMD_SUCCESS
}

/// enable_items: items can be used while this NPC's dialog is open.
fn enableitemuse(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    if let Some(aid) = rid2sd(rt, st, "buildin_enableitemuse") {
        st.npc_item_flag = true;
        let mut x = sdx(c, aid);
        x.npc_item_flag = true;
        put_sdx(c, &x);
    }
    SCRIPT_CMD_SUCCESS
}

/// disable_items.
fn disableitemuse(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    if let Some(aid) = rid2sd(rt, st, "buildin_disableitemuse") {
        st.npc_item_flag = false;
        let mut x = sdx(c, aid);
        x.npc_item_flag = false;
        put_sdx(c, &x);
    }
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- cart / storage

/// checkcart({char_id}).
fn checkcart(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 2, "buildin_checkcart") else { return SCRIPT_CMD_FAILURE };
    st.pushint(inv::pc_iscarton(c, aid) as i64);
    SCRIPT_CMD_SUCCESS
}

/// setcart {<type>{,<char_id>}}.
fn setcart(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = charid2sd(rt, st, 3, "buildin_setcart") else { return SCRIPT_CMD_FAILURE };
    let kind = if st.hasdata(2) { getnum(rt, st, 2) } else { 1 };
    crate::cart::pc_setcart(c, aid, kind);
    SCRIPT_CMD_SUCCESS
}

/// openstorage: the account storage window.
fn openstorage(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = rid2sd(rt, st, "buildin_openstorage") else { return SCRIPT_CMD_SUCCESS };
    crate::storage::storage_storageopen(c, aid);
    SCRIPT_CMD_SUCCESS
}

/// openstorage2's mode check: STOR_MODE_NONE .. STOR_MODE_ALL (e_storage_mode).
pub fn openstorage2_mode_ok(mode: i32) -> bool {
    (crate::storage::STOR_MODE_NONE..=crate::storage::STOR_MODE_ALL).contains(&mode)
}

/// BUILDIN_FUNC(openstorage2) (script.cpp:25096-25128): openstorage2 <storage id>{,<mode>{,<account id>}}; — opens
/// the premium storage (storage_premiumStorage_load) and pushes whether it opened. The storage id must be in storage_db
/// (storage_exists takes the uint8 the int converts to), the mode STOR_MODE_NONE .. STOR_MODE_ALL (default ALL); no
/// player, an invalid id or mode: the error, the end of the script and SCRIPT_CMD_FAILURE, nothing pushed.
fn openstorage2(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = accid2sd(rt, st, 4, "buildin_openstorage2") else {
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    };
    let stor_id = getnum(rt, st, 2);
    if !crate::storage::storage_exists(stor_id as u8) {
        log::error!("buildin_openstorage2: Invalid storage_id '{stor_id}'!");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    let mode = if !st.hasdata(3) {
        crate::storage::STOR_MODE_ALL
    } else {
        let mode = getnum(rt, st, 3);
        if !openstorage2_mode_ok(mode) {
            log::error!("buildin_openstorage2: Invalid mode '{mode}'!");
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
        mode
    };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    st.pushint(crate::storage::storage_premium_storage_load(c, aid, stor_id as u8, mode as u8) as i64);
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- skills (Kafra)

/// getskilllv(<skill id / name>): the level of a skill the player has.
fn getskilllv(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = rid2sd(rt, st, "buildin_getskilllv") else { return SCRIPT_CMD_SUCCESS }; // no player attached, report source
    let id = if isstring(rt, st, 2) { crate::db::skills::skill_name2id(&lat(&getstr(rt, st, 2))) as i32 } else { getnum(rt, st, 2) };
    st.pushint(crate::skill::checkskill(c, aid, id as u16) as i64);
    SCRIPT_CMD_SUCCESS
}

/// basicskillcheck(): battle_config.basic_skill_check.
fn basicskillcheck(_rt: &Rt, st: &mut State) -> i32 {
    st.pushint(crate::conf::basic_skill_check() as i64);
    SCRIPT_CMD_SUCCESS
}

/// vip_status(<type>{,"<character name>"}): without VIP_ENABLE (src/config/core.hpp, off) it is always 0.
fn vip_status(_rt: &Rt, st: &mut State) -> i32 {
    st.pushint(0);
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- shops

fn is_item_shop(subtype: u8) -> bool {
    use crate::npc::{NPCTYPE_CASHSHOP, NPCTYPE_ITEMSHOP, NPCTYPE_MARKETSHOP, NPCTYPE_POINTSHOP, NPCTYPE_SHOP};
    matches!(subtype, NPCTYPE_SHOP | NPCTYPE_CASHSHOP | NPCTYPE_ITEMSHOP | NPCTYPE_POINTSHOP | NPCTYPE_MARKETSHOP)
}

/// callshop "<name>"{,<option>}: opens a shop (0 menu, 1 buy, 2 sell for normal shops).
fn callshop(rt: &Rt, st: &mut State) -> i32 {
    use crate::npc::{NPCTYPE_BARTER, NPCTYPE_MARKETSHOP, NPCTYPE_SHOP};
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(aid) = rid2sd(rt, st, "buildin_callshop") else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let shopname = lat(&getstr(rt, st, 2));
    let flag = if st.hasdata(3) { getnum(rt, st, 3) } else { 0 };
    let nd = npc_run::npc_name2id(c, &shopname).filter(|nd| is_item_shop(nd.subtype) || nd.subtype == NPCTYPE_BARTER);
    let Some(nd) = nd else {
        log::error!("buildin_callshop: Shop [{shopname}] not found (or NPC is not shop type)");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let Some(sd) = crate::unit::get(c, aid) else { return SCRIPT_CMD_SUCCESS };
    if nd.subtype == NPCTYPE_SHOP {
        // flag the user as using a valid script call for opening the shop (for floating NPCs)
        let mut x = sdx(c, aid);
        x.callshop = true;
        put_sdx(c, &x);
        match flag {
            1 => {
                crate::shop::npc_buysellsel(c, aid, nd.id, 0); // Buy window
            }
            2 => {
                crate::shop::npc_buysellsel(c, aid, nd.id, 1); // Sell window
            }
            _ => crate::shop::npcbuysell(c, &sd, nd.id), // Show menu
        }
    } else if nd.subtype == NPCTYPE_MARKETSHOP {
        if !nd.shop.iter().any(|s| s.qty != 0) {
            // color_table[COLOR_RED]: msg 534 (Shop is out of stock! Come again later!)
            crate::clif::messagecolor_target(c, &sd, crate::social::COLOR_RED, &crate::net::wire::latin1(crate::data::msg_txt(534)), false, crate::clif::Target::SelfOnly, None);
            return SCRIPT_CMD_FAILURE;
        }
        crate::shop::set_npc_shopid(c, aid, nd.id);
        crate::shop::npc_market_open(c, aid, &nd);
        st.pushint(1);
        return SCRIPT_CMD_SUCCESS;
    } else if nd.subtype == NPCTYPE_BARTER {
        // flag the user as using a valid script call for opening the shop (for floating NPCs)
        let mut x = sdx(c, aid);
        x.callshop = true;
        put_sdx(c, &x);
        crate::barter::open(c, aid, &nd);
    } else {
        crate::cashshop::clif_cashshop_show(c, aid, &nd);
    }
    crate::shop::set_npc_shopid(c, aid, nd.id);
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}

/// npcshopadditem "<name>",<item id>,<price>{,<item id>,<price>…} (market shops: item id, price, stock).
fn npcshopadditem(rt: &Rt, st: &mut State) -> i32 {
    use crate::npc::{npc, ShopItem, NPCTYPE_CASHSHOP, NPCTYPE_ITEMSHOP, NPCTYPE_MARKETSHOP, NPCTYPE_POINTSHOP};
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let npcname = lat(&getstr(rt, st, 2));
    let Some(mut nd) = npc_run::npc_name2id(c, &npcname).filter(|nd| is_item_shop(nd.subtype)) else {
        // Not found.
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let offs: i32 = if nd.subtype == NPCTYPE_MARKETSHOP { 3 } else { 2 };
    // get the count of new entries
    let amount = ((st.lastdata() - 2) / offs) as u16;
    let mut ret = SCRIPT_CMD_SUCCESS;
    let mut pushed = false;
    // a market shop's existing entries change in place, seen by the duplicates sharing the list, until an entry is
    // appended (RECREATE); the other shops' lists are RECREATEd up front (npc::shop_sharers)
    let before = nd.shop.clone();
    let mut shared = nd.subtype == NPCTYPE_MARKETSHOP;
    let mut i = 3;
    for _ in 0..amount {
        let nameid = getnum(rt, st, i) as u32;
        let Some(id) = itemdb::itemdb_exists(nameid) else {
            log::error!("builtin_npcshopadditem: Item ID {nameid} does not exist.");
            st.pushint(0);
            ret = SCRIPT_CMD_FAILURE;
            pushed = true;
            break;
        };
        let mut price = getnum(rt, st, i + 1);
        if nd.subtype == NPCTYPE_MARKETSHOP {
            // Check existing entries
            let j = match nd.shop.iter().position(|s| s.nameid == nameid) {
                Some(j) => j,
                None => {
                    if shared {
                        crate::npc::shop_share(c, &nd, &before);
                        shared = false;
                    }
                    nd.shop.push(ShopItem { nameid, value: 0, qty: 0, flag: 1 });
                    nd.shop.len() - 1
                }
            };
            if price < 0 {
                price = id.value_buy as i32;
            }
            let stock = getnum(rt, st, i + 2);
            if stock < -1 {
                log::error!("builtin_npcshopadditem: Invalid stock amount in marketshop '{}'.", nd.exname);
                st.pushint(0);
                ret = SCRIPT_CMD_FAILURE;
                pushed = true;
                break;
            }
            nd.shop[j].nameid = nameid;
            nd.shop[j].value = price;
            nd.shop[j].qty = stock;
            let f = nd.shop[j].flag;
            crate::shop::npc_market_tosql(c, &nd.exname, nameid, price, stock, f);
        } else {
            if price < 0 {
                if matches!(nd.subtype, NPCTYPE_CASHSHOP | NPCTYPE_POINTSHOP | NPCTYPE_ITEMSHOP) {
                    log::error!("builtin_npcshopadditem: Invalid price in shop '{}'.", nd.exname);
                    st.pushint(0);
                    ret = SCRIPT_CMD_FAILURE;
                    pushed = true;
                    break;
                }
                price = id.value_buy as i32;
            }
            nd.shop.push(ShopItem { nameid, value: price, qty: 0, flag: 0 });
        }
        i += offs;
    }
    if shared {
        crate::npc::shop_share(c, &nd, &before);
    }
    c.db().npc().id().update(nd);
    if !pushed {
        st.pushint(1);
    }
    ret
}

/// npcshopupdate "<name>",<item id>,<price>{,<stock>}.
fn npcshopupdate(rt: &Rt, st: &mut State) -> i32 {
    use crate::npc::{npc, NPCTYPE_CASHSHOP, NPCTYPE_ITEMSHOP, NPCTYPE_MARKETSHOP, NPCTYPE_POINTSHOP};
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let npcname = lat(&getstr(rt, st, 2));
    let found = npc_run::npc_name2id(c, &npcname);
    let nameid = getnum(rt, st, 3) as u32;
    let price = getnum(rt, st, 4);
    let stock = if st.hasdata(5) { getnum(rt, st, 5) } else { -1 };
    if stock < -1 {
        log::error!("buildin_npcshopupdate: Invalid stock amount in marketshop '{}'.", found.as_ref().map(|n| n.exname.as_str()).unwrap_or(""));
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    }
    let Some(mut nd) = found.filter(|nd| is_item_shop(nd.subtype)) else {
        // Not found.
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    if nd.shop.is_empty() {
        log::error!("buildin_npcshopupdate: Attempt to update empty shop from '{}'.", nd.exname);
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    }
    // changes in place: the duplicates sharing the list see them (npc::shop_sharers)
    let before = nd.shop.clone();
    for i in 0..nd.shop.len() {
        if nd.shop[i].nameid != nameid {
            continue;
        }
        if price != 0 {
            let mut p = price;
            if p < 0 {
                if matches!(nd.subtype, NPCTYPE_CASHSHOP | NPCTYPE_POINTSHOP | NPCTYPE_ITEMSHOP) {
                    log::error!("builtin_npcshopupdate: Invalid price in shop '{}'.", nd.exname);
                    crate::npc::shop_share(c, &nd, &before);
                    c.db().npc().id().update(nd);
                    st.pushint(0);
                    return SCRIPT_CMD_FAILURE;
                }
                let Some(id) = itemdb::itemdb_exists(nameid) else {
                    log::error!("buildin_npcshopupdate: Item ID {nameid} does not exist.");
                    crate::npc::shop_share(c, &nd, &before);
                    c.db().npc().id().update(nd);
                    st.pushint(0);
                    return SCRIPT_CMD_FAILURE;
                };
                p = id.value_buy as i32;
            }
            nd.shop[i].value = p;
        }
        if nd.subtype == NPCTYPE_MARKETSHOP {
            nd.shop[i].qty = stock;
            let (v, f) = (nd.shop[i].value, nd.shop[i].flag);
            crate::shop::npc_market_tosql(c, &nd.exname, nameid, v, stock, f);
        }
    }
    crate::npc::shop_share(c, &nd, &before);
    c.db().npc().id().update(nd);
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}
