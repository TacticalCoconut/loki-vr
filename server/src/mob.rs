//! map/mob.cpp (rAthena e985006, RENEWAL): monsters at run time. The static databases (mob_db, drops, skills, chat,
//! summon groups, map drops) are `db/mobs.rs`; this file holds mob_data and the functions every other part of the
//! monster system shares. Submodules, each named after what it ports:
//! - `spawn`: mob_spawn_dataset, mob_spawn, mob_setdelayspawn / mob_delayspawn, npc_parse_mob2, map_spawnmobs /
//!   map_removemobs (dynamic mobs), mob_once_spawn(_area), mob_summonslave, mob_clone_spawn, npc_get_new_npc_id.
//! - `status`: status_calc_mob_, status_set_viewdata for BL_MOB, the status_get_* data of monsters.
//! - `unit`: the unit.cpp parts monsters need (unit_walktobl, unit_update_chase, unit_can_reach_*, unit_warp,
//!   unit_free / unit_remove_map for BL_MOB, unit_stop_walking_soon, exact sub-cell positions).
//! - `ai`: mob_ai_sub_hard and its helpers, the lazy AI, the AI timers, slaves, looting, assist.
//! - `dead`: mob_log_damage, mob_damage, the BL_MOB side of status_damage / status_heal / status_revive, mob_dead
//!   (EXP, drops, MVP), mob_revive, mob_heal, the delayed drops.
//! - `clif`: the monster packets (HP bar, MVP, size effects, delayed vanish).
//! - `cmd`: the monster @commands (the script commands are script/buildins/mob.rs).
//! - `rtdb`: run-time changes to the monster database (addmonsterdrop / delmonsterdrop, clones) and item_data's
//!   mob[] search data (mob_reload_itemmob_data).
//! - `conf`: the battle_config values monsters read.
//! - `skill`: monster skill use (mobskill_use, mobskill_event, mobskill_delay, the condition searches, the mob chat
//!   messages, a clone's skill list); the NPC_ skills' effects are the next wave's.
//!
//! Combat (battle_check_target, battle_check_range, unit_attack, damage) belongs to the battle agent: `battle_iface`
//! forwards to its port (src/battle/).

pub mod ai;
pub mod clif;
pub mod cmd;
pub mod conf;
pub mod dead;
pub mod rtdb;
pub mod skill; // monster skills (wave D skillcore)
pub mod spawn;
pub mod status;
pub mod stdsort;
pub mod status_bl;
pub mod unit;
pub mod battle_iface;

use spacetimedb::{table, SpacetimeType, Table};

use crate::clif::{self as core_clif, Target};
use crate::inventory::Item;
use crate::net::packets::PACKET_ZC_ACK_REQNAMEALL_NPC;
use crate::status::data::StatusData;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub use crate::db::mobs::{
    BossType, MSS_ANGRY, MSS_ANY, MSS_ANYTARGET, MSS_BERSERK, MSS_DEAD, MSS_FOLLOW, MSS_IDLE, MSS_LOOT, MSS_RUSH,
    MSS_WALK,
};

/// conf/battle/monster.conf `show_mob_info: 0`.
pub fn show_mob_info() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::SHOW_MOB_INFO)
}

