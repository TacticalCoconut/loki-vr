//! script.cpp's virtual machine: script_state / script_stack / script_data, the stack helpers (push_*, pop_stack),
//! get_val / conv_num / conv_str, the operators (op_1, op_2, op_2num, op_2str, op_3), run_func, run_script_main,
//! script_alloc_state / script_free_state and the attach / detach of a state to its player.
//!
//! A state that outlives its reducer call (waiting for dialog input, sleeping, or kept as a backup under another
//! script) is a `script_state` row (state.rs); its id plays the part of rAthena's `st` pointer (sd->st, bk_st).

use std::collections::BTreeMap;
use std::rc::Rc;

use spacetimedb::SpacetimeType;

use super::compiler::*;
use super::{code, reg};
use crate::world::Ctx;

/// e_script_state.
pub const RUN: u8 = 0;
pub const STOP: u8 = 1;
pub const END: u8 = 2;
pub const RERUNLINE: u8 = 3;
pub const GOTO: u8 = 4;
pub const RETFUNC: u8 = 5;
pub const CLOSE: u8 = 6;

/// script_cmd_result.
pub const SCRIPT_CMD_SUCCESS: i32 = 0;
pub const SCRIPT_CMD_FAILURE: i32 = 1;

/// conf/script_athena.conf: check_cmdcount, check_gotocount, input_max_value (input_min_value: default 0).
pub const CHECK_CMDCOUNT: i32 = 655360;
pub const CHECK_GOTOCOUNT: i32 = 2048;
pub const INPUT_MIN_VALUE: i32 = 0;
pub const INPUT_MAX_VALUE: i32 = 10_000_000;
/// SCRIPT_MAX_ARRAYSIZE (UINT_MAX - 1).
pub const SCRIPT_MAX_ARRAYSIZE: u32 = u32::MAX - 1;
/// Not in rAthena: a `freeloop` script is still stopped after this many commands, so one runaway script cannot hold
/// the database's single reducer thread forever (rAthena would spin its map server).
pub const FREELOOP_HARD_LIMIT: i64 = 200_000_000;

/// Which compiled script a state (or a variable reference) belongs to: the `script_code` object.
#[derive(Clone, Debug, PartialEq, Eq, Hash, PartialOrd, Ord, SpacetimeType)]
pub enum CodeKey {
    /// An NPC script, by the id of the NPC that owns the code (duplicates share their source's code).
    Npc(u32),
    /// A `function script` (userfunc_db) by name.
    Func(String),
    /// A script another system compiles from its own text (item scripts, …): kind and key, see code.rs.
    Ext(ExtCode),
}

#[derive(Clone, Debug, PartialEq, Eq, Hash, PartialOrd, Ord, SpacetimeType)]
pub struct ExtCode {
    pub kind: String,
    pub key: String,
}

impl CodeKey {
    /// The owner key of this code's `.` variables (npc_reg rows).
    pub fn owner(&self) -> String {
        match self {
            CodeKey::Npc(id) => format!("n{id}"),
            CodeKey::Func(n) => format!("f{n}"),
            CodeKey::Ext(e) => format!("x{}:{}", e.kind, e.key),
        }
    }
}

/// A variable reference's container (script_data.ref): which reg_db the variable lives in when it is not the default
/// one for its prefix.
#[derive(Clone, Debug, PartialEq, Eq, SpacetimeType)]
pub enum RegRef {
    None,
    /// A scope (`.@`) of this state, by scope id.
    Scope(u32),
    /// A script's local variables (`.`), e.g. getvariableofnpc or a callfunc argument.
    Local(CodeKey),
    /// An instance's variables (`'`).
    Instance(i32),
}

/// A variable's value in a reg_db.
#[derive(Clone, Debug, PartialEq, SpacetimeType)]
pub enum Val {
    Num(i64),
    Str(Vec<u8>),
}

/// struct script_retinfo.
#[derive(Clone, Debug, SpacetimeType)]
pub struct RetInfo {
    /// The caller's scope id (0 once the frame has been restored).
    pub scope: u32,
    pub code: CodeKey,
    pub pos: i32,
    pub nargs: i32,
    pub defsp: i32,
}

/// struct script_data.
#[derive(Clone, Debug)]
pub enum Data {
    /// C_NOP (nil).
    Nop,
    Int(i64),
    /// C_STR / C_CONSTSTR.
    Str(Vec<u8>),
    /// C_POS: a label.
    Pos(i32),
    /// C_NAME: str_data id, array index, container.
    Name(i32, u32, RegRef),
    /// C_ARG.
    Arg,
    RetInfo(RetInfo),
}

