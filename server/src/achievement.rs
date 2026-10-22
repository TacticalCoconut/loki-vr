//! map/achievement.cpp (the achievement log of a character, its objectives, levels, rewards and titles), its
//! char-server side char/int_achievement.cpp, the map/intif.cpp messages between them, and the clif.cpp packets
//! (ZC_ALL_ACH_LIST 0x0a23, ZC_ACH_UPDATE 0x0a24, CZ_REQ_ACH_REWARD 0x0a25 → ZC_REQ_ACH_REWARD_ACK 0x0a26,
//! CZ_REQ_CHANGE_TITLE 0x0a2e → ZC_ACK_CHANGE_TITLE 0x0a2f).
//!
//! State:
//! - `pc_achievement`: map_session_data's achievement_data (the log: the first `incomplete_count` entries are not
//!   complete, completed ones follow; total_score, level, the save flag) and sd->titles.
//! - `achievement`: the char server's `achievement` SQL table (sql-files/main.sql: MyISAM, static rows, PRIMARY KEY
//!   (char_id, id), KEY char_id). int_achievement.cpp reads a character's rows without ORDER BY; MariaDB answers
//!   through the `char_id` index, so the rows arrive in physical (record slot) order, and a new row takes the slot of
//!   the last deleted row first (checked on the oracle's MariaDB 10.11 with the query int_achievement.cpp runs). `pos`
//!   models the slot, `achievement_slots` the table's free list.
//!
//! Load / save: pc_reg_received → intif_request_achievements → mapif_parse_achievement_load → intif_parse_achievements
//! ([`intif_parse_achievements`], called from pc.rs at the place the char server's answer arrives). chrif_save's
//! `if (sd->achievement_data.save) intif_achievement_save(sd)` is [`chrif_save`]; the save and the reward claim travel
//! over the inter link (intif.rs queue), answered by [`mapif_parse_achievement_save`] / [`mapif_parse_achievement_reward`].
//!
//! Interfaces for other systems: [`achievement_update_objective`] (the hooks: `achievement_update_objective(c,
//! Some(aid), AG_…, &[args])`), [`achievement_update_objective_sub`] (mob.cpp mob_dead's party loop), [`mobexists`],
//! [`achievement_level_of`] (script param AchievementLevel), [`titles`].

use std::cell::Cell;
use std::rc::Rc;

use spacetimedb::{table, SpacetimeType, Table};

use crate::db::misc::achievement::*;
use crate::intif::{self, Inter};
use crate::script::compiler::ScriptCode;
use crate::script::npc_run::{self, sdx};
use crate::script::state::{self, script_state};
use crate::script::vm::{self, CodeKey, ExtCode, Rt, State};
use crate::world::Ctx;

/// achievement.hpp e_achievement_info (achievementinfo / achievementupdate types).
pub const ACHIEVEINFO_COUNT1: i32 = 1;
pub const ACHIEVEINFO_COUNT10: i32 = 10;
pub const ACHIEVEINFO_COMPLETE: i32 = 11;
pub const ACHIEVEINFO_COMPLETEDATE: i32 = 12;
pub const ACHIEVEINFO_GOTREWARD: i32 = 13;
pub const ACHIEVEINFO_LEVEL: i32 = 14;
pub const ACHIEVEINFO_SCORE: i32 = 15;

/// mmo.hpp NAME_LENGTH.
const NAME_LENGTH: usize = 24;

// ------------------------------------------------------------------------------------------------ tables

/// mmo.hpp struct achievement: one entry of the log. `completed` / `rewarded` are time_t (0 = not yet).
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct Achievement {
    pub achievement_id: i32,
    /// count[MAX_ACHIEVEMENT_OBJECTIVES].
    pub count: Vec<i32>,
    pub completed: i64,
    pub rewarded: i64,
    pub score: i32,
}

impl Achievement {
    fn zeroed(achievement_id: i32) -> Achievement {
        Achievement { achievement_id, count: vec![0; MAX_ACHIEVEMENT_OBJECTIVES], completed: 0, rewarded: 0, score: 0 }
    }
    fn cnt(&self, j: usize) -> i32 {
        self.count.get(j).copied().unwrap_or(0)
    }
}

/// map_session_data's achievement_data and titles for an online character.
#[table(accessor = pc_achievement)]
#[derive(Clone, Debug)]
pub struct PcAchievement {
    #[primary_key]
    pub account_id: u32,
    #[unique]
    pub char_id: u32,
    /// achievement_data.total_score.
    pub total_score: i32,
    /// achievement_data.level.
    pub level: i32,
    /// achievement_data.save: the log changed since it was last sent to the char server.
    pub save: bool,
    /// achievement_data.incompleteCount.
    pub incomplete_count: u16,
    /// achievement_data.achievements (count = len).
    pub achievements: Vec<Achievement>,
    /// sd->titles.
    pub titles: Vec<i32>,
}

/// The char server's `achievement` table. completed / rewarded: UNIX_TIMESTAMP of the datetime, 0 = NULL.
#[table(accessor = achievement, index(accessor = by_char, btree(columns = [char_id])))]
#[derive(Clone, Debug)]
pub struct AchievementRow {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    /// The MyISAM record slot (physical order of the rows).
    pub pos: u64,
    pub char_id: u32,
    pub achievement_id: i32,
    pub completed: u32,
    pub rewarded: u32,
    /// count1 .. count10.
    pub count: Vec<i32>,
}

/// The `achievement` table's MyISAM record slots: the next never-used slot and the deleted ones (reused last freed
/// first). Missing: no deletion happened since the slots were last compacted (a new slot follows the highest one).
#[table(accessor = achievement_slots)]
#[derive(Clone, Debug)]
pub struct AchievementSlots {
    #[primary_key]
    pub id: u8,
    pub next: u64,
    pub free: Vec<u64>,
}

// ------------------------------------------------------------------------------------------------ helpers

fn pa(c: &Ctx, aid: u32) -> Option<PcAchievement> {
    c.db().pc_achievement().account_id().find(aid)
}

fn put_pa(c: &Ctx, p: &PcAchievement) {
    if c.db().pc_achievement().account_id().find(p.account_id).is_some() {
        c.db().pc_achievement().account_id().update(p.clone());
    } else {
        c.db().pc_achievement().insert(p.clone());
    }
}

/// time(nullptr).
fn now(c: &Ctx) -> i64 {
    c.tick().div_euclid(1000)
}

/// WFIFOSET to the player's own connection.
fn send_fd(c: &Ctx, aid: u32, bytes: &[u8]) {
    if let Some(id) = crate::pc::identity_of(c, aid) {
        c.send(id, bytes);
    }
}

fn feature() -> bool {
    achievement_db().feature()
}

/// The achievement log's position of `id` (ARR_FIND over the whole log).
fn find_index(p: &PcAchievement, id: i32) -> Option<usize> {
    p.achievements.iter().position(|a| a.achievement_id == id)
}

