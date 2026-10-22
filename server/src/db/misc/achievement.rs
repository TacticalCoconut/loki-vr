//! achievement.cpp AchievementDatabase (db/achievement_db.yml -> db/re/achievement_db.yml -> import, with
//! loadingFinished's dependent check) and AchievementLevelDatabase (db/re/achievement_level_db.yml).

use std::collections::HashMap;

use serde::Deserialize;

/// achievement.hpp enum e_achievement_group.
pub const AG_NONE: i32 = 0;
pub const AG_ADD_FRIEND: i32 = 1;
pub const AG_ADVENTURE: i32 = 2;
pub const AG_BABY: i32 = 3;
pub const AG_BATTLE: i32 = 4;
pub const AG_CHATTING: i32 = 5;
pub const AG_CHATTING_COUNT: i32 = 6;
pub const AG_CHATTING_CREATE: i32 = 7;
pub const AG_CHATTING_DYING: i32 = 8;
pub const AG_EAT: i32 = 9;
pub const AG_GET_ITEM: i32 = 10;
pub const AG_GET_ZENY: i32 = 11;
pub const AG_GOAL_ACHIEVE: i32 = 12;
pub const AG_GOAL_LEVEL: i32 = 13;
pub const AG_GOAL_STATUS: i32 = 14;
pub const AG_JOB_CHANGE: i32 = 15;
pub const AG_MARRY: i32 = 16;
pub const AG_PARTY: i32 = 17;
pub const AG_ENCHANT_FAIL: i32 = 18;
pub const AG_ENCHANT_SUCCESS: i32 = 19;
pub const AG_SPEND_ZENY: i32 = 20;
pub const AG_TAMING: i32 = 21;
pub const AG_MAX: i32 = 22;

/// common/mmo.hpp MAX_ACHIEVEMENT_OBJECTIVES, ACHIEVEMENT_NAME_LENGTH.
pub const MAX_ACHIEVEMENT_OBJECTIVES: usize = 10;
pub const ACHIEVEMENT_NAME_LENGTH: usize = 50;

/// achievement.hpp struct achievement_target.
#[derive(Deserialize, Clone, Copy, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct AchievementTarget {
    /// `Targets[].Mob` (AG_BATTLE / AG_TAMING only).
    pub mob: i32,
    /// `Targets[].Count` (default 1).
    pub count: i32,
}

/// achievement.hpp s_achievement_db::ach_reward (`Rewards`).
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct AchievementReward {
    /// `Item`.
    pub nameid: u32,
    /// `Amount` (1 when Rewards is given without it, else 0).
    pub amount: u16,
    /// `Script` (source text).
    pub script: Option<String>,
    /// `TitleId` (TITLE_BASE..TITLE_MAX).
    pub title_id: u32,
}

/// achievement.hpp struct s_achievement_db.
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct AchievementDb {
    /// `Id`.
    pub achievement_id: u32,
    /// `Name`.
    pub name: String,
    /// `Group` (AG_ constant).
    pub group: i32,
    /// `Targets`: (target id, target), a std::map (ascending ids).
    pub targets: Vec<(u16, AchievementTarget)>,
    /// `Dependents` (unknown ids removed by loadingFinished, as far as its erase loop gets).
    pub dependent_ids: Vec<u32>,
    /// `Condition`, wrapped in `achievement_condition( ... );` when it does not call it (source text).
    pub condition: Option<String>,
    /// `Map` (AG_CHATTING): map_mapname2mapid, a map id despite the field name; -1 none.
    pub mapindex: i16,
    /// The map name for `mapindex`.
    pub map_name: String,
    pub rewards: AchievementReward,
    /// `Score`.
    pub score: i32,
    pub has_dependent: i32,
}

#[derive(Deserialize)]
struct AchFile {
    achievements: Vec<AchievementDb>,
    achievement_mobs: Vec<i32>,
    feature_achievement: bool,
    levels: Vec<(u16, u16)>,
}

/// AchievementDatabase (uint32 id -> s_achievement_db) with its achievement_mobs list, and the level table.
#[derive(Deserialize)]
#[serde(from = "AchFile")]
pub struct AchievementDatabase {
    list: Vec<AchievementDb>,
    index: HashMap<u32, usize>,
    achievement_mobs: Vec<i32>,
    feature_achievement: bool,
    levels: HashMap<u16, u16>,
}

impl From<AchFile> for AchievementDatabase {
    fn from(f: AchFile) -> Self {
        AchievementDatabase {
            index: f.achievements.iter().enumerate().map(|(i, a)| (a.achievement_id, i)).collect(),
            list: f.achievements,
            achievement_mobs: f.achievement_mobs,
            feature_achievement: f.feature_achievement,
            levels: f.levels.into_iter().collect(),
        }
    }
}

impl AchievementDatabase {
    pub fn find(&self, id: u32) -> Option<&AchievementDb> {
        self.index.get(&id).map(|&i| &self.list[i])
    }
    pub fn exists(&self, id: u32) -> bool {
        self.index.contains_key(&id)
    }
    pub fn len(&self) -> usize {
        self.list.len()
    }
    pub fn is_empty(&self) -> bool {
        self.list.is_empty()
    }
    /// `for (auto &ach : achievement_db)`: the std::unordered_map's iteration order (datagen's libstdc++ model; the
    /// 361 ids were checked against an unordered_map<uint32> filled in file order and built with g++ 13.3).
    pub fn iter(&self) -> impl Iterator<Item = &AchievementDb> {
        self.list.iter()
    }
    /// battle_config.feature_achievement (conf/battle/feature.conf `feature.achievement: on`).
    pub fn feature(&self) -> bool {
        self.feature_achievement
    }
    /// AchievementDatabase::mobexists (false while battle_config feature.achievement is off).
    pub fn mobexists(&self, mob_id: u32) -> bool {
        self.feature_achievement && self.achievement_mobs.contains(&(mob_id as i32))
    }
    /// achievement_level_db.find(level)->points for a zero-based level.
    pub fn level_points(&self, level: u16) -> Option<u16> {
        self.levels.get(&level).copied()
    }
    pub fn levels_len(&self) -> usize {
        self.levels.len()
    }
}

crate::dataset!(achievement_db, "misc_achievement", AchievementDatabase);

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn achievements() {
        assert_eq!(achievement_db().len(), 361);
        assert_eq!(achievement_db().levels_len(), 20);
        assert!(achievement_db().level_points(0).is_some());
        for a in achievement_db().iter() {
            for d in &a.dependent_ids {
                assert!(achievement_db().exists(*d));
            }
            if let Some(c) = &a.condition {
                assert!(c.contains("achievement_condition"));
            }
            assert!(a.targets.windows(2).all(|w| w[0].0 < w[1].0));
            for (_, t) in &a.targets {
                if t.mob != 0 {
                    assert!(a.group == AG_BATTLE || a.group == AG_TAMING);
                    assert!(achievement_db().mobexists(t.mob as u32));
                }
            }
        }
    }
}
