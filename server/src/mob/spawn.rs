//! Spawning monsters: mob.cpp mob_parse_dataset, mob_spawn_dataset, mob_spawn, mob_setdelayspawn / mob_delayspawn,
//! mob_once_spawn_sub / mob_once_spawn / mob_once_spawn_area, mob_summonslave, mob_timer_delete, npc.cpp
//! npc_parse_mob2 / npc_get_new_npc_id, map.cpp map_spawnmobs / map_removemobs(_sub / _timer) (dynamic mobs) and
//! map_search_freecell with a source unit. Guardians (mob_spawn_guardian) belong to WoE (guild agent) and
//! battleground monsters (mob_spawn_bg) to battlegrounds (later phase).

use spacetimedb::Table;

use super::{mob_data as _, mob_map as _};

use super::{conf, get, has_mode, mobs_on_map, put, MobData, DAMAGELOG_SIZE, MAX_MOBSKILL, MIN_RANDOMWALKTIME, MSS_IDLE, NAME_LENGTH};
use crate::db::mobs::{self, MD_IGNOREMAGIC, MD_IGNOREMELEE, MD_IGNOREMISC, MD_IGNORERANGED, MD_LOOTER, MD_STATUSIMMUNE};
use crate::map::{self, CellChk, BL_MOB, BL_PC};
use crate::npc::{spawn_data, SpawnData};
use crate::timer::{self, Func};
use crate::unit::{self, unit as _, ScView, Unit, UnitData};
use crate::world::{Ctx, Tick};

/// npc.hpp START_NPC_NUM.
const START_NPC_NUM: u32 = crate::npc::START_NPC_NUM;

// ---------------------------------------------------------------------------------------------- ids

/// npc.cpp npc_get_new_npc_id: the next free id from the NPC counter (npc_id), which after the world is loaded stands
/// right after the fake NPC (do_init_npc creates fake_nd last). Monsters take their ids from it, so they interleave
/// with NPCs created at run time exactly as in rAthena. The counter is `id_counter` row "npc_id"; a world rebuild
/// (a server start in rAthena terms) resets it ([`on_world_rebuild`]).
pub fn npc_get_new_npc_id(c: &Ctx) -> u32 {
    use crate::ids::id_counter;
    let key = "npc_id".to_string();
    let mut npc_id = match c.db().id_counter().kind().find(&key) {
        Some(r) => r.next,
        None => crate::script::fake_npc_id(c) + 1,
    };
    let taken = |id: u32| c.db().unit().id().find(id).is_some();
    let id = if npc_id >= START_NPC_NUM && !taken(npc_id) {
        npc_id += 1;
        npc_id - 1
    } else {
        // find next id
        let base_id = npc_id;
        loop {
            npc_id = npc_id.wrapping_add(1);
            if npc_id == base_id {
                log::error!("npc_get_new_npc_id: All ids are taken.");
                return 0;
            }
            if npc_id < START_NPC_NUM {
                npc_id = START_NPC_NUM;
            }
            if !taken(npc_id) {
                npc_id += 1;
                break npc_id - 1;
            }
        }
    };
    let row = crate::ids::IdCounter { kind: key.clone(), next: npc_id };
    if c.db().id_counter().kind().find(&key).is_some() {
        c.db().id_counter().kind().update(row);
    } else {
        c.db().id_counter().insert(row);
    }
    id
}

// ---------------------------------------------------------------------------------------------- spawn_data

/// map.hpp struct spawn_data as the spawn functions pass it around (the `spawn_data` table holds the permanent
/// ones, from `monster` lines; mob_once_spawn and friends build a temporary one on the stack).
#[derive(Clone, Debug, Default)]
pub struct SpawnArgs {
    pub id: i32,
    pub m: u16,
    pub x: i16,
    pub y: i16,
    pub xs: i16,
    pub ys: i16,
    pub num: u16,
    pub level: u32,
    pub size: u8,
    pub ai: u8,
    pub boss: bool,
    pub name: String,
    pub eventname: String,
}

impl SpawnArgs {
    pub fn of(sp: &SpawnData) -> SpawnArgs {
        SpawnArgs {
            id: sp.mob_id,
            m: sp.map,
            x: sp.x,
            y: sp.y,
            xs: sp.xs,
            ys: sp.ys,
            num: sp.num,
            level: sp.level,
            size: sp.size,
            ai: sp.ai,
            boss: sp.boss,
            name: sp.name.clone(),
            eventname: sp.eventname.clone(),
        }
    }
}

/// mob.cpp mob_parse_dataset: strips the quotes around the event name and resolves the "--en--" / "--ja--" names
/// to the database names. False when the monster does not exist or the amount is 0.
pub fn mob_parse_dataset(c: &Ctx, id: i32, num: u16, name: &mut String, eventname: &mut String) -> bool {
    if (mobs::mobdb_checkid(id) == 0 && super::rtdb::mob_is_clone(c, id) == 0) || num == 0 {
        return false;
    }
    if !eventname.is_empty() {
        if eventname.ends_with('"') {
            eventname.pop(); // Remove trailing quote.
        }
        if eventname.starts_with('"') {
            eventname.remove(0); // Strip leading quotes
        }
    }
    if name == "--en--" {
        *name = super::db_find(c, id).map(|m| truncate_name(&m.name)).unwrap_or_default();
    } else if name == "--ja--" {
        *name = super::db_find(c, id).map(|m| truncate_name(&m.jname)).unwrap_or_default();
    }
    true
}

/// safestrncpy into a NAME_LENGTH buffer: at most 23 bytes (names are latin-1 code points).
pub fn truncate_name(s: &str) -> String {
    s.chars().take(NAME_LENGTH - 1).collect()
}

// ---------------------------------------------------------------------------------------------- dataset

