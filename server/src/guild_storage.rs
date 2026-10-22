//! map/storage.cpp's guild storage (guild_storage_db, storage_guild_*), its log (storage_guild_log /
//! storage_guild_log_read → clif_guild_storage_log 0x09da), char/int_storage.cpp's guild parts (the `guild_storage`
//! SQL table: mapif_load_guild_storage / mapif_parse_SaveGuildStorage with char_memitemdata_to_sql /
//! char_memitemdata_from_sql) and the BOUND_ITEMS guild-bound item retrieval (guild_retrieveitembound,
//! intif_itembound_guild_retrieve / mapif_parse_itembound_retrieve / intif_parse_itembound_store2gstorage).
//!
//! Two copies exist, as in rAthena: the map server's guild_storage_db (`guild_storage` header + `guild_storage_item`
//! slots, filled from the char server's answer and written back with intif_send_guild_storage) and the SQL table
//! (`guild_storage_sql`, rows in SQL id order). OFFICIAL_GUILD_STORAGE is on (PACKETVER >= 20131223): the capacity is
//! 100 + GD_GUILD_STORAGE level * 100, a guild without the skill has none.

use spacetimedb::{table, Table};

use crate::clif_items as ci;
use crate::db::items as itemdb;
use crate::guild;
use crate::int_guild::*;
use crate::intif::{self, Inter};
use crate::inventory::{self, Item, MAX_AMOUNT, MAX_INVENTORY};
use crate::pc;
use crate::unit;
use crate::world::Ctx;

/// mmo.hpp MAX_GUILD_STORAGE.
pub const MAX_GUILD_STORAGE: usize = 600;
/// packets.hpp MAX_GUILD_STORAGE_LOG_PACKET: (INT16_MAX - sizeof(PACKET_ZC_ACK_GUILDSTORAGE_LOG)) /
/// sizeof(PACKET_ZC_ACK_GUILDSTORAGE_LOG_sub) = (32767 - 8) / 93.
pub const MAX_GUILD_STORAGE_LOG_PACKET: usize = (32767 - 8) / 93;

/// storage.hpp e_guild_storage_result (storage_guild_storageopen).
pub const GSTORAGE_OPEN: i32 = 0;
pub const GSTORAGE_STORAGE_ALREADY_OPEN: i32 = 1;
pub const GSTORAGE_ALREADY_OPEN: i32 = 2;
pub const GSTORAGE_NO_GUILD: i32 = 3;
pub const GSTORAGE_NO_STORAGE: i32 = 4;
pub const GSTORAGE_NO_PERMISSION: i32 = 5;

/// storage.hpp e_guild_storage_log.
pub const GUILDSTORAGE_LOG_SUCCESS: u16 = 0;
pub const GUILDSTORAGE_LOG_FINAL_SUCCESS: u16 = 1;
pub const GUILDSTORAGE_LOG_EMPTY: u16 = 2;
pub const GUILDSTORAGE_LOG_FAILED: u16 = 3;

/// storage.cpp guild_storage_db's struct s_storage (the header; the items are `guild_storage_item` rows).
#[table(accessor = guild_storage)]
#[derive(Clone, Debug)]
pub struct GuildStorage {
    #[primary_key]
    pub guild_id: u32,
    pub dirty: bool,
    /// Open (by one member).
    pub status: bool,
    pub amount: u16,
    /// Locked while guild-bound items are being retrieved.
    pub lock: bool,
    pub max_amount: u16,
}

/// s_storage::u.items_guild[idx].
#[table(accessor = guild_storage_item, index(accessor = by_guild_idx, btree(columns = [guild_id, idx])))]
#[derive(Clone, Debug)]
pub struct GuildStorageItem {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    #[index(btree)]
    pub guild_id: u32,
    pub idx: u16,
    pub item: Item,
}

/// rAthena `guild_storage` table (the char server's copy; `sql_id` is its AUTO_INCREMENT id).
#[table(accessor = guild_storage_sql)]
#[derive(Clone, Debug)]
pub struct GuildStorageSql {
    #[primary_key]
    pub sql_id: u32,
    #[index(btree)]
    pub guild_id: u32,
    pub item: Item,
}

/// rAthena `guild_storage_log` table (map server SQL; `time` is NOW() as unix seconds).
#[table(accessor = guild_storage_log)]
#[derive(Clone, Debug)]
pub struct GuildStorageLog {
    #[primary_key]
    pub id: u32,
    #[index(btree)]
    pub guild_id: u32,
    pub time: i64,
    pub char_id: u32,
    pub name: String,
    pub amount: i32,
    pub item: Item,
}

// ------------------------------------------------------------------------------------------------ guild_storage_db

/// storage.cpp guild2storage2: the storage if the map server has it.
pub fn guild2storage2(c: &Ctx, guild_id: i32) -> Option<GuildStorage> {
    if guild_id <= 0 {
        return None;
    }
    c.db().guild_storage().guild_id().find(guild_id as u32)
}

/// storage.cpp guild2storage: the storage of an existing guild, created empty (max_amount 0) when not loaded.
pub fn guild2storage(c: &Ctx, guild_id: i32) -> Option<GuildStorage> {
    guild::guild_search(c, guild_id)?;
    if let Some(s) = guild2storage2(c, guild_id) {
        return Some(s);
    }
    let s = GuildStorage { guild_id: guild_id as u32, dirty: false, status: false, amount: 0, lock: false, max_amount: 0 };
    c.db().guild_storage().insert(s.clone());
    Some(s)
}