/// sd->achievement_data.level (pc_readparam SP_ACHIEVEMENT_LEVEL); 0 without achievement data.
pub fn achievement_level_of(c: &Ctx, aid: u32) -> i32 {
    pa(c, aid).map(|p| p.level).unwrap_or(0)
}

/// sd->titles.
pub fn titles(c: &Ctx, aid: u32) -> Vec<i32> {
    pa(c, aid).map(|p| p.titles).unwrap_or_default()
}

/// AchievementDatabase::mobexists (mob.cpp mob_dead checks it before the AG_BATTLE update).
pub fn mobexists(mob_id: u32) -> bool {
    achievement_db().mobexists(mob_id)
}

// ------------------------------------------------------------------------------------------------ scripts

/// The source kind achievement conditions and reward scripts compile under (script::code providers).
const SCRIPT_KIND: &str = "achievement";

thread_local! {
    static PROVIDER: Cell<bool> = const { Cell::new(false) };
    /// achievement_level's `static int32 info[2]`: every call overwrites it, and a caller reads it after the call
    /// returns (so after any AG_GOAL_ACHIEVE recursion the call started).
    static INFO: Cell<(i32, i32)> = const { Cell::new((0, 0)) };
}

/// AchievementDatabase::parseBodyNode's parse_script(…, getCurrentFile(), getLineNumber(…),
/// SCRIPT_IGNORE_EXTERNAL_BRACKETS) for `Condition` (key "c<id>", already wrapped in achievement_condition) and
/// `Rewards.Script` ("r<id>"). do_init_achievement runs before do_init_npc, so no user function exists yet. (The
/// yaml line number only appears in compile error messages; it is not recorded.)
fn provider(_c: Option<&Ctx>, key: &str) -> Option<crate::script::code::ExtSource> {
    let (kind, id) = key.split_at(1);
    let ad = achievement_db().find(id.parse().ok()?)?;
    let text = match kind {
        "c" => ad.condition.clone()?,
        "r" => ad.rewards.script.clone()?,
        _ => return None,
    };
    Some(crate::script::code::ExtSource {
        text: crate::net::wire::latin1(&text),
        file: "db/re/achievement_db.yml".into(),
        line: 0,
        options: crate::script::compiler::SCRIPT_IGNORE_EXTERNAL_BRACKETS,
        userfuncs: Vec::new(),
    })
}

fn script_key(kind: char, id: u32) -> CodeKey {
    if !PROVIDER.with(|p| p.get()) {
        crate::script::code::register_provider(SCRIPT_KIND, provider);
        PROVIDER.with(|p| p.set(true));
    }
    CodeKey::Ext(ExtCode { kind: SCRIPT_KIND.into(), key: format!("{kind}{id}") })
}

/// ad->condition: the compiled condition, None when the entry has none or it did not compile (parse_script
/// returned nullptr at load).
fn condition_code(c: &Ctx, ad: &AchievementDb) -> Option<CodeKey> {
    ad.condition.as_ref()?;
    let key = script_key('c', ad.achievement_id);
    crate::script::code::get(Some(c), &key).map(|_| key)
}

/// ad->rewards.script.
fn reward_code(ad: &AchievementDb) -> Option<CodeKey> {
    ad.rewards.script.as_ref()?;
    Some(script_key('r', ad.achievement_id))
}

/// achievement.cpp achievement_check_condition: runs a condition script (`achievement_condition( <expr> );`) for the
/// player with the fake NPC as its object and returns whether the expression was non-zero. `condition` None is the
/// nullptr script AG_GOAL_ACHIEVE may pass (run_script returns at once: false).
///
/// rAthena detaches the player's current script (sd->st) first — script_detach_rid(previous_st) — and attaches it
/// again afterwards (previous_st->rid = sd->id; script_attach_state(previous_st)). achievement_condition leaves the
/// condition's state RERUNLINE, so run_script_main keeps it attached to the player, and the value is read back from
/// its stack (script_getnum(st, 2)) before script_free_state.
///
/// When the current script is the one executing right now (a buildin such as setquest got here), its script_state
/// lives on the Rust stack, not in its row: the player-side effects of the detach / attach are applied here, and the
/// state's own (its backup script is let go: bk_st / bk_npcid end up 0) through state::request_bk_reset, which the VM
/// applies when the running buildin returns. Its backup's id is not known here, so while the condition runs the
/// player has no script instead of that backup (rAthena's condition state would take it as its own backup and free it
/// as a "double continuation"; the VM frees it instead).
pub fn achievement_check_condition(c: &Ctx, condition: Option<&CodeKey>, aid: u32) -> bool {
    let prev_id = sdx(c, aid).st;
    let prev_row = if prev_id != 0 { c.db().script_state().id().find(prev_id) } else { None };
    // Save the old script the player was attached to; only if there was an old script, detach the player from it
    let mut previous: Option<Previous> = None;
    if let Some(row) = prev_row {
        let loaded = if state::is_running(prev_id) { None } else { state::load(c, prev_id) };
        match loaded {
            Some(mut st) => {
                npc_run::script_detach_rid(c, &mut st);
                previous = Some(Previous::Parked(st));
            }
            None => {
                // script_detach_state(previous_st, false) as seen from the player, then script_detach_rid's rid = 0
                let npc_item_flag = sdx(c, aid).npc_item_flag;
                let mut tmp = stand_in(prev_id, aid, row.oid, npc_item_flag);
                npc_run::script_detach_state(c, &mut tmp, false);
                if state::is_running(prev_id) {
                    state::request_bk_reset(prev_id);
                }
                previous = Some(Previous::Running { id: prev_id, oid: row.oid, npc_item_flag });
            }
        }
    }

    if let Some(condition) = condition {
        vm::run_script(&Rt::new(c), condition, 0, aid, npc_run::fake_npc_id(c));
    }

    let mut value: i32 = 0;
    let st_id = sdx(c, aid).st;
    if st_id != 0 && !state::is_running(st_id) {
        if let Some(mut st) = state::load(c, st_id) {
            // script_getnum(st, 2)
            if st.start + 2 >= 0 && ((st.start + 2) as usize) < st.stack.len() {
                value = vm::getnum(&Rt::new(c), &mut st, 2);
            }
            npc_run::script_free_state(c, &st);
        }
    }

    // If an old script is present: because of detach the RID was removed, so restore it and reattach the player to it,
    // so that the limitations of that script kick back in
    match previous {
        Some(Previous::Parked(mut st)) => {
            st.rid = aid;
            npc_run::script_attach_state(c, &mut st);
            state::save(c, &st);
        }
        Some(Previous::Running { id, oid, npc_item_flag }) => {
            let mut tmp = stand_in(id, aid, oid, npc_item_flag);
            npc_run::script_attach_state(c, &mut tmp);
        }
        None => {}
    }
    value != 0
}

enum Previous {
    /// A script waiting for the player (its row holds it).
    Parked(State),
    /// The script executing right now (its fields are on the Rust stack).
    Running { id: u64, oid: u32, npc_item_flag: bool },
}

