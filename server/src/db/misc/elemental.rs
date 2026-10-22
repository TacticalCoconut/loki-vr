//! elemental.cpp ElementalDatabase (db/elemental_db.yml -> db/re/elemental_db.yml -> import).

use std::collections::HashMap;

use serde::Deserialize;

use crate::db::mobs::{StatusData, ViewData};

/// elemental.hpp enum e_elemental_skillmode.
pub const EL_SKILLMODE_PASSIVE: u8 = 0x1;
pub const EL_SKILLMODE_ASSIST: u8 = 0x2;
pub const EL_SKILLMODE_AGGRESSIVE: u8 = 0x4;
/// elemental.hpp enum elemental_elementalid: the mercelem agent's (crate::elemental::data), re-exported for the
/// mage family's classes and skill_get_elemental_type.
pub use crate::elemental::data::{
    ELEMENTALID_AGNI_L, ELEMENTALID_AGNI_M, ELEMENTALID_AGNI_S, ELEMENTALID_AQUA_L, ELEMENTALID_AQUA_M, ELEMENTALID_AQUA_S, ELEMENTALID_ARDOR,
    ELEMENTALID_DILUVIO, ELEMENTALID_PROCELLA, ELEMENTALID_SERPENS, ELEMENTALID_TERA_L, ELEMENTALID_TERA_M, ELEMENTALID_TERA_S,
    ELEMENTALID_TERREMOTUS, ELEMENTALID_VENTUS_L, ELEMENTALID_VENTUS_M, ELEMENTALID_VENTUS_S,
};
/// mmo.hpp EL_SKILLBASE / MAX_ELEMENTALSKILL.
pub const EL_SKILLBASE: u16 = 8401;
pub const MAX_ELEMENTALSKILL: u16 = 57;

/// elemental.hpp struct s_elemental_skill.
#[derive(Deserialize, Clone, Copy, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct ElementalSkill {
    /// `Mode.<mode>.Skill`.
    pub id: u16,
    /// `Mode.<mode>.Level` (default 1).
    pub lv: u16,
}

/// elemental.hpp struct s_elemental_db.
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct ElementalDb {
    /// `Id` (2114-2125, 20816-20820).
    pub class_: i32,
    /// `AegisName`.
    pub sprite: String,
    /// `Name`.
    pub name: String,
    /// `Level` (default 1).
    pub lv: u16,
    /// `SkillRange` (default 5).
    pub range2: u16,
    /// `ChaseRange` (default 12).
    pub range3: u16,
    /// Hp (default 0) / Sp (1), Attack (rhw.atk, default 1), Attack2 (rhw.matk), Defense/MagicDefense, stats
    /// (default 0), AttackRange, Size/Race/Element/ElementLevel, WalkSpeed (200), AttackDelay (504), AttackMotion
    /// (1020), DamageMotion (360); aspd_rate 1000.
    pub status: StatusData,
    /// look[LOOK_BASE] = Id.
    pub vd: ViewData,
    /// `DamageTaken` (default 100).
    pub damagetaken: u16,
    /// `Mode`: (e_elemental_skillmode, skill), in rAthena's unordered_map iteration order.
    pub skill: Vec<(u8, ElementalSkill)>,
}

impl ElementalDb {
    /// The skill for a mode (`db->skill.find(mode)`).
    pub fn skill_for(&self, mode: u8) -> Option<&ElementalSkill> {
        self.skill.iter().find(|(m, _)| *m == mode).map(|(_, s)| s)
    }
}

#[derive(Deserialize)]
struct ElemFile {
    elementals: Vec<ElementalDb>,
}

/// ElementalDatabase (int32 class -> s_elemental_db).
#[derive(Deserialize)]
#[serde(from = "ElemFile")]
pub struct ElementalDatabase {
    list: Vec<ElementalDb>,
    index: HashMap<i32, usize>,
}

impl From<ElemFile> for ElementalDatabase {
    fn from(f: ElemFile) -> Self {
        ElementalDatabase { index: f.elementals.iter().enumerate().map(|(i, e)| (e.class_, i)).collect(), list: f.elementals }
    }
}

impl ElementalDatabase {
    pub fn find(&self, class_: i32) -> Option<&ElementalDb> {
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
    pub fn iter(&self) -> impl Iterator<Item = &ElementalDb> {
        self.list.iter()
    }
}

crate::dataset!(elemental_db, "misc_elemental", ElementalDatabase);

/// elemental.cpp elemental_get_viewdata.
pub fn elemental_get_viewdata(class_: i32) -> Option<&'static ViewData> {
    elemental_db().find(class_).map(|e| &e.vd)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn agni() {
        assert_eq!(elemental_db().len(), 17);
        let e = elemental_db().find(2114).expect("ELEMENTALID_AGNI_S");
        assert_eq!(e.vd.look[0], 2114);
        assert_eq!(e.status.aspd_rate, 1000);
        assert_eq!(e.status.def_ele, 3); // Fire
        assert!(e.skill.len() == 3);
        for (mode, s) in &e.skill {
            assert!([EL_SKILLMODE_PASSIVE, EL_SKILLMODE_ASSIST, EL_SKILLMODE_AGGRESSIVE].contains(mode));
            assert!(s.id >= EL_SKILLBASE && s.id < EL_SKILLBASE + MAX_ELEMENTALSKILL && s.lv >= 1);
        }
        assert!(e.skill_for(EL_SKILLMODE_AGGRESSIVE).is_some());
    }
}