fn put_stor(c: &Ctx, s: &GuildStorage) {
    if c.db().guild_storage().guild_id().find(s.guild_id).is_some() {
        c.db().guild_storage().guild_id().update(s.clone());
    } else {
        c.db().guild_storage().insert(s.clone());
    }
}

/// The storage's items as (slot, item), slot order.
pub fn items_of(c: &Ctx, guild_id: u32) -> Vec<(usize, Item)> {
    let mut v: Vec<GuildStorageItem> = c.db().guild_storage_item().guild_id().filter(guild_id).collect();
    v.sort_by_key(|r| r.idx);
    v.into_iter().map(|r| (r.idx as usize, r.item)).collect()
}

fn slot(c: &Ctx, guild_id: u32, idx: usize) -> Option<GuildStorageItem> {
    c.db().guild_storage_item().by_guild_idx().filter((guild_id, idx as u16)).next()
}

fn clear_items(c: &Ctx, guild_id: u32) {
    for id in c.db().guild_storage_item().guild_id().filter(guild_id).map(|r| r.id).collect::<Vec<_>>() {
        c.db().guild_storage_item().id().delete(id);
    }
}

/// storage.cpp storage_guild_delete (guild_broken).
pub fn storage_guild_delete(c: &Ctx, guild_id: i32) {
    if guild_id <= 0 {
        return;
    }
    clear_items(c, guild_id as u32);
    c.db().guild_storage().guild_id().delete(guild_id as u32);
}

// ------------------------------------------------------------------------------------------------ storage.cpp

/// storage.cpp storage_guild_storageopen.
pub fn storage_guild_storageopen(c: &Ctx, aid: u32) -> i32 {
    let gid = guild::guild_id_of(c, aid);
    if gid <= 0 {
        return GSTORAGE_NO_GUILD;
    }
    // OFFICIAL_GUILD_STORAGE (sd->guild->guild)
    let level = guild::sd_guild(c, aid).map(|g| guild::guild_checkskill(&g.guild, GD_GUILD_STORAGE)).unwrap_or(0);
    if level == 0 {
        return GSTORAGE_NO_STORAGE; // Can't open storage if the guild has not learned the skill
    }
    let max = (100 + level * 100) as u16; // Lv1..5 => 200..600
    let Some(p) = pc::get(c, aid) else { return GSTORAGE_NO_GUILD };
    if p.storage_flag == 2 {
        return GSTORAGE_ALREADY_OPEN; // Guild storage already open.
    } else if p.storage_flag != 0 {
        return GSTORAGE_STORAGE_ALREADY_OPEN; // Can't open both storages at a time.
    }
    // PACKETVER >= 20140205: the position needs the storage right
    let pos = guild::guild_getposition(c, aid);
    let allowed = pos >= 0 && guild::sd_guild(c, aid).map(|g| g.guild.position[pos as usize].mode & GUILD_PERM_STORAGE != 0).unwrap_or(false);
    if !allowed {
        return GSTORAGE_NO_PERMISSION; // Guild member doesn't have permission
    }
    if !inventory::pc_can_give_items(c, aid) {
        // check is this GM level can open guild storage and store items [Lupus]
        if let Some(sd) = unit::get(c, aid) {
            crate::clif::displaymessage(c, &sd, crate::data::msg_txt(246)); // Your GM level doesn't authorize you to perform this action.
        }
        return GSTORAGE_ALREADY_OPEN;
    }
    let gstor = guild2storage2(c, gid);
    let Some(mut gstor) = gstor.filter(|s| s.max_amount == max) else {
        intif::send(Inter::LoadGuildStorage { account_id: aid, guild_id: gid });
        return GSTORAGE_OPEN;
    };
    if gstor.status || gstor.lock {
        return GSTORAGE_ALREADY_OPEN;
    }
    gstor.status = true;
    put_stor(c, &gstor);
    let mut p = pc::get(c, aid).unwrap_or(p);
    p.storage_flag = 2;
    pc::put(c, &p);
    crate::storage::storage_sortitem(); // (client_sort_storage: no)
    ci::storagelist(c, aid, &items_of(c, gstor.guild_id), "Guild Storage");
    ci::updatestorageamount(c, aid, gstor.amount, gstor.max_amount);
    GSTORAGE_OPEN
}

/// storage.cpp storage_guild_log: one row of `guild_storage_log` (amount < 0: taken out).
fn storage_guild_log(c: &Ctx, aid: u32, item: &Item, amount: i16) {
    let Some(ch) = pc::character_of(c, aid) else { return };
    let id = crate::ids::next(c.rc, "guild_storage_log", 1);
    c.db().guild_storage_log().insert(GuildStorageLog {
        id,
        guild_id: ch.guild_id,
        time: c.tick() / 1000, // NOW()
        char_id: ch.id,
        name: ch.name.clone(),
        amount: amount as i32,
        item: item.clone(),
    });
}

/// storage.cpp storage_guild_log_read (+ _sub): the newest MAX_GUILD_STORAGE_LOG_PACKET entries of the guild
/// (`ORDER BY time DESC LIMIT max`), as ZC_ACK_GUILDSTORAGE_LOG. The query leaves the order of rows with the same
/// time (DATETIME, one second) to MariaDB, which reads the MyISAM table in insertion order (EXPLAIN: full scan +
/// filesort) and returned equal times oldest id first (parity scenario `storage`: two puts in one second, ids 1, 2).
pub fn storage_guild_log_read(c: &Ctx, aid: u32) -> u16 {
    let gid = guild::guild_id_of(c, aid).max(0) as u32;
    let mut log: Vec<GuildStorageLog> = c.db().guild_storage_log().guild_id().filter(gid).collect();
    log.sort_by(|a, b| b.time.cmp(&a.time).then(a.id.cmp(&b.id)));
    log.truncate(MAX_GUILD_STORAGE_LOG_PACKET);
    let ret = if log.is_empty() { GUILDSTORAGE_LOG_EMPTY } else { GUILDSTORAGE_LOG_FINAL_SUCCESS };
    clif_guild_storage_log(c, aid, &log, ret);
    ret
}

