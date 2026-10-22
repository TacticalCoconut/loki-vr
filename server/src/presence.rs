//! Floating headsets, voice and the game-data check: Loki's own side channel, OUTSIDE the RO protocol (nothing here is
//! ported from rAthena: rAthena has no such thing, and no RO packet is read or written here). On an rAthena server
//! this is Loki Host's pose relay (tools/lokihost/Core/PoseRelay.cs, a TCP side channel); on Loki Online the module is
//! that relay. The messages are the relay's own (client/Assets/Loki/Runtime/Net/PresenceWire.cs, without the TCP
//! length prefix), so both ends of the client's code stay what they are:
//!   up, as the argument of the `presence` reducer: HELLO 0x01, POSE 0x02, LEAVE 0x03, VOICE 0x05, VOICE_STOP 0x06,
//!       DATA 0x07;
//!   down, as `presence_out` rows: WELCOME 0x81, POSE_OF 0x82, GONE 0x83, VOICE_OF 0x84, VOICE_END 0x85, DATA_HOST 0x86.
//!
//! Who sees what is decided by the WORLD's own rows, never by what a client says:
//!   - a client is known here once it said HELLO while its character is in the world (`pc`), and `presence_here` holds
//!     the map the world has it on: set when the character is put on a map (pc.rs load_end_ack), cleared when it is
//!     taken off (pc.rs on_remove_map: a warp, a map change, quitting), the row removed when its connection closes
//!     (pc.rs on_client_gone);
//!   - a POSE is forwarded to the clients whose row is on the sender's map, as one `presence_out` row each (an event
//!     table: rows are broadcast, never stored; a visibility filter gives each client its own rows only, the shape of
//!     world.rs `packet_out`). `presence_here` is private. So a player on another map can read nothing: no pose, no
//!     name, not even who shares a headset;
//!   - leaving the map, logging out or losing the connection sends GONE to that map's clients in the same transaction.
//! Why one row per listener and not one row per player updated in place: measured, server/ARCHITECTURE.md.

use std::cell::RefCell;
use std::collections::HashMap;

use spacetimedb::{client_visibility_filter, reducer, table, Filter, Identity, ReducerContext, Table};

use crate::character::{character as _, Character};
use crate::pc::pc as _;
use crate::social::char_friends as _;
use crate::unit::unit as _;
use crate::world::Ctx;

// PresenceWire.cs / GestureWire.cs / DrawWire.cs / SpotWire.cs message types.
const HELLO: u8 = 0x01;
const POSE: u8 = 0x02;
const LEAVE: u8 = 0x03;
const VOICE: u8 = 0x05;
const VOICE_STOP: u8 = 0x06;
const DATA: u8 = 0x07;
const SETTINGS_AUTH: u8 = 0x08;
const SETTINGS_PUT: u8 = 0x09;
const GESTURE: u8 = 0x0A;
const GESTURE_READY: u8 = 0x0B;
const DRAW: u8 = 0x0C;
const DRAW_READY: u8 = 0x0D;
const SPOT: u8 = 0x0E;
const SPOT_READY: u8 = 0x0F;
const WELCOME: u8 = 0x81;
const POSE_OF: u8 = 0x82;
const GONE: u8 = 0x83;
const VOICE_OF: u8 = 0x84;
const VOICE_END: u8 = 0x85;
const DATA_HOST: u8 = 0x86;
const SETTINGS_OF: u8 = 0x87;
const SETTINGS_ACK: u8 = 0x88;
const GESTURE_OF: u8 = 0x89;
const DRAW_OF: u8 = 0x8A;
const SPOT_OF: u8 = 0x8B;
/// PresenceWire.Magic, VersionVoice, CapVoice, CapData, CapSettings, CapGesture, CapDraw, CapSpot.
const MAGIC: &[u8; 4] = b"LKPR";
const VERSION: u8 = 2;
const CAP_VOICE: u8 = 1;
const CAP_DATA: u8 = 2;
const CAP_SETTINGS: u8 = 8;
const CAP_GESTURE: u8 = 16;
const CAP_DRAW: u8 = 32;
const CAP_SPOT: u8 = 64;
/// PresenceWire settings state codes.
const SETTINGS_FOUND: u8 = 0;
const SETTINGS_NONE: u8 = 1;
const SETTINGS_STORED: u8 = 0;
const SETTINGS_OLDER: u8 = 1;
const MAX_SETTINGS_BYTES: usize = 60_000;
/// GestureWire / DrawWire audience & relation bits.
const TO_EVERYONE: u8 = 1;
const TO_FRIENDS: u8 = 2;
const TO_FAMILY: u8 = 4;
const TO_GUILD: u8 = 8;
const ALL_AUDIENCE: u8 = TO_EVERYONE | TO_FRIENDS | TO_FAMILY | TO_GUILD;
const DRAW_OP_STROKE: u8 = 1;
const DRAW_OP_ERASE: u8 = 2;
const DRAW_OP_CLEAR: u8 = 3;
/// SpotWire Chebyshev distance range in cells (PoseRelay.Limits.SpotRange = 32f).
const SPOT_RANGE: f32 = 32.0;
/// PresenceWire.MaxMapBytes / MaxNameBytes / PoseFloats / MaxVoiceData / MaxStampBytes / MaxLabelBytes.
const MAX_MAP_BYTES: usize = 15;
const MAX_NAME_BYTES: usize = 48;
const POSE_FLOATS: usize = 21;
const MAX_VOICE_DATA: usize = 200;
const MAX_STAMP_BYTES: usize = 32;
const MAX_LABEL_BYTES: usize = 48;
/// The relay's limits (PoseRelay.Limits): poses and voice frames above these rates are dropped (a client sends 15
/// poses and, while it talks, 50 voice frames a second), and a link's DATA messages beyond the 4th.
const MAX_POSES_PER_SECOND: u32 = 40;
const MAX_VOICE_PER_SECOND: u32 = 75;
const MAX_GESTURES_PER_SECOND: u32 = 90;
const MAX_DRAWS_PER_SECOND: u32 = 60;
const MAX_SPOTS_PER_SECOND: u32 = 40;
const MAX_SETTINGS_PUTS_PER_MINUTE: u32 = 30;
const MAX_DATA_PER_LINK: u8 = 4;
/// `PresenceHere::map` while the world has the character on no map (between two maps, or not loaded yet).
const NO_MAP: u16 = u16::MAX;
/// Voice through the module when the owner set nothing (see [`PresenceConfig`]).
const DEFAULT_VOICE: bool = true;

/// Per-account settings and Codex journal (client wave 18, w18acct, and wave 28, wcodex). Private.
#[table(accessor = account_settings)]
#[derive(Clone, Debug)]
pub struct AccountSettings {
    #[primary_key]
    pub account_id: u32,
    pub saved_at: i64,
    pub json: String,
}

/// The clients linked to this channel (they said HELLO) and where the world has their characters. Private.
#[table(accessor = presence_here, index(accessor = by_map, btree(columns = [map])))]
pub struct PresenceHere {
    #[primary_key]
    pub identity: Identity,
    #[unique]
    pub aid: u32,
    /// unit.map while the character is on a map (the server's map id: two copies of an instance map are two maps),
    /// else [`NO_MAP`].
    pub map: u16,
    /// The character's name, UTF-8, at most MAX_NAME_BYTES (from the world, never from the HELLO).
    pub name: Vec<u8>,
    /// A pose was forwarded and no GONE since: a GONE is owed when the player leaves.
    pub posed: bool,
    /// A voice frame was forwarded and no VOICE_END since.
    pub voiced: bool,
    /// DATA messages taken on this link.
    pub data_msgs: u8,
}

/// What one client is sent: a PresenceWire relay → client payload. An event table: rows are broadcast, never stored.
#[table(accessor = presence_out, public, event)]
pub struct PresenceOut {
    pub target: Identity,
    pub data: Vec<u8>,
}

/// Each client receives only its own rows.
#[client_visibility_filter]
const PRESENCE_OUT_VISIBILITY: Filter = Filter::Sql("SELECT * FROM presence_out WHERE presence_out.target = :sender");

