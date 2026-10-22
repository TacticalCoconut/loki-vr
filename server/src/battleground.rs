//! map/battleground.cpp: battleground teams (bg_team_db), the queue system of the client's battleground window
//! (bg_queues, feature.bgqueue), the run-time state of the battleground database (s_battleground_map::isReserved),
//! pc.cpp's queue timer (pc_set_bg_queue_timer / pc_delete_bg_queue_timer / pc_on_expire_active) and mob.cpp
//! mob_spawn_bg. The packets are in clif_bg.rs, the script commands in script/buildins/bg.rs. Progress, stubs and
//! parity: server/ARCHITECTURE.md.
//!
//! rAthena keeps `map_session_data *` in its member lists; here they are account ids (a member is always an online
//! player: every way out of the world goes through map_quit, which takes the player out of its team and queue).
//! `s_battleground_map *` (queue->map) is the index of the location in its battleground type's `Locations`, and the
//! locations' `isReserved` flags live in `bg_map_reserve` (the database itself is static data).
//!
//! Map indexes: rAthena stores `mapindex` numbers (0 = none); the module has one map server, so every mapindex is a map
//! id (`data::maps()`), and "mapindex 0" is `None`.

use spacetimedb::{table, SpacetimeType, Table};

use crate::db::misc::battleground::{battleground_db, bg_search_name, BattlegroundTeam, BattlegroundType, MAX_BG_MEMBERS};
use crate::script::reg::mapreg_num as _;
use crate::timer::{self, Func};
use crate::unit::{self, CLR_OUTSIGHT, CLR_TELEPORT};
use crate::world::Ctx;

/// battle/feature.conf `feature.bgqueue: on` (battle_config.feature_bgqueue; battle.cpp:9013-9018 keeps it on for
/// PACKETVER 20211103 >= 20120101).
pub fn feature_bgqueue() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_BGQUEUE) != 0
}
/// battle/battleground.conf `bgqueue_nowarp_mapflag: no` (battle_config.bgqueue_nowarp_mapflag).
pub fn bgqueue_nowarp_mapflag() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::BGQUEUE_NOWARP_MAPFLAG)
}
/// battle/battleground.conf `bg_update_interval: 1000` (battle_config.bg_update_interval, ms).
pub fn bg_update_interval() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::BG_UPDATE_INTERVAL) as i64
}

/// battleground.hpp e_queue_state.
pub const QUEUE_STATE_SETUP: u16 = 0;
pub const QUEUE_STATE_SETUP_DELAY: u16 = 1;
pub const QUEUE_STATE_ACTIVE: u16 = 2;
pub const QUEUE_STATE_ENDED: u16 = 3;

/// battleground.hpp e_bg_queue_apply_ack.
pub const BG_APPLY_NONE: u8 = 0;
pub const BG_APPLY_ACCEPT: u8 = 1;
pub const BG_APPLY_QUEUE_FINISHED: u8 = 2;
pub const BG_APPLY_INVALID_NAME: u8 = 3;
pub const BG_APPLY_INVALID_APP: u8 = 4;
pub const BG_APPLY_PLAYER_COUNT: u8 = 5;
pub const BG_APPLY_PLAYER_LEVEL: u8 = 6;
pub const BG_APPLY_DUPLICATE: u8 = 7;
pub const BG_APPLY_RECONNECT: u8 = 8;
pub const BG_APPLY_PARTYGUILD_LEADER: u8 = 9;
pub const BG_APPLY_PLAYER_CLASS: u8 = 10;

// ---------------------------------------------------------------------------------------------- state

/// map_session_data's battleground fields: sd->bg_id, sd->bg_queue_id and sd->tid_queue_active (0 = INVALID_TIMER). A
/// missing row is a player with all three at 0 (pc_setnewpc / pc_reg_received start them so); the row goes at unit_free.
#[table(accessor = pc_bg)]
#[derive(Clone, Debug, Default, PartialEq)]
pub struct PcBg {
    #[primary_key]
    pub account_id: u32,
    pub bg_id: i32,
    pub bg_queue_id: i32,
    pub tid_queue_active: u64,
}

/// battleground.hpp struct s_battleground_member_data (sd is the account id).
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct BgMember {
    pub aid: u32,
    pub x: i16,
    pub y: i16,
    pub afk: bool,
    /// entry_point (the place the player joined from through the queue; map None = 0, not joined by queue).
    pub entry_map: Option<u16>,
    pub entry_x: i16,
    pub entry_y: i16,
}

/// battleground.hpp struct s_battleground_data (bg_team_db, keyed by the team id).
#[table(accessor = bg_team)]
#[derive(Clone, Debug, Default)]
pub struct BgTeam {
    #[primary_key]
    pub id: i32,
    /// The members in vector order.
    pub members: Vec<BgMember>,
    /// cemetery (the respawn point; map None = 0: respawn not handled by the core).
    pub cemetery_map: Option<u16>,
    pub cemetery_x: i16,
    pub cemetery_y: i16,
    pub logout_event: String,
    pub die_event: String,
    pub active_event: String,
}

/// battleground.hpp struct s_battleground_queue (bg_queues, a vector in creation order = queue_id order).
#[table(accessor = bg_queue)]
#[derive(Clone, Debug, Default)]
pub struct BgQueue {
    #[primary_key]
    pub queue_id: i32,
    /// The battleground database id.
    pub id: i32,
    pub teama: Vec<u32>,
    pub teamb: Vec<u32>,
    pub required_players: i32,
    pub accepted_players: i32,
    pub state: u16,
    pub tid_expire: u64,
    pub tid_start: u64,
    pub tid_requeue: u64,
    /// queue->map: the index of the reserved location in the type's `Locations` (None = nullptr).
    pub map: Option<u32>,
}

/// s_battleground_map::isReserved of each battleground location (a row = reserved; key = type id << 16 | index).
#[table(accessor = bg_map_reserve)]
#[derive(Clone, Debug)]
pub struct BgMapReserve {
    #[primary_key]
    pub key: u64,
}

/// map_data::bgscore_lion / bgscore_eagle (the battleground score board of a map; a missing row is 0 / 0).
#[table(accessor = bg_score)]
#[derive(Clone, Debug, Default)]
pub struct BgScore {
    #[primary_key]
    pub map: u16,
    pub lion: i16,
    pub eagle: i16,
}

/// battleground.cpp's other globals: bg_queue_count and the bg_send_xy_timer interval timer.
#[table(accessor = bg_state)]
#[derive(Clone, Debug, Default)]
pub struct BgState {
    #[primary_key]
    pub key: u8,
    pub queue_count: i32,
    pub xy_timer: u64,
}

// ---------------------------------------------------------------------------------------------- row helpers

/// The player's battleground fields (zeros for a player without a row).
pub fn pcbg(c: &Ctx, aid: u32) -> PcBg {
    c.db().pc_bg().account_id().find(aid).unwrap_or(PcBg { account_id: aid, ..Default::default() })
}

fn put_pcbg(c: &Ctx, b: &PcBg) {
    match c.db().pc_bg().account_id().find(b.account_id) {
        Some(old) if old == *b => {}
        Some(_) => {
            c.db().pc_bg().account_id().update(b.clone());
        }
        None => {
            c.db().pc_bg().insert(b.clone());
        }
    }
}

/// sd->bg_id.
pub fn bg_id_of(c: &Ctx, aid: u32) -> i32 {
    c.db().pc_bg().account_id().find(aid).map(|b| b.bg_id).unwrap_or(0)
}

