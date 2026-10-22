//! The skill unit state as tables (skill.hpp s_skill_unit_group / skill_unit, unit_data's skillunits and
//! skillunittick, skill.cpp's skillusave_db and the group id counter, map_data::skill_duration), and the accessors of
//! map_session_data's skill_id_dance / skill_lv_dance (kept in skillcore's PcSkill row).
//!
//! A skill unit is a block_list of type BL_SKILL: its id, map and cell are a `unit` row (kind BL_SKILL, in the block
//! list while `on_map`), the rest is its `skill_unit` row. rAthena keeps a group's units in one array
//! (`group->unit[0..unit_count]`) and marks dead ones `alive = 0`; here a unit has rows from skill_initunit until the
//! end of skill_delunit (which clears `alive` first, as rAthena's dead unit stays readable in the array), and its array
//! index is `idx`. A unit's `group` pointer is its `group_id`: a missing group row is rAthena's `group == nullptr`.

use spacetimedb::{table, Table};

use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

use super::consts::MAX_SKILLUNITGROUPTICKSET;

/// skill.hpp struct s_skill_unit_group (skillunit_group_db's value).
#[table(accessor = skill_unit_group, index(accessor = by_src, btree(columns = [src_id])))]
#[derive(Clone, Debug)]
pub struct SkillUnitGroup {
    /// group_id (skill_get_new_group_id).
    #[primary_key]
    pub group_id: i32,
    /// The group's place in its source's `ud->skillunits` (a vector: push_back order, erase keeps the order).
    pub seq: u64,
    /// src_id: the caster's id (a player's account id).
    pub src_id: u32,
    pub party_id: i32,
    pub guild_id: i32,
    pub bg_id: i32,
    /// map (the source's map when the group was made).
    pub map: u16,
    /// target_flag: BCT_* for battle_check_target.
    pub target_flag: i32,
    /// bl_flag: BL_* for the map_foreachin* calls.
    pub bl_flag: i32,
    /// tick: when the group was made.
    pub tick: Tick,
    /// limit: life time (ms after `tick`).
    pub limit: i64,
    /// interval: the unit timer's interval (-1: none).
    pub interval: i32,
    pub skill_id: u16,
    pub skill_lv: u16,
    pub val1: i32,
    pub val2: i32,
    pub val3: i32,
    /// valstr: the Talkie Box / Graffiti message (None = nullptr).
    pub valstr: Option<String>,
    /// unit_id: UNT_* (what the client shows).
    pub unit_id: i32,
    pub link_group_id: i32,
    pub unit_count: i32,
    pub alive_count: i32,
    /// item_id: the trap item used.
    pub item_id: u32,
    /// state.ammo_consume.
    pub ammo_consume: bool,
    /// state.song_dance: 0x1 song / dance, 0x2 ensemble.
    pub song_dance: u8,
    /// state.guildaura.
    pub guildaura: bool,
}

/// skill.hpp struct skill_unit, without its block_list part (the `unit` row). A row exists from skill_initunit to the end of
/// skill_delunit (which clears `alive` first and removes the row last, as rAthena's unit stays readable meanwhile).
#[table(accessor = skill_unit, index(accessor = by_group, btree(columns = [group_id])))]
#[derive(Clone, Debug)]
pub struct SkillUnitRow {
    #[primary_key]
    pub id: u32,
    pub group_id: i32,
    /// The unit's index in `group->unit[]`.
    pub idx: i32,
    pub limit: i64,
    pub val1: i32,
    pub val2: i32,
    pub range: i16,
    pub alive: bool,
    pub hidden: bool,
}

/// A skill unit: its block_list (`bl`) and its skill_unit fields. rAthena code reads `unit->m / x / y` from the
/// block_list: here `bl.map / bl.x / bl.y`.
#[derive(Clone, Debug)]
pub struct SkillUnit {
    pub bl: Unit,
    pub group_id: i32,
    pub idx: i32,
    pub limit: i64,
    pub val1: i32,
    pub val2: i32,
    pub range: i16,
    pub alive: bool,
    pub hidden: bool,
}

