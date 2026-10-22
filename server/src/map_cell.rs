//! map.cpp's dynamic cells and invisible walls: map_setcell / map_setgatcell (a cell's walkable / shootable / water bits
//! and its flags), map_getcellp's flag checks and CELL_GETTYPE (map_cell2gat), map_iwall_set / map_iwall_remove /
//! map_iwall_exist / map_iwall_get (iwall_db), clif_changemapcell (ZC_UPDATE_MAPINFO).
//!
//! rAthena changes map_data::cell in place. Here the cells a script (or later a skill) changed are rows of `map_cell_mod`
//! (the whole mapcell bit set of that cell), and map.rs getcellp — which path search calls without a context — reads
//! them through an in-memory copy. The copy is checked against the table's generation counter at the start of every
//! reducer ([`sync`], from world::run), so a rolled-back call or a new module instance can never leave it stale.

use std::cell::RefCell;
use std::collections::HashMap;

use spacetimedb::{table, Table};

use crate::data::{self, MapData};
use crate::world::Ctx;

/// map.hpp cell_t.
pub const CELL_WALKABLE: i32 = 0;
pub const CELL_SHOOTABLE: i32 = 1;
pub const CELL_WATER: i32 = 2;
pub const CELL_NPC: i32 = 3;
pub const CELL_BASILICA: i32 = 4;
pub const CELL_LANDPROTECTOR: i32 = 5;
pub const CELL_NOVENDING: i32 = 6;
pub const CELL_NOCHAT: i32 = 7;
pub const CELL_MAELSTROM: i32 = 8;
pub const CELL_ICEWALL: i32 = 9;
pub const CELL_NOBUYINGSTORE: i32 = 10;

/// map.hpp cell_chk.
pub const CELL_GETTYPE: i32 = 0;
pub const CELL_CHKWALL: i32 = 1;
pub const CELL_CHKWATER: i32 = 2;
pub const CELL_CHKCLIFF: i32 = 3;
pub const CELL_CHKPASS: i32 = 4;
pub const CELL_CHKREACH: i32 = 5;
pub const CELL_CHKNOPASS: i32 = 6;
pub const CELL_CHKNOREACH: i32 = 7;
pub const CELL_CHKSTACK: i32 = 8;
pub const CELL_CHKNPC: i32 = 9;
pub const CELL_CHKBASILICA: i32 = 10;
pub const CELL_CHKLANDPROTECTOR: i32 = 11;
pub const CELL_CHKNOVENDING: i32 = 12;
pub const CELL_CHKNOCHAT: i32 = 13;
pub const CELL_CHKMAELSTROM: i32 = 14;
pub const CELL_CHKICEWALL: i32 = 15;
pub const CELL_CHKNOBUYINGSTORE: i32 = 16;

// struct mapcell bits as stored here
const B_WALKABLE: u16 = 1 << 0;
const B_SHOOTABLE: u16 = 1 << 1;
const B_WATER: u16 = 1 << 2;
/// cell.npc as a script / skill set it (the module otherwise answers CELL_CHKNPC from the NPC touch areas)
const B_NPC: u16 = 1 << 3;
const B_NPC_SET: u16 = 1 << 4;
const B_BASILICA: u16 = 1 << 5;
const B_LANDPROTECTOR: u16 = 1 << 6;
const B_NOVENDING: u16 = 1 << 7;
const B_NOCHAT: u16 = 1 << 8;
const B_MAELSTROM: u16 = 1 << 9;
const B_ICEWALL: u16 = 1 << 10;
const B_NOBUYINGSTORE: u16 = 1 << 11;

/// A cell whose mapcell bits differ from the map cache's.
#[table(accessor = map_cell_mod, index(accessor = by_map, btree(columns = [map])))]
#[derive(Clone, Debug)]
pub struct MapCellMod {
    #[primary_key]
    pub key: u64,
    pub map: u16,
    pub x: i16,
    pub y: i16,
    pub bits: u16,
}

/// The generation of `map_cell_mod` (one row, key 0): bumped by every change.
#[table(accessor = map_cell_gen)]
pub struct MapCellGen {
    #[primary_key]
    pub key: u8,
    pub generation: u64,
}

