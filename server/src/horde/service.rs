//! Horde around the engine, on the module: what tools/lokihost/Core/HordeService.cs does in Loki Host's relay
//! (invitations to everyone linked, the lobby, a late join, a lost link, the pause, snapshots, results, the reward
//! decision), served as wave H's presence is (presence.rs): ONE reducer takes the Horde wire's client messages
//! ([`horde`]: one or more frames, each `u16 length + payload`, exactly what the client's HordeClient hands its
//! transport), what a client is sent are `horde_out` rows (an event table: broadcast, never stored; each client reads
//! its own rows only; one row per client per reducer call, its payloads framed the same way and in order), and a
//! scheduled reducer ([`horde_step`]) exists only while a run is gathering or being played.
//! Loki's own, outside the RO protocol; nothing here is rAthena's. HordeService.cs is the specification of the rules
//! (cited by its line numbers); what differs because this is a database and not a relay thread is said where it is.
//!
//! WHERE THE RUN LIVES. The simulation (sim.rs) is held in the module instance's memory between two scheduled calls:
//! serialising up to 400 enemies, 400 pickups and 600 effects into a row forty times a second would write megabytes a
//! second into the commit log. So that a rolled-back call or a new module instance can never play on a stale copy
//! (the rule of map_cell.rs), the `horde_run` row counts the steps taken and the copy in memory must agree with it at
//! the start of every call; when it does not (the instance was replaced: a publish, a crash) the run is over as
//! "Stopped" with the results the row last kept (at most a second old), and the rewards are decided from those.
//! Everything a client asks that touches the simulation (a stick input, a choice, a late join, leaving) is a
//! `horde_op` row that the scheduled call applies in order: only that call ever touches the copy in memory.

use std::cell::RefCell;
use std::collections::HashMap;

use spacetimedb::{client_visibility_filter, reducer, table, Filter, Identity, ReducerContext, ScheduleAt, Table, TimeDuration};

use super::content::{self, Content};
use super::math::TICK_RATE;
use super::sim::{outcome, Sim, MAX_SHARE};
use super::wire::{self, ClientMsg, SnapshotEncoder};
use crate::npc::mapflag as _;
use crate::pc::pc as _;
use crate::unit::unit as _;
use crate::world::Ctx;

/// HordePhase.
const IDLE: u8 = 0;
const GATHERING: u8 = 1;
const RUNNING: u8 = 2;
/// HordeMemberState.
const ASKED: u8 = 0;
const JOINED: u8 = 1;
const DECLINED: u8 = 2;
const LEFT: u8 = 3;

/// Real milliseconds per step of the simulation: 25 (HordeService.TickMs: 1000 / HordeSim.TickRate).
const STEP_MS: i64 = 1000 / TICK_RATE as i64;
/// The scheduled reducer's period while a run is played: one call makes the steps that are due (two, as a rule) and
/// one snapshot (HordeService.SnapshotEvery 2: 20 snapshots a second), instead of forty calls a second.
const CALL_MS: i64 = 50;
/// Its period while the invitation's countdown runs (the deadline and "everyone answered" are looked at).
const GATHER_MS: i64 = 250;
/// HordeService.SnapshotEvery.
const SNAPSHOT_EVERY: i32 = 2;
/// HordeSettings.AwaySeconds: a player whose link dropped keeps its place this long (it stands still meanwhile).
const AWAY_MS: i64 = 60_000;
/// HordeSettings.InviteSeconds when the owner set nothing.
const DEFAULT_INVITE_SECONDS: u32 = 30;
/// HostSettings.HordeRunMinutes / HordeReward when the owner set nothing (15 minutes, Normal).
const DEFAULT_RUN_MINUTES: u32 = 15;
const DEFAULT_REWARD: u8 = 2;
/// A run whose row was not touched by its scheduled call for this long is taken for dead when someone asks for a new one.
const STALE_MS: i64 = 15_000;
/// The relay's limit (PoseRelay: 120 Horde messages a second per connection; a player sends 40 inputs a second).
const MAX_MSGS_PER_SECOND: u32 = 120;
/// messages taken from one reducer call (a client batches what one of its frames produced).
const MAX_MSGS_PER_CALL: usize = 16;
/// HordeService.MaxRunPlayers: at most 4 players in a run.
const MAX_RUN_PLAYERS: usize = 4;

/// The clients linked to Horde (they said H_HELLO while their character was in the world). Private.
#[table(accessor = horde_peer)]
#[derive(Clone)]
pub struct HordePeer {
    #[primary_key]
    pub identity: Identity,
    #[unique]
    pub aid: u32,
    pub char_id: u32,
    /// The character's name (the world's, never the client's word).
    pub name: String,
    /// H_HELLO's sex byte (1 male, 2 female, 0 not known): it filters the jobs a run offers (JobDef.Sex).
    pub sex: u8,
    /// Its protocol version and tables are this module's: it can play here.
    pub ok: bool,
    pub protocol: u8,
    /// Its next snapshot must be a key (it joined, came back, or was just sent the run).
    pub need_key: bool,
}

/// What one client is sent by one reducer call: one or more Horde wire payloads (relay → client, 0x90..0x9F), in
/// order, each as a frame (`u16 length + payload`). An event table: rows are broadcast, never stored.
#[table(accessor = horde_out, public, event)]
pub struct HordeOut {
    pub target: Identity,
    pub data: Vec<u8>,
}

/// Each client receives only its own rows.
#[client_visibility_filter]
const HORDE_OUT_VISIBILITY: Filter = Filter::Sql("SELECT * FROM horde_out WHERE horde_out.target = :sender");

/// The run that is gathering or being played (one row, id 0; none = no run). Private.
#[table(accessor = horde_run)]
#[derive(Clone)]
pub struct HordeRun {
    #[primary_key]
    pub id: u8,
    pub run_id: u32,
    pub phase: u8,
    pub starter_aid: u32,
    pub starter_name: String,
    /// Gathering: when the run begins at the latest.
    pub deadline_ms: i64,
    pub arena_id: i32,
    pub seed: u64,
    pub run_seconds: f32,
    /// HordeRewardSize of this run (the owner's setting when it was started).
    pub reward: u8,
    /// Running: the clock time of step 0, and the steps taken (the copy in memory must have taken as many).
    pub started_ms: i64,
    pub steps_done: i64,
    pub last_status_ms: i64,
    /// The results as of the last status (an H_RESULTS payload for slot -1): what is left of the run if its copy is lost.
    pub results: Vec<u8>,
    /// The `horde_tick` row that drives it.
    pub schedule_id: u64,
}

/// Everyone asked, with their answer (HordeService._members). Private.
#[table(accessor = horde_member)]
#[derive(Clone)]
pub struct HordeMember {
    #[primary_key]
    pub aid: u32,
    /// The order they were added in (the status lists them so).
    pub seq: u32,
    pub char_id: u32,
    pub name: String,
    pub state: u8,
    pub slot: i32,
    /// -1, or when its link dropped.
    pub away_since_ms: i64,
    /// As of the last status (the status and the lost-run case read them).
    pub level: i32,
    pub job_id: i32,
    pub fallen: bool,
}

