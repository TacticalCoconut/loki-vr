//! map/storage.cpp: the account storage (Kafra storage) — open / close, putting items in and taking them out
//! (from the inventory or the cart), the storage window packets' callers, and the load / save of the `storage`
//! table (rAthena `storage`, inter_server.yml storage 0 "Storage", MAX_STORAGE slots; the VIP system is off so
//! MIN_STORAGE = MAX_STORAGE). Guild storage (storage_flag 2) is guild_storage.rs. The premium storage (storage_flag
//! 3, openstorage2) is here too: with inter_server.yml's storage 0 as the only storage it is never loaded and stays
//! empty (see PcPremiumStorage).

use spacetimedb::{table, Table};

use crate::clif_items as ci;
use crate::db::items as itemdb;
use crate::inventory::*;
use crate::pc;
use crate::unit;
use crate::world::Ctx;

/// mmo.hpp MAX_STORAGE (inter_server.yml storage 0 has no `Max`).
pub const MAX_STORAGE: usize = 600;
/// conf/battle/client.conf `client_sort_storage: no`.
pub fn client_sort_storage() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::CLIENT_SORT_STORAGE) != 0
}
/// conf/map_athena.conf `save_settings: 4095` (CHARSAVE_STORAGE is on).
pub const CHARSAVE_STORAGE: bool = true;

/// rAthena `storage` table + the slot index (sd->storage.u.items_storage).
#[table(accessor = storage, index(accessor = by_account_idx, btree(columns = [account_id, idx])))]
#[derive(Clone, Debug)]
pub struct StorageItem {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    #[index(btree)]
    pub account_id: u32,
    pub idx: u16,
    pub save_id: u64,
    pub item: Item,
}

/// sd->storage.u.items_storage[idx].
pub fn storage_get(c: &Ctx, aid: u32, idx: usize) -> Option<StorageItem> {
    if idx >= MAX_STORAGE {
        return None;
    }
    c.db().storage().by_account_idx().filter((aid, idx as u16)).next()
}

pub fn storage_list(c: &Ctx, aid: u32) -> Vec<StorageItem> {
    let mut v: Vec<StorageItem> = c.db().storage().account_id().filter(aid).collect();
    v.sort_by_key(|r| r.idx);
    v
}

fn storage_put(c: &Ctx, r: &StorageItem) {
    c.db().storage().id().update(r.clone());
}

fn set_dirty(c: &Ctx, aid: u32) {
    if let Some(mut s) = st(c, aid) {
        if !s.storage_dirty {
            s.storage_dirty = true;
            put_st(c, &s);
        }
    }
}

fn amounts(c: &Ctx, aid: u32) -> (u16, u16) {
    st(c, aid).map(|s| (s.storage_amount, s.storage_max)).unwrap_or((0, MAX_STORAGE as u16))
}

fn set_amount(c: &Ctx, aid: u32, amount: u16) {
    if let Some(mut s) = st(c, aid) {
        s.storage_amount = amount;
        put_st(c, &s);
    }
}

/// intif_parse_StorageReceived(TABLE_STORAGE): char_memitemdata_from_sql puts the rows in slots 0.. in SQL id order;
/// the storage mode STOR_MODE_ALL lets the player put and get.
pub fn storage_received(c: &Ctx, aid: u32) {
    let mut rows: Vec<StorageItem> = c.db().storage().account_id().filter(aid).collect();
    rows.sort_by_key(|r| (r.save_id == 0, r.save_id, r.idx, r.id));
    let mut n = 0u16;
    for (i, mut r) in rows.into_iter().enumerate() {
        if i >= MAX_STORAGE {
            break;
        }
        if r.idx != i as u16 {
            r.idx = i as u16;
            c.db().storage().id().update(r);
        }
        n += 1;
    }
    if let Some(mut s) = st(c, aid) {
        s.storage_amount = n;
        s.storage_max = MAX_STORAGE as u16;
        s.storage_dirty = false;
        put_st(c, &s);
    }
}

/// storage_storagesave → char_memitemdata_to_sql(TABLE_STORAGE).
pub fn storage_tosql(c: &Ctx, aid: u32) {
    let rows = storage_list(c, aid);
    let items: Vec<(u64, u64, Item)> = rows.iter().map(|r| (r.id, r.save_id, r.item.clone())).collect();
    let assigned = memitemdata_to_sql(c, &items);
    for r in rows {
        if let Some(&sid) = assigned.get(&r.id) {
            if r.save_id != sid {
                storage_put(c, &StorageItem { save_id: sid, ..r });
            }
        }
    }
    if let Some(mut s) = st(c, aid) {
        s.storage_dirty = false;
        put_st(c, &s);
    }
}