/// map.hpp DAMAGELOG_SIZE / LOOTITEM_SIZE; mob.hpp MOB_SLAVEDISTANCE.
pub const DAMAGELOG_SIZE: usize = 20;
pub const LOOTITEM_SIZE: usize = 10;
pub const MOB_SLAVEDISTANCE: i16 = 2;
/// mob.hpp MIN_MOBTHINKTIME / MIN_MOBLINKTIME / MIN_RANDOMWALKTIME / MOB_SKILL_INTERVAL.
pub const MIN_MOBTHINKTIME: Tick = 100;
pub const MIN_MOBLINKTIME: Tick = 1000;
pub const MIN_RANDOMWALKTIME: Tick = 4000;
pub const MOB_SKILL_INTERVAL: Tick = 1000;
/// mob.cpp ACTIVE_AI_RANGE (distance added to AREA_SIZE at which monsters enter active AI) and RUDE_ATTACKED_COUNT.
pub const ACTIVE_AI_RANGE: i16 = 4;
pub const RUDE_ATTACKED_COUNT: u8 = 1;
/// map.hpp MAX_MOB_LIST_PER_MAP (dynamic spawn lines per map).
pub const MAX_MOB_LIST_PER_MAP: usize = 128;
/// map.hpp MAX_MOBSKILL (md->skilldelay entries).
pub const MAX_MOBSKILL: usize = crate::db::mobs::MAX_MOBSKILL;
/// mmo.hpp NAME_LENGTH.
pub const NAME_LENGTH: usize = 24;
/// map.hpp enum e_race2 (the ones monster code names).
pub const RC2_GOBLIN: u8 = 1;
pub const RC2_GVG: u8 = 7;
pub const RC2_BATTLEFIELD: u8 = 8;
pub const RC2_TREASURE: u8 = 9;
/// map.hpp enum e_mapflag, the flags only monster code reads.
pub const MF_NOBRANCH: i16 = 3;
pub const MF_ALLOWKS: i16 = 48;
pub const MF_MONSTER_NOTELEPORT: i16 = 49;
pub const MF_NOTOMB: i16 = 59;
pub const MF_NORENEWALEXPPENALTY: i16 = 72;
pub const MF_NORENEWALDROPPENALTY: i16 = 73;
/// status.hpp e_manner_flags MANNER_NOSKILL.
pub const MANNER_NOSKILL: i32 = 0x02;

/// map.hpp enum mob_ai.
pub const AI_NONE: u8 = 0;
pub const AI_ATTACK: u8 = 1;
pub const AI_SPHERE: u8 = 2;
pub const AI_FLORA: u8 = 3;
pub const AI_ZANZOU: u8 = 4;
pub const AI_LEGION: u8 = 5;
pub const AI_FAW: u8 = 6;
pub const AI_GUILD: u8 = 7;
pub const AI_WAVEMODE: u8 = 8;
pub const AI_ABR: u8 = 9;
pub const AI_BIONIC: u8 = 10;
pub const AI_MAX: u8 = 11;

/// mob.hpp enum MobDamageLogFlag.
pub const MDLF_NORMAL: u8 = 0;
pub const MDLF_HOMUN: u8 = 1;
pub const MDLF_PET: u8 = 2;
pub const MDLF_SELF: u8 = 3;

/// mob.hpp struct s_dmglog.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct DmgLog {
    /// Char id (or the monster's own id for MDLF_SELF).
    pub id: u32,
    pub dmg: i64,
    /// Damage tanked from normal attacks of the monster (the MVP is the player with the highest dmg + dmg_tanked).
    pub dmg_tanked: i64,
    pub flag: u8,
}

/// map.hpp struct s_mob_lootitem.
#[derive(SpacetimeType, Clone, Debug, PartialEq)]
pub struct LootItem {
    pub item: Item,
    /// The monster that dropped the item.
    pub mob_id: u16,
}

/// mob.hpp struct mob_data, less its block_list / unit_data / view_data / status_change, which are the `unit` row
/// (and the status change tables) as for every unit. Timer fields hold timer ids (0 = INVALID_TIMER).
#[table(accessor = mob_data,
    index(accessor = by_spawn, btree(columns = [spawn])),
    index(accessor = by_master, btree(columns = [master_id])))]