/// What clients asked of the running simulation, in order, until the scheduled call applies it. Private.
#[table(accessor = horde_op)]
pub struct HordeOp {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub kind: u8,
    pub aid: u32,
    pub a: i32,
    pub b: i32,
    pub c: i32,
}

const OP_INPUT: u8 = 1;
const OP_CHOOSE: u8 = 2;
const OP_JOIN: u8 = 3;
const OP_LEAVE: u8 = 4;
const OP_AWAY: u8 = 5;
const OP_BACK: u8 = 6;
const OP_STOP: u8 = 7;
/// Test databases only ([`horde_test_hold`]): the field is kept at `a` enemies.
const OP_HOLD: u8 = 8;
const OP_PAUSE: u8 = 9;
const OP_CAST: u8 = 10;

/// The scheduled call (one row while a run exists).
#[table(accessor = horde_tick, scheduled(horde_step))]
pub struct HordeTick {
    #[primary_key]
    #[auto_inc]
    pub scheduled_id: u64,
    pub scheduled_at: ScheduleAt,
}

/// The owner's switch and settings (private; one row, id 0; no row = the defaults): Loki Host's window has the same
/// four (HostSettings.HordeEnabled / HordeRunMinutes / HordeReward / HordeArena), the standalone relay the fifth
/// (--horde-invite). Written by [`horde_settings`] (or with SQL by the owner).
#[table(accessor = horde_config)]
#[derive(Clone)]
pub struct HordeConfig {
    #[primary_key]
    pub id: u32,
    pub enabled: bool,
    /// 1..120 (Loki Host offers 5 / 10 / 15 / 20 / 30).
    pub run_minutes: u32,
    /// HordeRewardSize: 0 off, 1 low, 2 normal, 3 high.
    pub reward: u8,
    /// 0 = a random arena of the tables; else that ArenaDef id.
    pub arena: i32,
    /// The invitation's countdown (30).
    pub invite_seconds: u32,
}

/// Run counter, the last run's line (Loki Host's "Last:" line) and the measurements of the service (private; id 0).
#[table(accessor = horde_state)]
#[derive(Clone, Default)]
pub struct HordeState {
    #[primary_key]
    pub id: u8,
    pub last_run_id: u32,
    pub last_run: String,
    pub runs: u32,
    /// Since the database was published: steps of the simulation, snapshots made, their bodies' bytes, keys, runs lost.
    pub steps: u64,
    pub snapshots: u64,
    pub snapshot_bytes: u64,
    pub keys: u64,
    pub lost_runs: u32,
    /// The run being played, as of its last status (Loki Host's "Horde" box: time, monsters): 0 when none.
    pub run_seconds: u32,
    pub enemies: u32,
}

/// A reward decision: one row per player with a share, granted ONCE through the module's own pc_gainexp (reward.rs).
#[table(accessor = horde_reward, index(accessor = by_char, btree(columns = [char_id])))]
#[derive(Clone)]
pub struct HordeReward {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub run_id: u32,
    pub account_id: u32,
    pub char_id: u32,
    pub char_name: String,
    /// 1/100 % of the next base / job level.
    pub base_share: u16,
    pub job_share: u16,
    pub created_ms: i64,
    /// 0 waiting, 1 granted, 2 granted and told to the player's client.
    pub granted: u8,
    pub granted_ms: i64,
    /// What was handed to pc_gainexp.
    pub base_exp: i64,
    pub job_exp: i64,
}

/// The run in this module instance's memory.
struct Live {
    run_id: u32,
    steps: i64,
    sim: Sim,
    enc: SnapshotEncoder,
    tick_in_run: i32,
    /// Test databases only: the number of enemies the field is topped up to before every step, and that dice's state.
    hold: i32,
    hold_rng: u32,
}

thread_local! {
    static LIVE: RefCell<Option<Live>> = const { RefCell::new(None) };
    static WINDOWS: RefCell<HashMap<Identity, (i64, u32)>> = RefCell::new(HashMap::new());
    static OUTBOX: RefCell<Vec<(Identity, Vec<u8>)>> = const { RefCell::new(Vec::new()) };
}

type Db = spacetimedb::Local;

fn now_ms(rc: &ReducerContext) -> i64 {
    rc.timestamp.to_micros_since_unix_epoch() / 1000
}

/// One second's messages of a client (the relay's rate limit), in the instance's memory as presence.rs keeps its own.
fn within_rate(who: Identity, now: i64) -> bool {
    WINDOWS.with(|w| {
        let mut w = w.borrow_mut();
        let e = w.entry(who).or_insert((now, 0));
        if now - e.0 >= 1000 || now < e.0 {
            *e = (now, 0);
        }
        e.1 += 1;
        e.1 <= MAX_MSGS_PER_SECOND
    })
}

fn config(db: &Db) -> HordeConfig {
    db.horde_config().id().find(0).unwrap_or(HordeConfig { id: 0, enabled: true, run_minutes: DEFAULT_RUN_MINUTES, reward: DEFAULT_REWARD, arena: 0, invite_seconds: DEFAULT_INVITE_SECONDS })
}

fn state(db: &Db) -> HordeState {
    db.horde_state().id().find(0).unwrap_or_default()
}

fn put_state(db: &Db, s: HordeState) {
    if db.horde_state().id().find(0).is_some() {
        db.horde_state().id().update(s);
    } else {
        db.horde_state().insert(s);
    }
}

/// Queues one payload for a client. What a reducer call sends one client goes out as ONE `horde_out` row, the payloads
/// in the order they were sent, each as a frame (`u16 length + payload`): the rows of one transaction reach a client in
/// no order it could rely on (a run's H_RUN must come before its first snapshot, a snapshot before the next).
fn send(_db: &Db, target: Identity, data: Vec<u8>) {
    if data.is_empty() || data.len() > wire::MAX_PAYLOAD {
        return;
    }
    OUTBOX.with(|o| {
        let mut o = o.borrow_mut();
        let buf = match o.iter().position(|(t, _)| *t == target) {
            Some(i) => &mut o[i].1,
            None => {
                o.push((target, Vec::with_capacity(data.len() + 2)));
                &mut o.last_mut().unwrap().1
            }
        };
        buf.extend_from_slice(&(data.len() as u16).to_le_bytes());
        buf.extend_from_slice(&data);
    });
}

/// A reducer call begins: nothing is queued (a call that failed part-way may have left its messages behind).
pub(super) fn outbox_begin() {
    OUTBOX.with(|o| o.borrow_mut().clear());
}

/// A reducer call ends: one row per client.
pub(super) fn outbox_flush(db: &Db) {
    for (target, data) in OUTBOX.with(|o| std::mem::take(&mut *o.borrow_mut())) {
        db.horde_out().insert(HordeOut { target, data });
    }
}

pub(super) fn send_to(db: &Db, target: Identity, data: Vec<u8>) {
    send(db, target, data);
}

fn send_aid(db: &Db, aid: u32, data: Vec<u8>) {
    if let Some(p) = db.horde_peer().aid().find(aid) {
        send(db, p.identity, data);
    }
}

fn who_text(name: &str, aid: u32) -> String {
    format!("{name} (AID {aid})")
}

