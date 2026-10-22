//! npc.cpp BarterDatabase (npc/barters.yml -> npc/re/merchants/barters.yml -> npc/re/merchants/barters/*.yml, then
//! npc/custom/barters.yml): barter shops. BarterDatabase::loadingFinished creates one NPC per barter (in the
//! database's iteration order, which is this list's order); that, the NPC's map placement and the persisted stock of
//! stock-limited items (SQL `barter` table) belong to the NPC module.

use std::collections::HashMap;
use std::sync::OnceLock;

/// npc.hpp struct s_npc_barter_requirement (one `RequiredItems` entry).
#[derive(serde::Deserialize, Debug, Clone)]
pub struct BarterRequirement {
    /// `Index`.
    pub index: u16,
    /// `Item`.
    pub nameid: u32,
    /// `Amount` (default 1, capped to MAX_AMOUNT).
    pub amount: u16,
    /// `Refine` (default -1: any; capped to MAX_REFINE).
    pub refine: i8,
}

/// npc.hpp struct s_npc_barter_item (one `Items` entry).
#[derive(serde::Deserialize, Debug, Clone)]
pub struct BarterItem {
    /// `Index`.
    pub index: u16,
    /// `Item`.
    pub nameid: u32,
    /// Stock > 0 (stockLimited).
    pub stock_limited: bool,
    /// `Stock` (default 0 = unlimited): the initial stock.
    pub stock: u32,
    /// `Zeny` (price, default 0).
    pub price: u32,
    /// `Refine` (default 0).
    pub refine: i8,
    /// `RequiredItems`, sorted by index (std::map), at most MAX_BARTER_REQUIREMENTS.
    pub requirements: Vec<BarterRequirement>,
}

/// npc.hpp struct s_npc_barter.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct Barter {
    /// `Name` (the NPC name, the database key).
    pub name: String,
    /// `Map` (a map this server loads; none: the NPC is not on a map, barter->m = -1).
    pub map: Option<String>,
    /// `X` / `Y` (default 0).
    pub x: u16,
    pub y: u16,
    /// `Direction` (DIR_*, default DIR_NORTH).
    pub dir: u8,
    /// `Sprite` (default JT_FAKENPC).
    pub sprite: i16,
    /// `Items`, sorted by index (std::map).
    pub items: Vec<BarterItem>,
    /// Uses the extended barter window (a price, a refine, no or several requirements, or a refine requirement).
    pub extended: bool,
    /// loadingFinished creates an NPC for it (conf/battle/feature.conf feature.barter / feature.barter_extended).
    pub npc: bool,
}

crate::dataset!(barter_db, "items_barters", Vec<Barter>);

static INDEX: OnceLock<HashMap<String, usize>> = OnceLock::new();

/// barter_db.find(name) (exact name).
pub fn barter(name: &str) -> Option<&'static Barter> {
    let ix = INDEX.get_or_init(|| barter_db().iter().enumerate().map(|(i, b)| (b.name.clone(), i)).collect());
    ix.get(name).map(|&i| &barter_db()[i])
}

impl Barter {
    /// items.find(index).
    pub fn item(&self, index: u16) -> Option<&BarterItem> {
        self.items.iter().find(|i| i.index == index)
    }
}
