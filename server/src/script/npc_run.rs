//! npc.cpp's script side: clicking an NPC (npc_click), continuing a dialog (npc_scriptcont and the clif_parse_Npc*
//! handlers), events (ev_db: npc_event, npc_event_do, npc_event_doall, npc_script_event, the event queue), OnTouch /
//! OnTouch_ areas, NPC timers (OnTimerXXX: npc_timerevent*), clock events (npc_event_do_clock), the secure NPC
//! timeout, OnInit / OnInterIfInit(Once) when the world is built, OnWhisperGlobal — plus script.cpp's attach /
//! detach of a state to its player and pc.cpp's pc_close_npc / event timers.

use spacetimedb::{table, SpacetimeType, Table};

use super::dbmap::StrDb;
use super::vm::*;
use super::{code, state};
use crate::clif;
use crate::npc::{npc, Npc, NPCTYPE_SCRIPT, NPCTYPE_TOMB, NPCTYPE_WARP};
use crate::pc;
use crate::timer::{self, Func};
use crate::unit::{self, OPTION_CHASEWALK, OPTION_CLOAK, OPTION_HIDE};
use crate::world::Ctx;

// ------------------------------------------------------------------------------------------------ configuration

/// script_config event names (script.cpp).
pub const ONTOUCH_EVENT_NAME: &str = "OnTouch_";
pub const ONTOUCH2_EVENT_NAME: &str = "OnTouch";
pub const ONTOUCHNPC_EVENT_NAME: &str = "OnTouchNPC";
pub const ONWHISPER_EVENT_NAME: &str = "OnWhisperGlobal";
pub const ONCOMMAND_EVENT_NAME: &str = "OnCommand";
pub const INIT_EVENT_NAME: &str = "OnInit";
pub const INTER_INIT_EVENT_NAME: &str = "OnInterIfInit";
pub const INTER_INIT_ONCE_EVENT_NAME: &str = "OnInterIfInitOnce";
pub const TIMER_EVENT_NAME: &str = "OnTimer";
pub const TIMER_QUIT_EVENT_NAME: &str = "OnTimerQuit";

/// enum npce_event and npc_get_script_event_name.
pub const NPCE_LOGIN: usize = 0;
pub const NPCE_LOGOUT: usize = 1;
pub const NPCE_LOADMAP: usize = 2;
pub const NPCE_BASELVUP: usize = 3;
pub const NPCE_JOBLVUP: usize = 4;
pub const NPCE_DIE: usize = 5;
pub const NPCE_KILLPC: usize = 6;
pub const NPCE_KILLNPC: usize = 7;
pub const NPCE_IDENTIFY: usize = 8;
pub const NPCE_NAMES: [&str; 9] =
    ["OnPCLoginEvent", "OnPCLogoutEvent", "OnPCLoadMapEvent", "OnPCBaseLvUpEvent", "OnPCJobLvUpEvent", "OnPCDieEvent", "OnPCKillEvent", "OnNPCKillEvent", "OnPCIdentifyEvent"];

/// mmo.hpp EVENT_NAME_LENGTH (NPC_NAME_LENGTH + 2 + NAME_LENGTH + 1) and NAME_LENGTH.
pub const EVENT_NAME_LENGTH: usize = 50 + 2 + 24 + 1;
const NAME_LENGTH: usize = 24;
/// map.hpp MAX_EVENTQUEUE, MAX_EVENTTIMER.
pub const MAX_EVENTQUEUE: usize = 2;
pub const MAX_EVENTTIMER: usize = 32;
/// config/secure.hpp: SECURE_NPCTIMEOUT on; NPC_SECURE_TIMEOUT_INPUT / MENU / NEXT seconds; interval 1 s.
pub const NPC_SECURE_TIMEOUT_INPUT: i64 = 180;
pub const NPC_SECURE_TIMEOUT_MENU: i64 = 60;
pub const NPC_SECURE_TIMEOUT_NEXT: i64 = 60;
pub const SECURE_NPCTIMEOUT_INTERVAL: i64 = 1;
/// npc.hpp npc_timeout_type.
pub const NPCT_INPUT: u8 = 0;
pub const NPCT_MENU: u8 = 1;
pub const NPCT_WAIT: u8 = 2;
/// conf/battle/gm.conf atcommand_disable_npc: yes.
fn atcommand_disable_npc() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::ATCOMMAND_DISABLE_NPC) != 0
}
/// clif.hpp MSI_BUSY (PACKETVER 20211103).
const MSI_BUSY: u16 = 1923;
/// map.hpp CHATBOX_SIZE.
const CHATBOX_SIZE: usize = 70 + 1;
/// The server's local time zone: the rAthena oracle runs with TZ=America/Sao_Paulo (UTC-3, no daylight saving since
/// 2019), so localtime() — clock events, gettime, gettimestr — is UTC-3 here too.
pub const LOCAL_TZ_OFFSET_SECS: i64 = -3 * 3600;

// ------------------------------------------------------------------------------------------------ tables

/// map_session_data's script state (kept apart from `pc` so other systems' fields and these do not collide).
#[table(accessor = pc_script)]
#[derive(Clone, Debug, Default)]
pub struct PcScript {
    #[primary_key]
    pub account_id: u32,
    /// sd->st: the attached script_state row (0 = none).
    pub st: u64,
    pub npc_menu: i32,
    pub npc_amount: i32,
    pub npc_str: Vec<u8>,
    pub menu_or_input: bool,
    pub using_fake_npc: bool,
    pub disable_atcommand_on_npc: bool,
    pub npc_item_flag: bool,
    pub callshop: bool,
    pub npc_idle_timer: u64,
    pub npc_idle_tick: i64,
    pub npc_idle_type: u8,
    pub ignoretimeout: bool,
    /// sd->eventqueue ("" = free slot).
    pub eventqueue: Vec<String>,
    /// sd->eventtimer slots: (timer id, event name), 0 = free.
    pub eventtimer: Vec<u64>,
    pub eventtimer_name: Vec<String>,
    /// sd->npc_ontouch_.
    pub npc_ontouch_: Vec<u32>,
    /// sd->npc_timer_id (a player-attached NPC timer).
    pub npc_timer_id: u64,
}

/// One OnTimerXXX label of an NPC (npc_timerevent_list).
#[derive(SpacetimeType, Clone, Debug)]
pub struct TimerLabel {
    pub timer: i32,
    pub pos: i32,
}

/// npc_data's script runtime (nd->u.scr) for a script NPC.
#[table(accessor = npc_scr)]
#[derive(Clone, Debug)]
pub struct NpcScr {
    #[primary_key]
    pub npc_id: u32,
    pub timer_event: Vec<TimerLabel>,
    pub timer: i64,
    pub timertick: i64,
    pub timerid: u64,
    pub rid: u32,
    pub touching_id: u32,
}

/// ev_db: "exname::Label" → (NPC, position), with the DBMap's iteration order.
#[table(accessor = npc_event, index(accessor = by_label, btree(columns = [label_lc])), index(accessor = by_name_lc, btree(columns = [name_lc])))]
#[derive(Clone, Debug)]
pub struct NpcEvent {
    #[primary_key]
    pub name: String,
    pub npc_id: u32,
    pub pos: i32,
    /// Rank in ev_db iteration (db_obj_vforeach: bucket order, each tree in pre-order).
    pub order: u32,
    /// Rank in DBIterator order (bucket order, each tree in key order): npc_read_event_script's lists.
    pub iorder: u32,
    /// Insertion sequence into ev_db (npc_event_export order), to rebuild the tree after removals.
    pub seq: u32,
    /// The key from its first ':' on, lower case (npc_event_doall_sub's strcmpi against "::Label").
    pub label_lc: String,
    pub name_lc: String,
}

/// Keys removed from ev_db (npc_unload_ev), in removal order: with the insertion sequence they let the tree — and so
/// the foreach order — be rebuilt exactly as db_rebalance_erase left it.
#[table(accessor = ev_db_removed)]
#[derive(Clone, Debug)]
pub struct EvDbRemoved {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub name: String,
    pub seq: u32,
}

/// ev_db changes made after the world was loaded, in the order they happened: keys put by NPCs created at run time
/// (the instance system's duplicates, npc_event_export) and keys removed (npc_unload_ev). With the keys put while the
/// world loaded (their `seq`) they rebuild the tree exactly as rAthena's DBMap holds it ([`ev_db_reorder`]).
#[table(accessor = ev_db_op)]
#[derive(Clone, Debug)]
pub struct EvDbOp {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub name: String,
    /// The key's insertion sequence (load-time keys below [`RT_SEQ_BASE`]).
    pub seq: u32,
    /// true = strdb_put of a new key, false = db_remove.
    pub put: bool,
}

/// Keys put into ev_db at run time take insertion sequences from here on (the load-time ones count from 0).
pub const RT_SEQ_BASE: u32 = 1 << 30;

/// timer_event_data of a running NPC timer, by timer id.
#[table(accessor = npc_timer_ted)]
#[derive(Clone, Debug)]
pub struct NpcTimerTed {
    #[primary_key]
    pub tid: u64,
    pub rid: u32,
    pub next: i32,
    pub time: i32,
}

/// Engine-wide state (one row).
#[table(accessor = script_world)]
#[derive(Clone, Debug)]
pub struct ScriptWorld {
    #[primary_key]
    pub key: u8,
    /// fake_nd->id.
    pub fake_npc_id: u32,
    pub clock_timer: u64,
    /// npc_event_do_clock's ev_tm_b (starts zeroed).
    pub ev_tm_min: i32,
    pub ev_tm_hour: i32,
    pub ev_tm_mday: i32,
}

fn world(c: &Ctx) -> ScriptWorld {
    c.db().script_world().key().find(0).unwrap_or(ScriptWorld { key: 0, fake_npc_id: 0, clock_timer: 0, ev_tm_min: 0, ev_tm_hour: 0, ev_tm_mday: 0 })
}

fn put_world(c: &Ctx, w: ScriptWorld) {
    if c.db().script_world().key().find(0).is_some() {
        c.db().script_world().key().update(w);
    } else {
        c.db().script_world().insert(w);
    }
}

pub fn fake_npc_id(c: &Ctx) -> u32 {
    world(c).fake_npc_id
}

/// The player's script state row (a default one when it has none yet).
pub fn sdx(c: &Ctx, aid: u32) -> PcScript {
    c.db().pc_script().account_id().find(aid).unwrap_or_else(|| PcScript {
        account_id: aid,
        eventqueue: vec![String::new(); MAX_EVENTQUEUE],
        eventtimer: vec![0; MAX_EVENTTIMER],
        eventtimer_name: vec![String::new(); MAX_EVENTTIMER],
        ..Default::default()
    })
}

pub fn put_sdx(c: &Ctx, x: &PcScript) {
    if c.db().pc_script().account_id().find(x.account_id).is_some() {
        c.db().pc_script().account_id().update(x.clone());
    } else {
        c.db().pc_script().insert(x.clone());
    }
}

fn scr(c: &Ctx, npc_id: u32) -> NpcScr {
    c.db().npc_scr().npc_id().find(npc_id).unwrap_or(NpcScr { npc_id, timer_event: Vec::new(), timer: 0, timertick: 0, timerid: 0, rid: 0, touching_id: 0 })
}

fn put_scr(c: &Ctx, s: NpcScr) {
    if c.db().npc_scr().npc_id().find(s.npc_id).is_some() {
        c.db().npc_scr().npc_id().update(s);
    } else {
        c.db().npc_scr().insert(s);
    }
}