/// The server's town maps (its "town" map flags), for the prompt's warning: HordeNpc.Towns reads the same flags from
/// the copy's npc/mapflag/town.txt; here they are the world's own rows.
fn towns(db: &Db) -> Vec<String> {
    let maps = crate::data::maps();
    let mut v: Vec<String> = db
        .mapflag()
        .iter()
        .filter(|f| f.flag == crate::npc::MF_TOWN && f.value != 0)
        .filter_map(|f| maps.list.get(f.map as usize).filter(|m| m.id == f.map && m.instance_src().is_none()).map(|m| m.name.clone()))
        .collect();
    v.sort_by_key(|s| s.to_ascii_lowercase());
    v.dedup();
    v
}

fn members(db: &Db) -> Vec<HordeMember> {
    let mut v: Vec<HordeMember> = db.horde_member().iter().collect();
    v.sort_by_key(|m| m.seq);
    v
}

fn put_member(db: &Db, m: HordeMember) {
    if db.horde_member().aid().find(m.aid).is_some() {
        db.horde_member().aid().update(m);
    } else {
        db.horde_member().insert(m);
    }
}

fn new_member(db: &Db, peer: &HordePeer, state: u8, slot: i32) -> HordeMember {
    let seq = db.horde_member().iter().map(|m| m.seq).max().map_or(0, |s| s + 1);
    HordeMember { aid: peer.aid, seq, char_id: peer.char_id, name: peer.name.clone(), state, slot, away_since_ms: -1, level: 0, job_id: 0, fallen: false }
}

/// BuildStatus (HordeService.cs:202-226).
fn build_status(db: &Db, run: Option<&HordeRun>, live: Option<&Live>, now: i64) -> wire::Status {
    let cfg = config(db);
    let c = content::game();
    let mut s = wire::Status { enabled: cfg.enabled, phase: IDLE, run_length: cfg.run_minutes.clamp(1, 120) as f32 * 60.0, reward: cfg.reward.min(3), lowest_level: 1, ..Default::default() };
    let mut peers: Vec<HordePeer> = db.horde_peer().iter().filter(|p| p.ok).collect();
    peers.sort_by_key(|p| p.aid);
    s.online = peers.into_iter().map(|p| wire::OnlinePlayer { aid: p.aid, name: p.name }).collect();
    let Some(run) = run else { return s };
    s.phase = run.phase;
    s.run_id = run.run_id;
    s.starter_aid = run.starter_aid;
    s.starter_name = run.starter_name.clone();
    s.seconds_left = if run.phase == GATHERING { 0f32.max((run.deadline_ms - now) as f32 / 1000.0) } else { 0.0 };
    s.run_seconds = live.map_or(0.0, |l| l.sim.tick as f32 / TICK_RATE as f32);
    s.run_length = run.run_seconds;
    s.arena_map = c.arena(run.arena_id).map(|a| c.arenas[a].map.clone()).unwrap_or_default();
    s.reward = run.reward;
    let mut lowest = i32::MAX;
    for m in members(db) {
        let mut wm = wire::Member { aid: m.aid, name: m.name.clone(), state: m.state, slot: m.slot, ..Default::default() };
        if let Some(p) = live.and_then(|l| l.sim.player_idx(m.slot).map(|i| &l.sim.players[i])) {
            wm.level = p.level;
            wm.job_id = p.job_id;
            wm.fallen = p.fallen;
            lowest = lowest.min(p.level);
        }
        s.members.push(wm);
    }
    if lowest != i32::MAX {
        s.lowest_level = lowest;
    }
    s
}

fn send_status_to_peer(db: &Db, peer: &HordePeer, st: &wire::Status) {
    let ver = if (2..=wire::VERSION).contains(&peer.protocol) { peer.protocol } else { wire::VERSION };
    send(db, peer.identity, wire::encode_status_with_version(st, peer.ok, ver));
}

/// BroadcastStatus (HordeService.cs:228-238): to every linked client, with its own "can play here" bit.
fn broadcast_status(db: &Db, run: Option<&HordeRun>, live: Option<&Live>, now: i64) {
    let st = build_status(db, run, live, now);
    for p in db.horde_peer().iter() {
        send_status_to_peer(db, &p, &st);
    }
}

fn schedule(db: &Db, period_ms: i64) -> u64 {
    db.horde_tick().insert(HordeTick { scheduled_id: 0, scheduled_at: ScheduleAt::Interval(TimeDuration::from_micros(period_ms * 1000)) }).scheduled_id
}

// ------------------------------------------------------------------------------------------- the clients' reducer

/// One or more Horde client messages (HordeWire.cs, client → relay 0x10..0x1F), each as the client's HordeClient hands
/// it to its transport: `u16 length + payload`. Anything malformed, untimely or over the rate is dropped without an
/// error (HordeService never closes a connection for it; a failed reducer here would tell the sender nothing it can use).
#[reducer]
pub fn horde(rc: &ReducerContext, data: Vec<u8>) {
    if crate::release::reject_if_outdated_package(rc) {
        return;
    }
    // the handshake rule of every client reducer (release.rs)
    if crate::release::check_client(rc).is_err() {
        return;
    }
    let who = rc.sender();
    let now = now_ms(rc);
    outbox_begin();
    let mut o = 0;
    for _ in 0..MAX_MSGS_PER_CALL {
        if o + 2 > data.len() {
            break;
        }
        let len = u16::from_le_bytes([data[o], data[o + 1]]) as usize;
        o += 2;
        if len == 0 || o + len > data.len() {
            break;
        }
        let payload = &data[o..o + len];
        o += len;
        if !within_rate(who, now) {
            continue;
        }
        if let Some(msg) = wire::decode_client(payload) {
            handle(rc, who, now, msg);
        }
    }
    outbox_flush(&rc.db);
}

/// Drain's switch (HordeService.cs:240-264).
fn handle(rc: &ReducerContext, who: Identity, now: i64, msg: ClientMsg) {
    let db = &rc.db;
    if let ClientMsg::Hello { version, sex, stamp } = msg {
        hello(rc, who, now, version, sex, &stamp);
        return;
    }
    let Some(peer) = db.horde_peer().identity().find(who) else { return }; // nothing before H_HELLO
    match msg {
        ClientMsg::Start { invite } => start_request(rc, &peer, now, invite),
        ClientMsg::Answer { run_id, join } => answer(rc, &peer, now, run_id, join),
        ClientMsg::Input { seq, dx, dy } => {
            if let Some(slot) = playing_slot(db, peer.aid) {
                db.horde_op().insert(HordeOp { id: 0, kind: OP_INPUT, aid: peer.aid, a: slot, b: seq as i32, c: (dx & 0xff) | ((dy & 0xff) << 8) });
            }
        }
        ClientMsg::Choose { offer_id, index } => {
            if let Some(slot) = playing_slot(db, peer.aid) {
                db.horde_op().insert(HordeOp { id: 0, kind: OP_CHOOSE, aid: peer.aid, a: slot, b: offer_id, c: index });
            }
        }
        ClientMsg::Leave => leave(rc, &peer, now),
        ClientMsg::End { run_id } => end_request(rc, &peer, now, run_id),
        ClientMsg::Pause { pause } => {
            if let Some(slot) = playing_slot(db, peer.aid) {
                db.horde_op().insert(HordeOp { id: 0, kind: OP_PAUSE, aid: peer.aid, a: slot, b: i32::from(pause), c: 0 });
            }
        }
        ClientMsg::Cast { weapon_id, target_x, target_y } => {
            if let Some(slot) = playing_slot(db, peer.aid) {
                db.horde_op().insert(HordeOp { id: 0, kind: OP_CAST, aid: peer.aid, a: (slot & 0xff) | ((weapon_id & 0xffff) << 8), b: target_x, c: target_y });
            }
        }
        ClientMsg::Hello { .. } => {}
    }
}

