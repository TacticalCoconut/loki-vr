//! rAthena's item databases (itemdb.cpp and the item-keyed tables loaded next to it), as tools/datagen_mods/items.py
//! resolves them: every loader's rules (base file then import, partial overrides, `Clear`, defaults for new entries,
//! caps, battle_config-dependent values, ItemDatabase::loadingFinished and ComboDatabase::loadingFinished) are applied
//! at datagen time, so what is embedded is the state rAthena's map-server holds once it has started.
//!
//! - [`item_db`] / [`itemdb_exists`] / [`itemdb_search`] … : ItemDatabase (db/re/item_db_*.yml + import, item_noequip.txt)
//! - [`combo`]: ComboDatabase, [`group`]: ItemGroupDatabase, [`roulette`]: the roulette table
//!   (itemdb_parse_roulette_db), [`randomopt`]: RandomOptionDatabase and
//!   RandomOptionGroupDatabase, [`laphine`]: Laphine synthesis/upgrade, [`reform`], [`enchant`], [`package`],
//!   [`cash`]: CashShopDatabase (cashshop.cpp), [`refine`] / [`enchantgrade`] (status.cpp), [`stylist`] / [`barter`]
//!   (npc.cpp).
//!
//! Lists that mirror a std::unordered_map are in rAthena's (libstdc++) iteration order, std::map ones are sorted by key
//! and vectors keep their insertion order, so code that iterates them behaves like rAthena's loops.
//!
//! Not here: fields other modules fill at run time (item_data::maxchance and mob[] from mob.cpp's drop tables,
//! maxchance = -1 for items sold in shops (npc.cpp), flag.autoequip / setiteminfo changes from scripts). Scripts are
//! kept as source text; the script engine compiles them (parse_script with SCRIPT_IGNORE_EXTERNAL_BRACKETS).

pub mod barter;
pub mod cash;
pub mod combo;
pub mod enchant;
pub mod enchantgrade;
pub mod group;
pub mod laphine;
pub mod package;
pub mod randomopt;
pub mod refine;
pub mod reform;
pub mod roulette;
pub mod stylist;

use std::collections::{HashMap, HashSet};
use std::sync::OnceLock;

// ---------------------------------------------------------------------------------------------- constants

/// mmo.hpp enum item_types.
pub const IT_HEALING: u8 = 0;
pub const IT_UNKNOWN: u8 = 1;
pub const IT_USABLE: u8 = 2;
pub const IT_ETC: u8 = 3;
pub const IT_ARMOR: u8 = 4;
pub const IT_WEAPON: u8 = 5;
pub const IT_CARD: u8 = 6;
pub const IT_PETEGG: u8 = 7;
pub const IT_PETARMOR: u8 = 8;
pub const IT_UNKNOWN2: u8 = 9;
pub const IT_AMMO: u8 = 10;
pub const IT_DELAYCONSUME: u8 = 11;
pub const IT_SHADOWGEAR: u8 = 12;
pub const IT_CASH: u8 = 18;
pub const IT_MAX: u8 = 19;

/// pc.hpp enum e_ammo_type.
pub const AMMO_NONE: u8 = 0;
pub const AMMO_ARROW: u8 = 1;
pub const AMMO_DAGGER: u8 = 2;
pub const AMMO_BULLET: u8 = 3;
pub const AMMO_SHELL: u8 = 4;
pub const AMMO_GRENADE: u8 = 5;
pub const AMMO_SHURIKEN: u8 = 6;
pub const AMMO_KUNAI: u8 = 7;
pub const AMMO_CANNONBALL: u8 = 8;
pub const AMMO_THROWWEAPON: u8 = 9;

/// mmo.hpp enum e_sex.
pub const SEX_FEMALE: u8 = 0;
pub const SEX_MALE: u8 = 1;
pub const SEX_BOTH: u8 = 2;

/// itemdb.hpp e_item_job (class_upper bits; ITEMJ_ALL is the RENEWAL value).
pub const ITEMJ_NORMAL: u16 = 0x01;
pub const ITEMJ_UPPER: u16 = 0x02;
pub const ITEMJ_BABY: u16 = 0x04;
pub const ITEMJ_THIRD: u16 = 0x08;
pub const ITEMJ_THIRD_UPPER: u16 = 0x10;
pub const ITEMJ_THIRD_BABY: u16 = 0x20;
pub const ITEMJ_FOURTH: u16 = 0x40;
pub const ITEMJ_ALL: u16 = 0x7f;