impl SkillUnit {
    pub fn id(&self) -> u32 {
        self.bl.id
    }
    fn row(&self) -> SkillUnitRow {
        SkillUnitRow {
            id: self.bl.id,
            group_id: self.group_id,
            idx: self.idx,
            limit: self.limit,
            val1: self.val1,
            val2: self.val2,
            range: self.range,
            alive: self.alive,
            hidden: self.hidden,
        }
    }
}

/// unit_data::skillunittick[MAX_SKILLUNITGROUPTICKSET] of one block_list (zeroed like a new unit_data).
#[table(accessor = skill_unit_tickset)]
#[derive(Clone, Debug)]
pub struct SkillUnitTickset {
    #[primary_key]
    pub bl_id: u32,
    pub tick: Vec<i64>,
    pub id: Vec<i32>,
}

/// skill.cpp skillusave_db: char_id -> struct skill_usave (a unit to set again after the map loads).
#[table(accessor = skill_usave)]
#[derive(Clone, Debug)]
pub struct SkillUsave {
    #[primary_key]
    pub char_id: u32,
    pub skill_id: u16,
    pub skill_lv: u16,
}

/// map_data::skill_duration (map.hpp `std::unordered_map<uint16, int32>`): (map, skill id) -> percentage (the
/// skill_duration map flag).
#[table(accessor = map_skill_duration, index(accessor = by_map, btree(columns = [map])))]
#[derive(Clone, Debug)]
pub struct MapSkillDuration {
    #[primary_key]
    pub key: u32,
    pub map: u16,
    pub skill_id: u16,
    pub per: i32,
}

/// The iteration order of a map's `skill_duration` unordered_map (skill/umap.rs: libstdc++'s list order, bucket count
/// and rehash state), for @mapinfo's " > %s : %d%%" lines. No row = a fresh map (no entry yet, bucket count 1).
#[table(accessor = map_skill_duration_order)]
#[derive(Clone, Debug)]
pub struct MapSkillDurationOrder {
    #[primary_key]
    pub map: u16,
    pub order: Vec<u16>,
    pub buckets: u64,
    pub next_resize: u64,
}

/// The module-wide state of the unit system (one row, key 0): skill_unit_group_newid, skill_unit_timer's interval
/// timer id, and skillunit_db's free lock.
#[table(accessor = skill_unit_state)]
#[derive(Clone, Debug)]
pub struct SkillUnitState {
    #[primary_key]
    pub key: u8,
    /// skill.cpp `static int32 skill_unit_group_newid = MAX_SKILL`.
    pub group_newid: i32,
    /// The Func::SkillUnitTimer interval timer (0: not armed).
    pub timer: u64,
    /// skillunit_db's db_free_lock count (unitdb.rs).
    pub db_free_lock: u32,
    /// The next `seq` of a group (ud->skillunits push order).
    pub next_seq: u64,
}

pub fn state(c: &Ctx) -> SkillUnitState {
    c.db().skill_unit_state().key().find(0).unwrap_or(SkillUnitState {
        key: 0,
        group_newid: crate::db::skills::MAX_SKILL as i32,
        timer: 0,
        db_free_lock: 0,
        next_seq: 1,
    })
}

pub fn put_state(c: &Ctx, s: &SkillUnitState) {
    if c.db().skill_unit_state().key().find(0).is_some() {
        c.db().skill_unit_state().key().update(s.clone());
    } else {
        c.db().skill_unit_state().insert(s.clone());
    }
}

// ---------------------------------------------------------------------------------------------- groups

/// skill.cpp skill_id2group.
pub fn skill_id2group(c: &Ctx, group_id: i32) -> Option<SkillUnitGroup> {
    if group_id == 0 {
        return None;
    }
    c.db().skill_unit_group().group_id().find(group_id)
}

/// Writes a group's changed fields back, if the group still exists (a group rAthena holds a shared_ptr to may have
/// been deleted from skillunit_group_db meanwhile; writing to it then changes nothing that is looked up again).
pub fn put_group(c: &Ctx, g: &SkillUnitGroup) {
    if c.db().skill_unit_group().group_id().find(g.group_id).is_some() {
        c.db().skill_unit_group().group_id().update(g.clone());
    }
}