/// The owner's switches (private; one row, id 0, written with SQL by the database's owner; no row = the defaults):
///   `voice`: the module forwards voice (WELCOME's capability bit 0); off = the Voice page says the host switched it off.
///   `data_stamp` / `data_label`: the data stamp (client IO/MapDataStamp.cs, "g1-" + 16 hex digits) and patch label
///   of the Ragnarok Online install this server's maps were made from, AS THE OWNER SAYS, for a build that does not
///   know it itself: the default build embeds rAthena's stock map cache (no install's maps: other maps, other cell
///   bytes, server/ARCHITECTURE.md), so it compares only with a stamp its owner wrote here (capability bit 1);
///   empty = no check, as on a relay without a host stamp. A build with the feature `install_maps` embeds the stamp
///   of the install its maps were made from (data.rs map_stamp, tools/datagen.py --install) and compares with THAT:
///   these two fields are then not read ([`host_stamp`]).
///   `stats`: test databases only: the module keeps [`PresenceStats`] (a row write per voice frame), which the voice
///   smoke reads as it reads the relay's own counters on rAthena.
/// `spacetime sql <db> "INSERT INTO presence_config (id, voice, stats, data_stamp, data_label) VALUES (0, true, false, 'g1-…', 'patch 1486')"`
#[table(accessor = presence_config)]
pub struct PresenceConfig {
    #[primary_key]
    pub id: u32,
    pub voice: bool,
    pub stats: bool,
    pub data_stamp: String,
    pub data_label: String,
}

/// The channel's own counters, as the relay prints them (LokiPoseRelay --stats: "voice in", "voice forwarded",
/// "voice dropped"): voice frames taken from speakers, copies sent to listeners, frames dropped (over the rate, or
/// voice switched off). Kept only while `presence_config.stats` is set; public (three numbers, no names, no maps).
#[table(accessor = presence_stats, public)]
pub struct PresenceStats {
    #[primary_key]
    pub id: u32,
    pub voice_in: u64,
    pub voice_forwarded: u64,
    pub voice_dropped: u64,
}

struct Config {
    voice: bool,
    stats: bool,
    stamp: Option<String>,
    label: String,
}

fn config(db: &spacetimedb::Local) -> Config {
    let row = db.presence_config().id().find(0);
    let (stamp, label) = host_stamp(crate::data::map_stamp(), row.as_ref().map(|c| (c.data_stamp.as_str(), c.data_label.as_str())));
    match row {
        Some(c) => Config { voice: c.voice, stats: c.stats, stamp, label },
        None => Config { voice: DEFAULT_VOICE, stats: false, stamp, label },
    }
}

/// The stamp players' clients are compared with, and its label: the stamp of the install the EMBEDDED maps were made
/// from when this build has one (`embedded`: data.rs map_stamp, the `install_maps` build: what the server really walks
/// on, as Loki Host's relay answers with the stamp of the install its map cache in place was made from,
/// tools/lokihost/Core/MapDataWatch.cs); else the owner's word (`owner`: presence_config.data_stamp / data_label);
/// else none: the module then does not compare and never answers a DATA message.
fn host_stamp(embedded: Option<(&str, &str)>, owner: Option<(&str, &str)>) -> (Option<String>, String) {
    for (stamp, label) in [embedded, owner].into_iter().flatten() {
        if valid_stamp(stamp.as_bytes()) {
            return (Some(stamp.to_string()), clean_label(label));
        }
    }
    (None, String::new())
}

fn count_voice(db: &spacetimedb::Local, cfg: &Config, taken: u64, forwarded: u64, dropped: u64) {
    if !cfg.stats {
        return;
    }
    match db.presence_stats().id().find(0) {
        Some(mut s) => {
            s.voice_in += taken;
            s.voice_forwarded += forwarded;
            s.voice_dropped += dropped;
            db.presence_stats().id().update(s);
        }
        None => {
            db.presence_stats().insert(PresenceStats { id: 0, voice_in: taken, voice_forwarded: forwarded, voice_dropped: dropped });
        }
    }
}

/// One second's messages of a client (the relay's rate limits), kept in the module instance's memory: a table write
/// per pose would cost more than the pose. Counters are lost when the instance restarts, which only resets a window.
#[derive(Default, Clone)]
struct Window {
    start_ms: i64,
    poses: u32,
    voice: u32,
    gestures: u32,
    draws: u32,
    spots: u32,
    settings_min_ms: i64,
    settings_puts: u32,
    gesture_ready: bool,
    draw_ready: bool,
    draw_viewers: Vec<u32>,
    spot_ready: bool,
    spot_map: String,
    spot_x: f32,
    spot_y: f32,
    spot_payload: Vec<u8>,
}

thread_local! {
    static WINDOWS: RefCell<HashMap<Identity, Window>> = RefCell::new(HashMap::new());
}

/// Counts one message of `who` in its current second; false = over the limit, drop it.
fn within_rate(who: Identity, now_ms: i64, voice: bool) -> bool {
    within_kind_rate(who, now_ms, if voice { VOICE } else { POSE })
}

fn within_kind_rate(who: Identity, now_ms: i64, kind: u8) -> bool {
    WINDOWS.with(|w| {
        let mut w = w.borrow_mut();
        let e = w.entry(who).or_default();
        if kind == SETTINGS_PUT {
            if now_ms - e.settings_min_ms >= 60_000 || now_ms < e.settings_min_ms {
                e.settings_min_ms = now_ms;
                e.settings_puts = 0;
            }
            e.settings_puts += 1;
            return e.settings_puts <= MAX_SETTINGS_PUTS_PER_MINUTE;
        }
        if now_ms - e.start_ms >= 1000 || now_ms < e.start_ms {
            e.start_ms = now_ms;
            e.poses = 0;
            e.voice = 0;
            e.gestures = 0;
            e.draws = 0;
            e.spots = 0;
        }
        match kind {
            VOICE => {
                e.voice += 1;
                e.voice <= MAX_VOICE_PER_SECOND
            }
            GESTURE => {
                e.gestures += 1;
                e.gestures <= MAX_GESTURES_PER_SECOND
            }
            DRAW => {
                e.draws += 1;
                e.draws <= MAX_DRAWS_PER_SECOND
            }
            SPOT => {
                e.spots += 1;
                e.spots <= MAX_SPOTS_PER_SECOND
            }
            _ => {
                e.poses += 1;
                e.poses <= MAX_POSES_PER_SECOND
            }
        }
    })
}

/// One client → relay payload (PresenceWire). Anything malformed, over the rate, or sent by a client that is not
/// linked is dropped without an error (the relay closes such a connection; here a failed reducer would cost the same
/// transaction and tell the sender nothing it can use).
#[reducer]
pub fn presence(rc: &ReducerContext, data: Vec<u8>) {
    // the handshake rule of every client reducer (release.rs): a client that may not log in is not heard here either
    if crate::release::check_client(rc).is_err() {
        return;
    }
    let Some(&kind) = data.first() else { return };
    if kind == 0x00 {
        let mut o = 1usize;
        let mut count = 0usize;
        while o + 2 <= data.len() && count < 16 {
            let n = u16::from_le_bytes([data[o], data[o + 1]]) as usize;
            o += 2;
            if n == 0 || o + n > data.len() {
                break;
            }
            presence_one(rc, &data[o..o + n]);
            o += n;
            count += 1;
        }
        return;
    }
    presence_one(rc, &data);
}

