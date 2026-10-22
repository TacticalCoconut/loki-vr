//! itemdb.cpp RandomOptionDatabase (db/re/item_randomopt_db.yml + import) and RandomOptionGroupDatabase
//! (db/re/item_randomopt_group.yml + import), and s_random_opt_group::apply.

use std::collections::HashMap;
use std::sync::OnceLock;

/// itemdb.hpp struct s_random_opt_data.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct RandomOption {
    /// `Id`.
    pub id: u16,
    /// `Option` (unique; RDMOPT_<Option> is the script constant for `id`).
    pub name: String,
    /// `Script` (source text).
    #[serde(default)]
    pub script: Option<String>,
}

/// itemdb.hpp struct s_random_opt_group_entry (RandomOptionGroupDatabase::add_option).
#[derive(serde::Deserialize, Debug, Clone)]
pub struct RandomOptionGroupEntry {
    /// `Option` resolved to its id.
    pub id: u16,
    /// `MinValue` (default 0).
    pub min_value: i16,
    /// `MaxValue` (default 0; MinValue + 1 when below MinValue).
    pub max_value: i16,
    /// `Param` (default 0).
    pub param: i8,
    /// `Chance` (1..10000, default 0).
    pub chance: u16,
}

/// itemdb.hpp struct s_random_opt_group.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct RandomOptionGroup {
    /// `Id`.
    pub id: u16,
    /// `Group` (unique).
    pub name: String,
    /// `Slots`: (slot - 1, its options), sorted by slot (std::map), at most MAX_ITEM_RDM_OPT slots.
    pub slots: Vec<(u16, Vec<RandomOptionGroupEntry>)>,
    /// `MaxRandom` (default 0, capped to MAX_ITEM_RDM_OPT).
    pub max_random: u16,
    /// `Random`.
    pub random_options: Vec<RandomOptionGroupEntry>,
}

/// data/json/items_randomopt.
#[derive(serde::Deserialize)]
pub struct RandomOptionFile {
    /// The options, in the database's iteration order.
    pub options: Vec<RandomOption>,
    /// RandomOptionDatabase::loadingFinished exports RDMOPT_<Option> = id with script_set_constant (never
    /// overwriting an existing name): the constants it adds, for the script engine's constant table.
    pub constants: Vec<(String, u16)>,
    /// The groups, in the database's iteration order.
    pub groups: Vec<RandomOptionGroup>,
}

crate::dataset!(random_option_file, "items_randomopt", RandomOptionFile);

struct Index {
    options: HashMap<u16, usize>,
    groups: HashMap<u16, usize>,
}

static INDEX: OnceLock<Index> = OnceLock::new();

fn index() -> &'static Index {
    INDEX.get_or_init(|| {
        let f = random_option_file();
        Index {
            options: f.options.iter().enumerate().map(|(i, o)| (o.id, i)).collect(),
            groups: f.groups.iter().enumerate().map(|(i, g)| (g.id, i)).collect(),
        }
    })
}

/// random_option_db.find.
pub fn random_option(id: u16) -> Option<&'static RandomOption> {
    index().options.get(&id).map(|&i| &random_option_file().options[i])
}

/// RandomOptionDatabase::option_get_id (exact, case-sensitive name).
pub fn random_option_get_id(name: &str) -> Option<u16> {
    random_option_file().options.iter().find(|o| o.name == name).map(|o| o.id)
}

/// random_option_group.find.
pub fn random_option_group(id: u16) -> Option<&'static RandomOptionGroup> {
    index().groups.get(&id).map(|&i| &random_option_file().groups[i])
}

/// RandomOptionGroupDatabase::option_get_id (exact, case-sensitive name).
pub fn random_option_group_get_id(name: &str) -> Option<u16> {
    random_option_file().groups.iter().find(|g| g.name == name).map(|g| g.id)
}

/// One item random option (mmo.hpp s_item_randomoption).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct ItemRandomOption {
    pub id: i16,
    pub value: i16,
    pub param: i8,
}

impl RandomOptionGroup {
    fn slot(&self, i: u16) -> &[RandomOptionGroupEntry] {
        self.slots.iter().find(|(s, _)| *s == i).map(|(_, v)| v.as_slice()).unwrap_or(&[])
    }

    /// s_random_opt_group::apply: the MAX_ITEM_RDM_OPT options an item gets from this group. `rnd_value(min, max)` is
    /// rAthena's inclusive rnd_value. The "must" slots each try up to 3 × (options) random picks that pass their
    /// chance, then take a random one; `MaxRandom` slots that are still free try one random pick; gaps are closed.
    /// (rAthena indexes slots 0..slots.size() with std::map::operator[]; a missing slot is an empty list here and
    /// gets no option.)
    pub fn apply(&self, rnd_value: &mut dyn FnMut(i32, i32) -> i32) -> [ItemRandomOption; 5] {
        const MAX_ITEM_RDM_OPT: usize = 5;
        let mut out = [ItemRandomOption::default(); MAX_ITEM_RDM_OPT];
        let sub = |o: &RandomOptionGroupEntry, rnd_value: &mut dyn FnMut(i32, i32) -> i32| ItemRandomOption {
            id: o.id as i16,
            value: rnd_value(o.min_value as i32, o.max_value as i32) as i16,
            param: o.param,
        };
        for i in 0..self.slots.len().min(MAX_ITEM_RDM_OPT) {
            let opts = self.slot(i as u16);
            if opts.is_empty() {
                continue;
            }
            for _ in 0..opts.len() * 3 {
                let o = &opts[rnd_value(0, opts.len() as i32 - 1) as usize];
                // rnd_chance<uint16>(chance, 10000): rnd_value(1, 10000) <= chance
                if rnd_value(1, 10000) <= o.chance as i32 {
                    out[i] = sub(o, rnd_value);
                    break;
                }
            }
            if out[i].id == 0 {
                let o = &opts[rnd_value(0, opts.len() as i32 - 1) as usize];
                out[i] = sub(o, rnd_value);
            }
        }
        if self.max_random > 0 && !self.random_options.is_empty() {
            for i in 0..(self.max_random as usize).min(MAX_ITEM_RDM_OPT) {
                if out[i].id > 0 {
                    continue;
                }
                let o = &self.random_options[rnd_value(0, self.random_options.len() as i32 - 1) as usize];
                if rnd_value(1, 10000) <= o.chance as i32 {
                    out[i] = sub(o, rnd_value);
                }
            }
        }
        for i in 0..MAX_ITEM_RDM_OPT {
            if out[i].id == 0 {
                match (i + 1..MAX_ITEM_RDM_OPT).find(|&j| out[j].id != 0) {
                    Some(j) => {
                        out[i] = out[j];
                        out[j] = ItemRandomOption::default();
                    }
                    None => break,
                }
            }
        }
        out
    }
}
