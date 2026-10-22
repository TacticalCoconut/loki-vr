//! itemdb.cpp ItemGroupDatabase (db/re/item_group_db.yml + import): IG_* groups made of sub groups, each with an
//! algorithm and indexed entries. loadingFinished drops empty sub groups and sums the rates. (The roulette table,
//! itemdb_parse_roulette_db, is roulette.rs.)

use std::collections::HashMap;
use std::sync::OnceLock;

/// itemdb.hpp e_group_algorithm_type.
pub const GROUP_ALGORITHM_USEDB: u8 = 0;
pub const GROUP_ALGORITHM_DROP: u8 = 1;
pub const GROUP_ALGORITHM_ALL: u8 = 2;
pub const GROUP_ALGORITHM_RANDOM: u8 = 3;
pub const GROUP_ALGORITHM_SHAREDPOOL: u8 = 4;

fn d_one() -> u16 {
    1
}
fn d_true() -> bool {
    true
}

/// itemdb.hpp struct s_item_group_entry (without `given`: see [`SharedPool`]).
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemGroupEntry {
    /// `Index` (the key of s_item_group_random::data).
    pub index: u32,
    /// `Item` resolved to its id.
    pub nameid: u32,
    /// `Rate` (0 for the All algorithm; entries of other algorithms need one).
    pub rate: u16,
    /// rate * battle_config.item_group_rate / 100 capped to [item_group_drop_min, item_group_drop_max]
    /// (conf/battle/drops.conf: 100, 1, 10000).
    pub adj_rate: u16,
    /// `Amount` (default 1, capped to 1..MAX_AMOUNT).
    #[serde(default = "d_one")]
    pub amount: u16,
    /// `Duration` (rental minutes, default 0).
    #[serde(default)]
    pub duration: u16,
    /// `Announced` (isAnnounced, default false).
    #[serde(default)]
    pub is_announced: bool,
    /// `UniqueId` (GUID, default: the item's Flags.UniqueId).
    #[serde(default)]
    pub guid: bool,
    /// `Stacked` (isStacked, default true).
    #[serde(default = "d_true")]
    pub is_stacked: bool,
    /// `Named` (isNamed, default false).
    #[serde(default)]
    pub is_named: bool,
    /// `Bound` (BOUND_*, default BOUND_NONE).
    #[serde(default)]
    pub bound: u8,
    /// `RandomOptionGroup` resolved to its id (see [`super::randomopt::random_option_group`]).
    #[serde(default)]
    pub random_option_group: Option<u16>,
    /// `RefineMinimum` / `RefineMaximum` (default 0, capped to MAX_REFINE).
    #[serde(default)]
    pub refine_minimum: u16,
    #[serde(default)]
    pub refine_maximum: u16,
    /// `GradeMinimum` / `GradeMaximum` (ENCHANTGRADE_*, default NONE).
    #[serde(default)]
    pub minimum_enchantgrade: u16,
    #[serde(default)]
    pub maximum_enchantgrade: u16,
}

/// itemdb.hpp struct s_item_group_random: one sub group.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemGroupRandom {
    /// `SubGroup` (the key of s_item_group_db::random).
    pub subgroup: u16,
    /// `Algorithm` (GROUP_ALGORITHM_*, default SHAREDPOOL).
    pub algorithm: u8,
    /// Sum of the entries' rates (loadingFinished).
    pub total_rate: u32,
    /// The entries, in the unordered_map's iteration order (the order getgroupitem hands out an All group).
    pub data: Vec<ItemGroupEntry>,
}

/// itemdb.hpp struct s_item_group_db.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemGroup {
    /// `Group` (IG_<Group> constant).
    pub id: u16,
    /// The non-empty sub groups, in the unordered_map's iteration order (pc_get_itemgroup's loop order).
    pub random: Vec<ItemGroupRandom>,
}

crate::dataset!(item_groups, "items_group", Vec<ItemGroup>);

static INDEX: OnceLock<HashMap<u16, usize>> = OnceLock::new();

/// itemdb_group.find.
pub fn item_group(id: u16) -> Option<&'static ItemGroup> {
    let ix = INDEX.get_or_init(|| item_groups().iter().enumerate().map(|(i, g)| (g.id, i)).collect());
    ix.get(&id).map(|&i| &item_groups()[i])
}

impl ItemGroup {
    /// The sub group `sub` (group->random[sub]).
    pub fn subgroup(&self, sub: u16) -> Option<&ItemGroupRandom> {
        self.random.iter().find(|r| r.subgroup == sub)
    }
}

/// ItemGroupDatabase::item_exists.
pub fn itemdb_group_item_exists(group_id: u16, nameid: u32) -> bool {
    item_group(group_id).is_some_and(|g| g.random.iter().any(|r| r.data.iter().any(|e| e.nameid == nameid)))
}

/// The run-time part of a GROUP_ALGORITHM_SHAREDPOOL sub group: how often each entry (by position in
/// [`ItemGroupRandom::data`]) and the sub group as a whole have been handed out (s_item_group_entry::given,
/// s_item_group_random::total_given). rAthena keeps it in memory until the next item db reload; the caller keeps it.
#[derive(Clone, Debug, Default)]
pub struct SharedPool {
    pub given: Vec<u16>,
    pub total_given: u32,
}

/// ItemGroupDatabase::get_random_itemsubgroup. `rnd_value(min, max)` is rAthena's inclusive rnd_value; `pool` is the
/// sub group's [`SharedPool`] (only the SHAREDPOOL algorithm reads and updates it).
pub fn get_random_itemsubgroup<'a>(random: &'a ItemGroupRandom, algorithm: u8, rnd_value: &mut dyn FnMut(i32, i32) -> i32, pool: &mut SharedPool) -> Option<&'a ItemGroupEntry> {
    let algorithm = if algorithm == GROUP_ALGORITHM_USEDB { random.algorithm } else { algorithm };
    if random.data.is_empty() {
        return None;
    }
    // util::umap_random: a random position of the map's iteration
    let umap_random = |rnd_value: &mut dyn FnMut(i32, i32) -> i32| &random.data[rnd_value(0, random.data.len() as i32 - 1) as usize];
    match algorithm {
        GROUP_ALGORITHM_DROP => {
            let e = umap_random(rnd_value);
            // rnd_chance_official<uint16>(adj_rate, 10000): rnd_value(0, 20000) % 10000 < adj_rate
            if rnd_value(0, 20000) % 10000 < e.adj_rate as i32 {
                return Some(e);
            }
            None
        }
        GROUP_ALGORITHM_ALL => Some(umap_random(rnd_value)),
        GROUP_ALGORITHM_RANDOM => {
            let pos = rnd_value(1, random.total_rate as i32) as u32;
            let mut current = 1u32;
            for e in &random.data {
                current += e.rate as u32;
                if current > pos {
                    return Some(e);
                }
            }
            None
        }
        GROUP_ALGORITHM_SHAREDPOOL => {
            pool.given.resize(random.data.len(), 0);
            let pos = rnd_value(1, random.total_rate.wrapping_sub(pool.total_given) as i32) as u32;
            let mut current = 1u32;
            for (i, e) in random.data.iter().enumerate() {
                current = current.wrapping_add((e.rate as u32).wrapping_sub(pool.given[i] as u32));
                if current > pos {
                    pool.given[i] = pool.given[i].wrapping_add(1);
                    pool.total_given += 1;
                    if pool.total_given >= random.total_rate {
                        pool.total_given = 0;
                        pool.given.iter_mut().for_each(|g| *g = 0);
                    }
                    return Some(e);
                }
            }
            None
        }
        _ => None,
    }
}