fn presence_one(rc: &ReducerContext, data: &[u8]) {
    let Some(&kind) = data.first() else { return };
    let who = rc.sender();
    let db = &rc.db;
    match kind {
        HELLO => {
            if !valid_hello(&data) {
                return;
            }
            // the caller's character must be in the world with its client (a character that only waits out
            // clif_delayquit has none); the client says HELLO again until it is
            let Some(p) = db.pc().identity().find(who) else { return };
            if p.disconnected {
                return;
            }
            let Some(u) = db.unit().id().find(p.account_id) else { return };
            // a new link: whatever this client (or an older link of its account) shared before is over
            if let Some(old) = db.presence_here().identity().find(who) {
                remove(db, old);
            }
            if let Some(old) = db.presence_here().aid().find(p.account_id) {
                remove(db, old);
            }
            let mut name = u.name.into_bytes();
            if name.len() > MAX_NAME_BYTES {
                let mut n = MAX_NAME_BYTES;
                while n > 0 && (name[n] & 0xc0) == 0x80 {
                    n -= 1; // never split a UTF-8 character
                }
                name.truncate(n);
            }
            let map = if u.on_map { u.map } else { NO_MAP };
            WINDOWS.with(|w| {
                w.borrow_mut().insert(who, Window::default());
            });
            db.presence_here().insert(PresenceHere {
                identity: who,
                aid: p.account_id,
                map,
                name,
                posed: false,
                voiced: false,
                data_msgs: 0,
            });
            let cfg = config(db);
            let caps = (if cfg.voice { CAP_VOICE } else { 0 })
                | (if cfg.stamp.is_some() { CAP_DATA } else { 0 })
                | CAP_SETTINGS
                | CAP_GESTURE
                | CAP_DRAW
                | CAP_SPOT;
            let mut w = Vec::with_capacity(7);
            w.push(WELCOME);
            w.extend_from_slice(MAGIC);
            w.push(VERSION);
            w.push(caps);
            db.presence_out().insert(PresenceOut { target: who, data: w });
        }
        POSE => {
            if !valid_pose(&data) {
                return;
            }
            let Some(mut me) = db.presence_here().identity().find(who) else { return };
            if me.map == NO_MAP || !within_rate(who, now_ms(rc), false) {
                return;
            }
            let out = stamped(POSE_OF, &me, &data);
            for h in db.presence_here().by_map().filter(me.map) {
                if h.identity != who {
                    db.presence_out().insert(PresenceOut { target: h.identity, data: out.clone() });
                }
            }
            if !me.posed {
                me.posed = true;
                db.presence_here().identity().update(me);
            }
        }
        LEAVE => {
            if data.len() != 1 {
                return;
            }
            let Some(mut me) = db.presence_here().identity().find(who) else { return };
            if me.posed {
                pose_gone(db, &me);
                me.posed = false;
                db.presence_here().identity().update(me);
            }
        }
        VOICE => {
            if !valid_voice(&data) {
                return;
            }
            let Some(mut me) = db.presence_here().identity().find(who) else { return };
            let cfg = config(db);
            if !cfg.voice || !within_rate(who, now_ms(rc), true) {
                count_voice(db, &cfg, 0, 0, 1);
                return;
            }
            // the relay's rule (PresenceWire: VOICE_OF "to every other version-2 client, whatever its map (the
            // listener decides what it plays)")
            let out = stamped(VOICE_OF, &me, &data);
            let mut forwarded = 0;
            for h in db.presence_here().iter() {
                if h.identity != who {
                    db.presence_out().insert(PresenceOut { target: h.identity, data: out.clone() });
                    forwarded += 1;
                }
            }
            count_voice(db, &cfg, 1, forwarded, 0);
            if !me.voiced {
                log::info!("presence: {} shares voice", who_text(&me));
                me.voiced = true;
                db.presence_here().identity().update(me);
            }
        }
        VOICE_STOP => {
            if data.len() != 1 {
                return;
            }
            let Some(mut me) = db.presence_here().identity().find(who) else { return };
            if me.voiced {
                voice_gone(db, &me);
                log::info!("presence: {} stopped sharing voice (switched off)", who_text(&me));
                me.voiced = false;
                db.presence_here().identity().update(me);
            }
        }
        DATA => {
            let Some((stamp, label)) = read_data(&data) else { return };
            let Some(mut me) = db.presence_here().identity().find(who) else { return };
            if me.data_msgs >= MAX_DATA_PER_LINK {
                return;
            }
            me.data_msgs += 1;
            let them = who_text(&me);
            db.presence_here().identity().update(me);
            let cfg = config(db);
            let Some(host) = cfg.stamp else { return }; // no stamp of its own: the module does not compare, and never answers
            let differs = host != stamp;
            // (the relay's own log lines: tools/lokihost/Core/PoseRelay.cs OnData)
            let theirs = if label.is_empty() { stamp.clone() } else { format!("{stamp}, {label}") };
            if differs {
                log::info!("presence: {them} has OTHER game data than the host ({theirs}; the host's: {host})");
            } else {
                log::info!("presence: {them} has the host's game data ({theirs})");
            }
            let mut w = Vec::with_capacity(4 + host.len() + cfg.label.len());
            w.push(DATA_HOST);
            w.push(differs as u8);
            w.push(host.len() as u8);
            w.extend_from_slice(host.as_bytes());
            w.push(cfg.label.len() as u8);
            w.extend_from_slice(cfg.label.as_bytes());
            db.presence_out().insert(PresenceOut { target: who, data: w });
        }
        SETTINGS_AUTH => {
            if !valid_settings_auth(&data) {
                return;
            }
            let Some(me) = db.presence_here().identity().find(who) else { return };
            // On SpacetimeDB, the caller's session is already authenticated by `pc.identity() == who`.
            let w = match db.account_settings().account_id().find(me.aid) {
                Some(row) if row.saved_at > 0 && valid_settings_json(row.json.as_bytes()) => {
                    let mut v = Vec::with_capacity(10 + row.json.len());
                    v.push(SETTINGS_OF);
                    v.push(SETTINGS_FOUND);
                    v.extend_from_slice(&row.saved_at.to_le_bytes());
                    v.extend_from_slice(row.json.as_bytes());
                    v
                }
                _ => {
                    let mut v = Vec::with_capacity(10);
                    v.push(SETTINGS_OF);
                    v.push(SETTINGS_NONE);
                    v.extend_from_slice(&0i64.to_le_bytes());
                    v
                }
            };
            db.presence_out().insert(PresenceOut { target: who, data: w });
        }
        SETTINGS_PUT => {
            let Some((saved_at, json)) = read_settings_put(&data) else { return };
            let Some(me) = db.presence_here().identity().find(who) else { return };
            if !within_kind_rate(who, now_ms(rc), SETTINGS_PUT) {
                return;
            }
            let (state, held) = match db.account_settings().account_id().find(me.aid) {
                Some(old) if old.saved_at > saved_at => {
                    // Even when the client's settings timestamp is older, union-merge any new Codex / tutorial
                    // discoveries from the incoming payload into the server's newer row so progress is never lost.
                    let merged = merge_settings_json(&json, &old.json);
                    if merged != old.json && valid_settings_json(merged.as_bytes()) {
                        db.account_settings().account_id().update(AccountSettings {
                            account_id: me.aid,
                            saved_at: old.saved_at,
                            json: merged,
                        });
                    }
                    (SETTINGS_OLDER, old.saved_at)
                }
                Some(old) => {
                    let merged = merge_settings_json(&old.json, &json);
                    let store_json = if valid_settings_json(merged.as_bytes()) { merged } else { json };
                    db.account_settings().account_id().update(AccountSettings { account_id: me.aid, saved_at, json: store_json });
                    (SETTINGS_STORED, saved_at)
                }
                None => {
                    db.account_settings().insert(AccountSettings { account_id: me.aid, saved_at, json });
                    (SETTINGS_STORED, saved_at)
                }
            };
            let mut w = Vec::with_capacity(10);
            w.push(SETTINGS_ACK);
            w.push(state);
            w.extend_from_slice(&held.to_le_bytes());
            db.presence_out().insert(PresenceOut { target: who, data: w });
        }
        GESTURE_READY => {
            if data.len() != 1 {
                return;
            }
            if db.presence_here().identity().find(who).is_none() {
                return;
            }
            WINDOWS.with(|w| {
                w.borrow_mut().entry(who).or_default().gesture_ready = true;
            });
        }
        GESTURE => {
            let Some(audience) = read_gesture_header(&data) else { return };
            let Some(me) = db.presence_here().identity().find(who) else { return };
            if me.map == NO_MAP || !within_kind_rate(who, now_ms(rc), GESTURE) {
                return;
            }
            let mut out: Option<Vec<u8>> = None;
            for h in db.presence_here().by_map().filter(me.map) {
                let ready = WINDOWS.with(|w| w.borrow().get(&h.identity).map(|e| e.gesture_ready).unwrap_or(false));
                if h.identity == who || h.aid == me.aid || !ready {
                    continue;
                }
                if !allowed(db, me.aid, h.aid, audience) {
                    continue;
                }
                let frame = out.get_or_insert_with(|| {
                    let mut v = Vec::with_capacity(4 + data.len());
                    v.push(GESTURE_OF);
                    v.extend_from_slice(&me.aid.to_le_bytes());
                    v.extend_from_slice(&data[1..]);
                    v
                });
                db.presence_out().insert(PresenceOut { target: h.identity, data: frame.clone() });
            }
        }
        DRAW_READY => {
            if data.len() != 1 {
                return;
            }
            if db.presence_here().identity().find(who).is_none() {
                return;
            }
            WINDOWS.with(|w| {
                w.borrow_mut().entry(who).or_default().draw_ready = true;
            });
        }
        DRAW => {
            let Some((audience, op)) = read_draw_header(&data) else { return };
            let Some(me) = db.presence_here().identity().find(who) else { return };
            if me.map == NO_MAP || !within_kind_rate(who, now_ms(rc), DRAW) {
                return;
            }
            let mut viewers = WINDOWS.with(|w| w.borrow().get(&who).map(|e| e.draw_viewers.clone()).unwrap_or_default());
            for h in db.presence_here().by_map().filter(me.map) {
                let ready = WINDOWS.with(|w| w.borrow().get(&h.identity).map(|e| e.draw_ready).unwrap_or(false));
                if h.identity == who || h.aid == me.aid || !ready {
                    continue;
                }
                if !allowed(db, me.aid, h.aid, audience) {
                    if let Some(pos) = viewers.iter().position(|&id| id == h.aid) {
                        viewers.swap_remove(pos);
                        let mut revoke = Vec::with_capacity(9);
                        revoke.push(DRAW_OF);
                        revoke.extend_from_slice(&me.aid.to_le_bytes());
                        revoke.push(0); // relation = 0
                        revoke.push(1); // Format = 1
                        revoke.push(TO_EVERYONE);
                        revoke.push(DRAW_OP_CLEAR);
                        db.presence_out().insert(PresenceOut { target: h.identity, data: revoke });
                    }
                    continue;
                }
                let rel = relation_bits(db, me.aid, h.aid);
                let mut v = Vec::with_capacity(5 + data.len());
                v.push(DRAW_OF);
                v.extend_from_slice(&me.aid.to_le_bytes());
                v.push(rel);
                v.extend_from_slice(&data[1..]);
                db.presence_out().insert(PresenceOut { target: h.identity, data: v });
                if op == DRAW_OP_STROKE {
                    if !viewers.contains(&h.aid) {
                        if viewers.len() >= 256 {
                            viewers.clear();
                        }
                        viewers.push(h.aid);
                    }
                } else if op == DRAW_OP_CLEAR {
                    if let Some(pos) = viewers.iter().position(|&id| id == h.aid) {
                        viewers.swap_remove(pos);
                    }
                }
            }
            WINDOWS.with(|w| {
                w.borrow_mut().entry(who).or_default().draw_viewers = viewers;
            });
        }
        SPOT_READY => {
            if data.len() != 1 {
                return;
            }
            let Some(me) = db.presence_here().identity().find(who) else { return };
            let (was_ready, spot_map, mx, my) = WINDOWS.with(|w| {
                let mut w = w.borrow_mut();
                let e = w.entry(who).or_default();
                let was = e.spot_ready;
                e.spot_ready = true;
                (was, e.spot_map.clone(), e.spot_x, e.spot_y)
            });
            if !was_ready && !spot_map.is_empty() && me.map != NO_MAP {
                for h in db.presence_here().by_map().filter(me.map) {
                    if h.identity == who || h.aid == 0 {
                        continue;
                    }
                    let peer = WINDOWS.with(|w| {
                        w.borrow().get(&h.identity).and_then(|e| {
                            if e.spot_map == spot_map && e.spot_payload.len() > 1 && spot_dist(mx, my, e.spot_x, e.spot_y) <= SPOT_RANGE {
                                Some(e.spot_payload.clone())
                            } else {
                                None
                            }
                        })
                    });
                    if let Some(payload) = peer {
                        let mut v = Vec::with_capacity(4 + payload.len());
                        v.push(SPOT_OF);
                        v.extend_from_slice(&h.aid.to_le_bytes());
                        v.extend_from_slice(&payload[1..]);
                        db.presence_out().insert(PresenceOut { target: who, data: v });
                    }
                }
            }
        }
        SPOT => {
            let Some((spot_map, x, y)) = read_spot(&data) else { return };
            let Some(me) = db.presence_here().identity().find(who) else { return };
            if !within_kind_rate(who, now_ms(rc), SPOT) {
                return;
            }
            let (map_changed, my_ready) = WINDOWS.with(|w| {
                let mut w = w.borrow_mut();
                let e = w.entry(who).or_default();
                let changed = e.spot_map != spot_map;
                e.spot_map = spot_map.clone();
                e.spot_x = x;
                e.spot_y = y;
                e.spot_payload = data.to_vec();
                (changed, e.spot_ready)
            });
            if me.map == NO_MAP {
                return;
            }
            let my_aid = me.aid;
            let mut out: Option<Vec<u8>> = None;
            let candidates: Vec<PresenceHere> = db.presence_here().by_map().filter(me.map).collect();
            for h in candidates {
                if h.identity == who || h.aid == my_aid {
                    continue;
                }
                let peer = WINDOWS.with(|w| {
                    w.borrow().get(&h.identity).and_then(|e| {
                        if e.spot_map == spot_map && spot_dist(e.spot_x, e.spot_y, x, y) <= SPOT_RANGE {
                            Some((e.spot_ready, e.spot_payload.clone()))
                        } else {
                            None
                        }
                    })
                });
                let Some((peer_ready, peer_payload)) = peer else { continue };
                if peer_ready {
                    let frame = out.get_or_insert_with(|| {
                        let mut v = Vec::with_capacity(4 + data.len());
                        v.push(SPOT_OF);
                        v.extend_from_slice(&my_aid.to_le_bytes());
                        v.extend_from_slice(&data[1..]);
                        v
                    });
                    db.presence_out().insert(PresenceOut { target: h.identity, data: frame.clone() });
                }
                if map_changed && my_ready && peer_payload.len() > 1 {
                    let mut v = Vec::with_capacity(4 + peer_payload.len());
                    v.push(SPOT_OF);
                    v.extend_from_slice(&h.aid.to_le_bytes());
                    v.extend_from_slice(&peer_payload[1..]);
                    db.presence_out().insert(PresenceOut { target: who, data: v });
                }
            }
        }
        _ => {}
    }
}