/// `YYYY-MM-DD HH:MM:SS` of a unix time (the log's DATETIME column; the module writes UTC).
fn datetime(t: i64) -> String {
    let days = t.div_euclid(86_400);
    let secs = t.rem_euclid(86_400);
    // civil from days (Howard Hinnant)
    let z = days + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z - era * 146_097;
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = doy - (153 * mp + 2) / 5 + 1;
    let m = if mp < 10 { mp + 3 } else { mp - 9 };
    let y = if m <= 2 { y + 1 } else { y };
    format!("{:04}-{:02}-{:02} {:02}:{:02}:{:02}", y, m, d, secs / 3600, secs / 60 % 60, secs % 60)
}

/// clif.cpp clif_guild_storage_log: ZC_ACK_GUILDSTORAGE_LOG (0x09da).
pub fn clif_guild_storage_log(c: &Ctx, aid: u32, log: &[GuildStorageLog], result: u16) {
    use crate::net::packets::*;
    let Some(sd) = unit::get(c, aid) else { return };
    let mut items = Vec::new();
    if result == GUILDSTORAGE_LOG_FINAL_SUCCESS {
        for e in log {
            items.push(PACKET_ZC_ACK_GUILDSTORAGE_LOG_sub {
                id: e.id,
                itemId: itemdb::client_nameid(e.item.nameid),
                amount: (if e.amount > 0 { e.amount } else { -e.amount }) as u16 as i32,
                action: (e.amount > 0) as u8, // action = true(put), false(get)
                refine: e.item.refine as i32,
                uniqueId: e.item.unique_id as i64,
                IsIdentified: e.item.identify,
                itemType: ci::itemtype(e.item.nameid) as u16,
                slot: ci::addcards(Some(&e.item)),
                name: trunc(&e.name, NAME_LENGTH),
                time: trunc(&datetime(e.time), NAME_LENGTH),
                attribute: e.item.attribute,
            });
        }
    }
    let amount = items.len() as u16;
    let p = PACKET_ZC_ACK_GUILDSTORAGE_LOG { packetType: PACKET_ZC_ACK_GUILDSTORAGE_LOG::ID as i16, PacketLength: 0, result, amount, items };
    crate::clif::send(c, &p.to_vec(), &sd, crate::clif::Target::SelfOnly);
}

/// storage.cpp storage_guild_additem: a player puts `amount` of `item` into the guild storage (true on success).
pub fn storage_guild_additem(c: &Ctx, aid: u32, stor: &mut GuildStorage, item: &Item, amount: i32) -> bool {
    if item.nameid == 0 || amount <= 0 {
        return false;
    }
    let id = itemdb::itemdb_search(item.nameid);
    if id.stack.guild_storage && amount > id.stack.amount as i32 {
        return false; // item stack limitation
    }
    let Some(sd) = unit::get(c, aid) else { return false };
    if !itemdb::itemdb_isrestricted(item.nameid, &item.cards(), pc::group_level(c, aid), 0, itemdb::itemdb_canguildstore_sub) || item.expire_time != 0 {
        // Check if item is storable. [Skotlex]
        crate::clif::displaymessage(c, &sd, crate::data::msg_txt(264));
        return false;
    }
    if (item.bound == inventory::BOUND_ACCOUNT || item.bound > inventory::BOUND_GUILD) && !inventory::pc_can_give_bounded_items(c, aid) {
        crate::clif::displaymessage(c, &sd, crate::data::msg_txt(294));
        return false;
    }
    let gid = stor.guild_id;
    if id.is_stackable() {
        // Stackable
        for i in 0..stor.max_amount as usize {
            let Some(mut r) = slot(c, gid, i) else { continue };
            if crate::storage::compare_item(&r.item, item) {
                if amount > MAX_AMOUNT - r.item.amount as i32 || (id.stack.guild_storage && amount > id.stack.amount as i32 - r.item.amount as i32) {
                    return false;
                }
                r.item.amount += amount as i16;
                c.db().guild_storage_item().id().update(r.clone());
                ci::storageitemadded(c, aid, &r.item, i, amount);
                stor.dirty = true;
                put_stor(c, stor);
                storage_guild_log(c, aid, &r.item, amount as i16);
                return true;
            }
        }
    }
    // Add item
    let Some(i) = (0..stor.max_amount as usize).find(|&i| slot(c, gid, i).map(|r| r.item.nameid == 0).unwrap_or(true)) else { return false };
    let mut ni = item.clone();
    ni.amount = amount as i16;
    if let Some(old) = slot(c, gid, i) {
        c.db().guild_storage_item().id().delete(old.id);
    }
    c.db().guild_storage_item().insert(GuildStorageItem { id: 0, guild_id: gid, idx: i as u16, item: ni.clone() });
    stor.amount += 1;
    ci::storageitemadded(c, aid, &ni, i, amount);
    ci::updatestorageamount(c, aid, stor.amount, stor.max_amount);
    stor.dirty = true;
    put_stor(c, stor);
    storage_guild_log(c, aid, &ni, amount as i16);
    true
}