/// A state that stands in for one whose fields are not at hand, for the attach / detach bookkeeping on the player
/// (the id, rid, oid and npc_item_flag it reads; no backup).
fn stand_in(id: u64, rid: u32, oid: u32, npc_item_flag: bool) -> State {
    let mut st = State::new(CodeKey::Npc(0), Rc::new(ScriptCode { buf: Vec::new(), labels: Vec::new() }), 0, rid, oid);
    st.id = id;
    st.npc_item_flag = npc_item_flag;
    st
}

/// pc.cpp pc_setglobalreg(sd, add_str(name), value) for the ARGn registries achievement_update_objective sets.
fn pc_setglobalreg(c: &Ctx, aid: u32, name: &str, value: i64) {
    let Some(p) = crate::pc::get(c, aid) else { return };
    let rt = Rt::new(c);
    let mut st = State::new(CodeKey::Func(String::new()), Rc::new(ScriptCode { buf: Vec::new(), labels: Vec::new() }), 0, aid, 0);
    crate::script::reg::store_set_num(&rt, &mut st, &crate::script::reg::Store::Char(p.char_id), &name.to_ascii_lowercase(), 0, value);
}

// ------------------------------------------------------------------------------------------------ achievement.cpp

/// achievement.cpp achievement_add: a new entry (not complete) goes in front of the completed ones. Returns its
/// index in the log.
pub fn achievement_add(c: &Ctx, aid: u32, achievement_id: i32) -> Option<usize> {
    let mut p = pa(c, aid)?;
    let Some(adb) = achievement_db().find(achievement_id as u32) else {
        log::error!("achievement_add: Achievement {achievement_id} not found in DB.");
        return None;
    };
    if find_index(&p, achievement_id).is_some() {
        log::error!("achievement_add: Character {} already has achievement {achievement_id}.", p.char_id);
        return None;
    }
    let index = p.incomplete_count as usize;
    p.incomplete_count += 1;
    // The character has some completed achievements, make room before them so that they will stay at the end of the array
    let mut entry = Achievement::zeroed(achievement_id);
    entry.score = adb.score;
    p.achievements.insert(index.min(p.achievements.len()), entry);
    p.save = true;
    put_pa(c, &p);
    clif_achievement_update(c, aid, AchRef::Index(index));
    Some(index)
}

/// achievement.cpp achievement_remove: the entry leaves the log and the client gets a zeroed ("removed fake")
/// entry of that id.
pub fn achievement_remove(c: &Ctx, aid: u32, achievement_id: i32) -> bool {
    let Some(mut p) = pa(c, aid) else { return false };
    if !achievement_db().exists(achievement_id as u32) {
        log::error!("achievement_delete: Achievement {achievement_id} not found in DB.");
        return false;
    }
    let Some(i) = find_index(&p, achievement_id) else {
        log::error!("achievement_delete: Character {} doesn't have achievement {achievement_id}.", p.char_id);
        return false;
    };
    if p.achievements[i].completed == 0 {
        p.incomplete_count = p.incomplete_count.saturating_sub(1);
    }
    p.achievements.remove(i);
    p.save = true;
    put_pa(c, &p);
    // Send a removed fake achievement
    clif_achievement_update(c, aid, AchRef::Dummy(Achievement::zeroed(achievement_id)));
    true
}

/// Changes a log entry in place (buildin achievementupdate writes the entry's fields directly: no save flag, no
/// move between the blocks). False when the player does not have the achievement.
pub fn with_entry(c: &Ctx, aid: u32, achievement_id: i32, f: &dyn Fn(&mut Achievement)) -> bool {
    let Some(mut p) = pa(c, aid) else { return false };
    let Some(i) = find_index(&p, achievement_id) else { return false };
    f(&mut p.achievements[i]);
    put_pa(c, &p);
    true
}

/// achievement.cpp achievement_done.
fn achievement_done(p: &PcAchievement, achievement_id: u32) -> bool {
    p.achievements.iter().any(|a| a.achievement_id as u32 == achievement_id && a.completed > 0)
}

/// achievement.cpp achievement_check_dependent: every dependent of the achievement is complete (true without any).
pub fn achievement_check_dependent(c: &Ctx, aid: u32, achievement_id: i32) -> bool {
    let Some(p) = pa(c, aid) else { return false };
    let Some(adb) = achievement_db().find(achievement_id as u32) else { return false };
    // Check if the achievement has a dependent; if so, check all of them
    adb.dependent_ids.iter().all(|&d| achievement_done(&p, d))
}

/// achievement.cpp achievement_check_groups: achievements that only have dependents (AG_BATTLE, AG_TAMING,
/// AG_ADVENTURE without a condition) are completed once their dependents are.
fn achievement_check_groups(c: &Ctx, aid: u32, ad: &AchievementDb) -> i32 {
    if ad.group != AG_BATTLE && ad.group != AG_TAMING && ad.group != AG_ADVENTURE {
        return 0;
    }
    if ad.dependent_ids.is_empty() || condition_code(c, ad).is_some() {
        return 0;
    }
    let Some(p) = pa(c, aid) else { return 0 };
    if find_index(&p, ad.achievement_id as i32).is_none() {
        // Achievement isn't in player's log
        if achievement_check_dependent(c, aid, ad.achievement_id as i32) {
            achievement_add(c, aid, ad.achievement_id as i32);
            achievement_update_achievement(c, aid, ad.achievement_id as i32, true);
        }
    }
    1
}

/// achievement.cpp achievement_update_achievement: sends an entry of the incomplete block; with `complete`, the
/// entry gets its target counts and the completion time, moves to the completed block, the level is recalculated
/// (AG_GOAL_ACHIEVE) and the dependents-only achievements are checked.
pub fn achievement_update_achievement(c: &Ctx, aid: u32, achievement_id: i32, complete: bool) -> bool {
    let Some(adb) = achievement_db().find(achievement_id as u32) else { return false };
    let Some(mut p) = pa(c, aid) else { return false };
    let ic = (p.incomplete_count as usize).min(p.achievements.len());
    let Some(mut i) = p.achievements[..ic].iter().position(|a| a.achievement_id == achievement_id) else { return false };
    if p.achievements[i].completed > 0 {
        return false;
    }
    // Finally we send the updated achievement to the client
    if complete {
        // Make sure all the objective targets are at their respective total requirement
        for (tid, t) in &adb.targets {
            if let Some(v) = p.achievements[i].count.get_mut(*tid as usize) {
                *v = t.count;
            }
        }
        p.achievements[i].completed = now(c);
        p.incomplete_count -= 1;
        let ic = p.incomplete_count as usize;
        if i < ic {
            // The achievement needs to be moved to the completed achievements block at the end of the array
            p.achievements.swap(i, ic);
        }
        put_pa(c, &p);
        achievement_level(c, aid, true); // Re-calculate achievement level
        // Check dependents
        for ach in achievement_db().iter() {
            achievement_check_groups(c, aid, ach);
        }
        // Look for the index again, the position most likely changed
        let Some(p2) = pa(c, aid) else { return false };
        let ic = (p2.incomplete_count as usize).min(p2.achievements.len());
        match p2.achievements[ic..].iter().position(|a| a.achievement_id == achievement_id) {
            Some(j) => i = ic + j,
            None => {
                // (ARR_FIND ends at count: rAthena would send the entry past the end of the log)
                log::error!("achievement_update_achievement: achievement {achievement_id} left the completed block");
                return true;
            }
        }
    }
    clif_achievement_update(c, aid, AchRef::Index(i));
    if let Some(mut p) = pa(c, aid) {
        p.save = true; // Flag to save with the autosave interval
        put_pa(c, &p);
    }
    true
}