#[derive(Clone, Debug, PartialEq)]
pub struct MobData {
    /// block_list id (npc_get_new_npc_id).
    #[primary_key]
    pub id: u32,
    /// md->mob_id: the current class.
    pub mob_id: i32,
    /// md->spawn: the `spawn_data` row (0 = no respawn data: a script / command / summoned monster).
    pub spawn: u32,
    /// md->level.
    pub level: i32,
    /// md->name.
    pub name: String,
    /// special_state (not reset on respawn): size, ai, clone.
    pub special_size: u8,
    pub special_ai: u8,
    pub special_clone: bool,
    // md->state (memset to 0 by mob_spawn)
    pub aggressive: bool,
    pub steal_coin_flag: bool,
    pub soul_change_flag: bool,
    pub can_escape: bool,
    pub npc_killmonster: bool,
    pub rebirth: bool,
    pub boss: bool,
    pub copy_master_mode: bool,
    pub skillstate: i32,
    pub steal_flag: u8,
    pub attacked_count: u8,
    pub provoke_flag: u32,
    /// md->status (battle status) and md->base_status (None = the database status is the base).
    pub status: StatusData,
    pub base_status: Option<StatusData>,
    /// md->dmglog (a deque, oldest first) and md->spotted_log (DAMAGELOG_SIZE char ids, 0 = free).
    pub dmglog: Vec<DmgLog>,
    pub spotted_log: Vec<u32>,
    pub spawn_timer: u64,
    pub center_x: i16,
    pub center_y: i16,
    /// md->lootitems: allocated for MD_LOOTER monsters only (`looter`); lootitem_count is its length.
    pub looter: bool,
    pub lootitems: Vec<LootItem>,
    pub target_id: u32,
    pub attacked_id: u32,
    pub norm_attacked_id: u32,
    pub areanpc_id: u32,
    pub bg_id: u32,
    pub next_walktime: Tick,
    pub next_thinktime: Tick,
    pub last_linktime: Tick,
    pub last_pcneartime: Tick,
    pub last_canmove: Tick,
    pub last_skillcheck: Tick,
    pub trickcasting: Tick,
    pub move_fail_count: i16,
    pub walktoxy_fail_count: u8,
    pub deletetimer: u64,
    pub master_id: u32,
    pub master_dist: i32,
    pub skill_idx: i8,
    pub skilldelay: Vec<Tick>,
    pub npc_event: String,
    pub idle_event: String,
    pub can_summon: bool,
    pub tomb_nid: u32,
    pub damagetaken: u16,
    /// md->vd_changed (mob_set_dynamic_viewdata: the view data is the monster's own copy).
    pub vd_changed: bool,
    /// unit_data::pos, the cache of unit_data::update_pos (exact coordinates while walking).
    pub pos_x: i16,
    pub pos_y: i16,
    pub pos_sx: u8,
    pub pos_sy: u8,
    pub pos_tick: Tick,
    /// md->guardian_data is set (the castle guardians and the Emperium; the data is woe/guardian.rs's `guardian_data`).
    pub guardian: bool,
}

/// The dynamic-mob part of struct map_data: `users` and `mob_delete_timer` (the moblist is the map's dynamic
/// `spawn_data` rows in load order).
#[table(accessor = mob_map)]
#[derive(Clone, Debug, Default)]
pub struct MobMap {
    #[primary_key]
    #[index(direct)]
    pub map: u16,
    /// mapdata->users: players that finished loading the map (LoadEndAck) and have not left it.
    pub users: i32,
    pub mob_delete_timer: u64,
}

// ---------------------------------------------------------------------------------------------- access

pub fn get(c: &Ctx, id: u32) -> Option<MobData> {
    if let Some(cached) = CACHE.with(|m| m.borrow().get(&id).cloned()) {
        return cached;
    }
    let row = c.db().mob_data().id().find(id);
    CACHE.with(|m| {
        m.borrow_mut().insert(id, row.clone());
    });
    row
}

/// Preloads all `mob_data` rows into the per-call cache and returns their ids (`mob_ai_lazy`).
pub fn preload_all(c: &Ctx) -> Vec<u32> {
    let mut ids = Vec::new();
    CACHE.with(|m| {
        let mut cache = m.borrow_mut();
        for row in c.db().mob_data().iter() {
            ids.push(row.id);
            cache.entry(row.id).or_insert(Some(row));
        }
    });
    ids
}

fn is_lazy_candidate(md: &MobData) -> bool {
    md.last_pcneartime != 0
        || mob_is_spotted(md)
        || md.target_id != 0
        || md.attacked_id != 0
        || md.master_id != 0
        || md.skillstate != MSS_IDLE
        || !md.idle_event.is_empty()
        || !md.dmglog.is_empty()
}