/// pc_check_available_item for ITMCHK_STORAGE.
pub fn check_available(c: &Ctx, aid: u32) {
    let Some(sd) = unit::get(c, aid) else { return };
    for r in storage_list(c, aid) {
        let nameid = r.item.nameid;
        if !itemdb::itemdb_available(nameid) {
            crate::clif::displaymessage(c, &sd, &crate::data::format_msg(crate::data::msg_txt(711), &[&nameid]));
            log::warn!("Removed invalid/disabled item (ID: {nameid}, amount: {}) from storage (account_id: {aid}).", r.item.amount);
            storage_delitem(c, aid, r.idx as usize, r.item.amount as i32);
            continue;
        }
        if r.item.unique_id == 0 && !itemdb::itemdb_isstackable(nameid) {
            let uid = pc_generate_unique_id(c, aid);
            if let Some(mut r2) = storage_get(c, aid, r.idx as usize) {
                r2.item.unique_id = uid;
                storage_put(c, &r2);
            }
        }
    }
}

/// storage.cpp storage_db: the storages the char server announces (intif_parse_StorageInfo, from conf/inter_server.yml;
/// Loki's overlay has no conf/import/inter_server.yml): only storage 0 "Storage", the account storage (id, name).
pub const STORAGE_DB: &[(u8, &str)] = &[(0, "Storage")];

/// storage.cpp storage_getName: the storage's name, "Storage" when unknown.
pub fn storage_get_name(id: u8) -> &'static str {
    STORAGE_DB.iter().find(|s| s.0 == id).map(|s| s.1).unwrap_or("Storage")
}

/// storage.cpp storage_exists (the id is the uint8 the caller's int converts to).
pub fn storage_exists(id: u8) -> bool {
    STORAGE_DB.iter().any(|s| s.0 == id)
}

/// storage.cpp storage_sortitem (client_sort_storage: no — nothing is sorted).
pub fn storage_sortitem() {
    let _ = client_sort_storage();
}

/// The storage window's slots (sd->storage.u.items_storage, ARRAYLENGTH = MAX_STORAGE).
fn items_of(c: &Ctx, aid: u32) -> Vec<(usize, Item)> {
    storage_list(c, aid).into_iter().map(|r| (r.idx as usize, r.item)).collect()
}

/// storage.cpp storage_storageopen: 0 opened, 1 refused.
pub fn storage_storageopen(c: &Ctx, aid: u32) -> i32 {
    let Some(mut p) = pc::get(c, aid) else { return 1 };
    if p.storage_flag != 0 {
        return 1; // Already open?
    }
    if !pc_can_give_items(c, aid) {
        if let Some(sd) = unit::get(c, aid) {
            crate::clif::displaymessage(c, &sd, crate::data::msg_txt(246)); // Your GM level doesn't authorize you to perform this action.
        }
        return 1;
    }
    p.storage_flag = 1;
    pc::put(c, &p);
    storage_sortitem();
    ci::storagelist(c, aid, &items_of(c, aid), storage_get_name(0));
    let (amount, max) = amounts(c, aid);
    ci::updatestorageamount(c, aid, amount, max);
    0
}

/// clif.cpp clif_refresh_storagewindow's storage part (storage_flag 1): the open storage window is sent again.
pub fn refresh_storagewindow(c: &Ctx, aid: u32) {
    storage_sortitem();
    ci::storagelist(c, aid, &items_of(c, aid), storage_get_name(0));
    let (amount, max) = amounts(c, aid);
    ci::updatestorageamount(c, aid, amount, max);
}

/// storage.cpp compare_item.
pub fn compare_item(a: &Item, b: &Item) -> bool {
    a.nameid == b.nameid
        && a.identify == b.identify
        && a.refine == b.refine
        && a.attribute == b.attribute
        && a.expire_time == b.expire_time
        && a.bound == b.bound
        && a.unique_id == b.unique_id
        && a.cards() == b.cards()
}

