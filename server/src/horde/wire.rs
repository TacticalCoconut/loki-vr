//! Horde's wire as the module speaks it: client/Assets/Loki/Runtime/Horde/Core/Net/HordeWire.cs (the messages; Horde
//! protocol version 2) and HordeSnapshot.cs (SnapshotEncoder: H_SNAP's shared body), ported byte for byte: the client's
//! own HordeClient / SnapshotDecoder read what this writes. The golden test compares every snapshot body of every
//! golden run, and frames of every encoder, with what the C# code wrote (server/src/horde/golden.rs).
//! A message here is a PAYLOAD (its type byte first), without the relay's u16 length prefix.

use std::collections::{HashMap, HashSet};

use super::math::{clamp, round, TICK_RATE, UNIT};
use super::sim::*;

/// HordeWire.Version: the layout of everything below.
pub const VERSION: u8 = 4;
pub const H_HELLO: u8 = 0x10;
pub const H_START: u8 = 0x11;
pub const H_ANSWER: u8 = 0x12;
pub const H_INPUT: u8 = 0x13;
pub const H_CHOOSE: u8 = 0x14;
pub const H_LEAVE: u8 = 0x15;
pub const H_END: u8 = 0x16;
pub const H_PAUSE: u8 = 0x17;
pub const H_CAST: u8 = 0x18;
pub const H_STATUS: u8 = 0x90;
pub const H_TOWNS: u8 = 0x91;
pub const H_INVITE: u8 = 0x92;
pub const H_RUN: u8 = 0x93;
pub const H_SNAP: u8 = 0x94;
pub const H_OFFER: u8 = 0x95;
pub const H_OFFER_CLOSED: u8 = 0x96;
pub const H_RESULTS: u8 = 0x97;
pub const H_REWARD: u8 = 0x98;
pub const MAX_PAYLOAD: usize = 65535;
pub const MAX_STAMP_BYTES: usize = 64;
pub const MAX_NAME_BYTES: usize = 48;

/// HordeWire.W.
#[derive(Default)]
pub struct W {
    pub b: Vec<u8>,
}

impl W {
    pub fn new() -> Self {
        W { b: Vec::with_capacity(256) }
    }
    pub fn u8(&mut self, v: i32) -> &mut Self {
        self.b.push(v as u8);
        self
    }
    pub fn i8(&mut self, v: i32) -> &mut Self {
        self.b.push(clamp(v, -128, 127) as i8 as u8);
        self
    }
    pub fn u16(&mut self, v: i32) -> &mut Self {
        self.b.extend_from_slice(&(v as u16).to_le_bytes());
        self
    }
    pub fn i16(&mut self, v: i32) -> &mut Self {
        self.b.extend_from_slice(&(clamp(v, -32768, 32767) as i16).to_le_bytes());
        self
    }
    pub fn u32(&mut self, v: u32) -> &mut Self {
        self.b.extend_from_slice(&v.to_le_bytes());
        self
    }
    pub fn i32(&mut self, v: i32) -> &mut Self {
        self.b.extend_from_slice(&v.to_le_bytes());
        self
    }
    pub fn i64(&mut self, v: i64) -> &mut Self {
        self.b.extend_from_slice(&v.to_le_bytes());
        self
    }
    /// u8 length + UTF-8, at most `max` bytes, never splitting a character.
    pub fn str(&mut self, s: &str, max: usize) -> &mut Self {
        let b = s.as_bytes();
        let mut n = b.len().min(max);
        while n > 0 && n < b.len() && (b[n] & 0xC0) == 0x80 {
            n -= 1;
        }
        self.b.push(n as u8);
        self.b.extend_from_slice(&b[..n]);
        self
    }
    pub fn raw(&mut self, b: &[u8]) -> &mut Self {
        self.b.extend_from_slice(b);
        self
    }
}

/// The relay's framing (u16 length + payload): what the C# encoders return, and what a client's reducer call carries.
pub fn frame(payload: &[u8]) -> Vec<u8> {
    let mut f = Vec::with_capacity(payload.len() + 2);
    f.extend_from_slice(&(payload.len() as u16).to_le_bytes());
    f.extend_from_slice(payload);
    f
}

// ------------------------------------------------------------------------------------------- client → host

/// HordeWire.ClientMsg: a client message, decoded and checked.
#[derive(Clone, Debug, PartialEq)]
pub enum ClientMsg {
    Hello { version: u8, sex: u8, stamp: String },
    Start { invite: Option<Vec<u32>> },
    Answer { run_id: u32, join: bool },
    Input { seq: u16, dx: i32, dy: i32 },
    Choose { offer_id: i32, index: i32 },
    Leave,
    End { run_id: u32 },
    Pause { pause: bool },
    Cast { weapon_id: i32, target_x: i32, target_y: i32 },
}