/// sd->bg_queue_id.
pub fn bg_queue_id_of(c: &Ctx, aid: u32) -> i32 {
    c.db().pc_bg().account_id().find(aid).map(|b| b.bg_queue_id).unwrap_or(0)
}

fn set_bg_id(c: &Ctx, aid: u32, v: i32) {
    let mut b = pcbg(c, aid);
    b.bg_id = v;
    put_pcbg(c, &b);
}

fn set_bg_queue_id(c: &Ctx, aid: u32, v: i32) {
    let mut b = pcbg(c, aid);
    b.bg_queue_id = v;
    put_pcbg(c, &b);
}

/// util::umap_find(bg_team_db, bg_id).
pub fn team(c: &Ctx, bg_id: i32) -> Option<BgTeam> {
    c.db().bg_team().id().find(bg_id)
}

fn put_team(c: &Ctx, t: &BgTeam) {
    if c.db().bg_team().id().find(t.id).is_some() {
        c.db().bg_team().id().update(t.clone());
    } else {
        c.db().bg_team().insert(t.clone());
    }
}

fn queue(c: &Ctx, queue_id: i32) -> Option<BgQueue> {
    c.db().bg_queue().queue_id().find(queue_id)
}

fn put_queue(c: &Ctx, q: &BgQueue) {
    if c.db().bg_queue().queue_id().find(q.queue_id).is_some() {
        c.db().bg_queue().queue_id().update(q.clone());
    }
}

/// bg_queues in vector order (creation order; queue ids are handed out in that order).
fn queues(c: &Ctx) -> Vec<BgQueue> {
    let mut v: Vec<BgQueue> = c.db().bg_queue().iter().collect();
    v.sort_by_key(|q| q.queue_id);
    v
}

fn state(c: &Ctx) -> BgState {
    c.db().bg_state().key().find(0).unwrap_or(BgState { key: 0, queue_count: 1, xy_timer: 0 })
}

fn put_state(c: &Ctx, s: BgState) {
    if c.db().bg_state().key().find(0).is_some() {
        c.db().bg_state().key().update(s);
    } else {
        c.db().bg_state().insert(s);
    }
}

fn reserve_key(type_id: i32, idx: u32) -> u64 {
    ((type_id as u32 as u64) << 16) | idx as u64
}

/// s_battleground_map::isReserved.
fn is_reserved(c: &Ctx, type_id: i32, idx: u32) -> bool {
    c.db().bg_map_reserve().key().find(reserve_key(type_id, idx)).is_some()
}

fn set_reserved(c: &Ctx, type_id: i32, idx: u32, on: bool) {
    let key = reserve_key(type_id, idx);
    let present = c.db().bg_map_reserve().key().find(key).is_some();
    if on && !present {
        c.db().bg_map_reserve().insert(BgMapReserve { key });
    } else if !on && present {
        c.db().bg_map_reserve().key().delete(key);
    }
}

/// A location's map (mapindex_name2id of its `Map`; the loader dropped locations of unknown maps).
fn loc_map(bg: &BattlegroundType, idx: u32) -> Option<u16> {
    bg.maps.get(idx as usize).and_then(|m| crate::data::maps().id_of(&m.map_name))
}

/// mapindex_id2name of a location's map.
fn loc_map_name(bg: &BattlegroundType, idx: u32) -> String {
    bg.maps.get(idx as usize).map(|m| m.map_name.clone()).unwrap_or_default()
}

/// sd->mapindex.
fn sd_mapindex(c: &Ctx, aid: u32) -> Option<u16> {
    crate::pc::get(c, aid).and_then(|p| p.mapindex)
}

/// mapreg_readreg(add_str(name)) for a `$@` number.
pub fn mapreg_readreg(c: &Ctx, name: &str) -> i64 {
    let key = name.to_ascii_lowercase();
    let v = c.db().mapreg_num().by_key().filter((&0u32, &key, &0u32)).next().map(|r| r.value).unwrap_or(0);
    v
}

// ---------------------------------------------------------------------------------------------- lookups

/// battleground.cpp bg_search_queue.
pub fn bg_search_queue(c: &Ctx, queue_id: i32) -> Option<BgQueue> {
    queues(c).into_iter().find(|q| q.queue_id == queue_id)
}

/// battleground.cpp bg_getavailablesd: the first member (every member here is a player).
pub fn bg_getavailablesd(t: &BgTeam) -> Option<u32> {
    t.members.first().map(|m| m.aid)
}

/// battleground.cpp bg_team_get_id: an object's battleground team.
pub fn bg_team_get_id(c: &Ctx, bl: &unit::Unit) -> i32 {
    match bl.kind {
        crate::map::BL_PC => bg_id_of(c, bl.id),
        crate::map::BL_MOB => {
            let Some(md) = crate::mob::get(c, bl.id) else { return 0 };
            if md.special_ai != 0 && md.master_id != 0 {
                if let Some(mbl) = unit::get(c, md.master_id) {
                    return bg_team_get_id(c, &mbl);
                }
            }
            md.bg_id as i32
        }
        // pd->master != nullptr: the master's bg_id
        crate::map::BL_PET => crate::pet::data::get(c, bl.id).filter(|pd| crate::pc::get(c, pd.master).is_some()).map(|pd| bg_id_of(c, pd.master)).unwrap_or(0),
        // su->group->bg_id (skillunit)
        crate::map::BL_SKILL => crate::skillunit::bg_id(c, bl),
        // BL_HOM: hd->master->bg_id (battleground.cpp:706-709; homunculus/data.rs)
        crate::map::BL_HOM => crate::homunculus::data::get(c, bl.id).and_then(|hd| crate::homunculus::data::master_sd(c, &hd)).map(|m| bg_id_of(c, m)).unwrap_or(0),
        // BL_MER: md->master != nullptr: the master's bg_id (battleground.cpp:710-713; mercenary/data.rs)
        crate::map::BL_MER => crate::mercenary::data::get(c, bl.id).filter(|md| crate::pc::get(c, md.master).is_some()).map(|md| bg_id_of(c, md.master)).unwrap_or(0),
        _ => 0,
    }
}

// ---------------------------------------------------------------------------------------------- teams

/// battleground.cpp bg_team_delete.
pub fn bg_team_delete(c: &Ctx, bg_id: i32) -> bool {
    let Some(t) = team(c, bg_id) else { return false };
    for m in &t.members {
        bg_send_dot_remove(c, m.aid);
        set_bg_id(c, m.aid, 0);
    }
    c.db().bg_team().id().delete(bg_id);
    true
}

/// battleground.cpp bg_team_warp.
pub fn bg_team_warp(c: &Ctx, bg_id: i32, m: u16, x: i16, y: i16) -> bool {
    let Some(t) = team(c, bg_id) else { return false };
    for mem in &t.members {
        crate::pc::setpos(c, mem.aid, m, x, y, CLR_TELEPORT);
    }
    true
}

/// battleground.cpp bg_send_dot_remove.
pub fn bg_send_dot_remove(c: &Ctx, aid: u32) {
    if bg_id_of(c, aid) != 0 {
        crate::clif_bg::clif_bg_xy_remove(c, aid);
    }
}