/// "Name (AID n)", as the relay's log names a player (tools/lokihost/Core/PoseRelay.cs Say): the module's own log
/// says the same things in the same words, so what reads the relay's log on rAthena reads this one on Loki Online.
fn who_text(me: &PresenceHere) -> String {
    format!("{} (AID {})", String::from_utf8_lossy(&me.name), me.aid)
}

fn now_ms(rc: &ReducerContext) -> i64 {
    rc.timestamp.to_micros_since_unix_epoch() / 1000
}

/// POSE_OF / VOICE_OF: the sender's AID and name (the world's, never the client's word), then the body as received.
fn stamped(kind: u8, me: &PresenceHere, payload: &[u8]) -> Vec<u8> {
    let mut out = Vec::with_capacity(6 + me.name.len() + payload.len() - 1);
    out.push(kind);
    out.extend_from_slice(&me.aid.to_le_bytes());
    out.push(me.name.len() as u8);
    out.extend_from_slice(&me.name);
    out.extend_from_slice(&payload[1..]);
    out
}

/// PresenceWire.TryDecodeHello: the type byte, the magic, version 2 (this channel never spoke version 1), an AID and
/// a name of at most 48 bytes that fills the rest. The AID and the name it claims are not used.
fn valid_hello(d: &[u8]) -> bool {
    d.len() >= 11 && &d[1..5] == MAGIC && d[5] == VERSION && d[10] as usize <= MAX_NAME_BYTES && d.len() == 11 + d[10] as usize
}

/// PresenceWire.TryDecodePose + Valid: the type byte, a map name of 1..15 valid characters, seq, metres per cell,
/// flags, 21 floats; finite numbers, positions within 100,000 cells, rotations of length 0.5..1.5, a sane scale.
fn valid_pose(d: &[u8]) -> bool {
    if d.len() < 3 {
        return false;
    }
    let m = d[1] as usize;
    if m < 1 || m > MAX_MAP_BYTES || d.len() != 2 + m + 2 + 4 + 1 + 4 * POSE_FLOATS {
        return false;
    }
    if !d[2..2 + m].iter().all(|&c| valid_map_char(c)) {
        return false;
    }
    let f = |o: usize| f32::from_le_bytes([d[o], d[o + 1], d[o + 2], d[o + 3]]);
    let mpc = f(2 + m + 2);
    if !(mpc >= 0.0001 && mpc <= 100.0) {
        return false; // (NaN fails both comparisons)
    }
    let base = 2 + m + 2 + 4 + 1;
    for k in 0..3 {
        let o = base + k * 28;
        let mut q = 0f64;
        for i in 0..7 {
            let v = f(o + i * 4);
            if !v.is_finite() {
                return false;
            }
            if i < 3 {
                if v.abs() > 100000.0 {
                    return false;
                }
            } else {
                q += v as f64 * v as f64;
            }
        }
        if !(0.25..=2.25).contains(&q) {
            return false;
        }
    }
    true
}