/// mob.cpp mob_spawn_dataset: a new monster (block_list, unit data, view data) from spawn data; not on a map yet.
/// `spawn` is the permanent spawn line (0 = none).
pub fn mob_spawn_dataset(c: &Ctx, data: &SpawnArgs, spawn: u32) -> Option<MobData> {
    let id = npc_get_new_npc_id(c);
    if id == 0 {
        return None;
    }
    let db = super::db_find(c, data.id)?;
    let tick = c.tick();
    let mut md = MobData {
        id,
        mob_id: data.id,
        spawn,
        level: if data.level > 0 { data.level as i32 } else { 0 },
        name: truncate_name(&data.name),
        special_size: data.size,
        special_ai: data.ai,
        special_clone: false,
        aggressive: false,
        steal_coin_flag: false,
        soul_change_flag: false,
        can_escape: false,
        npc_killmonster: false,
        rebirth: false,
        boss: data.boss,
        copy_master_mode: false,
        skillstate: MSS_IDLE,
        steal_flag: 0,
        attacked_count: 0,
        provoke_flag: 0,
        // md->status stays zeroed (aCalloc) until mob_spawn's status_calc_mob(SCO_FIRST): until then the monster
        // counts as dead (status_isdead: hp 0), which makes status_calc_slave_mode's SC_MODECHANGE fail at spawn
        status: Default::default(),
        base_status: None,
        dmglog: Vec::new(),
        spotted_log: vec![0; DAMAGELOG_SIZE],
        spawn_timer: 0,
        center_x: data.x,
        center_y: data.y,
        looter: db.status.has_mode(MD_LOOTER),
        lootitems: Vec::new(),
        target_id: 0,
        attacked_id: 0,
        norm_attacked_id: 0,
        areanpc_id: 0,
        bg_id: 0,
        next_walktime: 0,
        next_thinktime: 0,
        last_linktime: 0,
        last_pcneartime: 0,
        last_canmove: 0,
        last_skillcheck: 0,
        trickcasting: 0,
        move_fail_count: 0,
        walktoxy_fail_count: 0,
        deletetimer: 0,
        master_id: 0,
        master_dist: 0,
        skill_idx: -1,
        skilldelay: vec![0; MAX_MOBSKILL],
        npc_event: if data.eventname.len() >= 4 { data.eventname.chars().take(crate::script::npc_run::EVENT_NAME_LENGTH - 1).collect() } else { String::new() },
        idle_event: String::new(),
        can_summon: false,
        tomb_nid: 0,
        damagetaken: 0,
        vd_changed: false,
        pos_x: 0,
        pos_y: 0,
        pos_sx: 8,
        pos_sy: 8,
        pos_tick: 0,
        guardian: false,
    };
    // unit_dataset
    let ud = UnitData {
        attackabletime: tick,
        canact_tick: tick,
        canmove_tick: tick,
        endure_tick: tick,
        dmg_tick: 0,
        sx: 8,
        sy: 8,
        ..Default::default()
    };
    let u = Unit {
        id,
        kind: BL_MOB,
        map: data.m,
        x: data.x,
        y: data.y,
        on_map: false,
        name: md.name.clone(),
        vd: Default::default(),
        ud,
        sc: ScView::default(),
        speed: md.status.speed,
        mode: md.status.mode as u32,
        level: md.level,
        char_id: 0,
        head_dir: 0,
        font: 0,
        manner: 0,
        karma: false,
        guild_id: 0,
        emblem_id: 0,
        hp: md.status.hp as i64,
        max_hp: md.status.max_hp as i64,
        dead: false,
        size: data.size,
        boss: 0,
        invisible: false,
        npc_owner_char: 0,
    };
    // map_addiddb
    crate::unit::insert_mob(c, u);
    super::status::status_set_viewdata(c, &mut md, data.id);
    put(c, &md);
    Some(md)
}

// ---------------------------------------------------------------------------------------------- map_search_freecell

/// map.cpp map_search_freecell with a source unit. flag &1: search around (x, y) on `m` instead of around `src`;
/// &2: `src` must be able to walk to the cell; &4: no players around it (no_spawn_on_player). `rx` / `ry` < 0 search
/// the whole map (without its edges). Returns the cell, or None — rAthena then leaves x / y at the search centre,
/// which callers that need it take from [`search_center`].
#[allow(clippy::too_many_arguments)]
pub fn map_search_freecell(c: &Ctx, src: Option<(u16, i16, i16)>, m: i32, x: i16, y: i16, rx: i16, ry: i16, flag: i32, tries: i32) -> Option<(i16, i16)> {
    if src.is_none() && (flag & 1 == 0 || flag & 2 != 0) {
        log::debug!("map_search_freecell: Incorrect usage! When src is nullptr, flag has to be &1 and can't have &2");
        return None;
    }
    let (m, bx, by) = if flag & 1 != 0 {
        (m, x as i32, y as i32)
    } else {
        let (sm, sx, sy) = src.unwrap();
        (sm as i32, sx as i32, sy as i32)
    };
    if m < 0 {
        return None;
    }
    let m = m as u16;
    if rx == 0 && ry == 0 {
        // No range? Return the target cell then....
        return map::getcell(c, m, bx as i16, by as i16, CellChk::ChkReach).then_some((bx as i16, by as i16));
    }
    let md = crate::data::maps().get(m)?;
    let edge = map::map_edge_size() as i32;
    let edge_valid = edge.min(5);
    let mut spawn = 0;
    let mut tries = tries;
    while tries > 0 {
        tries -= 1;
        // For map-wide search, the configured tiles from the edge are not considered (default: 15)
        let cx = if rx >= 0 { c.rnd_value(bx - rx as i32, bx + rx as i32) } else { c.rnd_value(edge, md.xs as i32 - edge - 1) as i16 as i32 };
        let cy = if ry >= 0 { c.rnd_value(by - ry as i32, by + ry as i32) } else { c.rnd_value(edge, md.ys as i32 - edge - 1) as i16 as i32 };
        // *x / *y are int16
        let (cx, cy) = (cx as i16 as i32, cy as i16 as i32);
        if cx == bx && cy == by {
            continue; // Avoid picking the same target tile.
        }
        // Cells outside the map or within 4-5 cells of the map edge are considered invalid officially
        if cx < edge_valid || cx > md.xs as i32 - edge_valid || cy < edge_valid || cy > md.ys as i32 - edge_valid {
            continue;
        }
        if map::getcell(c, m, cx as i16, cy as i16, CellChk::ChkReach) {
            if flag & 2 != 0 {
                let (_, sx, sy) = src.unwrap();
                if !super::unit::unit_can_reach_pos(m, sx, sy, cx as i16, cy as i16, 1) {
                    continue;
                }
            }
            if flag & 4 != 0 {
                if spawn >= 100 {
                    return None; // Limit of retries reached.
                }
                let a = crate::conf::area_size() as i32;
                let players = !map::units_in_area(c, m, (cx - a) as i16, (cy - a) as i16, (cx + a) as i16, (cy + a) as i16, BL_PC).is_empty();
                let before = spawn;
                spawn += 1;
                if before < conf::no_spawn_on_player() && players {
                    tries += 1; // This failure should not affect the number of official tries
                    continue;
                }
            }
            return Some((cx as i16, cy as i16));
        }
    }
    None
}