fn set_npc_id(c: &Ctx, aid: u32, npc_id: u32) {
    if let Some(mut p) = pc::get(c, aid) {
        if p.npc_id != npc_id {
            p.npc_id = npc_id;
            pc::put(c, &p);
        }
    }
}

fn npc_id_of(c: &Ctx, aid: u32) -> u32 {
    pc::get(c, aid).map(|p| p.npc_id).unwrap_or(0)
}

/// npc_name2id: by unique name.
pub fn npc_name2id(c: &Ctx, name: &str) -> Option<Npc> {
    c.db().npc().exname().find(&name.to_string())
}

// ------------------------------------------------------------------------------------------------ attach / detach

/// script_attach_state.
pub fn script_attach_state(c: &Ctx, st: &mut State) {
    if st.rid == 0 {
        return;
    }
    let Some(mut p) = pc::get(c, st.rid) else { return };
    let mut x = sdx(c, st.rid);
    if st.id != x.st {
        if st.bk_st != 0 {
            log::debug!("script_attach_state: Previous script state lost (rid={}, oid={}, state={}, bk_npcid={}).", st.rid, st.oid, st.state, st.bk_npcid);
        }
        st.bk_st = x.st;
        st.bk_npcid = p.npc_id;
    }
    x.st = st.id;
    p.npc_id = st.oid;
    x.npc_item_flag = st.npc_item_flag;
    x.disable_atcommand_on_npc = atcommand_disable_npc() && !crate::pc::has_permission(c, p.account_id, crate::rules::PC_PERM_ENABLE_COMMAND);
    // SECURE_NPCTIMEOUT
    if x.npc_idle_timer == 0 && !x.ignoretimeout {
        x.npc_idle_timer = timer::add(c, c.tick() + SECURE_NPCTIMEOUT_INTERVAL * 1000, Func::NpcSecureTimeout, st.rid, 0);
    }
    x.npc_idle_tick = c.tick();
    pc::put(c, &p);
    put_sdx(c, &x);
}

/// script_detach_state: gives the player back its previous script, if any (and runs queued events).
pub fn script_detach_state(c: &Ctx, st: &mut State, dequeue_event: bool) {
    if st.rid != 0 && pc::get(c, st.rid).is_some() {
        clear_fake_npc(c, st.rid);
        let mut x = sdx(c, st.rid);
        x.st = st.bk_st;
        x.disable_atcommand_on_npc = false;
        let bk_npcid = st.bk_npcid;
        let mut dequeue = false;
        if st.bk_st != 0 {
            // remove tag for removal
            st.bk_st = 0;
            st.bk_npcid = 0;
        } else {
            x.menu_or_input = false;
            x.npc_menu = 0;
            if dequeue_event {
                // SECURE_NPCTIMEOUT: we're done with this NPC session, so we cancel the timer
                if x.npc_idle_timer != 0 {
                    timer::delete(c, x.npc_idle_timer);
                    x.npc_idle_timer = 0;
                }
                dequeue = true;
            }
        }
        put_sdx(c, &x);
        set_npc_id(c, st.rid, bk_npcid);
        if dequeue {
            npc_event_dequeue(c, st.rid, true);
        }
    } else if st.bk_st != 0 {
        // rid was set to 0 before detaching the script state
        log::error!("script_detach_state: Found previous script state without attached player (rid={}, oid={}, state={}, bk_npcid={})", st.rid, st.oid, st.state, st.bk_npcid);
        script_free_state_id(c, st.bk_st);
        st.bk_st = 0;
    }
}

/// script_detach_rid.
pub fn script_detach_rid(c: &Ctx, st: &mut State) {
    if st.rid != 0 {
        script_detach_state(c, st, false);
        st.rid = 0;
    }
}

/// sd->state.using_fake_npc: the fake NPC vanishes from the client.
pub fn clear_fake_npc(c: &Ctx, aid: u32) {
    let mut x = sdx(c, aid);
    if x.using_fake_npc {
        if let (Some(sd), Some(p)) = (unit::get(c, aid), pc::get(c, aid)) {
            clif::clearunit_single(c, p.npc_id, unit::CLR_OUTSIGHT, &sd);
        }
        x.using_fake_npc = false;
        put_sdx(c, &x);
    }
}

/// script_free_state for a state value (its row goes; the player lets go of it).
pub fn script_free_state(c: &Ctx, st: &State) {
    if st.id == 0 {
        return;
    }
    if st.rid != 0 && pc::get(c, st.rid).is_some() {
        let mut x = sdx(c, st.rid);
        if x.st == st.id {
            if x.using_fake_npc {
                if let Some(sd) = unit::get(c, st.rid) {
                    clif::clearunit_single(c, npc_id_of(c, st.rid), unit::CLR_OUTSIGHT, &sd);
                }
                x.using_fake_npc = false;
            }
            x.st = 0;
            x.menu_or_input = false;
            x.npc_menu = 0;
            put_sdx(c, &x);
            set_npc_id(c, st.rid, 0);
        }
    }
    if st.bk_st != 0 {
        log::debug!("script_free_state: Previous script state lost (rid={}, oid={}, bk_npcid={}).", st.rid, st.oid, st.bk_npcid);
    }
    if st.sleep_timer != 0 {
        timer::delete(c, st.sleep_timer);
    }
    state::delete(c, st.id);
}

/// script_free_state by id (a suspended state that is not loaded).
pub fn script_free_state_id(c: &Ctx, id: u64) {
    if id == 0 || state::is_running(id) {
        return;
    }
    match state::load(c, id) {
        Some(st) => script_free_state(c, &st),
        None => state::delete(c, id),
    }
}

// ------------------------------------------------------------------------------------------------ reporting

/// script_reportsrc for an NPC source.
pub fn reportsrc(c: &Ctx, oid: u32) {
    if let Some(nd) = c.db().npc().id().find(oid) {
        if nd.map >= 0 {
            let m = crate::data::maps().get(nd.map as u16).map(|d| d.name.as_str()).unwrap_or("?");
            log::debug!("Source (NPC): {} at {} ({},{})", nd.name, m, nd.x, nd.y);
        } else {
            log::debug!("Source (NPC): {} (invisible/not on a map)", nd.name);
        }
        log::debug!("Source (NPC): {} is located in: {}", nd.name, nd.path);
    } else if let Some(u) = unit::get(c, oid) {
        log::debug!("Source (Non-NPC type {}): name {}", u.kind, u.name);
    }
}

/// script.cpp script_instancegetid(st) (mode IM_NONE): the instance of the running NPC (nd->instance_id), 0 when it has
/// none (instance.rs).
pub fn script_instancegetid(rt: &Rt, st: &State) -> i32 {
    match rt.c {
        Some(c) => crate::instance::script_instancegetid(c, st.oid, st.rid, crate::instance::IM_NONE),
        None => 0,
    }
}

// ------------------------------------------------------------------------------------------------ running scripts

/// run_script for an NPC: its code at `pos`.
pub fn run_npc(c: &Ctx, npc_id: u32, pos: i32, rid: u32) {
    let Some((key, _)) = code::npc_code(c, npc_id) else { return };
    run_script(&Rt::new(c), &key, pos, rid, npc_id);
}

/// npc_event_dequeue: the attached script is done; a queued event runs 100 ms later.
pub fn npc_event_dequeue(c: &Ctx, aid: u32, free_script_stack: bool) -> bool {
    let Some(p) = pc::get(c, aid) else { return false };
    let mut x = sdx(c, aid);
    if p.npc_id != 0 {
        if x.using_fake_npc {
            if let Some(sd) = unit::get(c, aid) {
                clif::clearunit_single(c, p.npc_id, unit::CLR_OUTSIGHT, &sd);
            }
            x.using_fake_npc = false;
        }
        if free_script_stack && x.st != 0 {
            let id = x.st;
            x.st = 0;
            put_sdx(c, &x);
            script_free_state_id(c, id);
            x = sdx(c, aid);
        }
        if free_script_stack {
            if let Some(mut p) = pc::get(c, aid) {
                p.block_action = 0;
                pc::put(c, &p);
            }
            if let Some(mut u) = unit::get(c, aid) {
                u.ud.blockedmove = false;
                u.ud.blockedskill = false;
                unit::save(c, &u);
            }
        }
        put_sdx(c, &x);
        set_npc_id(c, aid, 0);
    }
    if x.eventqueue.first().map(|s| s.is_empty()).unwrap_or(true) {
        return false;
    }
    let ev = x.eventqueue[0].clone();
    if !pc_addeventtimer(c, aid, 100, &ev) {
        log::warn!("npc_event_dequeue: event timer is full !");
        return false;
    }
    let mut x = sdx(c, aid);
    x.eventqueue.remove(0);
    x.eventqueue.push(String::new());
    put_sdx(c, &x);
    true
}

/// pc_addeventtimer.
pub fn pc_addeventtimer(c: &Ctx, aid: u32, tick: i64, name: &str) -> bool {
    let mut x = sdx(c, aid);
    let Some(i) = x.eventtimer.iter().position(|&t| t == 0) else { return false };
    x.eventtimer[i] = timer::add(c, c.tick() + tick, Func::PcEventTimer, aid, 0);
    x.eventtimer_name[i] = name.to_string();
    put_sdx(c, &x);
    true
}

/// pc_eventtimer.
pub fn pc_eventtimer(c: &Ctx, tid: u64, aid: u32) {
    if pc::get(c, aid).is_none() {
        return;
    }
    let mut x = sdx(c, aid);
    match x.eventtimer.iter().position(|&t| t == tid) {
        Some(i) => {
            x.eventtimer[i] = 0;
            let name = std::mem::take(&mut x.eventtimer_name[i]);
            put_sdx(c, &x);
            npc_event(c, aid, &name, 0);
        }
        None => log::error!("pc_eventtimer: no such event timer"),
    }
}

/// pc_deleventtimer.
pub fn pc_deleventtimer(c: &Ctx, aid: u32, name: &str) -> bool {
    let mut x = sdx(c, aid);
    let Some(i) = x.eventtimer.iter().zip(&x.eventtimer_name).position(|(&t, n)| t != 0 && n == name) else { return false };
    timer::delete(c, x.eventtimer[i]);
    x.eventtimer[i] = 0;
    x.eventtimer_name[i].clear();
    put_sdx(c, &x);
    true
}

/// pc_cleareventtimer.
pub fn pc_cleareventtimer(c: &Ctx, aid: u32) {
    let Some(mut x) = c.db().pc_script().account_id().find(aid) else { return };
    for i in 0..x.eventtimer.len() {
        if x.eventtimer[i] != 0 {
            timer::delete(c, x.eventtimer[i]);
            x.eventtimer[i] = 0;
            x.eventtimer_name[i].clear();
        }
    }
    put_sdx(c, &x);
}

fn pc_ishiding(u: &unit::Unit) -> bool {
    u.sc.option & (OPTION_HIDE | OPTION_CLOAK | OPTION_CHASEWALK) != 0
}

/// npc_is_cloaked.
pub fn npc_is_cloaked(c: &Ctx, npc_id: u32, aid: u32) -> bool {
    let cloaked = unit::get(c, npc_id).map(|u| u.sc.option & OPTION_CLOAK != 0).unwrap_or(false);
    if pc::npc_cloaked_for(c, aid, npc_id) {
        !cloaked
    } else {
        cloaked
    }
}