/// itemdb.hpp e_delay_consume.
pub const DELAYCONSUME_NONE: u8 = 0x0;
pub const DELAYCONSUME_TEMP: u8 = 0x1;
pub const DELAYCONSUME_NOCONSUME: u8 = 0x2;

/// status.hpp SC_NONE (item_data::delay.sc when no delay group is set).
pub const SC_NONE: i16 = -1;

/// itemdb.hpp: ITEMID_DUMMY (itemdb_search's fallback, created by loadingFinished when missing), UNKNOWN_ITEM_ID
/// (its view id), ITEMID_APPLE.
pub const ITEMID_DUMMY: u32 = 499;
pub const UNKNOWN_ITEM_ID: u32 = 512;
pub const ITEMID_APPLE: u32 = 512;

/// itemdb.hpp CARD0_FORGE / CARD0_CREATE / CARD0_PET (card[0] markers of forged, created and pet-egg items).
pub const CARD0_FORGE: u32 = 0x00FF;
pub const CARD0_CREATE: u32 = 0x00FE;
pub const CARD0_PET: u32 = 0x0100;

/// itemdb.hpp itemdb_isspecial.
pub fn itemdb_isspecial(card0: u32) -> bool {
    card0 == CARD0_FORGE || card0 == CARD0_CREATE || card0 == CARD0_PET
}

/// map.hpp MAPID_ALL (class_base of an item without a `Jobs` list).
pub const MAPID_ALL: u64 = u64::MAX;
/// mmo.hpp MAX_SLOTS, map.hpp MAX_LEVEL.
pub const MAX_SLOTS: usize = 4;
pub const MAX_LEVEL: u16 = 275;

// ---------------------------------------------------------------------------------------------- item_data

fn d_true() -> bool {
    true
}
fn d_it_etc() -> u8 {
    IT_ETC
}
fn d_sex_both() -> u8 {
    SEX_BOTH
}
fn d_max_level() -> u16 {
    MAX_LEVEL
}
fn d_class_base() -> [u64; 3] {
    [MAPID_ALL; 3]
}
fn d_itemj_all() -> u16 {
    ITEMJ_ALL
}
fn d_100() -> u16 {
    100
}
fn d_100_i16() -> i16 {
    100
}