/// HordeWire.DecodeClient (HordeWire.cs:149-182): None = malformed (dropped).
pub fn decode_client(b: &[u8]) -> Option<ClientMsg> {
    let (&k, rest) = b.split_first()?;
    let m = match k {
        H_HELLO => {
            if rest.len() < 3 {
                return None;
            }
            let n = rest[2] as usize;
            if n > MAX_STAMP_BYTES || rest.len() != 3 + n {
                return None;
            }
            let stamp = std::str::from_utf8(&rest[3..]).ok()?;
            if stamp.chars().any(|c| c.is_control()) {
                return None;
            }
            ClientMsg::Hello { version: rest[0], sex: if rest[1] > 2 { 0 } else { rest[1] }, stamp: stamp.to_string() }
        }
        H_START => {
            if rest.is_empty() {
                ClientMsg::Start { invite: None }
            } else {
                let ni = rest[0] as usize;
                if ni > 64 || rest.len() != 1 + ni * 4 {
                    return None;
                }
                let mut inv = Vec::with_capacity(ni);
                for i in 0..ni {
                    let o = 1 + i * 4;
                    inv.push(u32::from_le_bytes([rest[o], rest[o + 1], rest[o + 2], rest[o + 3]]));
                }
                ClientMsg::Start { invite: Some(inv) }
            }
        }
        H_LEAVE if rest.is_empty() => ClientMsg::Leave,
        H_END if rest.len() == 4 => ClientMsg::End { run_id: u32::from_le_bytes([rest[0], rest[1], rest[2], rest[3]]) },
        H_PAUSE if rest.len() == 1 => ClientMsg::Pause { pause: rest[0] != 0 },
        H_ANSWER if rest.len() == 5 => ClientMsg::Answer { run_id: u32::from_le_bytes([rest[0], rest[1], rest[2], rest[3]]), join: rest[4] != 0 },
        H_INPUT if rest.len() == 4 => ClientMsg::Input { seq: u16::from_le_bytes([rest[0], rest[1]]), dx: rest[2] as i8 as i32, dy: rest[3] as i8 as i32 },
        H_CHOOSE if rest.len() == 3 => ClientMsg::Choose { offer_id: u16::from_le_bytes([rest[0], rest[1]]) as i32, index: rest[2] as i32 },
        H_CAST if rest.len() == 10 => ClientMsg::Cast {
            weapon_id: u16::from_le_bytes([rest[0], rest[1]]) as i32,
            target_x: i32::from_le_bytes([rest[2], rest[3], rest[4], rest[5]]),
            target_y: i32::from_le_bytes([rest[6], rest[7], rest[8], rest[9]]),
        },
        _ => return None,
    };
    Some(m)
}

// ------------------------------------------------------------------------------------------- host → client

/// HordeMember.
#[derive(Clone, Debug, Default)]
pub struct Member {
    pub aid: u32,
    pub name: String,
    /// HordeMemberState: 0 asked, 1 joined, 2 declined, 3 left.
    pub state: u8,
    pub slot: i32,
    pub level: i32,
    pub job_id: i32,
    pub fallen: bool,
}

/// HordeOnline: an online player in town who can be invited to a run.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct OnlinePlayer {
    pub aid: u32,
    pub name: String,
}

/// HordeStatus (what H_STATUS carries of it).
#[derive(Clone, Debug, Default)]
pub struct Status {
    pub enabled: bool,
    /// HordePhase: 0 idle, 1 gathering, 2 running.
    pub phase: u8,
    pub run_id: u32,
    pub starter_aid: u32,
    pub starter_name: String,
    pub seconds_left: f32,
    pub run_seconds: f32,
    pub run_length: f32,
    pub arena_map: String,
    pub reward: u8,
    pub lowest_level: i32,
    pub members: Vec<Member>,
    pub online: Vec<OnlinePlayer>,
}

/// `(int)Math.Round(Math.Max(0, seconds) * 10)`: a float × 10 in single precision, rounded half to even.
fn tenths(seconds: f32) -> i32 {
    round((0f32.max(seconds) * 10.0) as f64)
}

/// `(int)Math.Min(65535, seconds)`.
fn whole(seconds: f32) -> i32 {
    65535f32.min(seconds) as i32
}

/// HordeWire.EncodeStatus (HordeWire.cs:175-193).
pub fn encode_status(s: &Status, content_ok: bool) -> Vec<u8> {
    encode_status_with_version(s, content_ok, VERSION)
}

pub fn encode_status_with_version(s: &Status, content_ok: bool, version: u8) -> Vec<u8> {
    let mut w = W::new();
    w.u8(H_STATUS as i32).u8(s.enabled as i32 | if content_ok { 2 } else { 0 }).u8(s.phase as i32).u32(s.run_id).u32(s.starter_aid).str(&s.starter_name, MAX_NAME_BYTES);
    w.u16(tenths(s.seconds_left)).u16(whole(s.run_seconds)).u16(whole(s.run_length)).str(&s.arena_map, 15).u8(s.reward as i32).u8(255.min(s.lowest_level));
    let n = s.members.len().min(64);
    w.u8(n as i32);
    for m in &s.members[..n] {
        w.u32(m.aid).str(&m.name, MAX_NAME_BYTES).u8(m.state as i32).i8(m.slot).u8(255.min(m.level)).u16(m.job_id).u8(m.fallen as i32);
    }
    w.u8(version as i32);
    if version >= 4 {
        let no = s.online.len().min(64);
        w.u8(no as i32);
        for o in &s.online[..no] {
            w.u32(o.aid).str(&o.name, MAX_NAME_BYTES);
        }
    }
    w.b
}

/// HordeWire.EncodeTowns.
pub fn encode_towns(towns: &[String]) -> Vec<u8> {
    let mut w = W::new();
    let n = towns.len().min(1000);
    w.u8(H_TOWNS as i32).u16(n as i32);
    for t in &towns[..n] {
        w.str(t, 15);
    }
    w.b
}

/// HordeWire.EncodeInvite.
#[allow(clippy::too_many_arguments)]
pub fn encode_invite(run_id: u32, starter_aid: u32, starter_name: &str, seconds: f32, arena_map: &str, run_length: f32, reward: u8) -> Vec<u8> {
    let mut w = W::new();
    w.u8(H_INVITE as i32).u32(run_id).u32(starter_aid).str(starter_name, MAX_NAME_BYTES).u16(tenths(seconds)).str(arena_map, 15).u16(whole(run_length)).u8(reward as i32);
    w.b
}

