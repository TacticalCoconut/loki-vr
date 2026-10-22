//! itemdb.cpp ItemGroupDatabase at run time: random entries of item groups (get_random_itemsubgroup /
//! get_random_entry) and giving a group's items to a player (pc_get_itemgroup / pc_get_itemgroup_sub). The static
//! groups are db/items/group.rs; the SHAREDPOOL algorithm's counters (s_item_group_entry::given,
//! s_item_group_random::total_given), which rAthena keeps in memory until the item db is reloaded, are the
//! `item_group_pool` table here.

use spacetimedb::{table, Table};

use crate::db::items::group::{self, ItemGroupEntry, ItemGroupRandom, SharedPool, GROUP_ALGORITHM_ALL, GROUP_ALGORITHM_RANDOM, GROUP_ALGORITHM_SHAREDPOOL, GROUP_ALGORITHM_USEDB};
use crate::db::items::{self as itemdb, CARD0_CREATE, CARD0_FORGE};
use crate::inventory::{self, Item};
use crate::world::Ctx;

/// itemdb.hpp ENCHANTGRADE_NONE / MAX_ENCHANTGRADE (RENEWAL: ENCHANTGRADE_A).
const ENCHANTGRADE_NONE: u16 = 0;
const MAX_ENCHANTGRADE: u16 = 4;

/// The SHAREDPOOL counters of one sub group (key "group:subgroup").
#[table(accessor = item_group_pool)]
#[derive(Clone, Debug)]
pub struct ItemGroupPool {
    #[primary_key]
    pub key: String,
    pub given: Vec<u16>,
    pub total_given: u32,
}

/// ItemGroupDatabase::get_random_itemsubgroup (the SHAREDPOOL counters are read from / written back to the table).
pub fn get_random_itemsubgroup<'a>(c: &Ctx, group_id: u16, random: &'a ItemGroupRandom, algorithm: u8) -> Option<&'a ItemGroupEntry> {
    let key = format!("{group_id}:{}", random.subgroup);
    let row = c.db().item_group_pool().key().find(&key);
    let mut pool = row.as_ref().map(|r| SharedPool { given: r.given.clone(), total_given: r.total_given }).unwrap_or_default();
    let mut rnd = |a: i32, b: i32| c.rnd_value(a, b);
    let e = group::get_random_itemsubgroup(random, algorithm, &mut rnd, &mut pool);
    let effective = if algorithm == GROUP_ALGORITHM_USEDB { random.algorithm } else { algorithm };
    if effective == GROUP_ALGORITHM_SHAREDPOOL {
        let r = ItemGroupPool { key: key.clone(), given: pool.given, total_given: pool.total_given };
        if row.is_some() {
            c.db().item_group_pool().key().update(r);
        } else {
            c.db().item_group_pool().insert(r);
        }
    }
    e
}

/// ItemGroupDatabase::get_random_entry.
pub fn get_random_entry(c: &Ctx, group_id: u16, sub_group: u8, algorithm: u8) -> Option<&'static ItemGroupEntry> {
    let Some(g) = group::item_group(group_id) else {
        log::error!("get_random_entry: Invalid group id {group_id}.");
        return None;
    };
    if g.random.is_empty() {
        log::error!("get_random_entry: No item entries for group id {group_id}.");
        return None;
    }
    let Some(r) = g.subgroup(sub_group as u16) else {
        log::error!("get_random_entry: No item entries for group id {group_id} and sub group {sub_group}.");
        return None;
    };
    get_random_itemsubgroup(c, group_id, r, algorithm)
}