/// Returns the monster ids that `mob_ai_sub_lazy` needs to inspect on a normal lazy tick, maintaining an in-memory
/// set across calls so `on_wakeup` does not deserialize all ~3,700 idle monsters every second.
pub fn lazy_candidate_ids(c: &Ctx) -> Vec<u32> {
    use crate::mob::ai::mob_ai_state as _;
    let count = c.db().mob_data().count() as usize;
    let timers = c.db().mob_ai_state().key().find(0).map(|s| (s.hard_timer, s.lazy_timer)).unwrap_or((0, 0));
    let cached = LAZY_SET.with(|cell| {
        let guard = cell.borrow();
        match guard.as_ref() {
            Some((n, t, set)) if *n == count && *t == timers => Some(set.iter().copied().collect::<Vec<u32>>()),
            _ => None,
        }
    });
    if let Some(ids) = cached {
        return ids;
    }
    let mut set = std::collections::BTreeSet::new();
    CACHE.with(|m| {
        let mut cache = m.borrow_mut();
        for row in c.db().mob_data().iter() {
            if is_lazy_candidate(&row) {
                set.insert(row.id);
            }
            cache.entry(row.id).or_insert(Some(row));
        }
    });
    let ids: Vec<u32> = set.iter().copied().collect();
    LAZY_SET.with(|cell| {
        *cell.borrow_mut() = Some((count, timers, set));
    });
    ids
}

thread_local! {
    /// Monsters freed during this call (mob/unit.rs unit_free), as rAthena's map_freeblock keeps them until
    /// map_freeblock_unlock. Emptied when a call starts (world::run).
    static FREED: std::cell::RefCell<Vec<MobData>> = const { std::cell::RefCell::new(Vec::new()) };
    /// Per-call write-through cache of `mob_data` rows, emptied by `freed_reset` (`world::run`). Every point read and
    /// write of `mob_data` goes through `get` / `put` / `delete_row`, so repeated `mob::get` calls in the AI and combat
    /// loops read from memory and `mob::put` skips host table updates when the row is unchanged.
    static CACHE: std::cell::RefCell<std::collections::HashMap<u32, Option<MobData>>> =
        std::cell::RefCell::new(std::collections::HashMap::new());
    /// Cross-call set of non-idle monster ids for `mob_ai_lazy`, keyed by `(mob_data.count(), (hard_timer, lazy_timer))`
    /// and invalidated if a previous reducer call did not finish cleanly (`CALL_CLEAN`).
    static LAZY_SET: std::cell::RefCell<Option<(usize, (u64, u64), std::collections::BTreeSet<u32>)>> =
        const { std::cell::RefCell::new(None) };
    static CALL_CLEAN: std::cell::Cell<bool> = const { std::cell::Cell::new(true) };
}

/// unit_free's map_freeblock: the monster's last state stays readable through `get_or_freed` for this call.
pub fn freed_keep(md: MobData) {
    FREED.with(|f| {
        let mut f = f.borrow_mut();
        f.retain(|m| m.id != md.id);
        f.push(md);
    });
}

/// Removes a monster's `mob_data` row and marks it absent in the per-call cache (`mob/unit.rs` `unit_free`).
pub fn delete_row(c: &Ctx, id: u32) {
    let existed = CACHE.with(|m| m.borrow_mut().insert(id, None))
        .map(|o| o.is_some())
        .unwrap_or_else(|| c.db().mob_data().id().find(id).is_some());
    c.db().mob_data().id().delete(id);
    if existed {
        LAZY_SET.with(|cell| {
            if let Some((count, _, set)) = cell.borrow_mut().as_mut() {
                *count = count.saturating_sub(1);
                set.remove(&id);
            }
        });
    }
}

/// Forgets the monsters freed by an earlier call (world::run).
pub fn freed_reset() {
    FREED.with(|f| f.borrow_mut().clear());
    CACHE.with(|m| m.borrow_mut().clear());
    if !CALL_CLEAN.with(|c| c.replace(false)) {
        LAZY_SET.with(|cell| *cell.borrow_mut() = None);
    }
}

/// Marks the current `world::run` call as cleanly committed (`world::run` end).
pub fn mark_call_clean() {
    CALL_CLEAN.with(|c| c.set(true));
}

/// md from a pointer rAthena still holds: the live row, else the state it had when unit_free freed it in this call.
pub fn get_or_freed(c: &Ctx, id: u32) -> Option<MobData> {
    get(c, id).or_else(|| FREED.with(|f| f.borrow().iter().rev().find(|m| m.id == id).cloned()))
}

