//! map.cpp's instance maps: map_addinstancemap, map_delinstancemap (map_instancemap_leave / map_instancemap_clean),
//! map_data_copy, map_mapid2mapname, instance.cpp instance_generate_mapname, common/mapindex.cpp's run-time map
//! indexes (mapindex_addmap(-1, name) / mapindex_removemap) and pc.cpp pc_setpos_savepoint.
//!
//! rAthena keeps an instance map in map[] (ids from instance_start = map_num at start, the first empty slot first)
//! with a run-time mapindex. Here the `instance_map` rows are those map_data entries (id, name, instance id, source
//! map, index slot), and data::maps() answers them through an in-memory registry that follows the table (checked at
//! the start of every reducer, [`sync`], like map_cell.rs), so every map_getmapdata / map_mapname2mapid of the
//! ported code sees instance maps. The rest of an instance map's map_data lives where a static map's does: its flags
//! are `mapflag` rows, its nosave point a `map_save_point` row, its changed cells `map_cell_mod` rows, its users and
//! mob_delete_timer the `mob_map` row.

use std::cell::RefCell;
use std::collections::HashMap;

use spacetimedb::{table, Table};

use crate::data::{self, MapData};
use crate::world::Ctx;

/// mmo.hpp MAX_MAP_PER_SERVER.
pub const MAX_MAP_PER_SERVER: u16 = 1500;
/// mmo.hpp MAP_NAME_LENGTH (11 + 1).
pub const MAP_NAME_LENGTH: usize = 12;

/// An instance map: the map_data entry map_addinstancemap fills (map[m] with instance_id and instance_src_map).
#[table(accessor = instance_map, index(accessor = by_instance, btree(columns = [instance_id])))]
#[derive(Clone, Debug)]
pub struct InstanceMap {
    /// The map id (dst_m).
    #[primary_key]
    pub m: u16,
    /// mapdata->name (instance_generate_mapname).
    #[unique]
    pub name: String,
    /// mapdata->instance_id.
    pub instance_id: i32,
    /// mapdata->instance_src_map.
    pub src_m: u16,
    /// mapdata->index as a slot of the run-time indexes: rAthena's index is the first free one after the static
    /// indexes (mapindex_addmap(-1): max_index at start + slot).
    pub slot: u16,
}

/// mapindex_db's run-time entries: every instance map name ever added (strdb_iput) and its index slot. rAthena never
/// removes them (mapindex_removemap only clears indexes[index].name), so an old name still resolves to whatever map
/// holds its index later (map_mapindex2mapid).
#[table(accessor = instance_mapindex)]
#[derive(Clone, Debug)]
pub struct InstanceMapindex {
    #[primary_key]
    pub name: String,
    pub slot: u16,
}

/// The generation of `instance_map` (one row, key 0): bumped by every change, so the registry reloads.
#[table(accessor = instance_map_gen)]
pub struct InstanceMapGen {
    #[primary_key]
    pub key: u8,
    pub generation: u64,
}

// ---------------------------------------------------------------------------------------------- registry

struct Registry {
    generation: Option<u64>,
    /// map id -> its map_data
    maps: HashMap<u16, &'static MapData>,
    /// mapindex_db's run-time names -> index slot
    names: HashMap<String, u16>,
    /// index slot -> map id (map_db)
    slots: HashMap<u16, u16>,
    /// the map_data copies made so far in this module instance, by (id, name): reused, never freed (a reference handed
    /// out earlier stays valid, as rAthena's map[] entries do)
    made: HashMap<(u16, String), &'static MapData>,
}

thread_local! {
    static REG: RefCell<Registry> =
        RefCell::new(Registry { generation: None, maps: HashMap::new(), names: HashMap::new(), slots: HashMap::new(), made: HashMap::new() });
}

fn map_data_of(reg: &mut Registry, row: &InstanceMap) -> Option<&'static MapData> {
    let key = (row.m, row.name.clone());
    if let Some(md) = reg.made.get(&key) {
        return Some(*md);
    }
    let src = data::maps().list.get(row.src_m as usize)?;
    let md: &'static MapData = Box::leak(Box::new(MapData::instance_copy(row.m, row.name.clone(), src)));
    reg.made.insert(key, md);
    Some(md)
}