/// storage.hpp e_storage_add.
const STORAGE_ADD_OK: u8 = 0;
const STORAGE_ADD_NOROOM: u8 = 1;
const STORAGE_ADD_NOACCESS: u8 = 2;
const STORAGE_ADD_INVALID: u8 = 3;

/// storage.cpp storage_canAddItem; `put` is stor->state.put (the account storage is opened with STOR_MODE_ALL: put is
/// allowed; a premium storage has the mode of its openstorage2).
fn storage_can_add_item(item: Option<&Item>, amount: i32, put: bool) -> u8 {
    let Some(it) = item else { return STORAGE_ADD_INVALID };
    if it.nameid == 0 {
        return STORAGE_ADD_INVALID;
    }
    if amount < 1 || amount > it.amount as i32 {
        return STORAGE_ADD_INVALID;
    }
    if itemdb::itemdb_ishatched_egg(it.card0, it.attribute) {
        return STORAGE_ADD_INVALID;
    }
    if !put {
        return STORAGE_ADD_NOACCESS;
    }
    STORAGE_ADD_OK
}

/// storage.cpp storage_canGetItem (STOR_MODE_ALL: get is allowed).
fn storage_can_get_item(c: &Ctx, aid: u32, idx: i32, amount: i32) -> u8 {
    if idx < 0 || idx as usize >= MAX_STORAGE {
        return STORAGE_ADD_INVALID;
    }
    let Some(r) = storage_get(c, aid, idx as usize) else { return STORAGE_ADD_INVALID };
    if amount < 1 || amount > r.item.amount as i32 {
        return STORAGE_ADD_INVALID;
    }
    STORAGE_ADD_OK
}

/// storage.cpp storage_additem's checks before the storage's slots are looked at (any storage): Err(1) failed, Err(2)
/// over the item's storage stack limit.
fn storage_additem_checks(c: &Ctx, aid: u32, it: &Item, amount: i32) -> Result<(), i32> {
    if it.nameid == 0 || amount <= 0 {
        return Err(1);
    }
    let data = itemdb::itemdb_search(it.nameid);
    if data.stack.storage && amount > data.stack.amount as i32 {
        return Err(2); // item stack limitation
    }
    let Some(sd) = unit::get(c, aid) else { return Err(1) };
    if !itemdb::itemdb_isrestricted(it.nameid, &it.cards(), pc::group_level(c, aid), 0, itemdb::itemdb_canstore_sub) {
        crate::clif::displaymessage(c, &sd, crate::data::msg_txt(264)); // Check if item is storable. [Skotlex]
        return Err(1);
    }
    if it.bound > BOUND_ACCOUNT && !pc_can_give_bounded_items(c, aid) {
        crate::clif::displaymessage(c, &sd, crate::data::msg_txt(294));
        return Err(1);
    }
    Ok(())
}

/// storage.cpp storage_additem: 0 success, 1 failed, 2 no room / stack limit.
fn storage_additem(c: &Ctx, aid: u32, it: &Item, amount: i32) -> i32 {
    if let Err(r) = storage_additem_checks(c, aid, it, amount) {
        return r;
    }
    let data = itemdb::itemdb_search(it.nameid);
    let (amount_now, max) = amounts(c, aid);
    let list = storage_list(c, aid);
    if data.is_stackable() {
        // existing items found, stack them (the first max_amount slots)
        if let Some(r) = list.iter().find(|r| (r.idx as u16) < max && compare_item(&r.item, it)) {
            if amount > MAX_AMOUNT - r.item.amount as i32 || (data.stack.storage && amount > data.stack.amount as i32 - r.item.amount as i32) {
                return 2;
            }
            let mut r2 = r.clone();
            r2.item.amount += amount as i16;
            storage_put(c, &r2);
            set_dirty(c, aid);
            ci::storageitemadded(c, aid, &r2.item, r2.idx as usize, amount);
            return 0;
        }
    }
    if amount_now >= max {
        return 2;
    }
    // find free slot
    let used: std::collections::HashSet<u16> = list.iter().map(|r| r.idx).collect();
    let Some(i) = (0..max as usize).find(|i| !used.contains(&(*i as u16))) else { return 2 };
    let mut ni = it.clone();
    ni.amount = amount as i16;
    c.db().storage().insert(StorageItem { id: 0, account_id: aid, idx: i as u16, save_id: 0, item: ni.clone() });
    set_amount(c, aid, amount_now + 1);
    set_dirty(c, aid);
    ci::storageitemadded(c, aid, &ni, i, amount);
    ci::updatestorageamount(c, aid, amount_now + 1, max);
    0
}

