//! Run-time changes of the item database: script.cpp BUILDIN setiteminfo (item_data fields) and setitemscript
//! (item_data::script / equip_script / unequip_script).
//!
//! rAthena changes the shared item_data in memory: every later lookup sees it, nothing is saved, and a server restart
//! (which reloads item_db) forgets it. Here the item database is embedded read-only data, so the changes are rows of
//! `itemdb_edit`, applied in order over the embedded entries as an overlay that db/items looks at first
//! (`itemdb::set_runtime`). Each module instance rebuilds its overlay when the edit counter moves ([`sync`], called at
//! the start of every reducer by world::run); edits made before the world was last built (the module's "restart")
//! are ignored, as rAthena's reload would lose them. Like rAthena, a changed Weight does not recalculate the
//! weight players already carry.

use spacetimedb::{table, Table};

use crate::db::items::{self as itemdb, ItemData, ItemScripts, IT_ARMOR, IT_WEAPON};
use crate::world::Ctx;

/// script.cpp e_item_info (ITEMINFO_*).
pub const ITEMINFO_BUY: i32 = 0;
pub const ITEMINFO_SELL: i32 = 1;
pub const ITEMINFO_TYPE: i32 = 2;
pub const ITEMINFO_MAXCHANCE: i32 = 3;
pub const ITEMINFO_GENDER: i32 = 4;
pub const ITEMINFO_LOCATIONS: i32 = 5;
pub const ITEMINFO_WEIGHT: i32 = 6;
pub const ITEMINFO_ATTACK: i32 = 7;
pub const ITEMINFO_DEFENSE: i32 = 8;
pub const ITEMINFO_RANGE: i32 = 9;
pub const ITEMINFO_SLOT: i32 = 10;
pub const ITEMINFO_VIEW: i32 = 11;
pub const ITEMINFO_EQUIPLEVELMIN: i32 = 12;
pub const ITEMINFO_WEAPONLEVEL: i32 = 13;
pub const ITEMINFO_ALIASNAME: i32 = 14;
pub const ITEMINFO_EQUIPLEVELMAX: i32 = 15;
pub const ITEMINFO_MAGICATTACK: i32 = 16;
pub const ITEMINFO_SUBTYPE: i32 = 20;
/// itemdb.hpp MAX_WEAPON_LEVEL / MAX_ARMOR_LEVEL (RENEWAL).
pub const MAX_WEAPON_LEVEL: i32 = 5;
pub const MAX_ARMOR_LEVEL: i32 = 2;
/// `field` of a setitemscript edit: 100 + n (0 script, 1 equip script, 2 unequip script).
const SCRIPT_FIELD: i32 = 100;

/// One setiteminfo / setitemscript call.
#[table(accessor = itemdb_edit)]
#[derive(Clone, Debug)]
pub struct ItemdbEdit {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub nameid: u32,
    /// ITEMINFO_* for setiteminfo, 100 + n for setitemscript.
    pub field: i32,
    pub value: i32,
    /// setitemscript's text (None: the script is removed).
    pub script: Option<String>,
    /// The world build (world_state.built_at_tick) the edit belongs to.
    pub world: i64,
}

/// The edit counter (key 0).
#[table(accessor = itemdb_edit_state)]
#[derive(Clone, Debug)]
pub struct ItemdbEditState {
    #[primary_key]
    pub key: u8,
    pub version: u64,
}

fn world_generation(c: &Ctx) -> i64 {
    use crate::npc::world_state as _;
    c.db().world_state().key().find(0).map(|w| w.built_at_tick).unwrap_or(0)
}

