//! Script states that outlive a reducer call: rAthena's `st_db` (script_alloc_state / script_free_state) as the
//! `script_state` table. Every state gets a row when it is allocated (its id is the `st` pointer that sd->st, bk_st
//! and sleep timers hold); the row's `data` is the suspended state — stack, position, call frames, `.@` scopes,
//! attached player and NPC, dialog flags — encoded with BSATN (SpacetimeDB's compact bincode-like encoding) from the
//! `Saved*` types below, and written whenever the state stops (next / menu / input / sleep) so a later reducer can
//! resume it. Variable references are saved by name, never by str_data id (ids are per module instance).

use std::cell::RefCell;
use std::collections::{BTreeMap, HashSet};

use spacetimedb::{table, SpacetimeType, Table};

use super::vm::*;
use crate::world::Ctx;

#[table(accessor = script_state, index(accessor = by_oid, btree(columns = [oid])), index(accessor = by_rid, btree(columns = [rid])))]
#[derive(Clone)]
pub struct ScriptStateRow {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub rid: u32,
    pub oid: u32,
    /// st->sleep.timer (0 = none): the sleep_db.
    pub sleep_timer: u64,
    pub created_tick: i64,
    /// Format version of `data` (1).
    pub version: u8,
    /// The suspended state (empty while it has never stopped).
    pub data: Vec<u8>,
}

#[derive(SpacetimeType, Clone, Debug)]
pub enum SavedData {
    Nop,
    Int(i64),
    Str(Vec<u8>),
    Pos(i32),
    Name(SavedName),
    Arg,
    RetInfo(RetInfo),
}

#[derive(SpacetimeType, Clone, Debug)]
pub struct SavedName {
    pub name: Vec<u8>,
    pub idx: u32,
    pub rf: RegRef,
}

#[derive(SpacetimeType, Clone, Debug)]
pub struct SavedVar {
    pub key: Vec<u8>,
    pub idx: u32,
    pub val: Val,
}

#[derive(SpacetimeType, Clone, Debug)]
pub struct SavedScope {
    pub id: u32,
    pub vars: Vec<SavedVar>,
}

#[derive(SpacetimeType, Clone, Debug)]
pub struct SavedState {
    pub stack: Vec<SavedData>,
    pub start: i32,
    pub end: i32,
    pub defsp: i32,
    pub pos: i32,
    pub state: u8,
    pub rid: u32,
    pub oid: u32,
    pub code: CodeKey,
    pub scopes: Vec<SavedScope>,
    pub cur_scope: u32,
    pub next_scope: u32,
    pub sleep_tick: i32,
    pub sleep_timer: u64,
    pub sleep_charid: u32,
    pub bk_st: u64,
    pub bk_npcid: u32,
    pub freeloop: bool,
    pub npc_item_flag: bool,
    pub mes_active: bool,
    pub clear_cutin: bool,
    pub funcname: String,
}

pub const STATE_VERSION: u8 = 1;

thread_local! {
    /// States executing right now (on the Rust call stack): their rows are stale and their state is RUN.
    static RUNNING: RefCell<HashSet<u64>> = RefCell::new(HashSet::new());
}

pub fn mark_running(id: u64, on: bool) {
    if id == 0 {
        return;
    }
    RUNNING.with(|r| {
        if on {
            r.borrow_mut().insert(id);
        } else {
            r.borrow_mut().remove(&id);
        }
    });
}

pub fn is_running(id: u64) -> bool {
    RUNNING.with(|r| r.borrow().contains(&id))
}

thread_local! {
    /// Running states that were detached from their player and attached again while one of their buildins ran
    /// (achievement.rs achievement_check_condition: script_detach_rid + script_attach_state on sd->st): their fields
    /// are on the Rust stack, so run_func applies the result — no backup script (bk_st / bk_npcid 0) — when the
    /// buildin returns.
    static BK_RESET: RefCell<HashSet<u64>> = RefCell::new(HashSet::new());
}

/// Asks the VM to drop the backup of running state `id` when its current buildin returns (see BK_RESET).
pub fn request_bk_reset(id: u64) {
    if id != 0 {
        BK_RESET.with(|r| r.borrow_mut().insert(id));
    }
}

/// Whether a backup reset was requested for `id` (and forgets the request).
pub fn take_bk_reset(id: u64) -> bool {
    id != 0 && BK_RESET.with(|r| r.borrow_mut().remove(&id))
}