/// storage.cpp storage_guild_additem2: an item goes into the storage without a player (bound-item retrieval).
pub fn storage_guild_additem2(c: &Ctx, stor: &mut GuildStorage, item: &Item, amount: i32) -> bool {
    if item.nameid == 0 || amount <= 0 {
        return false;
    }
    if itemdb::itemdb_exists(item.nameid).is_none() || item.expire_time != 0 {
        return false;
    }
    let id = itemdb::itemdb_search(item.nameid);
    let gid = stor.guild_id;
    let mut amount = amount;
    if id.is_stackable() {
        // Stackable
        for i in 0..stor.max_amount as usize {
            let Some(mut r) = slot(c, gid, i) else { continue };
            if crate::storage::compare_item(&r.item, item) {
                // Set the amount, make it fit with max amount
                let cap = if id.stack.guild_storage { id.stack.amount as i32 } else { MAX_AMOUNT };
                amount = amount.min(cap - r.item.amount as i32);
                if amount != item.amount as i32 {
                    log::warn!("storage_guild_additem2: Stack limit reached! Altered amount of item \"{}\" ({}). '{}' -> '{}'.", id.name, item.nameid, item.amount, amount);
                }
                r.item.amount += amount as i16;
                c.db().guild_storage_item().id().update(r);
                stor.dirty = true;
                put_stor(c, stor);
                return true;
            }
        }
    }
    // Add the item
    let Some(i) = (0..stor.max_amount as usize).find(|&i| slot(c, gid, i).map(|r| r.item.nameid == 0).unwrap_or(true)) else { return false };
    let mut ni = item.clone();
    ni.amount = amount as i16;
    if let Some(old) = slot(c, gid, i) {
        c.db().guild_storage_item().id().delete(old.id);
    }
    c.db().guild_storage_item().insert(GuildStorageItem { id: 0, guild_id: gid, idx: i as u16, item: ni });
    stor.amount += 1;
    stor.dirty = true;
    put_stor(c, stor);
    true
}

/// storage.cpp storage_guild_delitem.
pub fn storage_guild_delitem(c: &Ctx, aid: u32, stor: &mut GuildStorage, n: usize, amount: i32) -> bool {
    let gid = stor.guild_id;
    let Some(mut r) = slot(c, gid, n) else { return false };
    if r.item.nameid == 0 || (r.item.amount as i32) < amount {
        return false;
    }
    // Log before removing it
    storage_guild_log(c, aid, &r.item, (-amount) as i16);
    r.item.amount -= amount as i16;
    if r.item.amount == 0 {
        c.db().guild_storage_item().id().delete(r.id);
        stor.amount = stor.amount.saturating_sub(1);
        ci::updatestorageamount(c, aid, stor.amount, stor.max_amount);
    } else {
        c.db().guild_storage_item().id().update(r);
    }
    ci::storageitemremoved(c, aid, n as i32, amount as u32);
    stor.dirty = true;
    put_stor(c, stor);
    true
}

/// storage.cpp storage_guild_storageadd: inventory → guild storage.
pub fn storage_guild_storageadd(c: &Ctx, aid: u32, index: i32, amount: i32) {
    let Some(mut stor) = guild2storage2(c, guild::guild_id_of(c, aid)) else { return };
    if !stor.status || stor.amount > stor.max_amount {
        return;
    }
    if index < 0 || index as usize >= MAX_INVENTORY {
        return;
    }
    let Some(it) = inventory::inv_get(c, aid, index as usize).map(|r| r.item).filter(|it| it.nameid != 0) else { return };
    if amount < 1 || amount > it.amount as i32 {
        return;
    }
    if itemdb::itemdb_ishatched_egg(it.card0, it.attribute) {
        return;
    }
    if stor.lock {
        storage_guild_storageclose(c, aid);
        return;
    }
    if storage_guild_additem(c, aid, &mut stor, &it, amount) {
        inventory::pc_delitem(c, aid, index, amount, 0, 4, crate::map_log::LOG_TYPE_GSTORAGE);
    } else {
        ci::storageitemremoved(c, aid, index, 0);
        ci::dropitem(c, aid, index, 0);
    }
}

/// storage.cpp storage_guild_storageget: guild storage → inventory.
pub fn storage_guild_storageget(c: &Ctx, aid: u32, index: i32, amount: i32, favorite: bool) {
    let Some(mut stor) = guild2storage2(c, guild::guild_id_of(c, aid)) else { return };
    if !stor.status {
        return;
    }
    if index < 0 || index >= stor.max_amount as i32 {
        return;
    }
    let Some(r) = slot(c, stor.guild_id, index as usize).filter(|r| r.item.nameid != 0) else { return };
    if amount < 1 || amount > r.item.amount as i32 {
        return;
    }
    if stor.lock {
        storage_guild_storageclose(c, aid);
        return;
    }
    let mut it = r.item.clone();
    let flag = inventory::pc_additem_fav(c, aid, &mut it, amount, crate::map_log::LOG_TYPE_GSTORAGE, favorite);
    // pc_additem may have given the stored item a unique id
    if it.unique_id != r.item.unique_id {
        if let Some(mut r2) = slot(c, stor.guild_id, index as usize) {
            r2.item.unique_id = it.unique_id;
            c.db().guild_storage_item().id().update(r2);
        }
    }
    if flag == 0 {
        storage_guild_delitem(c, aid, &mut stor, index as usize, amount);
    } else {
        // inform fail
        ci::storageitemremoved(c, aid, index, 0);
        ci::additem(c, aid, 0, 0, flag);
    }
}