/// The slot of a player who is in the run being played (`_sim != null && member Joined && Slot >= 0`).
fn playing_slot(db: &Db, aid: u32) -> Option<i32> {
    let run = db.horde_run().id().find(0)?;
    if run.phase != RUNNING {
        return None;
    }
    let m = db.horde_member().aid().find(aid)?;
    if m.state == JOINED && m.slot >= 0 { Some(m.slot) } else { None }
}

/// Hello (HordeService.cs:266-286). The caller's character must be in the world with its client (the rule of
/// presence.rs: the name and the account are the world's rows'); a client says H_HELLO again until it is answered.
fn hello(rc: &ReducerContext, who: Identity, now: i64, version: u8, sex: u8, stamp: &str) {
    let db = &rc.db;
    let Some(p) = db.pc().identity().find(who) else { return };
    if p.disconnected {
        return;
    }
    let Some(u) = db.unit().id().find(p.account_id) else { return };
    let c = content::game();
    // Accept client protocol versions (2..=4) and table stamps without blocking the player with OtherProtocol/OtherVersion.
    let ok = true;
    db.horde_peer().identity().delete(who);
    db.horde_peer().aid().delete(p.account_id);
    let peer = HordePeer { identity: who, aid: p.account_id, char_id: p.char_id, name: u.name.clone(), sex, ok, protocol: version, need_key: true };
    db.horde_peer().insert(peer.clone());
    send(db, who, wire::encode_towns(&towns(db)));
    let run = db.horde_run().id().find(0);
    // (the status of a run being played says its seconds and levels from the member rows' last status: the copy in
    // memory is the scheduled call's alone)
    let st = status_from_rows(db, run.as_ref(), now);
    send_status_to_peer(db, &peer, &st);
    if run.is_none() {
        for other in db.horde_peer().iter() {
            if other.identity != who {
                send_status_to_peer(db, &other, &st);
            }
        }
    }
    let them = who_text(&peer.name, peer.aid);
    if version != wire::VERSION {
        log::info!("horde: {them} connected with protocol {version} (server protocol {})", wire::VERSION);
    } else if stamp != c.stamp_cs {
        log::info!("horde: {them} connected with tables {stamp} (server tables {})", c.stamp_cs);
    }
    // rewards granted while this client was not linked (reward.rs): told now
    super::reward::tell_waiting(db, &peer);
    // back after a dropped link: its place in the run is still there (the scheduled call sends it the run again)
    if ok && run.as_ref().is_some_and(|r| r.phase == RUNNING) && playing_slot(db, peer.aid).is_some() {
        db.horde_op().insert(HordeOp { id: 0, kind: OP_BACK, aid: peer.aid, a: 0, b: 0, c: 0 });
    }
}

/// The status as the rows have it (for a reducer that must not touch the copy in memory).
fn status_from_rows(db: &Db, run: Option<&HordeRun>, now: i64) -> wire::Status {
    let mut s = build_status(db, run, None, now);
    if let Some(run) = run.filter(|r| r.phase == RUNNING) {
        s.run_seconds = (run.steps_done * STEP_MS) as f32 / 1000.0; // (the pauses included: the next broadcast says the played seconds)
        let mut lowest = i32::MAX;
        let rows = members(db);
        for (wm, m) in s.members.iter_mut().zip(rows.iter()) {
            if m.slot >= 0 && m.state == JOINED {
                wm.level = m.level;
                wm.job_id = m.job_id;
                wm.fallen = m.fallen;
                if m.level > 0 {
                    lowest = lowest.min(m.level);
                }
            }
        }
        if lowest != i32::MAX {
            s.lowest_level = lowest;
        }
    }
    s
}

/// StartRequest (HordeService.cs:322-369): "Start a run": everyone else who is linked and can play (or the selected
/// targets list, capped at MAX_RUN_PLAYERS) is invited. When 0 others are asked, the run begins immediately.
fn start_request(rc: &ReducerContext, peer: &HordePeer, now: i64, targets: Option<Vec<u32>>) {
    let db = &rc.db;
    let cfg = config(db);
    let mut run = db.horde_run().id().find(0);
    // a run whose scheduled call stopped coming (it says the status every second) must not hold the mode for ever: it is
    // over as a lost one is (timer.rs met a module update that left a scheduled row unscheduled)
    if let Some(r) = run.clone().filter(|r| now - r.last_status_ms > STALE_MS) {
        log::warn!("horde: run {} was not stepped for {} s: its scheduled call is gone", r.run_id, (now - r.last_status_ms) / 1000);
        LIVE.with(|l| *l.borrow_mut() = None);
        lost(rc, r, now);
        run = None;
    }
    if !cfg.enabled || !peer.ok || run.is_some() {
        send_status_to_peer(db, peer, &status_from_rows(db, run.as_ref(), now));
        return;
    }
    let c = content::game();
    if c.arenas.is_empty() {
        log::info!("horde: {} asked for a run, but the tables hold no arena", peer.name);
        return;
    }
    let mut st = state(db);
    st.last_run_id += 1;
    let run_id = st.last_run_id;
    put_state(db, st);
    let seed = rc.timestamp.to_micros_since_unix_epoch() as u64 ^ ((run_id as u64) << 32);
    let arena = match c.arena(cfg.arena).filter(|_| cfg.arena != 0) {
        Some(a) => &c.arenas[a],
        None if cfg.arena != 0 => &c.arenas[0],
        None => &c.arenas[(seed % c.arenas.len() as u64) as usize],
    };
    let invite_seconds = cfg.invite_seconds.clamp(1, 600);
    let run_seconds = cfg.run_minutes.clamp(1, 120) as f32 * 60.0;
    for m in db.horde_member().iter() {
        db.horde_member().aid().delete(m.aid);
    }
    for op in db.horde_op().iter() {
        db.horde_op().id().delete(op.id);
    }
    db.horde_member().insert(new_member(db, peer, JOINED, -1));
    let invite = wire::encode_invite(run_id, peer.aid, &peer.name, invite_seconds as f32, &arena.map, run_seconds, cfg.reward.min(3));
    let mut asked = 0usize;
    let mut others: Vec<HordePeer> = db.horde_peer().iter().collect();
    others.sort_by_key(|p| p.aid);
    for other in others {
        if other.identity == peer.identity || !other.ok {
            continue;
        }
        if let Some(ref list) = targets {
            if !list.contains(&other.aid) {
                continue;
            }
        }
        if 1 + asked >= MAX_RUN_PLAYERS {
            break;
        }
        db.horde_member().insert(new_member(db, &other, ASKED, -1));
        send(db, other.identity, invite.clone());
        asked += 1;
    }
    for t in db.horde_tick().iter() {
        db.horde_tick().scheduled_id().delete(t.scheduled_id);
    }
    let run = HordeRun {
        id: 0, run_id, phase: GATHERING, starter_aid: peer.aid, starter_name: peer.name.clone(), deadline_ms: now + invite_seconds as i64 * 1000, arena_id: arena.id, seed, run_seconds,
        reward: cfg.reward.min(3), started_ms: 0, steps_done: 0, last_status_ms: now, results: Vec::new(), schedule_id: schedule(db, GATHER_MS),
    };
    log::info!(
        "horde: {} is starting Horde run {run_id} on {} ({},{})-({},{}); {asked} other player(s) asked; it begins in {invite_seconds} s or when all answered",
        who_text(&peer.name, peer.aid), arena.map, arena.x0, arena.y0, arena.x1, arena.y1
    );
    db.horde_run().insert(run.clone());
    if asked == 0 {
        begin_run(rc, run, now);
    } else {
        broadcast_status(db, Some(&run), None, now);
    }
}