/// achievement.cpp achievement_get_reward: the char server's answer to a reward claim (`rewarded` 0: refused).
/// The reward item came by mail from the char server; here the reward script runs and the title is given.
pub fn achievement_get_reward(c: &Ctx, aid: u32, achievement_id: i32, rewarded: i64) {
    let Some(adb) = achievement_db().find(achievement_id as u32) else {
        log::error!("achievement_reward: Inter server sent a reward claim for achievement {achievement_id} not found in DB.");
        return;
    };
    if rewarded == 0 {
        clif_achievement_reward_ack(c, aid, 0, achievement_id);
        return;
    }
    let Some(mut p) = pa(c, aid) else { return };
    let Some(i) = find_index(&p, achievement_id) else { return };
    // Only update in the cache, db was updated already
    p.achievements[i].rewarded = rewarded;
    p.save = true;
    put_pa(c, &p);
    if let Some(key) = reward_code(adb) {
        vm::run_script(&Rt::new(c), &key, 0, aid, npc_run::fake_npc_id(c));
    }
    if adb.rewards.title_id != 0 {
        if let Some(mut p) = pa(c, aid) {
            p.titles.push(adb.rewards.title_id as i32);
            put_pa(c, &p);
        }
        clif_achievement_list_all(c, aid);
    } else {
        clif_achievement_reward_ack(c, aid, 1, achievement_id);
        clif_achievement_update(c, aid, AchRef::Index(i));
    }
}

/// achievement.cpp achievement_check_reward: a completed achievement whose reward was not taken is claimed from the
/// char server (intif_achievement_reward); every refusal answers 0x0a26 with 0.
pub fn achievement_check_reward(c: &Ctx, aid: u32, achievement_id: i32) {
    let Some(p) = pa(c, aid) else { return };
    let Some(adb) = achievement_db().find(achievement_id as u32) else {
        log::error!("achievement_reward: Trying to reward achievement {achievement_id} not found in DB.");
        clif_achievement_reward_ack(c, aid, 0, achievement_id);
        return;
    };
    let Some(i) = find_index(&p, achievement_id) else {
        clif_achievement_reward_ack(c, aid, 0, achievement_id);
        return;
    };
    if p.achievements[i].rewarded > 0 || p.achievements[i].completed == 0 {
        clif_achievement_reward_ack(c, aid, 0, achievement_id);
        return;
    }
    // intif_achievement_reward: the char server is always there (it never returns 0 here)
    intif_achievement_reward(c, aid, adb);
}

/// achievement.cpp achievement_get_titles: sd->titles from the completed achievements that give one.
pub fn achievement_get_titles(c: &Ctx, char_id: u32) {
    let Some(mut p) = c.db().pc_achievement().char_id().find(char_id) else { return };
    p.titles.clear();
    for a in &p.achievements {
        if let Some(adb) = achievement_db().find(a.achievement_id as u32) {
            // If the achievement has a title and is complete, give it to the player
            if adb.rewards.title_id != 0 && a.completed > 0 {
                p.titles.push(adb.rewards.title_id as i32);
            }
        }
    }
    put_pa(c, &p);
}

/// achievement.cpp achievement_free (unit_free: the log goes with the session, and so does sd->titles).
pub fn achievement_free(c: &Ctx, aid: u32) {
    c.db().pc_achievement().account_id().delete(aid);
}

/// achievement.cpp achievement_check_progress: -1 when the player does not have the achievement, -2 for an
/// unknown type.
pub fn achievement_check_progress(c: &Ctx, aid: u32, achievement_id: i32, type_: i32) -> i32 {
    let p = pa(c, aid);
    // Achievement ID is not needed so skip the lookup
    if type_ == ACHIEVEINFO_LEVEL {
        return p.map(|p| p.level).unwrap_or(0);
    } else if type_ == ACHIEVEINFO_SCORE {
        return p.map(|p| p.total_score).unwrap_or(0);
    }
    let Some(p) = p else { return -1 };
    let Some(i) = find_index(&p, achievement_id) else { return -1 };
    let a = &p.achievements[i];
    if (ACHIEVEINFO_COUNT1..=ACHIEVEINFO_COUNT10).contains(&type_) {
        a.cnt((type_ - 1) as usize)
    } else if type_ == ACHIEVEINFO_COMPLETE {
        (a.completed > 0) as i32
    } else if type_ == ACHIEVEINFO_COMPLETEDATE {
        a.completed as i32
    } else if type_ == ACHIEVEINFO_GOTREWARD {
        (a.rewarded > 0) as i32
    } else {
        -2
    }
}

/// achievement.cpp achievement_level: recounts the total score and the level; returns (and keeps, see INFO) the
/// numbers of the client's level bar. With `flag`, a level change gives AG_GOAL_ACHIEVE a try.
pub fn achievement_level(c: &Ctx, aid: u32, flag: bool) -> (i32, i32) {
    let Some(mut p) = pa(c, aid) else { return INFO.with(|i| i.get()) };
    let db = achievement_db();
    // Recount total score
    p.total_score = p.achievements.iter().filter(|a| a.completed > 0).fold(0i32, |s, a| s.wrapping_add(a.score));
    let total = p.total_score;
    let old_level = p.level;
    let points = |level: i32| -> Option<i32> {
        if (0..=u16::MAX as i32).contains(&level) {
            db.level_points(level as u16).map(|v| v as i32)
        } else {
            None
        }
    };
    let (left, right);
    p.level = 0;
    loop {
        let level = points(p.level);
        if let Some(lp) = level {
            if total > lp {
                // Check if there is another level
                if points(p.level + 1).is_none() {
                    left = total - lp;
                    right = 0;
                    // Increase the level for client side display
                    p.level += 1;
                    break;
                } else {
                    // Enough points for this level, check the next one
                    p.level += 1;
                    continue;
                }
            }
        }
        if p.level == 0 {
            left = total;
            right = level.unwrap_or(0);
        } else {
            let previous = points(p.level - 1).unwrap_or(0);
            left = total - previous;
            right = level.unwrap_or(0) - previous;
        }
        break;
    }
    let new_level = p.level;
    put_pa(c, &p);
    INFO.with(|i| i.set((left, right)));
    if flag && old_level != new_level {
        // Give AG_GOAL_ACHIEVE
        achievement_update_objective(c, Some(aid), AG_GOAL_ACHIEVE, &[]);
    }
    INFO.with(|i| i.get())
}