impl Data {
    pub fn is_string(&self) -> bool {
        matches!(self, Data::Str(_))
    }
    pub fn is_int(&self) -> bool {
        matches!(self, Data::Int(_))
    }
    pub fn is_reference(&self) -> bool {
        matches!(self, Data::Name(..))
    }
    pub fn is_label(&self) -> bool {
        matches!(self, Data::Pos(_))
    }
    pub fn typ(&self) -> u8 {
        match self {
            Data::Nop => C_NOP,
            Data::Int(_) => C_INT,
            Data::Str(_) => C_STR,
            Data::Pos(_) => C_POS,
            Data::Name(..) => C_NAME,
            Data::Arg => C_ARG,
            Data::RetInfo(_) => C_RETINFO,
        }
    }
}

/// A `.@` scope (or any in-memory reg_db): name (lower case: str_data is case-insensitive) and index → value.
#[derive(Clone, Debug, Default)]
pub struct VarDb {
    pub vars: BTreeMap<(Vec<u8>, u32), Val>,
}

/// struct script_state + its script_stack.
#[derive(Clone, Debug)]
pub struct State {
    /// st->id: the script_state row (0 for a state that never needed one: unit tests).
    pub id: u64,
    pub stack: Vec<Data>,
    pub start: i32,
    pub end: i32,
    pub defsp: i32,
    pub pos: i32,
    pub state: u8,
    pub rid: u32,
    pub oid: u32,
    pub code: CodeKey,
    pub scopes: BTreeMap<u32, VarDb>,
    pub cur_scope: u32,
    pub next_scope: u32,
    pub sleep_tick: i32,
    pub sleep_timer: u64,
    pub sleep_charid: u32,
    pub bk_st: u64,
    pub bk_npcid: u32,
    pub freeloop: bool,
    pub op2ref: bool,
    pub npc_item_flag: bool,
    pub mes_active: bool,
    pub clear_cutin: bool,
    /// st->funcname (runtime).
    pub funcname: String,
    /// The bytecode of `code` (runtime).
    pub buf: Rc<ScriptCode>,
    /// Commands run by this state in total (the freeloop hard limit).
    pub total_cmds: i64,
}

impl State {
    pub fn new(code_key: CodeKey, buf: Rc<ScriptCode>, pos: i32, rid: u32, oid: u32) -> State {
        let mut scopes = BTreeMap::new();
        scopes.insert(1, VarDb::default());
        State {
            id: 0,
            stack: Vec::with_capacity(64),
            start: 0,
            end: 0,
            defsp: 0,
            pos,
            state: RUN,
            rid,
            oid,
            code: code_key,
            scopes,
            cur_scope: 1,
            next_scope: 2,
            sleep_tick: 0,
            sleep_timer: 0,
            sleep_charid: 0,
            bk_st: 0,
            bk_npcid: 0,
            freeloop: false,
            op2ref: false,
            npc_item_flag: false, // battle_config.item_enabled_npc: 0 (renewal)
            mes_active: false,
            clear_cutin: false,
            funcname: String::new(),
            buf,
            total_cmds: 0,
        }
    }

    pub fn sp(&self) -> i32 {
        self.stack.len() as i32
    }

    /// script_getdata(st, i).
    pub fn data(&self, i: i32) -> &Data {
        &self.stack[(self.start + i) as usize]
    }
    pub fn data_mut(&mut self, i: i32) -> &mut Data {
        let k = (self.start + i) as usize;
        &mut self.stack[k]
    }
    /// script_hasdata.
    pub fn hasdata(&self, i: i32) -> bool {
        self.end > self.start + i
    }
    /// script_lastdata.
    pub fn lastdata(&self) -> i32 {
        self.end - self.start - 1
    }

    pub fn push(&mut self, d: Data) {
        self.stack.push(d);
    }
    pub fn pushint(&mut self, v: i64) {
        self.stack.push(Data::Int(v));
    }
    pub fn pushstr(&mut self, s: Vec<u8>) {
        self.stack.push(Data::Str(s));
    }
    pub fn pushnil(&mut self) {
        self.stack.push(Data::Nop);
    }
    /// push_copy (C_RETINFO copies are fatal in rAthena).
    pub fn push_copy(&mut self, pos: i32) {
        let d = self.stack[pos as usize].clone();
        if let Data::RetInfo(_) = d {
            log::error!("script:push_copy: can't create copies of C_RETINFO.");
            self.stack.push(Data::Nop);
            return;
        }
        self.stack.push(d);
    }
    /// script_pushcopy.
    pub fn pushcopy(&mut self, i: i32) {
        self.push_copy(self.start + i);
    }

    /// pop_stack: removes [start, end) and adjusts start / end / defsp.
    pub fn pop_stack(&mut self, start: i32, end: i32) {
        let start = start.max(0);
        let end = end.min(self.sp());
        if start >= end {
            return;
        }
        for d in self.stack.drain(start as usize..end as usize) {
            if let Data::RetInfo(ri) = d {
                if ri.scope != 0 {
                    self.scopes.remove(&ri.scope);
                }
            }
        }
        let n = end - start;
        if self.start > end {
            self.start -= n;
        } else if self.start > start {
            self.start = start;
        }
        if self.end > end {
            self.end -= n;
        } else if self.end > start {
            self.end = start;
        }
        if self.defsp > end {
            self.defsp -= n;
        } else if self.defsp > start {
            self.defsp = start;
        }
    }