// ---------------------------------------------------------------------------------------------- mob_spawn

/// mob.cpp mob_spawn: (re)spawns a monster on its map. Returns 0 spawned, 1 delayed, 2 error.
pub fn mob_spawn(c: &Ctx, id: u32) -> i32 {
    let tick = c.tick();
    let Some(mut md) = get(c, id) else { return 2 };
    md.next_thinktime = tick;
    put(c, &md);
    let on_map = unit::get(c, id).map(|u| u.on_map).unwrap_or(false);
    let sp = if md.spawn != 0 { c.db().spawn_data().id().find(md.spawn) } else { None };
    if on_map {
        super::unit::unit_remove_map(c, id, unit::CLR_RESPAWN);
        md = get(c, id).unwrap_or(md);
    } else if let Some(sp) = &sp {
        if md.mob_id != sp.mob_id {
            md.mob_id = sp.mob_id;
            super::status::status_set_viewdata(c, &mut md, sp.mob_id);
            md.name = sp.name.clone();
            put(c, &md);
        }
    }
    let Some(mut u) = unit::get(c, id) else { return 2 };
    if let Some(sp) = &sp {
        // Respawn data
        u.map = sp.map;
        u.x = md.center_x;
        u.y = md.center_y;
        // Search can be skipped for boss monster spawns if spawn location is fixed
        // We can't skip normal monsters as they should pick a random location if the cell is blocked (e.g. Icewall)
        // The center cell has a 1/(xs*ys) chance to be picked if free
        if (!sp.boss || (u.x == 0 && u.y == 0) || sp.xs != 1 || sp.ys != 1)
            && ((sp.xs as i32 + sp.ys as i32) < 1
                || !c.rnd_chance(1, sp.xs as i32 * sp.ys as i32)
                || !map::getcell(c, u.map, u.x, u.y, CellChk::ChkReach))
        {
            // Officially the area is split into 4 squares, 4 lines and 1 dot and for each of those there is one attempt
            // We simplify this to trying 8 times in the whole area and then at the center cell even though that's not
            // fully accurate
            let flag = if conf::no_spawn_on_player() != 0 { 4 } else { 0 };
            // Try to spawn monster in defined area (8 tries)
            match map_search_freecell(c, Some((u.map, u.x, u.y)), -1, u.x, u.y, sp.xs - 1, sp.ys - 1, flag, 8) {
                Some((x, y)) => {
                    u.x = x;
                    u.y = y;
                }
                None => {
                    // If area search failed and center cell not reachable, try to spawn the monster anywhere on the
                    // map (50 tries)
                    if !map::getcell(c, u.map, u.x, u.y, CellChk::ChkReach) {
                        match map_search_freecell(c, Some((u.map, u.x, u.y)), -1, u.x, u.y, -1, -1, flag, 50) {
                            Some((x, y)) => {
                                u.x = x;
                                u.y = y;
                            }
                            None => {
                                // Retry again later
                                unit::save(c, &u);
                                if md.spawn_timer != 0 {
                                    timer::delete(c, md.spawn_timer);
                                }
                                md.spawn_timer = timer::add(c, tick + crate::conf::mob_respawn_time(), Func::MobDelaySpawn, id, 0);
                                put(c, &md);
                                return 1;
                            }
                        }
                    }
                }
            }
        }
        // no_spawn_on_player > 99 (retry while players are in sight): the setting is 0
        unit::save(c, &u);
    }
    let Some(db) = super::db(c, &md) else { return 2 };
    // memset(&md->state, 0, sizeof(md->state))
    md.aggressive = false;
    md.steal_coin_flag = false;
    md.soul_change_flag = false;
    md.can_escape = false;
    md.npc_killmonster = false;
    md.rebirth = false;
    md.boss = false;
    md.copy_master_mode = false;
    md.skillstate = MSS_IDLE;
    md.steal_flag = 0;
    md.attacked_count = 0;
    md.provoke_flag = 0;
    put(c, &md);
    super::status::status_calc_mob(c, id, crate::status::SCO_FIRST);
    let mut md = get(c, id).unwrap_or(md);
    md.attacked_id = 0;
    md.norm_attacked_id = 0;
    md.target_id = 0;
    md.move_fail_count = 0;
    if md.spawn_timer != 0 {
        timer::delete(c, md.spawn_timer);
        md.spawn_timer = 0;
    }
    md.master_dist = 0;
    super::mob_setstate(&mut md, MSS_IDLE);
    md.next_walktime = tick + (c.rnd() % 1000) as i64 + MIN_RANDOMWALKTIME;
    md.last_linktime = 0;
    md.last_pcneartime = 0;
    md.last_canmove = tick;
    md.last_skillcheck = tick;
    md.trickcasting = 0;
    md.skilldelay = vec![0; MAX_MOBSKILL];
    md.spotted_log = vec![0; DAMAGELOG_SIZE];
    md.dmglog.clear();
    md.lootitems.clear();
    // MvP tomb [GreenBox]
    if md.tomb_nid != 0 {
        super::dead::mvptomb_destroy(c, &mut md);
    }
    put(c, &md);
    let Some(mut u) = unit::get(c, id) else { return 2 };
    u.ud.attack_continue = false;
    u.ud.target_to = 0;
    u.ud.dir = 0;
    u.ud.blockedmove = false;
    if db.option != 0 {
        // Added for carts, falcons and pecos for cloned monsters. [Valaris]
        u.sc.option = db.option as i32;
    }
    if !unit::addblock(c, &mut u) {
        return 2;
    }
    if super::map_users(c, u.map) > 0 {
        crate::clif::spawn(c, &u, false);
    }
    crate::skillunit::skill_unit_move_id(c, id, tick, 1);
    super::skill::mobskill_use(c, id, tick, mobs::MSC_SPAWN as i32, 0); // mob.cpp:1222
    0
}