/// achievement.cpp achievement_target_complete.
fn achievement_target_complete(ad: &AchievementDb, current: &[i32; MAX_ACHIEVEMENT_OBJECTIVES]) -> bool {
    ad.targets.iter().all(|(tid, t)| current.get(*tid as usize).copied().unwrap_or(0) >= t.count)
}

/// achievement.cpp achievement_update_objectives: one achievement against an event of its group.
fn achievement_update_objectives(c: &Ctx, aid: u32, ad: &AchievementDb, group: i32, update_count: &[i32; MAX_ACHIEVEMENT_OBJECTIVES]) -> bool {
    if group <= AG_NONE || group >= AG_MAX {
        return false;
    }
    if group != ad.group {
        return false;
    }
    let Some(p) = pa(c, aid) else { return false };
    let id = ad.achievement_id as i32;
    let mut entry: Option<usize> = None;
    let mut is_new = false;
    let mut changed = false;
    let mut complete = false;
    let mut current = [0i32; MAX_ACHIEVEMENT_OBJECTIVES]; // Player's current objective values
    match find_index(&p, id) {
        None => {
            // Achievement isn't in player's log: check to see if dependents are complete before adding to player's log
            if !achievement_check_dependent(c, aid, id) {
                return false;
            }
            is_new = true;
        }
        Some(i) => {
            if p.achievements[i].completed > 0 {
                return false; // Player has completed the achievement
            }
            for (j, v) in current.iter_mut().enumerate() {
                *v = p.achievements[i].cnt(j);
            }
            entry = Some(i);
        }
    }
    match group {
        AG_ADD_FRIEND | AG_BABY | AG_CHATTING_COUNT | AG_CHATTING_CREATE | AG_CHATTING_DYING | AG_GET_ITEM | AG_GET_ZENY | AG_GOAL_LEVEL
        | AG_GOAL_STATUS | AG_JOB_CHANGE | AG_MARRY | AG_PARTY | AG_ENCHANT_FAIL | AG_ENCHANT_SUCCESS => {
            let Some(cond) = condition_code(c, ad) else { return false };
            if !achievement_check_condition(c, Some(&cond), aid) {
                return false; // Parameters weren't met
            }
            changed = true;
            complete = true;
        }
        AG_SPEND_ZENY => {
            let cond = condition_code(c, ad);
            if ad.targets.is_empty() || cond.is_none() {
                return false;
            }
            for (tid, t) in &ad.targets {
                let k = *tid as usize;
                if k < MAX_ACHIEVEMENT_OBJECTIVES && current[k] < t.count {
                    current[k] = current[k].wrapping_add(update_count[k]);
                }
            }
            if !achievement_check_condition(c, cond.as_ref(), aid) {
                return false; // Parameters weren't met
            }
            changed = true;
            if achievement_target_complete(ad, &current) {
                complete = true;
            }
        }
        AG_BATTLE | AG_TAMING => {
            if ad.targets.is_empty() {
                return false;
            }
            for (tid, t) in &ad.targets {
                let k = *tid as usize;
                // Here update_count[0] contains the killed/tamed monster ID
                if k < MAX_ACHIEVEMENT_OBJECTIVES && t.mob as u32 == update_count[0] as u32 && current[k] < t.count {
                    current[k] += 1;
                    changed = true;
                }
            }
            if !changed {
                return false;
            }
            if achievement_target_complete(ad, &current) {
                complete = true;
            }
        }
        AG_GOAL_ACHIEVE => {
            // (no check that the condition exists: a missing one is a nullptr script, which is false)
            let cond = condition_code(c, ad);
            if !achievement_check_condition(c, cond.as_ref(), aid) {
                return false; // Parameters weren't met
            }
            changed = true;
            complete = true;
        }
        // AG_CHATTING is commented out in rAthena; AG_ADVENTURE and AG_EAT have no case
        _ => {}
    }
    if is_new {
        // Always add the achievement if it was completed; if not, check if it has a counter
        let has_counter = complete || current.iter().any(|&v| v != 0);
        if has_counter {
            match achievement_add(c, aid, id) {
                Some(i) => entry = Some(i),
                None => return false, // Failed to add achievement
            }
        } else {
            changed = false;
        }
    }
    if changed {
        // memcpy(entry->count, current_count): the entry stays at its index while achievement_add's recursion adds
        // and completes others (they go after it), see clif_achievement_update
        if let (Some(i), Some(mut p)) = (entry, pa(c, aid)) {
            if let Some(a) = p.achievements.get_mut(i) {
                a.count = current.to_vec();
            }
            put_pa(c, &p);
        }
        achievement_update_achievement(c, aid, id, complete);
    }
    true
}

/// achievement.cpp achievement_update_objective: an event of `group` with its arguments (ARG0.. for the condition
/// scripts, set as permanent character variables while the achievements are checked). `sd` may be absent.
pub fn achievement_update_objective(c: &Ctx, sd: Option<u32>, group: i32, args: &[i32]) {
    if !feature() {
        return;
    }
    let Some(aid) = sd else { return };
    if crate::pc::get(c, aid).is_none() {
        return;
    }
    let mut count = [0i32; MAX_ACHIEVEMENT_OBJECTIVES];
    let n = args.len().min(MAX_ACHIEVEMENT_OBJECTIVES);
    for (i, &a) in args.iter().take(n).enumerate() {
        count[i] = a;
        pc_setglobalreg(c, aid, &format!("ARG{i}"), a as i64);
    }
    for ach in achievement_db().iter() {
        achievement_update_objectives(c, aid, ach, group, &count);
    }
    // Remove variables that might have been set
    for i in 0..n {
        pc_setglobalreg(c, aid, &format!("ARG{i}"), 0);
    }
}

/// achievement.cpp achievement_update_objective_sub: mob_dead's map_foreachinallrange over the killer's party
/// members around the monster (mob agent).
pub fn achievement_update_objective_sub(c: &Ctx, aid: u32, party_id: u32, mob_id: i32) -> i32 {
    let Some(p) = pa(c, aid) else { return 0 };
    if p.achievements.is_empty() {
        return 0; // sd->achievement_data.achievements == nullptr
    }
    if crate::pc::character_of(c, aid).map(|ch| ch.party_id) != Some(party_id) {
        return 0;
    }
    achievement_update_objective(c, Some(aid), AG_BATTLE, &[mob_id]);
    1
}

// ------------------------------------------------------------------------------------------------ intif.cpp