/// itemdb.hpp struct item_data, as ItemDatabase::parseBodyNode and loadingFinished leave it. Each field names its YAML
/// key and the value a new entry gets when the key is missing.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemData {
    /// `Id`.
    pub nameid: u32,
    /// `AegisName` (item_data::name; not capped: the loader's resize() is overwritten by the assignment).
    pub name: String,
    /// `Name` (item_data::ename).
    pub ename: String,
    /// `Buy` (default 0). loadingFinished: without `Buy` but with `Sell` it is Sell * 2.
    #[serde(default)]
    pub value_buy: u32,
    /// `Sell` (default 0). loadingFinished: without `Sell` but with `Buy` it is Buy / 2; a Buy/Sell pair that allows
    /// the buy-low/sell-high exploit (Buy / 124 < Sell / 75) becomes Sell 1. "Given" means in the last entry that
    /// parsed the item (an import entry without prices resets it, like rAthena's hasPriceValue).
    #[serde(default)]
    pub value_sell: u32,
    /// `Type` (IT_*, default IT_ETC; IT_DELAYCONSUME becomes IT_USABLE + DELAYCONSUME_TEMP in loadingFinished; an
    /// equip type without `Locations`, or a shadow-gear location on another type, becomes IT_ETC).
    #[serde(default = "d_it_etc")]
    pub r#type: u8,
    /// `SubType`: weapon W_*, ammo AMMO_* or card CARD_* (default 0).
    #[serde(default)]
    pub subtype: u8,
    /// `Gender` (SEX_*, default SEX_BOTH; ItemDatabase::defaultGender: Bridegroom ring male, bride ring female,
    /// musical instruments male, whips female).
    #[serde(default = "d_sex_both")]
    pub sex: u8,
    /// `Locations` (EQP_* bits, default 0).
    #[serde(default)]
    pub equip: u32,
    /// `Weight` (default 0).
    #[serde(default)]
    pub weight: u32,
    /// `Attack` (default 0; only weapons and ammo keep it).
    #[serde(default)]
    pub atk: u32,
    /// `MagicAttack` (RENEWAL; default 0; weapons only).
    #[serde(default)]
    pub matk: u32,
    /// `Defense` (default 0, capped to DEFTYPE_MAX; armors and shadow gear only).
    #[serde(default)]
    pub def: u32,
    /// `Range` (default 0, capped to battle_config.area_size; weapons only).
    #[serde(default)]
    pub range: u16,
    /// `Slots` (default 0, capped to MAX_SLOTS).
    #[serde(default)]
    pub slots: u16,
    /// `View` (default 0; loadingFinished gives a shield without one view 1 so ST_SHIELD checks see it).
    #[serde(default)]
    pub look: u32,
    /// `EquipLevelMin` (default 0, capped to MAX_LEVEL).
    #[serde(default)]
    pub elv: u16,
    /// `EquipLevelMax` (default MAX_LEVEL; at least `elv`).
    #[serde(default = "d_max_level")]
    pub elvmax: u16,
    /// `WeaponLevel` (default 0; loadingFinished: 1 for a weapon without one, 0 for anything else).
    #[serde(default)]
    pub weapon_level: u16,
    /// `ArmorLevel` (default 0; loadingFinished: 1 for an armor without one, 0 for anything else).
    #[serde(default)]
    pub armor_level: u16,
    /// `AliasName` resolved to that item's id (item_data::view_id, itemdb_viewid; default 0).
    #[serde(default)]
    pub view_id: u32,
    /// `Jobs` as itemdb_jobid2mapid builds it: [base jobs, 2-1 jobs, 2-2 jobs], bit = 1 << (MAPID & MAPID_FIRSTMASK)
    /// (default: `All` = MAPID_ALL in all three).
    #[serde(default = "d_class_base")]
    pub class_base: [u64; 3],
    /// `Classes` (ITEMJ_* bits, default ITEMJ_ALL).
    #[serde(default = "d_itemj_all")]
    pub class_upper: u16,
    #[serde(default)]
    pub flag: ItemFlag,
    #[serde(default)]
    pub stack: ItemStack,
    #[serde(default)]
    pub item_usage: ItemUsage,
    /// `Trade.Override` (default 100): the group level that ignores the trade restrictions.
    #[serde(default = "d_100_i16")]
    pub gm_lv_trade_override: i16,
    /// The combos this item is part of (ComboDatabase::loadingFinished, in its iteration order): combo ids, see
    /// [`combo::combo`].
    #[serde(default)]
    pub combos: Vec<u16>,
    #[serde(default)]
    pub delay: ItemDelay,
}

/// item_data::flag.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemFlag {
    /// Set for every item the database defines (not for the dummy item).
    #[serde(default = "d_true")]
    pub available: bool,
    /// item_noequip.txt (+ import) bits: 1 normal maps, 2 PVP, 4 GVG, 8 battlegrounds, 16 WoE:TE, 32.. zones 1..
    /// (a negative flag in the file clears bits).
    #[serde(default)]
    pub no_equip: u32,
    /// Not `Refineable` (default true: not refineable).
    #[serde(default = "d_true")]
    pub no_refine: bool,
    /// DELAYCONSUME_* bits: `Flags.NoConsume` (NOCONSUME) and Type DelayConsume (TEMP).
    #[serde(default)]
    pub delay_consume: u8,
    #[serde(default)]
    pub trade_restriction: TradeRestriction,
    /// `Flags.BuyingStore` (stackable items only).
    #[serde(default)]
    pub buyingstore: bool,
    /// `Flags.DeadBranch`.
    #[serde(default)]
    pub dead_branch: bool,
    /// `Flags.Container` (item group container).
    #[serde(default)]
    pub group: bool,
    /// `Flags.UniqueId` (stackable items only).
    #[serde(default)]
    pub guid: bool,
    /// `Flags.DropAnnounce`.
    #[serde(default)]
    pub broadcast: bool,
    /// `Flags.BindOnEquip` (bindOnEquip).
    #[serde(default)]
    pub bind_on_equip: bool,
    /// `Flags.DropEffect` (DROPEFFECT_*, default DROPEFFECT_NONE; dropEffect).
    #[serde(default)]
    pub drop_effect: u16,
    /// `Gradable` (default false).
    #[serde(default)]
    pub gradable: bool,
}