/// HordeRunInfo + the tick now (HordeWire.EncodeRun).
#[derive(Clone, Debug, Default)]
pub struct RunInfo {
    pub run_id: u32,
    pub slot: i32,
    pub arena_id: i32,
    pub map: String,
    pub x0: i32,
    pub y0: i32,
    pub x1: i32,
    pub y1: i32,
    pub snapshot_every: i32,
    pub run_length: f32,
    pub final_boss_seconds: f32,
    pub late: bool,
}

pub fn encode_run(v: &RunInfo, tick_now: i32) -> Vec<u8> {
    let mut w = W::new();
    w.u8(H_RUN as i32).u32(v.run_id).u8(v.slot).u16(v.arena_id).str(&v.map, 15).u16(v.x0).u16(v.y0).u16(v.x1).u16(v.y1).u8(TICK_RATE);
    w.u16(whole(v.run_length)).u16(whole(v.final_boss_seconds)).u8(v.late as i32).i32(tick_now).u8(clamp(v.snapshot_every, 1, 255));
    w.b
}

/// HordeWire.EncodeOffer. An offer on the wire always has its cards.
pub fn encode_offer(o: &Offer) -> Vec<u8> {
    let mut w = W::new();
    w.u8(H_OFFER as i32).u16(o.id).u8(o.is_job as i32).u16(tenths(o.seconds));
    let cards = o.cards.as_deref().unwrap_or(&[]);
    let n = cards.len().min(16);
    w.u8(n as i32);
    let mut has_bonus = false;
    for c in &cards[..n] {
        w.u8(c.kind as i32).u16(c.def_id).u8(255.min(c.level));
        if c.bonus_stat != 0 || c.bonus_stat_points != 0 {
            has_bonus = true;
        }
    }
    if has_bonus {
        for c in &cards[..n] {
            w.u8(c.bonus_stat as i32).u8(c.bonus_stat_points as i32);
        }
    }
    w.b
}

pub fn encode_offer_closed(id: i32) -> Vec<u8> {
    let mut w = W::new();
    w.u8(H_OFFER_CLOSED as i32).u16(id);
    w.b
}

/// HordeWire.EncodeResults (HordeWire.cs:271-283).
pub fn encode_results(v: &Results, my_slot: i32) -> Vec<u8> {
    let mut w = W::new();
    // (uint)Math.Round(v.Seconds * 10): float × int in single precision
    w.u8(H_RESULTS as i32).u32(v.run_id).u8(v.outcome as i32).u32(((v.seconds * 10.0) as f64).round_ties_even() as u32).i8(my_slot);
    let n = v.players.len().min(64);
    w.u8(n as i32);
    for p in &v.players[..n] {
        w.u8(p.slot).u32(p.aid).str(&p.name, MAX_NAME_BYTES).u16(p.level).u16(p.job_id).i32(p.kills).i32(p.elite_kills).i32(p.boss_kills).u16(p.falls).u16(p.revives);
        w.i64(p.damage).i64(p.healing).u32(((p.seconds * 10.0) as f64).round_ties_even() as u32).i32(p.score).u16(p.base_share).u16(p.job_share).u8(p.reward as i32).u8(p.left as i32);
    }
    w.b
}

/// HordeWire.EncodeReward.
pub fn encode_reward(run_id: u32, base_exp: i64, job_exp: i64, base_share: i32, job_share: i32) -> Vec<u8> {
    let mut w = W::new();
    w.u8(H_REWARD as i32).u32(run_id).i64(base_exp).i64(job_exp).u16(base_share).u16(job_share);
    w.b
}

/// SnapshotEncoder.Frame: the whole H_SNAP payload for one client: its ack + the shared body. None: too long.
pub fn snap_payload(ack: u16, body: &[u8]) -> Option<Vec<u8>> {
    if 1 + 2 + body.len() > MAX_PAYLOAD {
        return None;
    }
    let mut f = Vec::with_capacity(3 + body.len());
    f.push(H_SNAP);
    f.extend_from_slice(&ack.to_le_bytes());
    f.extend_from_slice(body);
    Some(f)
}

// ------------------------------------------------------------------------------------------- the snapshot encoder

#[derive(Clone, Copy, Default, PartialEq)]
struct Vitals {
    speed: i32,
    hp: i32,
    max_hp: i32,
    job: i32,
    revive: i32,
    kills: i32,
}

fn vitals_of(p: &Player) -> Vitals {
    Vitals { speed: p.speed_per_tick, hp: p.hp.min(65535), max_hp: p.max_hp.min(65535), job: p.job_id, revive: 255f64.min(p.revive * 255.0) as i32, kills: p.kills.min(65535) }
}

/// An enemy's move on the wire: units per step (4 = 1/64 cell).
pub const MOVE_UNIT: i32 = 4;

