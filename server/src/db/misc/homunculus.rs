//! homunculus.cpp HomunculusDatabase (db/homunculus_db.yml -> db/re/homunculus_db.yml -> import) and HomExpDatabase
//! (db/exp_homun.yml -> db/re/exp_homun.yml -> import).

use std::collections::HashMap;

use serde::Deserialize;

/// mmo.hpp HM_CLASS_BASE / HM_CLASS_MAX (homdb_checkid) and the homunculus skill range.
pub const HM_CLASS_BASE: i32 = 6001;
pub const HM_CLASS_MAX: i32 = 6052;
pub const HM_SKILLBASE: u16 = 8001;
pub const MAX_HOMUNSKILL: u16 = 59;

/// homunculus.hpp struct s_hom_stats (`Status` rows: Base / GrowthMinimum / GrowthMaximum / EvolutionMinimum /
/// EvolutionMaximum per stat).
#[derive(Deserialize, Clone, Copy, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct HomStats {
    /// `Hp` (HP).
    pub hp: u32,
    /// `Sp` (SP).
    pub sp: u32,
    pub str: u16,
    pub agi: u16,
    pub vit: u16,
    pub int_: u16,
    pub dex: u16,
    pub luk: u16,
}

/// homunculus.hpp struct s_homun_skill_tree_entry (`SkillTree` entry).
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct HomunSkillTreeEntry {
    /// `Skill`.
    pub id: u16,
    /// `MaxLevel` (capped to the skill's MaxLevel).
    pub max: u16,
    /// `RequiredLevel` (capped to battle_config hom_max_level, hom_S_max_level for HT_S).
    pub need_level: u16,
    /// `RequiredIntimacy` times 100 (at most 1000 before).
    pub intimacy: u32,
    /// `RequireEvolution`.
    pub evolution: bool,
    /// `Required`: (skill id, level), in rAthena's unordered_map iteration order.
    pub need: Vec<(u16, u16)>,
}

/// homunculus.hpp struct s_homunculus_db.
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct HomunculusDb {
    /// `Class` (MER_ constant).
    pub base_class: i32,
    /// `EvolutionClass` (default: Class).
    pub evo_class: i32,
    /// `Name` (char[NAME_LENGTH]).
    pub name: String,
    /// Base stats (Hp defaults to 1).
    pub base: HomStats,
    pub gmin: HomStats,
    pub gmax: HomStats,
    pub emin: HomStats,
    pub emax: HomStats,
    /// `Food` (default ITEMID_PET_FOOD 537).
    pub food_id: i32,
    /// `AttackDelay` (baseASPD; at most MIN_ASPD, default 700).
    pub base_aspd: u16,
    /// `HungryDelay` (default 60000).
    pub hungry_delay: i32,
    /// `Element` (default ELE_NEUTRAL).
    pub element: i8,
    /// `Race` (default RC_DEMIHUMAN).
    pub race: i8,
    /// `Size`, then `EvolutionSize` (rAthena writes both into base_size; a new entry without EvolutionSize gets
    /// SZ_MEDIUM).
    pub base_size: u8,
    /// Never set by the loader.
    pub evo_size: u8,
    /// `SkillTree`.
    pub skill_tree: Vec<HomunSkillTreeEntry>,
}

#[derive(Deserialize)]
struct HomFile {
    homunculus: Vec<HomunculusDb>,
    exp: Vec<(u16, u64)>,
}

/// HomunculusDatabase (int32 class -> s_homunculus_db) with HomExpDatabase (level -> exp).
#[derive(Deserialize)]
#[serde(from = "HomFile")]
pub struct HomunculusDatabase {
    list: Vec<HomunculusDb>,
    index: HashMap<i32, usize>,
    exp: HashMap<u16, u64>,
}

impl From<HomFile> for HomunculusDatabase {
    fn from(f: HomFile) -> Self {
        HomunculusDatabase {
            index: f.homunculus.iter().enumerate().map(|(i, h)| (h.base_class, i)).collect(),
            list: f.homunculus,
            exp: f.exp.into_iter().collect(),
        }
    }
}

impl HomunculusDatabase {
    pub fn find(&self, class_: i32) -> Option<&HomunculusDb> {
        self.index.get(&class_).map(|&i| &self.list[i])
    }
    pub fn len(&self) -> usize {
        self.list.len()
    }
    pub fn is_empty(&self) -> bool {
        self.list.is_empty()
    }
    pub fn iter(&self) -> impl Iterator<Item = &HomunculusDb> {
        self.list.iter()
    }
    /// HomunculusDatabase::homun_search: the entry for `class_`, else the first (iteration order) whose evo_class it is.
    pub fn homun_search(&self, class_: i32) -> Option<&HomunculusDb> {
        self.find(class_).or_else(|| self.list.iter().find(|h| h.evo_class == class_))
    }
    /// HomExpDatabase::get_nextexp (0 for levels the table does not have).
    pub fn get_nextexp(&self, level: u16) -> u64 {
        self.exp.get(&level).copied().unwrap_or(0)
    }
    pub fn exp_len(&self) -> usize {
        self.exp.len()
    }
}

crate::dataset!(homunculus_db, "misc_homunculus", HomunculusDatabase);

/// homunculus.hpp homdb_checkid.
pub fn homdb_checkid(id: i32) -> bool {
    (HM_CLASS_BASE..=HM_CLASS_MAX).contains(&id)
}

/// homunculus.cpp hom_class2type over hom_class2mapid: HT_REG 1, HT_EVO 2, HT_S 4 (a class hom_class2mapid does not
/// know maps to -1, whose bits read as HT_EVO).
pub fn hom_class2type(class_: i32) -> i32 {
    match class_ {
        6001..=6008 => 1,
        6048..=6052 => 4,
        _ => 2,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn lif() {
        assert_eq!(homunculus_db().len(), 13);
        assert_eq!(homunculus_db().exp_len(), 274);
        let lif = crate::data::constant("MER_LIF").unwrap() as i32;
        let h = homunculus_db().find(lif).expect("Lif");
        assert_eq!(h.name, "Lif");
        assert_eq!(h.evo_class, crate::data::constant("MER_LIF_H").unwrap() as i32);
        assert_eq!(homunculus_db().homun_search(h.evo_class).map(|x| x.base_class), Some(lif));
        assert!(!h.skill_tree.is_empty());
        assert!(h.skill_tree.iter().all(|e| e.id >= HM_SKILLBASE && e.id < HM_SKILLBASE + MAX_HOMUNSKILL));
        let b = h.base;
        assert_eq!((b.hp, b.sp, b.str, b.agi, b.vit, b.int_, b.dex, b.luk), (150, 40, 17, 20, 15, 35, 24, 12));
        assert_eq!((h.gmin.hp, h.gmax.hp, h.emin.hp, h.emax.hp), (60, 100, 800, 2400));
        assert_eq!((h.food_id, h.base_aspd, h.hungry_delay), (537, 700, 60000));
        // HLIF_AVOID (8002) needs HLIF_HEAL (8001) level 3
        let avoid = h.skill_tree.iter().find(|e| e.id == 8002).expect("HLIF_AVOID");
        assert_eq!((avoid.max, avoid.need.clone()), (5, vec![(8001, 3)]));
        assert_eq!(homunculus_db().get_nextexp(1), 101);
        assert_eq!(homunculus_db().get_nextexp(0), 0);
        assert_eq!(hom_class2type(6050), 4);
    }
}