/// PresenceWire.TryReadVoice: the type byte, a map name, seq, a codec (not 0), flags, 1..200 bytes of data.
fn valid_voice(d: &[u8]) -> bool {
    if d.len() < 3 {
        return false;
    }
    let m = d[1] as usize;
    if m < 1 || m > MAX_MAP_BYTES || d.len() < 2 + m + 4 + 1 {
        return false;
    }
    if !d[2..2 + m].iter().all(|&c| valid_map_char(c)) {
        return false;
    }
    let codec = d[2 + m + 2];
    let n = d.len() - (2 + m + 4);
    codec != 0 && (1..=MAX_VOICE_DATA).contains(&n)
}

/// PresenceWire.ValidMapChar.
fn valid_map_char(c: u8) -> bool {
    c.is_ascii_alphanumeric() || c == b'_' || c == b'-' || c == b'.' || c == b'@'
}

/// PresenceWire.ValidStamp: 1..32 printable ASCII characters, no space.
fn valid_stamp(s: &[u8]) -> bool {
    !s.is_empty() && s.len() <= MAX_STAMP_BYTES && s.iter().all(|&c| (0x21..=0x7e).contains(&c))
}

fn valid_settings_auth(d: &[u8]) -> bool {
    if d.len() == 1 {
        return true;
    }
    let Some(&n) = d.get(1) else { return false };
    let n = n as usize;
    n <= MAX_STAMP_BYTES && d.len() == 2 + n && d[2..].iter().all(|&c| (0x21..=0x7e).contains(&c))
}

fn valid_settings_json(b: &[u8]) -> bool {
    (2..=MAX_SETTINGS_BYTES).contains(&b.len()) && b[0] == b'{' && std::str::from_utf8(b).is_ok()
}

fn read_settings_put(d: &[u8]) -> Option<(i64, String)> {
    if d.len() < 1 + 8 + 2 || d[0] != SETTINGS_PUT {
        return None;
    }
    let saved_at = i64::from_le_bytes([d[1], d[2], d[3], d[4], d[5], d[6], d[7], d[8]]);
    if saved_at <= 0 || !valid_settings_json(&d[9..]) {
        return None;
    }
    let json = std::str::from_utf8(&d[9..]).ok()?.to_string();
    Some((saved_at, json))
}

const B64_CHARS: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

fn b64_val(b: u8) -> Option<u8> {
    match b {
        b'A'..=b'Z' => Some(b - b'A'),
        b'a'..=b'z' => Some(b - b'a' + 26),
        b'0'..=b'9' => Some(b - b'0' + 52),
        b'+' => Some(62),
        b'/' => Some(63),
        _ => None,
    }
}

fn base64_decode(s: &str) -> Option<Vec<u8>> {
    let bytes = s.trim().as_bytes();
    if bytes.is_empty() {
        return Some(Vec::new());
    }
    let mut out = Vec::with_capacity(bytes.len() * 3 / 4);
    let mut buf = 0u32;
    let mut bits = 0u8;
    for &b in bytes {
        if b == b'=' {
            break;
        }
        let v = b64_val(b)? as u32;
        buf = (buf << 6) | v;
        bits += 6;
        if bits >= 8 {
            bits -= 8;
            out.push(((buf >> bits) & 0xff) as u8);
        }
    }
    Some(out)
}

fn base64_encode(bytes: &[u8]) -> String {
    if bytes.is_empty() {
        return String::new();
    }
    let mut out = String::with_capacity((bytes.len() + 2) / 3 * 4);
    for chunk in bytes.chunks(3) {
        let b0 = chunk[0] as u32;
        let b1 = if chunk.len() > 1 { chunk[1] as u32 } else { 0 };
        let b2 = if chunk.len() > 2 { chunk[2] as u32 } else { 0 };
        let n = (b0 << 16) | (b1 << 8) | b2;
        out.push(B64_CHARS[((n >> 18) & 0x3f) as usize] as char);
        out.push(B64_CHARS[((n >> 12) & 0x3f) as usize] as char);
        if chunk.len() > 1 {
            out.push(B64_CHARS[((n >> 6) & 0x3f) as usize] as char);
        } else {
            out.push('=');
        }
        if chunk.len() > 2 {
            out.push(B64_CHARS[(n & 0x3f) as usize] as char);
        } else {
            out.push('=');
        }
    }
    out
}

/// Matches client `CodexData.Decode`: unsigned LEB128 delta-encoded positive integer IDs in base64.
pub fn decode_codex_ids(s: &str) -> Vec<i32> {
    if s.is_empty() {
        return Vec::new();
    }
    let Some(b) = base64_decode(s) else {
        return Vec::new();
    };
    let mut out = Vec::new();
    let mut prev: i32 = 0;
    let mut i = 0;
    while i < b.len() {
        let mut d: u32 = 0;
        let mut shift: u32 = 0;
        while i < b.len() {
            let x = b[i];
            i += 1;
            d |= ((x & 0x7f) as u32) << shift;
            shift += 7;
            if (x & 0x80) == 0 || shift > 28 {
                break;
            }
        }
        prev = prev.saturating_add(d as i32);
        if prev > 0 {
            out.push(prev);
        }
    }
    out
}

/// Matches client `CodexData.Encode`: sorted unique positive integer IDs as unsigned LEB128 deltas in base64.
pub fn encode_codex_ids(ids: &[i32]) -> String {
    let mut sorted: Vec<i32> = ids.iter().copied().filter(|&x| x > 0).collect();
    sorted.sort_unstable();
    sorted.dedup();
    if sorted.is_empty() {
        return String::new();
    }
    let mut b = Vec::with_capacity(sorted.len() * 2);
    let mut prev: i32 = 0;
    for id in sorted {
        let mut d = (id - prev) as u32;
        prev = id;
        while d >= 0x80 {
            b.push((d as u8) | 0x80);
            d >>= 7;
        }
        b.push(d as u8);
    }
    base64_encode(&b)
}

fn union_codex_field(old_val: Option<&serde_json::Value>, new_val: Option<&serde_json::Value>) -> Option<serde_json::Value> {
    match (old_val, new_val) {
        (Some(serde_json::Value::String(os)), Some(serde_json::Value::String(ns))) => {
            let mut ids = decode_codex_ids(os);
            ids.extend(decode_codex_ids(ns));
            Some(serde_json::Value::String(encode_codex_ids(&ids)))
        }
        (Some(serde_json::Value::Array(oa)), Some(serde_json::Value::Array(na))) => {
            let mut out = na.clone();
            for item in oa {
                if !out.contains(item) {
                    out.push(item.clone());
                }
            }
            Some(serde_json::Value::Array(out))
        }
        (Some(o), None) => Some(o.clone()),
        (None, Some(n)) => Some(n.clone()),
        (None, None) => None,
        (_, Some(n)) => Some(n.clone()),
    }
}