/// Saves the row and keeps the unit row's copies of what clif / walking read (hp, max_hp, speed, mode, level).
pub fn put(c: &Ctx, md: &MobData) {
    let cached = CACHE.with(|m| m.borrow().get(&md.id).cloned());
    let inserted = match cached {
        Some(Some(ref old)) if old == md => {
            return;
        }
        Some(Some(_)) => {
            c.db().mob_data().id().update(md.clone());
            false
        }
        Some(None) => {
            c.db().mob_data().insert(md.clone());
            true
        }
        None => {
            if c.db().mob_data().id().find(md.id).is_some() {
                c.db().mob_data().id().update(md.clone());
                false
            } else {
                c.db().mob_data().insert(md.clone());
                true
            }
        }
    };
    CACHE.with(|m| {
        m.borrow_mut().insert(md.id, Some(md.clone()));
    });
    let cand = is_lazy_candidate(md);
    LAZY_SET.with(|cell| {
        if let Some((count, _, set)) = cell.borrow_mut().as_mut() {
            if inserted {
                *count += 1;
            }
            if cand {
                set.insert(md.id);
            } else {
                set.remove(&md.id);
            }
        }
    });
    sync_unit(c, md);
}

/// The unit row mirrors the monster's battle status for the packets and the walking code (status_get_hp / max_hp /
/// speed / lv / mode, the boss type of the unit packets).
pub fn sync_unit(c: &Ctx, md: &MobData) {
    let Some(mut u) = crate::unit::get(c, md.id) else { return };
    let (hp, max_hp) = (md.status.hp as i64, md.status.max_hp as i64);
    let boss = get_bosstype(md) as u8;
    let dead = md.status.hp == 0;
    if u.hp != hp || u.max_hp != max_hp || u.speed != md.status.speed || u.mode != md.status.mode as u32 || u.level != md.level || u.boss != boss || u.dead != dead || u.name != md.name {
        u.hp = hp;
        u.max_hp = max_hp;
        u.speed = md.status.speed;
        u.mode = md.status.mode as u32;
        u.level = md.level;
        u.boss = boss;
        u.dead = dead;
        u.name = md.name.clone();
        crate::unit::save(c, &u);
    }
}

pub fn is_mob(c: &Ctx, id: u32) -> bool {
    get(c, id).is_some()
}

/// map_foreachinmap(…, m, BL_MOB): the monsters in map m's block list.
pub fn mobs_on_map(c: &Ctx, m: u16) -> Vec<Unit> {
    use crate::unit::unit as _;
    c.db().unit().by_map_kind().filter((m, crate::map::BL_MOB)).filter(|u| u.on_map).collect()
}

/// map.cpp map_foreachinmap(func, m, BL_MOB)'s order: block by block (BLOCK_SIZE 8 cells; by, then bx). Within a
/// block rAthena's list starts with the unit that entered the block last; the module does not record that (the
/// block order is the fixes agent's W9), so a block's monsters come newest id first, which is exact for monsters
/// that have not left their spawn block. Callers re-check `on_map` before acting on each one, as map_foreachinmap
/// checks bl->prev.
pub fn mobs_foreachinmap(c: &Ctx, m: u16) -> Vec<Unit> {
    let mut v = mobs_on_map(c, m);
    v.sort_by_key(|u| (u.y / 8, u.x / 8, std::cmp::Reverse(u.id)));
    v
}

/// mob.cpp mob_data::get_bosstype.
pub fn get_bosstype(md: &MobData) -> BossType {
    if md.status.mode & crate::db::mobs::MD_MVP == crate::db::mobs::MD_MVP {
        BossType::Mvp
    } else if md.status.class_ == crate::db::mobs::CLASS_BOSS {
        BossType::MiniBoss
    } else {
        BossType::None
    }
}

/// status.hpp status_has_mode on the monster's battle status.
pub fn has_mode(md: &MobData, mode: i32) -> bool {
    md.status.mode & mode == mode
}

/// md->db (mob_db.find(md->mob_id), with the run-time changes of `rtdb`).
pub fn db(c: &Ctx, md: &MobData) -> Option<&'static crate::db::mobs::MobDb> {
    db_find(c, md.mob_id)
}

/// mob_db.find(mob_id): the static database with the run-time changes (addmonsterdrop / delmonsterdrop, clones).
pub fn db_find(c: &Ctx, mob_id: i32) -> Option<&'static crate::db::mobs::MobDb> {
    if mob_id < 0 {
        return None;
    }
    rtdb::find(c, mob_id as u32)
}