/// Changes a group in place (no-op when it is gone).
pub fn upd_group(c: &Ctx, group_id: i32, f: impl FnOnce(&mut SkillUnitGroup)) {
    if let Some(mut g) = skill_id2group(c, group_id) {
        f(&mut g);
        put_group(c, &g);
    }
}

/// unit_data::skillunits of `src_id`, in vector order.
pub fn groups_of(c: &Ctx, src_id: u32) -> Vec<SkillUnitGroup> {
    let mut v: Vec<SkillUnitGroup> = c.db().skill_unit_group().by_src().filter(src_id).collect();
    v.sort_by_key(|g| g.seq);
    v
}

// ---------------------------------------------------------------------------------------------- units

/// A skill unit with its block_list (its `alive` may be false while skill_delunit runs).
pub fn get_unit(c: &Ctx, id: u32) -> Option<SkillUnit> {
    let r = c.db().skill_unit().id().find(id)?;
    let bl = unit::get(c, id)?;
    Some(SkillUnit { bl, group_id: r.group_id, idx: r.idx, limit: r.limit, val1: r.val1, val2: r.val2, range: r.range, alive: r.alive, hidden: r.hidden })
}

/// A skill unit that is alive.
pub fn get_live_unit(c: &Ctx, id: u32) -> Option<SkillUnit> {
    get_unit(c, id).filter(|su| su.alive)
}

/// Writes a unit's skill_unit fields back (the block_list part moves through map_moveblock), if it still exists.
pub fn put_unit(c: &Ctx, su: &SkillUnit) {
    if c.db().skill_unit().id().find(su.bl.id).is_some() {
        c.db().skill_unit().id().update(su.row());
    }
}

/// Changes a unit in place (no-op when it is gone).
pub fn upd_unit(c: &Ctx, id: u32, f: impl FnOnce(&mut SkillUnit)) {
    if let Some(mut su) = get_unit(c, id) {
        f(&mut su);
        put_unit(c, &su);
    }
}

pub(super) fn insert_unit(c: &Ctx, su: &SkillUnit) {
    c.db().skill_unit().insert(su.row());
}

pub(super) fn delete_unit_row(c: &Ctx, id: u32) {
    c.db().skill_unit().id().delete(id);
}

/// The units of a group in `group->unit[]` order (rAthena's dead slots have no rows).
pub fn units_of(c: &Ctx, group_id: i32) -> Vec<SkillUnit> {
    let mut ids: Vec<(i32, u32)> = c.db().skill_unit().by_group().filter(group_id).map(|r| (r.idx, r.id)).collect();
    ids.sort();
    ids.into_iter().filter_map(|(_, id)| get_unit(c, id)).collect()
}

/// The group of a skill unit block_list (`((skill_unit*)bl)->group`), None when the unit is gone or has no group.
pub fn group_of_unit(c: &Ctx, bl_id: u32) -> Option<SkillUnitGroup> {
    let r = c.db().skill_unit().id().find(bl_id)?;
    skill_id2group(c, r.group_id)
}

// ---------------------------------------------------------------------------------------------- ticksets

/// A block_list's unit_data::skillunittick (a copy; `put_tickset` writes it back).
pub fn tickset(c: &Ctx, bl_id: u32) -> SkillUnitTickset {
    c.db().skill_unit_tickset().bl_id().find(bl_id).unwrap_or(SkillUnitTickset {
        bl_id,
        tick: vec![0; MAX_SKILLUNITGROUPTICKSET],
        id: vec![0; MAX_SKILLUNITGROUPTICKSET],
    })
}

pub fn put_tickset(c: &Ctx, t: &SkillUnitTickset) {
    if c.db().skill_unit_tickset().bl_id().find(t.bl_id).is_some() {
        c.db().skill_unit_tickset().bl_id().update(t.clone());
    } else {
        c.db().skill_unit_tickset().insert(t.clone());
    }
}

// ---------------------------------------------------------------------------------------------- dance

/// sd->skill_id_dance / sd->skill_lv_dance (skillcore's PcSkill row, skill/state.rs: 0, 0 after pc_authok).
pub fn dance(c: &Ctx, aid: u32) -> (u16, u16) {
    let s = crate::skill::state::get(c, aid);
    (s.skill_id_dance, s.skill_lv_dance)
}