/// battleground.cpp bg_team_join.
pub fn bg_team_join(c: &Ctx, bg_id: i32, aid: u32, is_queue: bool) -> bool {
    if crate::pc::get(c, aid).is_none() || bg_id_of(c, aid) != 0 {
        return false;
    }
    let Some(mut t) = team(c, bg_id) else { return false };
    if t.members.len() == MAX_BG_MEMBERS as usize {
        return false; // No free slots
    }
    let Some(sd) = unit::get(c, aid) else { return false };
    let mut member = BgMember { aid, x: sd.x, y: sd.y, ..Default::default() };
    set_bg_id(c, aid, bg_id);
    if is_queue {
        // Save the location from where the person entered the battleground
        member.entry_map = sd_mapindex(c, aid);
        member.entry_x = sd.x;
        member.entry_y = sd.y;
    }
    t.members.push(member);
    put_team(c, &t);
    crate::guild::guild_send_dot_remove(c, &sd);
    for pl in &t.members {
        if pl.aid != aid {
            let (hp, max_hp) = crate::status::status_data_of(c, pl.aid).map(|s| (s.hp, s.max_hp)).unwrap_or((0, 0));
            crate::status::clif::hpmeter_single(c, &sd, pl.aid, hp, max_hp);
        }
    }
    crate::clif_bg::clif_bg_hp(c, aid);
    crate::clif_bg::clif_bg_xy(c, aid);
    true
}

/// battleground.cpp bg_team_leave: `quit` when the client closed, `deserter` applies the deserter status. Returns the
/// members left, or -1.
pub fn bg_team_leave(c: &Ctx, aid: u32, quit: bool, deserter: bool) -> i32 {
    if crate::pc::get(c, aid).is_none() {
        return -1;
    }
    let bg_id = bg_id_of(c, aid);
    if bg_id == 0 {
        return -1;
    }
    bg_send_dot_remove(c, aid);
    let bgteam = team(c, bg_id);
    set_bg_id(c, aid, 0);
    let Some(_) = bgteam else { return -1 };
    // Warping members out only applies to the Battleground Queue System
    if feature_bgqueue() {
        let found = team(c, bg_id).and_then(|t| t.members.iter().find(|m| m.aid == aid).cloned());
        if let Some(member) = found {
            let entry_ok = member.entry_map.map(|m| !crate::npc::mapflag(c, m, crate::npc::MF_NOSAVE)).unwrap_or(false);
            if entry_ok {
                if let Some(m) = member.entry_map {
                    crate::pc::setpos(c, aid, m, member.entry_x, member.entry_y, CLR_TELEPORT);
                }
            } else if let Some(ch) = crate::pc::character_of(c, aid) {
                // Warp to save point if the entry map has no save flag. (mapindex_name2id of an unknown map is 0:
                // pc_setpos refuses it)
                if let Some(m) = crate::data::maps().id_of(&ch.save_map) {
                    crate::pc::setpos(c, aid, m, ch.save_x as i16, ch.save_y as i16, CLR_TELEPORT);
                }
            }
            // members.erase(member): the first entry of this player (the warp above cannot have changed the list)
            if let Some(mut t) = team(c, bg_id) {
                if let Some(i) = t.members.iter().position(|m| m.aid == aid) {
                    t.members.remove(i);
                    put_team(c, &t);
                }
            }
        }
    }
    let name = crate::pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
    let output = if quit { format!("Server: {name} has quit the game...") } else { format!("Server: {name} is leaving the battlefield...") };
    let out = crate::net::wire::latin1(&output);
    let Some(t) = team(c, bg_id) else { return -1 };
    crate::clif_bg::clif_bg_message(c, &t, 0, "Server", &out, out.len() + 1);
    if !t.logout_event.is_empty() && quit {
        crate::script::npc_run::npc_event(c, aid, &t.logout_event, 0);
    }
    if deserter {
        // quirk kept: battleground_db.find(bg_id) looks the TEAM id up in the battleground database
        if let Some(bg) = battleground_db().find(bg_id) {
            if let Some(u) = unit::get(c, aid) {
                // Deserter timer
                crate::status::sc::sc_start(c, None, &u, crate::status::consts::SC_ENTRY_QUEUE_NOTIFY_ADMISSION_TIME_OUT, 100, 1, bg.deserter_time as i64 * 1000);
            }
        }
    }
    team(c, bg_id).map(|t| t.members.len() as i32).unwrap_or(-1)
}

/// battleground.cpp bg_member_respawn: a dead member returns to its team's cemetery.
pub fn bg_member_respawn(c: &Ctx, aid: u32) -> bool {
    let bg_id = bg_id_of(c, aid);
    if bg_id == 0 || !crate::pc::isdead(c, aid) {
        return false;
    }
    let Some(t) = team(c, bg_id) else { return false };
    let Some(m) = t.cemetery_map else { return false }; // Respawn not handled by Core
    crate::pc::setpos(c, aid, m, t.cemetery_x, t.cemetery_y, CLR_OUTSIGHT);
    crate::status::hp::status_revive(c, aid, 1, 100, 0);
    true // Warped
}

/// battleground.cpp bg_create: a new team with the smallest free id (from 1). `m` None is mapindex 0 ("-").
pub fn bg_create(c: &Ctx, m: Option<u16>, team_def: &BattlegroundTeam) -> i32 {
    let mut id = 1;
    while team(c, id).is_some() {
        id += 1;
    }
    let t = BgTeam {
        id,
        members: Vec::new(),
        cemetery_map: m,
        cemetery_x: team_def.warp_x as i16,
        cemetery_y: team_def.warp_y as i16,
        logout_event: team_def.quit_event.clone(),
        die_event: team_def.death_event.clone(),
        active_event: team_def.active_event.clone(),
    };
    put_team(c, &t);
    ensure_xy_timer(c);
    id
}

/// battleground.cpp bg_send_message: a player's battleground chat (`mes` without its NUL, `len` rAthena's length).
pub fn bg_send_message(c: &Ctx, aid: u32, mes: &[u8], len: usize) {
    let bg_id = bg_id_of(c, aid);
    if bg_id == 0 {
        return;
    }
    let Some(t) = team(c, bg_id) else { return };
    let name = crate::pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
    crate::clif_bg::clif_bg_message(c, &t, aid, &name, mes, len);
}

/// battleground.cpp bg_send_xy_timer (+ bg_send_xy_timer_sub): each member's minimap dot when it moved. bg_team_db is an
/// unordered_map; its order does not show (a team's packets go to that team only).
pub fn bg_send_xy_timer(c: &Ctx) {
    let mut teams: Vec<BgTeam> = c.db().bg_team().iter().collect();
    teams.sort_by_key(|t| t.id);
    for t in teams {
        let mut t = t;
        let mut changed = false;
        for i in 0..t.members.len() {
            let aid = t.members[i].aid;
            let Some(sd) = unit::get(c, aid) else { continue };
            if sd.x != t.members[i].x || sd.y != t.members[i].y {
                // xy update
                t.members[i].x = sd.x;
                t.members[i].y = sd.y;
                changed = true;
                crate::clif_bg::clif_bg_xy(c, aid);
            }
        }
        if changed {
            // the packets above read nothing of the member list but the members' ids
            if let Some(mut cur) = team(c, t.id) {
                for m in cur.members.iter_mut() {
                    if let Some(n) = t.members.iter().find(|n| n.aid == m.aid) {
                        m.x = n.x;
                        m.y = n.y;
                    }
                }
                put_team(c, &cur);
            }
        }
    }
}

/// do_init_battleground's add_timer_interval(bg_send_xy_timer, bg_update_interval). Armed with the world (a restart in
/// rAthena terms) and again if its row is ever gone.
fn ensure_xy_timer(c: &Ctx) {
    let mut s = state(c);
    if s.xy_timer != 0 && timer::get(c, s.xy_timer).is_some() {
        return;
    }
    s.xy_timer = timer::add_interval(c, c.tick() + bg_update_interval(), Func::BgSendXyTimer, 0, 0, bg_update_interval());
    put_state(c, s);
}