fn generation(c: &Ctx) -> u64 {
    c.db().instance_map_gen().key().find(0).map(|g| g.generation).unwrap_or(0)
}

fn bump(c: &Ctx) -> u64 {
    let g = generation(c) + 1;
    if c.db().instance_map_gen().key().find(0).is_some() {
        c.db().instance_map_gen().key().update(InstanceMapGen { key: 0, generation: g });
    } else {
        c.db().instance_map_gen().insert(InstanceMapGen { key: 0, generation: g });
    }
    g
}

/// Reloads the registry when the table moved on (called at the start of every reducer, world::run).
pub fn sync(c: &Ctx) {
    let g = generation(c);
    if REG.with(|r| r.borrow().generation == Some(g)) {
        return;
    }
    reload(c, g);
}

fn reload(c: &Ctx, g: u64) {
    let rows: Vec<InstanceMap> = c.db().instance_map().iter().collect();
    let names: Vec<InstanceMapindex> = c.db().instance_mapindex().iter().collect();
    REG.with(|r| {
        let mut r = r.borrow_mut();
        r.maps.clear();
        r.slots.clear();
        r.names.clear();
        for row in &rows {
            if let Some(md) = map_data_of(&mut r, row) {
                r.maps.insert(row.m, md);
            }
            r.slots.insert(row.slot, row.m);
        }
        for n in names {
            r.names.insert(n.name, n.slot);
        }
        r.generation = Some(g);
    });
}

/// data::maps().get for an id past the static maps.
pub fn registry_get(id: u16) -> Option<&'static MapData> {
    REG.with(|r| r.borrow().maps.get(&id).copied())
}

/// data::maps().id_of for a name that is not a static map: mapindex_name2id (the run-time names) then
/// map_mapindex2mapid (the map that holds that index now, if any).
pub fn registry_id_of(name: &str) -> Option<u16> {
    REG.with(|r| {
        let r = r.borrow();
        let slot = *r.names.get(name)?;
        r.slots.get(&slot).copied()
    })
}

// ---------------------------------------------------------------------------------------------- queries

/// instance_start (instance.cpp do_init_instance: map_num once the static maps are loaded).
pub fn instance_start() -> u16 {
    data::maps().static_count()
}

/// The instance map row of `m` (None for a static map or a free slot).
pub fn get(c: &Ctx, m: u16) -> Option<InstanceMap> {
    if m < instance_start() {
        return None;
    }
    c.db().instance_map().m().find(m)
}

/// map_getmapdata(m)->instance_id (0 for a static map).
pub fn map_instance_id(c: &Ctx, m: u16) -> i32 {
    get(c, m).map(|r| r.instance_id).unwrap_or(0)
}

/// mapdata->instance_src_map of a live instance map.
pub fn instance_src_map(c: &Ctx, m: u16) -> Option<u16> {
    get(c, m).map(|r| r.src_m)
}

/// The live instance maps in map id order (map[instance_start .. map_num) with a name).
pub fn all(c: &Ctx) -> Vec<InstanceMap> {
    let mut v: Vec<InstanceMap> = c.db().instance_map().iter().collect();
    v.sort_by_key(|r| r.m);
    v
}

/// map.cpp map_mapid2mapname: an instance map answers with the name of its source map (the one of the instance's
/// map list whose copy it is; its own name when the instance is gone), every other map with its name. -1 is
/// "Floating" and an unknown map "".
pub fn map_mapid2mapname(c: &Ctx, m: i32) -> String {
    if m == -1 {
        return "Floating".to_string();
    }
    let Some(md) = u16::try_from(m).ok().and_then(|m| data::maps().get(m)) else { return String::new() };
    if let Some(row) = get(c, md.id) {
        match crate::instance::find(c, row.instance_id) {
            // This shouldn't happen but if it does give them the map we intended to give
            None => return md.name.clone(),
            Some(idata) => {
                // Loop to find the src map we want
                for it in &idata.map {
                    if it.m == md.id {
                        return data::maps().get(it.src_m).map(|s| s.name.clone()).unwrap_or_default();
                    }
                }
            }
        }
    }
    md.name.clone()
}