/// storage.cpp storage_guild_storageaddfromcart: cart → guild storage.
pub fn storage_guild_storageaddfromcart(c: &Ctx, aid: u32, index: i32, amount: i32) {
    let Some(mut stor) = guild2storage2(c, guild::guild_id_of(c, aid)) else { return };
    if !stor.status || stor.amount > stor.max_amount {
        return;
    }
    if index < 0 || index as usize >= inventory::MAX_CART {
        return;
    }
    let Some(it) = crate::cart::cart_get(c, aid, index as usize).map(|r| r.item).filter(|it| it.nameid != 0) else { return };
    if amount < 1 || amount > it.amount as i32 {
        return;
    }
    if storage_guild_additem(c, aid, &mut stor, &it, amount) {
        crate::cart::pc_cart_delitem(c, aid, index, amount, 0, crate::map_log::LOG_TYPE_GSTORAGE);
    } else {
        ci::storageitemremoved(c, aid, index, 0);
        ci::dropitem(c, aid, index, 0);
    }
}

/// storage.cpp storage_guild_storagegettocart: guild storage → cart.
pub fn storage_guild_storagegettocart(c: &Ctx, aid: u32, index: i32, amount: i32) {
    let Some(mut stor) = guild2storage2(c, guild::guild_id_of(c, aid)) else { return };
    if !stor.status {
        return;
    }
    if index < 0 || index >= stor.max_amount as i32 {
        return;
    }
    let Some(r) = slot(c, stor.guild_id, index as usize).filter(|r| r.item.nameid != 0) else { return };
    if amount < 1 || amount > r.item.amount as i32 {
        return;
    }
    let flag = crate::cart::pc_cart_additem(c, aid, &r.item, amount, crate::map_log::LOG_TYPE_GSTORAGE);
    if flag == 0 {
        storage_guild_delitem(c, aid, &mut stor, index as usize, amount);
    } else {
        ci::storageitemremoved(c, aid, index, 0);
        ci::cart_additem_ack(c, aid, if flag == inventory::ADDITEM_INVALID { 0 } else { 1 });
    }
}

/// storage.cpp storage_guild_storagesave: sends the storage to the char server when it changed (flag & CSAVE_QUIT
/// closes it). False when the map server has no storage for the guild.
pub fn storage_guild_storagesave(c: &Ctx, account_id: u32, guild_id: i32, quit: bool) -> bool {
    let Some(mut stor) = guild2storage2(c, guild_id) else { return false };
    if quit {
        // Char quitting, close it.
        stor.status = false;
        put_stor(c, &stor);
    }
    if stor.dirty {
        // intif_send_guild_storage (the whole s_storage: items_guild[] in slot order)
        let items: Vec<Item> = slots_array(c, stor.guild_id);
        intif::send(Inter::SaveGuildStorage { account_id, guild_id, items });
    }
    true
}

/// items_guild[0 .. MAX_GUILD_STORAGE] (empty slots are zero items).
fn slots_array(c: &Ctx, guild_id: u32) -> Vec<Item> {
    let mut v = vec![Item::default(); MAX_GUILD_STORAGE];
    for (i, it) in items_of(c, guild_id) {
        if i < MAX_GUILD_STORAGE {
            v[i] = it;
        }
    }
    v
}

/// storage.cpp storage_guild_storagesaved: the char server saved it (a closed storage is clean again).
pub fn storage_guild_storagesaved(c: &Ctx, guild_id: i32) {
    if let Some(mut stor) = guild2storage2(c, guild_id) {
        if stor.dirty && !stor.status {
            // Storage has been correctly saved.
            stor.dirty = false;
            put_stor(c, &stor);
        }
    }
}

/// chrif_save(sd, CSAVE_INVENTORY | CSAVE_CART) as storage_guild_storageclose / _quit call it (save_settings has
/// CHARSAVE_STORAGE): it also saves the guild storage while it is open (storage_flag 2; chrif.rs).
fn chrif_save_storage(c: &Ctx, aid: u32) {
    crate::chrif::chrif_save(c, aid, crate::chrif::CSAVE_INVENTORY | crate::chrif::CSAVE_CART);
}

/// storage.cpp storage_guild_storageclose.
pub fn storage_guild_storageclose(c: &Ctx, aid: u32) {
    let Some(stor) = guild2storage2(c, guild::guild_id_of(c, aid)) else { return };
    ci::storageclose(c, aid);
    if stor.status {
        if crate::storage::CHARSAVE_STORAGE {
            chrif_save_storage(c, aid); // This one also saves the storage. [Skotlex]
        } else {
            storage_guild_storagesave(c, aid, stor.guild_id as i32, false);
        }
        if let Some(mut s) = guild2storage2(c, stor.guild_id as i32) {
            s.status = false;
            put_stor(c, &s);
        }
    }
    if let Some(mut p) = pc::get(c, aid) {
        p.storage_flag = 0;
        pc::put(c, &p);
    }
}

/// storage.cpp storage_guild_storage_quit: flag 1 (a guild break) closes without saving.
pub fn storage_guild_storage_quit(c: &Ctx, aid: u32, flag: i32) {
    let Some(stor) = guild2storage2(c, guild::guild_id_of(c, aid)) else { return };
    if flag != 0 {
        // Only during a guild break flag is 1 (don't save storage)
        ci::storageclose(c, aid);
        if crate::storage::CHARSAVE_STORAGE {
            chrif_save_storage(c, aid);
        }
        if let Some(mut p) = pc::get(c, aid) {
            p.storage_flag = 0;
            pc::put(c, &p);
        }
        if let Some(mut s) = guild2storage2(c, stor.guild_id as i32) {
            s.status = false;
            put_stor(c, &s);
        }
        return;
    }
    if stor.status {
        if crate::storage::CHARSAVE_STORAGE {
            chrif_save_storage(c, aid);
        } else {
            storage_guild_storagesave(c, aid, stor.guild_id as i32, true);
        }
    }
    if let Some(mut p) = pc::get(c, aid) {
        p.storage_flag = 0;
        pc::put(c, &p);
    }
    if let Some(mut s) = guild2storage2(c, stor.guild_id as i32) {
        s.status = false;
        put_stor(c, &s);
    }
}