/// mob.cpp mob_delayspawn (timer).
pub fn mob_delayspawn(c: &Ctx, tid: u64, id: u32) {
    let Some(mut md) = get(c, id) else { return };
    if md.spawn_timer != tid {
        log::error!("mob_delayspawn: Timer mismatch: {} != {}", tid, md.spawn_timer);
        return;
    }
    md.spawn_timer = 0;
    put(c, &md);
    mob_spawn(c, id);
}

/// mob.cpp mob_setdelayspawn: schedules the respawn (a monster without respawn data is freed).
pub fn mob_setdelayspawn(c: &Ctx, id: u32) -> i32 {
    let Some(mut md) = get(c, id) else { return 0 };
    let Some(sp) = (if md.spawn != 0 { c.db().spawn_data().id().find(md.spawn) } else { None }) else {
        // Doesn't has respawn data!
        return super::unit::unit_free(c, id, unit::CLR_DEAD);
    };
    let mut spawntime: u32 = sp.delay1; // Base respawn time
    if sp.delay2 != 0 {
        // random variance
        spawntime = spawntime.wrapping_add(c.rnd() % sp.delay2);
    }
    // Apply the spawn delay fix [Skotlex]
    if let Some(db) = super::db_find(c, sp.mob_id) {
        if db.status.has_mode(MD_STATUSIMMUNE) {
            // Status Immune
            if conf::boss_spawn_delay() != 100 {
                // Divide by 100 first to prevent overflows (precision loss is minimal as duration is in ms already)
                spawntime = spawntime / 100 * conf::boss_spawn_delay();
            }
        } else if db.status.mode & (MD_IGNOREMELEE | MD_IGNOREMAGIC | MD_IGNORERANGED | MD_IGNOREMISC) == (MD_IGNOREMELEE | MD_IGNOREMAGIC | MD_IGNORERANGED | MD_IGNOREMISC) {
            // Plant type
            if conf::plant_spawn_delay() != 100 {
                spawntime = spawntime / 100 * conf::plant_spawn_delay();
            }
        } else if conf::mob_spawn_delay() != 100 {
            // Normal mobs
            spawntime = spawntime / 100 * conf::mob_spawn_delay();
        }
    }
    spawntime = spawntime.max(1000); // Monsters should never respawn faster than 1 second
    if md.spawn_timer != 0 {
        timer::delete(c, md.spawn_timer);
    }
    md.spawn_timer = timer::add(c, c.tick() + spawntime as Tick, Func::MobDelaySpawn, id, 0);
    put(c, &md);
    0
}

/// mob.cpp mob_timer_delete (Alchemist Cannibalize / clone lifetimes): frees the monster.
pub fn mob_timer_delete(c: &Ctx, tid: u64, id: u32) {
    let Some(mut md) = get(c, id) else { return };
    if md.deletetimer != tid {
        log::error!("mob_timer_delete: Timer mismatch: {} != {}", tid, md.deletetimer);
        return;
    }
    // for Alchemist CANNIBALIZE [Lupus]
    md.deletetimer = 0;
    put(c, &md);
    super::unit::unit_free(c, id, unit::CLR_TELEPORT);
}

// ---------------------------------------------------------------------------------------------- spawn lines

/// npc.cpp npc_parse_mob2: spawns the monsters of a spawn line that are not active yet.
pub fn npc_parse_mob2(c: &Ctx, spawn_id: u32) {
    let Some(sp) = c.db().spawn_data().id().find(spawn_id) else { return };
    let args = SpawnArgs::of(&sp);
    let mut i = sp.active;
    while i < sp.num {
        let Some(mut md) = mob_spawn_dataset(c, &args, spawn_id) else { return };
        // Determine center cell for each mob in the spawn line
        if crate::conf::randomize_center_cell() {
            if sp.xs > 1 {
                md.center_x = c.rnd_value(sp.x as i32 - sp.xs as i32 + 1, sp.x as i32 + sp.xs as i32 - 1) as i16;
            }
            if sp.ys > 1 {
                md.center_y = c.rnd_value(sp.y as i32 - sp.ys as i32 + 1, sp.y as i32 + sp.ys as i32 - 1) as i16;
            }
            put(c, &md);
        }
        if let Some(mut row) = c.db().spawn_data().id().find(spawn_id) {
            row.active += 1;
            c.db().spawn_data().id().update(row);
        }
        mob_spawn(c, md.id);
        i += 1;
    }
}

/// npc.cpp npc_parse_mob's spawning tail, called by the world build for every stored spawn line: with dynamic_mobs
/// the line goes into its map's mob list (map_addmobtolist, at most MAX_MOB_LIST_PER_MAP per map) and spawns now
/// only if the map has players; otherwise it spawns now (and its `dynamic` flag stays off).
pub fn npc_parse_mob_spawn(c: &Ctx, spawn_id: u32) {
    let Some(mut sp) = c.db().spawn_data().id().find(spawn_id) else { return };
    let listed = c.db().spawn_data().by_map().filter(sp.map).filter(|s| s.dynamic && s.id != spawn_id).count();
    if crate::conf::dynamic_mobs() && listed < super::MAX_MOB_LIST_PER_MAP {
        sp.dynamic = true;
        c.db().spawn_data().id().update(sp.clone());
        // check if target map has players (usually shouldn't occur when map server is just starting, but not the
        // case when we do @reloadscript)
        if super::map_users(c, sp.map) > 0 {
            npc_parse_mob2(c, spawn_id);
        }
    } else {
        sp.dynamic = false;
        c.db().spawn_data().id().update(sp);
        npc_parse_mob2(c, spawn_id);
    }
}

/// npc.cpp npc_reload / npc_unload_... for the world rebuild: every monster is freed (unit_free CLR_OUTSIGHT), the
/// maps' removal timers are dropped, and the NPC id counter starts over (the rebuild stands for a server start).
pub fn on_world_rebuild(c: &Ctx) {
    let ids: Vec<u32> = c.db().mob_data().iter().map(|m| m.id).collect();
    for id in ids {
        super::unit::unit_free(c, id, unit::CLR_OUTSIGHT);
    }
    let mut pc_counts: std::collections::HashMap<u16, i32> = std::collections::HashMap::new();
    for u in c.db().unit().iter() {
        if u.kind == BL_PC && u.on_map {
            *pc_counts.entry(u.map).or_insert(0) += 1;
        }
    }
    let maps: Vec<super::MobMap> = c.db().mob_map().iter().collect();
    for mut r in maps {
        if r.mob_delete_timer != 0 {
            timer::delete(c, r.mob_delete_timer);
            r.mob_delete_timer = 0;
        }
        r.users = pc_counts.remove(&r.map).unwrap_or(0);
        super::put_mob_map(c, r);
    }
    for (m, users) in pc_counts {
        let mut r = super::mob_map(c, m);
        r.users = users;
        super::put_mob_map(c, r);
    }
    use crate::ids::id_counter;
    c.db().id_counter().kind().delete(&"npc_id".to_string());
}