    /// script_removetop(st, start, end): relative to the top.
    pub fn removetop(&mut self, start: i32, end: i32) {
        let sp = self.sp();
        self.pop_stack(sp + start, sp + end);
    }

    pub fn new_scope(&mut self) -> u32 {
        let id = self.next_scope;
        self.next_scope += 1;
        self.scopes.insert(id, VarDb::default());
        id
    }
}

// ------------------------------------------------------------------------------------------------ bytecode reading

/// get_com (note rAthena's `i =` rather than `i +=` for multi-byte ops; ops never exceed 0x3f).
pub fn get_com(buf: &[u8], pos: &mut usize) -> u8 {
    let b = |p: usize| buf.get(p).copied().unwrap_or(0);
    if b(*pos) >= 0x80 {
        return C_INT;
    }
    let mut i: i32 = 0;
    let mut j = 0;
    while b(*pos) >= 0x40 {
        i = (b(*pos) as i32) << j;
        *pos += 1;
        j += 6;
    }
    let v = i + ((b(*pos) as i32) << j);
    *pos += 1;
    v as u8
}

/// get_num.
pub fn get_num(buf: &[u8], pos: &mut usize) -> i64 {
    let b = |p: usize| buf.get(p).copied().unwrap_or(0);
    let mut i = 0;
    while b(*pos + i) >= 0xc0 {
        i += 1;
    }
    let end = i;
    let mut value: i64 = 0;
    while i > 0 {
        value |= (b(*pos + i) & 0x3f) as i64;
        value <<= 6;
        i -= 1;
    }
    value |= (b(*pos + i) & 0x3f) as i64;
    *pos += end + 1;
    value
}

fn getvalue(buf: &[u8], i: usize) -> i32 {
    let b = |p: usize| buf.get(p).copied().unwrap_or(0) as i32;
    b(i) | (b(i + 1) << 8) | (b(i + 2) << 16)
}

// ------------------------------------------------------------------------------------------------ the runtime

/// What a running script can reach: the reducer context (None in native unit tests, where only the language core
/// runs: scope variables, strings, arithmetic, callsub / callfunc).
pub struct Rt<'r> {
    pub c: Option<&'r Ctx<'r>>,
}

impl<'r> Rt<'r> {
    pub fn new(c: &'r Ctx<'r>) -> Self {
        Rt { c: Some(c) }
    }
    pub fn none() -> Self {
        Rt { c: None }
    }
}

/// The name of a str_data id.
pub fn name_of(id: i32) -> Vec<u8> {
    code::with_table(|t| t.get_str(id).to_vec())
}
pub fn str_type(id: i32) -> (u8, i64) {
    code::with_table(|t| t.data.get(id as usize).map(|d| (d.typ, d.val)).unwrap_or((C_NOP, 0)))
}
pub fn add_str(name: &[u8]) -> i32 {
    code::with_table(|t| t.add_str(name))
}

/// not_server_variable.
pub fn not_server_variable(prefix: u8) -> bool {
    prefix != b'$' && prefix != b'.' && prefix != b'\''
}
/// is_string_variable.
pub fn is_string_variable(name: &[u8]) -> bool {
    name.last() == Some(&b'$')
}

/// script_reportsrc.
pub fn reportsrc(rt: &Rt, st: &State) {
    if st.oid == 0 {
        return;
    }
    if let Some(c) = rt.c {
        super::npc_run::reportsrc(c, st.oid);
    }
}

/// script_reportdata.
pub fn reportdata(d: &Data) {
    match d {
        Data::Nop => log::debug!("Data: nothing (nil)"),
        Data::Int(v) => log::debug!("Data: number value={v}"),
        Data::Str(s) => log::debug!("Data: string value=\"{}\"", crate::net::wire::from_latin1(s)),
        Data::Name(id, idx, _) => {
            let (t, v) = str_type(*id);
            let n = crate::net::wire::from_latin1(&name_of(*id));
            match t {
                C_INT => log::debug!("Data: constant name='{n}' value={v}"),
                C_PARAM => log::debug!("Data: param name='{n}' type={v}"),
                _ => log::debug!("Data: variable name='{n}' index={idx}"),
            }
        }
        Data::Pos(p) => log::debug!("Data: label pos={p}"),
        d => log::debug!("Data: {}", op2name(d.typ())),
    }
}

/// script_rid2sd: the attached player (its account id); ends the script when there is none.
pub fn rid2sd(rt: &Rt, st: &mut State, func: &str) -> Option<u32> {
    if let Some(c) = rt.c {
        if st.rid != 0 && crate::pc::get(c, st.rid).is_some() {
            return Some(st.rid);
        }
    }
    log::error!("{func}: fatal error ! player not attached!");
    reportsrc(rt, st);
    st.state = END;
    None
}