/// iwall_db: an invisible wall (struct iwall_data).
#[table(accessor = iwall)]
#[derive(Clone, Debug)]
pub struct IWall {
    #[primary_key]
    pub wall_name: String,
    pub m: u16,
    pub x: i16,
    pub y: i16,
    pub size: i16,
    pub dir: i8,
    pub shootable: bool,
}

struct Cache {
    generation: Option<u64>,
    cells: HashMap<(u16, i16, i16), u16>,
}

thread_local! {
    static CACHE: RefCell<Cache> = RefCell::new(Cache { generation: None, cells: HashMap::new() });
}

fn key(m: u16, x: i16, y: i16) -> u64 {
    ((m as u64) << 32) | ((x as u16 as u64) << 16) | (y as u16 as u64)
}

fn generation(c: &Ctx) -> u64 {
    c.db().map_cell_gen().key().find(0).map(|g| g.generation).unwrap_or(0)
}

/// Reloads the in-memory copy when the table moved on (called at the start of every reducer).
pub fn sync(c: &Ctx) {
    let g = generation(c);
    let fresh = CACHE.with(|k| k.borrow().generation == Some(g));
    if fresh {
        return;
    }
    let cells: HashMap<(u16, i16, i16), u16> = c.db().map_cell_mod().iter().map(|r| ((r.map, r.x, r.y), r.bits)).collect();
    CACHE.with(|k| {
        let mut k = k.borrow_mut();
        k.generation = Some(g);
        k.cells = cells;
    });
}

/// The changed bits of a cell, if a script or skill changed it.
pub fn cell_bits(m: u16, x: i16, y: i16) -> Option<u16> {
    CACHE.with(|k| k.borrow().cells.get(&(m, x, y)).copied())
}

/// map.cpp map_gat2cell (map.rs gat2cell) as bits: the cell as the map cache has it.
fn gat_bits(gat: u8) -> u16 {
    let (w, s, wa) = crate::map::gat2cell(gat);
    (w as u16 * B_WALKABLE) | (s as u16 * B_SHOOTABLE) | (wa as u16 * B_WATER)
}

fn base_bits(md: &MapData, x: i16, y: i16) -> u16 {
    gat_bits(md.gat(x, y))
}

/// The cell's current terrain bits (walkable, shootable, water) for map.rs getcellp.
pub fn terrain(md: &MapData, x: i16, y: i16) -> (bool, bool, bool) {
    let b = cell_bits(md.id, x, y).unwrap_or_else(|| base_bits(md, x, y));
    (b & B_WALKABLE != 0, b & B_SHOOTABLE != 0, b & B_WATER != 0)
}

fn bits_of(c: &Ctx, md: &MapData, x: i16, y: i16) -> u16 {
    let _ = c;
    cell_bits(md.id, x, y).unwrap_or_else(|| base_bits(md, x, y))
}

/// map.cpp map_cell2gat.
fn cell2gat(b: u16) -> i32 {
    let (w, s, wa) = (b & B_WALKABLE != 0, b & B_SHOOTABLE != 0, b & B_WATER != 0);
    match (w, s, wa) {
        (true, true, false) => 0,
        (false, false, false) => 1,
        (true, true, true) => 3,
        (false, true, false) => 5,
        _ => {
            log::warn!("map_cell2gat: cell has no matching gat type");
            1 // default to 'wall'
        }
    }
}