// ---------------------------------------------------------------------------------------------- dynamic mobs

/// clif.cpp clif_parse_LoadEndAck: `if( mapdata->users++ == 0 && battle_config.dynamic_mobs ) map_spawnmobs(sd->m);`
/// and `sd->state.debug_remove_map = 0;` (clif.cpp:10805-10810). Also arms the AI timers (do_init_mob's, which the
/// module creates the first time a player enters a map).
pub fn map_users_inc(c: &Ctx, aid: u32, m: u16) {
    super::ai::ensure_ai_timers(c);
    let mut r = super::mob_map(c, m);
    let before = r.users;
    r.users += 1;
    super::put_mob_map(c, r);
    if crate::conf::dynamic_mobs()
        && (before <= 0 || c.db().spawn_data().by_map().filter(m).any(|s| s.dynamic && s.active == 0))
    {
        map_spawnmobs(c, m);
    }
    if let Some(mut p) = crate::pc::get(c, aid) {
        if p.connect_new {
            // a new map_session_data: killer / kill ids start at 0
            super::dead::reset_kill_params(c, aid);
        }
        p.debug_remove_map = false; // temporary state to track double remove_map's [FlavioJS]
        crate::pc::put(c, &p);
    }
}

/// unit.cpp unit_remove_map for BL_PC: `else if (--map[bl->m].users == 0 && battle_config.dynamic_mobs)
/// map_removemobs(bl->m);` — skipped (with rAthena's debug report) when users is already 0 or the player was removed
/// before (`debug_remove_map`).
pub fn map_users_dec(c: &Ctx, aid: u32, m: u16, debug_remove_map: bool) {
    let mut r = super::mob_map(c, m);
    if r.users <= 0 || debug_remove_map {
        log::debug!("unit_remove_map: unexpected state when removing player AID {aid} from map {m} (users={}, debug_remove_map={debug_remove_map})", r.users);
        return;
    }
    r.users -= 1;
    let now = r.users;
    super::put_mob_map(c, r);
    if now == 0 && crate::conf::dynamic_mobs() {
        map_removemobs(c, m);
    }
}

/// map.cpp map_spawnmobs.
pub fn map_spawnmobs(c: &Ctx, m: u16) {
    let mut r = super::mob_map(c, m);
    if r.mob_delete_timer != 0 {
        // Mobs have not been removed yet [Skotlex]
        timer::delete(c, r.mob_delete_timer);
        r.mob_delete_timer = 0;
        super::put_mob_map(c, r);
    }
    let mut list: Vec<u32> = c.db().spawn_data().by_map().filter(m).filter(|s| s.dynamic).map(|s| s.id).collect();
    list.sort_unstable();
    for sid in list.into_iter().take(super::MAX_MOB_LIST_PER_MAP) {
        npc_parse_mob2(c, sid);
    }
    // etc_log: off
}

/// map.cpp map_removemobs.
pub fn map_removemobs(c: &Ctx, m: u16) {
    let mut r = super::mob_map(c, m);
    if r.mob_delete_timer != 0 {
        return; // Mobs are already scheduled for removal
    }
    // Don't remove mobs on instance map
    if crate::instance_map::map_instance_id(c, m) > 0 {
        return;
    }
    r.mob_delete_timer = timer::add(c, c.tick() + conf::mob_remove_delay(), Func::MapRemoveMobs, m as u32, 0);
    super::put_mob_map(c, r);
}

/// map.cpp map_removemobs_timer.
pub fn map_removemobs_timer(c: &Ctx, tid: u64, m: u32) {
    let m = m as u16;
    let mut r = super::mob_map(c, m);
    if r.mob_delete_timer != tid {
        log::error!("map_removemobs_timer mismatch: {} != {} (map {m})", r.mob_delete_timer, tid);
        return;
    }
    r.mob_delete_timer = 0;
    let users = r.users;
    super::put_mob_map(c, r);
    if users > 0 {
        return; // Map not empty!
    }
    let ids: Vec<u32> = mobs_on_map(c, m).iter().map(|u| u.id).collect();
    for id in ids {
        map_removemobs_sub(c, id);
    }
}

/// map.cpp map_removemobs_sub.
fn map_removemobs_sub(c: &Ctx, id: u32) -> bool {
    let Some(md) = get(c, id) else { return false };
    // When not to remove mob: doesn't respawn and is not a slave
    if md.spawn == 0 && md.master_id == 0 {
        return false;
    }
    // respawn data is not in cache
    if md.spawn != 0 && !c.db().spawn_data().id().find(md.spawn).map(|s| s.dynamic).unwrap_or(false) {
        return false;
    }
    // hasn't spawned yet
    if md.spawn_timer != 0 {
        return false;
    }
    // is damaged and mob_remove_damaged is off
    if !conf::mob_remove_damaged() && md.status.hp < md.status.max_hp {
        return false;
    }
    // is a mvp
    if super::get_bosstype(&md) == mobs::BossType::Mvp {
        return false;
    }
    super::unit::unit_free(c, id, unit::CLR_OUTSIGHT);
    true
}

// ---------------------------------------------------------------------------------------------- one-time spawns