/// npc_event_sub: runs an event for a player (queued when the player is busy with another NPC).
pub fn npc_event_sub(c: &Ctx, aid: u32, ev: &NpcEvent, eventname: &str) -> i32 {
    let Some(p) = pc::get(c, aid) else { return 1 };
    if p.npc_id != 0 {
        let mut x = sdx(c, aid);
        if let Some(i) = x.eventqueue.iter().position(|s| s.is_empty()) {
            x.eventqueue[i] = eventname.chars().take(EVENT_NAME_LENGTH - 1).collect();
            put_sdx(c, &x);
            return 0;
        }
        log::warn!("npc_event: player's event queue is full, can't add event '{eventname}' !");
        return 1;
    }
    let Some(nd) = c.db().npc().id().find(ev.npc_id) else { return 1 };
    if nd.is_invisible {
        npc_event_dequeue(c, aid, true);
        return 2;
    }
    let ontouch = format!("{}::{}", nd.exname, ONTOUCH_EVENT_NAME);
    let ontouch2 = format!("{}::{}", nd.exname, ONTOUCH2_EVENT_NAME);
    if eventname == ontouch || eventname == ontouch2 {
        let (xs, ys, x, y) = (nd.xs as i32, nd.ys as i32, nd.x as i32, nd.y as i32);
        let sd = unit::get(c, aid);
        let (sx, sy) = sd.as_ref().map(|u| (u.x as i32, u.y as i32)).unwrap_or((0, 0));
        let out = sx < x - xs || sx > x + xs || sy < y - ys || sy > y + ys;
        if (x > 0 && y > 0 && (xs > -1 && ys > -1) && out) || p.block_action & pc::PCBLOCK_NPCCLICK != 0 || npc_is_cloaked(c, nd.id, aid) {
            npc_event_dequeue(c, aid, true);
            return 2;
        }
    }
    run_npc(c, ev.npc_id, ev.pos, aid);
    0
}

/// npc_event: runs `eventname` ("exname::Label") for a player. `ontouch` 1 = OnTouch_, 2 = OnTouch.
pub fn npc_event(c: &Ctx, aid: u32, eventname: &str, ontouch: i32) -> i32 {
    let ev = c.db().npc_event().name().find(&eventname.to_string());
    let Some(ev) = ev.filter(|e| c.db().npc().id().find(e.npc_id).is_some()) else {
        if ontouch == 0 {
            log::error!("npc_event: event not found [{eventname}]");
        }
        return ontouch;
    };
    if ontouch == 1 {
        if unit::get(c, aid).map(|u| pc_ishiding(&u)).unwrap_or(false) {
            return 0;
        }
        let mut s = scr(c, ev.npc_id);
        s.touching_id = aid;
        put_scr(c, s);
        let mut x = sdx(c, aid);
        if !x.npc_ontouch_.contains(&ev.npc_id) {
            x.npc_ontouch_.push(ev.npc_id);
            put_sdx(c, &x);
        }
    } else if ontouch == 2 {
        if let Some(mut p) = pc::get(c, aid) {
            if !p.areanpc.contains(&ev.npc_id) {
                p.areanpc.push(ev.npc_id);
                pc::put(c, &p);
            }
        }
    }
    npc_event_sub(c, aid, &ev, eventname);
    0
}

/// ev_db entries whose key from the first ':' equals `label` ("::OnX", case-insensitive), in iteration order.
fn events_with_label(c: &Ctx, label: &str) -> Vec<NpcEvent> {
    let mut v: Vec<NpcEvent> = c.db().npc_event().by_label().filter(&label.to_ascii_lowercase()).collect();
    v.sort_by_key(|e| e.order);
    v
}

thread_local! {
    /// ev_db's free_lock: foreach loops running (removals meanwhile only mark keys deleted).
    static EV_LOCK: std::cell::Cell<u32> = const { std::cell::Cell::new(0) };
    /// Keys were removed while locked: the iteration order changes when the last lock goes.
    static EV_DIRTY: std::cell::Cell<bool> = const { std::cell::Cell::new(false) };
}

/// ev_db->foreach: runs `f` over `evs` (a snapshot in iteration order) under the free lock, skipping entries removed
/// meanwhile (node->deleted).
fn ev_foreach(c: &Ctx, evs: Vec<NpcEvent>, mut f: impl FnMut(&NpcEvent)) {
    EV_LOCK.with(|l| l.set(l.get() + 1));
    for ev in evs {
        if c.db().npc_event().name().find(&ev.name).is_none() {
            continue;
        }
        f(&ev);
    }
    EV_LOCK.with(|l| l.set(l.get() - 1));
    if EV_LOCK.with(|l| l.get()) == 0 && EV_DIRTY.with(|d| d.replace(false)) {
        ev_db_reorder(c);
    }
}

/// Each key's place in db_obj_vforeach order, as (hash bucket << 20) | (pre-order rank within the bucket): sorting by
/// it is the iteration order, and a key put or removed changes the values of its own bucket's keys only (the
/// instance system adds and drops NPC events at run time, so a global rank would rewrite every row each time).
fn ev_db_ranks<V: Clone>(db: &StrDb<V>) -> std::collections::HashMap<Vec<u8>, u32> {
    let mut out = std::collections::HashMap::with_capacity(db.len());
    let mut last_bucket = usize::MAX;
    let mut within: u32 = 0;
    for (k, _) in db.iter_order() {
        let b = db.bucket(k);
        if b != last_bucket {
            last_bucket = b;
            within = 0;
        }
        out.insert(k.to_vec(), ((b as u32) << 20) | within);
        within += 1;
    }
    out
}

/// The tree after the run-time changes: rebuilt from the keys the world load put (in their insertion sequence), then
/// the run-time puts and removals replayed in the order they happened (strdb_put / db_rebalance_erase); the
/// pre-order ranks of what is left are stored.
fn ev_db_reorder(c: &Ctx) {
    let rows: Vec<NpcEvent> = c.db().npc_event().iter().collect();
    let mut ops: Vec<EvDbOp> = c.db().ev_db_op().iter().collect();
    ops.sort_by_key(|r| r.id);
    let mut all: Vec<(u32, &str)> = rows.iter().filter(|r| r.seq < RT_SEQ_BASE).map(|r| (r.seq, r.name.as_str())).collect();
    all.extend(ops.iter().filter(|o| !o.put && o.seq < RT_SEQ_BASE).map(|o| (o.seq, o.name.as_str())));
    all.sort_by_key(|&(seq, _)| seq);
    all.dedup_by_key(|x| x.0);
    let mut db: StrDb<()> = StrDb::new(EVENT_NAME_LENGTH as u16, false);
    for (_, name) in &all {
        db.put(&crate::net::wire::latin1(name), ());
    }
    for o in &ops {
        if o.put {
            db.put(&crate::net::wire::latin1(&o.name), ());
        } else {
            db.remove(&crate::net::wire::latin1(&o.name));
        }
    }
    let rank = ev_db_ranks(&db);
    for r in rows {
        let Some(&o) = rank.get(&crate::net::wire::latin1(&r.name)) else { continue };
        if o != r.order {
            c.db().npc_event().name().update(NpcEvent { order: o, ..r });
        }
    }
}

/// npc.cpp npc_unload_ev: drops every event of an NPC from ev_db (the NPC is being unloaded).
pub fn npc_unload_ev(c: &Ctx, npc_id: u32) {
    // an NPC's keys are "exname::label" for the labels of its script (npc_event_export): they are looked up by name
    // rather than by scanning ev_db (tens of thousands of rows) for every NPC an instance unloads
    let by_labels = c.db().npc().id().find(npc_id).and_then(|nd| code::npc_code(c, npc_id).map(|(_, code)| (nd, code)));
    let evs: Vec<NpcEvent> = match by_labels {
        Some((nd, code)) => {
            let mut keys: Vec<String> = code.labels.iter().map(|(l, _)| format!("{}::{}", nd.exname, l).chars().take(EVENT_NAME_LENGTH - 1).collect()).collect();
            keys.sort();
            keys.dedup();
            keys.into_iter().filter_map(|k| c.db().npc_event().name().find(&k)).filter(|e| e.npc_id == npc_id).collect()
        }
        None => c.db().npc_event().iter().filter(|e| e.npc_id == npc_id).collect(),
    };
    if evs.is_empty() {
        return;
    }
    let mut evs = evs;
    evs.sort_by_key(|e| e.order); // the foreach visits them in iteration order
    for ev in evs {
        c.db().ev_db_op().insert(EvDbOp { id: 0, name: ev.name.clone(), seq: ev.seq, put: false });
        c.db().npc_event().name().delete(&ev.name);
    }
    if EV_LOCK.with(|l| l.get()) == 0 {
        ev_db_reorder(c);
    } else {
        EV_DIRTY.with(|d| d.set(true));
    }
}

/// Holds back ev_db_reorder while many keys change at once (the instance system duplicating or unloading a map's
/// NPCs): the tree itself is rebuilt from the log, so only the stored iteration ranks wait. Pair with
/// [`ev_db_unlock`]; nothing may iterate ev_db in between.
pub fn ev_db_lock() {
    EV_LOCK.with(|l| l.set(l.get() + 1));
}

/// A reducer starts with ev_db unlocked (a call that failed while a foreach or [`ev_db_lock`] held it cannot leave the
/// next one locked: its changes were rolled back with it).
pub fn ev_db_reset_lock() {
    EV_LOCK.with(|l| l.set(0));
    EV_DIRTY.with(|d| d.set(false));
}

/// Ends an [`ev_db_lock`]: the ranks are brought up to date when the last lock goes.
pub fn ev_db_unlock(c: &Ctx) {
    EV_LOCK.with(|l| l.set(l.get().saturating_sub(1)));
    if EV_LOCK.with(|l| l.get()) == 0 && EV_DIRTY.with(|d| d.replace(false)) {
        ev_db_reorder(c);
    }
}

/// npc.cpp npc_event_export for an NPC made at run time (npc_parse_duplicate from the instance system): an "On"
/// label becomes the ev_db key "exname::label" — refused for the script events (OnPCLoginEvent …) on an instance map
/// and for OnWhisperGlobal of a name too long to whisper. Returns true when the key already existed (strdb_put
/// replaced its data: the caller's "duplicate event" warning).
pub fn npc_event_export_rt(c: &Ctx, nd: &Npc, lname: &str, pos: i32, on_instance_map: bool) -> bool {
    let lb = lname.as_bytes();
    if !(lb.len() >= 2 && lb[0].eq_ignore_ascii_case(&b'O') && lb[1].eq_ignore_ascii_case(&b'N')) {
        return false;
    }
    if nd.map > -1 && on_instance_map {
        // Block script events in instances
        if NPCE_NAMES.iter().any(|n| n.eq_ignore_ascii_case(lname)) {
            log::warn!("npc_event_export: attempting to duplicate a script event in an instance ({}::{lname}), ignoring", nd.name);
            return false;
        }
    }
    // NPC:<name> the prefix uses 4 characters (strncasecmp over NAME_LENGTH)
    let lcut: Vec<u8> = lb.iter().take(NAME_LENGTH).map(|b| b.to_ascii_lowercase()).collect();
    let wcut: Vec<u8> = ONWHISPER_EVENT_NAME.as_bytes().iter().take(NAME_LENGTH).map(|b| b.to_ascii_lowercase()).collect();
    if lcut == wcut && crate::net::wire::latin1(&nd.exname).len() > NAME_LENGTH - 4 {
        // The client only allows that many character so that NPC could not be whispered by unmodified clients
        log::warn!("Whisper event in npc '{}' was ignored, because it's name is too long.", nd.exname);
        return false;
    }
    let key: String = format!("{}::{}", nd.exname, lname).chars().take(EVENT_NAME_LENGTH - 1).collect();
    if let Some(r) = c.db().npc_event().name().find(&key) {
        // There was already another event of the same name (strdb_put replaces the data; the tree is unchanged)
        c.db().npc_event().name().update(NpcEvent { npc_id: nd.id, pos, ..r });
        return true;
    }
    let seq = RT_SEQ_BASE + crate::ids::next(c.rc, "ev_db_rt_seq", 0);
    let label_lc = match key.find(':') {
        Some(i) => key[i..].to_ascii_lowercase(),
        None => String::new(),
    };
    // (not in npc_read_event_script's lists until it runs again: iorder u32::MAX)
    c.db().npc_event().insert(NpcEvent { name_lc: key.to_ascii_lowercase(), name: key.clone(), npc_id: nd.id, pos, order: 0, iorder: u32::MAX, seq, label_lc });
    c.db().ev_db_op().insert(EvDbOp { id: 0, name: key, seq, put: true });
    if EV_LOCK.with(|l| l.get()) == 0 {
        ev_db_reorder(c);
    } else {
        EV_DIRTY.with(|d| d.set(true));
    }
    false
}

