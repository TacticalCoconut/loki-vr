//! guild.cpp CastleDatabase (db/castle_db.yml -> db/re/castle_db.yml -> import) with loadingFinished (warping is turned
//! off for castles without ClientId / WarpX / WarpY). The run-time castle state (owner, economy, guardians) is the
//! guild system's.

use serde::Deserialize;

/// mmo.hpp enum e_woe_type.
pub const WOE_FIRST_EDITION: i32 = 1;
pub const WOE_SECOND_EDITION: i32 = 2;
pub const WOE_THIRD_EDITION: i32 = 3;

/// mmo.hpp struct guild_castle (the fields castle_db.yml sets).
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct GuildCastle {
    /// `Id`.
    pub castle_id: i32,
    /// `Map` as mapindex_name2idx (db/map_index.txt numbering).
    pub mapindex: i32,
    /// The map name for `mapindex`.
    pub map_name: String,
    /// `Name` (char[NAME_LENGTH]).
    pub castle_name: String,
    /// `Npc` (char[NPC_NAME_LENGTH]).
    pub castle_event: String,
    /// `Type` (WOE_ constant, default WOE_FIRST_EDITION).
    #[serde(rename = "type")]
    pub type_: i32,
    /// `ClientId` (default 0).
    pub client_id: u16,
    /// `WarpEnabled` (default false; cleared when ClientId / WarpX / WarpY is missing).
    pub warp_enabled: bool,
    /// `WarpX`.
    pub warp_x: u16,
    /// `WarpY`.
    pub warp_y: u16,
    /// `WarpCost` (default 100).
    pub zeny: u32,
    /// `WarpCostSiege` (default 100000).
    pub zeny_siege: u32,
}

#[derive(Deserialize)]
struct CastleFile {
    castles: Vec<GuildCastle>,
}

/// CastleDatabase (int32 id -> guild_castle); iteration in rAthena's order.
#[derive(Deserialize)]
#[serde(from = "CastleFile")]
pub struct CastleDatabase {
    list: Vec<GuildCastle>,
}

impl From<CastleFile> for CastleDatabase {
    fn from(f: CastleFile) -> Self {
        CastleDatabase { list: f.castles }
    }
}

impl CastleDatabase {
    pub fn find(&self, castle_id: i32) -> Option<&GuildCastle> {
        self.list.iter().find(|c| c.castle_id == castle_id)
    }
    pub fn len(&self) -> usize {
        self.list.len()
    }
    pub fn is_empty(&self) -> bool {
        self.list.is_empty()
    }
    pub fn iter(&self) -> impl Iterator<Item = &GuildCastle> {
        self.list.iter()
    }
    /// CastleDatabase::mapindex2gc (first match in iteration order).
    pub fn mapindex2gc(&self, mapindex: i32) -> Option<&GuildCastle> {
        self.list.iter().find(|c| c.mapindex == mapindex)
    }
    /// CastleDatabase::mapname2gc (mapindex_name2id normalises the name first; unknown names match nothing).
    pub fn mapname2gc(&self, mapname: &str) -> Option<&GuildCastle> {
        let n = super::mapindex_getmapname(mapname);
        self.list.iter().find(|c| !c.map_name.is_empty() && c.map_name == n)
    }
    /// CastleDatabase::find_by_clientid (first match in iteration order).
    pub fn find_by_clientid(&self, client_id: u16) -> Option<&GuildCastle> {
        self.list.iter().find(|c| c.client_id == client_id)
    }
}

crate::dataset!(castle_db, "misc_castle", CastleDatabase);

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn castles() {
        assert_eq!(castle_db().len(), 44);
        let c = castle_db().find(0).expect("castle 0");
        assert_eq!(c.map_name, "aldeg_cas01");
        assert_eq!(c.castle_name, "Neuschwanstein");
        assert_eq!(c.castle_event, "Agit#aldeg_cas01");
        assert_eq!(c.type_, WOE_FIRST_EDITION);
        assert_eq!((c.client_id, c.warp_enabled, c.warp_x, c.warp_y, c.zeny, c.zeny_siege), (6, true, 212, 175, 100, 100000));
        assert_eq!(castle_db().find_by_clientid(6).map(|c| c.castle_id), Some(0));
        assert_eq!(castle_db().mapname2gc("aldeg_cas01.gat").map(|c| c.castle_id), Some(0));
        assert_eq!(castle_db().mapindex2gc(c.mapindex).map(|c| c.castle_id), Some(0));
        for c in castle_db().iter() {
            if c.warp_enabled {
                assert!(c.client_id != 0 && c.warp_x != 0 && c.warp_y != 0);
            }
        }
    }
}
