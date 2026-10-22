//! itemdb.cpp itemdb_parse_roulette_db: the Roulette of Fortune's prize table (`struct s_roulette_db rd`,
//! itemdb.hpp:3450-3457). rAthena reads it from the `db_roulette` SQL table at start (itemdb_read, only with
//! battle_config.feature_roulette); tools/datagen_mods/items.py (`roulette_db`, `ROULETTE_SQL`) replays the load over
//! the rows a new install's sql-files/roulette_default_data.sql puts there — rows dealt out in order,
//! MAX_ROULETTE_COLUMNS - i to level i, bad rows skipped, short levels filled with Apples — so what is embedded is `rd`
//! as the map-server holds it. (No SQL here: deliberately a start-up table from the data files.)

/// itemdb.hpp MAX_ROULETTE_LEVEL / MAX_ROULETTE_COLUMNS ("client-defined value").
pub const MAX_ROULETTE_LEVEL: usize = 7;
pub const MAX_ROULETTE_COLUMNS: usize = 9;

/// One prize slot: rd.nameid[i][j], rd.qty[i][j], rd.flag[i][j] (flag & 1: the losing slot, which ends the run).
pub type RouletteItem = (u32, u16, i32);

// data/json/items_roulette.json.z: MAX_ROULETTE_LEVEL lists (empty when feature.roulette is off: nothing loaded).
crate::dataset!(roulette_file, "items_roulette", Vec<Vec<RouletteItem>>);

/// The prize table, as the port reads it (a stand-in table in unit tests).
pub trait RouletteDb {
    /// rd.items[level]: how many slots the level has (MAX_ROULETTE_COLUMNS - level once loaded).
    fn items(&self, level: usize) -> usize;
    /// (rd.nameid, rd.qty, rd.flag)[level][idx].
    fn slot(&self, level: usize, idx: usize) -> RouletteItem;
}

impl RouletteDb for [Vec<RouletteItem>] {
    fn items(&self, level: usize) -> usize {
        self.get(level).map(|l| l.len()).unwrap_or(0)
    }
    fn slot(&self, level: usize, idx: usize) -> RouletteItem {
        self.get(level).and_then(|l| l.get(idx)).copied().unwrap_or((0, 0, 0))
    }
}

/// `rd` (itemdb.cpp:36).
pub fn rd() -> &'static [Vec<RouletteItem>] {
    roulette_file().as_slice()
}