// ---------------------------------------------------------------------------------------------- queue timers

/// battleground.cpp bg_on_ready_loopback: every location was reserved; try again.
pub fn bg_on_ready_loopback(c: &Ctx, data: i64) {
    let queue_id = data as i32;
    let Some(q) = bg_search_queue(c, queue_id) else {
        log::error!("bg_on_ready_loopback: Invalid battleground queue {queue_id}.");
        return;
    };
    match battleground_db().find(q.id) {
        Some(bg) => {
            bg_queue_on_ready(c, &bg.name, queue_id);
        }
        None => log::error!("bg_on_ready_loopback: Can't find battleground {} in the battlegrounds database.", q.id),
    }
}

/// battleground.cpp bg_on_ready_expire: not everyone accepted in time.
pub fn bg_on_ready_expire(c: &Ctx, data: i64) {
    let queue_id = data as i32;
    let Some(q) = bg_search_queue(c, queue_id) else {
        log::error!("bg_on_ready_expire: Invalid battleground queue {queue_id}.");
        return;
    };
    let bg_name = battleground_db().find(q.id).map(|b| b.name.clone()).unwrap_or_default();
    for &aid in &q.teama {
        crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_QUEUE_FINISHED, &bg_name, aid);
        crate::clif_bg::clif_bg_queue_entry_init(c, aid);
    }
    for &aid in &q.teamb {
        crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_QUEUE_FINISHED, &bg_name, aid);
        crate::clif_bg::clif_bg_queue_entry_init(c, aid);
    }
    bg_queue_clear(c, queue_id, true);
}

/// battleground.cpp bg_on_ready_start: everyone accepted and the start delay has passed.
pub fn bg_on_ready_start(c: &Ctx, data: i64) {
    let queue_id = data as i32;
    let Some(mut q) = bg_search_queue(c, queue_id) else {
        log::error!("bg_on_ready_start: Invalid battleground queue {queue_id}.");
        return;
    };
    q.tid_start = 0;
    put_queue(c, &q);
    bg_queue_start_battleground(c, queue_id);
}

// ---------------------------------------------------------------------------------------------- queue checks

/// battleground.cpp bg_player_is_in_bg_map: the player stands on a map of any battleground location.
pub fn bg_player_is_in_bg_map(c: &Ctx, aid: u32) -> bool {
    let Some(mi) = sd_mapindex(c, aid) else { return false };
    battleground_db().iter().any(|bg| (0..bg.maps.len() as u32).any(|i| loc_map(bg, i) == Some(mi)))
}

/// battleground.cpp bg_queue_check_status: the queue-leave and deserter statuses keep a player out.
fn bg_queue_check_status(c: &Ctx, aid: u32, name: &str) -> bool {
    use crate::status::consts::{SC_ENTRY_QUEUE_APPLY_DELAY, SC_ENTRY_QUEUE_NOTIFY_ADMISSION_TIME_OUT};
    let sc = crate::status::sc::load(c, aid);
    if !sc.empty() {
        let remaining = |t: i32| -> i64 { sc.get(t).and_then(|e| timer::due(c, e.timer)).map(|due| due - c.tick()).unwrap_or(0) };
        if sc.has(SC_ENTRY_QUEUE_APPLY_DELAY) {
            // Exclude any player who's recently left a battleground queue
            let secs = (remaining(SC_ENTRY_QUEUE_APPLY_DELAY) / 1000) as i32;
            // You can't apply to a battleground queue for %d seconds due to recently leaving one.
            let buf = crate::data::msg_txt(339).replacen("%d", &secs.to_string(), 1);
            crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_NONE, name, aid);
            messagecolor_self(c, aid, &buf);
            return false;
        }
        if sc.has(SC_ENTRY_QUEUE_NOTIFY_ADMISSION_TIME_OUT) {
            // Exclude any player who's recently deserted a battleground
            let status_tick = (remaining(SC_ENTRY_QUEUE_NOTIFY_ADMISSION_TIME_OUT) / 1000) as i32;
            // You can't apply to a battleground queue due to recently deserting a battleground. Time remaining: %d minutes
            // and %d seconds.
            let buf = crate::data::msg_txt(338).replacen("%d", &(status_tick / 60).to_string(), 1).replacen("%d", &(status_tick % 60).to_string(), 1);
            crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_NONE, name, aid);
            messagecolor_self(c, aid, &buf);
            return false;
        }
    }
    true
}

/// clif_messagecolor(sd, color_table[COLOR_LIGHT_GREEN], msg, false, SELF).
fn messagecolor_self(c: &Ctx, aid: u32, msg: &str) {
    if let Some(sd) = unit::get(c, aid) {
        crate::clif::messagecolor_target(c, &sd, crate::social::COLOR_LIGHT_GREEN, &crate::net::wire::latin1(msg), false, crate::clif::Target::SelfOnly, None);
    }
}

/// battleground.cpp bg_queue_check_joinable.
pub fn bg_queue_check_joinable(c: &Ctx, bg: &BattlegroundType, aid: u32, name: &str) -> bool {
    let Some(ch) = crate::pc::character_of(c, aid) else { return false };
    for &job in &bg.job_restrictions {
        // Check class requirement
        if ch.class as i32 == job {
            crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_PLAYER_CLASS, name, aid);
            return false;
        }
    }
    if bg.min_lvl > 0 && (ch.base_level as i32) < bg.min_lvl {
        // Check minimum level requirement
        crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_PLAYER_LEVEL, name, aid);
        return false;
    }
    if bg.max_lvl > 0 && ch.base_level as i32 > bg.max_lvl {
        // Check maximum level requirement
        crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_PLAYER_LEVEL, name, aid);
        return false;
    }
    if !bg_queue_check_status(c, aid, name) {
        // Check status blocks
        return false;
    }
    if bg_player_is_in_bg_map(c, aid) {
        // Is the player currently in a battleground map? Reject them.
        crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_NONE, name, aid);
        messagecolor_self(c, aid, crate::data::msg_txt(337)); // You can't apply to a battleground queue from this map.
        return false;
    }
    if bgqueue_nowarp_mapflag() > 0 && on_nowarp_map(c, aid) {
        // Check player's current position for mapflag check
        crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_NONE, name, aid);
        messagecolor_self(c, aid, crate::data::msg_txt(337)); // You can't apply to a battleground queue from this map.
        return false;
    }
    true
}

/// map_getmapflag(sd->m, MF_NOWARP).
fn on_nowarp_map(c: &Ctx, aid: u32) -> bool {
    unit::get(c, aid).map(|u| crate::npc::mapflag(c, u.map, crate::npc::MF_NOWARP)).unwrap_or(false)
}

// ---------------------------------------------------------------------------------------------- reservations

/// battleground.cpp bg_queue_reservation (bg_queue_reserve / bg_queue_unbook): marks a battleground map reserved or
/// free; `ended` puts its queue in QUEUE_STATE_ENDED, freeing it clears the queue for the next one.
pub fn bg_queue_reservation(c: &Ctx, name: &str, reserve: bool, ended: bool) -> bool {
    // mapindex_name2id: an unknown name is 0, which no location has
    let Some(mi) = crate::data::maps().id_of(name) else { return false };
    for bg in battleground_db().iter() {
        for idx in 0..bg.maps.len() as u32 {
            if loc_map(bg, idx) != Some(mi) {
                continue;
            }
            set_reserved(c, bg.id, idx, reserve);
            for q in queues(c) {
                if q.id == bg.id && q.map == Some(idx) {
                    if ended {
                        // The ended flag is applied from bg_reserve (bg_unbook clears it for the next queue)
                        if let Some(mut q2) = queue(c, q.queue_id) {
                            q2.state = QUEUE_STATE_ENDED;
                            put_queue(c, &q2);
                        }
                    }
                    if !reserve {
                        bg_queue_clear(c, q.queue_id, true);
                    }
                }
            }
            return true;
        }
    }
    false
}