/// Brings this instance's item_db overlay up to date (world::run, before the reducer's work).
pub fn sync(c: &Ctx) {
    let version = c.db().itemdb_edit_state().key().find(0).map(|s| s.version).unwrap_or(0);
    if version == itemdb::runtime_version() {
        return;
    }
    let world = world_generation(c);
    let mut edits: Vec<ItemdbEdit> = c.db().itemdb_edit().iter().filter(|e| e.world == world).collect();
    edits.sort_by_key(|e| e.id);
    let mut items: std::collections::BTreeMap<u32, ItemData> = std::collections::BTreeMap::new();
    let mut scripts: std::collections::BTreeMap<u32, ItemScripts> = std::collections::BTreeMap::new();
    for e in &edits {
        if e.field >= SCRIPT_FIELD {
            let s = scripts.entry(e.nameid).or_insert_with(|| {
                itemdb::item_scripts_base(e.nameid).cloned().unwrap_or(ItemScripts { nameid: e.nameid, script: None, equip_script: None, unequip_script: None })
            });
            match e.field - SCRIPT_FIELD {
                2 => s.unequip_script = e.script.clone(),
                1 => s.equip_script = e.script.clone(),
                _ => s.script = e.script.clone(),
            }
        } else if let Some(base) = itemdb::itemdb_exists_base(e.nameid) {
            let d = items.entry(e.nameid).or_insert_with(|| base.clone());
            apply(d, e.field, e.value);
        }
    }
    itemdb::set_runtime(version, items.into_values().collect(), scripts.into_values().collect());
    // item scripts compiled from the old text are forgotten (script_free_code of the replaced script)
    crate::script::code::reset();
}

/// The field assignments of BUILDIN setiteminfo (the checks that refuse a value are made before the edit is stored).
pub(crate) fn apply(d: &mut ItemData, field: i32, value: i32) {
    match field {
        ITEMINFO_BUY => d.value_buy = value as u32,
        ITEMINFO_SELL => d.value_sell = value as u32,
        ITEMINFO_TYPE => d.r#type = value as u8,
        ITEMINFO_MAXCHANCE => {} // item_data::maxchance: the monster drop search is the mob agent's (db/mobs keeps it)
        ITEMINFO_GENDER => d.sex = value as u8,
        ITEMINFO_LOCATIONS => d.equip = value as u32,
        ITEMINFO_WEIGHT => d.weight = value as u32,
        ITEMINFO_ATTACK => d.atk = value as u32,
        ITEMINFO_DEFENSE => d.def = value as u32,
        ITEMINFO_RANGE => d.range = value as u16,
        ITEMINFO_SLOT => d.slots = value as u16,
        ITEMINFO_VIEW => {
            // keep old compatibility
            if d.r#type == IT_WEAPON || d.r#type == itemdb::IT_AMMO {
                d.subtype = value as u8;
            } else {
                d.look = value as u32;
            }
        }
        ITEMINFO_EQUIPLEVELMIN => d.elv = value as u16,
        ITEMINFO_WEAPONLEVEL => d.weapon_level = value as u16,
        ITEMINFO_ARMORLEVEL => d.armor_level = value as u16,
        ITEMINFO_ALIASNAME => d.view_id = value as u32,
        ITEMINFO_EQUIPLEVELMAX => d.elvmax = value as u16,
        ITEMINFO_MAGICATTACK => d.matk = value as u32, // RENEWAL
        ITEMINFO_SUBTYPE => d.subtype = value as u8,
        _ => {}
    }
}

/// script.cpp e_item_info ITEMINFO_ARMORLEVEL.
pub const ITEMINFO_ARMORLEVEL: i32 = 19;

fn record(c: &Ctx, nameid: u32, field: i32, value: i32, script: Option<String>) {
    let world = world_generation(c);
    c.db().itemdb_edit().insert(ItemdbEdit { id: 0, nameid, field, value, script, world });
    let mut s = c.db().itemdb_edit_state().key().find(0).unwrap_or(ItemdbEditState { key: 0, version: 0 });
    s.version += 1;
    if c.db().itemdb_edit_state().key().find(0).is_some() {
        c.db().itemdb_edit_state().key().update(s);
    } else {
        c.db().itemdb_edit_state().insert(s);
    }
    // this reducer sees its own change at once, as rAthena's next line would
    sync(c);
}