impl Default for ItemFlag {
    fn default() -> Self {
        ItemFlag {
            available: true,
            no_equip: 0,
            no_refine: true,
            delay_consume: DELAYCONSUME_NONE,
            trade_restriction: TradeRestriction::default(),
            buyingstore: false,
            dead_branch: false,
            group: false,
            guid: false,
            broadcast: false,
            bind_on_equip: false,
            drop_effect: 0,
            gradable: false,
        }
    }
}

/// item_data::flag.trade_restriction: `Trade.NoDrop`, `NoTrade`, `TradePartner`, `NoSell`, `NoCart`, `NoStorage`,
/// `NoGuildStorage`, `NoMail`, `NoAuction` (all default false).
#[derive(serde::Deserialize, Debug, Clone, Default)]
pub struct TradeRestriction {
    #[serde(default)]
    pub drop: bool,
    #[serde(default)]
    pub trade: bool,
    #[serde(default)]
    pub trade_partner: bool,
    #[serde(default)]
    pub sell: bool,
    #[serde(default)]
    pub cart: bool,
    #[serde(default)]
    pub storage: bool,
    #[serde(default)]
    pub guild_storage: bool,
    #[serde(default)]
    pub mail: bool,
    #[serde(default)]
    pub auction: bool,
}

/// item_data::stack: `Stack.Amount` (stackable items only), `Inventory`, `Cart`, `Storage`, `GuildStorage` (defaults
/// 0 / false).
#[derive(serde::Deserialize, Debug, Clone, Default)]
pub struct ItemStack {
    #[serde(default)]
    pub amount: u16,
    #[serde(default)]
    pub inventory: bool,
    #[serde(default)]
    pub cart: bool,
    #[serde(default)]
    pub storage: bool,
    #[serde(default)]
    pub guild_storage: bool,
}

/// item_data::item_usage: `NoUse.Override` (default 100, capped to 100) and `NoUse.Sitting` (default false).
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemUsage {
    #[serde(default = "d_100")]
    pub r#override: u16,
    #[serde(default)]
    pub sitting: bool,
}

impl Default for ItemUsage {
    fn default() -> Self {
        ItemUsage { r#override: 100, sitting: false }
    }
}

/// item_data::delay: `Delay.Duration` (ms, default 0) and `Delay.Status` (SC_* delay group, default SC_NONE).
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemDelay {
    #[serde(default)]
    pub duration: u32,
    #[serde(default = "d_sc_none")]
    pub sc: i16,
}

fn d_sc_none() -> i16 {
    SC_NONE
}

impl Default for ItemDelay {
    fn default() -> Self {
        ItemDelay { duration: 0, sc: SC_NONE }
    }
}

impl ItemData {
    /// item_data::isStackable.
    pub fn is_stackable(&self) -> bool {
        !matches!(self.r#type, IT_WEAPON | IT_ARMOR | IT_PETEGG | IT_PETARMOR | IT_SHADOWGEAR)
    }
    /// item_data::inventorySlotNeeded.
    pub fn inventory_slot_needed(&self, quantity: i32) -> i32 {
        if self.flag.guid || !self.is_stackable() {
            quantity
        } else {
            1
        }
    }
    /// The item's scripts (source text), see [`item_scripts`].
    pub fn scripts(&self) -> Option<&'static ItemScripts> {
        item_scripts(self.nameid)
    }
}

/// `Script`, `EquipScript`, `UnEquipScript` of one item (item_data::script / equip_script / unequip_script) as source
/// text (None: the key is absent).
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemScripts {
    pub nameid: u32,
    #[serde(default)]
    pub script: Option<String>,
    #[serde(default)]
    pub equip_script: Option<String>,
    #[serde(default)]
    pub unequip_script: Option<String>,
}

/// data/json/items_db: the items (sorted by id) and what is needed to rebuild ItemDatabase's two name lookups.
#[derive(serde::Deserialize)]
pub struct ItemDbFile {
    pub items: Vec<ItemData>,
    /// Items that are not in aegisNameToItemDataMap (the dummy item is put() directly).
    aegis_missing: Vec<u32>,
    /// Lookup keys (lower-cased) that do not follow from an item's current AegisName.
    aegis_extra: Vec<(String, u32)>,
    /// Items whose `Name` is not in nameToItemDataMap: a later item took the same (lower-cased) name.
    name_missing: Vec<u32>,
    name_extra: Vec<(String, u32)>,
}

crate::dataset!(item_db_file, "items_db", ItemDbFile);
crate::dataset!(item_scripts_file, "items_script", Vec<ItemScripts>);

struct Index {
    by_id: HashMap<u32, usize>,
    by_aegis: HashMap<String, usize>,
    by_name: HashMap<String, usize>,
}

static INDEX: OnceLock<Index> = OnceLock::new();

fn index() -> &'static Index {
    INDEX.get_or_init(|| {
        let f = item_db_file();
        let by_id: HashMap<u32, usize> = f.items.iter().enumerate().map(|(i, it)| (it.nameid, i)).collect();
        let mut by_aegis = HashMap::with_capacity(f.items.len());
        let mut by_name = HashMap::with_capacity(f.items.len());
        let aegis_missing: HashSet<u32> = f.aegis_missing.iter().copied().collect();
        let name_missing: HashSet<u32> = f.name_missing.iter().copied().collect();
        for (i, it) in f.items.iter().enumerate() {
            if !aegis_missing.contains(&it.nameid) {
                by_aegis.insert(it.name.to_ascii_lowercase(), i);
            }
            if !name_missing.contains(&it.nameid) {
                by_name.insert(it.ename.to_ascii_lowercase(), i);
            }
        }
        for (k, id) in &f.aegis_extra {
            if let Some(&i) = by_id.get(id) {
                by_aegis.insert(k.clone(), i);
            }
        }
        for (k, id) in &f.name_extra {
            if let Some(&i) = by_id.get(id) {
                by_name.insert(k.clone(), i);
            }
        }
        Index { by_id, by_aegis, by_name }
    })
}