/// instance.cpp instance_generate_mapname: "%0*u#%0*u" with a 4-digit map id prefix and a 6-digit instance id suffix
/// (MAP_NAME_LENGTH 12: 12 - 4 - 1 - 1), each taken modulo its power of ten.
pub fn instance_generate_mapname(map_id: i32, instance_id: i32) -> String {
    const PREFIX_LENGTH: u32 = 4;
    const SUFFIX_LENGTH: u32 = MAP_NAME_LENGTH as u32 - PREFIX_LENGTH - 1 - 1;
    let prefix_limit = 10u32.pow(PREFIX_LENGTH);
    let suffix_limit = 10u32.pow(SUFFIX_LENGTH);
    // %u of an int32: the value as unsigned
    format!("{:0width$}#{:0width2$}", (map_id as u32) % prefix_limit, (instance_id as u32) % suffix_limit, width = PREFIX_LENGTH as usize, width2 = SUFFIX_LENGTH as usize)
}

// ---------------------------------------------------------------------------------------------- map.cpp

/// map.cpp map_addinstancemap: a copy of map `src_m` for instance `instance_id` (flags too unless `no_mapflag`).
/// Returns the new map id, or -1 (no source), -2 (source name too long), -3 (no free map slot).
pub fn map_addinstancemap(c: &Ctx, src_m: i32, instance_id: i32, no_mapflag: bool) -> i32 {
    if src_m < 0 {
        return -1;
    }
    let name = map_mapid2mapname(c, src_m);
    if name.len() > 20 {
        // against buffer overflow
        log::error!("map_addinstancemap: Map name \"{name}\" is too long.");
        return -2;
    }
    // the first empty map[] slot from instance_start on (a destroyed instance's slot, else map_num++)
    let start = instance_start();
    let mut i = start;
    while i < MAX_MAP_PER_SERVER && c.db().instance_map().m().find(i).is_some() {
        i += 1;
    }
    if i >= MAX_MAP_PER_SERVER {
        // Out of bounds
        log::error!("map_addinstancemap failed. map_num({}) > map_max({MAX_MAP_PER_SERVER})", i);
        return -3;
    }
    let dst_m = i;
    let Some(src_map) = data::maps().get(src_m as u16) else { return -1 };
    // Alter the name. This also allows us to maintain complete independence with main map functions
    let dst_name = instance_generate_mapname(src_m, instance_id);
    // users = 0, mob_delete_timer = INVALID_TIMER: the map's mob_map row starts afresh
    crate::mob::put_mob_map(c, crate::mob::MobMap { map: dst_m, users: 0, mob_delete_timer: 0 });
    // Reallocate cells: the source's cells as they are now (its script / skill changes included)
    copy_cells(c, src_map.id, dst_m);
    // dst_map->index = mapindex_addmap(-1, dst_map->name): the first free run-time index
    let used: std::collections::HashSet<u16> = c.db().instance_map().iter().map(|r| r.slot).collect();
    let mut slot = 0u16;
    while used.contains(&slot) {
        slot += 1;
    }
    match c.db().instance_mapindex().name().find(&dst_name) {
        Some(_) => {
            c.db().instance_mapindex().name().update(InstanceMapindex { name: dst_name.clone(), slot });
        }
        None => {
            c.db().instance_mapindex().insert(InstanceMapindex { name: dst_name.clone(), slot });
        }
    }
    if !no_mapflag {
        map_data_copy(c, dst_m, src_map.id);
    }
    log::info!("[Instance] Created map '{dst_name}' ({dst_m}) from '{name}' ({}).", src_map.id);
    // map_addmap2db
    c.db().instance_map().insert(InstanceMap { m: dst_m, name: dst_name, instance_id, src_m: src_map.id, slot });
    let g = bump(c);
    reload(c, g);
    dst_m as i32
}