/// storage.cpp storage_delitem: 0 success, 1 fail.
pub fn storage_delitem(c: &Ctx, aid: u32, index: usize, amount: i32) -> i32 {
    let Some(mut r) = storage_get(c, aid, index) else { return 1 };
    if r.item.nameid == 0 || (r.item.amount as i32) < amount {
        return 1;
    }
    r.item.amount -= amount as i16;
    set_dirty(c, aid);
    let flag = pc::get(c, aid).map(|p| p.storage_flag).unwrap_or(0);
    if r.item.amount == 0 {
        c.db().storage().id().delete(r.id);
        let (a, max) = amounts(c, aid);
        let a = a.saturating_sub(1);
        set_amount(c, aid, a);
        if flag == 1 || flag == 3 {
            ci::updatestorageamount(c, aid, a, max);
        }
    } else {
        storage_put(c, &r);
    }
    if flag == 1 || flag == 3 {
        ci::storageitemremoved(c, aid, index as i32, amount as u32);
    }
    0
}

/// storage.cpp storage_storageadd: inventory → storage.
pub fn storage_storageadd(c: &Ctx, aid: u32, index: i32, amount: i32) {
    let item = if index >= 0 && (index as usize) < MAX_INVENTORY { inv_get(c, aid, index as usize).map(|r| r.item) } else { None };
    let result = storage_can_add_item(item.as_ref(), amount, true);
    if result == STORAGE_ADD_INVALID {
        return;
    }
    if result == STORAGE_ADD_OK {
        if let Some(it) = item.as_ref() {
            match storage_additem(c, aid, it, amount) {
                0 => {
                    pc_delitem(c, aid, index, amount, 0, 4, crate::map_log::LOG_TYPE_STORAGE);
                    return;
                }
                _ => {
                    let _ = STORAGE_ADD_NOROOM;
                }
            }
        }
    }
    ci::storageitemremoved(c, aid, index, 0);
    ci::dropitem(c, aid, index, 0);
}

/// storage.cpp storage_storageget: storage → inventory.
pub fn storage_storageget(c: &Ctx, aid: u32, index: i32, amount: i32, favorite: bool) {
    if storage_can_get_item(c, aid, index, amount) != STORAGE_ADD_OK {
        return;
    }
    let Some(r) = storage_get(c, aid, index as usize) else { return };
    let mut it = r.item.clone();
    let flag = pc_additem_fav(c, aid, &mut it, amount, crate::map_log::LOG_TYPE_STORAGE, favorite);
    // pc_additem may have given the stored item a unique id
    if it.unique_id != r.item.unique_id {
        if let Some(mut r2) = storage_get(c, aid, index as usize) {
            r2.item.unique_id = it.unique_id;
            storage_put(c, &r2);
        }
    }
    if flag == ADDITEM_SUCCESS {
        storage_delitem(c, aid, index as usize, amount);
    } else {
        ci::storageitemremoved(c, aid, index, 0);
        ci::additem(c, aid, 0, 0, flag);
    }
}

/// storage.cpp storage_storageaddfromcart: cart → storage.
pub fn storage_storageaddfromcart(c: &Ctx, aid: u32, index: i32, amount: i32) {
    if pc::get(c, aid).map(|p| p.prevend).unwrap_or(false) {
        return;
    }
    let item = if index >= 0 && (index as usize) < MAX_CART { crate::cart::cart_get(c, aid, index as usize).map(|r| r.item) } else { None };
    let result = storage_can_add_item(item.as_ref(), amount, true);
    if result == STORAGE_ADD_INVALID {
        return;
    }
    if result == STORAGE_ADD_OK {
        if let Some(it) = item.as_ref() {
            if storage_additem(c, aid, it, amount) == 0 {
                crate::cart::pc_cart_delitem(c, aid, index, amount, 0, crate::map_log::LOG_TYPE_STORAGE);
                return;
            }
        }
    }
    ci::storageitemremoved(c, aid, index, 0);
    ci::dropitem(c, aid, index, 0);
}

