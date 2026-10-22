//! mercenary.cpp MercenaryDatabase (db/mercenary_db.yml, then db/re/mercenary_db.yml and the import, which update or
//! add entries by `Id`).

use std::collections::HashMap;

use serde::Deserialize;

use crate::db::mobs::{StatusData, ViewData};

/// mmo.hpp MC_SKILLBASE / MAX_MERCSKILL.
pub const MC_SKILLBASE: u16 = 8201;
pub const MAX_MERCSKILL: u16 = 41;

/// mercenary.hpp struct s_mercenary_db.
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct MercenaryDb {
    /// `Id`.
    pub class_: i32,
    /// `AegisName`.
    pub sprite: String,
    /// `Name`.
    pub name: String,
    /// `Level` (at most MAX_LEVEL, default 1).
    pub lv: u16,
    /// `SkillRange` (default 0).
    pub range2: u16,
    /// `ChaseRange` (default 0).
    pub range3: u16,
    /// Hp/Sp (default 1), Attack (rhw.atk), Attack2 (rhw.atk2 = Attack + Attack2), Defense/MagicDefense, stats
    /// (default 1), AttackRange, Size/Race/Element/ElementLevel, WalkSpeed (150), AttackDelay (4000), AttackMotion
    /// (2000), DamageMotion; aspd_rate 1000.
    pub status: StatusData,
    /// Never set by the loader (all zero).
    pub vd: ViewData,
    /// `Skills`: (skill id, `MaxLevel`), in rAthena's unordered_map iteration order.
    pub skill: Vec<(u16, u16)>,
}

impl MercenaryDb {
    /// mercenary.cpp mercenary_checkskill (the database part: the level, or 0).
    pub fn checkskill(&self, skill_id: u16) -> u16 {
        self.skill.iter().find(|(id, _)| *id == skill_id).map(|(_, lv)| *lv).unwrap_or(0)
    }
}

#[derive(Deserialize)]
struct MercFile {
    mercenaries: Vec<MercenaryDb>,
}

/// MercenaryDatabase (int32 class -> s_mercenary_db).
#[derive(Deserialize)]
#[serde(from = "MercFile")]
pub struct MercenaryDatabase {
    list: Vec<MercenaryDb>,
    index: HashMap<i32, usize>,
}

impl From<MercFile> for MercenaryDatabase {
    fn from(f: MercFile) -> Self {
        MercenaryDatabase { index: f.mercenaries.iter().enumerate().map(|(i, m)| (m.class_, i)).collect(), list: f.mercenaries }
    }
}

impl MercenaryDatabase {
    pub fn find(&self, class_: i32) -> Option<&MercenaryDb> {
        self.index.get(&class_).map(|&i| &self.list[i])
    }
    pub fn exists(&self, class_: i32) -> bool {
        self.index.contains_key(&class_)
    }
    pub fn len(&self) -> usize {
        self.list.len()
    }
    pub fn is_empty(&self) -> bool {
        self.list.is_empty()
    }
    pub fn iter(&self) -> impl Iterator<Item = &MercenaryDb> {
        self.list.iter()
    }
}

crate::dataset!(mercenary_db, "misc_mercenary", MercenaryDatabase);

/// mercenary.cpp mercenary_get_viewdata.
pub fn mercenary_get_viewdata(class_: u16) -> Option<&'static ViewData> {
    mercenary_db().find(class_ as i32).map(|m| &m.vd)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn archer01() {
        // db/mercenary_db.yml 44 entries, db/re/mercenary_db.yml 20 more
        assert_eq!(mercenary_db().len(), 64);
        let m = mercenary_db().find(6017).expect("MER_ARCHER01");
        assert_eq!((m.sprite.as_str(), m.name.as_str(), m.lv, m.range2, m.range3), ("MER_ARCHER01", "Mina", 20, 10, 0));
        let s = &m.status;
        assert_eq!((s.max_hp, s.max_sp, s.rhw.atk, s.rhw.atk2, s.rhw.range, s.def, s.mdef), (256, 200, 170, 255, 10, 7, 5));
        assert_eq!((s.speed, s.adelay, s.amotion, s.dmotion, s.race, s.ele_lv), (150, 700, 432, 300, 7, 1));
        assert_eq!((m.checkskill(8207), m.checkskill(8233), m.checkskill(8201)), (2, 1, 0));
        assert_eq!(m.status.aspd_rate, 1000);
        assert!(m.lv >= 1 && m.status.max_hp > 0);
        assert!(m.skill.iter().all(|(id, lv)| *id >= MC_SKILLBASE && *id < MC_SKILLBASE + MAX_MERCSKILL && *lv > 0));
        assert!(m.status.rhw.atk2 >= m.status.rhw.atk);
        assert_eq!(mercenary_get_viewdata(6017).map(|v| v.look[0]), Some(0));
    }
}