/// Every item, sorted by id (ItemDatabase iteration; rAthena's own order is its hash map's and nothing depends on it).
pub fn item_db() -> &'static [ItemData] {
    &item_db_file().items
}

/// itemdb_exists / ItemDatabase::find.
pub fn itemdb_exists(nameid: u32) -> Option<&'static ItemData> {
    if let Some(d) = runtime_item(nameid) {
        return Some(d);
    }
    itemdb_exists_base(nameid)
}

/// The embedded entry, without the run-time changes (setiteminfo).
pub fn itemdb_exists_base(nameid: u32) -> Option<&'static ItemData> {
    index().by_id.get(&nameid).map(|&i| &item_db_file().items[i])
}

// ---------------------------------------------------------------------------------------------- run-time changes

/// Entries changed at run time (script.cpp setiteminfo / setitemscript; built by itemui/itemdb_rt.rs from its
/// table): looked at before the embedded data. `version` is the edit counter the overlay was built from.
#[derive(Default)]
struct Runtime {
    version: u64,
    items: HashMap<u32, &'static ItemData>,
    scripts: HashMap<u32, &'static ItemScripts>,
}

thread_local! {
    static RUNTIME: std::cell::RefCell<Runtime> = std::cell::RefCell::new(Runtime::default());
}

fn runtime_item(nameid: u32) -> Option<&'static ItemData> {
    RUNTIME.with(|r| r.borrow().items.get(&nameid).copied())
}

/// The edit counter this instance's overlay reflects.
pub fn runtime_version() -> u64 {
    RUNTIME.with(|r| r.borrow().version)
}

/// Replaces the overlay (the entries live for the rest of the instance: item_data pointers are never freed).
pub fn set_runtime(version: u64, items: Vec<ItemData>, scripts: Vec<ItemScripts>) {
    let items: HashMap<u32, &'static ItemData> = items.into_iter().map(|d| (d.nameid, &*Box::leak(Box::new(d)))).collect();
    let scripts: HashMap<u32, &'static ItemScripts> = scripts.into_iter().map(|s| (s.nameid, &*Box::leak(Box::new(s)))).collect();
    RUNTIME.with(|r| *r.borrow_mut() = Runtime { version, items, scripts });
}