/// clif.cpp clif_refresh_storagewindow's guild part (storage_flag 2): the window again, or the storage requested.
pub fn refresh_storagewindow(c: &Ctx, aid: u32) {
    let gid = guild::guild_id_of(c, aid);
    match guild2storage2(c, gid) {
        None => intif::send(Inter::LoadGuildStorage { account_id: aid, guild_id: gid }),
        Some(g) => {
            ci::storagelist(c, aid, &items_of(c, g.guild_id), "Guild Storage");
            ci::updatestorageamount(c, aid, g.amount, g.max_amount);
        }
    }
}

// ------------------------------------------------------------------------------------------------ intif / char server

/// int_storage.cpp mapif_parse_LoadGuildStorage → mapif_load_guild_storage (flag 1: open it): the guild must exist in
/// SQL; guild_storage_fromsql loads the rows in id order into slots 0.. (at most inter_guild_storagemax).
pub fn mapif_parse_load_guild_storage(c: &Ctx, account_id: u32, guild_id: i32) {
    if guild_id > 0 && c.db().guild_sql().guild_id().find(guild_id as u32).is_some() {
        // guild exists
        let max = inter_guild_storagemax(c, guild_id);
        let mut rows: Vec<GuildStorageSql> = c.db().guild_storage_sql().guild_id().filter(guild_id as u32).collect();
        rows.sort_by_key(|r| r.sql_id);
        let items: Vec<Item> = rows.into_iter().take(max as usize).map(|r| r.item).collect();
        log::info!("Loaded Guild Storage data from table guild_storage for guild_id: {guild_id} (total: {})", items.len());
        intif::send(Inter::LoadGuildStorageAck { account_id, guild_id, flag: 1, storage: Some((max, items)) });
        return;
    }
    // guild does not exist
    intif::send(Inter::LoadGuildStorageAck { account_id, guild_id: 0, flag: 0, storage: None });
}

/// int_storage.cpp mapif_parse_SaveGuildStorage (guild_storage_tosql = char_memitemdata_to_sql over
/// inter_guild_storagemax slots) and mapif_save_guild_storage_ack.
pub fn mapif_parse_save_guild_storage(c: &Ctx, account_id: u32, guild_id: i32, items: Vec<Item>) {
    if guild_id > 0 && c.db().guild_sql().guild_id().find(guild_id as u32).is_some() {
        // guild exists
        let max = inter_guild_storagemax(c, guild_id) as usize;
        guild_storage_tosql(c, guild_id as u32, &items[..items.len().min(max)]);
        intif::send(Inter::SaveGuildStorageAck { account_id, guild_id, fail: 0 });
        return;
    }
    intif::send(Inter::SaveGuildStorageAck { account_id, guild_id, fail: 1 });
}

/// char.cpp char_memitemdata_to_sql for TABLE_GUILD_STORAGE: every SQL row (id order) is matched with the first
/// unmatched slot holding the same nameid, card0, card2, card3 and unique_id (updated if another field differs);
/// rows without a slot are deleted; the slots left over are inserted in slot order.
fn guild_storage_tosql(c: &Ctx, guild_id: u32, items: &[Item]) {
    let mut rows: Vec<GuildStorageSql> = c.db().guild_storage_sql().guild_id().filter(guild_id).collect();
    rows.sort_by_key(|r| r.sql_id);
    let mut flag = vec![false; items.len()];
    for row in rows {
        let found = items.iter().enumerate().find(|(i, it)| {
            !flag[*i] && it.nameid != 0 && it.nameid == row.item.nameid && it.card0 == row.item.card0 && it.card2 == row.item.card2 && it.card3 == row.item.card3 && it.unique_id == row.item.unique_id
        });
        match found {
            Some((i, it)) => {
                if *it != row.item {
                    c.db().guild_storage_sql().sql_id().update(GuildStorageSql { item: it.clone(), ..row });
                }
                flag[i] = true;
            }
            None => {
                // Item not present in inventory, remove it.
                c.db().guild_storage_sql().sql_id().delete(row.sql_id);
            }
        }
    }
    for (i, it) in items.iter().enumerate() {
        if it.nameid == 0 || flag[i] {
            continue;
        }
        let sql_id = crate::ids::next(c.rc, "guild_storage_sql", 1);
        c.db().guild_storage_sql().insert(GuildStorageSql { sql_id, guild_id, item: it.clone() });
    }
    log::info!("Saved Guild Storage (0) data to table guild_storage for guild_id: {guild_id}");
}

/// int_guild.cpp mapif_parse_BreakGuild's `DELETE FROM guild_storage WHERE guild_id`.
pub fn sql_delete_guild_storage(c: &Ctx, guild_id: u32) {
    for id in c.db().guild_storage_sql().guild_id().filter(guild_id).map(|r| r.sql_id).collect::<Vec<_>>() {
        c.db().guild_storage_sql().sql_id().delete(id);
    }
}