/// get_val_: dereferences a variable / constant / parameter on the stack in place. `sd` is the player to read player
/// variables from (None: the attached player).
pub fn get_val_at(rt: &Rt, st: &mut State, k: usize, sd: Option<u32>) {
    let Data::Name(id, idx, rf) = st.stack[k].clone() else { return };
    let name = name_of(id);
    let prefix = name.first().copied().unwrap_or(0);
    let postfix = name.last().copied().unwrap_or(0);
    let (typ, val) = str_type(id);
    let mut sd = sd;
    if typ != C_INT && not_server_variable(prefix) && sd.is_none() {
        sd = rid2sd(rt, st, "get_val");
        if sd.is_none() {
            let n = crate::net::wire::from_latin1(&name);
            if postfix == b'$' {
                log::warn!("script:get_val: cannot access player variable '{n}', defaulting to \"\"");
                st.stack[k] = Data::Str(Vec::new());
            } else {
                log::warn!("script:get_val: cannot access player variable '{n}', defaulting to 0");
                st.stack[k] = Data::Int(0);
            }
            return;
        }
    }
    if postfix == b'$' {
        let v = reg::read_str(rt, st, sd, &name, idx, &rf);
        st.stack[k] = Data::Str(v);
    } else if typ == C_INT {
        st.stack[k] = Data::Int(val);
    } else if typ == C_PARAM {
        let v = match (rt.c, sd) {
            (Some(c), Some(aid)) => match super::param::readparam(c, aid, val) {
                Some(v) => v,
                None => {
                    log::error!("pc_readparam: parameter '{}' ({val}) is not available on this server yet", crate::net::wire::from_latin1(&name));
                    reportsrc(rt, st);
                    st.state = END;
                    0
                }
            },
            _ => 0,
        };
        st.stack[k] = Data::Int(v);
    } else {
        let v = reg::read_num(rt, st, sd, &name, idx, &rf);
        st.stack[k] = Data::Int(v);
    }
}

pub fn get_val(rt: &Rt, st: &mut State, k: usize) {
    get_val_at(rt, st, k, None)
}

/// conv_str_: the stack entry as a string (converted in place, like rAthena).
pub fn conv_str_at(rt: &Rt, st: &mut State, k: usize, sd: Option<u32>) -> Vec<u8> {
    get_val_at(rt, st, k, sd);
    match &st.stack[k] {
        Data::Str(s) => s.clone(),
        Data::Int(v) => {
            // snprintf(p, ITEM_NAME_LENGTH (50), "%lld")
            let mut s = v.to_string().into_bytes();
            s.truncate(49);
            st.stack[k] = Data::Str(s.clone());
            s
        }
        Data::Name(id, ..) => {
            let s = name_of(*id);
            st.stack[k] = Data::Str(s.clone());
            s
        }
        d => {
            log::error!("script:conv_str: cannot convert to string, defaulting to \"\"");
            reportdata(d);
            reportsrc(rt, st);
            st.stack[k] = Data::Str(Vec::new());
            Vec::new()
        }
    }
}

/// conv_num_: the stack entry as a number (strings through strtoll base 10, capped).
pub fn conv_num_at(rt: &Rt, st: &mut State, k: usize, sd: Option<u32>) -> i64 {
    get_val_at(rt, st, k, sd);
    match &st.stack[k] {
        Data::Int(v) => *v,
        Data::Str(s) => {
            let (num, _, overflow) = strtoll(s, 0, 10);
            if overflow {
                log::error!("script:conv_num: {} detected, capping value to {num}", if num < 0 { "underflow" } else { "overflow" });
                reportsrc(rt, st);
            }
            st.stack[k] = Data::Int(num);
            num
        }
        Data::Pos(p) => *p as i64,
        // the "unsupported data type" branch is compiled out (#if 0): u.num is whatever the union holds
        Data::Name(id, idx, _) => (*id as i64 & 0xffff_ffff) | ((*idx as i64) << 32),
        _ => 0,
    }
}

/// script_getstr(st, i).
pub fn getstr(rt: &Rt, st: &mut State, i: i32) -> Vec<u8> {
    let k = (st.start + i) as usize;
    conv_str_at(rt, st, k, None)
}
/// script_getnum(st, i) (int32).
pub fn getnum(rt: &Rt, st: &mut State, i: i32) -> i32 {
    let k = (st.start + i) as usize;
    conv_num_at(rt, st, k, None) as i32
}
/// script_getnum64.
pub fn getnum64(rt: &Rt, st: &mut State, i: i32) -> i64 {
    let k = (st.start + i) as usize;
    conv_num_at(rt, st, k, None)
}
/// script_isstring(st, i) (get_val first).
pub fn isstring(rt: &Rt, st: &mut State, i: i32) -> bool {
    let k = (st.start + i) as usize;
    get_val_at(rt, st, k, None);
    st.stack[k].is_string()
}
/// script_isint.
pub fn isint(rt: &Rt, st: &mut State, i: i32) -> bool {
    let k = (st.start + i) as usize;
    get_val_at(rt, st, k, None);
    st.stack[k].is_int()
}