/// mob.hpp DEFAULT_ENEMY_TYPE(md).
pub fn default_enemy_type(md: &MobData) -> u16 {
    use crate::map::*;
    if md.special_ai != AI_NONE {
        BL_CHAR
    } else {
        BL_MOB | BL_PC | BL_HOM | BL_MER
    }
}

/// map_getmapdata(m)->users.
pub fn map_users(c: &Ctx, m: u16) -> i32 {
    c.db().mob_map().map().find(m).map(|r| r.users).unwrap_or(0)
}

pub(crate) fn mob_map(c: &Ctx, m: u16) -> MobMap {
    c.db().mob_map().map().find(m).unwrap_or(MobMap { map: m, ..Default::default() })
}

pub(crate) fn put_mob_map(c: &Ctx, r: MobMap) {
    if c.db().mob_map().map().find(r.map).is_some() {
        c.db().mob_map().map().update(r);
    } else {
        c.db().mob_map().insert(r);
    }
}

/// mob.cpp mob_setstate: the new skill state, considering the aggressive bit.
pub fn mob_setstate(md: &mut MobData, skillstate: i32) {
    match skillstate {
        MSS_BERSERK | MSS_ANGRY => md.skillstate = if md.aggressive { MSS_ANGRY } else { MSS_BERSERK },
        MSS_RUSH | MSS_FOLLOW => md.skillstate = if md.aggressive { MSS_FOLLOW } else { MSS_RUSH },
        _ => {
            // When going to any state other than BERSERK/RUSH, aggressive should be reset
            md.aggressive = has_mode(md, crate::db::mobs::MD_ANGRY);
            md.skillstate = skillstate;
        }
    }
}

/// mob.cpp mob_is_chasing: MSS_LOOT, MSS_RUSH and MSS_FOLLOW are chase states.
pub fn mob_is_chasing(state: i32) -> bool {
    matches!(state, MSS_LOOT | MSS_RUSH | MSS_FOLLOW)
}

// ---------------------------------------------------------------------------------------------- spotted log

/// mob.cpp mob_clean_spotted: removes the characters that spotted the monster but are no longer online.
/// Returns whether an entry was cleared (the caller writes the row back only then).
pub fn mob_clean_spotted(c: &Ctx, md: &mut MobData) -> bool {
    let mut changed = false;
    for s in md.spotted_log.iter_mut() {
        if *s != 0 && crate::pc::charid2sd(c, *s).is_none() {
            *s = 0;
            changed = true;
        }
    }
    changed
}

/// mob.cpp mob_add_spotted.
/// Returns whether the log changed (the module saves the row only then: a monster near N players is visited N times
/// per AI tick, mob_ai_sub_foreachclient).
pub fn mob_add_spotted(md: &mut MobData, char_id: u32) -> bool {
    if md.spotted_log.iter().any(|&s| s == char_id) {
        return false;
    }
    if let Some(s) = md.spotted_log.iter_mut().find(|s| **s == 0) {
        *s = char_id;
        return true;
    }
    false
}

/// mob.cpp mob_is_spotted.
pub fn mob_is_spotted(md: &MobData) -> bool {
    md.spotted_log.iter().any(|&s| s != 0)
}

// ---------------------------------------------------------------------------------------------- clif_name

/// clif.cpp clif_name for BL_MOB with show_mob_info 0: ZC_ACK_REQNAMEALL_NPC with the unit's title and group id
/// (guardians with a guild answer ZC_ACK_REQNAMEALL with the guild and castle names: woe/guardian.rs).
pub fn name(c: &Ctx, src: &Unit, bl: &Unit, target: Target) {
    if crate::woe::guardian::clif_name(c, src, bl, target) {
        return;
    }
    let p = PACKET_ZC_ACK_REQNAMEALL_NPC {
        packet_id: PACKET_ZC_ACK_REQNAMEALL_NPC::ID,
        gid: bl.id as i32,
        name: bl.name.clone(),
        title: bl.ud.title.clone(),
        groupId: bl.ud.group_id,
    };
    core_clif::send(c, &p.to_vec(), src, target);
}