/// npc_parse_script / npc_parse_duplicate's label loop for an NPC made at run time (`who` names the caller in the
/// "duplicate event" warning): npc_event_export and npc_timerevent_export for each label of the (shared) script, then
/// `nd->u.scr.timerid = INVALID_TIMER` (a fresh npc_scr row).
pub fn export_labels_rt(c: &Ctx, nd: &Npc, on_instance_map: bool, who: &str) {
    let mut timers: Vec<TimerLabel> = Vec::new();
    if let Some((_, code)) = code::npc_code(c, nd.id) {
        for (lname, pos) in &code.labels {
            if npc_event_export_rt(c, nd, lname, *pos, on_instance_map) {
                log::warn!("{who} : duplicate event {}::{} ({})", nd.exname, lname, nd.path);
            }
            // npc_timerevent_export: "OnTimer" + %11d and nothing after
            if let Some(rest) = lname.strip_prefix(TIMER_EVENT_NAME) {
                let rb = rest.as_bytes();
                let (t, k, _) = super::compiler::strtoll(rb, 0, 10);
                let signed_ok = !rest.is_empty() && k > 0 && k <= 11;
                if signed_ok && k == rb.len() {
                    let t = t as i32;
                    let j = timers.iter().position(|x| x.timer > t).unwrap_or(timers.len());
                    timers.insert(j, TimerLabel { timer: t, pos: *pos });
                }
            }
        }
    }
    put_scr(c, NpcScr { npc_id: nd.id, timer_event: timers, timer: 0, timertick: 0, timerid: 0, rid: 0, touching_id: 0 });
}

/// npc_unload's script part (single = true): the NPC's events, players' and its own NPC timers, its sleeping
/// scripts (script_stop_sleeptimers) and its runtime row.
pub fn on_npc_unload(c: &Ctx, nd: &Npc) {
    on_npc_unload_ex(c, nd, true);
}

/// npc_unload(nd, single)'s script part: `if (single) ev_db->foreach(ev_db, npc_unload_ev, nd->exname)` (npc_reload
/// cleared ev_db before it unloads), the players' NPC timers of this NPC and its own, and — for a source NPC —
/// script_free_code, whose `.` variables go with the code; then script_stop_sleeptimers.
pub fn on_npc_unload_ex(c: &Ctx, nd: &Npc, single: bool) {
    if nd.subtype != NPCTYPE_SCRIPT {
        return;
    }
    if single {
        npc_unload_ev(c, nd.id);
    }
    if nd.src_id == 0 {
        crate::npc_load::free_code_vars(c, &CodeKey::Npc(nd.id));
    }
    for mut x in c.db().pc_script().iter().filter(|x| x.npc_timer_id != 0).collect::<Vec<_>>() {
        if let Some(t) = c.db().timer().id().find(x.npc_timer_id) {
            if t.target != nd.id {
                continue;
            }
        }
        timer::delete(c, x.npc_timer_id);
        c.db().npc_timer_ted().tid().delete(x.npc_timer_id);
        x.npc_timer_id = 0;
        put_sdx(c, &x);
    }
    let s = scr(c, nd.id);
    if s.timerid != 0 {
        timer::delete(c, s.timerid);
        c.db().npc_timer_ted().tid().delete(s.timerid);
    }
    c.db().npc_scr().npc_id().delete(nd.id);
    let sleeping: Vec<u64> = c.db().script_state().by_oid().filter(&nd.id).filter(|r| r.sleep_timer != 0).map(|r| r.id).collect();
    for id in sleeping {
        script_free_state_id(c, id);
    }
}

/// npc_event_do_id: runs an event on every NPC that has it ("::OnX": all of them) — for `rid` when given.
pub fn npc_event_do_id(c: &Ctx, name: &str, rid: u32) -> i32 {
    let mut count = 0;
    if name.starts_with("::") {
        // ev_db->foreach(ev_db, npc_event_doall_sub, &c, name, 0, rid): the 0 lands where npc_event_doall_sub reads
        // its rid, so the event runs without a player
        ev_foreach(c, events_with_label(c, name), |ev| {
            run_npc(c, ev.npc_id, ev.pos, 0);
            count += 1;
        });
    } else {
        let mut v: Vec<NpcEvent> = c.db().npc_event().by_name_lc().filter(&name.to_ascii_lowercase()).collect();
        v.sort_by_key(|e| e.order);
        ev_foreach(c, v, |ev| {
            run_npc(c, ev.npc_id, ev.pos, rid);
            count += 1;
        });
    }
    count
}

/// npc_event_do.
pub fn npc_event_do(c: &Ctx, name: &str) -> i32 {
    npc_event_do_id(c, name, 0)
}

/// npc_event_doall_id.
pub fn npc_event_doall_id(c: &Ctx, name: &str, rid: u32) -> i32 {
    let buf: String = format!("::{name}").chars().take(EVENT_NAME_LENGTH - 1).collect();
    let mut count = 0;
    ev_foreach(c, events_with_label(c, &buf), |ev| {
        if rid != 0 {
            // a player may only have 1 script running at the same time
            if pc::get(c, rid).is_some() {
                npc_event_sub(c, rid, ev, &ev.name);
            }
        } else {
            run_npc(c, ev.npc_id, ev.pos, 0);
        }
        count += 1;
    });
    count
}

/// npc_event_doall.
pub fn npc_event_doall(c: &Ctx, name: &str) -> i32 {
    npc_event_doall_id(c, name, 0)
}

/// npc_event_runall.
pub fn npc_event_runall(c: &Ctx, name: &str) {
    let n = npc_event_doall(c, name);
    log::info!("Event '{name}' executed with '{n}' NPCs.");
}

/// The order an ev_db DBIterator meets `keys` (all in the map): bucket by bucket, each bucket's tree in key order
/// (strncmp) — which the keys alone decide. Returns indices into `keys`.
pub(crate) fn ev_db_iterator_order(keys: &[Vec<u8>]) -> Vec<usize> {
    let db: StrDb<()> = StrDb::new(EVENT_NAME_LENGTH as u16, false);
    let mut idx: Vec<(usize, usize)> = keys.iter().enumerate().map(|(i, k)| (db.bucket(k), i)).collect();
    idx.sort_by(|a, b| (a.0, &keys[a.1]).cmp(&(b.0, &keys[b.1])));
    idx.into_iter().map(|(_, i)| i).collect()
}

/// npc.cpp npc_read_event_script at run time: script_event[] is built again from ev_db as it is now — for each of the
/// NPCE_* events, the keys "<exname>::<event>" (strcmpi from the first ':') in DBIterator order (bucket by bucket,
/// each bucket's tree in key order, which depends on the keys only). Only those lists are ever read
/// (npc_script_event), so only their rows get a rank here: a consistent order among them, not the global one the
/// start-up gives every key (on_world_built); a key put since the last call joins its list now.
pub fn npc_read_event_script(c: &Ctx) {
    let mut rows: Vec<NpcEvent> = Vec::new();
    for n in NPCE_NAMES {
        rows.extend(c.db().npc_event().by_label().filter(&format!("::{n}").to_ascii_lowercase()));
    }
    let keys: Vec<Vec<u8>> = rows.iter().map(|e| crate::net::wire::latin1(&e.name)).collect();
    for (i, k) in ev_db_iterator_order(&keys).into_iter().enumerate() {
        let e = &rows[k];
        if e.iorder != i as u32 {
            c.db().npc_event().name().update(NpcEvent { iorder: i as u32, ..e.clone() });
        }
    }
    // battle_config.etc_log (off): the per-event summary
    for n in NPCE_NAMES {
        log::debug!("{} '{n}' events.", c.db().npc_event().by_label().filter(&format!("::{n}").to_ascii_lowercase()).count());
    }
}

/// npc_script_event: OnPCLoginEvent & co for a player.
pub fn npc_script_event(c: &Ctx, aid: u32, kind: usize) -> usize {
    let Some(name) = NPCE_NAMES.get(kind) else { return 0 };
    // script_event[type], built by npc_read_event_script with a DBIterator (key order within each bucket): the keys
    // put since the last npc_read_event_script (iorder u32::MAX) are not in it
    let mut evs: Vec<NpcEvent> = c.db().npc_event().by_label().filter(&format!("::{name}").to_ascii_lowercase()).filter(|e| e.iorder != u32::MAX).collect();
    evs.sort_by_key(|e| e.iorder);
    let n = evs.len();
    for ev in evs {
        if pc::get(c, aid).is_none() {
            break;
        }
        npc_event_sub(c, aid, &ev, &ev.name);
    }
    n
}

// ------------------------------------------------------------------------------------------------ OnTouch

/// npc_ontouch_event (OnTouch_).
fn npc_ontouch_event(c: &Ctx, aid: u32, nd: &Npc) -> i32 {
    if pc::isdead(c, aid) {
        return 0;
    }
    if scr(c, nd.id).touching_id != 0 {
        return 0;
    }
    if sdx(c, aid).npc_ontouch_.contains(&nd.id) {
        return 0;
    }
    let name: String = format!("{}::{}", nd.exname, ONTOUCH_EVENT_NAME).chars().take(EVENT_NAME_LENGTH - 1).collect();
    npc_event(c, aid, &name, 1)
}

/// npc_ontouch2_event (OnTouch).
fn npc_ontouch2_event(c: &Ctx, aid: u32, nd: &Npc) -> i32 {
    if pc::get(c, aid).map(|p| p.areanpc.contains(&nd.id)).unwrap_or(false) {
        return 0;
    }
    let name: String = format!("{}::{}", nd.exname, ONTOUCH2_EVENT_NAME).chars().take(EVENT_NAME_LENGTH - 1).collect();
    npc_event(c, aid, &name, 2)
}

/// npc_touch_areanpc's NPCTYPE_SCRIPT branch: OnTouch_ / OnTouch, or a click when the NPC has neither.
pub fn touch_script(c: &Ctx, aid: u32, nd: &Npc) {
    if npc_ontouch_event(c, aid, nd) > 0 && npc_ontouch2_event(c, aid, nd) > 0 {
        if let Some(mut p) = pc::get(c, aid) {
            if !p.areanpc.contains(&nd.id) {
                p.areanpc.push(nd.id);
                pc::put(c, &p);
            }
            if p.npc_id == 0 {
                npc_click(c, aid, nd.id);
            }
        }
    }
}