/// get_val2_num: the numeric value of a variable (id, index, container).
pub fn get_val2_num(rt: &Rt, st: &mut State, id: i32, idx: u32, rf: &RegRef) -> i64 {
    st.push(Data::Name(id, idx, rf.clone()));
    let k = st.stack.len() - 1;
    get_val(rt, st, k);
    let v = match &st.stack[k] {
        Data::Int(v) => *v,
        _ => {
            log::error!("get_val2_num: Invalid call. Variable {} is not a numeric type.", crate::net::wire::from_latin1(&name_of(id)));
            0
        }
    };
    st.removetop(-1, 0);
    v
}

/// get_val2_str (the caller removes nothing: the value is returned by copy here).
pub fn get_val2_str(rt: &Rt, st: &mut State, id: i32, idx: u32, rf: &RegRef) -> Vec<u8> {
    st.push(Data::Name(id, idx, rf.clone()));
    let k = st.stack.len() - 1;
    get_val(rt, st, k);
    let v = match &st.stack[k] {
        Data::Str(s) => s.clone(),
        _ => {
            log::error!("get_val2_num: Invalid call. Variable {} is a numeric type.", crate::net::wire::from_latin1(&name_of(id)));
            Vec::new()
        }
    };
    st.removetop(-1, 0);
    v
}

// ------------------------------------------------------------------------------------------------ operators

/// op_3: test ? a : b.
fn op_3(rt: &Rt, st: &mut State) {
    let k = st.stack.len() - 3;
    get_val(rt, st, k);
    let flag = match &st.stack[k] {
        Data::Str(s) => !s.is_empty(),
        Data::Int(v) => *v != 0,
        d => {
            log::error!("script:op_3: invalid data for the ternary operator test");
            reportdata(d);
            reportsrc(rt, st);
            st.removetop(-3, 0);
            st.pushnil();
            return;
        }
    };
    if flag {
        st.push_copy(st.sp() - 2);
    } else {
        st.push_copy(st.sp() - 1);
    }
    st.removetop(-4, -1);
}

/// op_2str.
fn op_2str(rt: &Rt, st: &mut State, op: u8, s1: &[u8], s2: &[u8]) {
    let a = match op {
        C_EQ => s1 == s2,
        C_NE => s1 != s2,
        C_GT => s1 > s2,
        C_GE => s1 >= s2,
        C_LT => s1 < s2,
        C_LE => s1 <= s2,
        C_ADD => {
            let mut buf = s1.to_vec();
            buf.extend_from_slice(s2);
            st.pushstr(buf);
            return;
        }
        _ => {
            log::error!("script:op2_str: unexpected string operator {}", op2name(op));
            reportsrc(rt, st);
            st.pushnil();
            st.state = END;
            return;
        }
    };
    st.pushint(a as i64);
}

/// op_2num.
fn op_2num(rt: &Rt, st: &mut State, op: u8, i1: i64, i2: i64) {
    let ret = match op {
        C_AND => i1 & i2,
        C_OR => i1 | i2,
        C_XOR => i1 ^ i2,
        C_LAND => (i1 != 0 && i2 != 0) as i64,
        C_LOR => (i1 != 0 || i2 != 0) as i64,
        C_EQ => (i1 == i2) as i64,
        C_NE => (i1 != i2) as i64,
        C_GT => (i1 > i2) as i64,
        C_GE => (i1 >= i2) as i64,
        C_LT => (i1 < i2) as i64,
        C_LE => (i1 <= i2) as i64,
        // x86 masks the shift count to 6 bits (int64 shifts)
        C_R_SHIFT => i1.wrapping_shr(i2 as u32),
        C_L_SHIFT => i1.wrapping_shl(i2 as u32),
        C_DIV | C_MOD => {
            if i2 == 0 {
                log::error!("script:op_2num: division by zero detected op={} i1={i1} i2={i2}", op2name(op));
                reportsrc(rt, st);
                st.pushnil();
                st.state = END;
                return;
            }
            if op == C_DIV {
                i1.wrapping_div(i2)
            } else {
                i1.wrapping_rem(i2)
            }
        }
        C_ADD | C_SUB | C_MUL => {
            let r = match op {
                C_ADD => i1.checked_add(i2),
                C_SUB => i1.checked_sub(i2),
                _ => i1.checked_mul(i2),
            };
            match r {
                Some(v) => v,
                None => {
                    log::warn!("script:op_2num: overflow detected op={} i1={i1} i2={i2}", op2name(op));
                    reportsrc(rt, st);
                    st.pushnil();
                    st.state = END;
                    return;
                }
            }
        }
        _ => {
            log::error!("script:op_2num: unexpected number operator {} i1={i1} i2={i2}", op2name(op));
            reportsrc(rt, st);
            st.pushnil();
            return;
        }
    };
    st.pushint(ret);
}