/// The source's dynamic cells (map_cell_mod rows) become the copy's: memcpy(dst_map->cell, src_map->cell).
fn copy_cells(c: &Ctx, src: u16, dst: u16) {
    crate::map_cell::drop_map(c, dst);
    for r in c.db().map_cell_mod().by_map().filter(src).collect::<Vec<_>>() {
        crate::map_cell::put_raw(c, dst, r.x, r.y, r.bits);
    }
}

/// map.cpp map_data_copy: the save point, the damage adjustments, the flags (with the restricted zone and the skill
/// damage / duration flags: `mapflag` rows here).
pub fn map_data_copy(c: &Ctx, dst: u16, src: u16) {
    use crate::npc::{map_save_point, mapflag, MapFlag, MapSavePoint};
    // memcpy(&dst_map->save, &src_map->save, sizeof(struct point))
    match c.db().map_save_point().map().find(src) {
        Some(sp) => {
            let row = MapSavePoint { map: dst, ..sp };
            if c.db().map_save_point().map().find(dst).is_some() {
                c.db().map_save_point().map().update(row);
            } else {
                c.db().map_save_point().insert(row);
            }
        }
        None => {
            c.db().map_save_point().map().delete(dst);
        }
    }
    // dst_map->copyFlags(*src_map): the whole flag vector (skill_damage / skill_duration / zone live in the rows)
    for f in c.db().mapflag().iter().filter(|f| f.map == dst).collect::<Vec<_>>() {
        c.db().mapflag().key().delete(f.key);
    }
    for f in c.db().mapflag().iter().filter(|f| f.map == src).collect::<Vec<_>>() {
        let key = ((dst as u32) << 16) | (f.flag as u16 as u32);
        c.db().mapflag().insert(MapFlag { key, map: dst, flag: f.flag, value: f.value });
    }
    // memcpy(&dst_map->damage_adjust, …) and dst_map->skill_damage.insert(src begin, end) (map_skill_damage.rs)
    crate::map_skill_damage::copy(c, dst, src);
    // dst_map->skill_duration.insert(src begin, end): the source's entries in its order, a key the destination
    // already has keeping its value
    for (skill_id, per) in crate::skillunit::data::map_skill_duration_list(c, src) {
        crate::skillunit::data::map_skill_duration_insert(c, dst, skill_id, per);
    }
}

/// map.cpp map_delinstancemap: every player on the map goes to its save point, every NPC / monster / floor item on it
/// is removed, then the map_data entry is freed. Returns 1, or 0 for a map that is not an instance map.
pub fn map_delinstancemap(c: &Ctx, m: u16) -> i32 {
    let Some(row) = get(c, m) else { return 0 };
    if row.instance_id <= 0 {
        return 0;
    }
    let Some(md) = data::maps().get(m) else { return 0 };
    let (xs, ys) = (md.xs, md.ys);
    // Kick everyone out (map_foreachinmap(map_instancemap_leave, m, BL_PC))
    for u in crate::map::units_in_area(c, m, 0, 0, xs, ys, crate::map::BL_PC) {
        if !crate::unit::get(c, u.id).map(|x| x.on_map && x.map == m).unwrap_or(false) {
            continue;
        }
        pc_setpos_savepoint(c, u.id, crate::unit::CLR_TELEPORT);
    }
    // Do the unit cleanup (map_foreachinmap(map_instancemap_clean, m, BL_ALL): every other unit, then the monsters)
    for u in crate::map::units_in_area(c, m, 0, 0, xs, ys, crate::map::BL_ALL) {
        if !crate::unit::get(c, u.id).map(|x| x.on_map && x.map == m).unwrap_or(false) {
            continue; // bl->prev: removed meanwhile
        }
        match u.kind {
            crate::map::BL_NPC => crate::npc::unload(c, u.id),
            crate::map::BL_MOB => {
                crate::mob::unit::unit_free(c, u.id, crate::unit::CLR_OUTSIGHT);
            }
            crate::map::BL_ITEM => crate::flooritem::map_clearflooritem(c, u.id),
            crate::map::BL_SKILL => {
                crate::skillunit::skill_delunit(c, u.id);
            }
            // BL_PET, BL_HOM, BL_MER, BL_ELEM are moved when BL_PC warped out in map_instancemap_leave
            _ => {}
        }
    }
    let r = crate::mob::mob_map(c, m);
    if r.mob_delete_timer != 0 {
        crate::timer::delete(c, r.mob_delete_timer);
    }
    c.db().mob_map().map().delete(m);
    // Free memory: the cells (the copy's changed cells), the flags (initMapFlags), damage adjustments, the nosave point
    crate::map_cell::drop_map(c, m);
    for f in c.db().mapflag().iter().filter(|f| f.map == m).collect::<Vec<_>>() {
        c.db().mapflag().key().delete(f.key);
    }
    c.db().map_save_point().map().delete(m);
    // mapdata->damage_adjust = {}; mapdata->skill_damage.clear() (map.cpp map_delinstancemap; skill_duration is not
    // cleared there, and map_data_copy's insert keeps what a reused map still has)
    crate::map_skill_damage::clear(c, m);
    // map_free_questinfo: the map's NPC quest markers went with its NPCs (npc_unload -> map_remove_questinfo)
    // mapindex_removemap(mapdata->index); map_removemapdb(mapdata); memset(&mapdata->name, '\0', ...)
    c.db().instance_map().m().delete(m);
    let g = bump(c);
    reload(c, g);
    1
}