fn merge_codex_objects(
    old_map: &serde_json::Map<String, serde_json::Value>,
    new_map: &serde_json::Map<String, serde_json::Value>,
) -> serde_json::Map<String, serde_json::Value> {
    let mut out = new_map.clone();
    for (k, v) in old_map {
        if !out.contains_key(k) {
            out.insert(k.clone(), v.clone());
        }
    }
    // Keep non-empty account tag
    if let (Some(serde_json::Value::String(oa)), Some(serde_json::Value::String(na))) = (old_map.get("acct"), new_map.get("acct")) {
        if na.is_empty() && !oa.is_empty() {
            out.insert("acct".to_string(), serde_json::Value::String(oa.clone()));
        }
    }
    // Full & seen: a creature in `full` is removed from `seen`
    let old_full = old_map.get("full").and_then(|v| v.as_str()).map(decode_codex_ids).unwrap_or_default();
    let new_full = new_map.get("full").and_then(|v| v.as_str()).map(decode_codex_ids).unwrap_or_default();
    let old_seen = old_map.get("seen").and_then(|v| v.as_str()).map(decode_codex_ids).unwrap_or_default();
    let new_seen = new_map.get("seen").and_then(|v| v.as_str()).map(decode_codex_ids).unwrap_or_default();
    if old_map.contains_key("full") || new_map.contains_key("full") || old_map.contains_key("seen") || new_map.contains_key("seen") {
        let mut full_all = old_full;
        full_all.extend(new_full);
        full_all.sort_unstable();
        full_all.dedup();
        let mut seen_all = old_seen;
        seen_all.extend(new_seen);
        seen_all.retain(|id| full_all.binary_search(id).is_err());
        out.insert("full".to_string(), serde_json::Value::String(encode_codex_ids(&full_all)));
        out.insert("seen".to_string(), serde_json::Value::String(encode_codex_ids(&seen_all)));
    }
    // Monotonic set fields (LEB128 base64 strings or JSON arrays)
    for key in ["items", "heard", "scanned", "claimed"] {
        if let Some(merged) = union_codex_field(old_map.get(key), new_map.get(key)) {
            out.insert(key.to_string(), merged);
        }
    }
    // Kill counters (object map of mob_id -> count): take per-mob max
    if let (Some(serde_json::Value::Object(ok)), Some(serde_json::Value::Object(nk))) = (old_map.get("kills"), new_map.get("kills")) {
        let mut km = nk.clone();
        for (mob, ov) in ok {
            let oc = ov.as_i64().unwrap_or(0);
            let nc = km.get(mob).and_then(|v| v.as_i64()).unwrap_or(0);
            if oc > nc {
                km.insert(mob.clone(), serde_json::Value::Number(oc.into()));
            }
        }
        out.insert("kills".to_string(), serde_json::Value::Object(km));
    }
    // Pending comma-separated IDs: if incoming codex was empty/wiped while old had pending, preserve old pending
    if let (Some(serde_json::Value::String(op)), Some(serde_json::Value::String(np))) = (old_map.get("pending"), new_map.get("pending")) {
        let new_was_empty = new_map.get("full").and_then(|v| v.as_str()).unwrap_or("").is_empty()
            && new_map.get("seen").and_then(|v| v.as_str()).unwrap_or("").is_empty()
            && new_map.get("items").and_then(|v| v.as_str()).unwrap_or("").is_empty()
            && new_map.get("heard").and_then(|v| v.as_str()).unwrap_or("").is_empty();
        if np.is_empty() && !op.is_empty() && new_was_empty {
            out.insert("pending".to_string(), serde_json::Value::String(op.clone()));
        } else if !op.is_empty() && !np.is_empty() {
            let mut list: Vec<&str> = np.split(',').filter(|s| !s.is_empty()).collect();
            for p in op.split(',').filter(|s| !s.is_empty()) {
                if !list.contains(&p) {
                    list.push(p);
                }
            }
            out.insert("pending".to_string(), serde_json::Value::String(list.join(",")));
        }
    }
    // Recent entries (newest first, up to 8)
    if let (Some(serde_json::Value::Array(or)), Some(serde_json::Value::Array(nr))) = (old_map.get("recent"), new_map.get("recent")) {
        let mut r = Vec::new();
        for item in nr.iter().chain(or.iter()) {
            if let Some(s) = item.as_str() {
                if !s.is_empty() && !r.iter().any(|x: &serde_json::Value| x.as_str() == Some(s)) && r.len() < 8 {
                    r.push(serde_json::Value::String(s.to_string()));
                }
            }
        }
        out.insert("recent".to_string(), serde_json::Value::Array(r));
    }
    out
}

fn merge_codex_value(old_val: &serde_json::Value, new_val: &serde_json::Value) -> serde_json::Value {
    match (old_val, new_val) {
        (serde_json::Value::Object(om), serde_json::Value::Object(nm)) => {
            serde_json::Value::Object(merge_codex_objects(om, nm))
        }
        (serde_json::Value::String(os), serde_json::Value::String(ns)) => {
            if let (Ok(serde_json::Value::Object(om)), Ok(serde_json::Value::Object(nm))) = (
                serde_json::from_str::<serde_json::Value>(os),
                serde_json::from_str::<serde_json::Value>(ns),
            ) {
                let merged = merge_codex_objects(&om, &nm);
                if let Ok(s) = serde_json::to_string(&serde_json::Value::Object(merged)) {
                    return serde_json::Value::String(s);
                }
            }
            if ns.is_empty() && !os.is_empty() {
                serde_json::Value::String(os.clone())
            } else {
                serde_json::Value::String(ns.clone())
            }
        }
        _ => new_val.clone(),
    }
}

/// Non-destructively merges `old_json` and `new_json` (`new_json` wins on scalar preferences, while any top-level
/// keys missing in `new_json`, per-character arrays, `tutorialChars`, and all `codex` Bestiary/Itemarium discoveries
/// are preserved and union-merged so player progress is never lost across updates).
pub fn merge_settings_json(old_json: &str, new_json: &str) -> String {
    let (Ok(serde_json::Value::Object(old_map)), Ok(serde_json::Value::Object(new_map))) = (
        serde_json::from_str::<serde_json::Value>(old_json),
        serde_json::from_str::<serde_json::Value>(new_json),
    ) else {
        return new_json.to_string();
    };
    let mut out = new_map.clone();
    for (k, old_v) in &old_map {
        if !out.contains_key(k) {
            out.insert(k.clone(), old_v.clone());
            continue;
        }
        if k == "codex" {
            let merged_codex = merge_codex_value(old_v, &out["codex"]);
            out.insert(k.clone(), merged_codex);
        } else if k == "tutorialChars" {
            if let (serde_json::Value::Array(oa), Some(serde_json::Value::Array(na))) = (old_v, out.get(k)) {
                let mut combined = na.clone();
                for item in oa {
                    if !combined.contains(item) {
                        combined.push(item.clone());
                    }
                }
                out.insert(k.clone(), serde_json::Value::Array(combined));
            }
        } else if matches!(k.as_str(), "wandSlots" | "quickTabs" | "bubbleSets" | "itemButtons") {
            if let (serde_json::Value::Array(oa), Some(serde_json::Value::Array(na))) = (old_v, out.get(k)) {
                let mut combined = na.clone();
                for old_entry in oa {
                    let Some(ch) = old_entry.get("character").and_then(|v| v.as_str()) else { continue };
                    if !ch.is_empty() && !combined.iter().any(|e| e.get("character").and_then(|v| v.as_str()) == Some(ch)) {
                        combined.push(old_entry.clone());
                    }
                }
                out.insert(k.clone(), serde_json::Value::Array(combined));
            }
        }
    }
    match serde_json::to_string(&serde_json::Value::Object(out)) {
        Ok(s) if s.len() <= MAX_SETTINGS_BYTES => s,
        _ => new_json.to_string(),
    }
}

/// GestureWire.TryReadHeader: [0x0A, Format=1, audience, menuKind, state, ...]
fn read_gesture_header(d: &[u8]) -> Option<u8> {
    if d.len() < 5 || d.len() > 270 || d[0] != GESTURE || d[1] != 1 {
        return None;
    }
    let audience = d[2];
    if audience == 0 || (audience & !ALL_AUDIENCE) != 0 {
        return None;
    }
    if d[3] > 4 || d[4] > 5 {
        return None;
    }
    Some(audience)
}

/// DrawWire.TryReadHeader: [0x0C, Format=1, audience, op, ...]
fn read_draw_header(d: &[u8]) -> Option<(u8, u8)> {
    if d.len() < 4 || d.len() > 198 || d[0] != DRAW || d[1] != 1 {
        return None;
    }
    let audience = d[2];
    if (audience & !ALL_AUDIENCE) != 0 {
        return None;
    }
    let op = d[3];
    match op {
        DRAW_OP_STROKE if d.len() >= 19 => Some((audience, op)),
        DRAW_OP_ERASE if d.len() == 6 => Some((audience, op)),
        DRAW_OP_CLEAR if d.len() == 4 => Some((audience, op)),
        _ => None,
    }
}

/// SpotWire.TryReadSpot: [0x0E, Format=1, map_len, map_bytes..., seq(u16), x(f32), y(f32), vx(f32), vy(f32), dir(u8)]
fn read_spot(d: &[u8]) -> Option<(String, f32, f32)> {
    if d.len() < 3 || d[0] != SPOT || d[1] != 1 {
        return None;
    }
    let m = d[2] as usize;
    if m < 1 || m > MAX_MAP_BYTES || d.len() != 3 + m + 2 + 16 + 1 {
        return None;
    }
    if !d[3..3 + m].iter().all(|&c| valid_map_char(c)) {
        return None;
    }
    let map = String::from_utf8_lossy(&d[3..3 + m]).into_owned();
    let o = 3 + m + 2;
    let f = |i: usize| f32::from_le_bytes([d[o + i], d[o + i + 1], d[o + i + 2], d[o + i + 3]]);
    let (x, y, vx, vy) = (f(0), f(4), f(8), f(12));
    let dir = d[o + 16];
    if !x.is_finite() || !y.is_finite() || !vx.is_finite() || !vy.is_finite() {
        return None;
    }
    if x.abs() > 10000.0 || y.abs() > 10000.0 || vx.abs() > 1000.0 || vy.abs() > 1000.0 || dir >= 8 {
        return None;
    }
    Some((map, x, y))
}