/// BUILDIN setiteminfo(<item>,<type>,<value>): the values pushed, in order (rAthena pushes -1 and then the value for
/// an unknown type). Err(()) = the command fails after pushing -1 (a weapon / armor level it refuses).
pub fn setiteminfo(c: &Ctx, nameid: u32, field: i32, value: i32) -> Result<Vec<i64>, ()> {
    let Some(d) = itemdb::itemdb_exists(nameid) else { return Ok(vec![-1]) };
    match field {
        ITEMINFO_WEAPONLEVEL => {
            if d.r#type != IT_WEAPON {
                log::error!("buildin_setiteminfo: Can not set a weapon level for {}.", d.nameid);
                return Err(());
            }
            if value > MAX_WEAPON_LEVEL {
                log::error!("buildin_setiteminfo: weapon level {value} is above maximum {MAX_WEAPON_LEVEL}.");
                return Err(());
            }
        }
        ITEMINFO_ARMORLEVEL => {
            if d.r#type != IT_ARMOR {
                log::error!("buildin_setiteminfo: Can not set an armor level for {}.", d.nameid);
                return Err(());
            }
            if value > MAX_ARMOR_LEVEL {
                log::error!("buildin_setiteminfo: armor level {value} is above maximum {MAX_ARMOR_LEVEL}.");
                return Err(());
            }
        }
        ITEMINFO_BUY | ITEMINFO_SELL | ITEMINFO_TYPE | ITEMINFO_MAXCHANCE | ITEMINFO_GENDER | ITEMINFO_LOCATIONS | ITEMINFO_WEIGHT
        | ITEMINFO_ATTACK | ITEMINFO_DEFENSE | ITEMINFO_RANGE | ITEMINFO_SLOT | ITEMINFO_VIEW | ITEMINFO_EQUIPLEVELMIN
        | ITEMINFO_ALIASNAME | ITEMINFO_EQUIPLEVELMAX | ITEMINFO_MAGICATTACK | ITEMINFO_SUBTYPE => {}
        _ => return Ok(vec![-1, value as i64]),
    }
    if field == ITEMINFO_MAXCHANCE {
        // item_data::maxchance is not part of the embedded item data (the monster databases hold the drop chances)
        log::warn!("buildin_setiteminfo: ITEMINFO_MAXCHANCE of {nameid} is not kept (monster drop data)");
    }
    record(c, nameid, field, value, None);
    Ok(vec![value as i64])
}

/// BUILDIN setitemscript(<item id>,"<script>"{,<n>}): 1 when set, 0 for an unknown item or a text that does not start
/// with '{' (an empty text removes the script).
pub fn setitemscript(c: &Ctx, nameid: u32, script: &str, n: i32) -> i64 {
    if itemdb::itemdb_exists(nameid).is_none() || (!script.is_empty() && !script.starts_with('{')) {
        return 0;
    }
    let which = match n {
        2 => 2,
        1 => 1,
        _ => 0,
    };
    record(c, nameid, SCRIPT_FIELD + which, 0, if script.is_empty() { None } else { Some(script.to_string()) });
    1
}

/// A script set by setitemscript: Some(text or None when removed) for (nameid, 0 script / 1 equip / 2 unequip).
/// rAthena parses these with parse_script(script, "script_setitemscript", 0, 0) (options 0: the text keeps its
/// braces), unlike the database's scripts.
pub fn runtime_script(c: &Ctx, nameid: u32, which: i32) -> Option<Option<String>> {
    let world = world_generation(c);
    let mut edits: Vec<ItemdbEdit> = c.db().itemdb_edit().iter().filter(|e| e.world == world && e.nameid == nameid && e.field == SCRIPT_FIELD + which).collect();
    edits.sort_by_key(|e| e.id);
    edits.last().map(|e| e.script.clone())
}
