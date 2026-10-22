//! The per-call context every game function receives, and the one way packets leave the module.
//!
//! rAthena writes packets into each client's socket buffer (WFIFO*) as it goes. Here a reducer collects them per
//! recipient in [`Ctx`] and [`Ctx::flush`] writes one `packet_out` row per recipient when the reducer finishes: the row's
//! bytes are exactly what rAthena would have sent on that client's map-server connection during this call, in order.
//! `packet_out` is an event table (rows are broadcast, never stored) and a row-level-security filter delivers each row
//! only to its `target`.

use std::cell::RefCell;
use std::collections::BTreeMap;

use spacetimedb::{client_visibility_filter, table, Filter, Identity, ReducerContext, Table};

/// rAthena's server tick (common/timer.cpp gettick): milliseconds. Unix time in ms, so ticks survive a restart.
pub type Tick = i64;

#[table(accessor = packet_out, public, event)]
pub struct PacketOut {
    pub target: Identity,
    pub data: Vec<u8>,
}

/// Each client receives only its own packets (and the database owner, who can see everything, receives none unless
/// it is a player).
#[client_visibility_filter]
const PACKET_OUT_VISIBILITY: Filter = Filter::Sql("SELECT * FROM packet_out WHERE packet_out.target = :sender");

pub struct Ctx<'a> {
    pub rc: &'a ReducerContext,
    /// gettick() for this call: read it with [`Ctx::tick`]. The reducer's time, except inside a timer callback, where
    /// timer.rs sets it to the callback's tick (see [`Ctx::tick`]).
    tick: std::cell::Cell<Tick>,
    /// The reducer's own time (rc.timestamp in ms), whatever tick a timer callback runs with: the host clock, for
    /// scheduling the next wakeup (timer.rs), never for game rules.
    real_tick: Tick,
    out: RefCell<BTreeMap<Identity, Vec<u8>>>,
    /// The wakeup row that is running right now (timer.rs), so it is not deleted while in use.
    pub(crate) executing_wakeup: std::cell::Cell<u64>,
    /// Test rig only: the fixed roll from [`DebugRng`] (None = random, the normal case).
    rnd_k: Option<u32>,
}

/// Test rig only (not part of rAthena): while this row exists every roll is deterministic, exactly as the oracle's
/// patched common/random (env `LOKI_RND=<k>`, rathena-loki-rnd.patch) makes rAthena's, so combat
/// and AI can be compared packet for packet: rnd() returns k, rnd_value(min, max) returns min + k % (max - min + 1).
/// Set it with `spacetimedb-cli sql -s local <db> "INSERT INTO debug_rng (id, k) VALUES (0, 0)"`, clear it with
/// `DELETE FROM debug_rng WHERE id = 0`. Never present on a real server, and ignored on a release server (release.rs).
#[table(accessor = debug_rng)]
pub struct DebugRng {
    #[primary_key]
    pub id: u32,
    pub k: u32,
}

thread_local! {
    /// The client whose packets are dropped for now (`with_muted`).
    static MUTED: std::cell::Cell<Option<Identity>> = const { std::cell::Cell::new(None) };
}

/// Runs `f` with the client's packets dropped: rAthena's `fd = sd->fd; sd->fd = 0; ...; sd->fd = fd;` (mob.cpp
/// mob_clone_spawn around skill_check_condition_castbegin), during which clif_send skips the session (fd 0 is never
/// an active session) whether the packet is sent to the player alone or to an area around it.
pub fn with_muted<R>(who: Identity, f: impl FnOnce() -> R) -> R {
    let old = MUTED.with(|m| m.replace(Some(who)));
    let r = f();
    MUTED.with(|m| m.set(old));
    r
}

impl<'a> Ctx<'a> {
    pub fn new(rc: &'a ReducerContext) -> Self {
        let tick = rc.timestamp.to_micros_since_unix_epoch() / 1000;
        // the release switch (release.rs), read once per call: on a release server the test rig's fixed roll is ignored
        let release = crate::release::load(&rc.db);
        // battle_config for this call (battle_config/mod.rs): one stamp-row lookup, the settings themselves only when
        // another call changed one
        crate::battle_config::load(&rc.db);
        let rnd_k = if release { None } else { rc.db.debug_rng().id().find(0).map(|r| r.k & 0x7fff_ffff) };
        Ctx { rc, tick: std::cell::Cell::new(tick), real_tick: tick, out: RefCell::new(BTreeMap::new()), executing_wakeup: std::cell::Cell::new(0), rnd_k }
    }

    /// common/timer.cpp gettick(). rAthena's gettick() is the clock; inside a timer callback its main loop has just
    /// woken for that timer (core.cpp: `do_timer(gettick_nocache())`, then do_sockets waits until the next timer:
    /// timer.cpp:411 `cap_value(diff, TIMER_MIN_INTERVAL, TIMER_MAX_INTERVAL)`, 20 ms to 1 s), so gettick() there is
    /// the timer's own tick (up to 20 ms later when the timer was due less than 20 ms after the previous pass).
    /// SpacetimeDB starts the wakeup reducer ~15 ms after its due time, so the reducer's time would put every delay set
    /// from gettick() in a callback ~15 ms late against a check on the same grid (wave D skillcore.md "mobidle": an
    /// idle monster's 5 s skill delay seen as still running by the AI check 5 s later). So inside a callback this is
    /// the callback's tick (the due tick, or the pass's tick when the timer ran more than a second late:
    /// timer.cpp:376 `diff < -1000`); everywhere else, the reducer's time.
    pub fn tick(&self) -> Tick {
        self.tick.get()
    }