/// Answer (HordeService.cs:371-395).
fn answer(rc: &ReducerContext, peer: &HordePeer, now: i64, run_id: u32, join: bool) {
    let db = &rc.db;
    let Some(run) = db.horde_run().id().find(0) else { return };
    if run_id != run.run_id || !peer.ok {
        return;
    }
    if run.phase == GATHERING {
        if join && db.horde_member().iter().filter(|m| m.aid != peer.aid && m.state == JOINED).count() >= MAX_RUN_PLAYERS {
            return;
        }
        let mut m = db.horde_member().aid().find(peer.aid).unwrap_or_else(|| new_member(db, peer, ASKED, -1));
        m.state = if join { JOINED } else { DECLINED };
        put_member(db, m);
        log::info!("horde: {} {} run {}", who_text(&peer.name, peer.aid), if join { "joins" } else { "declines" }, run.run_id);
        broadcast_status(db, Some(&run), None, now);
    } else if run.phase == RUNNING && join {
        // a late join: the scheduled call puts the player into the simulation
        db.horde_op().insert(HordeOp { id: 0, kind: OP_JOIN, aid: peer.aid, a: 0, b: 0, c: 0 });
    }
}

/// Leave (HordeService.cs:445-456).
fn leave(rc: &ReducerContext, peer: &HordePeer, now: i64) {
    let db = &rc.db;
    let Some(run) = db.horde_run().id().find(0) else { return };
    if run.phase == GATHERING {
        if let Some(mut m) = db.horde_member().aid().find(peer.aid) {
            m.state = DECLINED;
            db.horde_member().aid().update(m);
            broadcast_status(db, Some(&run), None, now);
        }
        return;
    }
    if playing_slot(db, peer.aid).is_some() {
        db.horde_op().insert(HordeOp { id: 0, kind: OP_LEAVE, aid: peer.aid, a: 0, b: 0, c: 0 });
    }
}

/// EndRequest (HordeService.cs:458-471): "End run for everyone": any joined player (or the starter while gathering)
/// ends the run as Stopped so everyone receives the EXP earned so far.
fn end_request(rc: &ReducerContext, peer: &HordePeer, now: i64, run_id: u32) {
    let db = &rc.db;
    let Some(run) = db.horde_run().id().find(0) else { return };
    if run.run_id != run_id {
        return;
    }
    if run.phase == GATHERING {
        if peer.aid != run.starter_aid && !db.horde_member().aid().find(peer.aid).is_some_and(|m| m.state == JOINED) {
            return;
        }
        log::info!("horde: {} cancelled Horde run {} while gathering", who_text(&peer.name, peer.aid), run.run_id);
        end_run_rows(db, &run);
        broadcast_status(db, None, None, now);
        return;
    }
    if playing_slot(db, peer.aid).is_some() {
        log::info!("horde: {} ended Horde run {} for everyone", who_text(&peer.name, peer.aid), run.run_id);
        db.horde_op().insert(HordeOp { id: 0, kind: OP_STOP, aid: peer.aid, a: 0, b: 0, c: 0 });
    }
}

/// World hook (pc.rs on_client_gone: the connection closed, or the client left for the character screen):
/// PeerGone (HordeService.cs:288-296): the link is gone; a player in a run keeps its place for a while.
pub fn on_client_gone(c: &Ctx, who: Identity) {
    let db = c.db();
    let Some(peer) = db.horde_peer().identity().find(who) else { return };
    db.horde_peer().identity().delete(who);
    WINDOWS.with(|w| w.borrow_mut().remove(&who));
    if playing_slot(db, peer.aid).is_some() {
        db.horde_op().insert(HordeOp { id: 0, kind: OP_AWAY, aid: peer.aid, a: 0, b: 0, c: 0 });
    }
}

// ------------------------------------------------------------------------------------------- the scheduled call

/// The run's clock (HordeService.Loop, HordeService.cs:160-196). Only the scheduler calls it (a scheduled reducer is
/// an ordinary reducer any client may call: timer.rs on_wakeup).
#[reducer]
pub fn horde_step(rc: &ReducerContext, row: HordeTick) -> Result<(), String> {
    if rc.sender() != rc.database_identity() {
        return Err("horde_step is called by the scheduler only.".into());
    }
    let db = &rc.db;
    let now = now_ms(rc);
    let Some(run) = db.horde_run().id().find(0).filter(|r| r.schedule_id == row.scheduled_id) else {
        // no run (or another call drives it): this row has nothing to do
        db.horde_tick().scheduled_id().delete(row.scheduled_id);
        return Ok(());
    };
    outbox_begin();
    match run.phase {
        GATHERING => {
            if now >= run.deadline_ms || everyone_answered(db) {
                begin_run(rc, run, now);
            } else if now - run.last_status_ms >= 1000 {
                let run = HordeRun { last_status_ms: now, ..run };
                broadcast_status(db, Some(&run), None, now);
                db.horde_run().id().update(run);
            }
        }
        RUNNING => step_run(rc, run, now),
        _ => {}
    }
    outbox_flush(db);
    Ok(())
}

/// EveryoneAnswered (HordeService.cs:305-309).
fn everyone_answered(db: &Db) -> bool {
    !db.horde_member().iter().any(|m| m.state == ASKED && db.horde_peer().aid().find(m.aid).is_some())
}

fn run_info(c: &Content, a: &content::ArenaDef, run: &HordeRun, slot: i32, late: bool) -> wire::RunInfo {
    wire::RunInfo {
        run_id: run.run_id, slot, arena_id: a.id, map: a.map.clone(), x0: a.x0, y0: a.y0, x1: a.x1, y1: a.y1, snapshot_every: SNAPSHOT_EVERY, run_length: run.run_seconds,
        final_boss_seconds: c.rules.final_boss_seconds, late,
    }
}