/// mob.cpp mob_once_spawn_sub: a monster without respawn data at (x, y) (a free cell next to `bl` when x or y < 0,
/// anywhere on the map when that fails).
#[allow(clippy::too_many_arguments)]
pub fn mob_once_spawn_sub(c: &Ctx, bl: Option<&Unit>, m: u16, x: i16, y: i16, mobname: Option<&str>, mob_id: i32, event: Option<&str>, size: u8, ai: u8) -> Option<MobData> {
    let mut data = SpawnArgs { m, num: 1, id: mob_id, size, ai, ..Default::default() };
    data.name = match mobname {
        Some(n) => truncate_name(n),
        None => {
            if conf::override_mob_names() == 1 {
                "--en--".into()
            } else {
                "--ja--".into()
            }
        }
    };
    if let Some(e) = event {
        data.eventname = e.chars().take(crate::script::npc_run::EVENT_NAME_LENGTH - 1).collect();
    }
    let (mut x, mut y) = (x, y);
    // Locate spot next to player.
    if let Some(b) = bl {
        if x < 0 || y < 0 {
            match map_search_freecell(c, Some((b.map, b.x, b.y)), m as i32, x, y, 1, 1, 0, 50) {
                Some((nx, ny)) => {
                    x = nx;
                    y = ny;
                }
                None => {
                    // rAthena leaves *x / *y at the source's position
                    x = b.x;
                    y = b.y;
                }
            }
        }
    }
    let Some(md) = crate::data::maps().get(m) else { return None };
    // if none found, pick random position on map
    if x <= 0 || x >= md.xs || y <= 0 || y >= md.ys {
        if let Some((nx, ny)) = map_search_freecell(c, None, m as i32, x, y, -1, -1, 1, 50) {
            x = nx;
            y = ny;
        }
    }
    data.x = x;
    data.y = y;
    let (mut name, mut ev) = (data.name.clone(), data.eventname.clone());
    if !mob_parse_dataset(c, data.id, data.num, &mut name, &mut ev) {
        return None;
    }
    data.name = name;
    data.eventname = ev;
    mob_spawn_dataset(c, &data, 0)
}

/// mob.cpp mob_once_spawn: `amount` monsters at (x, y) on map m (mob_id < 0: a random one of group -mob_id-1).
/// Returns the id of the last one spawned (0 none).
#[allow(clippy::too_many_arguments)]
pub fn mob_once_spawn(c: &Ctx, sd: Option<&Unit>, m: i32, x: i16, y: i16, mobname: Option<&str>, mob_id: i32, amount: i32, event: Option<&str>, size: u8, ai: u8) -> u32 {
    if m < 0 || amount <= 0 {
        return 0; // invalid input
    }
    let lv = match sd {
        Some(s) => crate::status::status_get_lv(c, s),
        None => 255,
    };
    let mut last = 0u32;
    for _ in 0..amount {
        let cls = if mob_id >= 0 {
            mob_id
        } else {
            let flag = if conf::random_monster_checklv() { mobs::RMF_DB_RATE | mobs::RMF_CHECK_MOB_LV } else { mobs::RMF_DB_RATE };
            get_random_id(c, -mob_id - 1, flag, lv)
        };
        let Some(md) = mob_once_spawn_sub(c, sd, m as u16, x, y, mobname, cls, event, size, ai) else { continue };
        if mob_id == mobs::MOBID_EMPERIUM as i32 {
            // The Emperium's castle / guild data (guardian_data; woe/guardian.rs)
            crate::woe::guardian::mob_once_spawn_emperium(c, md.id, m as u16);
        }
        mob_spawn(c, md.id);
        if mob_id < 0 && conf::dead_branch_active() {
            // Behold Aegis's masterful decisions yet again...
            if let Some(u) = unit::get(c, md.id) {
                crate::status::sc::sc_start4(c, None, &u, crate::status::consts::SC_MODECHANGE, 100, 1, 0, mobs::MD_AGGRESSIVE | mobs::MD_CANATTACK | mobs::MD_CANMOVE | mobs::MD_ANGRY, 0, 60000);
            }
        }
        last = md.id;
    }
    last // id of last spawned mob
}

/// mob.cpp mob_once_spawn_area: `amount` monsters on random passable cells of the rectangle.
#[allow(clippy::too_many_arguments)]
pub fn mob_once_spawn_area(c: &Ctx, sd: Option<&Unit>, m: i32, x0: i16, y0: i16, x1: i16, y1: i16, mobname: Option<&str>, mob_id: i32, amount: i32, event: Option<&str>, size: u8, ai: u8) -> u32 {
    if m < 0 || amount <= 0 {
        return 0; // invalid input
    }
    let (mut x0, mut y0, mut x1, mut y1) = (x0 as i32, y0 as i32, x1 as i32, y1 as i32);
    // normalize x/y coordinates
    if x0 > x1 {
        std::mem::swap(&mut x0, &mut x1);
    }
    if y0 > y1 {
        std::mem::swap(&mut y0, &mut y1);
    }
    // choose a suitable max. number of attempts
    let max = ((y1 - y0 + 1) * (x1 - x0 + 1) * 3).min(1000);
    let (mut lx, mut ly) = (-1i32, -1i32);
    let mut id = 0;
    for _ in 0..amount {
        let (mut x, mut y);
        let mut j = 0;
        // find a suitable map cell
        loop {
            x = (c.rnd() % (x1 - x0 + 1) as u32) as i32 + x0;
            y = (c.rnd() % (y1 - y0 + 1) as u32) as i32 + y0;
            j += 1;
            if !(map::getcell(c, m as u16, x as i16, y as i16, CellChk::ChkNoPass) && j < max) {
                break;
            }
        }
        if j == max {
            // attempt to find an available cell failed
            if lx == -1 && ly == -1 {
                return 0; // total failure
            }
            // fallback to last good x/y pair
            x = lx;
            y = ly;
        }
        // record last successful coordinates
        lx = x;
        ly = y;
        id = mob_once_spawn(c, sd, m, x as i16, y as i16, mobname, mob_id, 1, event, size, ai);
    }
    id // id of last spawned mob
}

/// mob.cpp mob_get_random_id with the reducer RNG and the spawn data of the world.
pub fn get_random_id(c: &Ctx, type_: i32, flag: u32, lv: i32) -> i32 {
    let mut rv = |a: i32, b: i32| c.rnd_value(a, b);
    let mut r = || c.rnd();
    mobs::mob_get_random_id(type_, flag, lv, &mut rv, &mut r, &|id| mob_has_spawn(c, id))
}

/// mob.cpp mob_has_spawn: the monster has a spawn line (mob_spawn_data).
pub fn mob_has_spawn(c: &Ctx, mob_id: u16) -> bool {
    c.db().spawn_data().iter().any(|s| s.mob_id == mob_id as i32 && s.num > 0)
}