/// pc_reg_received (feature.achievement: the log starts empty, level 0) → intif_request_achievements →
/// mapif_parse_achievement_load → intif_parse_achievements: the log arrives; every group is checked once, the
/// level and the titles are computed, and the client gets ZC_ACH_UPDATE (empty entry) then ZC_ALL_ACH_LIST. The
/// char server is part of this database, so the answer is immediate (pc.rs calls this where it arrives).
pub fn intif_parse_achievements(c: &Ctx, aid: u32) {
    if !feature() {
        return;
    }
    let Some(pc) = crate::pc::get(c, aid) else { return };
    let char_id = pc.char_id;
    let received = mapif_achievements_fromsql(c, char_id);
    let num_received = received.len();
    // CREATE(achievements, num_received): zeroed
    let mut log = vec![Achievement::zeroed(0); num_received];
    let mut incomplete = 0usize;
    let mut k = num_received;
    let mut count = 0usize;
    for mut r in received {
        let Some(adb) = achievement_db().find(r.achievement_id as u32) else {
            log::error!("intif_parse_achievements: Achievement {} not found in achievement_db.", r.achievement_id);
            continue;
        };
        r.score = adb.score;
        if r.completed == 0 {
            // Insert at the beginning
            log[incomplete] = r;
            incomplete += 1;
        } else {
            // Insert at the end
            k -= 1;
            log[k] = r;
        }
        count += 1;
    }
    if incomplete < k {
        // incompleteCount and k didn't meet in the middle: some entries were skipped
        if k < num_received {
            // "Move the entries at the end to fill the gap": memmove(&achievements[k], &achievements[incompleteCount],
            // sizeof * (num_received - k)) — rAthena copies in this direction, so the completed entries are
            // overwritten; kept as it does
            let n = num_received - k;
            let src: Vec<Achievement> = log[incomplete..incomplete + n].to_vec();
            log[k..k + n].clone_from_slice(&src);
        }
        log.truncate(count);
    }
    let p = PcAchievement { account_id: aid, char_id, total_score: 0, level: 0, save: false, incomplete_count: incomplete as u16, achievements: log, titles: Vec::new() };
    put_pa(c, &p);
    // Check all conditions and counters on login
    for group in (AG_NONE + 1)..AG_MAX {
        achievement_update_objective(c, Some(aid), group, &[]);
    }
    achievement_level(c, aid, false); // Calculate level info but don't give any AG_GOAL_ACHIEVE achievements
    achievement_get_titles(c, char_id); // Populate the title list for completed achievements
    clif_achievement_update(c, aid, AchRef::None);
    clif_achievement_list_all(c, aid);
}

/// chrif_save's achievement part: `if (sd->achievement_data.save) intif_achievement_save(sd)`.
pub fn chrif_save(c: &Ctx, aid: u32) {
    if pa(c, aid).map(|p| p.save).unwrap_or(false) {
        intif_achievement_save(c, aid);
    }
}

/// intif.cpp intif_achievement_save (0x3063): the whole log goes to the char server.
pub fn intif_achievement_save(c: &Ctx, aid: u32) {
    let Some(mut p) = pa(c, aid) else { return };
    intif::send(Inter::AchievementSave { char_id: p.char_id, achievements: p.achievements.clone() });
    p.save = false;
    put_pa(c, &p);
}

/// intif.cpp intif_parse_achievementsave (0x3863).
pub fn intif_parse_achievementsave(c: &Ctx, char_id: u32, success: bool) {
    let Some(p) = c.db().pc_achievement().char_id().find(char_id) else { return }; // User not online anymore
    if !success {
        let name = crate::pc::character_of(c, p.account_id).map(|ch| ch.name).unwrap_or_default();
        log::error!("intif_parse_achievementsave: Failed to save achievement(s) for character {name} ({char_id})!");
    }
}

/// intif.cpp intif_achievement_reward (0x3064): the claim, with the reward item and the names for the mail.
fn intif_achievement_reward(c: &Ctx, aid: u32, adb: &AchievementDb) {
    let Some(p) = crate::pc::character_of(c, aid) else { return };
    // safestrncpy(…, sd->status.name, NAME_LENGTH), safestrncpy(…, adb->name, ACHIEVEMENT_NAME_LENGTH)
    let name = crate::net::wire::latin1(&adb.name);
    intif::send(Inter::AchievementReward {
        char_id: p.id,
        achievement_id: adb.achievement_id as i32,
        nameid: adb.rewards.nameid,
        amount: adb.rewards.amount,
        char_name: safe_name(&p.name),
        achievement_name: crate::net::wire::from_latin1(&name[..name.len().min(ACHIEVEMENT_NAME_LENGTH - 1)]),
    });
}

/// intif.cpp intif_parse_achievementreward (0x3864).
pub fn intif_parse_achievementreward(c: &Ctx, char_id: u32, achievement_id: i32, rewarded: u32) {
    let Some(p) = c.db().pc_achievement().char_id().find(char_id) else { return }; // User not online anymore
    achievement_get_reward(c, p.account_id, achievement_id, rewarded as i64);
}

// ------------------------------------------------------------------------------------------------ int_achievement.cpp

/// The `achievement` rows of a character in the order MariaDB returns them (record slot order).
fn rows_of(c: &Ctx, char_id: u32) -> Vec<AchievementRow> {
    let mut v: Vec<AchievementRow> = c.db().achievement().by_char().filter(&char_id).collect();
    v.sort_by_key(|r| (r.pos, r.id));
    v
}

/// int_achievement.cpp mapif_achievements_fromsql: `SELECT id, COALESCE(UNIX_TIMESTAMP(completed),0),
/// COALESCE(UNIX_TIMESTAMP(rewarded),0), count1..count10 FROM achievement WHERE char_id = ?`.
fn mapif_achievements_fromsql(c: &Ctx, char_id: u32) -> Vec<Achievement> {
    let v: Vec<Achievement> = rows_of(c, char_id)
        .into_iter()
        .map(|r| {
            let mut count = r.count.clone();
            count.resize(MAX_ACHIEVEMENT_OBJECTIVES, 0);
            // bound as SQLDT_INT32 into the (zeroed) time_t fields
            Achievement { achievement_id: r.achievement_id, count, completed: r.completed as i32 as i64, rewarded: r.rewarded as i32 as i64, score: 0 }
        })
        .collect();
    log::info!("achievement load complete from DB - id: {char_id} (total: {})", v.len());
    v
}

/// The slot bookkeeping row (created from the table's highest slot when missing).
fn slots(db: &spacetimedb::Local) -> AchievementSlots {
    match db.achievement_slots().id().find(0) {
        Some(s) => s,
        None => {
            let next = db.achievement().iter().map(|r| r.pos + 1).max().unwrap_or(0);
            db.achievement_slots().insert(AchievementSlots { id: 0, next, free: Vec::new() })
        }
    }
}

/// A record slot for a new row (the last freed one first, else a new one at the end).
fn take_slot(c: &Ctx) -> u64 {
    let mut s = slots(c.db());
    let pos = match s.free.pop() {
        Some(p) => p,
        None => {
            s.next += 1;
            s.next - 1
        }
    };
    c.db().achievement_slots().id().update(s);
    pos
}

/// int_achievement.cpp mapif_achievement_delete.
fn mapif_achievement_delete(c: &Ctx, char_id: u32, achievement_id: i32) -> bool {
    let rows: Vec<AchievementRow> = c.db().achievement().by_char().filter(&char_id).filter(|r| r.achievement_id == achievement_id).collect();
    for r in rows {
        c.db().achievement().id().delete(r.id);
        let mut s = slots(c.db());
        s.free.push(r.pos);
        c.db().achievement_slots().id().update(s);
    }
    true
}