/// op_2.
fn op_2(rt: &Rt, st: &mut State, op: u8) {
    let kl = st.stack.len() - 2;
    let kr = st.stack.len() - 1;
    let mut leftref: Option<Data> = None;
    if st.op2ref {
        if st.stack[kl].is_reference() {
            leftref = Some(st.stack[kl].clone());
        }
        st.op2ref = false;
    }
    get_val(rt, st, kl);
    get_val(rt, st, kr);
    if op == C_ADD {
        if st.stack[kl].is_int() && st.stack[kr].is_string() {
            conv_str_at(rt, st, kl, None);
        } else if st.stack[kl].is_string() && st.stack[kr].is_int() {
            conv_str_at(rt, st, kr, None);
        }
    }
    match (st.stack[kl].clone(), st.stack[kr].clone()) {
        (Data::Str(a), Data::Str(b)) => {
            op_2str(rt, st, op, &a, &b);
            st.removetop(if leftref.is_none() { -3 } else { -2 }, -1);
            if let Some(l) = leftref {
                st.stack[kl] = l;
            }
        }
        (Data::Int(i1), Data::Int(i2)) => {
            st.removetop(if leftref.is_none() { -2 } else { -1 }, 0);
            op_2num(rt, st, op, i1, i2);
            if let Some(l) = leftref {
                st.stack[kl] = l;
            }
        }
        (l, r) => {
            log::error!("script:op_2: invalid data for operator {}", op2name(op));
            reportdata(&l);
            reportdata(&r);
            reportsrc(rt, st);
            st.removetop(-2, 0);
            st.pushnil();
            st.state = END;
        }
    }
}

/// op_1.
fn op_1(rt: &Rt, st: &mut State, op: u8) {
    let k = st.stack.len() - 1;
    get_val(rt, st, k);
    let Data::Int(i1) = st.stack[k] else {
        log::error!("script:op_1: argument is not a number (op={})", op2name(op));
        reportdata(&st.stack[k].clone());
        reportsrc(rt, st);
        st.pushnil();
        st.state = END;
        return;
    };
    st.removetop(-1, 0);
    let r = match op {
        C_NEG => i1.wrapping_neg(),
        C_NOT => !i1,
        C_LNOT => (i1 == 0) as i64,
        _ => {
            log::error!("script:op_1: unexpected operator {} i1={i1}", op2name(op));
            reportsrc(rt, st);
            st.pushnil();
            st.state = END;
            return;
        }
    };
    st.pushint(r);
}

// ------------------------------------------------------------------------------------------------ run_func

/// script_check_buildin_argtype (warn_func_mismatch_argtypes: yes).
fn check_buildin_argtype(rt: &Rt, st: &mut State, func_idx: usize) {
    let spec = super::buildin_table::BUILDIN[func_idx].1.as_bytes();
    let mut invalid = 0;
    let mut idx = 2;
    while st.hasdata(idx) {
        let t = spec.get((idx - 2) as usize).copied().unwrap_or(0);
        if t == b'?' || t == b'*' {
            break;
        }
        let d = st.data(idx).clone();
        if t == 0 {
            log::warn!("Found more arguments than necessary. unexpected arg type {}", op2name(d.typ()));
            invalid += 1;
            break;
        }
        let name = if let Data::Name(id, ..) = &d { Some(name_of(*id)) } else { None };
        let ok = match t {
            b'v' => d.is_string() || d.is_int() || d.is_reference(),
            b's' => d.is_string() || (d.is_reference() && is_string_variable(name.as_deref().unwrap_or(b""))),
            b'i' => {
                d.is_int()
                    || (d.is_reference() && {
                        let (ty, _) = if let Data::Name(id, ..) = &d { str_type(*id) } else { (C_NOP, 0) };
                        ty == C_PARAM || ty == C_INT || !is_string_variable(name.as_deref().unwrap_or(b""))
                    })
            }
            b'r' => d.is_reference(),
            b'l' => d.is_label(),
            _ => true,
        };
        if !ok {
            let what = match t {
                b'v' => "string, number or variable".to_string(),
                b's' => "string".to_string(),
                b'i' => "number".to_string(),
                b'r' => format!("variable, got {}", op2name(d.typ())),
                _ => format!("label, got {}", op2name(d.typ())),
            };
            log::warn!("Unexpected type for argument {}. Expected {what}.", idx - 1);
            reportdata(&d);
            invalid += 1;
        }
        idx += 1;
    }
    if invalid > 0 {
        log::debug!("Function: {}", st.funcname);
        reportsrc(rt, st);
    }
}

