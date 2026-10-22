//! pet.cpp PetDatabase (db/pet_db.yml -> db/re/pet_db.yml -> import), keyed by the pet's monster id.

use std::collections::HashMap;

use serde::Deserialize;

/// pet.hpp struct s_pet_evo_data (`Evolution` entry).
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct PetEvoData {
    /// `Target` (monster id).
    pub target_mob_id: u16,
    /// `ItemRequirements`: (item id, `Amount`), in rAthena's unordered_map iteration order.
    pub requirements: Vec<(u32, u16)>,
}

/// pet.hpp struct s_pet_db.
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct PetDb {
    /// `Mob` (monster id).
    pub class_: u16,
    /// `TameItem` (itemID; default 0).
    pub item_id: u32,
    /// `EggItem` (EggID).
    pub egg_id: u32,
    /// `EquipItem` (AcceID; default 0).
    pub acce_id: u32,
    /// `FoodItem` (FoodID; default 0).
    pub food_id: u32,
    /// `Fullness` (at most 100).
    pub fullness: u16,
    /// `HungryDelay` in seconds, stored in ms (default 60000).
    pub hungry_delay: u32,
    /// `HungerIncrease` (default 20).
    pub hunger_increase: i32,
    /// `IntimacyFed` (r_hungry; default 50).
    pub r_hungry: i32,
    /// `IntimacyOverfed` (r_full; default -100).
    pub r_full: i32,
    /// `IntimacyStart` (at most 10000; default 250).
    pub intimate: u32,
    /// `IntimacyOwnerDie` (default -20).
    pub die: i32,
    /// `IntimacyHungry` (default -5).
    pub hungry_intimacy_dec: i32,
    /// `CaptureRate` (x/10000).
    pub capture: u16,
    /// `SpecialPerformance` (default true).
    pub s_perfor: bool,
    /// `AttackRate` (default 10001: never).
    pub attack_rate: u16,
    /// `RetaliateRate` (default 10001).
    pub defence_attack_rate: u16,
    /// `ChangeTargetRate` (default 10001).
    pub change_target_rate: u16,
    /// `AllowAutoFeed` (default false).
    pub allow_autofeed: bool,
    /// `Evolution`: (target monster id -> data), in iteration order.
    pub evolution_data: Vec<PetEvoData>,
    /// `SupportScript` (source text).
    pub pet_support_script: Option<String>,
    /// `Script` (pet_bonus_script, source text).
    pub pet_bonus_script: Option<String>,
}

/// pet.hpp enum e_pet_itemtype (pet_db_search).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum PetItemType {
    Catch,
    Egg,
    Equip,
    Food,
}

#[derive(Deserialize)]
struct PetFile {
    pets: Vec<PetDb>,
}

/// PetDatabase (uint16 monster id -> s_pet_db).
#[derive(Deserialize)]
#[serde(from = "PetFile")]
pub struct PetDatabase {
    list: Vec<PetDb>,
    index: HashMap<u16, usize>,
}

impl From<PetFile> for PetDatabase {
    fn from(f: PetFile) -> Self {
        PetDatabase { index: f.pets.iter().enumerate().map(|(i, p)| (p.class_, i)).collect(), list: f.pets }
    }
}

impl PetDatabase {
    pub fn find(&self, mob_id: u16) -> Option<&PetDb> {
        self.index.get(&mob_id).map(|&i| &self.list[i])
    }
    pub fn len(&self) -> usize {
        self.list.len()
    }
    pub fn is_empty(&self) -> bool {
        self.list.is_empty()
    }
    pub fn iter(&self) -> impl Iterator<Item = &PetDb> {
        self.list.iter()
    }
}

crate::dataset!(pet_db, "misc_pet", PetDatabase);

/// pet.cpp pet_db_search: the first pet (iteration order) whose taming / egg / accessory / food item is `key`.
pub fn pet_db_search(key: i32, type_: PetItemType) -> Option<&'static PetDb> {
    pet_db().iter().find(|p| {
        let v = match type_ {
            PetItemType::Catch => p.item_id,
            PetItemType::Egg => p.egg_id,
            PetItemType::Equip => p.acce_id,
            PetItemType::Food => p.food_id,
        };
        v as i64 == key as i64
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn poring_pet() {
        assert_eq!(pet_db().len(), 107);
        let p = pet_db().find(1002).expect("PORING");
        // db/re/pet_db.yml: Unripe_Apple (619), Poring_Egg (9001), Backpack (10013), Apple_Juice (531)
        assert_eq!((p.item_id, p.egg_id, p.acce_id, p.food_id), (619, 9001, 10013, 531));
        assert_eq!((p.fullness, p.hungry_delay, p.hunger_increase), (3, 60000, 20));
        assert_eq!((p.intimate, p.r_hungry, p.r_full, p.hungry_intimacy_dec, p.die), (250, 50, -100, -5, -20));
        assert_eq!(p.capture, 2000);
        assert!(p.s_perfor);
        assert_eq!((p.attack_rate, p.defence_attack_rate, p.change_target_rate), (10001, 10001, 10001));
        assert!(p.pet_bonus_script.as_deref().unwrap_or("").contains("bonus bLuk,3;"));
        assert!(p.pet_support_script.is_none());
        // Evolution: MASTERING (1090) from 10 Leaf_Of_Yggdrasil (610) and 3 Unripe_Apple (619)
        assert_eq!(p.evolution_data.len(), 1);
        assert_eq!(p.evolution_data[0].target_mob_id, 1090);
        let mut req = p.evolution_data[0].requirements.clone();
        req.sort();
        assert_eq!(req, vec![(610, 10), (619, 3)]);
        assert_eq!(pet_db_search(9001, PetItemType::Egg).map(|p| p.class_), Some(1002));
        assert_eq!(pet_db_search(619, PetItemType::Catch).map(|p| p.class_), Some(1002));
        // evolutions keep their requirement lists
        assert!(pet_db().iter().any(|p| p.evolution_data.iter().any(|e| !e.requirements.is_empty())));
    }
}