/// bg_queue_reserve(name, end).
pub fn bg_queue_reserve(c: &Ctx, name: &str, ended: bool) -> bool {
    bg_queue_reservation(c, name, true, ended)
}

/// bg_queue_unbook(name).
pub fn bg_queue_unbook(c: &Ctx, name: &str) -> bool {
    bg_queue_reservation(c, name, false, false)
}

// ---------------------------------------------------------------------------------------------- joining a queue

/// battleground.cpp bg_queue_join_solo.
pub fn bg_queue_join_solo(c: &Ctx, name: &str, aid: u32) {
    let Some(bg) = bg_search_name(name) else {
        let (n, cid) = crate::pc::character_of(c, aid).map(|ch| (ch.name, ch.id)).unwrap_or_default();
        log::warn!("bq_queue_join_solo: Could not find battleground \"{name}\" requested by {n} (AID: {aid} / CID: {cid})");
        return;
    };
    if !bg.solo {
        crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_INVALID_APP, name, aid);
        return;
    }
    bg_queue_join_multi(c, name, aid, vec![aid]); // Join as solo
}

/// battleground.cpp bg_queue_join_party.
pub fn bg_queue_join_party(c: &Ctx, name: &str, aid: u32) {
    let Some(ch) = crate::pc::character_of(c, aid) else { return };
    let Some(p) = crate::party::party_search(c, ch.party_id) else {
        crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_INVALID_APP, name, aid);
        return; // Someone has bypassed the client check for being in a party
    };
    for m in &p.party.member {
        if m.leader && ch.id != m.char_id {
            crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_PARTYGUILD_LEADER, name, aid);
            return; // Not the party leader
        }
    }
    let Some(bg) = bg_search_name(name) else {
        log::warn!("clif_parse_bg_queue_apply_request: Could not find Battleground: \"{name}\" requested by player: {} (AID:{aid} CID:{})", ch.name, ch.id);
        crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_INVALID_NAME, name, aid);
        return; // Invalid BG name
    };
    if !bg.party {
        crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_INVALID_APP, name, aid);
        return;
    }
    let p_online = p.party.member.iter().filter(|m| m.online).count() as i32;
    if p_online > bg.max_players {
        crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_PLAYER_COUNT, name, aid);
        return; // Too many party members online
    }
    let mut list = Vec::new();
    for m in &p.party.member {
        if list.len() == bg.max_players as usize {
            break;
        }
        if m.online {
            if let Some(pl) = crate::pc::charid2sd(c, m.char_id) {
                list.push(pl.id);
            }
        }
    }
    bg_queue_join_multi(c, name, aid, list); // Join as party, all on the same side of the BG
}

/// battleground.cpp bg_queue_join_guild.
pub fn bg_queue_join_guild(c: &Ctx, name: &str, aid: u32) {
    let Some(g) = crate::guild::sd_guild(c, aid) else {
        crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_INVALID_APP, name, aid);
        return; // Someone has bypassed the client check for being in a guild
    };
    let Some(ch) = crate::pc::character_of(c, aid) else { return };
    if ch.name != g.guild.master {
        crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_PARTYGUILD_LEADER, name, aid);
        return; // Not the guild leader
    }
    let Some(bg) = bg_search_name(name) else {
        log::warn!("clif_parse_bg_queue_apply_request: Could not find Battleground: \"{name}\" requested by player: {} (AID:{aid} CID:{})", ch.name, ch.id);
        crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_INVALID_NAME, name, aid);
        return; // Invalid BG name
    };
    if !bg.guild {
        crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_INVALID_APP, name, aid);
        return;
    }
    if g.guild.connect_member as i32 > bg.max_players {
        crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_PLAYER_COUNT, name, aid);
        return; // Too many guild members online
    }
    let mut list = Vec::new();
    for m in &g.guild.member {
        if list.len() == bg.max_players as usize {
            break;
        }
        if m.online != 0 {
            if let Some(pl) = crate::pc::charid2sd(c, m.char_id) {
                list.push(pl.id);
            }
        }
    }
    bg_queue_join_multi(c, name, aid, list); // Join as guild, all on the same side of the BG
}

/// battleground.cpp bg_queue_join_multi: `list` (every player, including the one who asked) joins one side.
pub fn bg_queue_join_multi(c: &Ctx, name: &str, aid: u32, mut list: Vec<u32>) {
    let Some(bg) = bg_search_name(name) else {
        let (n, cid) = crate::pc::character_of(c, aid).map(|ch| (ch.name, ch.id)).unwrap_or_default();
        log::warn!("bq_queue_join_multi: Could not find battleground \"{name}\" requested by {n} (AID: {aid} / CID: {cid})");
        return;
    };
    if !bg_queue_check_joinable(c, bg, aid, name) {
        return;
    }
    for q0 in queues(c) {
        let Some(mut q) = queue(c, q0.queue_id) else { continue };
        if q.id != bg.id || q.state == QUEUE_STATE_SETUP_DELAY || q.state == QUEUE_STATE_ENDED {
            continue;
        }
        let max = bg.max_players as usize;
        // Make sure there's enough space on one side to join as a party/guild in this queue
        if q.teama.len() + list.len() > max && q.teamb.len() + list.len() > max {
            break;
        }
        let r = c.rnd_chance(50, 100);
        let mut team_b = r;
        if q.state == QUEUE_STATE_ACTIVE {
            // If one team has lesser members try to balance (on an active BG)
            if r && q.teama.len() < q.teamb.len() {
                team_b = false;
            } else if !r && q.teamb.len() < q.teama.len() {
                team_b = true;
            }
        } else {
            // If the designated team is full, put the player into the other team
            let size = if team_b { q.teamb.len() } else { q.teama.len() };
            if size + list.len() > bg.required_players as usize {
                team_b = !r;
            }
        }
        loop {
            let size = if team_b { q.teamb.len() } else { q.teama.len() };
            if list.is_empty() || size >= max {
                break;
            }
            let Some(sd2) = list.pop() else { break };
            if crate::pc::get(c, sd2).is_none() || bg_queue_id_of(c, sd2) > 0 {
                continue;
            }
            if !bg_queue_check_joinable(c, bg, sd2, name) {
                continue;
            }
            set_bg_queue_id(c, sd2, q.queue_id);
            if team_b {
                q.teamb.push(sd2);
            } else {
                q.teama.push(sd2);
            }
            put_queue(c, &q);
            crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_ACCEPT, name, sd2);
            crate::clif_bg::clif_bg_queue_apply_notify(c, name, sd2);
            q = queue(c, q.queue_id).unwrap_or(q);
        }
        if q.state == QUEUE_STATE_ACTIVE {
            // Battleground is already active
            let members = if team_b { q.teamb.clone() } else { q.teama.clone() };
            let qmap = q.map.and_then(|i| loc_map(bg, i));
            for pl in members {
                if qmap.is_some() && qmap == sd_mapindex(c, pl) {
                    continue;
                }
                pc_set_bg_queue_timer(c, pl);
                crate::clif_bg::clif_bg_queue_lobby_notify(c, name, pl);
            }
        } else if q.state == QUEUE_STATE_SETUP && q.teamb.len() >= bg.required_players as usize && q.teama.len() >= bg.required_players as usize {
            // Enough players have joined
            bg_queue_on_ready(c, name, q.queue_id);
        }
        return;
    }
    // Something went wrong, sends reconnect and then reapply message to client.
    crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_RECONNECT, name, aid);
}