/// SendRun (HordeService.cs:359-369).
fn send_run(db: &Db, c: &Content, a: &content::ArenaDef, run: &HordeRun, aid: u32, slot: i32, late: bool, tick: i32) {
    if let Some(mut p) = db.horde_peer().aid().find(aid) {
        send(db, p.identity, wire::encode_run(&run_info(c, a, run, slot, late), tick));
        p.need_key = true;
        db.horde_peer().identity().update(p);
    }
}

fn end_run_rows(db: &Db, run: &HordeRun) {
    db.horde_run().id().delete(0);
    for m in db.horde_member().iter() {
        db.horde_member().aid().delete(m.aid);
    }
    for op in db.horde_op().iter() {
        db.horde_op().id().delete(op.id);
    }
    db.horde_tick().scheduled_id().delete(run.schedule_id);
}

/// BeginRun (HordeService.cs:371-387).
fn begin_run(rc: &ReducerContext, mut run: HordeRun, now: i64) {
    let db = &rc.db;
    let c = content::game();
    let joined: Vec<(HordeMember, HordePeer)> = members(db).into_iter().filter(|m| m.state == JOINED).filter_map(|m| db.horde_peer().aid().find(m.aid).map(|p| (m, p))).collect();
    if joined.is_empty() {
        log::info!("horde: run {}: nobody joined (or everyone who did is gone): no run", run.run_id);
        end_run_rows(db, &run);
        broadcast_status(db, None, None, now);
        return;
    }
    let arena = c.arenas[c.arena(run.arena_id).unwrap_or(0)].scaled_for_party(joined.len());
    let sim = match Sim::new(c, &arena, run.seed, run.run_seconds) {
        Ok(s) => s,
        Err(e) => {
            log::error!("horde: run {} cannot begin: {e}", run.run_id);
            end_run_rows(db, &run);
            broadcast_status(db, None, None, now);
            return;
        }
    };
    let mut live = Live { run_id: run.run_id, steps: 0, enc: SnapshotEncoder::new(&sim), sim, tick_in_run: 0, hold: 0, hold_rng: 12345 };
    let mut names = Vec::new();
    for (mut m, p) in joined.clone() {
        m.slot = live.sim.add_player(m.aid, &m.name, p.sex);
        m.char_id = p.char_id;
        m.level = 1;
        names.push(m.name.clone());
        db.horde_member().aid().update(m);
    }
    for mut m in db.horde_member().iter() {
        if m.state == ASKED {
            m.state = DECLINED;
            db.horde_member().aid().update(m);
        }
    }
    db.horde_tick().scheduled_id().delete(run.schedule_id);
    run.phase = RUNNING;
    run.started_ms = now;
    run.steps_done = 0;
    run.last_status_ms = now;
    run.schedule_id = schedule(db, CALL_MS);
    for m in db.horde_member().iter() {
        if m.state == JOINED && m.slot >= 0 {
            send_run(db, c, &arena, &run, m.aid, m.slot, false, live.sim.tick);
        }
    }
    log::info!("horde: run {} begins on {} ({},{})-({},{}) with {} player(s): {} (seed {})", run.run_id, arena.map, arena.x0, arena.y0, arena.x1, arena.y1, names.len(), names.join(", "), run.seed);
    broadcast_status(db, Some(&run), Some(&live), now);
    db.horde_run().id().update(run);
    LIVE.with(|l| *l.borrow_mut() = Some(live));
}

fn aid_of_slot(db: &Db, slot: i32) -> Option<u32> {
    db.horde_member().iter().find(|m| m.slot == slot && m.state == JOINED).map(|m| m.aid)
}

