//! itemdb.cpp ItemPackageDatabase (db/re/item_packages.yml + import): selectable packages (the client's package
//! selection window).

use std::collections::HashMap;
use std::sync::OnceLock;

/// itemdb.hpp struct s_item_package_item.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemPackageItem {
    /// `Item`.
    pub item_id: u32,
    /// `Amount` (default 1, capped to MAX_AMOUNT).
    pub amount: u16,
    /// `RentalHours` (default 0).
    pub rentalhours: u16,
    /// `Refine` (default 0, capped to MAX_REFINE).
    pub refine: u16,
    /// `Grade` (ENCHANTGRADE_*, default NONE).
    pub grade: u16,
    /// `RandomOptionGroup` (id, default none).
    pub random_option_group: Option<u16>,
}

/// itemdb.hpp struct s_item_package_group.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemPackageGroup {
    /// `Group`.
    pub group_index: u32,
    /// `Items`, in the unordered_map's iteration order.
    pub items: Vec<ItemPackageItem>,
}

/// itemdb.hpp struct s_item_package.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemPackage {
    /// `Item` (the package).
    pub item_id: u32,
    /// `Groups`, in the unordered_map's iteration order.
    pub groups: Vec<ItemPackageGroup>,
}

crate::dataset!(item_package_db, "items_packages", Vec<ItemPackage>);

static INDEX: OnceLock<HashMap<u32, usize>> = OnceLock::new();

/// item_package_db.find.
pub fn item_package(item_id: u32) -> Option<&'static ItemPackage> {
    let ix = INDEX.get_or_init(|| item_package_db().iter().enumerate().map(|(i, e)| (e.item_id, i)).collect());
    ix.get(&item_id).map(|&i| &item_package_db()[i])
}

impl ItemPackage {
    /// groups.find(group_index).
    pub fn group(&self, group_index: u32) -> Option<&ItemPackageGroup> {
        self.groups.iter().find(|g| g.group_index == group_index)
    }
}