/// map.cpp map_getcell(m, x, y, cellchk) for every cell_chk (map_getcellp; CELL_NOSTACK is not defined). `m` None =
/// an unknown map (m < 0): 0.
pub fn getcell(c: &Ctx, m: Option<u16>, x: i16, y: i16, chk: i32) -> i32 {
    let Some(m) = m else { return 0 };
    let Some(md) = data::maps().get(m) else { return 0 };
    // NOTE: this intentionally overrides the last row and column
    if x < 0 || x >= md.xs - 1 || y < 0 || y >= md.ys - 1 {
        return (chk == CELL_CHKNOPASS) as i32;
    }
    let b = bits_of(c, md, x, y);
    let has = |bit: u16| (b & bit != 0) as i32;
    match chk {
        CELL_GETTYPE => cell2gat(b),
        CELL_CHKWALL => (b & (B_WALKABLE | B_SHOOTABLE) == 0) as i32,
        CELL_CHKWATER => has(B_WATER),
        CELL_CHKCLIFF => (b & B_WALKABLE == 0 && b & B_SHOOTABLE != 0) as i32,
        CELL_CHKNPC => {
            if b & B_NPC_SET != 0 {
                has(B_NPC)
            } else {
                crate::npc::has_touch_area_at(c, m, x, y) as i32
            }
        }
        CELL_CHKBASILICA => has(B_BASILICA),
        CELL_CHKLANDPROTECTOR => has(B_LANDPROTECTOR),
        CELL_CHKNOVENDING => has(B_NOVENDING),
        CELL_CHKNOCHAT => has(B_NOCHAT),
        CELL_CHKMAELSTROM => has(B_MAELSTROM),
        CELL_CHKICEWALL => has(B_ICEWALL),
        CELL_CHKNOBUYINGSTORE => has(B_NOBUYINGSTORE),
        CELL_CHKPASS | CELL_CHKREACH => has(B_WALKABLE),
        CELL_CHKNOPASS | CELL_CHKNOREACH => (b & B_WALKABLE == 0) as i32,
        CELL_CHKSTACK => 0,
        _ => 0,
    }
}

fn put_bits(c: &Ctx, md: &MapData, x: i16, y: i16, bits: u16) {
    let k = key(md.id, x, y);
    if bits == base_bits(md, x, y) {
        c.db().map_cell_mod().key().delete(k);
    } else if c.db().map_cell_mod().key().find(k).is_some() {
        c.db().map_cell_mod().key().update(MapCellMod { key: k, map: md.id, x, y, bits });
    } else {
        c.db().map_cell_mod().insert(MapCellMod { key: k, map: md.id, x, y, bits });
    }
    let g = generation(c) + 1;
    if c.db().map_cell_gen().key().find(0).is_some() {
        c.db().map_cell_gen().key().update(MapCellGen { key: 0, generation: g });
    } else {
        c.db().map_cell_gen().insert(MapCellGen { key: 0, generation: g });
    }
    CACHE.with(|kk| {
        let mut kk = kk.borrow_mut();
        if bits == base_bits(md, x, y) {
            kk.cells.remove(&(md.id, x, y));
        } else {
            kk.cells.insert((md.id, x, y), bits);
        }
        kk.generation = Some(g);
    });
}

/// map.cpp map_addinstancemap's memcpy of the source's cells, one changed cell at a time: the copy `m` gets the same
/// bits (instance_map.rs; the copy shares the source's gat types, so the row is a difference there too).
pub fn put_raw(c: &Ctx, m: u16, x: i16, y: i16, bits: u16) {
    let k = key(m, x, y);
    if c.db().map_cell_mod().key().find(k).is_some() {
        c.db().map_cell_mod().key().update(MapCellMod { key: k, map: m, x, y, bits });
    } else {
        c.db().map_cell_mod().insert(MapCellMod { key: k, map: m, x, y, bits });
    }
    let g = generation(c) + 1;
    if c.db().map_cell_gen().key().find(0).is_some() {
        c.db().map_cell_gen().key().update(MapCellGen { key: 0, generation: g });
    } else {
        c.db().map_cell_gen().insert(MapCellGen { key: 0, generation: g });
    }
    CACHE.with(|kk| {
        let mut kk = kk.borrow_mut();
        kk.cells.insert((m, x, y), bits);
        kk.generation = Some(g);
    });
}

/// map.cpp map_delinstancemap's `aFree(mapdata->cell)`: the instance map's changed cells go.
pub fn drop_map(c: &Ctx, m: u16) {
    for r in c.db().map_cell_mod().by_map().filter(m).collect::<Vec<_>>() {
        c.db().map_cell_mod().key().delete(r.key);
    }
    let g = generation(c) + 1;
    if c.db().map_cell_gen().key().find(0).is_some() {
        c.db().map_cell_gen().key().update(MapCellGen { key: 0, generation: g });
    } else {
        c.db().map_cell_gen().insert(MapCellGen { key: 0, generation: g });
    }
    CACHE.with(|kk| {
        let mut kk = kk.borrow_mut();
        kk.cells.retain(|&(mm, _, _), _| mm != m);
        kk.generation = Some(g);
    });
}