/// run_func: executes the buildin whose C_NAME / C_ARG / arguments are on top of the stack.
pub fn run_func(rt: &Rt, st: &mut State) -> i32 {
    let end_sp = st.sp();
    let mut i = end_sp - 1;
    while i > 0 {
        if matches!(st.stack[i as usize], Data::Arg) {
            break;
        }
        i -= 1;
    }
    if i <= 0 {
        log::error!("script:run_func: C_ARG not found. please report this!!!");
        st.state = END;
        reportsrc(rt, st);
        return 1;
    }
    st.start = i - 1;
    st.end = end_sp;
    let func = match st.stack[st.start as usize].clone() {
        Data::Name(id, ..) if str_type(id).0 == C_FUNC => id,
        d => {
            log::error!("script:run_func: not a buildin command.");
            reportdata(&d);
            reportsrc(rt, st);
            st.state = END;
            return 1;
        }
    };
    st.funcname = crate::net::wire::from_latin1(&name_of(func));
    let idx = str_type(func).1 as usize;
    check_buildin_argtype(rt, st, idx);
    let cfunc = super::buildin_table::BUILDIN[idx].2;
    match super::buildins::lookup(idx) {
        Some(f) => {
            // SCRIPT_COMMAND_DEPRECATION (config/core.hpp:81)
            if let Some(date) = super::compiler::buildin_deprecated(idx) {
                log::warn!("Usage of deprecated script function '{}'.", st.funcname);
                log::warn!("This function was deprecated on '{date}' and could become unavailable anytime soon.");
                reportsrc(rt, st);
            }
            if f(rt, st) == SCRIPT_CMD_FAILURE {
                log::warn!("Script command '{}' returned failure.", st.funcname);
                reportsrc(rt, st);
            }
            // the command evaluated a questinfo / achievement condition, which detached this state from its player
            // and attached it again (achievement.rs): the backup script is gone (freed, as rAthena's condition state
            // frees it as a "double continuation")
            if super::state::take_bk_reset(st.id) {
                if st.bk_st != 0 {
                    if let Some(c) = rt.c {
                        super::npc_run::script_free_state_id(c, st.bk_st);
                    }
                }
                st.bk_st = 0;
                st.bk_npcid = 0;
            }
        }
        None => {
            // a command whose system is not ported yet: say so and stop, never pretend it ran
            log::warn!("buildin {} ({cfunc}): not ported yet", st.funcname);
            reportsrc(rt, st);
            st.state = END;
        }
    }
    if st.state == RERUNLINE {
        return 0;
    }
    let (s, e) = (st.start, st.end);
    st.pop_stack(s, e);
    if st.state == RETFUNC {
        // return from a user-defined function
        let olddefsp = st.defsp;
        let (d, s2) = (st.defsp, st.start);
        st.pop_stack(d, s2);
        let ri = match st.defsp >= 1 {
            true => match &st.stack[(st.defsp - 1) as usize] {
                Data::RetInfo(ri) => Some(ri.clone()),
                _ => None,
            },
            false => None,
        };
        let Some(ri) = ri else {
            log::warn!("script:run_func: return without callfunc or callsub!");
            reportsrc(rt, st);
            st.state = END;
            return 1;
        };
        let cur = st.cur_scope;
        st.scopes.remove(&cur);
        let nargs = ri.nargs;
        st.pos = ri.pos;
        set_code(rt, st, ri.code.clone());
        st.cur_scope = ri.scope;
        st.defsp = ri.defsp;
        if let Data::RetInfo(r) = &mut st.stack[(olddefsp - 1) as usize] {
            r.scope = 0; // memset(ri, 0, ...): the restored scope is no longer owned by the frame
        }
        st.pop_stack(olddefsp - nargs - 1, olddefsp);
        st.state = GOTO;
    }
    0
}

/// st->script = code (loads its bytecode).
pub fn set_code(rt: &Rt, st: &mut State, key: CodeKey) {
    match code::get(rt.c, &key) {
        Some(buf) => st.buf = buf,
        None => {
            log::error!("script: code {key:?} is not available");
            st.buf = Rc::new(ScriptCode { buf: vec![C_NOP], labels: Vec::new() });
            st.state = END;
        }
    }
    st.code = key;
}

// ------------------------------------------------------------------------------------------------ run_script_main