/// Sets sd->skill_id_dance / sd->skill_lv_dance (players only, as rAthena's callers check sd first).
pub fn set_dance(c: &Ctx, aid: u32, skill_id: u16, skill_lv: u16) {
    crate::skill::state::with(c, aid, |s| {
        s.skill_id_dance = skill_id;
        s.skill_lv_dance = skill_lv;
    });
}

// ---------------------------------------------------------------------------------------------- per unit_data

/// A block_list's unit_data goes (unit_free): its skillunittick.
pub fn on_unit_freed(c: &Ctx, bl_id: u32) {
    c.db().skill_unit_tickset().bl_id().delete(bl_id);
}

// ---------------------------------------------------------------------------------------------- skill_duration

fn duration_key(m: u16, skill_id: u16) -> u32 {
    ((m as u32) << 16) | skill_id as u32
}

fn duration_order(c: &Ctx, m: u16) -> crate::skill::umap::Umap {
    c.db()
        .map_skill_duration_order()
        .map()
        .find(m)
        .map(|r| crate::skill::umap::Umap { order: r.order, buckets: r.buckets, next_resize: r.next_resize })
        .unwrap_or_default()
}

fn put_duration_order(c: &Ctx, m: u16, u: crate::skill::umap::Umap) {
    let row = MapSkillDurationOrder { map: m, order: u.order, buckets: u.buckets, next_resize: u.next_resize };
    if c.db().map_skill_duration_order().map().find(m).is_some() {
        c.db().map_skill_duration_order().map().update(row);
    } else {
        c.db().map_skill_duration_order().insert(row);
    }
}

/// map.cpp map_skill_duration_add: an existing entry's percentage grows (`skill_duration[skill_id] += per`, int32),
/// else `insert({ skill_id, per })`.
pub fn map_skill_duration_add(c: &Ctx, m: u16, skill_id: u16, per: u16) {
    let key = duration_key(m, skill_id);
    match c.db().map_skill_duration().key().find(key) {
        Some(mut r) => {
            r.per = r.per.wrapping_add(per as i32);
            c.db().map_skill_duration().key().update(r);
        }
        None => {
            c.db().map_skill_duration().insert(MapSkillDuration { key, map: m, skill_id, per: per as i32 });
            let mut u = duration_order(c, m);
            u.insert(skill_id);
            put_duration_order(c, m, u);
        }
    }
}

/// `mapdata->skill_duration.insert({ skill_id, per })` (map.cpp map_data_copy): a key already there keeps its value.
pub fn map_skill_duration_insert(c: &Ctx, m: u16, skill_id: u16, per: i32) {
    let key = duration_key(m, skill_id);
    if c.db().map_skill_duration().key().find(key).is_some() {
        return;
    }
    c.db().map_skill_duration().insert(MapSkillDuration { key, map: m, skill_id, per });
    let mut u = duration_order(c, m);
    u.insert(skill_id);
    put_duration_order(c, m, u);
}

/// map.cpp map_setmapflag_sub MF_SKILL_DURATION: `mapdata->skill_duration.clear()` (the flag turned off; the
/// unordered_map keeps its bucket count).
pub fn map_skill_duration_clear(c: &Ctx, m: u16) {
    let keys: Vec<u32> = c.db().map_skill_duration().by_map().filter(m).map(|r| r.key).collect();
    for k in keys {
        c.db().map_skill_duration().key().delete(k);
    }
    if c.db().map_skill_duration_order().map().find(m).is_some() {
        let mut u = duration_order(c, m);
        u.clear();
        put_duration_order(c, m, u);
    }
}

/// mapdata->skill_duration[skill_id], when present.
pub fn map_skill_duration(c: &Ctx, m: u16, skill_id: u16) -> Option<i32> {
    c.db().map_skill_duration().key().find(duration_key(m, skill_id)).map(|r| r.per)
}

/// `for (const auto &it : mapdata->skill_duration)`: (skill id, percentage) in the unordered_map's iteration order.
pub fn map_skill_duration_list(c: &Ctx, m: u16) -> Vec<(u16, i32)> {
    duration_order(c, m).order.iter().filter_map(|&id| map_skill_duration(c, m, id).map(|per| (id, per))).collect()
}