/// ItemGroupDatabase::pc_get_itemgroup_sub.
pub fn pc_get_itemgroup_sub(c: &Ctx, aid: u32, identify: bool, data: Option<&ItemGroupEntry>) {
    let Some(data) = data else { return };
    let mut tmp = Item::new(data.nameid);
    tmp.bound = data.bound;
    tmp.identify = if identify { 1 } else { itemdb::itemdb_isidentified(data.nameid) as u8 };
    tmp.expire_time = if data.duration != 0 { (c.tick() / 1000 + data.duration as i64 * 60) as u32 } else { 0 };
    if data.is_named {
        let char_id = crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
        tmp.card0 = if itemdb::itemdb_isequip(data.nameid) { CARD0_FORGE } else { CARD0_CREATE };
        tmp.card1 = 0;
        tmp.card2 = char_id & 0xffff;
        tmp.card3 = char_id >> 16;
    }
    let get_amt: u16 = if itemdb::itemdb_isstackable(data.nameid) && data.is_stacked { data.amount } else { 1 };
    tmp.amount = get_amt as i16;
    // Do loop for non-stackable item
    let mut i: u16 = 0;
    while i < data.amount {
        tmp.unique_id = if data.guid { inventory::pc_generate_unique_id(c, aid) } else { 0 }; // Generate GUID
        if itemdb::itemdb_isequip(data.nameid) {
            let max_refine = inventory::MAX_REFINE as u16;
            tmp.refine = if data.refine_minimum > 0 && data.refine_maximum > 0 {
                c.rnd_value(data.refine_minimum as i32, data.refine_maximum as i32) as u8
            } else if data.refine_minimum > 0 {
                c.rnd_value(data.refine_minimum as i32, max_refine as i32) as u8
            } else if data.refine_maximum > 0 {
                c.rnd_value(1, data.refine_maximum as i32) as u8
            } else {
                0
            };
            tmp.enchantgrade = if data.minimum_enchantgrade > ENCHANTGRADE_NONE && data.maximum_enchantgrade > ENCHANTGRADE_NONE {
                c.rnd_value(data.minimum_enchantgrade as i32, data.maximum_enchantgrade as i32) as u8
            } else if data.minimum_enchantgrade > ENCHANTGRADE_NONE {
                c.rnd_value(data.minimum_enchantgrade as i32, MAX_ENCHANTGRADE as i32) as u8
            } else if data.maximum_enchantgrade > ENCHANTGRADE_NONE {
                c.rnd_value(ENCHANTGRADE_NONE as i32, data.maximum_enchantgrade as i32) as u8
            } else {
                ENCHANTGRADE_NONE as u8
            };
            if let Some(g) = data.random_option_group.and_then(itemdb::randomopt::random_option_group) {
                let mut rnd = |a: i32, b: i32| c.rnd_value(a, b);
                let opts = g.apply(&mut rnd);
                tmp.option = opts.iter().map(|o| inventory::ItemOption { id: o.id, value: o.value, param: o.param as u8 }).collect();
            }
        }
        let flag = inventory::pc_additem(c, aid, &mut tmp, get_amt as i32, crate::map_log::LOG_TYPE_SCRIPT);
        if flag == inventory::ADDITEM_SUCCESS {
            if data.is_announced {
                let box_id = inventory::st(c, aid).map(|s| s.itemid).unwrap_or(0); // sd.itemid
                crate::atcmd_pc::intif_broadcast_obtain_special_item(c, aid, data.nameid, box_id, crate::atcmd_pc::ITEMOBTAIN_TYPE_BOXITEM);
            }
        } else {
            crate::clif_items::additem(c, aid, 0, 0, flag);
        }
        // Amount is capped to 1..MAX_AMOUNT by the group database, so get_amt is never 0
        i = i.wrapping_add(get_amt.max(1));
    }
}

/// ItemGroupDatabase::pc_get_itemgroup: 0 success, 2 invalid group.
pub fn pc_get_itemgroup(c: &Ctx, group_id: u16, identify: bool, aid: u32) -> u8 {
    let Some(g) = group::item_group(group_id) else {
        log::error!("pc_get_itemgroup: Invalid group id '{group_id}' specified.");
        return 2;
    };
    if g.random.is_empty() {
        return 0;
    }
    for random in &g.random {
        match random.algorithm {
            GROUP_ALGORITHM_RANDOM | GROUP_ALGORITHM_SHAREDPOOL => {
                let e = get_random_itemsubgroup(c, group_id, random, GROUP_ALGORITHM_USEDB);
                pc_get_itemgroup_sub(c, aid, identify, e);
            }
            GROUP_ALGORITHM_ALL => {
                for it in &random.data {
                    pc_get_itemgroup_sub(c, aid, identify, Some(it));
                }
            }
            _ => {}
        }
    }
    0
}