/// battleground.cpp bg_queue_clear: stops the queue's timers; `ended` also frees its map and empties it for the next
/// queue.
pub fn bg_queue_clear(c: &Ctx, queue_id: i32, ended: bool) {
    let Some(mut q) = queue(c, queue_id) else { return };
    for tid in [&mut q.tid_requeue, &mut q.tid_expire, &mut q.tid_start] {
        if *tid != 0 {
            timer::delete(c, *tid);
            *tid = 0;
        }
    }
    if ended {
        if let Some(idx) = q.map.take() {
            set_reserved(c, q.id, idx, false); // Remove reservation to free up for future queue
        }
        for &aid in q.teama.iter().chain(q.teamb.iter()) {
            set_bg_queue_id(c, aid, 0);
        }
        q.teama.clear();
        q.teamb.clear();
        q.accepted_players = 0;
        q.state = QUEUE_STATE_SETUP;
    }
    put_queue(c, &q);
}

/// battleground.cpp bg_queue_leave_sub: the player leaves one side's list.
fn bg_queue_leave_sub(c: &Ctx, aid: u32, queue_id: i32, team_b: bool, apply_sc: bool) -> bool {
    let Some(mut q) = queue(c, queue_id) else { return false };
    let members = if team_b { &mut q.teamb } else { &mut q.teama };
    let Some(i) = members.iter().position(|&m| m == aid) else { return false };
    members.remove(i);
    put_queue(c, &q);
    if apply_sc {
        if let Some(u) = unit::get(c, aid) {
            crate::status::sc::sc_start(c, None, &u, crate::status::consts::SC_ENTRY_QUEUE_APPLY_DELAY, 100, 1, 60000);
        }
    }
    set_bg_queue_id(c, aid, 0);
    pc_delete_bg_queue_timer(c, aid);
    true
}

/// battleground.cpp bg_queue_leave (`apply_sc`: SC_ENTRY_QUEUE_APPLY_DELAY on leaving, default true).
pub fn bg_queue_leave(c: &Ctx, aid: u32, apply_sc: bool) -> bool {
    let qid = bg_queue_id_of(c, aid);
    if crate::pc::get(c, aid).is_none() || qid == 0 {
        return false;
    }
    pc_delete_bg_queue_timer(c, aid);
    for q in queues(c) {
        if qid != q.queue_id {
            continue;
        }
        if !bg_queue_leave_sub(c, aid, q.queue_id, false, apply_sc) && !bg_queue_leave_sub(c, aid, q.queue_id, true, apply_sc) {
            let n = crate::pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
            log::error!("bg_queue_leave: Couldn't find player {n} in battlegrounds queue.");
            return false;
        }
        let Some(q) = queue(c, q.queue_id) else { return true };
        if (q.state == QUEUE_STATE_SETUP || q.state == QUEUE_STATE_SETUP_DELAY) && q.teama.is_empty() && q.teamb.is_empty() {
            // If there are no players left in the queue (that hasn't started), discard it
            bg_queue_clear(c, q.queue_id, true);
        }
        return true;
    }
    false
}

/// battleground.cpp bg_queue_on_ready: both sides are full; reserve a location and ask everyone to enter.
pub fn bg_queue_on_ready(c: &Ctx, name: &str, queue_id: i32) -> bool {
    let Some(mut q) = queue(c, queue_id) else { return false };
    let Some(bg) = battleground_db().find(q.id) else {
        log::error!("bg_queue_on_ready: Couldn't find battleground ID {} in battlegrounds database.", q.id);
        return false;
    };
    if (q.teama.len() as i32) < q.required_players || (q.teamb.len() as i32) < q.required_players {
        return false; // Return players to the queue and stop reapplying the timer
    }
    let mut map_reserved = false;
    for idx in 0..bg.maps.len() as u32 {
        if !is_reserved(c, bg.id, idx) {
            set_reserved(c, bg.id, idx, true);
            map_reserved = true;
            q.map = Some(idx);
            break;
        }
    }
    if !map_reserved {
        // All the battleground maps are reserved. Set a timer to check for an open battleground every 10 seconds.
        q.tid_requeue = timer::add(c, c.tick() + 10000, Func::BgOnReadyLoopback, 0, queue_id as i64);
        put_queue(c, &q);
        return false;
    }
    q.state = QUEUE_STATE_SETUP_DELAY;
    q.tid_expire = timer::add(c, c.tick() + 20000, Func::BgOnReadyExpire, 0, queue_id as i64);
    put_queue(c, &q);
    for &aid in &q.teama {
        crate::clif_bg::clif_bg_queue_lobby_notify(c, name, aid);
    }
    for &aid in &q.teamb {
        crate::clif_bg::clif_bg_queue_lobby_notify(c, name, aid);
    }
    true
}

/// battleground.cpp bg_join_active: a player who accepted joins a battleground that is already running.
fn bg_join_active(c: &Ctx, aid: u32, queue_id: i32) {
    let Some(q) = queue(c, queue_id) else { return };
    // Check player's current position for mapflag check
    if bgqueue_nowarp_mapflag() > 0 && on_nowarp_map(c, aid) {
        messagecolor_self(c, aid, crate::data::msg_txt(337)); // You can't apply to a battleground queue from this map.
        bg_queue_leave(c, aid, true);
        crate::clif_bg::clif_bg_queue_entry_init(c, aid);
        return;
    }
    pc_delete_bg_queue_timer(c, aid); // Cancel timer so player doesn't leave the queue.
    let Some(bg) = battleground_db().find(q.id) else { return };
    let Some(loc) = q.map.and_then(|i| bg.maps.get(i as usize)) else { return };
    let bg_id_team_1 = mapreg_readreg(c, &loc.team1.bg_id_var) as i32;
    let bgteam_1 = team(c, bg_id_team_1);
    if q.teama.contains(&aid) {
        let Some(t1) = bgteam_1 else {
            bg_queue_leave(c, aid, true);
            crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_RECONNECT, &bg.name, aid);
            crate::clif_bg::clif_bg_queue_entry_init(c, aid);
            return;
        };
        crate::clif_bg::clif_bg_queue_entry_init(c, aid);
        bg_team_join(c, bg_id_team_1, aid, true);
        crate::script::npc_run::npc_event(c, aid, &t1.active_event, 0);
        return;
    }
    let bg_id_team_2 = mapreg_readreg(c, &loc.team2.bg_id_var) as i32;
    let bgteam_2 = team(c, bg_id_team_2);
    if q.teamb.contains(&aid) {
        let Some(t2) = bgteam_2 else {
            bg_queue_leave(c, aid, true);
            crate::clif_bg::clif_bg_queue_apply_result(c, BG_APPLY_RECONNECT, &bg.name, aid);
            crate::clif_bg::clif_bg_queue_entry_init(c, aid);
            return;
        };
        crate::clif_bg::clif_bg_queue_entry_init(c, aid);
        bg_team_join(c, bg_id_team_2, aid, true);
        crate::script::npc_run::npc_event(c, aid, &t2.active_event, 0);
    }
}