/// npc_touchnext_areanpc: the player left (or left the map of) an OnTouch_ area it held; the next player standing
/// in it gets the event.
pub fn npc_touchnext_areanpc(c: &Ctx, aid: u32, leavemap: bool) -> bool {
    let Some(mut x) = c.db().pc_script().account_id().find(aid) else { return false };
    if x.npc_ontouch_.is_empty() {
        return false;
    }
    let sd = unit::get(c, aid);
    let mut found = false;
    let mut keep = Vec::new();
    let list = std::mem::take(&mut x.npc_ontouch_);
    put_sdx(c, &x);
    let mut to_fire: Vec<(Npc, String)> = Vec::new();
    for id in list {
        let Some(nd) = c.db().npc().id().find(id) else { continue };
        let (xs, ys) = (nd.xs, nd.ys);
        let away = match &sd {
            Some(u) => u.map as i32 != nd.map || u.x < nd.x - xs || u.x > nd.x + xs || u.y < nd.y - ys || u.y > nd.y + ys,
            None => true,
        };
        if away || leavemap {
            let mut s = scr(c, nd.id);
            if s.touching_id != 0 && s.touching_id == aid {
                found = true;
                s.touching_id = 0;
                put_scr(c, s);
                let name: String = format!("{}::{}", nd.exname, ONTOUCH_EVENT_NAME).chars().take(EVENT_NAME_LENGTH - 1).collect();
                to_fire.push((nd, name));
            }
        } else {
            keep.push(id);
        }
    }
    let mut x = sdx(c, aid);
    x.npc_ontouch_ = keep;
    put_sdx(c, &x);
    for (nd, name) in to_fire {
        // map_forcountinarea(npc_touch_areanpc_sub, ..., 1, BL_PC, sd->id, name)
        if nd.map < 0 {
            continue;
        }
        for other in crate::map::units_in_area(c, nd.map as u16, nd.x - nd.xs, nd.y - nd.ys, nd.x + nd.xs, nd.y + nd.ys, crate::map::BL_PC) {
            let Some(op) = pc::get(c, other.id) else { continue };
            if op.warping || pc_ishiding(&other) || op.dead_sit == 1 || other.id == aid {
                continue;
            }
            npc_event(c, other.id, &name, 1);
            break;
        }
    }
    found
}

// ------------------------------------------------------------------------------------------------ clicking and dialogs

/// npc_checknear.
pub fn npc_checknear(c: &Ctx, aid: u32, npc_id: u32) -> Option<Npc> {
    let nd = c.db().npc().id().find(npc_id)?;
    let x = sdx(c, aid);
    if x.using_fake_npc && npc_id_of(c, aid) == npc_id {
        return Some(nd);
    }
    if nd.class < 0 {
        return Some(nd);
    }
    let sd = unit::get(c, aid)?;
    let r = crate::conf::area_size();
    if nd.map != sd.map as i32 || nd.x < sd.x - r - 1 || nd.x > sd.x + r + 1 || nd.y < sd.y - r - 1 || nd.y > sd.y + r + 1 {
        return None;
    }
    Some(nd)
}


/// npc_click: a player clicked (or walked into) an NPC.
pub fn npc_click(c: &Ctx, aid: u32, npc_id: u32) -> i32 {
    if npc_id_of(c, aid) != 0 {
        log::error!("npc_click: npc_id != 0");
        return 1;
    }
    let Some(nd) = npc_checknear(c, aid, npc_id) else { return 1 };
    let option = unit::get(c, nd.id).map(|u| u.sc.option).unwrap_or(0);
    if nd.class < 0 || option & OPTION_HIDE != 0 {
        return 1;
    }
    let Some(sd) = unit::get(c, aid) else { return 1 };
    if nd.owner_char != 0 && nd.owner_char != sd.char_id {
        return 1;
    }
    let Some(p) = pc::get(c, aid) else { return 1 };
    if p.block_action & pc::PCBLOCK_NPCCLICK != 0 {
        crate::clif_items::msg(c, &sd, MSI_BUSY);
        return 1;
    }
    if nd.owner_char != 0 {
        crate::npc_load::dynamicnpc_touch(c, nd.id); // nd->dynamicnpc.last_interaction = gettick()
    }
    match nd.subtype {
        NPCTYPE_SCRIPT => run_npc(c, nd.id, 0, aid),
        NPCTYPE_TOMB => crate::mob::dead::run_tomb(c, aid, nd.id),
        // NPCTYPE_SHOP, CASHSHOP, ITEMSHOP, POINTSHOP, MARKETSHOP, BARTER (shop.rs)
        _ => {
            if !crate::shop::npc_click_shop(c, aid, &nd) {
                return 0; // (rAthena's MARKETSHOP branch returns false = 0 when sold out)
            }
        }
    }
    0
}

/// npc_scriptcont: the player answered the NPC's dialog (next / menu / input / close).
pub fn npc_scriptcont(c: &Ctx, aid: u32, id: u32, closing: bool) -> bool {
    let mut x = sdx(c, aid);
    if !closing && x.npc_idle_timer == 0 && !x.ignoretimeout {
        return true;
    }
    let cur = npc_id_of(c, aid);
    if id != cur {
        let a = c.db().npc().id().find(cur).map(|n| n.name).unwrap_or_else(|| "'Unknown NPC'".into());
        let b = c.db().npc().id().find(id).map(|n| n.name).unwrap_or_else(|| "'Unknown NPC'".into());
        log::debug!("npc_scriptcont: {a} (sd->npc_id={cur}) is not {b} (id={id}).");
        return true;
    }
    if id != fake_npc_id(c) && npc_checknear(c, aid, id).is_none() {
        log::warn!("npc_scriptcont: failed npc_checknear test.");
        return true;
    }
    if !closing {
        x.npc_idle_tick = c.tick();
        put_sdx(c, &x);
    }
    // nd->dynamicnpc.last_interaction = gettick() for a dynamic NPC
    crate::npc_load::dynamicnpc_touch(c, id);
    // WPE can get to this point with a progressbar; we deny it.
    if crate::pc_misc::progressbar_blocks_scriptcont(c, aid) {
        return true;
    }
    let Some(mut st) = state::load(c, x.st) else { return true };
    if closing {
        match st.state {
            CLOSE => {
                st.state = END;
                if st.clear_cutin {
                    if let Some(sd) = unit::get(c, aid) {
                        clif::cutin(c, &sd, b"", 255);
                    }
                }
            }
            STOP => st.state = RUN,
            _ => {
                // (rAthena prints the state after setting it to END)
                x.menu_or_input = false;
                x.npc_menu = 0;
                put_sdx(c, &x);
                st.state = END;
                log::error!("npc_scriptcont: unexpected state '{END}' for closing call. (AID: {aid})");
            }
        }
    } else {
        match st.state {
            STOP => st.state = RUN,
            RERUNLINE => {}
            _ => {
                x.menu_or_input = false;
                x.npc_menu = 0;
                put_sdx(c, &x);
                st.state = END;
                log::error!("npc_scriptcont: unexpected state '{END}' for continue call. (AID: {aid})");
            }
        }
    }
    run_script_main(&Rt::new(c), st);
    false
}

/// clif_parse_NpcClicked (CZ_CONTACTNPC).
pub fn parse_npc_clicked(c: &Ctx, aid: u32, pkt: &[u8]) {
    let id = u32::from_le_bytes([pkt[2], pkt[3], pkt[4], pkt[5]]);
    let Some(sd) = unit::get(c, aid) else { return };
    let Some(p) = pc::get(c, aid) else { return };
    if p.dead_sit == 1 {
        clif::clearunit_area(c, &sd, unit::CLR_DEAD);
        return;
    }
    const OPT1_BURNING: i16 = 7;
    let cant_act2 = p.vending || p.buyingstore || (sd.sc.opt1 != 0 && sd.sc.opt1 != OPT1_BURNING) || p.trading || p.storage_flag != 0 || p.prevend || p.ui_open != 0 || p.npc_shopid != 0;
    if cant_act2 || p.npc_id != 0 || crate::itemui::pc_hasprogress(c, aid, crate::itemui::WIP_DISABLE_NPC) {
        crate::clif_items::msg(c, &sd, MSI_BUSY); // RENEWAL
        return;
    }
    if p.mail_writing {
        return;
    }
    let Some(bl) = unit::get(c, id) else { return };
    match bl.kind {
        crate::map::BL_MOB | crate::map::BL_PC => crate::parse::action_request_sub(c, aid, 7, bl.id as i32),
        crate::map::BL_NPC => {
            // RENEWAL: a non-3rd-job skill cast in progress (skills, phase 4)
            if sd.ud.skilltimer != 0 && (sd.ud.skill_id as i32) < 2001 {
                crate::clif_items::msg(c, &sd, MSI_BUSY);
                return;
            }
            // (floating NPCs have no unit here, so they are never clickable)
            // Progressbar is running
            if crate::pc_misc::npc_progressbar_running(c, bl.id) {
                return;
            }
            npc_click(c, aid, bl.id);
        }
        _ => {}
    }
}

/// idletime_option (0x7C1F) has every IDLE_NPC_* bit.
fn touch_idle(c: &Ctx, aid: u32, bit: i32) {
    if crate::conf::idletime_option() & bit != 0 {
        if let Some(mut p) = pc::get(c, aid) {
            p.idletime = crate::pc::last_tick(c);
            pc::put(c, &p);
        }
    }
}

/// clif_parse_NpcSelectMenu (CZ_CHOOSE_MENU).
pub fn parse_npc_select_menu(c: &Ctx, aid: u32, pkt: &[u8]) {
    let pos = crate::net::packets::packet_pos(u16::from_le_bytes([pkt[0], pkt[1]]));
    let rd = |i: usize| pos.get(i).copied().unwrap_or(0) as usize;
    let npc_id = pkt.get(rd(0)..rd(0) + 4).map(|b| u32::from_le_bytes([b[0], b[1], b[2], b[3]])).unwrap_or(0);
    let select = pkt.get(rd(1)).copied().unwrap_or(0);
    let mut x = sdx(c, aid);
    if x.npc_idle_timer == 0 && !x.ignoretimeout {
        return;
    }
    if (select as i32 > x.npc_menu && select != 0xff) || select == 0 {
        let name = c.db().npc().id().find(npc_id).map(|n| n.name).unwrap_or_else(|| "invalid npc id".into());
        log::warn!("Invalid menu selection on npc {npc_id}:'{name}' - got {select}, valid range is [1..{}] (player AID:{aid})!", x.npc_menu);
        // clif_GM_kick(nullptr, sd): SC_NOTIFY_BAN 15, set_eof; clif_parse closes the session before its next
        // packet (net::parse), dropping what the client sent after this one
        crate::session::clif_gm_kick(c, None, aid);
        return;
    }
    x.npc_menu = select as i32;
    put_sdx(c, &x);
    touch_idle(c, aid, 0x1000); // IDLE_NPC_MENU
    npc_scriptcont(c, aid, npc_id, false);
}

/// clif_parse_NpcNextClicked (CZ_REQ_NEXT_SCRIPT).
pub fn parse_npc_next_clicked(c: &Ctx, aid: u32, pkt: &[u8]) {
    let pos = crate::net::packets::packet_pos(u16::from_le_bytes([pkt[0], pkt[1]]));
    let o = pos.first().copied().unwrap_or(2) as usize;
    let id = pkt.get(o..o + 4).map(|b| u32::from_le_bytes([b[0], b[1], b[2], b[3]])).unwrap_or(0);
    touch_idle(c, aid, 0x2000); // IDLE_NPC_NEXT
    npc_scriptcont(c, aid, id, false);
}

