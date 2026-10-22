//! battleground.cpp BattlegroundDatabase (db/battleground_db.yml -> import). The queues and running battlegrounds are
//! the battleground system's.

use serde::Deserialize;

/// battleground.hpp MAX_BG_MEMBERS.
pub const MAX_BG_MEMBERS: i32 = 30;

/// battleground.hpp struct s_battleground_team (`Locations[].TeamA` / `TeamB`).
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct BattlegroundTeam {
    /// `RespawnX`.
    pub warp_x: u16,
    /// `RespawnY`.
    pub warp_y: u16,
    /// `QuitEvent` (EVENT_NAME_LENGTH).
    pub quit_event: String,
    /// `DeathEvent`.
    pub death_event: String,
    /// `ActiveEvent`.
    pub active_event: String,
    /// `Variable` (NAME_LENGTH).
    pub bg_id_var: String,
}

/// battleground.hpp struct s_battleground_map (`Locations` entry).
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct BattlegroundMap {
    /// Counter over the location's teams (rAthena sets it once per team: 1, 3, 5, ... with two teams).
    pub id: i32,
    /// `Map` (mapindex_name2id).
    pub mapindex: u16,
    /// The map name for `mapindex`.
    pub map_name: String,
    pub team1: BattlegroundTeam,
    pub team2: BattlegroundTeam,
    /// `StartEvent`.
    pub bgcallscript: String,
    pub is_reserved: bool,
}

/// battleground.hpp struct s_battleground_type.
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct BattlegroundType {
    /// `Id`.
    pub id: i32,
    /// `Name` (NAME_LENGTH).
    pub name: String,
    /// `MinPlayers` (1..MAX_BG_MEMBERS/2, default 1).
    pub required_players: i32,
    /// `MaxPlayers` (default MAX_BG_MEMBERS/2).
    pub max_players: i32,
    /// `MinLevel` (default 1).
    pub min_lvl: i32,
    /// `MaxLevel` (default MAX_LEVEL).
    pub max_lvl: i32,
    /// `Locations`.
    pub maps: Vec<BattlegroundMap>,
    /// `Deserter` seconds (default 600).
    pub deserter_time: u32,
    /// `StartDelay` seconds (default 0).
    pub start_delay: u32,
    /// `Join.Solo` (default true).
    pub solo: bool,
    /// `Join.Party` (default true).
    pub party: bool,
    /// `Join.Guild` (default true).
    pub guild: bool,
    /// `JobRestrictions` (JOB_ constants).
    pub job_restrictions: Vec<i32>,
}

#[derive(Deserialize)]
struct BgFile {
    battlegrounds: Vec<BattlegroundType>,
}

/// BattlegroundDatabase (uint32 id -> s_battleground_type); iteration in rAthena's order.
#[derive(Deserialize)]
#[serde(from = "BgFile")]
pub struct BattlegroundDatabase {
    list: Vec<BattlegroundType>,
}

impl From<BgFile> for BattlegroundDatabase {
    fn from(f: BgFile) -> Self {
        BattlegroundDatabase { list: f.battlegrounds }
    }
}

impl BattlegroundDatabase {
    pub fn find(&self, id: i32) -> Option<&BattlegroundType> {
        self.list.iter().find(|b| b.id == id)
    }
    pub fn len(&self) -> usize {
        self.list.len()
    }
    pub fn is_empty(&self) -> bool {
        self.list.is_empty()
    }
    pub fn iter(&self) -> impl Iterator<Item = &BattlegroundType> {
        self.list.iter()
    }
}

crate::dataset!(battleground_db, "misc_battleground", BattlegroundDatabase);

/// battleground.cpp bg_search_name (stricmp, first match in iteration order).
pub fn bg_search_name(name: &str) -> Option<&'static BattlegroundType> {
    battleground_db().iter().find(|b| b.name.eq_ignore_ascii_case(name))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn tierra() {
        assert_eq!(battleground_db().len(), 5);
        let b = battleground_db().find(1).expect("bg 1");
        assert_eq!(b.name, "Tierra Gorge");
        assert_eq!((b.required_players, b.max_players, b.min_lvl, b.max_lvl), (6, MAX_BG_MEMBERS / 2, 80, 275));
        assert_eq!((b.deserter_time, b.solo, b.party, b.guild), (600, true, true, true));
        assert_eq!(b.job_restrictions.len(), 7);
        assert_eq!(b.maps[0].map_name, "bat_a01");
        assert_eq!(b.maps[0].bgcallscript, "start#bat_a01::OnReadyCheck");
        assert_eq!(b.maps[0].id, 1);
        assert_eq!(bg_search_name("tierra gorge").map(|b| b.id), Some(1));
    }
}
