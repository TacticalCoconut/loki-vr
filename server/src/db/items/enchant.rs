//! itemdb.cpp ItemEnchantDatabase (db/re/item_enchant.yml + import): the enchant UI's per-slot tables.

use std::collections::HashMap;
use std::sync::OnceLock;

/// itemdb.hpp struct s_item_enchant_normal_sub.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemEnchantNormalSub {
    /// `Item`.
    pub item_id: u32,
    /// `Chance` (1..100000, required for a new entry).
    pub chance: u32,
}

/// itemdb.hpp struct s_item_enchant_normal: the enchants offered at one enchant grade.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemEnchantNormal {
    /// `Enchantgrade`.
    pub enchantgrade: u16,
    /// `Items`, in the unordered_map's iteration order.
    pub enchants: Vec<ItemEnchantNormalSub>,
}

/// The `normal` part of s_item_enchant_slot.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemEnchantSlotNormal {
    /// `Price` (default 0).
    pub zeny: u32,
    /// `Materials`: (item id, amount), unordered_map iteration order.
    pub materials: Vec<(u32, u16)>,
    /// `Chance` (default 100000).
    pub chance: u32,
    /// `EnchantgradeBonus`: (grade, chance increase) for grades 0..MAX_ENCHANTGRADE (0 by default). Note the loader
    /// stores the slot's `Chance` for every listed grade (it reads "Chance" from the slot node), as ported.
    pub enchantgrade_chance_increase: Vec<(u16, u32)>,
    /// `Enchants` per enchant grade, unordered_map iteration order.
    pub enchants: Vec<ItemEnchantNormal>,
}

/// itemdb.hpp struct s_item_enchant_perfect.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemEnchantPerfect {
    /// `Item`.
    pub item_id: u32,
    /// `Price` (default 0).
    pub zeny: u32,
    /// `Materials`: (item id, amount).
    pub materials: Vec<(u32, u16)>,
}

/// itemdb.hpp struct s_item_enchant_upgrade.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemEnchantUpgrade {
    /// `Enchant`.
    pub enchant_item_id: u32,
    /// `Upgrade`.
    pub upgrade_item_id: u32,
    /// `Price` (default 0).
    pub zeny: u32,
    /// `Materials`: (item id, amount).
    pub materials: Vec<(u32, u16)>,
}

/// itemdb.hpp struct s_item_enchant_slot.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemEnchantSlot {
    /// `Slot` (0..MAX_SLOTS-1).
    pub slot: u16,
    pub normal: ItemEnchantSlotNormal,
    /// `PerfectEnchants`, unordered_map iteration order.
    pub perfect: Vec<ItemEnchantPerfect>,
    /// `Upgrades`, unordered_map iteration order.
    pub upgrade: Vec<ItemEnchantUpgrade>,
}

/// The `reset` part of s_item_enchant.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemEnchantReset {
    /// `Reset.Price` (default 0).
    pub zeny: u32,
    /// `Reset.Materials`.
    pub materials: Vec<(u32, u16)>,
    /// `Reset.Chance` (default 0).
    pub chance: u32,
}

/// itemdb.hpp struct s_item_enchant.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemEnchant {
    /// `Id`.
    pub id: u64,
    /// `TargetItems` (enabled ones, in file order).
    pub target_item_ids: Vec<u32>,
    /// `MinimumRefine` / `MinimumEnchantgrade` (default 0).
    pub minimum_refine: u16,
    pub minimum_enchantgrade: u16,
    /// `AllowRandomOptions` (default true).
    pub allow_random_options: bool,
    pub reset: ItemEnchantReset,
    /// `Order`: the slots in the order they are enchanted.
    pub order: Vec<u16>,
    /// `Slots`, unordered_map iteration order.
    pub slots: Vec<ItemEnchantSlot>,
}

crate::dataset!(item_enchant_db, "items_enchant", Vec<ItemEnchant>);

static INDEX: OnceLock<HashMap<u64, usize>> = OnceLock::new();

/// item_enchant_db.find.
pub fn item_enchant(id: u64) -> Option<&'static ItemEnchant> {
    let ix = INDEX.get_or_init(|| item_enchant_db().iter().enumerate().map(|(i, e)| (e.id, i)).collect());
    ix.get(&id).map(|&i| &item_enchant_db()[i])
}

impl ItemEnchant {
    /// slots.find(slot).
    pub fn slot(&self, slot: u16) -> Option<&ItemEnchantSlot> {
        self.slots.iter().find(|s| s.slot == slot)
    }
}
