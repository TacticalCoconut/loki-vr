//! status.cpp EnchantgradeDatabase (db/enchantgrade.yml -> db/re/enchantgrade.yml, db/import/enchantgrade.yml):
//! grade upgrade chances and costs per item type (weapon/armor) and level. loadingFinished clears the catalyst of
//! grades without an AmountPerStep.

use super::{ItemData, IT_ARMOR, IT_WEAPON};

/// mmo.hpp e_enchantgrade.
pub const ENCHANTGRADE_NONE: u16 = 0;
pub const ENCHANTGRADE_D: u16 = 1;
pub const ENCHANTGRADE_C: u16 = 2;
pub const ENCHANTGRADE_B: u16 = 3;
pub const ENCHANTGRADE_A: u16 = 4;
pub const MAX_ENCHANTGRADE: u16 = ENCHANTGRADE_A;

/// status.hpp struct s_enchantgradeoption (one `Options` entry).
#[derive(serde::Deserialize, Debug, Clone)]
pub struct EnchantgradeOption {
    /// `Option`.
    pub id: u16,
    /// `Item` (default 0).
    pub item: u32,
    /// `Amount` (default 1).
    pub amount: u16,
    /// `Zeny` (default 0).
    pub zeny: u32,
    /// `BreakingRate` (default 0).
    pub breaking_rate: u16,
    /// `DowngradeAmount` (default 0).
    pub downgrade_amount: u16,
}

/// The `catalyst` part of s_enchantgradelevel.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct EnchantgradeCatalyst {
    /// `Catalyst.Item`, `AmountPerStep`, `MaximumSteps`, `ChanceIncrease` (all 0 when AmountPerStep is 0).
    pub item: u32,
    pub amount_per_step: u16,
    pub maximum_steps: u16,
    pub chance_increase: u16,
}

/// status.hpp struct s_enchantgradelevel (one `Grades` entry: the upgrade from this grade to the next).
#[derive(serde::Deserialize, Debug, Clone)]
pub struct EnchantgradeLevel {
    /// `Grade` (ENCHANTGRADE_*).
    pub grade: u16,
    /// `Chances`: chance per refine level 0..=MAX_REFINE (0 when not listed).
    pub chances: Vec<u16>,
    /// `Bonus` (default 0).
    pub bonus: u16,
    /// `AnnounceSuccess` (default true), `AnnounceFail` (default false); `Announce` sets both.
    pub announce_success: bool,
    pub announce_fail: bool,
    pub catalyst: EnchantgradeCatalyst,
    /// `Options`, sorted by option (std::map).
    pub options: Vec<EnchantgradeOption>,
}

/// status.hpp struct s_enchantgrade (one `Type`).
#[derive(serde::Deserialize, Debug, Clone)]
pub struct Enchantgrade {
    /// `Type` (IT_WEAPON or IT_ARMOR).
    pub itemtype: u16,
    /// `Levels`: (weapon/armor level, its grades sorted by grade), sorted by level (std::map).
    pub levels: Vec<(u16, Vec<EnchantgradeLevel>)>,
}

crate::dataset!(enchantgrade_db, "items_enchantgrade", Vec<Enchantgrade>);

/// enchantgrade_db.find(item type).
pub fn enchantgrade(itemtype: u16) -> Option<&'static Enchantgrade> {
    enchantgrade_db().iter().find(|e| e.itemtype == itemtype)
}

impl EnchantgradeLevel {
    /// options.find(option).
    pub fn option(&self, id: u16) -> Option<&EnchantgradeOption> {
        self.options.iter().find(|o| o.id == id)
    }
}

/// EnchantgradeDatabase::findCurrentLevelInfo: for an item of grade `enchantgrade`, the grade entry
/// (e_enchantgrade)(enchantgrade - 1) of its weapon/armor level (grade 0 wraps to 65535: none).
pub fn find_current_level_info(data: &ItemData, enchantgrade: u8) -> Option<&'static EnchantgradeLevel> {
    let eg = self::enchantgrade(data.r#type as u16)?;
    let level = match data.r#type {
        IT_WEAPON => data.weapon_level,
        IT_ARMOR => data.armor_level,
        _ => 0,
    };
    let (_, grades) = eg.levels.iter().find(|(l, _)| *l == level)?;
    let want = (enchantgrade as i32 - 1) as u16;
    grades.iter().find(|g| g.grade == want)
}