/// itemdb_search: the item, or the dummy item (ITEMID_DUMMY, "UNKNOWN_ITEM", view id UNKNOWN_ITEM_ID) with rAthena's
/// warning when the id is not in the database.
pub fn itemdb_search(nameid: u32) -> &'static ItemData {
    match itemdb_exists(nameid) {
        Some(it) => it,
        None => {
            log::warn!("itemdb_search: Item ID {nameid} does not exists in the item_db. Using dummy data.");
            itemdb_exists(ITEMID_DUMMY).expect("items_db: the dummy item is always defined")
        }
    }
}

/// ItemDatabase::search_aegisname: case-insensitive (ASCII, util::tolower).
pub fn search_aegisname(name: &str) -> Option<&'static ItemData> {
    index().by_aegis.get(&name.to_ascii_lowercase()).map(|&i| &item_db_file().items[i]).map(|d| runtime_item(d.nameid).unwrap_or(d))
}

/// ItemDatabase::searchname: the AegisName first, then the display name (the last item loaded with that name),
/// both case-insensitive.
pub fn itemdb_searchname(name: &str) -> Option<&'static ItemData> {
    let key = name.to_ascii_lowercase();
    let ix = index();
    ix.by_aegis.get(&key).or_else(|| ix.by_name.get(&key)).map(|&i| &item_db_file().items[i]).map(|d| runtime_item(d.nameid).unwrap_or(d))
}

/// itemdb_searchname_array: every item whose AegisName or Name contains `s` (stristr, ASCII case-insensitive; an empty
/// string matches everything) or whose Name equals it (strcmpi), by ascending id, at most `size` of them.
pub fn itemdb_searchname_array(s: &str, size: usize) -> Vec<&'static ItemData> {
    let needle = s.to_ascii_uppercase();
    let contains = |h: &str| needle.is_empty() || h.to_ascii_uppercase().contains(&needle);
    item_db()
        .iter()
        .filter(|it| contains(&it.name) || contains(&it.ename) || it.ename.eq_ignore_ascii_case(s))
        .take(size)
        .collect()
}

/// The scripts of an item (None: it has none of `Script`, `EquipScript`, `UnEquipScript`), with setitemscript's
/// changes.
pub fn item_scripts(nameid: u32) -> Option<&'static ItemScripts> {
    if let Some(s) = RUNTIME.with(|r| r.borrow().scripts.get(&nameid).copied()) {
        return Some(s);
    }
    item_scripts_base(nameid)
}

/// The embedded scripts of an item.
pub fn item_scripts_base(nameid: u32) -> Option<&'static ItemScripts> {
    // a separate dataset and index: the script text is only parsed when something asks for it
    static SCRIPTS: OnceLock<HashMap<u32, usize>> = OnceLock::new();
    let ix = SCRIPTS.get_or_init(|| item_scripts_file().iter().enumerate().map(|(i, s)| (s.nameid, i)).collect());
    ix.get(&nameid).map(|&i| &item_scripts_file()[i])
}

/// itemdb_name(n): itemdb_search(n)->name (the AegisName).
pub fn itemdb_name(nameid: u32) -> &'static str {
    &itemdb_search(nameid).name
}
/// itemdb_ename(n): the display name.
pub fn itemdb_ename(nameid: u32) -> &'static str {
    &itemdb_search(nameid).ename
}
/// itemdb_type(n).
pub fn itemdb_type(nameid: u32) -> u8 {
    itemdb_search(nameid).r#type
}
/// itemdb_subtype(n).
pub fn itemdb_subtype(nameid: u32) -> u8 {
    itemdb_search(nameid).subtype
}
/// itemdb_weight(n).
pub fn itemdb_weight(nameid: u32) -> u32 {
    itemdb_search(nameid).weight
}
/// itemdb_value_buy(n).
pub fn itemdb_value_buy(nameid: u32) -> u32 {
    itemdb_search(nameid).value_buy
}
/// itemdb_value_sell(n).
pub fn itemdb_value_sell(nameid: u32) -> u32 {
    itemdb_search(nameid).value_sell
}
/// itemdb_viewid(n): the `AliasName` item id (0 when none; the dummy's UNKNOWN_ITEM_ID for unknown ids).
pub fn itemdb_viewid(nameid: u32) -> u32 {
    itemdb_search(nameid).view_id
}
/// itemdb_look(n).
pub fn itemdb_look(nameid: u32) -> u32 {
    itemdb_search(nameid).look
}
/// itemdb_equip(n).
pub fn itemdb_equip(nameid: u32) -> u32 {
    itemdb_search(nameid).equip
}
/// itemdb_slots(n).
pub fn itemdb_slots(nameid: u32) -> u16 {
    itemdb_search(nameid).slots
}
/// itemdb_wlv(n).
pub fn itemdb_wlv(nameid: u32) -> u16 {
    itemdb_search(nameid).weapon_level
}
/// itemdb_range(n).
pub fn itemdb_range(nameid: u32) -> u16 {
    itemdb_search(nameid).range
}
/// itemdb_available(n).
pub fn itemdb_available(nameid: u32) -> bool {
    itemdb_search(nameid).flag.available
}
/// itemdb_dropeffect(n).
pub fn itemdb_dropeffect(nameid: u32) -> u16 {
    itemdb_search(nameid).flag.drop_effect
}
/// itemdb_traderight(n).
pub fn itemdb_traderight(nameid: u32) -> &'static TradeRestriction {
    &itemdb_search(nameid).flag.trade_restriction
}