/// SnapshotEncoder (HordeSnapshot.cs:31-253): what changed since the previous snapshot (a delta) or everything (a key).
pub struct SnapshotEncoder {
    /// Hits per snapshot that are sent (the largest; the rest are only counted).
    pub max_hits: usize,
    bx: i32,
    by: i32,
    alive: Vec<bool>,
    serial: Vec<i32>,
    sx: Vec<i32>,
    sy: Vec<i32>,
    hp: Vec<u8>,
    fl: Vec<u8>,
    loadout: [i32; MAX_PLAYERS],
    present: [bool; MAX_PLAYERS],
    vitals: [Vitals; MAX_PLAYERS],
    prev_tick: i32,
    events: Vec<Event>,
    hits: Vec<Event>,
    begun: Vec<u64>,
    /// The begun effects that are gone from the engine's list, in their last state (C# reads them through its reference).
    gone: HashMap<u64, Fx>,
    ended: Vec<(i32, bool)>,
    p_added: HashSet<i32>,
    /// A HashSet in C#, read in the order the ids were added (nothing is ever taken out of it between two clears).
    p_removed: Vec<i32>,
    p_pulled: HashSet<i32>,
    pub hits_dropped: i64,
}

impl SnapshotEncoder {
    pub fn new(sim: &Sim) -> Self {
        SnapshotEncoder {
            max_hits: 12,
            bx: sim.arena.x0 * UNIT,
            by: sim.arena.y0 * UNIT,
            alive: vec![false; MAX_ENEMIES],
            serial: vec![0; MAX_ENEMIES],
            sx: vec![0; MAX_ENEMIES],
            sy: vec![0; MAX_ENEMIES],
            hp: vec![0; MAX_ENEMIES],
            fl: vec![0; MAX_ENEMIES],
            loadout: [-1; MAX_PLAYERS],
            present: [false; MAX_PLAYERS],
            vitals: [Vitals::default(); MAX_PLAYERS],
            prev_tick: -1,
            events: Vec::new(),
            hits: Vec::new(),
            begun: Vec::new(),
            gone: HashMap::new(),
            ended: Vec::new(),
            p_added: HashSet::new(),
            p_removed: Vec::new(),
            p_pulled: HashSet::new(),
            hits_dropped: 0,
        }
    }

    /// Collect: after each sim step (before its outputs are cleared): keeps what the next snapshot must tell.
    pub fn collect(&mut self, sim: &Sim) {
        self.events.extend_from_slice(&sim.events);
        self.hits.extend_from_slice(&sim.hits);
        self.begun.extend_from_slice(&sim.fx_begun);
        self.ended.extend_from_slice(&sim.fx_ended);
        for f in &sim.fx_removed {
            if self.begun.contains(&f.uid) {
                self.gone.insert(f.uid, f.clone());
            }
        }
        for &id in &sim.pickups_added {
            self.p_added.insert(id);
        }
        for &id in &sim.pickups_pulled {
            if !self.p_added.contains(&id) {
                self.p_pulled.insert(id);
            }
        }
        for &id in &sim.pickups_removed {
            self.p_pulled.remove(&id);
            if !self.p_added.remove(&id) && !self.p_removed.contains(&id) {
                self.p_removed.push(id);
            }
        }
    }

    fn x(&self, x: i32) -> i32 {
        clamp(x - self.bx, 0, 65535)
    }
    fn y(&self, y: i32) -> i32 {
        clamp(y - self.by, 0, 65535)
    }

    /// Encode: this snapshot's delta (for clients that had the previous one) and, when asked, the key.
    pub fn encode(&mut self, sim: &Sim, want_key: bool) -> (Vec<u8>, Option<Vec<u8>>) {
        let tick = sim.tick;
        // ---- enemies: work out the delta against what clients hold (and update it)
        let (mut removed, mut added, mut moved, mut jumped, mut hp_ch, mut fl_ch) = (Vec::new(), Vec::new(), Vec::new(), Vec::new(), Vec::new(), Vec::new());
        let mut mdx = vec![0i8; MAX_ENEMIES];
        let mut mdy = vec![0i8; MAX_ENEMIES];
        let mut covered = 0usize;
        for i in 0..MAX_ENEMIES {
            let e = &sim.enemies[i];
            let now = e.alive;
            let hp: u8 = if now { 1.max(255.min((e.hp as i64 * 255 / 1.max(e.max_hp) as i64) as i32)) as u8 } else { 0 };
            let fl: u8 = if now { e.flags } else { 0 };
            if self.alive[i] && (!now || e.serial != self.serial[i]) {
                removed.push(i);
                self.alive[i] = false;
            }
            if now && !self.alive[i] {
                self.alive[i] = true;
                self.serial[i] = e.serial;
                self.sx[i] = self.x(e.x);
                self.sy[i] = self.y(e.y);
                self.hp[i] = hp;
                self.fl[i] = fl;
                added.push(i);
                continue;
            }
            if !now {
                continue;
            }
            let dx = (self.x(e.x) - self.sx[i]) / MOVE_UNIT;
            let dy = (self.y(e.y) - self.sy[i]) / MOVE_UNIT;
            if dx != 0 || dy != 0 {
                if !(-127..=127).contains(&dx) || !(-127..=127).contains(&dy) {
                    self.sx[i] = self.x(e.x);
                    self.sy[i] = self.y(e.y);
                    jumped.push(i);
                } else {
                    mdx[i] = dx as i8;
                    mdy[i] = dy as i8;
                    self.sx[i] += dx * MOVE_UNIT;
                    self.sy[i] += dy * MOVE_UNIT;
                    moved.push(i);
                    covered = i + 1;
                }
            }
            if hp != self.hp[i] {
                self.hp[i] = hp;
                hp_ch.push(i);
            }
            if fl != self.fl[i] {
                self.fl[i] = fl;
                fl_ch.push(i);
            }
        }
        // ---- hits: the largest (List.Sort's own order among equal values: see intro_sort)
        intro_sort(&mut self.hits, &|a: &Event, b: &Event| b.value.cmp(&a.value));
        if self.hits.len() > self.max_hits {
            self.hits_dropped += (self.hits.len() - self.max_hits) as i64;
            self.hits.truncate(self.max_hits);
        }
        let delta = self.build_body(sim, false, tick, &removed, &added, &moved, &jumped, covered, &mdx, &mdy, &hp_ch, &fl_ch);
        let key = if want_key { Some(self.build_body(sim, true, tick, &[], &[], &[], &[], 0, &mdx, &mdy, &[], &[])) } else { None };
        // what was told is told
        for s in 0..MAX_PLAYERS {
            let p = &sim.players[s];
            self.present[s] = p.present;
            if p.present {
                self.loadout[s] = p.loadout_version;
                self.vitals[s] = vitals_of(p);
            }
        }
        self.events.clear();
        self.hits.clear();
        self.begun.clear();
        self.gone.clear();
        self.ended.clear();
        self.p_added.clear();
        self.p_removed.clear();
        self.p_pulled.clear();
        self.prev_tick = tick;
        (delta, key)
    }