/// clif_parse_NpcAmountInput (CZ_INPUT_EDITDLG).
pub fn parse_npc_amount_input(c: &Ctx, aid: u32, pkt: &[u8]) {
    if pkt.len() < 10 {
        return;
    }
    let gid = u32::from_le_bytes([pkt[2], pkt[3], pkt[4], pkt[5]]);
    let value = i32::from_le_bytes([pkt[6], pkt[7], pkt[8], pkt[9]]);
    let mut x = sdx(c, aid);
    x.npc_amount = value;
    put_sdx(c, &x);
    touch_idle(c, aid, 0x0800); // IDLE_NPC_INPUT
    npc_scriptcont(c, aid, gid, false);
}

/// clif_parse_NpcStringInput (CZ_INPUT_EDITDLGSTR).
pub fn parse_npc_string_input(c: &Ctx, aid: u32, pkt: &[u8]) {
    if pkt.len() < 8 {
        return;
    }
    let size = u16::from_le_bytes([pkt[2], pkt[3]]) as usize;
    let gid = u32::from_le_bytes([pkt[4], pkt[5], pkt[6], pkt[7]]);
    if size <= 8 {
        return; // invalid input
    }
    let message_len = size - 8 + 1; // PACKETVER >= 20151001: no zero termination
    let raw = &pkt[8..];
    let n = message_len.min(CHATBOX_SIZE) - 1; // safestrncpy
    let s: Vec<u8> = raw.iter().copied().take(n).take_while(|&b| b != 0).collect();
    let mut x = sdx(c, aid);
    x.npc_str = s;
    put_sdx(c, &x);
    touch_idle(c, aid, 0x0800);
    npc_scriptcont(c, aid, gid, false);
}

/// clif_parse_NpcCloseClicked (CZ_CLOSE_DIALOG).
pub fn parse_npc_close_clicked(c: &Ctx, aid: u32, pkt: &[u8]) {
    if npc_id_of(c, aid) == 0 {
        return;
    }
    touch_idle(c, aid, 0x0400); // IDLE_NPC_CLOSE
    let gid = u32::from_le_bytes([pkt[2], pkt[3], pkt[4], pkt[5]]);
    npc_scriptcont(c, aid, gid, true);
}

/// pc_close_npc.
pub fn pc_close_npc(c: &Ctx, aid: u32, flag: i32) {
    let Some(p) = pc::get(c, aid) else { return };
    if p.npc_id == 0 && p.npc_shopid == 0 {
        return;
    }
    clear_fake_npc(c, aid);
    let mut x = sdx(c, aid);
    let mut st = if x.st != 0 { state::load(c, x.st) } else { None };
    if x.st != 0 && state::is_running(x.st) {
        timer::add(c, c.tick() + 500, Func::PcCloseNpc, aid, flag as i64);
        return;
    }
    if let Some(s) = st.as_mut() {
        s.state = if flag == 1 && s.mes_active { CLOSE } else { END };
        s.mes_active = false;
    }
    x.menu_or_input = false;
    x.npc_menu = 0;
    if x.npc_idle_timer != 0 {
        timer::delete(c, x.npc_idle_timer);
        x.npc_idle_timer = 0;
    }
    put_sdx(c, &x);
    if let Some(mut p) = pc::get(c, aid) {
        p.npc_shopid = 0;
        pc::put(c, &p);
    }
    if let Some(mut s) = st {
        if s.state == CLOSE {
            if let Some(sd) = unit::get(c, aid) {
                clif::scriptclose(c, &sd, p.npc_id);
                clif::cutin(c, &sd, b"", 255);
            }
            s.state = END;
        }
        if s.state == END {
            script_free_state(c, &s);
            let mut x = sdx(c, aid);
            x.st = 0;
            put_sdx(c, &x);
            set_npc_id(c, aid, 0);
            if let Some(mut p) = pc::get(c, aid) {
                p.block_action = 0;
                pc::put(c, &p);
            }
            if let Some(mut u) = unit::get(c, aid) {
                u.ud.blockedmove = false;
                u.ud.blockedskill = false;
                unit::save(c, &u);
            }
        } else {
            state::save(c, &s);
        }
    }
}

/// npc_secure_timeout_timer.
pub fn npc_secure_timeout_timer(c: &Ctx, tid: u64, aid: u32) {
    let p = pc::get(c, aid);
    let mut x = sdx(c, aid);
    if p.as_ref().map(|p| p.npc_id == 0).unwrap_or(true) || x.ignoretimeout {
        if p.is_some() && x.npc_idle_timer != 0 {
            if x.npc_idle_timer != tid {
                timer::delete(c, x.npc_idle_timer);
            }
            x.npc_idle_timer = 0;
            put_sdx(c, &x);
        }
        return;
    }
    let timeout = match x.npc_idle_type {
        NPCT_INPUT => NPC_SECURE_TIMEOUT_INPUT,
        NPCT_MENU => NPC_SECURE_TIMEOUT_MENU,
        _ => NPC_SECURE_TIMEOUT_NEXT,
    };
    let st_state = if x.st != 0 && !state::is_running(x.st) { c.db().script_state().id().find(x.st).and_then(|r| state::load(c, r.id)).map(|s| s.state) } else { None };
    if c.tick() - x.npc_idle_tick > timeout * 1000 {
        pc_close_npc(c, aid, 1);
    } else if matches!(st_state, Some(END) | Some(CLOSE)) {
        if x.npc_idle_timer != 0 && x.npc_idle_timer != tid {
            timer::delete(c, x.npc_idle_timer);
        }
        x.npc_idle_timer = 0;
        put_sdx(c, &x);
    } else {
        x.npc_idle_timer = timer::add(c, c.tick() + SECURE_NPCTIMEOUT_INTERVAL * 1000, Func::NpcSecureTimeout, aid, 0);
        put_sdx(c, &x);
    }
}

use super::state::script_state;

// ------------------------------------------------------------------------------------------------ sleep

/// run_script_timer: a sleeping script wakes up.
pub fn run_script_timer(c: &Ctx, tid: u64, st_id: u64) {
    let Some(mut st) = state::load(c, st_id) else { return };
    if st.sleep_timer != tid && tid != 0 {
        return; // not this state's timer any more
    }
    let charid = st.sleep_charid;
    if charid != 0 && st.rid != 0 {
        match pc::get(c, st.rid) {
            None => {
                st.rid = 0;
                st.state = END;
            }
            Some(p) if p.char_id != charid => {
                log::warn!("Script sleep timer detected a character mismatch CID {} != {charid}", p.char_id);
                reportsrc(c, st.oid);
                st.rid = 0;
                st.state = END;
            }
            _ => {}
        }
    }
    st.sleep_timer = 0;
    if st.state != RERUNLINE {
        st.sleep_tick = 0;
    }
    run_script_main(&Rt::new(c), st);
}

/// buildin awake: wakes every sleeping state of an NPC now.
pub fn awake_npc(c: &Ctx, npc_id: u32) {
    let rows: Vec<(u64, u64)> = c.db().script_state().by_oid().filter(&npc_id).filter(|r| r.sleep_timer != 0).map(|r| (r.id, r.sleep_timer)).collect();
    for (id, tid) in rows {
        timer::delete(c, tid);
        run_script_timer(c, 0, id);
    }
}

// ------------------------------------------------------------------------------------------------ NPC timers

/// npc_timerevent: an OnTimerXXX label is due.
pub fn npc_timerevent(c: &Ctx, tid: u64, tick: i64, npc_id: u32) {
    let Some(ted) = c.db().npc_timer_ted().tid().find(tid) else { return };
    c.db().npc_timer_ted().tid().delete(tid);
    if c.db().npc().id().find(npc_id).is_none() {
        log::error!("npc_timerevent: NPC not found??");
        return;
    }
    let sd = if ted.rid != 0 {
        if pc::get(c, ted.rid).is_none() {
            log::error!("npc_timerevent: Attached player not found.");
            return;
        }
        Some(ted.rid)
    } else {
        None
    };
    let mut s = scr(c, npc_id);
    let old_rid = s.rid;
    let old_tick = s.timertick;
    let old_timer = s.timer;
    s.rid = sd.unwrap_or(0);
    s.timertick = tick;
    s.timer = ted.time as i64;
    let Some(te) = s.timer_event.get(ted.next as usize).cloned() else {
        put_scr(c, s);
        return;
    };
    let mut ted = ted;
    ted.next += 1;
    if (s.timer_event.len() as i32) > ted.next {
        let next = s.timer_event[ted.next as usize].timer - s.timer_event[ted.next as usize - 1].timer;
        ted.time += next;
        let ntid = timer::add(c, tick + next as i64, Func::NpcTimerEvent, npc_id, 0);
        c.db().npc_timer_ted().insert(NpcTimerTed { tid: ntid, ..ted });
        if let Some(aid) = sd {
            let mut x = sdx(c, aid);
            x.npc_timer_id = ntid;
            put_sdx(c, &x);
        } else {
            s.timerid = ntid;
        }
    } else if let Some(aid) = sd {
        let mut x = sdx(c, aid);
        x.npc_timer_id = 0;
        put_sdx(c, &x);
    } else {
        s.timerid = 0;
    }
    let rid = s.rid;
    put_scr(c, s);
    run_npc(c, npc_id, te.pos, rid);
    let mut s = scr(c, npc_id);
    s.rid = old_rid;
    if sd.is_some() {
        s.timer = old_timer;
        s.timertick = old_tick;
    }
    put_scr(c, s);
}

/// npc_timerevent_start.
pub fn npc_timerevent_start(c: &Ctx, npc_id: u32) -> i32 {
    let mut s = scr(c, npc_id);
    let j = s.timer_event.iter().position(|t| t.timer as i64 > s.timer).unwrap_or(s.timer_event.len());
    let sd = if s.rid > 0 {
        if pc::get(c, s.rid).is_none() {
            log::error!("npc_timerevent_start: Attached player not found!");
            return 1;
        }
        Some(s.rid)
    } else {
        None
    };
    if let Some(aid) = sd {
        if sdx(c, aid).npc_timer_id != 0 {
            return 0;
        }
    } else if s.timerid != 0 || s.timertick != 0 {
        return 0;
    }
    if j < s.timer_event.len() {
        let next = s.timer_event[j].timer as i64 - s.timer;
        let ted = NpcTimerTed { tid: 0, rid: sd.unwrap_or(0), next: j as i32, time: s.timer_event[j].timer };
        let tid = timer::add(c, c.tick() + next, Func::NpcTimerEvent, npc_id, 0);
        c.db().npc_timer_ted().insert(NpcTimerTed { tid, ..ted });
        if let Some(aid) = sd {
            let mut x = sdx(c, aid);
            x.npc_timer_id = tid;
            put_sdx(c, &x);
        } else {
            s.timertick = c.tick();
            s.timerid = tid;
        }
    } else if sd.is_none() {
        s.timertick = c.tick();
    }
    put_scr(c, s);
    0
}