fn spot_dist(ax: f32, ay: f32, bx: f32, by: f32) -> f32 {
    (ax - bx).abs().max((ay - by).abs())
}

fn active_char(db: &spacetimedb::Local, aid: u32) -> Option<Character> {
    if let Some(p) = db.pc().account_id().find(aid) {
        if let Some(ch) = db.character().id().find(p.char_id) {
            return Some(ch);
        }
    }
    if let Some(h) = db.presence_here().aid().find(aid) {
        let name = String::from_utf8_lossy(&h.name);
        if let Some(ch) = db.character().account_id().filter(aid).find(|c| c.name == name) {
            return Some(ch);
        }
    }
    db.character().account_id().filter(aid).next()
}

fn relation_bits(db: &spacetimedb::Local, sender_aid: u32, viewer_aid: u32) -> u8 {
    let (Some(a), Some(b)) = (active_char(db, sender_aid), active_char(db, viewer_aid)) else {
        return 0;
    };
    let mut bits = 0u8;
    let a_friends = db.char_friends().char_id().find(a.id).map(|r| r.friends).unwrap_or_default();
    let b_friends = db.char_friends().char_id().find(b.id).map(|r| r.friends).unwrap_or_default();
    if a_friends.iter().any(|f| f.char_id == b.id) || b_friends.iter().any(|f| f.char_id == a.id) {
        bits |= TO_FRIENDS;
    }
    let holds = |ch: &Character, id: u32| id != 0 && (ch.partner_id == id || ch.father == id || ch.mother == id || ch.child == id);
    if holds(&a, b.id) || holds(&b, a.id) {
        bits |= TO_FAMILY;
    }
    if a.guild_id != 0 && a.guild_id == b.guild_id {
        bits |= TO_GUILD;
    }
    bits
}

fn allowed(db: &spacetimedb::Local, sender_aid: u32, viewer_aid: u32, audience: u8) -> bool {
    if (audience & TO_EVERYONE) != 0 {
        return true;
    }
    (relation_bits(db, sender_aid, viewer_aid) & audience) != 0
}

/// PresenceWire.LabelBytes: no control characters, at most 48 UTF-8 bytes (cut at a character).
fn clean_label(s: &str) -> String {
    let mut out = String::new();
    for ch in s.chars().filter(|c| !c.is_control()) {
        if out.len() + ch.len_utf8() > MAX_LABEL_BYTES {
            break;
        }
        out.push(ch);
    }
    out
}

/// PresenceWire.TryDecodeData: the type byte, u8 n, stamp[n], u8 m, label[m] (UTF-8, no control characters), nothing after.
fn read_data(d: &[u8]) -> Option<(String, String)> {
    let n = *d.get(1)? as usize;
    let stamp = d.get(2..2 + n)?;
    if !valid_stamp(stamp) {
        return None;
    }
    let m = *d.get(2 + n)? as usize;
    if m > MAX_LABEL_BYTES || d.len() != 3 + n + m {
        return None;
    }
    let label = std::str::from_utf8(&d[3 + n..]).ok()?;
    if label.chars().any(|c| c.is_control()) {
        return None;
    }
    Some((String::from_utf8_lossy(stamp).into_owned(), label.to_string()))
}

fn frame5(kind: u8, aid: u32) -> Vec<u8> {
    let mut v = Vec::with_capacity(5);
    v.push(kind);
    v.extend_from_slice(&aid.to_le_bytes());
    v
}

/// The others on this player's map stop seeing its headset.
fn pose_gone(db: &spacetimedb::Local, me: &PresenceHere) {
    if me.map == NO_MAP {
        return;
    }
    let gone = frame5(GONE, me.aid);
    for h in db.presence_here().by_map().filter(me.map) {
        if h.identity != me.identity {
            db.presence_out().insert(PresenceOut { target: h.identity, data: gone.clone() });
        }
    }
}

/// Every listener frees this speaker's stream.
fn voice_gone(db: &spacetimedb::Local, me: &PresenceHere) {
    let end = frame5(VOICE_END, me.aid);
    for h in db.presence_here().iter() {
        if h.identity != me.identity {
            db.presence_out().insert(PresenceOut { target: h.identity, data: end.clone() });
        }
    }
}

/// The link is over: GONE and VOICE_END where they are owed, then the row.
fn remove(db: &spacetimedb::Local, me: PresenceHere) {
    if me.posed {
        pose_gone(db, &me);
    }
    if me.voiced {
        voice_gone(db, &me);
    }
    WINDOWS.with(|w| w.borrow_mut().remove(&me.identity));
    db.presence_here().identity().delete(me.identity);
}

/// World hook (pc.rs on_remove_map: unit_remove_map's player part): the player leaves its map (a warp, a map change,
/// quitting): its headset goes from that map at once. Its voice goes on (the relay's rule: voice is the server's, not
/// a map's).
pub fn on_remove_map(c: &Ctx, aid: u32) {
    let Some(mut me) = c.db().presence_here().aid().find(aid) else { return };
    if me.posed {
        pose_gone(c.db(), &me);
    }
    me.posed = false;
    me.map = NO_MAP;
    let who = me.identity;
    c.db().presence_here().identity().update(me);
    WINDOWS.with(|w| {
        if let Some(e) = w.borrow_mut().get_mut(&who) {
            e.draw_viewers.clear();
            e.spot_map.clear();
            e.spot_payload.clear();
        }
    });
}

/// World hook (pc.rs load_end_ack, after map_addblock): the player is on a map: its poses go to that map's clients
/// from now on, and theirs to it.
pub fn on_enter_map(c: &Ctx, aid: u32, map: u16) {
    let Some(mut me) = c.db().presence_here().aid().find(aid) else { return };
    if me.map != map {
        me.map = map;
        c.db().presence_here().identity().update(me);
    }
}