/// The commands of one run_script_main pass (the interpreter loop). Returns when the state is no longer RUN.
pub fn execute(rt: &Rt, st: &mut State) {
    let mut cmdcount = CHECK_CMDCOUNT;
    let mut gotocount = CHECK_GOTOCOUNT;
    if st.state == RERUNLINE {
        run_func(rt, st);
        if st.state == GOTO {
            st.state = RUN;
        }
    } else if st.state != END {
        st.state = RUN;
    }
    while st.state == RUN {
        let buf = st.buf.clone();
        let b = &buf.buf;
        let mut pos = st.pos as usize;
        let c = get_com(b, &mut pos);
        st.pos = pos as i32;
        match c {
            C_EOL => {
                if st.defsp > st.sp() {
                    log::error!("script:run_script_main: unexpected stack position (defsp={} sp={}). please report this!!!", st.defsp, st.sp());
                } else {
                    let (d, s) = (st.defsp, st.sp());
                    st.pop_stack(d, s);
                }
            }
            C_INT => {
                let mut pos = st.pos as usize;
                let v = get_num(b, &mut pos);
                st.pos = pos as i32;
                st.pushint(v);
            }
            C_POS => {
                let v = getvalue(b, st.pos as usize);
                st.pos += 3;
                st.push(Data::Pos(v));
            }
            C_NAME => {
                let v = getvalue(b, st.pos as usize);
                st.pos += 3;
                st.push(Data::Name(v, 0, RegRef::None));
            }
            C_ARG => st.push(Data::Arg),
            C_STR => {
                let p = st.pos as usize;
                let n = b[p..].iter().position(|&x| x == 0).unwrap_or(b.len() - p);
                st.pushstr(b[p..p + n].to_vec());
                st.pos = (p + n + 1) as i32;
            }
            C_FUNC => {
                run_func(rt, st);
                if st.state == GOTO {
                    st.state = RUN;
                    if !st.freeloop && gotocount > 0 {
                        gotocount -= 1;
                        if gotocount <= 0 {
                            log::error!("script:run_script_main: infinity loop !");
                            reportsrc(rt, st);
                            st.state = END;
                        }
                    }
                }
            }
            C_REF => st.op2ref = true,
            C_NEG | C_NOT | C_LNOT => op_1(rt, st, c),
            C_ADD | C_SUB | C_MUL | C_DIV | C_MOD | C_EQ | C_NE | C_GT | C_GE | C_LT | C_LE | C_AND | C_OR | C_XOR | C_LAND | C_LOR | C_R_SHIFT | C_L_SHIFT => {
                op_2(rt, st, c)
            }
            C_OP3 => op_3(rt, st),
            C_NOP => st.state = END,
            _ => {
                log::error!("script:run_script_main:unknown command : {c} @ {}", st.pos);
                st.state = END;
            }
        }
        st.total_cmds += 1;
        if !st.freeloop && cmdcount > 0 {
            cmdcount -= 1;
            if cmdcount <= 0 {
                log::error!("script:run_script_main: infinity loop !");
                reportsrc(rt, st);
                st.state = END;
            }
        }
        if st.freeloop && st.total_cmds > FREELOOP_HARD_LIMIT {
            log::error!("script:run_script_main: freeloop script ran {} commands; stopped (Loki safety limit)", st.total_cmds);
            reportsrc(rt, st);
            st.state = END;
        }
    }
}

/// run_script_main: runs a state until it stops, then parks it (sleep / waiting for the player) or disposes of it.
pub fn run_script_main(rt: &Rt, mut st: State) {
    if let Some(c) = rt.c {
        super::npc_run::script_attach_state(c, &mut st);
    }
    super::state::mark_running(st.id, true);
    execute(rt, &mut st);
    super::state::mark_running(st.id, false);
    let Some(c) = rt.c else { return };
    if st.sleep_tick > 0 {
        // restore the previous script, delay execution
        super::npc_run::script_detach_state(c, &mut st, false);
        let charid = crate::pc::get(c, st.rid).map(|p| p.char_id).unwrap_or(0);
        st.sleep_charid = charid;
        // (rAthena passes the char id as the timer's id; here the timer's target stays 0 — the unit-id space of
        // delete_for_target — and run_script_timer reads sleep.charid from the state)
        st.sleep_timer = crate::timer::add(c, c.tick() + st.sleep_tick as i64, crate::timer::Func::ScriptSleep, 0, st.id as i64);
        super::state::save(c, &st);
    } else if st.state != END && st.rid != 0 {
        // resume later (st is already attached to the player)
        if st.bk_st != 0 {
            log::warn!("Unable to restore stack! Double continuation!");
            super::npc_run::script_free_state_id(c, st.bk_st);
            st.bk_st = 0;
        }
        super::state::save(c, &st);
    } else {
        if st.defsp >= 1 && matches!(st.stack.get((st.defsp - 1) as usize), Some(Data::RetInfo(_))) {
            if let Some(Data::RetInfo(ri)) = st.stack.iter().find(|d| matches!(d, Data::RetInfo(_))) {
                st.code = ri.code.clone(); // grab the first, aka the original
            }
        }
        if crate::pc::get(c, st.rid).is_some() {
            super::npc_run::clear_fake_npc(c, st.rid);
            super::npc_run::script_detach_state(c, &mut st, true);
        }
        super::npc_run::script_free_state(c, &st);
    }
}

/// run_script: a new state for `code` at `pos`, run at once.
pub fn run_script(rt: &Rt, key: &CodeKey, pos: i32, rid: u32, oid: u32) {
    if pos < 0 {
        return;
    }
    let Some(buf) = code::get(rt.c, key) else { return };
    let mut st = State::new(key.clone(), buf, pos, rid, oid);
    if let Some(c) = rt.c {
        super::state::alloc(c, &mut st);
    }
    run_script_main(rt, st);
}