    #[allow(clippy::too_many_arguments)]
    fn build_body(&self, sim: &Sim, key: bool, tick: i32, removed: &[usize], added: &[usize], moved: &[usize], jumped: &[usize], covered: usize, mdx: &[i8], mdy: &[i8], hp_ch: &[usize], fl_ch: &[usize]) -> Vec<u8> {
        let c = sim.c;
        let mut w = W::new();
        w.i32(tick).u8(key as i32 | if sim.final_phase { 2 } else { 0 } | if sim.paused { 4 } else { 0 }).i32(self.prev_tick);
        w.u8(255.min(sim.party_level)).i32(sim.party_exp).i32(sim.party_exp_to_next());
        // players
        w.u8(sim.players.iter().filter(|p| p.present).count() as i32);
        for p in &sim.players {
            if !p.present {
                continue;
            }
            let s = p.slot as usize;
            let fl = (if p.fallen { 1 } else { 0 })
                | (if p.safe_until > sim.tick || p.open.is_some() && !p.idle_chooser { 2 } else { 0 })
                | (if p.open.is_some() { 4 } else { 0 })
                | (if p.away { 8 } else { 0 })
                | (if p.helped { 16 } else { 0 })
                | (if p.slot == sim.held_by { 32 } else { 0 });
            let fresh = key || !self.present[s];
            let identity = fresh;
            let loadout = fresh || self.loadout[s] != p.loadout_version;
            let vit = vitals_of(p);
            let vitals = fresh || vit != self.vitals[s];
            w.u8(p.slot).u8(fl).u16(self.x(p.x)).u16(self.y(p.y)).i16(p.face_deg).i16(p.vx).i16(p.vy).u8(identity as i32 | if loadout { 2 } else { 0 } | if vitals { 4 } else { 0 });
            if vitals {
                w.u16(vit.speed).u16(vit.hp).u16(vit.max_hp).u16(vit.job).u8(vit.revive).u16(vit.kills);
            }
            if identity {
                w.u32(p.aid).str(&p.name, MAX_NAME_BYTES);
            }
            if loadout {
                let nw = p.weapons.len().min(32);
                w.u8(nw as i32);
                for ow in &p.weapons[..nw] {
                    w.u16(c.weapons[ow.def].id).u8(ow.level);
                }
                let np = p.passives.len().min(32);
                w.u8(np as i32);
                for op in &p.passives[..np] {
                    w.u16(c.passives[op.def].id).u8(op.level);
                }
                for i in 0..6 {
                    w.u16(clamp(p.stat(i), 0, 65535));
                }
            }
        }
        // enemies
        let enemy_record = |w: &mut W, i: usize| {
            w.u16(i as i32).u8(self.serial[i]).u16(c.enemies[sim.enemies[i].def].id).u16(self.sx[i]).u16(self.sy[i]).u8(self.hp[i] as i32).u8(self.fl[i] as i32);
        };
        if key {
            w.u16(self.alive.iter().filter(|a| **a).count() as i32);
            for i in 0..MAX_ENEMIES {
                if self.alive[i] {
                    enemy_record(&mut w, i);
                }
            }
        } else {
            w.u16(removed.len() as i32);
            for &i in removed {
                w.u16(i as i32);
            }
            w.u16(added.len() as i32);
            for &i in added {
                enemy_record(&mut w, i);
            }
            w.u16(covered as i32);
            let mut bits = vec![0u8; (covered + 7) / 8];
            for &i in moved {
                bits[i >> 3] |= 1 << (i & 7);
            }
            w.raw(&bits);
            for &i in moved {
                w.i8(mdx[i] as i32).i8(mdy[i] as i32);
            }
            w.u16(jumped.len() as i32);
            for &i in jumped {
                w.u16(i as i32).u16(self.sx[i]).u16(self.sy[i]);
            }
            w.u16(hp_ch.len() as i32);
            for &i in hp_ch {
                w.u16(i as i32).u8(self.hp[i] as i32);
            }
            w.u16(fl_ch.len() as i32);
            for &i in fl_ch {
                w.u16(i as i32).u8(self.fl[i] as i32);
            }
        }
        // pickups: a record is where it lies (a flying one: where the pull began; the client draws the flight) and whether it flies
        let pickup_record = |w: &mut W, k: &Pickup| {
            w.u16(k.id).u8(c.pickups[k.def].id).u16(self.x(k.x)).u16(self.y(k.y)).i32(k.value).i8(k.pulled_by).u8(if k.pulled_by >= 0 { clamp(tick - k.pull_tick, 0, 255) } else { 0 });
        };
        if key {
            w.u16(sim.pickups.len() as i32);
            for k in &sim.pickups {
                pickup_record(&mut w, k);
            }
        } else {
            w.u16(self.p_removed.len() as i32);
            for &id in &self.p_removed {
                w.u16(id);
            }
            let add: Vec<&Pickup> = sim.pickups.iter().filter(|k| self.p_added.contains(&k.id)).collect();
            let pulled: Vec<&Pickup> = sim.pickups.iter().filter(|k| !self.p_added.contains(&k.id) && self.p_pulled.contains(&k.id) && k.pulled_by >= 0).collect();
            w.u16(add.len() as i32);
            for k in add {
                pickup_record(&mut w, k);
            }
            w.u16(pulled.len() as i32);
            for k in pulled {
                w.u16(k.id).u8(k.pulled_by).u8(clamp(tick - k.pull_tick, 0, 255));
            }
        }
        // summons (always all)
        w.u8(255.min(sim.summons.len() as i32));
        for s in &sim.summons {
            let def = sim.players[s.owner_slot as usize].weapons[s.widx].def;
            w.u16(s.id).u8(s.owner_slot).u16(c.weapons[def].id).u16(self.x(s.x)).u16(self.y(s.y)).u8(if s.max_hp > 0 { 1.max(s.hp * 255 / s.max_hp) } else { 255 });
        }
        // fx
        let fx_record = |w: &mut W, f: &Fx| {
            w.u16(f.id).u8(f.kind as i32).i8(f.owner_slot).i16(f.owner_enemy).u16(f.def_id).u8(f.level).u16(self.x(f.x)).u16(self.y(f.y)).i16(f.vx).i16(f.vy);
            w.u16(65535.min(f.radius)).u16(65535.min(f.range)).i16(round(f.angle)).i16(round(f.turns * 1000.0)).u8(255.min(f.count));
            w.i16(f.target).u8(f.follows as i32).i32(f.start_tick).i32(f.arm_tick).i32(f.end_tick);
        };
        if key {
            w.u16(sim.fx.iter().filter(|f| !f.ended).count() as i32);
            for f in sim.fx.iter().filter(|f| !f.ended) {
                fx_record(&mut w, f);
            }
            w.u16(0);
        } else {
            w.u16(self.begun.len() as i32);
            for uid in &self.begun {
                // the effect as it is now (C# holds the object: a shot begun last tick has flown one step since)
                match sim.fx.iter().find(|f| f.uid == *uid).or_else(|| self.gone.get(uid)).or_else(|| sim.fx_removed.iter().find(|f| f.uid == *uid)) {
                    Some(f) => fx_record(&mut w, f),
                    None => fx_record(&mut w, &Fx::placeholder()),
                }
            }
            w.u16(self.ended.len() as i32);
            for &(id, hit) in &self.ended {
                w.u16(id).u8(hit as i32);
            }
        }
        // events (a key carries the same events: a client that gets a key instead of a delta loses none of this tick's)
        w.u16((self.events.len() + self.hits.len()) as i32);
        for e in self.events.iter().chain(self.hits.iter()) {
            let place = e.kind != ev::HIT && e.kind != ev::LEVEL_UP && e.kind != ev::FINAL_PHASE;
            let extra = matches!(e.kind, ev::SWING | ev::NOVA | ev::STRIKE | ev::CHAIN | ev::ENEMY_ABILITY | ev::ENEMY_BURST | ev::CARD_TAKEN)
                || (c.is_v4() && matches!(e.kind, ev::PLAYER_HIT | ev::PLAYER_SLOWED | ev::BOSS_STRIKE | ev::DASH | ev::WIND_UP));
            w.u8(e.kind as i32 | if place { 0x80 } else { 0 } | if extra { 0x40 } else { 0 }).u8(e.flags as i32).u8(clamp(tick - e.tick, 0, 255)).i8(e.slot).i16(e.target).u16(e.def_id).i32(e.value);
            if place {
                w.u16(self.x(e.x)).u16(self.y(e.y));
            }
            if extra {
                // (int)Math.Round(e.A * 10): float × int in single precision
                w.u16(self.x(e.x2)).u16(self.y(e.y2)).i16(round((e.a * 10.0) as f64)).i16(round((e.b * 10.0) as f64)).i16(round((e.c * 10.0) as f64));
            }
        }
        if c.is_v4() {
            // HordePassiveBlock: passive proc readiness
            if key {
                w.u8(sim.players.iter().filter(|p| p.present).count() as i32);
                for p in &sim.players {
                    if p.present {
                        w.u8(p.slot).u8(0);
                    }
                }
            } else {
                w.u8(0);
            }
            // HordeObjectiveWire: objectives & objective buffs (HordeSim.Objectives.cs:651-667)
            let on = sim.objectives.len().min(255);
            w.u8(on as i32);
            for o in sim.objectives.iter().take(on) {
                let g = if o.guard_slot >= 0 && sim.enemies[o.guard_slot as usize].alive && sim.enemies[o.guard_slot as usize].serial == o.guard_serial {
                    o.guard_slot
                } else {
                    -1
                };
                let end = if o.state == 0 {
                    o.deadline
                } else if o.state == 1 {
                    o.last_inside + super::math::ticks(6.0)
                } else {
                    o.end_tick
                };
                w.u8(o.id)
                    .u8(o.kind as i32)
                    .u8(o.state as i32)
                    .u16(self.x(o.x))
                    .u16(self.y(o.y))
                    .u8(clamp(o.radius * 16 / UNIT, 0, 255))
                    .u8(clamp(round(o.progress * 255.0), 0, 255))
                    .i8(o.claimer)
                    .u8(255.min(o.crowd))
                    .i32(o.start_tick)
                    .i32(end)
                    .i16(g);
            }
            let om = sim.objective_buffs.len().min(255);
            w.u8(om as i32);
            for b in sim.objective_buffs.iter().take(om) {
                w.u8(b.slot).u8(b.kind as i32).i32(b.until);
            }
            // HordeSoulWire: souls, soul counts, berries (HordeSim.Souls.cs:397-413)
            let sn = sim.souls.len().min(255);
            w.u8(sn as i32);
            for s in sim.souls.iter().take(sn) {
                w.u8(s.id)
                    .u16(self.x(s.x))
                    .u16(self.y(s.y))
                    .u8(clamp(s.radius * 16 / UNIT, 0, 255))
                    .u8(clamp(round(s.progress * 255.0), 0, 255))
                    .i8(s.claimer)
                    .u8(255.min(s.crowd))
                    .u8(s.owners & 0xff);
            }
            let sm = sim.soul_total.iter().filter(|&&t| t > 0).count() as i32;
            w.u8(sm);
            for (slot, &tot) in sim.soul_total.iter().enumerate() {
                if tot > 0 {
                    w.u8(slot as i32).u8(255.min(tot)).u8(255.min(sim.soul_need[slot]));
                }
            }
            let mut sk = 0;
            for (pi, p) in sim.players.iter().enumerate() {
                if p.present && (sim.ygg_burnt(pi) || sim.ygg_cool_until(pi) > sim.tick) {
                    sk += 1;
                }
            }
            w.u8(sk);
            for (pi, p) in sim.players.iter().enumerate() {
                if p.present && (sim.ygg_burnt(pi) || sim.ygg_cool_until(pi) > sim.tick) {
                    w.u8(p.slot).u8(if sim.ygg_burnt(pi) { 1 } else { 2 }).i32(sim.ygg_cool_until(pi));
                }
            }
            // HordeRulesWire: co-op pause countdown
            if sim.pause_count_by < 0 {
                w.u8(0);
            } else {
                w.u8(if sim.pause_count_to_pause { 1 } else { 2 }).i8(sim.pause_count_by).u16(sim.pause_count_ticks_left().min(65535));
            }
        }
        w.b
    }
}

