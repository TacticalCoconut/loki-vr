//! npc.cpp StylistDatabase (db/stylist.yml -> db/re/stylist.yml, db/import/stylist.yml): the stylist window's
//! options per look (hair, colors, headgears, body style) and their costs for humans and doram.

/// status.hpp look types used by the stylist.
pub const LOOK_HAIR: u16 = 1;
pub const LOOK_HEAD_BOTTOM: u16 = 3;
pub const LOOK_HEAD_TOP: u16 = 4;
pub const LOOK_HEAD_MID: u16 = 5;
pub const LOOK_HAIR_COLOR: u16 = 6;
pub const LOOK_CLOTHES_COLOR: u16 = 7;
pub const LOOK_BODY2: u16 = 13;

/// npc.hpp struct s_stylist_costs (`CostsHuman` / `CostsDoram`).
#[derive(serde::Deserialize, Debug, Clone)]
pub struct StylistCosts {
    /// `Price` (default 0).
    pub price: u32,
    /// `RequiredItem` / `RequiredItemBox` (default 0).
    pub required_item: u32,
    pub required_item_box: u32,
}

/// npc.hpp struct s_stylist_entry (one `Options` entry).
#[derive(serde::Deserialize, Debug, Clone)]
pub struct StylistEntry {
    pub look: u16,
    /// `Index` (non-zero).
    pub index: i16,
    /// `Value`: an item id for headgears, else the style/color (hair styles/colors and cloth colors checked against
    /// conf/battle/client.conf min/max_hair_style, min/max_hair_color, min/max_cloth_color).
    pub value: u32,
    /// `CostsHuman` (none: not available to humans).
    pub human: Option<StylistCosts>,
    /// `CostsDoram` (none: not available to doram).
    pub doram: Option<StylistCosts>,
}

/// npc.hpp struct s_stylist_list (one `Look`).
#[derive(serde::Deserialize, Debug, Clone)]
pub struct StylistList {
    /// `Look` (LOOK_*).
    pub look: u16,
    /// `Options`, in the unordered_map's iteration order.
    pub entries: Vec<StylistEntry>,
}

crate::dataset!(stylist_db, "items_stylist", Vec<StylistList>);

/// stylist_db.find(look).
pub fn stylist_list(look: u16) -> Option<&'static StylistList> {
    stylist_db().iter().find(|s| s.look == look)
}

/// The entry for `index` of a look (list->entries.find(index)).
pub fn stylist_entry(look: u16, index: i16) -> Option<&'static StylistEntry> {
    stylist_list(look)?.entries.iter().find(|e| e.index == index)
}