/// clif.cpp client_nameid (PACKETVER >= 20181121: 32-bit item ids): the id the client is told, the `AliasName` item
/// when there is one.
pub fn client_nameid(server_nameid: u32) -> u32 {
    let view = itemdb_viewid(server_nameid);
    if view > 0 {
        view
    } else {
        server_nameid
    }
}

/// itemdb_isequip2.
pub fn itemdb_isequip2(id: &ItemData) -> bool {
    matches!(id.r#type, IT_WEAPON | IT_ARMOR | IT_AMMO | IT_SHADOWGEAR)
}
/// itemdb_isequip(n).
pub fn itemdb_isequip(nameid: u32) -> bool {
    itemdb_isequip2(itemdb_search(nameid))
}
/// itemdb_isstackable2.
pub fn itemdb_isstackable2(id: &ItemData) -> bool {
    id.is_stackable()
}
/// itemdb_isstackable(n).
pub fn itemdb_isstackable(nameid: u32) -> bool {
    itemdb_search(nameid).is_stackable()
}
/// itemdb_isidentified: whether the item type drops identified (weapons, armors, pet armors and shadow gear don't).
pub fn itemdb_isidentified(nameid: u32) -> bool {
    !matches!(itemdb_type(nameid), IT_WEAPON | IT_ARMOR | IT_PETARMOR | IT_SHADOWGEAR)
}

/// itemdb_typename.
pub fn itemdb_typename(t: u8) -> &'static str {
    match t {
        IT_HEALING => "Potion/Food",
        IT_USABLE => "Usable",
        IT_ETC => "Etc.",
        IT_WEAPON => "Weapon",
        IT_ARMOR => "Armor",
        IT_CARD => "Card",
        IT_PETEGG => "Pet Egg",
        IT_PETARMOR => "Pet Accessory",
        IT_AMMO => "Arrow/Ammunition",
        IT_DELAYCONSUME => "Delay-Consume Usable",
        IT_SHADOWGEAR => "Shadow Equipment",
        IT_CASH => "Cash Usable",
        _ => "Unknown Type",
    }
}

/// itemdb_typename_ammo.
pub fn itemdb_typename_ammo(ammo: u8) -> &'static str {
    match ammo {
        AMMO_ARROW => "Arrow",
        AMMO_DAGGER => "Throwable Dagger",
        AMMO_BULLET => "Bullet",
        AMMO_SHELL => "Shell",
        AMMO_GRENADE => "Grenade",
        AMMO_SHURIKEN => "Shuriken",
        AMMO_KUNAI => "Kunai",
        AMMO_CANNONBALL => "Cannonball",
        AMMO_THROWWEAPON => "Throwable Item/Sling Item",
        _ => "Ammunition",
    }
}

// ---------------------------------------------------------------------------------------------- trade restrictions

