//! quest.cpp QuestDatabase (db/quest_db.yml -> db/re/quest_db.yml -> import).

use std::collections::HashMap;

use serde::Deserialize;

/// mmo.hpp MAX_QUEST_OBJECTIVES.
pub const MAX_QUEST_OBJECTIVES: usize = 3;

/// quest.hpp struct s_quest_objective (`Targets` entry).
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct QuestObjective {
    /// `Id` (0 for Mob targets).
    pub index: u16,
    /// `Mob` (0 for Id targets).
    pub mob_id: u16,
    /// `Count`.
    pub count: u16,
    /// `MinLevel` (1 when only MaxLevel is given; Id targets only).
    pub min_level: u16,
    /// `MaxLevel` (MAX_LEVEL when below MinLevel).
    pub max_level: u16,
    /// `Race` (e_race; default RC_ALL 12).
    pub race: i8,
    /// `Size` (e_size; default SZ_ALL 3).
    pub size: u8,
    /// `Element` (e_element; default ELE_ALL 10).
    pub element: i8,
    /// `Location` as a map id (map_mapindex2mapid; -1 for All / unset). Same numbering as data::maps().
    pub mapid: i16,
    /// `MapName` (the name the quest window shows).
    pub map_name: String,
    /// `MapMobTargets` (monster ids).
    pub mobs_allowed: Vec<u16>,
}

/// quest.hpp struct s_quest_dropitem (`Drops` entry).
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct QuestDropItem {
    /// `Item`.
    pub nameid: u32,
    /// `Count` (default 1; 1 for non-stackable items).
    pub count: u16,
    /// `Rate` (x/10000).
    pub rate: u16,
    /// `Mob` (0: every monster).
    pub mob_id: u16,
}

/// quest.hpp struct s_quest_db.
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct QuestDb {
    /// `Id`.
    pub id: i32,
    /// `TimeLimit`: seconds from now ("+..." , solve_time) or, with time_at, the time of day / week day.
    pub time: i64,
    /// `TimeLimit` without '+': the quest expires at a given time.
    pub time_at: bool,
    /// Week day of a time_at limit (-1 none; a '+' limit on a new entry leaves it 0 as the struct was zeroed).
    pub time_week: i32,
    /// `Targets` (an updated target is pushed again, as rAthena does).
    pub objectives: Vec<QuestObjective>,
    /// `Drops` (likewise).
    pub dropitem: Vec<QuestDropItem>,
    /// `Title`.
    pub name: String,
}

#[derive(Deserialize)]
struct QuestFile {
    quests: Vec<QuestDb>,
}

/// QuestDatabase (uint32 id -> s_quest_db).
#[derive(Deserialize)]
#[serde(from = "QuestFile")]
pub struct QuestDatabase {
    list: Vec<QuestDb>,
    index: HashMap<i32, usize>,
}

impl From<QuestFile> for QuestDatabase {
    fn from(f: QuestFile) -> Self {
        QuestDatabase { index: f.quests.iter().enumerate().map(|(i, q)| (q.id, i)).collect(), list: f.quests }
    }
}

impl QuestDatabase {
    pub fn find(&self, id: i32) -> Option<&QuestDb> {
        self.index.get(&id).map(|&i| &self.list[i])
    }
    pub fn len(&self) -> usize {
        self.list.len()
    }
    pub fn is_empty(&self) -> bool {
        self.list.is_empty()
    }
    pub fn iter(&self) -> impl Iterator<Item = &QuestDb> {
        self.list.iter()
    }
}

crate::dataset!(quest_db, "misc_quest", QuestDatabase);

/// quest.cpp quest_search.
pub fn quest_search(quest_id: i32) -> Option<&'static QuestDb> {
    quest_db().find(quest_id)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn quests() {
        assert_eq!(quest_db().len(), 4821);
        let q = quest_search(1001).expect("quest 1001");
        assert!(!q.name.is_empty());
        // Illusion of Moonlight 100 Kills: one Id target on pay_d03_i restricted to six monsters
        let q = quest_search(3447).expect("quest 3447");
        assert_eq!(q.name, "Illusion of Moonlight 100 Kills");
        assert_eq!(q.objectives.len(), 1);
        let o = &q.objectives[0];
        assert_eq!((o.index, o.mob_id, o.count, o.race, o.size, o.element), (1, 0, 100, 12, 3, 10));
        assert_eq!(Some(o.mapid as u16), crate::data::maps().id_of("pay_d03_i"));
        assert_eq!(o.mobs_allowed.len(), 6);
        assert_eq!((q.time, q.time_at, q.time_week), (0, false, -1));
        // Get Rid of Bakonawa: TIYANAK drops Lost_Belongings at 30%
        let q = quest_search(1180).expect("quest 1180");
        assert_eq!(q.dropitem.len(), 1);
        let d = &q.dropitem[0];
        assert_eq!((d.rate, d.count), (3000, 1));
        assert_eq!(Some(d.mob_id as u32), crate::db::mobs::mobdb_search_aegisname("TIYANAK").map(|m| m.id));
        // TimeLimit "4h": time of day 04:00, "+3h": three hours from now
        assert!(quest_db().iter().any(|q| q.time_at && q.time == 4 * 3600 && q.time_week == -1));
        assert!(quest_db().iter().any(|q| !q.time_at && q.time == 3 * 3600 && q.time_week == 0));
        assert!(quest_db().iter().any(|q| q.time_at && q.time == 4 * 3600 && q.time_week == 1)); // Monday 4h
    }
}