/// Drops every instance map (a world rebuild: rAthena restarts without instances). Their units go with the world's.
pub fn on_world_rebuild(c: &Ctx) {
    for r in c.db().instance_map().iter().collect::<Vec<_>>() {
        c.db().mob_map().map().delete(r.m);
        c.db().instance_map().m().delete(r.m);
    }
    for n in c.db().instance_mapindex().iter().map(|n| n.name).collect::<Vec<_>>() {
        c.db().instance_mapindex().name().delete(n);
    }
    let g = bump(c);
    reload(c, g);
}

// ---------------------------------------------------------------------------------------------- pc.cpp

/// pc.cpp pc_setpos_savepoint: the map's nosave return point when it has one, else the character's save point.
pub fn pc_setpos_savepoint(c: &Ctx, aid: u32, clrtype: u8) -> crate::pc::SetPos {
    let Some(u) = crate::unit::get(c, aid) else { return crate::pc::SetPos::MapIndex };
    if crate::npc::mapflag(c, u.map, crate::npc::MF_NOSAVE) {
        if let Some(sp) = c.db().map_save_point().map().find(u.map).filter(|sp| sp.save_map >= 0) {
            return crate::pc::setpos(c, aid, sp.save_map as u16, sp.x, sp.y, clrtype);
        }
    }
    let Some(ch) = crate::pc::character_of(c, aid) else { return crate::pc::SetPos::MapIndex };
    // mapindex_name2id(sd.status.save_point.map): 0 (SETPOS_MAPINDEX) for an unknown name
    match data::maps().id_of(&ch.save_map) {
        Some(m) => crate::pc::setpos(c, aid, m, ch.save_x as i16, ch.save_y as i16, clrtype),
        None => crate::pc::SetPos::MapIndex,
    }
}

use crate::map_cell::map_cell_mod as _;
use crate::mob::mob_map as _;
use crate::npc::{map_save_point as _, mapflag as _};

#[cfg(test)]
mod tests {
    use super::*;

    /// instance_generate_mapname: 4 + '#' + 6 digits, zero padded, taken modulo their widths.
    #[test]
    fn generate_mapname() {
        assert_eq!(instance_generate_mapname(123, 1), "0123#000001");
        assert_eq!(instance_generate_mapname(12345, 1234567), "2345#234567");
        assert_eq!(instance_generate_mapname(0, 0), "0000#000000");
        assert_eq!(instance_generate_mapname(1021, 42).len(), MAP_NAME_LENGTH - 1);
    }
}
