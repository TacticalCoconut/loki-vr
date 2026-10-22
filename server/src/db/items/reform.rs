//! itemdb.cpp ItemReformDatabase (db/re/item_reform.yml + import): reform tickets and the items they turn into others.

use std::collections::HashMap;
use std::sync::OnceLock;

/// itemdb.hpp struct s_item_reform_base.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemReformBase {
    /// `BaseItem`.
    pub item_id: u32,
    /// `MinimumRefine` (default 0) / `MaximumRefine` (default MAX_REFINE).
    pub minimum_refine: u16,
    pub maximum_refine: u16,
    /// `RequiredRandomOptions` (default 0).
    pub required_random_options: u16,
    /// `CardsAllowed` (default true).
    pub cards_allowed: bool,
    /// `Materials`: (item id, amount), in the unordered_map's iteration order (amount 0 removes one).
    pub materials: Vec<(u32, u16)>,
    /// `ResultItem`.
    pub result_item_id: u32,
    /// `ChangeRefine` (refineChange, -MAX_REFINE..MAX_REFINE, default 0).
    pub refine_change: i16,
    /// `RandomOptionGroup` (id, default none).
    pub random_option_group: Option<u16>,
    /// `ClearSlots` / `RemoveEnchantgrade` (default false).
    pub clear_slots: bool,
    pub remove_enchantgrade: bool,
}

/// itemdb.hpp struct s_item_reform.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemReform {
    /// `Item` (the reform ticket).
    pub item_id: u32,
    /// `BaseItems`, in the unordered_map's iteration order.
    pub base_items: Vec<ItemReformBase>,
}

crate::dataset!(item_reform_db, "items_reform", Vec<ItemReform>);

static INDEX: OnceLock<HashMap<u32, usize>> = OnceLock::new();

/// item_reform_db.find.
pub fn item_reform(item_id: u32) -> Option<&'static ItemReform> {
    let ix = INDEX.get_or_init(|| item_reform_db().iter().enumerate().map(|(i, e)| (e.item_id, i)).collect());
    ix.get(&item_id).map(|&i| &item_reform_db()[i])
}

impl ItemReform {
    /// base_items.find(item_id).
    pub fn base_item(&self, item_id: u32) -> Option<&ItemReformBase> {
        self.base_items.iter().find(|b| b.item_id == item_id)
    }
}