/// storage.cpp storage_storagegettocart: storage → cart.
pub fn storage_storagegettocart(c: &Ctx, aid: u32, index: i32, amount: i32) {
    if pc::get(c, aid).map(|p| p.prevend).unwrap_or(false) {
        return;
    }
    if storage_can_get_item(c, aid, index, amount) != STORAGE_ADD_OK {
        return;
    }
    let Some(r) = storage_get(c, aid, index as usize) else { return };
    let flag = crate::cart::pc_cart_additem(c, aid, &r.item, amount, crate::map_log::LOG_TYPE_STORAGE);
    if flag == 0 {
        storage_delitem(c, aid, index as usize, amount);
    } else {
        ci::storageitemremoved(c, aid, index, 0);
        ci::cart_additem_ack(c, aid, if flag == ADDITEM_INVALID { 0 } else { 1 });
    }
}

/// chrif_save's item part (CSAVE_INVENTORY | CSAVE_CART) and storage_storagesave for a dirty storage.
pub fn chrif_save_items(c: &Ctx, aid: u32) {
    let Some(s) = st(c, aid) else { return };
    if s.storage_dirty {
        storage_tosql(c, aid);
    }
    inventory_tosql(c, s.char_id);
    crate::cart::cart_tosql(c, s.char_id);
}

/// storage.cpp storage_storageclose.
pub fn storage_storageclose(c: &Ctx, aid: u32) {
    if st(c, aid).map(|s| s.storage_dirty).unwrap_or(false) {
        if CHARSAVE_STORAGE {
            crate::chrif::chrif_save(c, aid, crate::chrif::CSAVE_INVENTORY | crate::chrif::CSAVE_CART);
        } else {
            storage_tosql(c, aid);
        }
    }
    if let Some(mut p) = pc::get(c, aid) {
        if p.storage_flag == 1 {
            p.storage_flag = 0;
            pc::put(c, &p);
            ci::storageclose(c, aid);
        }
    }
}

/// storage.cpp storage_storage_quit: save without closing the window (unit_remove_map clears storage_flag after it).
pub fn storage_storage_quit(c: &Ctx, aid: u32) {
    if CHARSAVE_STORAGE {
        crate::chrif::chrif_save(c, aid, crate::chrif::CSAVE_INVENTORY | crate::chrif::CSAVE_CART);
    } else {
        storage_tosql(c, aid);
    }
}

// ---------------------------------------------------------------------------------------------- premium storages

/// mmo.hpp e_storage_mode.
pub const STOR_MODE_NONE: i32 = 0x0;
pub const STOR_MODE_GET: i32 = 0x1;
pub const STOR_MODE_PUT: i32 = 0x2;
pub const STOR_MODE_ALL: i32 = 0x3;

/// sd->premiumStorage.stor_id: the 0 pc_setnewpc's memset leaves (see [`PcPremiumStorage`]).
const PREMIUM_STOR_ID: u8 = 0;

// Only storage 0 exists: sd->premiumStorage is never loaded (PcPremiumStorage). A premium storage in inter_server.yml
// would need intif_storage_request / intif_parse_StorageReceived's premium part and the storage rows of one.
const _: () = assert!(STORAGE_DB.len() == 1 && STORAGE_DB[0].0 == PREMIUM_STOR_ID, "a premium storage in STORAGE_DB: port the premium storage loading");

/// sd->premiumStorage's state.get / state.put (map_session_data; pc_setnewpc memsets the whole struct at login). With
/// STORAGE_DB holding only storage 0 the premium storage is never loaded: storage_premiumStorage_load asks the char
/// server only when `sd->premiumStorage.stor_id != num`, and stor_id stays the 0 of the memset — the only id
/// storage_exists lets through —, while intif_parse_StorageReceived puts a stor_id 0 answer into sd->storage. So it
/// stays empty: stor_id 0, amount 0, max_amount 0, never dirty (nothing can be put into 0 slots), no items; only get
/// and put change (the mode of the last openstorage2). The row lives from the first openstorage2 until unit_free.
#[table(accessor = pc_premium_storage)]
#[derive(Clone, Debug)]
pub struct PcPremiumStorage {
    #[primary_key]
    pub account_id: u32,
    /// sd->premiumStorage.state.get / state.put.
    pub state_get: bool,
    pub state_put: bool,
}