/// int_achievement.cpp mapif_achievement_add: an entry without counters, completion or reward is not saved.
fn mapif_achievement_add(c: &Ctx, char_id: u32, ad: &Achievement) -> bool {
    if ad.count.iter().all(|&v| v == 0) && ad.completed == 0 && ad.rewarded == 0 {
        return true; // Do not save
    }
    let pos = take_slot(c);
    let mut count = ad.count.clone();
    count.resize(MAX_ACHIEVEMENT_OBJECTIVES, 0);
    c.db().achievement().insert(AchievementRow {
        id: 0,
        pos,
        char_id,
        achievement_id: ad.achievement_id,
        completed: ad.completed as u32,
        rewarded: ad.rewarded as u32,
        count,
    });
    true
}

/// int_achievement.cpp mapif_achievement_update.
fn mapif_achievement_update(c: &Ctx, char_id: u32, ad: &Achievement) -> bool {
    let rows: Vec<AchievementRow> = c.db().achievement().by_char().filter(&char_id).filter(|r| r.achievement_id == ad.achievement_id).collect();
    for r in rows {
        let mut count = ad.count.clone();
        count.resize(MAX_ACHIEVEMENT_OBJECTIVES, 0);
        c.db().achievement().id().update(AchievementRow { completed: ad.completed as u32, rewarded: ad.rewarded as u32, count, ..r });
    }
    true
}

/// int_achievement.cpp mapif_parse_achievement_save (0x3063): the rows are brought in line with the log (changed
/// counts / completion / reward updated, new entries inserted, the others deleted), then the ack (0x3863).
pub fn mapif_parse_achievement_save(c: &Ctx, char_id: u32, new_ad: &[Achievement]) {
    let mut old_ad = mapif_achievements_fromsql(c, char_id);
    let mut success = true;
    for n in new_ad {
        match old_ad.iter().position(|o| o.achievement_id == n.achievement_id) {
            Some(j) => {
                // Update existing achievements: only counts, complete, and reward are changable.
                let o = &old_ad[j];
                let counts_differ = (0..MAX_ACHIEVEMENT_OBJECTIVES).any(|k| n.cnt(k) != o.cnt(k));
                if counts_differ || n.completed != o.completed || n.rewarded != o.rewarded {
                    success = mapif_achievement_update(c, char_id, n);
                    if !success {
                        break;
                    }
                }
                old_ad.remove(j); // Compact array
            }
            None => {
                // Add new achievements
                if n.achievement_id != 0 {
                    success = mapif_achievement_add(c, char_id, n);
                    if !success {
                        break;
                    }
                }
            }
        }
    }
    // Achievements not in new_ad but in old_ad are to be erased.
    for o in &old_ad {
        success = mapif_achievement_delete(c, char_id, o.achievement_id);
        if !success {
            break;
        }
    }
    intif::send(Inter::AchievementSaveAck { char_id, success });
}

/// int_achievement.cpp mapif_parse_achievement_reward (0x3064): `UPDATE achievement SET rewarded = NOW WHERE
/// char_id AND id AND completed IS NOT NULL AND rewarded IS NULL`; with a reward item the char server mails it
/// (mail_sendmail); the answer (0x3864) carries the time, 0 when nothing was updated or the mail failed.
pub fn mapif_parse_achievement_reward(c: &Ctx, char_id: u32, achievement_id: i32, nameid: u32, amount: u16, char_name: &str, achievement_name: &str) {
    let mut current = now(c) as u32;
    let row = c.db().achievement().by_char().filter(&char_id).find(|r| r.achievement_id == achievement_id && r.completed != 0 && r.rewarded == 0);
    match row {
        None => current = 0, // Sql_NumRowsAffected <= 0
        Some(r) => {
            c.db().achievement().id().update(AchievementRow { rewarded: current, ..r });
            // `RFIFOW(fd,10) > 0`: the low 16 bits of the item id decide "Do not send a mail if no item reward"
            if nameid & 0xffff > 0 {
                let mut item = crate::inventory::Item::new(nameid);
                item.amount = amount as i16;
                item.identify = 1;
                // conf/msg_conf/char_msg.conf 227 "GM", 228 "Achievement Reward Mail", 229 "[%s] Achievement Reward."
                let mail_sender = "GM";
                let mail_receiver = safe_name(char_name);
                let mail_title = "Achievement Reward Mail";
                let mail_text = format!("[{achievement_name}] Achievement Reward.");
                // RoDEX (social2 agent, 678e275)
                if !crate::int_mail::mail_sendmail(c, 0, mail_sender, char_id, &mail_receiver, mail_title, &mail_text, 0, &[item]) {
                    current = 0;
                }
            }
        }
    }
    intif::send(Inter::AchievementRewardAck { char_id, achievement_id, rewarded: current });
}

/// safestrncpy(mail_receiver, name, NAME_LENGTH).
fn safe_name(s: &str) -> String {
    let b = crate::net::wire::latin1(s);
    crate::net::wire::from_latin1(&b[..b.len().min(NAME_LENGTH - 1)])
}

/// char.cpp char_delete: `DELETE FROM achievement WHERE char_id = ?` (the MyISAM slots are freed).
pub fn char_delete(db: &spacetimedb::Local, char_id: u32) {
    let rows: Vec<AchievementRow> = db.achievement().by_char().filter(&char_id).collect();
    if rows.is_empty() {
        return;
    }
    let mut s = slots(db);
    for r in rows {
        db.achievement().id().delete(r.id);
        s.free.push(r.pos);
    }
    db.achievement_slots().id().update(s);
}

// ------------------------------------------------------------------------------------------------ clif.cpp

/// The entry clif_achievement_update is given: a log entry (by index; rAthena passes a pointer into the log, which
/// a recursion in achievement_level may move — see achievement_update_objectives), a stand-alone struct (the removed
/// fake entry) or none.
pub enum AchRef {
    None,
    Index(usize),
    Dummy(Achievement),
}

/// One ach_list_info (50 bytes).
fn write_entry(w: &mut Vec<u8>, a: &Achievement) {
    w.extend_from_slice(&(a.achievement_id as u32).to_le_bytes());
    w.push((a.completed > 0) as u8);
    for j in 0..MAX_ACHIEVEMENT_OBJECTIVES {
        w.extend_from_slice(&(a.cnt(j) as u32).to_le_bytes());
    }
    w.extend_from_slice(&(a.completed as u32).to_le_bytes());
    w.push((a.rewarded > 0) as u8);
}