/// mob_has_spawn for many monsters at once (the name searches test it for every monster of the database): the
/// monsters of mob_spawn_data, read once.
pub fn mob_has_spawn_fn(c: &Ctx) -> impl Fn(u16) -> bool {
    let set: std::collections::HashSet<u16> = c.db().spawn_data().iter().filter(|s| s.num > 0).map(|s| s.mob_id as u16).collect();
    move |id| set.contains(&id)
}

/// mob.cpp mob_get_spawns: (map, quantity) per map, most monsters first (mob_add_spawn sums the lines of a map and
/// re-sorts after each addition).
pub fn mob_get_spawns(c: &Ctx, mob_id: u16) -> Vec<(u16, u32)> {
    let mut lines: Vec<SpawnData> = c.db().spawn_data().iter().filter(|s| s.mob_id == mob_id as i32).collect();
    lines.sort_by_key(|s| s.id);
    let mut out: Vec<(u16, u32)> = Vec::new();
    for s in lines {
        if s.num == 0 {
            continue; // ignore empty spawns
        }
        match out.iter_mut().find(|(m, _)| *m == s.map) {
            Some(e) => e.1 += s.num as u32,
            None => out.push((s.map, s.num as u32)),
        }
        // std::sort by quantity, descending (libstdc++'s introsort: equal quantities keep no particular order)
        super::stdsort::std_sort(&mut out, |a, b| a.1 > b.1);
    }
    out
}

// ---------------------------------------------------------------------------------------------- slaves

/// mob.cpp mob_summonslave: summons `amount` monsters from value[0..5] around md2 in round-robin fashion.
/// `skill_id` is the summoning skill (NPC_SUMMONSLAVE makes them slaves of md2).
pub fn mob_summonslave(c: &Ctx, md2_id: u32, value: &[i32], amount: i32, skill_id: u16) -> i32 {
    let Some(mut md2) = get(c, md2_id) else { return 0 };
    let Some(u2) = unit::get(c, md2_id) else { return 0 };
    let mut data = SpawnArgs { m: u2.map, x: u2.x, y: u2.y, num: 1, size: md2.special_size, ai: md2.special_ai, ..Default::default() };
    if value.is_empty() || mobs::mobdb_checkid(value[0]) == 0 {
        return 0;
    }
    // Flags this monster is able to summon; saves a worth amount of memory upon deletion
    md2.can_summon = true;
    put(c, &md2);
    let mut count = 0usize;
    while count < 5 && count < value.len() && mobs::mobdb_checkid(value[count]) != 0 {
        count += 1;
    }
    if count < 1 {
        return 0;
    }
    let mut amount = amount;
    let mut k: i32 = 0;
    if amount > 0 && (amount as usize) < count {
        // Do not start on 0, pick some random sub subset [Skotlex]
        k = (c.rnd() % count as u32) as i32;
        amount += k; // Increase final value by same amount to preserve total number to summon.
    }
    let hp_rate = if !conf::monster_class_change_recover() && (skill_id == crate::db::skills::ids::NPC_TRANSFORMATION || skill_id == crate::db::skills::ids::NPC_METAMORPHOSIS) {
        get_percentage(md2.status.hp as i64, md2.status.max_hp as i64)
    } else {
        0
    };
    while k < amount {
        data.id = value[k as usize % count]; // Summon slaves in round-robin fashion. [Skotlex]
        k += 1;
        if mobs::mobdb_checkid(data.id) == 0 {
            continue;
        }
        let u2 = unit::get(c, md2_id).unwrap_or(u2.clone());
        let found = if skill_id != crate::db::skills::ids::NPC_DEATHSUMMON { map_search_freecell(c, Some((u2.map, u2.x, u2.y)), 0, 0, 0, super::MOB_SLAVEDISTANCE, super::MOB_SLAVEDISTANCE, 0, 50) } else { None };
        match found {
            Some((x, y)) => {
                data.x = x;
                data.y = y;
            }
            None => {
                data.x = u2.x;
                data.y = u2.y;
            }
        }
        // These two need to be loaded from the db for each slave.
        data.name = if conf::override_mob_names() == 1 { "--en--".into() } else { "--ja--".into() };
        let (mut name, mut ev) = (data.name.clone(), data.eventname.clone());
        if !mob_parse_dataset(c, data.id, data.num, &mut name, &mut ev) {
            continue;
        }
        let mut d2 = data.clone();
        d2.name = name;
        d2.eventname = ev;
        let Some(mut md) = mob_spawn_dataset(c, &d2, 0) else { continue };
        if skill_id == crate::db::skills::ids::NPC_SUMMONSLAVE {
            md.master_id = md2_id;
            md.special_ai = md2.special_ai;
            put(c, &md);
        }
        mob_spawn(c, md.id);
        let mut md = get(c, md.id).unwrap_or(md);
        if hp_rate != 0 {
            // Scale HP
            md.status.hp = (md.status.max_hp as i64 * hp_rate / 100) as u32;
            put(c, &md);
        }
        if skill_id == crate::db::skills::ids::NPC_SUMMONSLAVE {
            // Only appies to NPC_SUMMONSLAVE: Inherit the aggressive mode of the master.
            super::status::status_calc_slave_mode(c, md.id);
        }
        let slave_id = md.id;
        let md2 = get(c, md2_id).unwrap_or(md2.clone());
        if md2.copy_master_mode {
            let mut md = get(c, md.id).unwrap_or(md);
            md.status.mode = md2.status.mode;
            put(c, &md);
        }
        // clif_skill_nodamage(md, *md, skill_id, amount) (mob.cpp:4055)
        if let Some(mu) = unit::get(c, slave_id) {
            crate::skill::clif::clif_skill_nodamage(c, Some(&mu), &mu, skill_id, amount, true);
        }
    }
    0
}

/// common/utils get_percentage: (int)(A * 100 / B) with the 64-bit guard rAthena uses.
pub fn get_percentage(a: i64, b: i64) -> i64 {
    if b == 0 {
        log::error!("get_percentage: division by zero! (A={a},B={b})");
        return !0;
    }
    a * 100 / b
}

/// mob.cpp mob_countslave: slaves of `master` on its map.
pub fn mob_countslave(c: &Ctx, master: &Unit) -> usize {
    mobs_on_map(c, master.map).iter().filter(|u| get(c, u.id).map(|m| m.master_id == master.id).unwrap_or(false)).count()
}