/// battleground.cpp bg_mapflag_check: queue members on a nowarp map leave the queue (bgqueue_nowarp_mapflag only).
fn bg_mapflag_check(c: &Ctx, queue_id: i32) -> bool {
    if bgqueue_nowarp_mapflag() == 0 {
        return false;
    }
    let Some(q) = queue(c, queue_id) else { return false };
    let mut found = false;
    for aid in q.teama.iter().chain(q.teamb.iter()).copied().collect::<Vec<_>>() {
        if on_nowarp_map(c, aid) {
            messagecolor_self(c, aid, crate::data::msg_txt(337)); // You can't apply to a battleground queue from this map.
            bg_queue_leave(c, aid, true);
            crate::clif_bg::clif_bg_queue_entry_init(c, aid);
            found = true;
        }
    }
    if found {
        let Some(mut q) = queue(c, queue_id) else { return true };
        q.state = QUEUE_STATE_SETUP; // Set back to queueing state
        q.accepted_players = 0; // Reset acceptance count
        // Free map to avoid creating a reservation delay
        if let Some(idx) = q.map.take() {
            set_reserved(c, q.id, idx, false);
        }
        put_queue(c, &q);
        // Announce failure to remaining players
        for &aid in q.teama.iter().chain(q.teamb.iter()) {
            messagecolor_self(c, aid, crate::data::msg_txt(340)); // Participants were unable to join. Delaying entry for more participants.
        }
    }
    found
}

/// battleground.cpp bg_queue_on_accept_invite: the player accepted the entry window.
pub fn bg_queue_on_accept_invite(c: &Ctx, aid: u32) {
    let Some(mut q) = bg_search_queue(c, bg_queue_id_of(c, aid)) else {
        let n = crate::pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
        log::error!("bg_queue_on_accept_invite: Couldn't find player {n} in battlegrounds queue.");
        return;
    };
    q.accepted_players += 1;
    put_queue(c, &q);
    let Some(bg) = battleground_db().find(q.id) else { return };
    let Some(idx) = q.map else {
        // Deliberately different: rAthena dereferences queue->map, which is null before the queue is ready (a client that
        // accepts an entry window it was never shown crashes the map server there); here the request is dropped.
        log::error!("bg_queue_on_accept_invite: queue {} has no battleground map yet.", q.queue_id);
        return;
    };
    let mapname = loc_map_name(bg, idx);
    crate::clif_bg::clif_bg_queue_ack_lobby(c, true, &mapname, &mapname, aid);
    if q.state == QUEUE_STATE_ACTIVE {
        // Battleground is already active
        bg_join_active(c, aid, q.queue_id);
    } else if q.state == QUEUE_STATE_SETUP_DELAY && q.accepted_players == q.required_players * 2 {
        if q.tid_expire != 0 {
            timer::delete(c, q.tid_expire);
            q.tid_expire = 0;
            put_queue(c, &q);
        }
        // Check player's current position for mapflag check
        if bgqueue_nowarp_mapflag() > 0 && bg_mapflag_check(c, q.queue_id) {
            return;
        }
        let Some(mut q) = queue(c, q.queue_id) else { return };
        q.tid_start = timer::add(c, c.tick() + bg.start_delay as i64 * 1000, Func::BgOnReadyStart, 0, q.queue_id as i64);
        put_queue(c, &q);
    }
}

/// battleground.cpp bg_queue_start_battleground: the two teams are made, their members join, the location's start
/// event runs.
pub fn bg_queue_start_battleground(c: &Ctx, queue_id: i32) {
    let Some(q) = queue(c, queue_id) else { return };
    let Some(bg) = battleground_db().find(q.id) else {
        bg_queue_clear(c, queue_id, true);
        log::error!("bg_queue_start_battleground: Could not find battleground ID {} in battlegrounds database.", q.id);
        return;
    };
    // Check player's current position for mapflag check
    if bgqueue_nowarp_mapflag() > 0 && bg_mapflag_check(c, queue_id) {
        return;
    }
    let Some(idx) = q.map else { return };
    let Some(loc) = bg.maps.get(idx as usize) else { return };
    let map_idx = loc_map(bg, idx);
    let bg_team_1 = bg_create(c, map_idx, &loc.team1);
    let bg_team_2 = bg_create(c, map_idx, &loc.team2);
    for &aid in &q.teama {
        crate::clif_bg::clif_bg_queue_entry_init(c, aid);
        bg_team_join(c, bg_team_1, aid, true);
    }
    for &aid in &q.teamb {
        crate::clif_bg::clif_bg_queue_entry_init(c, aid);
        bg_team_join(c, bg_team_2, aid, true);
    }
    crate::party::mapreg_setreg(c, &loc.team1.bg_id_var, 0, bg_team_1 as i64);
    crate::party::mapreg_setreg(c, &loc.team2.bg_id_var, 0, bg_team_2 as i64);
    crate::script::npc_run::npc_event_do(c, &loc.bgcallscript);
    // (the start event may have changed the queue: read it again)
    if let Some(mut q) = queue(c, queue_id) {
        q.state = QUEUE_STATE_ACTIVE;
        put_queue(c, &q);
    }
    bg_queue_clear(c, queue_id, false);
}

/// battleground.cpp bg_queue_create.
fn bg_queue_create(c: &Ctx, bg_id: i32, req_players: i32) {
    let mut s = state(c);
    let queue_id = s.queue_count;
    s.queue_count += 1;
    put_state(c, s);
    c.db().bg_queue().insert(BgQueue { queue_id, id: bg_id, required_players: req_players, state: QUEUE_STATE_SETUP, ..Default::default() });
}

/// battleground.cpp do_init_battleground, run when the world is (re)built (a server start in rAthena terms: the map
/// server starts with no team, queue or reservation; do_init_battleground runs before do_init_npc's OnInit events).
/// The queues are created in the database's iteration order (an unordered_map in rAthena: the order only decides the
/// queue ids, which never reach a client).
pub fn do_init_battleground(c: &Ctx) {
    for aid in c.db().pc_bg().iter().map(|b| b.account_id).collect::<Vec<_>>() {
        c.db().pc_bg().account_id().delete(aid);
    }
    for id in c.db().bg_team().iter().map(|t| t.id).collect::<Vec<_>>() {
        c.db().bg_team().id().delete(id);
    }
    for q in c.db().bg_queue().iter().collect::<Vec<_>>() {
        for tid in [q.tid_expire, q.tid_start, q.tid_requeue] {
            if tid != 0 {
                timer::delete(c, tid);
            }
        }
        c.db().bg_queue().queue_id().delete(q.queue_id);
    }
    for k in c.db().bg_map_reserve().iter().map(|r| r.key).collect::<Vec<_>>() {
        c.db().bg_map_reserve().key().delete(k);
    }
    for m in c.db().bg_score().iter().map(|s| s.map).collect::<Vec<_>>() {
        c.db().bg_score().map().delete(m);
    }
    let old = state(c);
    if old.xy_timer != 0 {
        timer::delete(c, old.xy_timer);
    }
    put_state(c, BgState { key: 0, queue_count: 1, xy_timer: 0 });
    if feature_bgqueue() {
        for bg in battleground_db().iter() {
            bg_queue_create(c, bg.id, bg.required_players);
        }
    }
    ensure_xy_timer(c);
}

// ---------------------------------------------------------------------------------------------- pc.cpp queue timer

/// pc.cpp pc_on_expire_active: the player did not enter an active battleground in time.
pub fn pc_on_expire_active(c: &Ctx, tid: u64, aid: u32) {
    let mut b = pcbg(c, aid);
    if crate::pc::get(c, aid).is_none() || b.tid_queue_active != tid {
        return;
    }
    b.tid_queue_active = 0;
    put_pcbg(c, &b);
    bg_queue_leave(c, aid, true);
    crate::clif_bg::clif_bg_queue_entry_init(c, aid);
}