/// World hook (pc.rs on_client_gone: the connection closed, or the client left for the character screen): the
/// headset and the voice go at once, also while the character itself waits out clif_delayquit.
pub fn on_client_gone(c: &Ctx, who: Identity) {
    if let Some(me) = c.db().presence_here().identity().find(who) {
        log::info!("presence: {} disconnected: its connection to the world closed", who_text(&me));
        remove(c.db(), me);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn pose(map: &str) -> Vec<u8> {
        let mut d = vec![POSE, map.len() as u8];
        d.extend_from_slice(map.as_bytes());
        d.extend_from_slice(&7u16.to_le_bytes());
        d.extend_from_slice(&0.02f32.to_le_bytes());
        d.push(3);
        for _ in 0..3 {
            for v in [1.0f32, 2.0, 3.0, 0.0, 0.0, 0.0, 1.0] {
                d.extend_from_slice(&v.to_le_bytes());
            }
        }
        d
    }

    #[test]
    fn pose_validation_follows_presence_wire() {
        let good = pose("prontera");
        assert_eq!(good.len(), 93 + 8);
        assert!(valid_pose(&good));
        assert!(valid_pose(&pose("1@tower")));
        assert!(!valid_pose(&pose("")));
        assert!(!valid_pose(&pose("a_map_name_too_long")));
        assert!(!valid_pose(&pose("bad map")));
        let mut cut = good.clone();
        cut.pop();
        assert!(!valid_pose(&cut));
        let mut long = good.clone();
        long.push(0);
        assert!(!valid_pose(&long));
        // a NaN position, a position beyond 100,000 cells, a zero rotation, a scale of 0
        let base = 2 + 8 + 2 + 4 + 1;
        let mut nan = good.clone();
        nan[base..base + 4].copy_from_slice(&f32::NAN.to_le_bytes());
        assert!(!valid_pose(&nan));
        let mut far = good.clone();
        far[base..base + 4].copy_from_slice(&200000f32.to_le_bytes());
        assert!(!valid_pose(&far));
        let mut flat = good.clone();
        flat[base + 24..base + 28].copy_from_slice(&0f32.to_le_bytes());
        assert!(!valid_pose(&flat));
        let mut scale = good.clone();
        scale[12..16].copy_from_slice(&0f32.to_le_bytes());
        assert!(!valid_pose(&scale));
        scale[12..16].copy_from_slice(&f32::NAN.to_le_bytes());
        assert!(!valid_pose(&scale));
    }

    #[test]
    fn voice_validation_follows_presence_wire() {
        let mut v = vec![VOICE, 8];
        v.extend_from_slice(b"prontera");
        v.extend_from_slice(&1u16.to_le_bytes());
        v.push(1); // codec
        v.push(0); // flags
        assert!(!valid_voice(&v)); // no data
        v.extend_from_slice(&[0u8; 163]);
        assert!(valid_voice(&v));
        let mut zero_codec = v.clone();
        zero_codec[12] = 0;
        assert!(!valid_voice(&zero_codec));
        let mut long = v.clone();
        long.extend_from_slice(&[0u8; 38]);
        assert!(!valid_voice(&long)); // 201 bytes of data
    }

    #[test]
    fn hello_and_data_follow_presence_wire() {
        // HELLO: type, "LKPR", version, AID, n, name
        let mut h = vec![HELLO];
        h.extend_from_slice(b"LKPR");
        h.push(2);
        h.extend_from_slice(&2000001u32.to_le_bytes());
        h.push(6);
        h.extend_from_slice(b"Tester");
        assert!(valid_hello(&h));
        let mut v1 = h.clone();
        v1[5] = 1;
        assert!(!valid_hello(&v1)); // version 1 is the relay's past, never this channel's
        let mut more = h.clone();
        more.push(b'x');
        assert!(!valid_hello(&more));
        let mut magic = h.clone();
        magic[1] = b'X';
        assert!(!valid_hello(&magic));
        // DATA: type, n, stamp, m, label
        let mut d = vec![DATA, 19];
        d.extend_from_slice(b"g1-603785af4ecfea8d");
        d.push(10);
        d.extend_from_slice(b"patch 1486");
        assert_eq!(read_data(&d), Some(("g1-603785af4ecfea8d".to_string(), "patch 1486".to_string())));
        let mut no_label = vec![DATA, 2];
        no_label.extend_from_slice(b"g1");
        no_label.push(0);
        assert_eq!(read_data(&no_label), Some(("g1".to_string(), String::new())));
        let mut space = vec![DATA, 3];
        space.extend_from_slice(b"g 1");
        space.push(0);
        assert_eq!(read_data(&space), None);
        let mut tail = d.clone();
        tail.push(0);
        assert_eq!(read_data(&tail), None);
        assert_eq!(read_data(&[DATA, 0, 0]), None);
        assert!(valid_stamp(b"g1-603785af4ecfea8d"));
        assert!(!valid_stamp(b""));
        assert!(!valid_stamp(&[b'a'; 33]));
        assert_eq!(clean_label("patch\t1486\n"), "patch1486");
        assert_eq!(clean_label(&"é".repeat(30)).len(), 48);
    }

    #[test]
    fn the_embedded_install_stamp_comes_before_the_owners_word() {
        // the stock build: no embedded stamp; the owner's word, or nothing
        assert_eq!(host_stamp(None, None), (None, String::new()));
        assert_eq!(host_stamp(None, Some(("", ""))), (None, String::new()));
        assert_eq!(host_stamp(None, Some(("g1-bea85c2d733c0b28", "patch 1480"))), (Some("g1-bea85c2d733c0b28".to_string()), "patch 1480".to_string()));
        assert_eq!(host_stamp(None, Some(("not a stamp", "patch 1480"))), (None, String::new()));
        // the install build: its own stamp, whatever the row says (also with no row)
        let own = Some(("g1-603785af4ecfea8d", "patch 1486"));
        assert_eq!(host_stamp(own, None), (Some("g1-603785af4ecfea8d".to_string()), "patch 1486".to_string()));
        assert_eq!(host_stamp(own, Some(("g1-bea85c2d733c0b28", "patch 1480"))), (Some("g1-603785af4ecfea8d".to_string()), "patch 1486".to_string()));
        assert_eq!(host_stamp(own, Some(("", ""))).0.as_deref(), Some("g1-603785af4ecfea8d"));
        // what this build embeds: a stamp only with the install's maps
        match crate::data::map_stamp() {
            Some((stamp, _label)) => {
                assert!(cfg!(feature = "install_maps"));
                assert!(valid_stamp(stamp.as_bytes()) && stamp.starts_with("g1-") && stamp.len() == 19, "{stamp}");
            }
            None => assert!(!cfg!(feature = "install_maps")),
        }
    }

    #[test]
    fn the_rate_limits_are_the_relays() {
        let who = Identity::from_byte_array([7u8; 32]);
        let mut passed = 0;
        for _ in 0..60 {
            if within_rate(who, 5_000, false) {
                passed += 1;
            }
        }
        assert_eq!(passed, 40);
        assert!(within_rate(who, 6_000, false)); // the next second
        let mut voice = 0;
        for _ in 0..100 {
            if within_rate(who, 6_100, true) {
                voice += 1;
            }
        }
        assert_eq!(voice, 75);
    }

    #[test]
    fn codex_leb128_base64_matches_client_encoding() {
        // Client CodexData.Encode([1002, 1063]) == "6gc9"
        assert_eq!(decode_codex_ids("6gc9"), vec![1002, 1063]);
        assert_eq!(encode_codex_ids(&[1063, 1002, 1002]), "6gc9");
        // Sample multi-entry archive from codex.json
        let sample_full = "6QcBBAIBARQrAggCCwItAwg7pAalBQ==";
        let ids = decode_codex_ids(sample_full);
        assert_eq!(
            ids,
            vec![1001, 1002, 1006, 1008, 1009, 1010, 1030, 1073, 1075, 1083, 1085, 1096, 1098, 1143, 1146, 1154, 1213, 2017, 2694]
        );
        assert_eq!(encode_codex_ids(&ids), sample_full);
    }

    #[test]
    fn merge_settings_json_never_loses_codex_or_missing_keys() {
        let old = r#"{
            "snapTurn": false,
            "futureFeatureFlag": 42,
            "tutorialChars": ["Alice#101"],
            "wandSlots": [{"character": "Alice#101", "quick": [1,2,3,4,0,0,0,0]}],
            "codex": {
                "acct": "127.0.0.1:3000#2000004",
                "full": "6gc9",
                "seen": "+Ac=",
                "items": "9QM=",
                "heard": "7Ac=",
                "pending": "1004",
                "recent": ["c1002", "i501"],
                "scanned": [1002, 1063],
                "kills": {"1002": 5, "1063": 2},
                "cut": false
            }
        }"#;
        // Client sends a partial/wiped codex after an update changed ServerKey, plus updated snapTurn
        let new_wiped = r#"{
            "snapTurn": true,
            "tutorialChars": ["Bob#102"],
            "wandSlots": [{"character": "Bob#102", "quick": [5,0,0,0,0,0,0,0]}],
            "codex": {
                "acct": "127.0.0.1:6900#2000004",
                "full": "+Ac=",
                "seen": "",
                "items": "",
                "heard": "",
                "pending": "",
                "recent": ["c1016"],
                "scanned": [1016],
                "kills": {"1002": 1, "1016": 3},
                "cut": false
            }
        }"#;
        let merged_str = merge_settings_json(old, new_wiped);
        let v: serde_json::Value = serde_json::from_str(&merged_str).unwrap();
        // Scalar preference updated to new value
        assert_eq!(v["snapTurn"], serde_json::Value::Bool(true));
        // Removed / unknown top-level key preserved
        assert_eq!(v["futureFeatureFlag"], serde_json::Value::from(42));
        // Per-character arrays preserved across characters
        let tut = v["tutorialChars"].as_array().unwrap();
        assert_eq!(tut.len(), 2);
        let wands = v["wandSlots"].as_array().unwrap();
        assert_eq!(wands.len(), 2);
        // Codex full is union of [1002, 1063] and [1016] -> [1002, 1016, 1063]
        // And 1016 was in old.seen ("+Ac=" = [1016]), so seen is now empty since 1016 graduated to full!
        let codex = &v["codex"];
        assert_eq!(decode_codex_ids(codex["full"].as_str().unwrap()), vec![1002, 1016, 1063]);
        assert_eq!(decode_codex_ids(codex["seen"].as_str().unwrap()), Vec::<i32>::new());
        assert_eq!(decode_codex_ids(codex["items"].as_str().unwrap()), vec![501]);
        assert_eq!(decode_codex_ids(codex["heard"].as_str().unwrap()), vec![1004]);
        assert_eq!(codex["kills"]["1002"], serde_json::Value::from(5));
        assert_eq!(codex["kills"]["1063"], serde_json::Value::from(2));
        assert_eq!(codex["kills"]["1016"], serde_json::Value::from(3));
        let scanned = codex["scanned"].as_array().unwrap();
        assert_eq!(scanned.len(), 3);
    }
}