impl Fx {
    /// Never written in practice (an effect the encoder was told of is on the engine's list or among the gone ones).
    fn placeholder() -> Fx {
        Fx {
            uid: 0, id: 0, kind: 0, owner_slot: -1, owner_enemy: -1, def_id: 0, level: 0, enemy_def_id: 0, x: 0, y: 0, vx: 0, vy: 0, radius: 0, range: 0, angle: 0.0, turns: 0.0, count: 0, target: -1,
            follows: false, start_tick: 0, arm_tick: 0, end_tick: 0, widx: 0, lv: (0, 0), base_damage: 0.0, damage: 0, pierce_left: 0, next_tick: 0, every: 0, heal: 0, heal_pct: 0.0, zone_flags: 0,
            burst_area: 0, hit_list: Vec::new(), ended: true,
        }
    }
}

// ------------------------------------------------------------------------------------------- List<T>.Sort(Comparison)
//
// .NET's ArraySortHelper<T>.IntroSort (System.Private.CoreLib, .NET 5 and later: the runtime Loki Host runs on): not
// a stable sort, so WHICH of two hits of the same damage is kept among the largest twelve is this algorithm's doing.
// Ported so that the snapshot's bytes can be compared with the C# encoder's; the game does not depend on it.

fn intro_sort<T: Clone>(keys: &mut [T], cmp: &dyn Fn(&T, &T) -> std::cmp::Ordering) {
    if keys.len() > 1 {
        let depth = 2 * ((usize::BITS - 1 - keys.len().leading_zeros()) as i32 + 1);
        intro_sort_range(keys, depth, cmp);
    }
}