/// intif.cpp intif_parse_LoadGuildStorage: the storage arrived (flag 1: open it for the player).
pub fn intif_parse_load_guild_storage(c: &Ctx, account_id: u32, guild_id: i32, flag: u8, storage: Option<(u16, Vec<Item>)>) {
    if guild_id <= 0 {
        return;
    }
    let sd = pc::get(c, account_id).map(|_| account_id);
    if flag != 0 && sd.is_none() {
        log::error!("intif_parse_LoadGuildStorage: user not found (AID: {account_id})");
        return;
    }
    let Some(gstor) = guild2storage(c, guild_id) else {
        log::warn!("intif_parse_LoadGuildStorage: error guild_id {guild_id} not exist");
        return;
    };
    if gstor.status {
        // Already open.. lets ignore this update
        log::warn!("intif_parse_LoadGuildStorage: storage received for a client already open (User {account_id})");
        return;
    }
    if gstor.dirty {
        // Already have storage, and it has been modified and not saved yet! Exploit! [Skotlex]
        log::warn!("intif_parse_LoadGuildStorage: received storage for an already modified non-saved storage! (User {account_id})");
        return;
    }
    let Some((max, items)) = storage else { return };
    // memcpy(gstor, the char server's s_storage): a fresh storage (not dirty, closed, unlocked)
    clear_items(c, gstor.guild_id);
    for (i, it) in items.iter().enumerate() {
        c.db().guild_storage_item().insert(GuildStorageItem { id: 0, guild_id: gstor.guild_id, idx: i as u16, item: it.clone() });
    }
    put_stor(c, &GuildStorage { guild_id: gstor.guild_id, dirty: false, status: false, amount: items.len() as u16, lock: false, max_amount: max });
    if let Some(aid) = sd.filter(|_| flag != 0) {
        storage_guild_storageopen(c, aid);
    }
}

// ------------------------------------------------------------------------------------------------ BOUND_ITEMS

/// pc.cpp pc_bound_chk: the inventory slots holding items bound with `kind`.
pub fn pc_bound_chk(c: &Ctx, aid: u32, kind: u8) -> Vec<usize> {
    let mut v: Vec<usize> = inventory::inv_list(c, aid).into_iter().filter(|r| r.item.nameid > 0 && r.item.amount > 0 && r.item.bound == kind).map(|r| r.idx as usize).collect();
    v.sort_unstable();
    v
}

/// guild.cpp guild_retrieveitembound: a member leaving the guild gives its guild-bound items to the guild storage
/// (online: at once; offline: the char server takes them out of the inventory table).
pub fn guild_retrieveitembound(c: &Ctx, char_id: u32, account_id: u32, guild_id: i32) {
    if let Some(su) = pc::charid2sd(c, char_id) {
        // Character is online
        let aid = su.id;
        let idxlist = pc_bound_chk(c, aid, inventory::BOUND_GUILD);
        if idxlist.is_empty() {
            return;
        }
        let stor = guild2storage(c, guild::guild_id_of(c, aid));
        if stor.as_ref().map(|s| s.status).unwrap_or(false) {
            // Someone is in guild storage, close them
            if let Some(g) = guild::guild_search(c, guild_id) {
                for i in 0..g.guild.max() {
                    let m = g.guild.member[i].sd;
                    if m != 0 && pc::get(c, m).map(|p| p.storage_flag == 2).unwrap_or(false) {
                        storage_guild_storageclose(c, m);
                    }
                }
            }
        }
        for idx in idxlist {
            // Loop the matching items, gstorage_additem takes care of opening storage
            let Some(it) = inventory::inv_get(c, aid, idx).map(|r| r.item) else { continue };
            if let Some(mut s) = stor.as_ref().and_then(|s| guild2storage2(c, s.guild_id as i32)) {
                storage_guild_additem(c, aid, &mut s, &it, it.amount as i32);
            }
            inventory::pc_delitem(c, aid, idx as i32, it.amount as i32, 0, 4, crate::map_log::LOG_TYPE_GSTORAGE);
        }
        storage_guild_storageclose(c, aid); // Close and save the storage
    } else {
        // Character is offline, ask char server to do the job
        let stor = guild2storage2(c, guild_id);
        if guild::guild_search(c, guild_id).is_none() {
            return;
        }
        if stor.as_ref().map(|s| s.status).unwrap_or(false) {
            // Someone is in guild storage, close them
            if let Some(g) = guild::guild_search(c, guild_id) {
                for i in 0..g.guild.max() {
                    let m = g.guild.member[i].sd;
                    if m != 0 && pc::get(c, m).map(|p| p.storage_flag == 2).unwrap_or(false) {
                        storage_guild_storageclose(c, m);
                    }
                }
            }
        }
        // intif_itembound_guild_retrieve: the storage is locked for the retrieval
        if let Some(mut s) = guild2storage2(c, guild_id) {
            s.lock = true;
            put_stor(c, &s);
        }
        log::info!("Request guild bound item(s) retrieval for CID = {char_id}, AID = {account_id}, Guild ID = {guild_id}.");
        mapif_parse_itembound_retrieve(c, char_id, account_id, guild_id);
    }
}

