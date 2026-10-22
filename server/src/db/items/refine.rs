//! status.cpp RefineDatabase (db/refine.yml -> db/re/refine.yml, db/import/refine.yml): refine chances, costs and
//! bonuses per refine group (REFINE_TYPE_*), weapon/armor level and refine level.

use super::{ItemData, IT_ARMOR, IT_SHADOWGEAR, IT_WEAPON};

/// status.hpp e_refine_type.
pub const REFINE_TYPE_ARMOR: u16 = 0;
pub const REFINE_TYPE_WEAPON: u16 = 1;
pub const REFINE_TYPE_SHADOW_ARMOR: u16 = 2;
pub const REFINE_TYPE_SHADOW_WEAPON: u16 = 3;
/// status.hpp e_refine_cost_type.
pub const REFINE_COST_NORMAL: u16 = 0;
pub const REFINE_COST_HD: u16 = 1;
pub const REFINE_COST_ENRICHED: u16 = 2;
/// status.hpp MAX_REFINE (RENEWAL).
pub const MAX_REFINE: u16 = 20;
/// map.hpp EQP_SHADOW_WEAPON.
const EQP_SHADOW_WEAPON: u32 = 0x20000;

/// status.hpp struct s_refine_cost (one `Chances` entry).
#[derive(serde::Deserialize, Debug, Clone)]
pub struct RefineCost {
    /// `Type` (REFINE_COST_*).
    pub index: u16,
    /// `Material` (default 0).
    pub nameid: u32,
    /// `Rate` (chance, 1..10000, default 0).
    pub chance: u16,
    /// `Price` (default 0).
    pub zeny: u32,
    /// `BreakingRate` (default 0).
    pub breaking_rate: u16,
    /// `DowngradeAmount` (default 0).
    pub downgrade_amount: u16,
}

/// status.hpp struct s_refine_level_info (one `RefineLevels` entry; `level` is 0-based: YAML level - 1).
#[derive(serde::Deserialize, Debug, Clone)]
pub struct RefineLevelInfo {
    pub level: u16,
    /// `Bonus` (default 0).
    pub bonus: u32,
    /// `RandomBonus` (randombonus_max, default 0).
    pub randombonus_max: u32,
    /// `BlacksmithBlessingAmount` (default 0).
    pub blessing_amount: u16,
    /// `BroadcastSuccess` / `BroadcastFailure` (default false).
    pub broadcast_success: bool,
    pub broadcast_failure: bool,
    /// `Chances`, in the unordered_map's iteration order.
    pub costs: Vec<RefineCost>,
}

/// status.hpp struct s_refine_levels_info (one `Levels` entry: a weapon/armor level).
#[derive(serde::Deserialize, Debug, Clone)]
pub struct RefineLevelsInfo {
    pub level: u16,
    pub levels: Vec<RefineLevelInfo>,
}

/// status.hpp struct s_refine_info (one `Group`).
#[derive(serde::Deserialize, Debug, Clone)]
pub struct RefineInfo {
    /// `Group` (REFINE_TYPE_*; the key of the database).
    pub group_id: u16,
    pub levels: Vec<RefineLevelsInfo>,
}

crate::dataset!(refine_db, "items_refine", Vec<RefineInfo>);

impl RefineLevelInfo {
    /// costs.find(REFINE_COST_*).
    pub fn cost(&self, index: u16) -> Option<&RefineCost> {
        self.costs.iter().find(|c| c.index == index)
    }
}

/// RefineDatabase::calculate_refine_info: (refine type, level) of an item.
pub fn calculate_refine_info(data: &ItemData) -> Option<(u16, u16)> {
    match data.r#type {
        IT_WEAPON => Some((REFINE_TYPE_WEAPON, data.weapon_level)),
        IT_ARMOR => Some((REFINE_TYPE_ARMOR, data.armor_level)),
        IT_SHADOWGEAR => Some((if data.equip == EQP_SHADOW_WEAPON { REFINE_TYPE_SHADOW_WEAPON } else { REFINE_TYPE_SHADOW_ARMOR }, 1)),
        _ => None,
    }
}

/// RefineDatabase::findLevelInfoSub (`refine` is 0-based and capped to MAX_REFINE).
fn find_level_info_sub(data: &ItemData, refine: u16) -> Option<&'static RefineLevelInfo> {
    if data.flag.no_refine {
        return None;
    }
    let refine = refine.min(MAX_REFINE);
    let (t, level) = calculate_refine_info(data)?;
    let info = refine_db().iter().find(|r| r.group_id == t)?;
    let levels = info.levels.iter().find(|l| l.level == level)?;
    levels.levels.iter().find(|l| l.level == refine)
}

/// RefineDatabase::findLevelInfo: the row for refining an item at `refine` to the next level (none at MAX_REFINE).
pub fn find_level_info(data: &ItemData, refine: u8) -> Option<&'static RefineLevelInfo> {
    if refine as u16 >= MAX_REFINE {
        return None;
    }
    find_level_info_sub(data, refine as u16)
}

/// RefineDatabase::findCurrentLevelInfo: the row of the item's current refine level (none at +0).
pub fn find_current_level_info(data: &ItemData, refine: u8) -> Option<&'static RefineLevelInfo> {
    if refine > 0 {
        find_level_info_sub(data, refine as u16 - 1)
    } else {
        None
    }
}