/// One scheduled call of a run being played: what clients asked, the steps that are due, their offers, a snapshot, the
/// status once a second, the end.
fn step_run(rc: &ReducerContext, mut run: HordeRun, now: i64) {
    let db = &rc.db;
    let c = content::game();
    // the copy in memory must be this run at this step (see the top of this file)
    let mut live = match LIVE.with(|l| l.borrow_mut().take()) {
        Some(l) if l.run_id == run.run_id && l.steps == run.steps_done => l,
        _ => {
            lost(rc, run, now);
            return;
        }
    };
    let mut status_due = now - run.last_status_ms >= 1000;
    // ---- Drain (HordeService.cs:235-257): what clients asked since the last call, in order
    let mut ops: Vec<HordeOp> = db.horde_op().iter().collect();
    ops.sort_by_key(|o| o.id);
    for op in ops {
        db.horde_op().id().delete(op.id);
        match op.kind {
            OP_INPUT => live.sim.push_input(op.a, op.b as u16, (op.c & 0xff) as u8 as i8 as i32, ((op.c >> 8) & 0xff) as u8 as i8 as i32),
            OP_CHOOSE => {
                live.sim.choose(op.a, op.b, op.c);
            }
            OP_JOIN => {
                // Answer, the late join (HordeService.cs:347-356)
                let Some(peer) = db.horde_peer().aid().find(op.aid).filter(|p| p.ok) else { continue };
                let old = db.horde_member().aid().find(op.aid);
                if old.as_ref().is_some_and(|m| m.state == JOINED && m.slot >= 0 && live.sim.player_idx(m.slot).is_some()) {
                    continue;
                }
                let them = who_text(&peer.name, peer.aid);
                if live.sim.players_present() as usize >= MAX_RUN_PLAYERS {
                    log::info!("horde: {them} could not join run {}: it already has {MAX_RUN_PLAYERS} players", run.run_id);
                    continue;
                }
                let slot = live.sim.add_player(peer.aid, &peer.name, peer.sex);
                if slot < 0 {
                    log::info!("horde: {them} could not join run {}: it is full", run.run_id);
                    continue;
                }
                let mut m = old.unwrap_or_else(|| new_member(db, &peer, JOINED, slot));
                m.state = JOINED;
                m.slot = slot;
                m.char_id = peer.char_id;
                m.name = peer.name.clone();
                m.away_since_ms = -1;
                put_member(db, m);
                send_run(db, c, &live.sim.arena, &run, peer.aid, slot, true, live.sim.tick);
                log::info!("horde: {them} joins run {} late, at level {}", run.run_id, live.sim.players[slot as usize].level);
                status_due = true;
            }
            OP_LEAVE => {
                // Leave (HordeService.cs:420-426): its results at once (no share yet: the reward is decided at the run's end)
                let Some(mut m) = db.horde_member().aid().find(op.aid).filter(|m| m.state == JOINED && m.slot >= 0) else { continue };
                live.sim.remove_player(m.slot);
                m.state = LEFT;
                let (slot, name) = (m.slot, m.name.clone());
                db.horde_member().aid().update(m);
                send_aid(db, op.aid, wire::encode_results(&live.sim.results(run.run_id, 0), slot));
                log::info!("horde: {} left the run {} after {} s (its result stands; its reward is decided at the run's end)", who_text(&name, op.aid), run.run_id, live.sim.tick / TICK_RATE);
                status_due = true;
            }
            OP_AWAY => {
                // PeerGone (HordeService.cs:284-288)
                let Some(mut m) = db.horde_member().aid().find(op.aid).filter(|m| m.state == JOINED && m.slot >= 0) else { continue };
                if db.horde_peer().aid().find(op.aid).is_some() {
                    continue; // it is linked again already
                }
                live.sim.set_away(m.slot, true);
                m.away_since_ms = now;
                log::info!("horde: {} lost its link during run {}: it keeps its place for {} s", who_text(&m.name, op.aid), run.run_id, AWAY_MS / 1000);
                db.horde_member().aid().update(m);
            }
            OP_BACK => {
                // Hello, back after a dropped link (HordeService.cs:271-278)
                let Some(mut m) = db.horde_member().aid().find(op.aid).filter(|m| m.state == JOINED && m.slot >= 0) else { continue };
                let Some(pi) = live.sim.player_idx(m.slot) else { continue };
                if db.horde_peer().aid().find(op.aid).is_none() {
                    continue;
                }
                live.sim.set_away(m.slot, false);
                m.away_since_ms = -1;
                send_run(db, c, &live.sim.arena, &run, op.aid, m.slot, false, live.sim.tick);
                if let Some(open) = &live.sim.players[pi].open {
                    send_aid(db, op.aid, wire::encode_offer(open));
                }
                log::info!("horde: {} is back in run {}", who_text(&m.name, op.aid), run.run_id);
                db.horde_member().aid().update(m);
            }
            OP_STOP => live.sim.end(outcome::STOPPED),
            OP_HOLD => live.hold = op.a.clamp(0, 300),
            OP_PAUSE => live.sim.hold(op.a, op.b != 0),
            OP_CAST => {
                let slot = op.a & 0xff;
                let weapon_id = (op.a >> 8) & 0xffff;
                live.sim.cast_active(slot, weapon_id, op.b, op.c);
            }
            _ => {}
        }
    }
    // ---- the steps that are due: one per 25 ms of the clock (HordeService.cs:166-169: far behind, no burst)
    let mut due = (now - run.started_ms) / STEP_MS - run.steps_done;
    if due > 20 {
        run.started_ms += (due - 2) * STEP_MS;
        due = 2;
    }
    let (mut snapshots, mut bytes, mut keys, mut steps) = (0u64, 0u64, 0u64, 0u64);
    for _ in 0..due.clamp(0, 4) {
        if live.sim.over {
            break;
        }
        if live.hold > 0 {
            // (HordeMeasureTests' load: the field held full, whatever the tables' pace)
            if let Some(normal) = c.enemies.iter().position(|e| e.rank == 0) {
                while live.sim.enemies_alive < live.hold {
                    let mut dice = || {
                        live.hold_rng = live.hold_rng.wrapping_mul(1664525).wrapping_add(1013904223);
                        (live.hold_rng >> 8) as i32
                    };
                    let (x, y) = (live.sim.min_x + dice() % (live.sim.max_x - live.sim.min_x), live.sim.min_y + dice() % (live.sim.max_y - live.sim.min_y));
                    if live.sim.spawn(normal, x, y, true) < 0 {
                        break;
                    }
                }
            }
        }
        live.sim.step();
        live.steps += 1;
        steps += 1;
        live.enc.collect(&live.sim);
        for (slot, offer) in &live.sim.offers_opened {
            if let Some(aid) = aid_of_slot(db, *slot) {
                send_aid(db, aid, wire::encode_offer(offer));
            }
        }
        for (slot, id) in &live.sim.offers_closed {
            if *id != 0 {
                if let Some(aid) = aid_of_slot(db, *slot) {
                    send_aid(db, aid, wire::encode_offer_closed(*id));
                }
            }
        }
        live.sim.clear_outputs();
        // CheckAway (HordeService.cs:291-303)
        for mut m in db.horde_member().iter() {
            if m.state != JOINED || m.away_since_ms < 0 || now - m.away_since_ms < AWAY_MS {
                continue;
            }
            m.away_since_ms = -1;
            if m.slot >= 0 {
                live.sim.remove_player(m.slot);
            }
            m.state = LEFT;
            log::info!("horde: {} did not come back: out of run {} (its result stands)", who_text(&m.name, m.aid), run.run_id);
            db.horde_member().aid().update(m);
            status_due = true;
        }
        live.tick_in_run += 1;
        if live.tick_in_run >= SNAPSHOT_EVERY || live.sim.over {
            live.tick_in_run = 0;
            // Snapshot (HordeService.cs:389-415): ONE delta and, when some client needs it, ONE key
            let targets: Vec<(HordeMember, HordePeer)> =
                db.horde_member().iter().filter(|m| m.state == JOINED && m.slot >= 0).filter_map(|m| db.horde_peer().aid().find(m.aid).map(|p| (m, p))).collect();
            let want_key = targets.iter().any(|(_, p)| p.need_key);
            let (delta, key) = live.enc.encode(&live.sim, want_key);
            snapshots += 1;
            bytes += delta.len() as u64;
            for (m, mut p) in targets {
                let Some(pi) = live.sim.player_idx(m.slot) else { continue };
                let body = if p.need_key { key.as_ref() } else { Some(&delta) };
                let Some(payload) = body.and_then(|b| wire::snap_payload(live.sim.players[pi].ack_seq, b)) else { continue };
                send(db, p.identity, payload);
                if p.need_key {
                    keys += 1;
                    p.need_key = false;
                    db.horde_peer().identity().update(p);
                }
            }
        }
    }
    run.steps_done = live.steps;
    if steps > 0 {
        let mut st = state(db);
        st.steps += steps;
        st.snapshots += snapshots;
        st.snapshot_bytes += bytes;
        st.keys += keys;
        if status_due {
            st.run_seconds = (live.sim.tick / TICK_RATE) as u32;
            st.enemies = live.sim.enemies_alive.max(0) as u32;
        }
        put_state(db, st);
    }
    if live.sim.over {
        finish(rc, &run, live, now);
        return;
    }
    if status_due {
        run.last_status_ms = now;
        // what is left of the run if this copy is lost: its results as of now, and each member's line of the status
        run.results = wire::encode_results(&live.sim.results(run.run_id, run.reward), -1);
        for mut m in db.horde_member().iter() {
            if let Some(pi) = live.sim.player_idx(m.slot).filter(|_| m.state == JOINED) {
                let p = &live.sim.players[pi];
                if m.level != p.level || m.job_id != p.job_id || m.fallen != p.fallen {
                    m.level = p.level;
                    m.job_id = p.job_id;
                    m.fallen = p.fallen;
                    db.horde_member().aid().update(m);
                }
            }
        }
        broadcast_status(db, Some(&run), Some(&live), now);
    }
    db.horde_run().id().update(run);
    LIVE.with(|l| *l.borrow_mut() = Some(live));
}