fn swap_if_greater<T>(keys: &mut [T], cmp: &dyn Fn(&T, &T) -> std::cmp::Ordering, i: usize, j: usize) {
    if cmp(&keys[i], &keys[j]) == std::cmp::Ordering::Greater {
        keys.swap(i, j);
    }
}

fn intro_sort_range<T: Clone>(keys: &mut [T], mut depth: i32, cmp: &dyn Fn(&T, &T) -> std::cmp::Ordering) {
    use std::cmp::Ordering::Less;
    let mut size = keys.len();
    while size > 1 {
        if size <= 16 {
            if size == 2 {
                swap_if_greater(keys, cmp, 0, 1);
                return;
            }
            if size == 3 {
                swap_if_greater(keys, cmp, 0, 1);
                swap_if_greater(keys, cmp, 0, 2);
                swap_if_greater(keys, cmp, 1, 2);
                return;
            }
            // InsertionSort
            for i in 0..size - 1 {
                let t = keys[i + 1].clone();
                let mut j = i as isize;
                while j >= 0 && cmp(&t, &keys[j as usize]) == Less {
                    keys[(j + 1) as usize] = keys[j as usize].clone();
                    j -= 1;
                }
                keys[(j + 1) as usize] = t;
            }
            return;
        }
        if depth == 0 {
            heap_sort(&mut keys[..size], cmp);
            return;
        }
        depth -= 1;
        // PickPivotAndPartition
        let hi = size - 1;
        let middle = hi >> 1;
        swap_if_greater(keys, cmp, 0, middle);
        swap_if_greater(keys, cmp, 0, hi);
        swap_if_greater(keys, cmp, middle, hi);
        let pivot = keys[middle].clone();
        keys.swap(middle, hi - 1);
        let (mut left, mut right) = (0usize, hi - 1);
        while left < right {
            loop {
                left += 1;
                if cmp(&keys[left], &pivot) != Less {
                    break;
                }
            }
            loop {
                right -= 1;
                if cmp(&pivot, &keys[right]) != Less {
                    break;
                }
            }
            if left >= right {
                break;
            }
            keys.swap(left, right);
        }
        if left != hi - 1 {
            keys.swap(left, hi - 1);
        }
        let p = left;
        intro_sort_range(&mut keys[p + 1..size], depth, cmp);
        size = p;
    }
}