/// clif.cpp clif_achievement_list_all: ZC_ALL_ACH_LIST (0x0a23) with every entry of the log (none: not sent).
pub fn clif_achievement_list_all(c: &Ctx, aid: u32) {
    if !feature() {
        disabled_message(c, aid);
        return;
    }
    let Some(p) = pa(c, aid) else { return };
    let count = p.achievements.len(); // All achievements should be sent to the client
    if count == 0 {
        return;
    }
    let len = 50 * count + 22;
    let (left, right) = achievement_level(c, aid, true);
    let Some(p) = pa(c, aid) else { return };
    let mut w = Vec::with_capacity(len);
    w.extend_from_slice(&0x0a23u16.to_le_bytes());
    w.extend_from_slice(&(len as u16).to_le_bytes());
    w.extend_from_slice(&(count as u32).to_le_bytes()); // started / completed
    w.extend_from_slice(&(p.total_score as u32).to_le_bytes()); // Top number
    w.extend_from_slice(&(p.level as u16).to_le_bytes()); // Achievement Level (gold circle)
    w.extend_from_slice(&(left as u32).to_le_bytes()); // Achievement EXP (left number in bar)
    w.extend_from_slice(&(right as u32).to_le_bytes()); // Achievement EXP TNL (right number in bar)
    for i in 0..count {
        let a = p.achievements.get(i).cloned().unwrap_or_default();
        write_entry(&mut w, &a);
    }
    send_fd(c, aid, &w);
}

/// clif.cpp clif_achievement_update: ZC_ACH_UPDATE (0x0a24) with the totals and one entry. rAthena clears only 40
/// of the 50 entry bytes when there is no entry (memset(WFIFOP(fd, 16), 0, 40)); the last 10 bytes are whatever the
/// send buffer held there — zeros here.
pub fn clif_achievement_update(c: &Ctx, aid: u32, ach: AchRef) {
    if !feature() {
        disabled_message(c, aid);
        return;
    }
    let (left, right) = achievement_level(c, aid, true);
    let Some(p) = pa(c, aid) else { return };
    let mut w = Vec::with_capacity(66);
    w.extend_from_slice(&0x0a24u16.to_le_bytes());
    w.extend_from_slice(&(p.total_score as u32).to_le_bytes()); // Total Achievement Points (top of screen)
    w.extend_from_slice(&(p.level as u16).to_le_bytes()); // Achievement Level (gold circle)
    w.extend_from_slice(&(left as u32).to_le_bytes()); // Achievement EXP (left number in bar)
    w.extend_from_slice(&(right as u32).to_le_bytes()); // Achievement EXP TNL (right number in bar)
    match ach {
        AchRef::Index(i) => write_entry(&mut w, &p.achievements.get(i).cloned().unwrap_or_default()),
        AchRef::Dummy(a) => write_entry(&mut w, &a),
        AchRef::None => w.resize(66, 0),
    }
    send_fd(c, aid, &w);
}

/// msg 772 "Achievements are disabled." in color_table[COLOR_RED] (feature.achievement off).
fn disabled_message(c: &Ctx, aid: u32) {
    if let Some(u) = crate::unit::get(c, aid) {
        let m = crate::net::wire::latin1z(crate::data::msg_txt(772));
        crate::clif::messagecolor_target(c, &u, crate::social::COLOR_RED, &m, false, crate::clif::Target::SelfOnly, None);
    }
}

/// clif.cpp clif_parse_AchievementCheckReward (CZ_REQ_ACH_REWARD 0x0a25): the log is saved first, then the claim.
pub fn parse_achievement_check_reward(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(id) = crate::net::wire::Reader::at(pkt, 2).i32() else { return };
    if pa(c, aid).map(|p| p.save).unwrap_or(false) {
        intif_achievement_save(c, aid);
    }
    achievement_check_reward(c, aid, id);
}

/// clif.cpp clif_achievement_reward_ack: ZC_REQ_ACH_REWARD_ACK (0x0a26) result.B achievement id.L.
pub fn clif_achievement_reward_ack(c: &Ctx, aid: u32, result: u8, achievement_id: i32) {
    let mut w = Vec::with_capacity(7);
    w.extend_from_slice(&0x0a26u16.to_le_bytes());
    w.push(result);
    w.extend_from_slice(&achievement_id.to_le_bytes());
    send_fd(c, aid, &w);
}

// ------------------------------------------------------------------------------------------------ titles

/// clif.cpp clif_parse_change_title (CZ_REQ_CHANGE_TITLE 0x0a2e): a title the character owns (0 or less: none).
pub fn parse_change_title(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(title_id) = crate::net::wire::Reader::at(pkt, 2).i32() else { return };
    let Some(mut ch) = crate::pc::character_of(c, aid) else { return };
    if title_id == ch.title_id as i32 {
        return; // It is exactly the same as the old one
    } else if title_id <= 0 {
        ch.title_id = 0;
    } else {
        if !titles(c, aid).contains(&title_id) {
            clif_change_title_ack(c, aid, 1, title_id as u32);
            return;
        }
        ch.title_id = title_id as u32;
    }
    crate::status::put_char(c, &ch);
    if let Some(u) = crate::unit::get(c, aid) {
        crate::clif::name_area(c, &u);
    }
    clif_change_title_ack(c, aid, 0, title_id as u32);
}

/// clif.cpp clif_change_title_ack: ZC_ACK_CHANGE_TITLE (0x0a2f) result.B title id.L (clif_session_isValid).
pub fn clif_change_title_ack(c: &Ctx, aid: u32, result: u8, title_id: u32) {
    if !crate::pc::is_active_session(c, aid) {
        return;
    }
    let mut w = Vec::with_capacity(7);
    w.extend_from_slice(&0x0a2fu16.to_le_bytes());
    w.push(result);
    w.extend_from_slice(&title_id.to_le_bytes());
    send_fd(c, aid, &w);
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ach(id: i32, completed: i64) -> Achievement {
        Achievement { achievement_id: id, count: vec![0; MAX_ACHIEVEMENT_OBJECTIVES], completed, rewarded: 0, score: 0 }
    }

    #[test]
    fn entry_layout() {
        let mut a = ach(120001, 1_700_000_000);
        a.count[0] = 3;
        a.rewarded = 5;
        let mut w = Vec::new();
        write_entry(&mut w, &a);
        assert_eq!(w.len(), 50);
        assert_eq!(&w[0..4], &120001u32.to_le_bytes());
        assert_eq!(w[4], 1);
        assert_eq!(&w[5..9], &3u32.to_le_bytes());
        assert_eq!(&w[45..49], &1_700_000_000u32.to_le_bytes());
        assert_eq!(w[49], 1);
    }

    #[test]
    fn provider_keys() {
        // 220000 "Community begin" (AG_CHATTING_CREATE) has a condition and no reward script
        let src = provider(None, "c220000").unwrap();
        assert!(crate::net::wire::from_latin1(&src.text).contains("ARG0 == 1"));
        assert!(provider(None, "r220000").is_none());
        // 240001 "Reaching Level 1" rewards a script
        assert!(provider(None, "r240001").is_some());
        assert!(provider(None, "c999999").is_none());
    }

    #[test]
    fn target_complete() {
        let ad = achievement_db().find(128002).unwrap(); // XM_CELINE_KIMI x25
        let mut cur = [0i32; MAX_ACHIEVEMENT_OBJECTIVES];
        assert!(!achievement_target_complete(ad, &cur));
        cur[0] = 25;
        assert!(achievement_target_complete(ad, &cur));
    }
}