/// npc_timerevent_stop.
pub fn npc_timerevent_stop(c: &Ctx, npc_id: u32) -> i32 {
    let mut s = scr(c, npc_id);
    let sd = if s.rid != 0 {
        if pc::get(c, s.rid).is_none() {
            log::error!("npc_timerevent_stop: Attached player not found!");
            return 1;
        }
        Some(s.rid)
    } else {
        None
    };
    let tid = match sd {
        Some(aid) => sdx(c, aid).npc_timer_id,
        None => s.timerid,
    };
    if tid == 0 && (sd.is_some() || s.timertick == 0) {
        return 0;
    }
    if tid != 0 {
        c.db().npc_timer_ted().tid().delete(tid);
        timer::delete(c, tid);
        match sd {
            Some(aid) => {
                let mut x = sdx(c, aid);
                x.npc_timer_id = 0;
                put_sdx(c, &x);
            }
            None => s.timerid = 0,
        }
    }
    if sd.is_none() && s.timertick != 0 {
        s.timer += c.tick() - s.timertick;
        s.timertick = 0;
    }
    put_scr(c, s);
    0
}

/// npc_timerevent_quit: a player-attached NPC timer is aborted (the player leaves): OnTimerQuit runs.
pub fn npc_timerevent_quit(c: &Ctx, aid: u32) {
    let mut x = sdx(c, aid);
    if x.npc_timer_id == 0 {
        return;
    }
    let tid = x.npc_timer_id;
    x.npc_timer_id = 0;
    put_sdx(c, &x);
    let Some(ted) = c.db().npc_timer_ted().tid().find(tid) else { return };
    let npc_id = c.db().timer().id().find(tid).map(|t| t.target);
    c.db().npc_timer_ted().tid().delete(tid);
    timer::delete(c, tid);
    let Some(npc_id) = npc_id else { return };
    let Some(nd) = c.db().npc().id().find(npc_id) else { return };
    let buf: String = format!("{}::{}", nd.exname, TIMER_QUIT_EVENT_NAME).chars().take(EVENT_NAME_LENGTH - 1).collect();
    let ev = c.db().npc_event().name().find(&buf);
    let ev = match ev {
        Some(e) if e.npc_id != nd.id => {
            log::warn!("npc_timerevent_quit: Unable to execute \"{TIMER_QUIT_EVENT_NAME}\", two NPCs have the same event name [{buf}]!");
            None
        }
        e => e,
    };
    if let Some(ev) = ev {
        let mut s = scr(c, nd.id);
        let old_rid = if s.rid == aid { 0 } else { s.rid };
        let old_tick = s.timertick;
        let old_timer = s.timer;
        s.rid = aid;
        s.timertick = c.tick();
        s.timer = ted.time as i64;
        put_scr(c, s);
        run_npc(c, nd.id, ev.pos, aid);
        let mut s = scr(c, nd.id);
        s.rid = old_rid;
        s.timer = old_timer;
        s.timertick = old_tick;
        put_scr(c, s);
    }
}

use crate::timer::timer as _;
use crate::ids::id_counter as _;

/// npc_gettimerevent_tick.
pub fn npc_gettimerevent_tick(c: &Ctx, npc_id: u32) -> i64 {
    let s = scr(c, npc_id);
    let mut tick = s.timer;
    if s.timertick != 0 {
        tick += c.tick() - s.timertick;
    }
    tick
}

/// npc_settimerevent_tick.
pub fn npc_settimerevent_tick(c: &Ctx, npc_id: u32, newtimer: i32) {
    let mut s = scr(c, npc_id);
    let old_rid = s.rid;
    s.rid = 0;
    let flag = s.timerid != 0 || s.timertick != 0;
    put_scr(c, s);
    if flag {
        npc_timerevent_stop(c, npc_id);
    }
    let mut s = scr(c, npc_id);
    s.timer = newtimer as i64;
    put_scr(c, s);
    if flag {
        npc_timerevent_start(c, npc_id);
    }
    let mut s = scr(c, npc_id);
    s.rid = old_rid;
    put_scr(c, s);
}

pub fn npc_timer_info(c: &Ctx, npc_id: u32) -> (u32, u64, usize) {
    let s = scr(c, npc_id);
    (s.rid, s.timerid, s.timer_event.len())
}

pub fn set_npc_timer_rid(c: &Ctx, npc_id: u32, rid: u32) {
    let mut s = scr(c, npc_id);
    s.rid = rid;
    put_scr(c, s);
}

pub fn reset_npc_timertick(c: &Ctx, npc_id: u32) {
    let mut s = scr(c, npc_id);
    s.timertick = 0;
    put_scr(c, s);
}

// ------------------------------------------------------------------------------------------------ clock

/// localtime(): the parts of `struct tm` the engine uses, for a unix time in seconds (server time zone).
#[derive(Clone, Copy, Debug, Default)]
pub struct Tm {
    pub sec: i32,
    pub min: i32,
    pub hour: i32,
    pub mday: i32,
    /// 0..11
    pub mon: i32,
    /// years since 1900
    pub year: i32,
    /// 0 = Sunday
    pub wday: i32,
    /// 0..365
    pub yday: i32,
}

pub fn localtime(unix_secs: i64) -> Tm {
    let t = unix_secs + LOCAL_TZ_OFFSET_SECS;
    let days = t.div_euclid(86_400);
    let rem = t.rem_euclid(86_400);
    let (hour, min, sec) = ((rem / 3600) as i32, ((rem % 3600) / 60) as i32, (rem % 60) as i32);
    // civil from days (Howard Hinnant)
    let z = days + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z - era * 146_097;
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = doy - (153 * mp + 2) / 5 + 1;
    let m = if mp < 10 { mp + 3 } else { mp - 9 };
    let y = if m <= 2 { y + 1 } else { y };
    let leap = (y % 4 == 0 && y % 100 != 0) || y % 400 == 0;
    const CUM: [i32; 12] = [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334];
    let yday = CUM[(m - 1) as usize] + d as i32 - 1 + if leap && m > 2 { 1 } else { 0 };
    let wday = (days + 4).rem_euclid(7) as i32; // 1970-01-01 was a Thursday
    Tm { sec, min, hour, mday: d as i32, mon: (m - 1) as i32, year: (y - 1900) as i32, wday, yday }
}

/// npc_event_do_clock: OnMinuteMM, OnClockHHMM, On<Day>HHMM, OnHourHH, OnDayMMDD as the time moves on.
pub fn npc_event_do_clock(c: &Ctx) {
    let t = localtime(c.tick().div_euclid(1000));
    let mut w = world(c);
    let mut n = 0;
    if t.min != w.ev_tm_min {
        n += npc_event_doall(c, &format!("OnMinute{:02}", t.min));
        n += npc_event_doall(c, &format!("OnClock{:02}{:02}", t.hour, t.min));
        let day = ["OnSun", "OnMon", "OnTue", "OnWed", "OnThu", "OnFri", "OnSat"][t.wday as usize];
        n += npc_event_doall(c, &format!("{day}{:02}{:02}", t.hour, t.min));
    }
    if t.hour != w.ev_tm_hour {
        n += npc_event_doall(c, &format!("OnHour{:02}", t.hour));
    }
    if t.mday != w.ev_tm_mday {
        n += npc_event_doall(c, &format!("OnDay{:02}{:02}", t.mon + 1, t.mday));
    }
    let _ = n;
    w = world(c);
    w.ev_tm_min = t.min;
    w.ev_tm_hour = t.hour;
    w.ev_tm_mday = t.mday;
    put_world(c, w);
}

// ------------------------------------------------------------------------------------------------ whispers

/// clif_parse_WisMessage's "NPC:<name>" branch: @whispervar0$..9$ and the NPC's OnWhisperGlobal.
pub fn whisper_npc(c: &Ctx, aid: u32, npc_name: &str, message: &str) -> bool {
    let Some(nd) = npc_name2id(c, npc_name) else { return false };
    let mut msg = crate::net::wire::latin1(message);
    if msg.first() == Some(&b'|') && msg.len() >= 4 {
        msg.drain(..3); // skip the codepage indicator
    }
    let mut split: Vec<Vec<u8>> = Vec::with_capacity(10);
    let mut rest: &[u8] = &msg;
    for i in 0..10 {
        match rest.iter().position(|&b| b == b'#') {
            None => {
                split.push(rest.iter().copied().take(crate::net::CHAT_SIZE_MAX - 1).collect());
                for _ in i + 1..10 {
                    split.push(Vec::new());
                }
                break;
            }
            Some(k) => {
                split.push(rest[..k].iter().copied().take(crate::net::CHAT_SIZE_MAX - 1).collect());
                rest = &rest[k + 1..];
            }
        }
    }
    for (i, v) in split.iter().enumerate() {
        super::reg::set_var_str(c, aid, &format!("@whispervar{i}$"), v);
    }
    let event: String = format!("{}::{}", nd.exname, ONWHISPER_EVENT_NAME).chars().take(EVENT_NAME_LENGTH - 1).collect();
    npc_event(c, aid, &event, 0);
    true
}

// ------------------------------------------------------------------------------------------------ player hooks

/// The first script parts of map_quit (map.cpp:2244-2251): the player's NPC timer (OnTimerQuit), the dialog it had
/// open. (OnPCLogoutEvent comes after pc_itemcd_do: pc.rs quit.)
pub fn on_quit(c: &Ctx, aid: u32) {
    if sdx(c, aid).npc_timer_id != 0 {
        npc_timerevent_quit(c, aid);
    }
    if npc_id_of(c, aid) != 0 {
        npc_event_dequeue(c, aid, true);
    }
}

/// clif_parse_LoadEndAck after a map change: "abort currently running script".
pub fn abort_on_map_change(c: &Ctx, aid: u32) {
    let mut x = sdx(c, aid);
    x.using_fake_npc = false;
    x.menu_or_input = false;
    x.npc_menu = 0;
    put_sdx(c, &x);
    if npc_id_of(c, aid) != 0 {
        npc_event_dequeue(c, aid, true);
    }
    if let Some(mut p) = pc::get(c, aid) {
        p.block_action = 0;
        pc::put(c, &p);
    }
    if let Some(mut u) = unit::get(c, aid) {
        u.ud.blockedmove = false;
        u.ud.blockedskill = false;
        unit::save(c, &u);
    }
}

/// unit_free's player part: event timers, a script still waiting for the player, the `@` variables.
pub fn on_unit_free(c: &Ctx, aid: u32) {
    pc_cleareventtimer(c, aid);
    let x = sdx(c, aid);
    if x.st != 0 && !state::is_running(x.st) {
        script_free_state_id(c, x.st);
    }
    // sd->npc_id_dynamic: the player's dynamic NPCs are unloaded (npc_load.rs)
    crate::npc_load::unit_free_pc(c, aid);
    if x.npc_idle_timer != 0 {
        timer::delete(c, x.npc_idle_timer);
    }
    // any other state still attached to this player (backups) dies with the session
    let ids: Vec<u64> = c.db().script_state().by_rid().filter(&aid).map(|r| r.id).collect();
    for id in ids {
        if !state::is_running(id) {
            script_free_state_id(c, id);
        }
    }
    super::reg::clear_tmp_regs(c, aid);
    c.db().pc_script().account_id().delete(aid);
}

// ------------------------------------------------------------------------------------------------ world build