/// sd->premiumStorage's state (get and put off, as the memset leaves them, before any openstorage2).
fn premium(c: &Ctx, aid: u32) -> PcPremiumStorage {
    c.db().pc_premium_storage().account_id().find(aid).unwrap_or(PcPremiumStorage { account_id: aid, state_get: false, state_put: false })
}

fn put_premium(c: &Ctx, r: PcPremiumStorage) {
    if c.db().pc_premium_storage().account_id().find(r.account_id).is_some() {
        c.db().pc_premium_storage().account_id().update(r);
    } else {
        c.db().pc_premium_storage().insert(r);
    }
}

/// unit_free: sd->premiumStorage goes with the map_session_data.
pub fn on_unit_free(c: &Ctx, aid: u32) {
    c.db().pc_premium_storage().account_id().delete(aid);
}

/// The premium storage window: storage_sortitem, clif_storagelist of its (empty) slots under storage_getName(stor_id),
/// clif_updatestorageamount(amount 0, max_amount 0).
fn premium_storagelist(c: &Ctx, aid: u32) {
    storage_sortitem();
    ci::storagelist(c, aid, &[], storage_get_name(PREMIUM_STOR_ID));
    ci::updatestorageamount(c, aid, 0, 0);
}

/// storage.cpp storage_premiumStorage_open: storage_flag 3 and the window.
pub fn storage_premium_storage_open(c: &Ctx, aid: u32) {
    if let Some(mut p) = pc::get(c, aid) {
        p.storage_flag = 3;
        pc::put(c, &p);
    }
    premium_storagelist(c, aid);
}

/// storage.cpp storage_premiumStorage_load (openstorage2): false when the player may not open a storage now (one is
/// open; vending, a buying store, preparing a shop or autotrading; the bank or a callshop window; a GM level that may
/// not move items: msg 246), else the mode's get / put are set and the premium storage opens (true).
pub fn storage_premium_storage_load(c: &Ctx, aid: u32, num: u8, mode: u8) -> bool {
    let Some(p) = pc::get(c, aid) else { return false };
    if p.storage_flag != 0 {
        return false;
    }
    if p.vending || p.buyingstore || p.prevend || crate::autotrade::autotrade(c, aid) != 0 {
        return false;
    }
    let banking = crate::cashshop::points(c, aid).map(|x| x.banking).unwrap_or(false);
    if banking || crate::script::npc_run::sdx(c, aid).callshop {
        return false;
    }
    if !pc_can_give_items(c, aid) {
        // check is this GM level is allowed to put items to storage
        if let Some(sd) = unit::get(c, aid) {
            crate::clif::displaymessage(c, &sd, crate::data::msg_txt(246)); // Your GM level doesn't authorize you to perform this action.
        }
        return false;
    }
    if num != PREMIUM_STOR_ID {
        // intif_storage_request(sd, TABLE_STORAGE, num, mode): unreachable, storage_exists lets only storage 0 through
        // (see the assertion on STORAGE_DB)
        log::error!("storage_premiumStorage_load: storage {num} is not in storage_db.");
        return false;
    }
    let mode = mode as i32;
    put_premium(c, PcPremiumStorage { account_id: aid, state_get: mode & STOR_MODE_GET != 0, state_put: mode & STOR_MODE_PUT != 0 });
    storage_premium_storage_open(c, aid);
    true
}

/// clif.cpp clif_refresh_storagewindow's premium part (storage_flag 3): the window again.
pub fn refresh_premium_storagewindow(c: &Ctx, aid: u32) {
    premium_storagelist(c, aid);
}

/// clif.cpp clif_refresh_storagewindow (clif.cpp:9811-9837): the storage window the player has open (storage_flag 1
/// account storage, 2 guild storage — requested from the char server when not loaded —, 3 premium storage) is sent
/// again, "otherwise it will remain locked forever"; nothing without one. @refresh and the Teleport / Odin's Recall
/// warp of skill_castend_map (bugreport:8027) call it.
pub fn clif_refresh_storagewindow(c: &Ctx, aid: u32) {
    match pc::get(c, aid).map(|p| p.storage_flag).unwrap_or(0) {
        1 => refresh_storagewindow(c, aid),
        2 => crate::guild_storage::refresh_storagewindow(c, aid),
        3 => refresh_premium_storagewindow(c, aid),
        _ => {}
    }
}