/// map.cpp map_setcell: one flag of a cell on or off (out-of-map cells are ignored).
pub fn setcell(c: &Ctx, m: Option<u16>, x: i16, y: i16, cell: i32, flag: bool) {
    let Some(md) = m.and_then(|m| data::maps().get(m)) else { return };
    if x < 0 || x >= md.xs || y < 0 || y >= md.ys {
        return;
    }
    let mut b = bits_of(c, md, x, y);
    let bit = match cell {
        CELL_WALKABLE => B_WALKABLE,
        CELL_SHOOTABLE => B_SHOOTABLE,
        CELL_WATER => B_WATER,
        CELL_NPC => {
            b |= B_NPC_SET;
            B_NPC
        }
        CELL_BASILICA => B_BASILICA,
        CELL_LANDPROTECTOR => B_LANDPROTECTOR,
        CELL_NOVENDING => B_NOVENDING,
        CELL_NOCHAT => B_NOCHAT,
        CELL_MAELSTROM => B_MAELSTROM,
        CELL_ICEWALL => B_ICEWALL,
        CELL_NOBUYINGSTORE => B_NOBUYINGSTORE,
        _ => {
            log::warn!("map_setcell: invalid cell type '{cell}'");
            return;
        }
    };
    if flag {
        b |= bit;
    } else {
        b &= !bit;
    }
    put_bits(c, md, x, y, b);
}

/// map.cpp map_setgatcell: a cell's terrain from a gat type.
pub fn setgatcell(c: &Ctx, m: u16, x: i16, y: i16, gat: i32) {
    let Some(md) = data::maps().get(m) else { return };
    if x < 0 || x >= md.xs || y < 0 || y >= md.ys {
        return;
    }
    if !matches!(gat, 0..=6) {
        log::warn!("map_gat2cell: unrecognized gat type '{gat}'");
    }
    let t = gat_bits(gat as u8);
    let b = (bits_of(c, md, x, y) & !(B_WALKABLE | B_SHOOTABLE | B_WATER)) | t;
    put_bits(c, md, x, y, b);
}