/// mob.cpp mob_deleteslave: kills md's slaves on its map (status_kill).
pub fn mob_deleteslave(c: &Ctx, md_id: u32, m: u16) {
    for u in mobs_on_map(c, m) {
        if get(c, u.id).map(|s| s.master_id > 0 && s.master_id == md_id).unwrap_or(false) {
            crate::status::hp::status_kill(c, u.id);
        }
    }
}

/// mob.cpp mob_removeslaves: frees the slaves of a player leaving (unit_free CLR_OUTSIGHT).
pub fn mob_removeslaves(c: &Ctx, bl: &Unit) -> i32 {
    let mut n = 0;
    for u in mobs_on_map(c, bl.map) {
        if get(c, u.id).map(|s| s.master_id == bl.id).unwrap_or(false) {
            super::unit::unit_free(c, u.id, unit::CLR_OUTSIGHT);
            n += 1;
        }
    }
    n
}

/// mob.cpp mob_warpslave: warps md's slaves next to it (range at least 1).
pub fn mob_warpslave(c: &Ctx, bl: &Unit, range: i16) -> i32 {
    let range = range.max(1); // Min range needed to avoid crashes and stuff. [Skotlex]
    let mut n = 0;
    for u in mobs_on_map(c, bl.map) {
        if get(c, u.id).map(|s| s.master_id == bl.id).unwrap_or(false) {
            // mob_warpslave_sub: map_search_freecell(master, 0, &x, &y, range, range, 0), then unit_warp
            let (x, y) = map_search_freecell(c, Some((bl.map, bl.x, bl.y)), 0, 0, 0, range, range, 0, 50).unwrap_or((bl.x, bl.y));
            super::unit::unit_warp(c, u.id, bl.map as i32, x, y, unit::CLR_TELEPORT);
            n += 1;
        }
    }
    n
}

/// Is the monster a (non-dynamic) plant? mob.cpp's plant test used for spawn delays.
pub fn is_plant(md: &MobData) -> bool {
    has_mode(md, MD_IGNOREMELEE | MD_IGNOREMAGIC | MD_IGNORERANGED | MD_IGNOREMISC)
}

// ---------------------------------------------------------------------------------------------- clones

/// mob.cpp mob_clone_spawn: a monster copy of player `sd_id` (its name, level, base status, look and options) at
/// (x, y) on map m. flag & 1: friendly (AI_ATTACK, no looting); `master_id` (a unit id) attaches it; `duration` (ms)
/// deletes it after a while. Returns the clone's unit id (0: failed). The clone's skills are made from the player's
/// skill tree (mob/skill.rs mob_clone_skills).
#[allow(clippy::too_many_arguments)]
pub fn mob_clone_spawn(c: &Ctx, sd_id: u32, m: i32, x: i16, y: i16, event: &str, master_id: u32, mode: i32, flag: i32, duration: u32) -> u32 {
    use super::rtdb;
    let Some(sd) = unit::get(c, sd_id).filter(|u| u.kind == BL_PC) else { return 0 };
    if crate::pc::isdead(c, sd_id) && master_id != 0 && flag & 1 != 0 {
        return 0;
    }
    // ARR_FIND( MOB_CLONE_START, MOB_CLONE_END, mob_id, !mob_db.exists(mob_id) );
    let Some(mob_id) = (mobs::MOB_CLONE_START..mobs::MOB_CLONE_END).find(|&id| !rtdb::exists(c, id as u32)) else { return 0 };
    let mut status = crate::status::status_get_base_status(c, &sd);
    status.rhw.atk2 = (status.dex as i32 + status.rhw.atk as i32 + status.rhw.atk2 as i32) as u16; // Max ATK
    status.rhw.atk = status.dex as u16; // Min ATK
    if status.lhw.atk != 0 {
        status.lhw.atk2 = (status.dex as i32 + status.lhw.atk as i32 + status.lhw.atk2 as i32) as u16; // Max ATK
        status.lhw.atk = status.dex as u16; // Min ATK
    }
    if mode > mobs::MD_NONE {
        status.mode = mode; // User provided mode.
    } else if flag & 1 != 0 {
        status.mode &= !MD_LOOTER; // Friendly Character, remove looting.
    }
    status.hp = status.max_hp;
    status.sp = status.max_sp;
    // Go Backwards to give better priority to advanced skills (mob.cpp:4630-4768). rAthena fills db->skill after
    // mob_db.put; nothing reads the entry in between, so the list is made first and stored with it.
    let skill = super::skill::mob_clone_skills(c, &sd, flag).iter().map(rtdb::CloneSkill::from).collect();
    let cl = rtdb::CloneDb {
        name: sd.name.clone(),
        lv: crate::status::status_get_lv(c, &sd) as u16,
        status,
        vd: sd.vd.clone(),
        option: sd.sc.option as u32,
        skill,
    };
    rtdb::put_clone(c, mob_id as u32, cl);
    // Finally, spawn it.
    let Some(mut md) = mob_once_spawn_sub(c, Some(&sd), m as u16, x, y, Some("--en--"), mob_id, Some(event), crate::status::defs::SZ_SMALL as u8, super::AI_NONE) else {
        return 0; // Failed?
    };
    md.special_clone = true;
    if master_id != 0 || flag != 0 || duration != 0 {
        // Further manipulate crafted char.
        if flag & 1 != 0 {
            md.special_ai = super::AI_ATTACK; // Friendly Character
        }
        if master_id != 0 {
            md.master_id = master_id; // Attach to Master
        }
        if duration != 0 {
            // Auto Delete after a while.
            if md.deletetimer != 0 {
                timer::delete(c, md.deletetimer);
            }
            md.deletetimer = timer::add(c, c.tick() + duration as Tick, Func::MobTimerDelete, md.id, 0);
        }
    }
    put(c, &md);
    mob_spawn(c, md.id);
    md.id
}

/// mob.cpp mob_clone_delete: a clone's runtime database entry goes with it. Returns 1 for a clone.
pub fn mob_clone_delete(c: &Ctx, md: &mut MobData) -> i32 {
    if super::rtdb::mob_is_clone(c, md.mob_id) != 0 {
        super::rtdb::erase_clone(c, md.mob_id as u32);
        // Clear references to the db (md->db / md->vd: the row's mob_id no longer resolves)
        return 1;
    }
    0
}