/// itemdb_isdropable_sub.
pub fn itemdb_isdropable_sub(item: &ItemData, gmlv: i32, _unused: i32) -> bool {
    !item.flag.trade_restriction.drop || gmlv >= item.gm_lv_trade_override as i32
}
/// itemdb_cantrade_sub.
pub fn itemdb_cantrade_sub(item: &ItemData, gmlv: i32, gmlv2: i32) -> bool {
    let o = item.gm_lv_trade_override as i32;
    !item.flag.trade_restriction.trade || gmlv >= o || gmlv2 >= o
}
/// itemdb_canpartnertrade_sub.
pub fn itemdb_canpartnertrade_sub(item: &ItemData, gmlv: i32, gmlv2: i32) -> bool {
    let o = item.gm_lv_trade_override as i32;
    item.flag.trade_restriction.trade_partner || gmlv >= o || gmlv2 >= o
}
/// itemdb_cansell_sub.
pub fn itemdb_cansell_sub(item: &ItemData, gmlv: i32, _unused: i32) -> bool {
    !item.flag.trade_restriction.sell || gmlv >= item.gm_lv_trade_override as i32
}
/// itemdb_cancartstore_sub.
pub fn itemdb_cancartstore_sub(item: &ItemData, gmlv: i32, _unused: i32) -> bool {
    !item.flag.trade_restriction.cart || gmlv >= item.gm_lv_trade_override as i32
}
/// itemdb_canstore_sub.
pub fn itemdb_canstore_sub(item: &ItemData, gmlv: i32, _unused: i32) -> bool {
    !item.flag.trade_restriction.storage || gmlv >= item.gm_lv_trade_override as i32
}
/// itemdb_canguildstore_sub.
pub fn itemdb_canguildstore_sub(item: &ItemData, gmlv: i32, _unused: i32) -> bool {
    !item.flag.trade_restriction.guild_storage || gmlv >= item.gm_lv_trade_override as i32
}
/// itemdb_canmail_sub.
pub fn itemdb_canmail_sub(item: &ItemData, gmlv: i32, _unused: i32) -> bool {
    !item.flag.trade_restriction.mail || gmlv >= item.gm_lv_trade_override as i32
}
/// itemdb_canauction_sub.
pub fn itemdb_canauction_sub(item: &ItemData, gmlv: i32, _unused: i32) -> bool {
    !item.flag.trade_restriction.auction || gmlv >= item.gm_lv_trade_override as i32
}

/// itemdb_isrestricted: `func` for the item (`nameid`, its `card` array) and, unless it has no slots or is a
/// forged/created/pet item, for each card in its slots. The itemdb_isdropable / cantrade / … macros are this with the
/// matching `_sub` function.
pub fn itemdb_isrestricted(nameid: u32, card: &[u32], gmlv: i32, gmlv2: i32, func: fn(&ItemData, i32, i32) -> bool) -> bool {
    let data = itemdb_search(nameid);
    if !func(data, gmlv, gmlv2) {
        return false;
    }
    if data.slots == 0 || itemdb_isspecial(card.first().copied().unwrap_or(0)) {
        return true;
    }
    for i in 0..data.slots as usize {
        let c = card.get(i).copied().unwrap_or(0);
        if c == 0 {
            continue;
        }
        if !func(itemdb_search(c), gmlv, gmlv2) {
            return false;
        }
    }
    true
}

/// itemdb_ishatched_egg: a pet egg whose pet is out (card[0] == CARD0_PET, attribute 1).
pub fn itemdb_ishatched_egg(card0: u32, attribute: u8) -> bool {
    card0 == CARD0_PET && attribute == 1
}

/// The map properties itemdb_isNoEquip looks at (map.hpp mapdata_flag_vs2, MF_PVP, mapdata_flag_gvg2_no_te,
/// MF_BATTLEGROUND, mapdata_flag_gvg2_te, map_data::zone, MF_RESTRICTED).
#[derive(Clone, Copy, Debug, Default)]
pub struct NoEquipMap {
    pub vs: bool,
    pub pvp: bool,
    pub gvg_no_te: bool,
    pub battleground: bool,
    pub gvg_te: bool,
    pub restricted: bool,
    pub zone: u32,
}

/// itemdb_isNoEquip: true when item_noequip.txt forbids the item on this kind of map.
pub fn itemdb_is_no_equip(id: &ItemData, m: &NoEquipMap) -> bool {
    let f = id.flag.no_equip;
    if f == 0 {
        return false;
    }
    (f & 1 != 0 && !m.vs)
        || (f & 2 != 0 && m.pvp)
        || (f & 4 != 0 && m.gvg_no_te)
        || (f & 8 != 0 && m.battleground)
        || (f & 16 != 0 && m.gvg_te)
        || (f & m.zone != 0 && m.restricted)
}

#[cfg(test)]
mod tests;