/// clif.cpp clif_changemapcell: ZC_UPDATE_MAPINFO (0x0192) — to every player whose map is `m` (ALL_SAMEMAP with a
/// dummy block_list: players still loading the map included), or to `to` alone.
pub fn clif_changemapcell(c: &Ctx, m: u16, x: i16, y: i16, kind: i16, to: Option<&crate::unit::Unit>) {
    // mapindex_getmapname_ext(map_mapid2mapname(m)): an instance map's source map
    let name = crate::instance_map::map_mapid2mapname(c, m as i32);
    let p = crate::net::packets::PACKET_ZC_UPDATE_MAPINFO { packetType: crate::net::packets::PACKET_ZC_UPDATE_MAPINFO::ID as i16, x, y, r#type: kind, mapname: format!("{name}.gat") };
    let buf = p.to_vec();
    match to {
        Some(u) => crate::clif::send(c, &buf, u, crate::clif::Target::SelfOnly),
        None => {
            for pc in c.db().pc().iter() {
                if pc.disconnected {
                    continue;
                }
                if crate::unit::get(c, pc.account_id).map(|u| u.map == m).unwrap_or(false) {
                    c.send(pc.identity, &buf);
                }
            }
        }
    }
}
use crate::pc::pc as _;

// ---------------------------------------------------------------------------------------------- invisible walls

/// map.cpp map_iwall_nextxy.
pub fn iwall_nextxy(x: i16, y: i16, dir: i8, pos: i32) -> (i16, i16) {
    use crate::path::*;
    let dir = dir as i32;
    let (n, s, w, e) = (DIR_NORTH as i32, DIR_SOUTH as i32, DIR_WEST as i32, DIR_EAST as i32);
    let x1 = if dir == n || dir == s {
        x // Keep X
    } else if dir > n && dir < s {
        (x as i32 - pos) as i16 // Going left
    } else {
        (x as i32 + pos) as i16 // Going right
    };
    let y1 = if dir == w || dir == e {
        y
    } else if dir > w && dir < e {
        (y as i32 - pos) as i16
    } else {
        (y as i32 + pos) as i16
    };
    (x1, y1)
}

/// map.cpp map_iwall_exist.
pub fn iwall_exist(c: &Ctx, name: &str) -> bool {
    c.db().iwall().wall_name().find(name.to_string()).is_some()
}

/// map.cpp map_iwall_set: an invisible wall of up to `size` cells from (x, y) in direction `dir`, stopping at the first
/// unreachable cell; each cell loses walkable (and shootable unless `shootable`) and every player on the map is told.
pub fn iwall_set(c: &Ctx, m: u16, x: i16, y: i16, size: i32, dir: i8, shootable: bool, name: &str) -> bool {
    if size < 1 {
        return false;
    }
    // safestrncpy(iwall->wall_name, wall_name, 50)
    let wall_name: String = name.chars().take(49).collect();
    if c.db().iwall().wall_name().find(wall_name.clone()).is_some() {
        return false; // Already Exists
    }
    if getcell(c, Some(m), x, y, CELL_CHKNOREACH) != 0 {
        return false; // Starting cell problem
    }
    let mut i = 0;
    while i < size {
        let (x1, y1) = iwall_nextxy(x, y, dir, i);
        if getcell(c, Some(m), x1, y1, CELL_CHKNOREACH) != 0 {
            break; // Collision
        }
        setcell(c, Some(m), x1, y1, CELL_WALKABLE, false);
        setcell(c, Some(m), x1, y1, CELL_SHOOTABLE, shootable);
        let t = getcell(c, Some(m), x1, y1, CELL_GETTYPE);
        clif_changemapcell(c, m, x1, y1, t as i16, None);
        i += 1;
    }
    c.db().iwall().insert(IWall { wall_name, m, x, y, size: i as i16, dir, shootable });
    true
}

/// map.cpp map_iwall_remove.
pub fn iwall_remove(c: &Ctx, name: &str) -> bool {
    let Some(w) = c.db().iwall().wall_name().find(name.to_string()) else { return false }; // Nothing to do
    for i in 0..w.size as i32 {
        let (x1, y1) = iwall_nextxy(w.x, w.y, w.dir, i);
        setcell(c, Some(w.m), x1, y1, CELL_SHOOTABLE, true);
        setcell(c, Some(w.m), x1, y1, CELL_WALKABLE, true);
        let t = getcell(c, Some(w.m), x1, y1, CELL_GETTYPE);
        clif_changemapcell(c, w.m, x1, y1, t as i16, None);
    }
    c.db().iwall().wall_name().delete(w.wall_name);
    true
}

/// map.cpp map_iwall_get: at LoadEndAck on a map change, the walls of the player's map (iwall_db in DBIterator order:
/// string hash bucket, then key).
pub fn iwall_get(c: &Ctx, sd: &crate::unit::Unit) {
    let walls: Vec<IWall> = c.db().iwall().iter().filter(|w| w.m == sd.map).collect();
    if walls.is_empty() {
        return; // iwall_num < 1
    }
    let mut db = crate::script::dbmap::StrDb::new(2 * 24 + 2 + 1, false);
    for w in c.db().iwall().iter() {
        db.put(w.wall_name.as_bytes(), w.clone());
    }
    for (_, w) in db.iter_inorder() {
        if w.m != sd.map {
            continue;
        }
        for i in 0..w.size as i32 {
            let (x1, y1) = iwall_nextxy(w.x, w.y, w.dir, i);
            let t = getcell(c, Some(w.m), x1, y1, CELL_GETTYPE);
            clif_changemapcell(c, w.m, x1, y1, t as i16, Some(sd));
        }
    }
}

/// The world is rebuilt (rAthena restarts with fresh map_data cells and an empty iwall_db).
pub fn on_world_rebuilt(c: &Ctx) {
    for k in c.db().map_cell_mod().iter().map(|r| r.key).collect::<Vec<_>>() {
        c.db().map_cell_mod().key().delete(k);
    }
    for n in c.db().iwall().iter().map(|w| w.wall_name).collect::<Vec<_>>() {
        c.db().iwall().wall_name().delete(n);
    }
    let g = generation(c) + 1;
    if c.db().map_cell_gen().key().find(0).is_some() {
        c.db().map_cell_gen().key().update(MapCellGen { key: 0, generation: g });
    } else {
        c.db().map_cell_gen().insert(MapCellGen { key: 0, generation: g });
    }
    CACHE.with(|k| {
        let mut k = k.borrow_mut();
        k.cells.clear();
        k.generation = Some(g);
    });
}