/// storage.cpp storage_premiumStorage_close: the premium storage is never dirty (no save), then storage_flag 3 → 0 and
/// ZC_CLOSE_STORE.
pub fn storage_premium_storage_close(c: &Ctx, aid: u32) {
    if let Some(mut p) = pc::get(c, aid) {
        if p.storage_flag == 3 {
            p.storage_flag = 0;
            pc::put(c, &p);
            ci::storageclose(c, aid);
        }
    }
}

/// storage.cpp storage_premiumStorage_quit: chrif_save(CSAVE_INVENTORY | CSAVE_CART) (CHARSAVE_STORAGE; its premium
/// part, storage_premiumStorage_save of a dirty premium storage, never runs: it is never dirty).
pub fn storage_premium_storage_quit(c: &Ctx, aid: u32) {
    if CHARSAVE_STORAGE {
        crate::chrif::chrif_save(c, aid, crate::chrif::CSAVE_INVENTORY | crate::chrif::CSAVE_CART);
    }
    // else storage_premiumStorage_save(sd): intif_storage_save of the empty premium storage, nothing to write
}

/// storage_additem on sd->premiumStorage: the checks, then no stackable slot among its first max_amount (0) slots and
/// `stor->amount >= stor->max_amount` (0 >= 0): 2, no room.
fn premium_additem(c: &Ctx, aid: u32, it: &Item, amount: i32) -> i32 {
    if let Err(r) = storage_additem_checks(c, aid, it, amount) {
        return r;
    }
    2
}

/// storage_storageadd(sd, &sd->premiumStorage, …): inventory → premium storage. Refused (no put access, or no room:
/// premium_additem) with ZC_DELETE_ITEM_FROM_STORE and ZC_ITEM_THROW_ACK of 0.
fn premium_storageadd(c: &Ctx, aid: u32, index: i32, amount: i32) {
    let item = if index >= 0 && (index as usize) < MAX_INVENTORY { inv_get(c, aid, index as usize).map(|r| r.item) } else { None };
    let result = storage_can_add_item(item.as_ref(), amount, premium(c, aid).state_put);
    if result == STORAGE_ADD_INVALID {
        return;
    }
    if result == STORAGE_ADD_OK {
        if let Some(it) = item.as_ref() {
            if premium_additem(c, aid, it, amount) == 0 {
                pc_delitem(c, aid, index, amount, 0, 4, crate::map_log::LOG_TYPE_STORAGE);
                return;
            }
        }
    }
    ci::storageitemremoved(c, aid, index, 0);
    ci::dropitem(c, aid, index, 0);
}

/// storage_storageaddfromcart(sd, &sd->premiumStorage, …): cart → premium storage, refused as premium_storageadd.
fn premium_storageaddfromcart(c: &Ctx, aid: u32, index: i32, amount: i32) {
    if pc::get(c, aid).map(|p| p.prevend).unwrap_or(false) {
        return;
    }
    let item = if index >= 0 && (index as usize) < MAX_CART { crate::cart::cart_get(c, aid, index as usize).map(|r| r.item) } else { None };
    let result = storage_can_add_item(item.as_ref(), amount, premium(c, aid).state_put);
    if result == STORAGE_ADD_INVALID {
        return;
    }
    if result == STORAGE_ADD_OK {
        if let Some(it) = item.as_ref() {
            if premium_additem(c, aid, it, amount) == 0 {
                crate::cart::pc_cart_delitem(c, aid, index, amount, 0, crate::map_log::LOG_TYPE_STORAGE);
                return;
            }
        }
    }
    ci::storageitemremoved(c, aid, index, 0);
    ci::dropitem(c, aid, index, 0);
}

// storage_storageget / storage_storagegettocart(sd, &sd->premiumStorage, …) (premium storage → inventory / cart): their
// storage_canGetItem finds every slot of the empty premium storage empty (`nameid <= 0`: STORAGE_ADD_INVALID for any
// index), so nothing happens (see the parse functions).

// ---------------------------------------------------------------------------------------------- parse

fn pos(pkt: &[u8]) -> &'static [u16] {
    crate::net::packets::packet_pos(u16::from_le_bytes([pkt[0], pkt[1]]))
}
fn rd_u16(pkt: &[u8], off: usize) -> Option<u16> {
    pkt.get(off..off + 2).map(|b| u16::from_le_bytes([b[0], b[1]]))
}
fn rd_i32(pkt: &[u8], off: usize) -> Option<i32> {
    pkt.get(off..off + 4).map(|b| i32::from_le_bytes([b[0], b[1], b[2], b[3]]))
}