    /// Sets gettick() for the timer callback that is about to run (timer.rs do_timer only).
    pub(crate) fn set_tick(&self, tick: Tick) {
        self.tick.set(tick);
    }

    /// The reducer's own time (the host clock), unaffected by [`Ctx::set_tick`]: timer.rs arms the next wakeup with it.
    pub(crate) fn real_tick(&self) -> Tick {
        self.real_tick
    }

    pub fn db(&self) -> &spacetimedb::Local {
        &self.rc.db
    }

    pub fn sender(&self) -> Identity {
        self.rc.sender()
    }

    /// Queues bytes for one client (WFIFOSET to that client's fd).
    pub fn send(&self, to: Identity, bytes: &[u8]) {
        if bytes.is_empty() {
            return;
        }
        if MUTED.with(|m| m.get()) == Some(to) {
            return; // the session's fd is 0 for now (see `with_muted`)
        }
        crate::prof::count("send_bytes", bytes.len() as u64);
        self.out.borrow_mut().entry(to).or_default().extend_from_slice(bytes);
    }

    /// Anything queued so far for `to` is dropped (a connection that is being refused / closed).
    pub fn discard(&self, to: Identity) {
        self.out.borrow_mut().remove(&to);
    }

    /// Writes the queued packets out. Called once, at the end of every reducer that may send packets.
    pub fn flush(&self) {
        let out = std::mem::take(&mut *self.out.borrow_mut());
        for (target, data) in out {
            self.rc.db.packet_out().insert(PacketOut { target, data });
        }
    }

    /// common/random.cpp rnd(): uniformly random in [0, SINT32_MAX] (31 bits, like rAthena's int31_distribution).
    pub fn rnd(&self) -> u32 {
        if let Some(k) = self.rnd_k {
            return k;
        }
        self.rc.random::<u32>() >> 1
    }

    /// common/random.hpp rnd_value(min, max): inclusive, reversed bounds are swapped.
    pub fn rnd_value(&self, min: i32, max: i32) -> i32 {
        self.rnd_value_i64(min as i64, max as i64) as i32
    }

    /// rnd_value for 64-bit bounds (script `rand` works on int64).
    pub fn rnd_value_i64(&self, min: i64, max: i64) -> i64 {
        let (lo, hi) = if min > max { (max, min) } else { (min, max) };
        let span = (hi as i128 - lo as i128 + 1) as u128;
        let r: u128 = match self.rnd_k {
            Some(k) => k as u128,
            None => self.rc.random::<u64>() as u128,
        };
        (lo as i128 + (r % span) as i128) as i64
    }

    /// common/random.hpp rnd_chance(chance, base): rnd_value(1, base) <= chance.
    pub fn rnd_chance(&self, chance: i32, base: i32) -> bool {
        self.rnd_value(1, base) <= chance
    }

    /// common/random.hpp rnd_chance_official(chance, base): rnd_value(0, 20000) % base < chance (official bias to 0).
    pub fn rnd_chance_official(&self, chance: i32, base: i32) -> bool {
        base != 0 && self.rnd_value(0, 20000) % base < chance
    }

    /// common/random.hpp rnd_vector_order: shuffles (Fisher-Yates); left alone while the test rig fixes the rolls.
    pub fn rnd_vector_order<T>(&self, v: &mut [T]) {
        if self.rnd_k.is_some() {
            return;
        }
        for i in (1..v.len()).rev() {
            let j = (self.rc.random::<u64>() % (i as u64 + 1)) as usize;
            v.swap(i, j);
        }
    }

    /// True while the test rig fixes the rolls ([`DebugRng`]).
    pub fn rnd_fixed(&self) -> bool {
        self.rnd_k.is_some()
    }
}

/// Runs a game function with a fresh context and flushes its packets (the shape of every reducer below).
pub fn run<R>(rc: &ReducerContext, f: impl FnOnce(&Ctx) -> R) -> R {
    // A reducer that failed part-way may have left a status calculation's working copy behind (status/data.rs).
    crate::status::data::reset_work();
    // objects freed by an earlier call are gone (map_freeblock_unlock)
    crate::unit::freed_reset();
    crate::mob::freed_reset();
    crate::timer::heap::reset_cache();
    let c = Ctx::new(rc);
    // run-time item_db changes (setiteminfo / setitemscript) made in another instance or an earlier call
    crate::itemui::itemdb_rt::sync(&c);
    // map_data cells changed by map_setcell (map_cell.rs): the in-memory copy getcellp reads follows the table
    crate::map_cell::sync(&c);
    // instance maps (instance_map.rs): data::maps() answers them through an in-memory registry that follows the table
    crate::instance_map::sync(&c);
    // ev_db's foreach / batch lock (script/npc_run.rs) never outlives a call
    crate::script::npc_run::ev_db_reset_lock();
    crate::intif::reset();
    let r = f(&c);
    // the sessions this call marked eof are closed by clif_parse in do_sockets' next pass (session.rs)
    crate::session::do_eof(&c);
    // the char server's answers to what this call asked (map/intif.cpp), handled after it as in rAthena
    crate::intif::drain(&c);
    crate::session::do_eof(&c);
    crate::timer::heap::flush(&c);
    c.flush();
    crate::mob::mark_call_clean();
    r
}
