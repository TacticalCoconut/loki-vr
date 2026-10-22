//! Horde's reward: the real character's Base and Job EXP for a run. The DECISION is the engine's (sim.rs results:
//! HordeReward.Shares, Loki's own rule: a share of what the character needs for its next base / job level, capped by
//! the owner's reward size; never an amount a client names). The GRANT is the module's own port of rAthena's EXP path:
//! on an rAthena server Loki Host writes the decision into a table and an NPC script it installs calls `getexp`
//! (tools/lokihost/Core/HordeHost.cs HordeNpc.Script); here there is no script: the module does what that script's
//! lines do, in the transaction that marks the row granted, so a row is granted exactly once:
//!   .@base = NextBaseExp * share / 10000; .@job = NextJobExp * share / 10000        (HordeNpc.Script)
//!   getexp .@base, .@job                    BUILDIN(getexp), script.cpp:11142-11182 (script/buildins/status.rs getexp):
//!       base / job × quest_exp_rate / 100, pc_gainexp(sd, nullptr, base, job, 1) (quest EXP: the guild's tax and the
//!       player's EXP boosts apply, pc.cpp:8462-8477), the homunculus's share of the base EXP (script.cpp:11177-11180).
//! pc_gainexp gives nothing to a character that is on no map or dead (pc.cpp:8459) or on a PvP map without pvp_exp
//! (pc.cpp:8464-8465): the script finds that out afterwards and tries again 5 s later; here the three conditions are
//! looked at first and the row waits for the character's NEXT MAP ENTRY (pc.rs load_end_ack → [`on_enter_map`]):
//! logging in, a warp, the respawn after a death. A player offline at the run's end is simply not in the world: the
//! same wait.

use spacetimedb::{ReducerContext, Table};

use super::service::{horde_peer as _, horde_reward as _, HordePeer, HordeReward};
use super::wire;
use crate::db::jobs::{pc_nextbaseexp, pc_nextjobexp};
use crate::pc::pc as _;
use crate::status::defs::MAX_EXP;
use crate::world::Ctx;

/// battle_config.quest_exp_rate (exp.conf `quest_exp_rate: 100`), read at run time as the module's getexp reads it
/// (script/buildins/status.rs quest_exp_rate; world::run / the world's reducer has loaded the settings of this call).
fn quest_exp_rate() -> f64 {
    crate::battle_config::get(crate::battle_config::Bc::QUEST_EXP_RATE) as f64
}

/// Finish's reward row (HordeService.cs:440-453; HordeRewardDb.RecordAsync): the decision, waiting.
#[allow(clippy::too_many_arguments)]
pub(super) fn record(db: &spacetimedb::Local, run_id: u32, account_id: u32, char_id: u32, name: &str, base_share: i32, job_share: i32, now: i64) -> HordeReward {
    let row = db.horde_reward().insert(HordeReward {
        id: 0,
        run_id,
        account_id,
        char_id,
        char_name: name.to_string(),
        base_share: base_share.clamp(0, super::sim::MAX_SHARE) as u16,
        job_share: job_share.clamp(0, super::sim::MAX_SHARE) as u16,
        created_ms: now,
        granted: 0,
        granted_ms: 0,
        base_exp: 0,
        job_exp: 0,
    });
    log::info!(
        "horde: reward written for {name} (AID {account_id}): {} % of its next base level, {} % of its next job level (granted once, when the character is on a map and alive)",
        row.base_share as f64 / 100.0,
        row.job_share as f64 / 100.0
    );
    row
}

/// The rows of a run that just ended: given at once to the characters that can take them (the others wait).
pub(super) fn grant_rows(rc: &ReducerContext, rows: Vec<HordeReward>) {
    if rows.is_empty() {
        return;
    }
    crate::world::run(rc, |c| {
        for row in rows {
            try_grant(c, row);
        }
    });
}