fn heap_sort<T: Clone>(keys: &mut [T], cmp: &dyn Fn(&T, &T) -> std::cmp::Ordering) {
    use std::cmp::Ordering::Less;
    fn down_heap<T: Clone>(keys: &mut [T], mut i: usize, n: usize, cmp: &dyn Fn(&T, &T) -> std::cmp::Ordering) {
        let d = keys[i - 1].clone();
        while i <= n >> 1 {
            let mut child = 2 * i;
            if child < n && cmp(&keys[child - 1], &keys[child]) == Less {
                child += 1;
            }
            if cmp(&d, &keys[child - 1]) != Less {
                break;
            }
            keys[i - 1] = keys[child - 1].clone();
            i = child;
        }
        keys[i - 1] = d;
    }
    let n = keys.len();
    let mut i = n >> 1;
    while i >= 1 {
        down_heap(keys, i, n, cmp);
        i -= 1;
    }
    let mut i = n;
    while i > 1 {
        keys.swap(0, i - 1);
        down_heap(keys, 1, i - 1, cmp);
        i -= 1;
    }
}

/// From an H_RESULTS payload (encode_results): each player's (account, name, base share, job share). The service keeps
/// a run's results in its row once a second; when the simulation's copy in memory is lost the rewards are decided
/// from that row (service.rs). Empty when the payload is not whole.
pub fn results_shares(b: &[u8]) -> Vec<(u32, String, i32, i32)> {
    let mut out = Vec::new();
    if b.len() < 12 || b[0] != H_RESULTS {
        return out;
    }
    let n = b[11] as usize;
    let mut o = 12;
    for _ in 0..n {
        // u8 slot, u32 aid, str name, then 50 bytes of numbers that end with u16 base share, u16 job share, u8 reward, u8 left
        if o + 6 > b.len() {
            return Vec::new();
        }
        let aid = u32::from_le_bytes([b[o + 1], b[o + 2], b[o + 3], b[o + 4]]);
        let len = b[o + 5] as usize;
        let rest = o + 6 + len;
        if rest + 50 > b.len() {
            return Vec::new();
        }
        let name = String::from_utf8_lossy(&b[o + 6..rest]).into_owned();
        // level 2, job 2, kills 4, elite 4, boss 4, falls 2, revives 2, damage 8, healing 8, seconds 4, score 4 = 44
        let bs = u16::from_le_bytes([b[rest + 44], b[rest + 45]]) as i32;
        let js = u16::from_le_bytes([b[rest + 46], b[rest + 47]]) as i32;
        out.push((aid, name, bs, js));
        o = rest + 50;
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    fn player(slot: i32, aid: u32, name: &str, base_share: i32, job_share: i32) -> PlayerResult {
        PlayerResult {
            slot, aid, name: name.to_string(), level: 12, job_id: 7, kills: 300, elite_kills: 2, boss_kills: 1, falls: 1, revives: 0, damage: 123_456_789_012, healing: 77, seconds: 181.5, score: 1500,
            base_share, job_share, reward: 1, left: false,
        }
    }

    /// What the service keeps of a run in its row (an H_RESULTS payload) gives back every player and its shares.
    #[test]
    fn the_shares_are_read_back_from_a_results_payload() {
        let res = Results { run_id: 9, outcome: 1, seconds: 181.5, players: vec![player(0, 2000001, "Alpha", 1000, 980), player(1, 2000002, "Bravó the Second", 0, 0), player(5, 2000003, "", 10000, 1)] };
        let b = encode_results(&res, -1);
        assert_eq!(b[5], 1); // the outcome's place (service.rs lost() writes Stopped there)
        assert_eq!(b[10] as i8, -1); // my slot's place
        assert_eq!(
            results_shares(&b),
            vec![(2000001, "Alpha".to_string(), 1000, 980), (2000002, "Bravó the Second".to_string(), 0, 0), (2000003, String::new(), 10000, 1)]
        );
        assert!(results_shares(&b[..b.len() - 1]).is_empty()); // cut short: nothing is decided from half a payload
        assert!(results_shares(&[]).is_empty());
    }

    #[test]
    fn list_sort_keeps_dotnets_order() {
        // 40 hits of few distinct values: beyond the insertion sort's 16, where List.Sort is not stable
        let mut v: Vec<Event> = (0..40).map(|i| Event { value: [5, 9, 5, 7, 9, 1][i % 6], slot: i as i32, ..Default::default() }).collect();
        intro_sort(&mut v, &|a: &Event, b: &Event| b.value.cmp(&a.value));
        assert!(v.windows(2).all(|w| w[0].value >= w[1].value));
        let mut slots: Vec<i32> = v.iter().map(|e| e.slot).collect();
        slots.sort();
        assert_eq!(slots, (0..40).collect::<Vec<_>>());
    }
}