/// Finish (HordeService.cs:429-460): the results to everyone in the run, the reward decisions written (also for those
/// who left or are gone), the mode idle again; then the rewards are given where they can be (reward.rs).
fn finish(rc: &ReducerContext, run: &HordeRun, live: Live, now: i64) {
    let db = &rc.db;
    let c = content::game();
    let res = live.sim.results(run.run_id, run.reward);
    let mem = members(db);
    for m in &mem {
        if m.slot < 0 || m.state == DECLINED || m.state == ASKED {
            continue;
        }
        send_aid(db, m.aid, wire::encode_results(&res, m.slot));
    }
    let mut rows = Vec::new();
    for p in &res.players {
        if p.base_share <= 0 && p.job_share <= 0 {
            continue;
        }
        let Some(m) = mem.iter().find(|m| m.aid == p.aid) else { continue };
        rows.push(super::reward::record(db, run.run_id, p.aid, m.char_id, &p.name, p.base_share, p.job_share, now));
    }
    let outcome_name = ["?", "Victory", "TimeUp", "Defeat", "Stopped"][res.outcome.min(4) as usize];
    let line = format!(
        "run {}: {outcome_name} after {:.1} min on {}; {}",
        run.run_id,
        res.seconds / 60.0,
        c.arena(run.arena_id).map(|a| c.arenas[a].map.as_str()).unwrap_or(""),
        res.players
            .iter()
            .map(|p| format!("{} lv {} job {} kills {} score {} reward {}%/{}%{}", p.name, p.level, p.job_id, p.kills, p.score, p.base_share as f64 / 100.0, p.job_share as f64 / 100.0, if p.left { " (left)" } else { "" }))
            .collect::<Vec<_>>()
            .join("; ")
    );
    log::info!("horde: {line} [steps: {}; hits not drawn {}]", live.steps, live.enc.hits_dropped);
    let mut st = state(db);
    st.last_run = line;
    st.runs += 1;
    st.run_seconds = 0;
    st.enemies = 0;
    put_state(db, st);
    end_run_rows(db, run);
    broadcast_status(db, None, None, now);
    super::reward::grant_rows(rc, rows);
}

/// The copy in memory is not this run (see the top of this file): the run is over as "Stopped" with what its row last
/// kept; the reward is decided from those results.
fn lost(rc: &ReducerContext, run: HordeRun, now: i64) {
    let db = &rc.db;
    log::warn!("horde: run {} lost its simulation at step {} (the module instance was replaced): it ends as Stopped with the results of its last status", run.run_id, run.steps_done);
    let mem = members(db);
    let mut rows = Vec::new();
    if run.results.len() > 11 {
        let mut frame = run.results.clone();
        frame[5] = outcome::STOPPED;
        for m in &mem {
            if m.slot < 0 || m.state == DECLINED || m.state == ASKED {
                continue;
            }
            frame[10] = m.slot as i8 as u8;
            send_aid(db, m.aid, frame.clone());
        }
        for (aid, name, bs, js) in wire::results_shares(&run.results) {
            if bs <= 0 && js <= 0 {
                continue;
            }
            let Some(m) = mem.iter().find(|m| m.aid == aid) else { continue };
            rows.push(super::reward::record(db, run.run_id, aid, m.char_id, &name, bs.min(MAX_SHARE), js.min(MAX_SHARE), now));
        }
    }
    let mut st = state(db);
    st.last_run = format!("run {}: Stopped (its simulation was lost at step {})", run.run_id, run.steps_done);
    st.runs += 1;
    st.lost_runs += 1;
    st.run_seconds = 0;
    st.enemies = 0;
    put_state(db, st);
    end_run_rows(db, &run);
    broadcast_status(db, None, None, now);
    super::reward::grant_rows(rc, rows);
}

// ------------------------------------------------------------------------------------------- the owner's switch

/// The owner's switch and settings, as Loki Host's window has them: `enabled`; `run_minutes` 1..120 (15); `reward`
/// "off" / "low" / "normal" / "high" (normal); `arena` 0 = any of the tables', else an arena's id; `invite_seconds`
/// 1..600 (30). They hold from the next run on; switching the mode off stops a run being played (it ends as
/// "Stopped", with its results and rewards, as when Loki Host is stopped).
/// `spacetime call <db> horde_settings true 15 normal 0 30`
#[reducer]
pub fn horde_settings(rc: &ReducerContext, enabled: bool, run_minutes: u32, reward: String, arena: u32, invite_seconds: u32) -> Result<(), String> {
    crate::release::require_owner(rc, "change Horde's settings")?;
    let reward = match reward.trim().to_ascii_lowercase().as_str() {
        "off" | "0" => 0,
        "low" | "1" => 1,
        "normal" | "2" => 2,
        "high" | "3" => 3,
        _ => return Err("The reward is \"off\", \"low\", \"normal\" or \"high\".".into()),
    };
    if !(1..=120).contains(&run_minutes) {
        return Err("A run lasts 1 to 120 minutes.".into());
    }
    if !(1..=600).contains(&invite_seconds) {
        return Err("The invitation lasts 1 to 600 seconds.".into());
    }
    let c = content::game();
    if arena != 0 && c.arena(arena as i32).is_none() {
        return Err(format!("There is no arena {arena} in the tables (0 = any; the tables have {}).", c.arenas.iter().map(|a| format!("{} {}", a.id, a.map)).collect::<Vec<_>>().join(", ")));
    }
    let db = &rc.db;
    outbox_begin();
    let cfg = HordeConfig { id: 0, enabled, run_minutes, reward, arena: arena as i32, invite_seconds };
    if db.horde_config().id().find(0).is_some() {
        db.horde_config().id().update(cfg);
    } else {
        db.horde_config().insert(cfg);
    }
    let now = now_ms(rc);
    let run = db.horde_run().id().find(0);
    if !enabled {
        match &run {
            Some(r) if r.phase == RUNNING => {
                db.horde_op().insert(HordeOp { id: 0, kind: OP_STOP, aid: 0, a: 0, b: 0, c: 0 });
            }
            Some(r) => {
                end_run_rows(db, r);
                broadcast_status(db, None, None, now);
                outbox_flush(db);
                return Ok(());
            }
            None => {}
        }
    }
    log::info!("horde: Horde {}: runs of {run_minutes} min, reward {reward}, arena {arena}, invitation {invite_seconds} s, tables {}", if enabled { "on" } else { "off" }, c.stamp_cs);
    // every linked client learns it (the panel's "switched off by the host")
    let st = status_from_rows(db, run.as_ref(), now);
    for p in db.horde_peer().iter() {
        send_status_to_peer(db, &p, &st);
    }
    outbox_flush(db);
    Ok(())
}

/// TEST DATABASES ONLY (the owner, and never in release mode): the run being played keeps its field at `enemies`
/// monsters (0 = no more), as Loki Host's HordeMeasureTests does to measure what a full field costs. Not a way to play.
#[reducer]
pub fn horde_test_hold(rc: &ReducerContext, enemies: u32) -> Result<(), String> {
    crate::release::require_owner(rc, "hold Horde's field full")?;
    if crate::release::load(&rc.db) {
        return Err("A release server has no test hooks.".into());
    }
    if playing_any(&rc.db) {
        rc.db.horde_op().insert(HordeOp { id: 0, kind: OP_HOLD, aid: 0, a: enemies.min(300) as i32, b: 0, c: 0 });
        Ok(())
    } else {
        Err("No run is being played.".into())
    }
}

fn playing_any(db: &Db) -> bool {
    db.horde_run().id().find(0).is_some_and(|r| r.phase == RUNNING)
}