/// World hook (pc.rs load_end_ack, after the character is put on its map): the rewards that waited for it.
pub fn on_enter_map(c: &Ctx, aid: u32) {
    let Some(p) = crate::pc::get(c, aid) else { return };
    let waiting: Vec<HordeReward> = c.db().horde_reward().by_char().filter(p.char_id).filter(|r| r.granted == 0).collect();
    if waiting.is_empty() {
        return;
    }
    // (this runs inside one of the world's reducers: Horde's own messages of this call are the reward notes alone)
    super::service::outbox_begin();
    for row in waiting {
        try_grant(c, row);
    }
    super::service::outbox_flush(c.db());
}

/// Gives one row's EXP if its character can gain EXP now; false = it waits.
fn try_grant(c: &Ctx, mut row: HordeReward) -> bool {
    let db = c.db();
    let Some(p) = db.pc().char_id().find(row.char_id) else { return false }; // not in the world
    let aid = p.account_id;
    let Some(u) = crate::unit::get(c, aid) else { return false };
    // what pc_gainexp returns on without giving (pc.cpp:8459, 8464-8465; status/progress.rs pc_gainexp)
    if !u.on_map || crate::pc::isdead(c, aid) {
        return false;
    }
    if !crate::conf::pvp_exp() && crate::npc::mapflag(c, u.map, crate::npc::MF_PVP) {
        return false;
    }
    let Some(ch) = crate::pc::character_of(c, aid) else { return false };
    // the script's NextBaseExp / NextJobExp (status/param.rs SP_NEXTBASEEXP / SP_NEXTJOBEXP);
    // npc_horde.txt:154-156 (< 99999999 / < 999999999) — pc_nextbaseexp returns 99_999_999 at cap level.
    let raw_b = pc_nextbaseexp(ch.class, ch.base_level as u32);
    let raw_j = pc_nextjobexp(ch.class, ch.job_level as u32);
    let next_b = if raw_b < 99_999_999 { raw_b.min(MAX_EXP) as i128 } else { 0 };
    let next_j = if raw_j < 999_999_999 { raw_j.min(MAX_EXP) as i128 } else { 0 };
    let base = (next_b * row.base_share as i128 / 10000) as i64;
    let job = (next_j * row.job_share as i128 / 10000) as i64;
    row.granted = 1;
    row.granted_ms = c.real_tick();
    row.base_exp = base;
    row.job_exp = job;
    db.horde_reward().id().update(row.clone());
    if base > 0 || job > 0 {
        // BUILDIN(getexp): the bonus for npc-given exp, then quest EXP, then the homunculus's share
        let bonus = quest_exp_rate() / 100.0;
        let b = if base != 0 { (base as f64 * bonus).clamp(0.0, MAX_EXP as f64) as i64 } else { 0 };
        let j = if job != 0 { (job as f64 * bonus).clamp(0.0, MAX_EXP as f64) as i64 } else { 0 };
        crate::status::progress::pc_gainexp(c, aid, None, b as u64, j as u64, 1);
        crate::homunculus::buildins::getexp_share(c, aid, b);
    }
    log::info!("horde: reward GRANTED to {} (AID {}) for run {}: {base} base EXP, {job} job EXP", row.char_name, row.account_id, row.run_id);
    if let Some(peer) = db.horde_peer().aid().find(aid) {
        tell(db, &peer, row);
    }
    true
}

/// H_REWARD to the player's client (HordeService.PollRewards, HordeService.cs:476-477), once.
fn tell(db: &spacetimedb::Local, peer: &HordePeer, mut row: HordeReward) {
    if row.granted != 1 || peer.char_id != row.char_id {
        return;
    }
    super::service::send_to(db, peer.identity, wire::encode_reward(row.run_id, row.base_exp, row.job_exp, row.base_share as i32, row.job_share as i32));
    row.granted = 2;
    db.horde_reward().id().update(row);
}

/// A client linked (H_HELLO): the rewards its character was given while it was not linked are told now.
pub(super) fn tell_waiting(db: &spacetimedb::Local, peer: &HordePeer) {
    let rows: Vec<HordeReward> = db.horde_reward().by_char().filter(peer.char_id).filter(|r| r.granted == 1).collect();
    for row in rows {
        tell(db, peer, row);
    }
}
