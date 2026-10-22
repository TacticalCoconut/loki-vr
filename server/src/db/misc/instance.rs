//! instance.cpp InstanceDatabase (db/instance_db.yml -> db/re/instance_db.yml -> import). Running instances are the
//! instance system's.

use serde::Deserialize;

/// mmo.hpp struct point (the entry position).
#[derive(Deserialize, Clone, Copy, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct Point {
    /// Map id (map_mapindex2mapid; data::maps() numbering).
    pub map: i16,
    pub x: i16,
    pub y: i16,
}

/// instance.hpp struct s_instance_db.
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct InstanceDb {
    /// `Id`.
    pub id: i32,
    /// `Name` (unique).
    pub name: String,
    /// `TimeLimit` seconds (default 3600).
    pub limit: i64,
    /// `IdleTimeOut` seconds (default 300).
    pub timeout: i64,
    /// `NoNpc` (default false).
    pub nonpc: bool,
    /// `NoMapFlag` (default false).
    pub nomapflag: bool,
    /// `Destroyable` (default true).
    pub destroyable: bool,
    /// TimeLimit 0.
    pub infinite_limit: bool,
    /// IdleTimeOut 0.
    pub infinite_timeout: bool,
    /// `Enter` (Map, X, Y).
    pub enter: Point,
    /// The map name of `enter.map`.
    pub enter_map_name: String,
    /// `AdditionalMaps` (map ids).
    pub maplist: Vec<i16>,
    /// Their names.
    pub maplist_names: Vec<String>,
}

#[derive(Deserialize)]
struct InstFile {
    instances: Vec<InstanceDb>,
}

/// InstanceDatabase (int32 id -> s_instance_db); iteration in rAthena's order.
#[derive(Deserialize)]
#[serde(from = "InstFile")]
pub struct InstanceDatabase {
    list: Vec<InstanceDb>,
}

impl From<InstFile> for InstanceDatabase {
    fn from(f: InstFile) -> Self {
        InstanceDatabase { list: f.instances }
    }
}

impl InstanceDatabase {
    pub fn find(&self, id: i32) -> Option<&InstanceDb> {
        self.list.iter().find(|i| i.id == id)
    }
    pub fn len(&self) -> usize {
        self.list.len()
    }
    pub fn is_empty(&self) -> bool {
        self.list.is_empty()
    }
    pub fn iter(&self) -> impl Iterator<Item = &InstanceDb> {
        self.list.iter()
    }
}

crate::dataset!(instance_db, "misc_instance", InstanceDatabase);

/// instance.cpp instance_search_db_name (strcmp, first match in iteration order).
pub fn instance_search_db_name(name: &str) -> Option<&'static InstanceDb> {
    instance_db().iter().find(|i| i.name == name)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn instances() {
        // db/re/instance_db.yml's 78, and the Loki test servers' import (data/overlay/db/import: id 1000, a test instance)
        assert_eq!(instance_db().iter().filter(|i| i.id < 1000).count(), 78);
        let i = instance_db().find(1).expect("instance 1");
        assert_eq!(instance_search_db_name(&i.name).map(|x| x.id), Some(1));
        assert_eq!(Some(i.enter.map as u16), crate::data::maps().id_of(&i.enter_map_name));
        assert!(i.enter.x > 0 && i.enter.y > 0);
        for i in instance_db().iter() {
            assert_eq!(i.maplist.len(), i.maplist_names.len());
            assert!(!i.maplist.contains(&i.enter.map));
        }
    }
}