/// pc.cpp pc_set_bg_queue_timer.
pub fn pc_set_bg_queue_timer(c: &Ctx, aid: u32) {
    let mut b = pcbg(c, aid);
    if b.tid_queue_active != 0 {
        timer::delete(c, b.tid_queue_active);
    }
    b.tid_queue_active = timer::add(c, c.tick() + 20000, Func::PcOnExpireActive, aid, 0);
    put_pcbg(c, &b);
}

/// pc.cpp pc_delete_bg_queue_timer.
pub fn pc_delete_bg_queue_timer(c: &Ctx, aid: u32) {
    let mut b = pcbg(c, aid);
    if b.tid_queue_active != 0 {
        timer::delete(c, b.tid_queue_active);
        b.tid_queue_active = 0;
        put_pcbg(c, &b);
    }
}

// ---------------------------------------------------------------------------------------------- mob.cpp

/// mob.cpp mob_spawn_bg: a monster of a battleground team (bg_monster). Returns its id, 0 on failure.
pub fn mob_spawn_bg(c: &Ctx, mapname: &str, x: i16, y: i16, mobname: &str, mob_id: i32, event: &str, bg_id: u32) -> u32 {
    let Some(m) = crate::data::maps().id_of(mapname) else {
        log::warn!("mob_spawn_bg: Map [{mapname}] not found.");
        return 0;
    };
    let mut mob_id = mob_id;
    if mob_id <= 0 {
        mob_id = crate::mob::spawn::get_random_id(c, -mob_id - 1, crate::db::mobs::RMF_DB_RATE, 0);
        if mob_id == 0 {
            return 0;
        }
    }
    let (mut x, mut y) = (x, y);
    if x <= 0 || y <= 0 {
        match crate::mob::spawn::map_search_freecell(c, None, m as i32, x, y, -1, -1, 1, 50) {
            Some((nx, ny)) => {
                x = nx;
                y = ny;
            }
            None => {
                log::warn!("mob_spawn_bg: Couldn't locate a spawn cell for guardian class {mob_id} (bg_id {bg_id}) at map {mapname}");
                return 0;
            }
        }
    }
    let mut data = crate::mob::spawn::SpawnArgs { m, num: 1, id: mob_id, x, y, ..Default::default() };
    // safestrncpy(data.name, mobname, NAME_LENGTH); safestrncpy(data.eventname, event, EVENT_NAME_LENGTH)
    let mut name: String = mobname.chars().take(crate::mob::NAME_LENGTH - 1).collect();
    let mut ev: String = event.chars().take(crate::script::npc_run::EVENT_NAME_LENGTH - 1).collect();
    if !crate::mob::spawn::mob_parse_dataset(c, data.id, data.num, &mut name, &mut ev) {
        return 0;
    }
    data.name = name;
    data.eventname = ev;
    let Some(md) = crate::mob::spawn::mob_spawn_dataset(c, &data, 0) else { return 0 };
    crate::mob::spawn::mob_spawn(c, md.id);
    if let Some(mut md2) = crate::mob::get(c, md.id) {
        md2.bg_id = bg_id; // BG Team ID
        crate::mob::put(c, &md2);
    }
    md.id
}

// ---------------------------------------------------------------------------------------------- hooks (other files)

/// pc.cpp pc_setpos, on a map change (pc.cpp:6980-6981): moving to a map that isn't a battleground takes the player out
/// of its team (a deserter). Returns true when that happened (bg_team_leave may have warped the player already).
pub fn pc_setpos_changemap(c: &Ctx, aid: u32, to: u16) -> bool {
    if bg_id_of(c, aid) != 0 && !crate::npc::mapflag(c, to, crate::npc::MF_BATTLEGROUND) {
        bg_team_leave(c, aid, false, true);
        return true;
    }
    false
}

/// map.cpp map_quit (map.cpp:2253-2257): the player leaves its team (quit, deserter) and its queue (without the delay
/// status).
pub fn map_quit(c: &Ctx, aid: u32) {
    if bg_id_of(c, aid) != 0 {
        bg_team_leave(c, aid, true, true);
    }
    if bg_queue_id_of(c, aid) > 0 {
        bg_queue_leave(c, aid, false);
    }
}

/// atcommand.cpp ACMD_FUNC(reloadscript), its second battleground loop (atcommand.cpp:4408-4414): every queue's members
/// leave their team and the queue is cleared. (Its first loop, per online player, calls bg_queue_leave(pl_sd) with the
/// apply delay among pc_close_npc and the cutin: an @reloadscript port calls [`bg_queue_leave`] there, so the players
/// online are no longer in these lists when this runs, as in rAthena.)
pub fn reloadscript_queues(c: &Ctx) {
    for q in queues(c) {
        for &aid in q.teama.iter().chain(q.teamb.iter()) {
            bg_team_leave(c, aid, false, false); // Kick Team A / Team B from battlegrounds
        }
        bg_queue_clear(c, q.queue_id, true);
    }
}

/// unit_free: the player's battleground fields go with its map_session_data.
pub fn unit_free(c: &Ctx, aid: u32) {
    c.db().pc_bg().account_id().delete(aid);
}

/// pc.cpp pc_dead (pc.cpp:10132-10141): a member of a team with a cemetery respawns through the respawn timer.
pub fn pc_dead_respawn(c: &Ctx, aid: u32) -> bool {
    let bg_id = bg_id_of(c, aid);
    if bg_id == 0 {
        return false;
    }
    team(c, bg_id).map(|t| t.cemetery_map.is_some()).unwrap_or(false) // Respawn by BG
}

/// status.cpp status_damage (status.cpp:1779-1784): a dying member runs its team's die event before OnPCDieEvent.
pub fn status_damage_die_event(c: &Ctx, aid: u32) {
    let bg_id = bg_id_of(c, aid);
    if bg_id == 0 {
        return;
    }
    if let Some(t) = team(c, bg_id) {
        if !t.die_event.is_empty() {
            crate::script::npc_run::npc_event(c, aid, &t.die_event, 0);
        }
    }
}

/// map_data::bgscore_lion / bgscore_eagle.
pub fn score(c: &Ctx, m: u16) -> (i16, i16) {
    c.db().bg_score().map().find(m).map(|s| (s.lion, s.eagle)).unwrap_or((0, 0))
}

/// Sets map_data::bgscore_lion / bgscore_eagle.
pub fn set_score(c: &Ctx, m: u16, lion: i16, eagle: i16) {
    let row = BgScore { map: m, lion, eagle };
    if c.db().bg_score().map().find(m).is_some() {
        c.db().bg_score().map().update(row);
    } else {
        c.db().bg_score().insert(row);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn config_matches_battle_flags() {
        let flag = |n: &str| crate::script::battle_flags::BATTLE_FLAGS.iter().find(|f| f.0 == n).map(|f| f.1);
        assert_eq!(flag("feature.bgqueue"), Some(feature_bgqueue() as i32));
        assert_eq!(flag("bgqueue_nowarp_mapflag"), Some(bgqueue_nowarp_mapflag()));
        assert_eq!(flag("bg_update_interval"), Some(bg_update_interval() as i32));
    }

    #[test]
    fn locations_resolve_to_maps() {
        for bg in battleground_db().iter() {
            for i in 0..bg.maps.len() as u32 {
                assert!(loc_map(bg, i).is_some(), "{} location {i}", bg.name);
            }
        }
    }
}