/// Called at the end of npc.rs build_world (npc_loadsrcfiles done): compiles every script, builds ev_db and the NPC
/// timer lists, creates the fake NPC, and runs OnInit / OnInterIfInit / OnInterIfInitOnce like a map-server start.
pub fn on_world_built(c: &Ctx, next_npc_id: u32) {
    // everything from the previous world is gone (rAthena restart); npc.rs build_world already cleared the compiled
    // scripts and userfunc_db (code::clear_functions) and registered the functions it loaded (code::define_function)
    for id in c.db().script_state().iter().map(|r| r.id).collect::<Vec<_>>() {
        state::delete(c, id);
    }
    for x in c.db().pc_script().iter().collect::<Vec<_>>() {
        let aid = x.account_id;
        c.db().pc_script().account_id().update(PcScript { st: 0, menu_or_input: false, npc_timer_id: 0, npc_ontouch_: Vec::new(), ..x });
        set_npc_id(c, aid, 0);
    }
    for id in c.db().npc_scr().iter().map(|s| s.npc_id).collect::<Vec<_>>() {
        c.db().npc_scr().npc_id().delete(id);
    }
    ev_db_clear(c);
    for t in c.db().npc_timer_ted().iter().map(|t| t.tid).collect::<Vec<_>>() {
        c.db().npc_timer_ted().tid().delete(t);
    }
    let script_funcs = [Func::ScriptSleep as u16, Func::NpcTimerEvent as u16, Func::NpcEventDoClock as u16];
    for t in c.db().timer().iter().filter(|t| script_funcs.contains(&t.func)).map(|t| t.id).collect::<Vec<_>>() {
        timer::delete(c, t);
    }
    super::reg::clear_world_regs(c);

    // the fake NPC (do_init_npc's fake_nd, npc.cpp:6370): npc_create_npc takes the next NPC id after every file is
    // loaded and after barter_db.load(), which made one NPC per barter shop (npc.rs create_barters): next_npc_id
    let fake_id = next_npc_id;
    let fake = Npc {
        id: fake_id,
        exname: "FAKE_NPC".into(),
        name: "FAKE_NPC".into(),
        subtype: NPCTYPE_SCRIPT,
        map: -1,
        x: 0,
        y: 0,
        class: crate::npc::JT_FAKENPC,
        src_id: 0,
        xs: -1,
        ys: -1,
        warp_map: -1,
        warp_x: 0,
        warp_y: 0,
        trigger_on_hidden: false,
        state: crate::npc::NPCVIEW_ENABLE,
        is_invisible: false,
        path: String::new(),
        script_file: 0,
        script_start: 0,
        script_end: 0,
        script_line: 0,
        shop: Vec::new(),
        shop_discount: false,
        itemshop_nameid: 0,
        pointshop_str: String::new(),
        owner_char: 0,
        touch_order: 0,
    };
    if c.db().npc().exname().find(&fake.exname).is_none() {
        c.db().npc().insert(fake);
    }
    let mut w = world(c);
    w.fake_npc_id = fake_id;
    w.ev_tm_min = 0;
    w.ev_tm_hour = 0;
    w.ev_tm_mday = 0;
    w.clock_timer = 0;
    put_world(c, w);

    export_all_labels(c);
    code::set_loading(false);
    // npc_read_event_script's summary (battle_config.etc_log: off)
    for n in NPCE_NAMES {
        log::debug!("{} '{n}' events.", events_with_label(c, &format!("::{n}")).len());
    }

    // map.cpp do_init → npc_event_do_oninit: OnInit, then the clock (100 ms, every second)
    npc_event_runall(c, INIT_EVENT_NAME);
    let tid = timer::add_interval(c, c.tick() + 100, Func::NpcEventDoClock, 0, 0, 1000);
    let mut w = world(c);
    w.clock_timer = tid;
    put_world(c, w);
    // chrif_connectack: the char server is always there
    npc_event_runall(c, INTER_INIT_EVENT_NAME);
    npc_event_runall(c, INTER_INIT_ONCE_EVENT_NAME);
    // chrif_connectack: guild_castle_map_init after OnInterIfInitOnce (the castles' answer runs OnAgitInit; woe.rs)
    crate::woe::guild_castle_map_init(c);
}

/// db_clear(ev_db): no key is left, and the log of run-time changes starts over with it (the tree is built again from
/// the keys put from now on, as [`export_all_labels`] numbers them).
pub(crate) fn ev_db_clear(c: &Ctx) {
    for n in c.db().npc_event().iter().map(|e| e.name).collect::<Vec<_>>() {
        c.db().npc_event().name().delete(n);
    }
    for id in c.db().ev_db_removed().iter().map(|e| e.id).collect::<Vec<_>>() {
        c.db().ev_db_removed().id().delete(id);
    }
    for id in c.db().ev_db_op().iter().map(|e| e.id).collect::<Vec<_>>() {
        c.db().ev_db_op().id().delete(id);
    }
    c.db().id_counter().kind().delete(&"ev_db_rt_seq".to_string());
}

/// npc_parse_script / npc_parse_duplicate's label loops for a whole load into an empty ev_db (the start-up and
/// npc_reload, npc.rs Mode::Boot / Mode::Reload): every script NPC (the ids count up in load order, so id order is the
/// order rAthena exported them in) compiles its code and exports its labels (npc_event_export) and timers
/// (npc_timerevent_export), with a fresh npc_scr row; then npc_read_event_script's lists (every key ranked).
pub(crate) fn export_all_labels(c: &Ctx) {
    // npc_parse_script / npc_parse_duplicate: export each NPC's labels as events (npc_event_export) and timers
    let mut npcs: Vec<Npc> = c.db().npc().iter().filter(|n| n.subtype == NPCTYPE_SCRIPT && n.script_end != 0).collect();
    npcs.sort_by_key(|n| n.id);
    // value: (npc, position, insertion sequence of the key)
    let mut ev_db: StrDb<(u32, i32, u32)> = StrDb::new(EVENT_NAME_LENGTH as u16, false);
    let mut seq: u32 = 0;
    let mut compiled = 0;
    for nd in &npcs {
        let Some((_, code)) = code::npc_code(c, nd.id) else {
            put_scr(c, NpcScr { npc_id: nd.id, timer_event: Vec::new(), timer: 0, timertick: 0, timerid: 0, rid: 0, touching_id: 0 });
            continue;
        };
        compiled += 1;
        let mut timers: Vec<TimerLabel> = Vec::new();
        for (lname, pos) in &code.labels {
            let lb = lname.as_bytes();
            if lb.len() >= 2 && lb[0].eq_ignore_ascii_case(&b'O') && lb[1].eq_ignore_ascii_case(&b'N') {
                // (block script events in instances: no NPC is on an instance map while the world loads; the
                // instance duplicates export their labels with npc_event_export_rt)
                let is_whisper = lname.len() >= ONWHISPER_EVENT_NAME.len().min(NAME_LENGTH)
                    && lname.as_bytes()[..ONWHISPER_EVENT_NAME.len()].eq_ignore_ascii_case(ONWHISPER_EVENT_NAME.as_bytes())
                    && lname.len() == ONWHISPER_EVENT_NAME.len();
                if is_whisper && crate::net::wire::latin1(&nd.exname).len() > NAME_LENGTH - 4 {
                    log::warn!("Whisper event in npc '{}' was ignored, because it's name is too long.", nd.exname);
                } else {
                    let key: String = format!("{}::{}", nd.exname, lname).chars().take(EVENT_NAME_LENGTH - 1).collect();
                    let kb = crate::net::wire::latin1(&key);
                    let first = ev_db.get(&kb).map(|v| v.2).unwrap_or(seq);
                    if ev_db.put(&kb, (nd.id, *pos, first)) {
                        let who = if nd.src_id != 0 { "npc_parse_duplicate" } else { "npc_parse_script" };
                        log::warn!("{who} : duplicate event {}::{} ({})", nd.exname, lname, nd.path);
                    } else {
                        seq += 1;
                    }
                }
            }
            // npc_timerevent_export: "OnTimer" + %11d and nothing after
            if let Some(rest) = lname.strip_prefix(TIMER_EVENT_NAME) {
                let rb = rest.as_bytes();
                let (t, k, _) = super::compiler::strtoll(rb, 0, 10);
                let signed_ok = !rest.is_empty() && k > 0 && k <= 11;
                if signed_ok && k == rb.len() {
                    let t = t as i32;
                    let j = timers.iter().position(|x| x.timer > t).unwrap_or(timers.len());
                    timers.insert(j, TimerLabel { timer: t, pos: *pos });
                }
            }
        }
        put_scr(c, NpcScr { npc_id: nd.id, timer_event: timers, timer: 0, timertick: 0, timerid: 0, rid: 0, touching_id: 0 });
    }
    let irank: std::collections::HashMap<Vec<u8>, u32> = ev_db.iter_inorder().into_iter().enumerate().map(|(i, (k, _))| (k.to_vec(), i as u32)).collect();
    let ranks = ev_db_ranks(&ev_db);
    for (key, &(npc_id, pos, seq)) in ev_db.iter_order().into_iter() {
        let rank = ranks[key];
        let name = crate::net::wire::from_latin1(key);
        let label_lc = match name.find(':') {
            Some(i) => name[i..].to_ascii_lowercase(),
            None => String::new(),
        };
        let iorder = irank[key];
        c.db().npc_event().insert(NpcEvent { name_lc: name.to_ascii_lowercase(), name, npc_id, pos, order: rank, iorder, seq, label_lc });
    }
    log::info!("script: {compiled} NPC scripts compiled, {} events", ev_db.len());
}

/// The warp NPC type constant, re-exported for the touch code.
pub const WARP: u8 = NPCTYPE_WARP;

#[cfg(test)]
mod tests {
    use super::*;

    /// ev_db_ranks: sorting the keys by their bucket-encoded rank gives db_obj_vforeach's order, after puts and removes.
    #[test]
    fn ev_db_ranks_follow_iteration_order() {
        let mut db: StrDb<()> = StrDb::new(EVENT_NAME_LENGTH as u16, false);
        let keys: Vec<String> = (0..600).map(|i| format!("npc{}::On{}", i % 97, i)).collect();
        for k in &keys {
            db.put(k.as_bytes(), ());
        }
        for k in keys.iter().step_by(7) {
            db.remove(k.as_bytes());
        }
        db.put(b"dup_1_110000001::OnInstanceInit", ());
        let ranks = ev_db_ranks(&db);
        let mut by_rank: Vec<(u32, Vec<u8>)> = ranks.iter().map(|(k, &r)| (r, k.clone())).collect();
        by_rank.sort();
        let order: Vec<Vec<u8>> = db.iter_order().into_iter().map(|(k, _)| k.to_vec()).collect();
        assert_eq!(by_rank.into_iter().map(|(_, k)| k).collect::<Vec<_>>(), order);
    }

    /// npc_read_event_script's lists: ranking just the keys (bucket, then key) gives the DBIterator order the tree has,
    /// whatever the insertion history shaped it into.
    #[test]
    fn event_lists_follow_the_iterator_order() {
        let mut db: StrDb<()> = StrDb::new(EVENT_NAME_LENGTH as u16, false);
        let keys: Vec<String> = (0..900).map(|i| format!("{}npc{}::{}", if i % 3 == 0 { "Z" } else { "a" }, i % 131, NPCE_NAMES[i % NPCE_NAMES.len()])).collect();
        for k in &keys {
            db.put(k.as_bytes(), ());
        }
        for k in keys.iter().step_by(5) {
            db.remove(k.as_bytes());
        }
        db.put(b"1_12_150_150::OnPCLoginEvent", ());
        let want: Vec<Vec<u8>> = db.iter_inorder().into_iter().map(|(k, _)| k.to_vec()).collect();
        let mut live: Vec<Vec<u8>> = want.clone();
        live.reverse(); // any order in
        let got: Vec<Vec<u8>> = ev_db_iterator_order(&live).into_iter().map(|i| live[i].clone()).collect();
        assert_eq!(got, want);
    }
}