/// int_storage.cpp mapif_parse_itembound_retrieve (0x3056): the offline character's guild-bound items leave its
/// inventory table (and the look columns of equipped ones are cleared) and go to the map server's guild storage
/// (mapif_itembound_store2gstorage → intif_parse_itembound_store2gstorage); without items the storage is unlocked
/// (mapif_itembound_ack → intif_parse_itembound_ack). The guild id travels as a 16-bit word.
fn mapif_parse_itembound_retrieve(c: &Ctx, char_id: u32, account_id: u32, guild_id: i32) {
    let gid16 = guild_id as u16 as i16 as i32;
    let mut rows: Vec<inventory::InventoryItem> = inventory::inv_list_char(c, char_id).into_iter().filter(|r| r.item.bound == inventory::BOUND_GUILD).collect();
    rows.sort_by_key(|r| (r.save_id == 0, r.save_id, r.idx));
    let items: Vec<Item> = rows.iter().map(|r| r.item.clone()).take(MAX_INVENTORY).collect();
    log::info!("Found '{}' guild bound item(s) from CID = {char_id}, AID = {account_id}, Guild ID = {guild_id}.", items.len());
    if items.is_empty() {
        // mapif_itembound_ack: unlock the storage
        if let Some(mut s) = guild2storage2(c, gid16) {
            s.lock = false;
            put_stor(c, &s);
        }
        return;
    }
    // Delete bound items from player's inventory
    for r in rows.iter() {
        c.db().inventory().id().delete(r.id);
    }
    // Verifies equip bitmasks and clears the look columns of the equipped ones
    if let Some(mut ch) = c.db().character().id().find(char_id) {
        use crate::inventory::*;
        for it in items.iter().filter(|it| it.equip != 0) {
            let e = it.equip;
            if e & EQP_HAND_R != 0 {
                ch.weapon = 0;
            }
            if e & EQP_HAND_L != 0 {
                ch.shield = 0;
            }
            if e & (EQP_HEAD_TOP | EQP_COSTUME_HEAD_TOP) != 0 {
                ch.head_top = 0;
            }
            if e & (EQP_HEAD_MID | EQP_COSTUME_HEAD_MID) != 0 {
                ch.head_mid = 0;
            }
            if e & (EQP_HEAD_LOW | EQP_COSTUME_HEAD_LOW) != 0 {
                ch.head_bottom = 0;
            }
            if e & (EQP_GARMENT | EQP_COSTUME_GARMENT) != 0 {
                ch.robe = 0;
            }
        }
        c.db().character().id().update(ch);
    }
    // intif_parse_itembound_store2gstorage
    let Some(mut gstor) = guild2storage(c, gid16) else {
        log::error!("intif_parse_itembound_store2gstorage: Guild '{gid16}' not found.");
        return;
    };
    let mut failed = 0;
    for it in items.iter() {
        if !storage_guild_additem2(c, &mut gstor, it, it.amount as i32) {
            failed += 1;
        }
        gstor = guild2storage2(c, gid16).unwrap_or(gstor);
    }
    log::info!("Retrieved '{}' (failed: {failed}) guild bound item(s) for Guild ID = {gid16}.", items.len());
    gstor.lock = false;
    gstor.status = false;
    put_stor(c, &gstor);
}

use crate::character::character;
use crate::inventory::inventory as _;

// ------------------------------------------------------------------------------------------------ script helpers

/// gstor->u.items_guild[idx] of the guild's storage (the script item commands' TABLE_GUILD_STORAGE).
pub fn slot_item(c: &Ctx, guild_id: i32, idx: usize) -> Option<Item> {
    if guild_id <= 0 {
        return None;
    }
    slot(c, guild_id as u32, idx).map(|r| r.item)
}

/// items_guild[0 .. MAX_GUILD_STORAGE] of the guild's storage.
pub fn slots_array_of(c: &Ctx, guild_id: i32) -> Vec<Item> {
    if guild_id <= 0 {
        return vec![Item::default(); MAX_GUILD_STORAGE];
    }
    slots_array(c, guild_id as u32)
}

/// gstor->lock = on.
pub fn set_lock(c: &Ctx, guild_id: i32, on: bool) {
    if let Some(mut s) = guild2storage2(c, guild_id) {
        s.lock = on;
        put_stor(c, &s);
    }
}

/// script.cpp buildin_delitem_delete for TABLE_GUILD_STORAGE: the storage is locked, the item deleted (with the
/// storage packets to the player), the storage closed for the player, then unlocked.
pub fn script_delitem(c: &Ctx, aid: u32, idx: usize, amount: i32) {
    let gid = guild::guild_id_of(c, aid);
    set_lock(c, gid, true);
    if let Some(mut s) = guild2storage2(c, gid) {
        storage_guild_delitem(c, aid, &mut s, idx, amount);
    }
    storage_guild_storageclose(c, aid);
    set_lock(c, gid, false);
}

// ------------------------------------------------------------------------------------------------ parse hooks

/// clif_parse_MoveToKafra / MoveFromKafra / MoveToKafraFromCart / MoveFromKafraToCart / CloseKafra for
/// storage_flag 2 (storage.rs routes them here).
pub fn move_to_kafra(c: &Ctx, aid: u32, item_index: i32, amount: i32) {
    storage_guild_storageadd(c, aid, item_index, amount);
}
pub fn move_from_kafra(c: &Ctx, aid: u32, item_index: i32, amount: i32) {
    storage_guild_storageget(c, aid, item_index, amount, false);
}
pub fn move_to_kafra_from_cart(c: &Ctx, aid: u32, idx: i32, amount: i32) {
    storage_guild_storageaddfromcart(c, aid, idx, amount);
}
pub fn move_from_kafra_to_cart(c: &Ctx, aid: u32, idx: i32, amount: i32) {
    storage_guild_storagegettocart(c, aid, idx, amount);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn datetime_format() {
        assert_eq!(datetime(0), "1970-01-01 00:00:00");
        assert_eq!(datetime(1_790_000_000), "2026-09-21 14:13:20");
        assert_eq!(MAX_GUILD_STORAGE_LOG_PACKET, 352);
    }
}