/// Encodes a state.
pub fn encode(st: &State) -> Vec<u8> {
    let stack = st
        .stack
        .iter()
        .map(|d| match d {
            Data::Nop => SavedData::Nop,
            Data::Int(v) => SavedData::Int(*v),
            Data::Str(s) => SavedData::Str(s.clone()),
            Data::Pos(p) => SavedData::Pos(*p),
            Data::Name(id, idx, rf) => SavedData::Name(SavedName { name: name_of(*id), idx: *idx, rf: rf.clone() }),
            Data::Arg => SavedData::Arg,
            Data::RetInfo(ri) => SavedData::RetInfo(ri.clone()),
        })
        .collect();
    let scopes = st
        .scopes
        .iter()
        .map(|(id, db)| SavedScope { id: *id, vars: db.vars.iter().map(|((k, i), v)| SavedVar { key: k.clone(), idx: *i, val: v.clone() }).collect() })
        .collect();
    let s = SavedState {
        stack,
        start: st.start,
        end: st.end,
        defsp: st.defsp,
        pos: st.pos,
        state: st.state,
        rid: st.rid,
        oid: st.oid,
        code: st.code.clone(),
        scopes,
        cur_scope: st.cur_scope,
        next_scope: st.next_scope,
        sleep_tick: st.sleep_tick,
        sleep_timer: st.sleep_timer,
        sleep_charid: st.sleep_charid,
        bk_st: st.bk_st,
        bk_npcid: st.bk_npcid,
        freeloop: st.freeloop,
        npc_item_flag: st.npc_item_flag,
        mes_active: st.mes_active,
        clear_cutin: st.clear_cutin,
        funcname: st.funcname.clone(),
    };
    spacetimedb::sats::bsatn::to_vec(&s).unwrap_or_default()
}

/// Decodes a state (its bytecode is looked up again; ids are re-interned).
pub fn decode(c: Option<&Ctx>, id: u64, data: &[u8]) -> Option<State> {
    let s: SavedState = match spacetimedb::sats::bsatn::from_slice(data) {
        Ok(s) => s,
        Err(e) => {
            log::error!("script_state {id}: cannot decode ({e:?})");
            return None;
        }
    };
    let buf = super::code::get(c, &s.code)?;
    let stack = s
        .stack
        .into_iter()
        .map(|d| match d {
            SavedData::Nop => Data::Nop,
            SavedData::Int(v) => Data::Int(v),
            SavedData::Str(v) => Data::Str(v),
            SavedData::Pos(p) => Data::Pos(p),
            SavedData::Name(n) => Data::Name(add_str(&n.name), n.idx, n.rf),
            SavedData::Arg => Data::Arg,
            SavedData::RetInfo(ri) => Data::RetInfo(ri),
        })
        .collect();
    let mut scopes = BTreeMap::new();
    for sc in s.scopes {
        let mut db = VarDb::default();
        for v in sc.vars {
            db.vars.insert((v.key, v.idx), v.val);
        }
        scopes.insert(sc.id, db);
    }
    Some(State {
        id,
        stack,
        start: s.start,
        end: s.end,
        defsp: s.defsp,
        pos: s.pos,
        state: s.state,
        rid: s.rid,
        oid: s.oid,
        code: s.code,
        scopes,
        cur_scope: s.cur_scope,
        next_scope: s.next_scope,
        sleep_tick: s.sleep_tick,
        sleep_timer: s.sleep_timer,
        sleep_charid: s.sleep_charid,
        bk_st: s.bk_st,
        bk_npcid: s.bk_npcid,
        freeloop: s.freeloop,
        op2ref: false,
        npc_item_flag: s.npc_item_flag,
        mes_active: s.mes_active,
        clear_cutin: s.clear_cutin,
        funcname: s.funcname,
        buf,
        total_cmds: 0,
    })
}

/// script_alloc_state's st_db entry: gives the state its id.
pub fn alloc(c: &Ctx, st: &mut State) {
    let row = c.db().script_state().insert(ScriptStateRow { id: 0, rid: st.rid, oid: st.oid, sleep_timer: 0, created_tick: c.tick(), version: STATE_VERSION, data: Vec::new() });
    st.id = row.id;
}

/// Writes a suspended state.
pub fn save(c: &Ctx, st: &State) {
    if st.id == 0 {
        return;
    }
    let Some(row) = c.db().script_state().id().find(st.id) else { return };
    c.db().script_state().id().update(ScriptStateRow { rid: st.rid, oid: st.oid, sleep_timer: st.sleep_timer, version: STATE_VERSION, data: encode(st), ..row });
}

/// Loads a suspended state (None when it is gone or was never saved).
pub fn load(c: &Ctx, id: u64) -> Option<State> {
    if id == 0 {
        return None;
    }
    let row = c.db().script_state().id().find(id)?;
    if row.data.is_empty() {
        return None;
    }
    decode(Some(c), id, &row.data)
}

pub fn delete(c: &Ctx, id: u64) {
    if id != 0 {
        c.db().script_state().id().delete(id);
    }
}

pub fn exists(c: &Ctx, id: u64) -> bool {
    id != 0 && c.db().script_state().id().find(id).is_some()
}