/// clif.cpp clif_parse_MoveToKafra.
pub fn parse_move_to_kafra(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = pc::get(c, aid) else { return };
    if pc_istrading(&p) {
        return;
    }
    let ps = pos(pkt);
    let (Some(index), Some(amount)) = (rd_u16(pkt, ps.first().copied().unwrap_or(2) as usize), rd_i32(pkt, ps.get(1).copied().unwrap_or(4) as usize)) else { return };
    let item_index = index as i32 - 2;
    if item_index < 0 || item_index as usize >= MAX_INVENTORY || amount < 1 {
        return;
    }
    if inv_get(c, aid, item_index as usize).map(|r| r.item.equip_switch != 0).unwrap_or(false) {
        if let Some(sd) = unit::get(c, aid) {
            ci::msg(c, &sd, ci::MSI_SWAP_EQUIPITEM_UNREGISTER_FIRST);
        }
        return;
    }
    match p.storage_flag {
        1 => storage_storageadd(c, aid, item_index, amount),
        2 => crate::guild_storage::move_to_kafra(c, aid, item_index, amount),
        3 => premium_storageadd(c, aid, item_index, amount),
        _ => {}
    }
}

/// clif.cpp clif_parse_MoveFromKafra.
pub fn parse_move_from_kafra(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = pc::get(c, aid) else { return };
    let ps = pos(pkt);
    let (Some(index), Some(amount)) = (rd_u16(pkt, ps.first().copied().unwrap_or(2) as usize), rd_i32(pkt, ps.get(1).copied().unwrap_or(4) as usize)) else { return };
    let item_index = index as i32 - 1;
    match p.storage_flag {
        1 => storage_storageget(c, aid, item_index, amount, false),
        2 => crate::guild_storage::move_from_kafra(c, aid, item_index, amount),
        3 => {} // storage_storageget(sd, &sd->premiumStorage, …): nothing in the empty premium storage
        _ => {}
    }
}

/// clif.cpp clif_parse_MoveToKafraFromCart (CZ_MOVE_ITEM_FROM_CART_TO_STORE).
pub fn parse_move_to_kafra_from_cart(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = pc::get(c, aid) else { return };
    let Some(sd) = unit::get(c, aid) else { return };
    if p.vending || !pc_iscarton(c, aid) || crate::npc::mapflag(c, sd.map, MF_NOUSECART) {
        return;
    }
    let (Some(index), Some(amount)) = (rd_u16(pkt, 2), rd_i32(pkt, 4)) else { return };
    let idx = index.wrapping_sub(2) as i32;
    match p.storage_flag {
        1 => storage_storageaddfromcart(c, aid, idx, amount),
        2 => crate::guild_storage::move_to_kafra_from_cart(c, aid, idx, amount),
        3 => premium_storageaddfromcart(c, aid, idx, amount),
        _ => {}
    }
}

/// clif.cpp clif_parse_MoveFromKafraToCart (CZ_MOVE_ITEM_FROM_STORE_TO_CART).
pub fn parse_move_from_kafra_to_cart(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = pc::get(c, aid) else { return };
    let Some(sd) = unit::get(c, aid) else { return };
    if p.vending || !pc_iscarton(c, aid) || crate::npc::mapflag(c, sd.map, MF_NOUSECART) {
        return;
    }
    let (Some(index), Some(amount)) = (rd_u16(pkt, 2), rd_i32(pkt, 4)) else { return };
    let idx = index.wrapping_sub(1) as i32;
    match p.storage_flag {
        1 => storage_storagegettocart(c, aid, idx, amount),
        2 => crate::guild_storage::move_from_kafra_to_cart(c, aid, idx, amount),
        3 => {} // storage_storagegettocart(sd, &sd->premiumStorage, …): nothing in the empty premium storage
        _ => {}
    }
}

/// clif.cpp clif_parse_CloseKafra.
pub fn parse_close_kafra(c: &Ctx, aid: u32) {
    match pc::get(c, aid).map(|p| p.storage_flag).unwrap_or(0) {
        1 => storage_storageclose(c, aid),
        2 => crate::guild_storage::storage_guild_storageclose(c, aid),
        3 => storage_premium_storage_close(c, aid),
        _ => {}
    }
}
