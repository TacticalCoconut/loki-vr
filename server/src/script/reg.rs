//! Script variables (get_val_ / set_reg_num / set_reg_str / script_array_*), by prefix:
//!
//! | prefix | rAthena storage                         | here                                   |
//! |--------|-----------------------------------------|----------------------------------------|
//! | (none) | char_reg_num / char_reg_str (char SQL)  | `char_reg_num` / `char_reg_str`        |
//! | `@`    | sd->regs (temporary)                    | `char_tmp_reg_*` (cleared at logout)   |
//! | `#`    | acc_reg_num / acc_reg_str               | `acc_reg_num` / `acc_reg_str`          |
//! | `##`   | global_acc_reg_num / _str (login SQL)   | `global_acc_reg_num` / `_str`          |
//! | `$`    | mapreg (SQL)                            | `mapreg_num` / `mapreg_str`            |
//! | `$@`   | mapreg, never saved                     | same tables, cleared on world rebuild  |
//! | `.`    | the script_code's local reg_db          | `npc_reg_num` / `npc_reg_str`          |
//! | `.@`   | the stack's scope                       | the state's scopes (saved with it)     |
//! | `'`    | the instance's regs                     | `instance_reg_num` / `_str`            |
//!
//! Names are case-insensitive (str_data), so rows are keyed by the lower-case name. A value of 0 / "" deletes the
//! row, exactly as rAthena drops the entry; an array's members are therefore the indices that have a row, which is
//! what script_array_ensure_zero + script_array_update maintain (getarraysize = highest member + 1).

use spacetimedb::{table, Table};

use super::compiler::C_PARAM;
use super::vm::*;
use crate::world::Ctx;

// One table per reg_db kind and value type (the `table` attribute does not expand inside macro_rules).
#[table(accessor = char_reg_num, index(accessor = by_key, btree(columns = [owner, key, index])))]
#[derive(Clone, Debug)]
pub struct CharRegNum {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub owner: u32,
    /// The variable name, lower case (with its prefix and `$` postfix).
    pub key: String,
    pub index: u32,
    pub value: i64,
}

#[table(accessor = char_reg_str, index(accessor = by_key, btree(columns = [owner, key, index])))]
#[derive(Clone, Debug)]
pub struct CharRegStr {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub owner: u32,
    /// The variable name, lower case (with its prefix and `$` postfix).
    pub key: String,
    pub index: u32,
    pub value: Vec<u8>,
}

#[table(accessor = char_tmp_reg_num, index(accessor = by_key, btree(columns = [owner, key, index])))]
#[derive(Clone, Debug)]
pub struct CharTmpRegNum {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub owner: u32,
    /// The variable name, lower case (with its prefix and `$` postfix).
    pub key: String,
    pub index: u32,
    pub value: i64,
}

#[table(accessor = char_tmp_reg_str, index(accessor = by_key, btree(columns = [owner, key, index])))]
#[derive(Clone, Debug)]
pub struct CharTmpRegStr {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub owner: u32,
    /// The variable name, lower case (with its prefix and `$` postfix).
    pub key: String,
    pub index: u32,
    pub value: Vec<u8>,
}

#[table(accessor = acc_reg_num, index(accessor = by_key, btree(columns = [owner, key, index])))]
#[derive(Clone, Debug)]
pub struct AccRegNum {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub owner: u32,
    /// The variable name, lower case (with its prefix and `$` postfix).
    pub key: String,
    pub index: u32,
    pub value: i64,
}

#[table(accessor = acc_reg_str, index(accessor = by_key, btree(columns = [owner, key, index])))]
#[derive(Clone, Debug)]
pub struct AccRegStr {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub owner: u32,
    /// The variable name, lower case (with its prefix and `$` postfix).
    pub key: String,
    pub index: u32,
    pub value: Vec<u8>,
}

#[table(accessor = global_acc_reg_num, index(accessor = by_key, btree(columns = [owner, key, index])))]
#[derive(Clone, Debug)]
pub struct GlobalAccRegNum {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub owner: u32,
    /// The variable name, lower case (with its prefix and `$` postfix).
    pub key: String,
    pub index: u32,
    pub value: i64,
}

#[table(accessor = global_acc_reg_str, index(accessor = by_key, btree(columns = [owner, key, index])))]
#[derive(Clone, Debug)]
pub struct GlobalAccRegStr {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub owner: u32,
    /// The variable name, lower case (with its prefix and `$` postfix).
    pub key: String,
    pub index: u32,
    pub value: Vec<u8>,
}

#[table(accessor = mapreg_num, index(accessor = by_key, btree(columns = [owner, key, index])))]
#[derive(Clone, Debug)]
pub struct MapregNum {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub owner: u32,
    /// The variable name, lower case (with its prefix and `$` postfix).
    pub key: String,
    pub index: u32,
    pub value: i64,
}

#[table(accessor = mapreg_str, index(accessor = by_key, btree(columns = [owner, key, index])))]
#[derive(Clone, Debug)]
pub struct MapregStr {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub owner: u32,
    /// The variable name, lower case (with its prefix and `$` postfix).
    pub key: String,
    pub index: u32,
    pub value: Vec<u8>,
}

#[table(accessor = npc_reg_num, index(accessor = by_key, btree(columns = [owner, key, index])))]
#[derive(Clone, Debug)]
pub struct NpcRegNum {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub owner: String,
    /// The variable name, lower case (with its prefix and `$` postfix).
    pub key: String,
    pub index: u32,
    pub value: i64,
}

#[table(accessor = npc_reg_str, index(accessor = by_key, btree(columns = [owner, key, index])))]
#[derive(Clone, Debug)]
pub struct NpcRegStr {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub owner: String,
    /// The variable name, lower case (with its prefix and `$` postfix).
    pub key: String,
    pub index: u32,
    pub value: Vec<u8>,
}

#[table(accessor = instance_reg_num, index(accessor = by_key, btree(columns = [owner, key, index])))]
#[derive(Clone, Debug)]
pub struct InstanceRegNum {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub owner: i32,
    /// The variable name, lower case (with its prefix and `$` postfix).
    pub key: String,
    pub index: u32,
    pub value: i64,
}

#[table(accessor = instance_reg_str, index(accessor = by_key, btree(columns = [owner, key, index])))]
#[derive(Clone, Debug)]
pub struct InstanceRegStr {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub owner: i32,
    /// The variable name, lower case (with its prefix and `$` postfix).
    pub key: String,
    pub index: u32,
    pub value: Vec<u8>,
}

/// Where one variable lives.
#[derive(Clone, Debug)]
pub enum Store {
    Char(u32),
    CharTmp(u32),
    Acc(u32),
    GlobalAcc(u32),
    Map,
    Npc(String),
    Instance(i32),
    Scope(u32),
}

/// The lower-case key of a variable name (latin-1 bytes).
pub fn key_of(name: &[u8]) -> String {
    crate::net::wire::from_latin1(name).to_ascii_lowercase()
}

/// The storage of `name` for this state (script_array_src): None when it has none (no player, no instance).
pub fn store_for(rt: &Rt, st: &State, sd: Option<u32>, name: &[u8], rf: &RegRef) -> Option<Store> {
    let p0 = name.first().copied().unwrap_or(0);
    let p1 = name.get(1).copied().unwrap_or(0);
    match p0 {
        b'$' => Some(Store::Map),
        b'.' => Some(match rf {
            RegRef::Scope(id) => Store::Scope(*id),
            RegRef::Local(k) => Store::Npc(k.owner()),
            RegRef::Instance(i) => Store::Instance(*i),
            RegRef::None => {
                if p1 == b'@' {
                    Store::Scope(st.cur_scope)
                } else {
                    Store::Npc(st.code.owner())
                }
            }
        }),
        b'\'' => match rf {
            RegRef::Instance(i) => Some(Store::Instance(*i)),
            RegRef::Local(k) => Some(Store::Npc(k.owner())),
            RegRef::Scope(id) => Some(Store::Scope(*id)),
            RegRef::None => {
                // util::umap_find(instances, script_instancegetid(st)): the NPC's instance, if it exists
                rt.c.and_then(|c| crate::instance::script_regs_instance(c, st.oid)).map(Store::Instance)
            }
        },
        _ => {
            let aid = sd?;
            match p0 {
                b'@' => Some(Store::CharTmp(aid)),
                b'#' => {
                    if p1 == b'#' {
                        Some(Store::GlobalAcc(aid))
                    } else {
                        Some(Store::Acc(aid))
                    }
                }
                _ => {
                    let c = rt.c?;
                    let cid = crate::pc::get(c, aid)?.char_id;
                    Some(Store::Char(cid))
                }
            }
        }
    }
}

macro_rules! num_ops {
    ($c:expr, $tbl:ident, $owner:expr, $key:expr, $idx:expr) => {
        $c.db().$tbl().by_key().filter(($owner, $key, $idx)).next()
    };
}

/// Reads a numeric variable from a store.
pub fn store_get_num(rt: &Rt, st: &State, s: &Store, key: &str, idx: u32) -> i64 {
    if let Store::Scope(id) = s {
        return match st.scopes.get(id).and_then(|v| v.vars.get(&(key.as_bytes().to_vec(), idx))) {
            Some(Val::Num(n)) => *n,
            _ => 0,
        };
    }
    let Some(c) = rt.c else { return 0 };
    let k = &key.to_string();
    match s {
        Store::Char(o) => num_ops!(c, char_reg_num, o, k, &idx).map(|r| r.value),
        Store::CharTmp(o) => num_ops!(c, char_tmp_reg_num, o, k, &idx).map(|r| r.value),
        Store::Acc(o) => num_ops!(c, acc_reg_num, o, k, &idx).map(|r| r.value),
        Store::GlobalAcc(o) => num_ops!(c, global_acc_reg_num, o, k, &idx).map(|r| r.value),
        Store::Map => num_ops!(c, mapreg_num, &0u32, k, &idx).map(|r| r.value),
        Store::Npc(o) => num_ops!(c, npc_reg_num, o, k, &idx).map(|r| r.value),
        Store::Instance(o) => num_ops!(c, instance_reg_num, o, k, &idx).map(|r| r.value),
        Store::Scope(_) => None,
    }
    .unwrap_or(0)
}

/// Reads a string variable from a store ("" when unset).
pub fn store_get_str(rt: &Rt, st: &State, s: &Store, key: &str, idx: u32) -> Vec<u8> {
    if let Store::Scope(id) = s {
        return match st.scopes.get(id).and_then(|v| v.vars.get(&(key.as_bytes().to_vec(), idx))) {
            Some(Val::Str(v)) => v.clone(),
            _ => Vec::new(),
        };
    }
    let Some(c) = rt.c else { return Vec::new() };
    let k = &key.to_string();
    match s {
        Store::Char(o) => num_ops!(c, char_reg_str, o, k, &idx).map(|r| r.value),
        Store::CharTmp(o) => num_ops!(c, char_tmp_reg_str, o, k, &idx).map(|r| r.value),
        Store::Acc(o) => num_ops!(c, acc_reg_str, o, k, &idx).map(|r| r.value),
        Store::GlobalAcc(o) => num_ops!(c, global_acc_reg_str, o, k, &idx).map(|r| r.value),
        Store::Map => num_ops!(c, mapreg_str, &0u32, k, &idx).map(|r| r.value),
        Store::Npc(o) => num_ops!(c, npc_reg_str, o, k, &idx).map(|r| r.value),
        Store::Instance(o) => num_ops!(c, instance_reg_str, o, k, &idx).map(|r| r.value),
        Store::Scope(_) => None,
    }
    .unwrap_or_default()
}

macro_rules! put_row {
    ($c:expr, $tbl:ident, $T:ident, $owner:expr, $key:expr, $idx:expr, $val:expr, $empty:expr) => {{
        let existing = $c.db().$tbl().by_key().filter(($owner, $key, $idx)).next();
        match (existing, $empty) {
            (Some(r), true) => {
                $c.db().$tbl().id().delete(r.id);
            }
            (Some(r), false) => {
                $c.db().$tbl().id().update($T { value: $val, ..r });
            }
            (None, false) => {
                $c.db().$tbl().insert($T { id: 0, owner: $owner.clone(), key: $key.clone(), index: *$idx, value: $val });
            }
            (None, true) => {}
        }
    }};
}

/// Writes a numeric variable (0 deletes it).
pub fn store_set_num(rt: &Rt, st: &mut State, s: &Store, key: &str, idx: u32, v: i64) {
    if let Store::Scope(id) = s {
        if let Some(db) = st.scopes.get_mut(id) {
            if v != 0 {
                db.vars.insert((key.as_bytes().to_vec(), idx), Val::Num(v));
            } else {
                db.vars.remove(&(key.as_bytes().to_vec(), idx));
            }
        }
        return;
    }
    let Some(c) = rt.c else { return };
    let k = &key.to_string();
    let e = v == 0;
    match s {
        Store::Char(o) => put_row!(c, char_reg_num, CharRegNum, o, k, &idx, v, e),
        Store::CharTmp(o) => put_row!(c, char_tmp_reg_num, CharTmpRegNum, o, k, &idx, v, e),
        Store::Acc(o) => put_row!(c, acc_reg_num, AccRegNum, o, k, &idx, v, e),
        Store::GlobalAcc(o) => put_row!(c, global_acc_reg_num, GlobalAccRegNum, o, k, &idx, v, e),
        Store::Map => put_row!(c, mapreg_num, MapregNum, &0u32, k, &idx, v, e),
        Store::Npc(o) => put_row!(c, npc_reg_num, NpcRegNum, o, k, &idx, v, e),
        Store::Instance(o) => put_row!(c, instance_reg_num, InstanceRegNum, o, k, &idx, v, e),
        Store::Scope(_) => {}
    }
}

/// Writes a string variable ("" deletes it).
pub fn store_set_str(rt: &Rt, st: &mut State, s: &Store, key: &str, idx: u32, v: &[u8]) {
    if let Store::Scope(id) = s {
        if let Some(db) = st.scopes.get_mut(id) {
            if !v.is_empty() {
                db.vars.insert((key.as_bytes().to_vec(), idx), Val::Str(v.to_vec()));
            } else {
                db.vars.remove(&(key.as_bytes().to_vec(), idx));
            }
        }
        return;
    }
    let Some(c) = rt.c else { return };
    let k = &key.to_string();
    let e = v.is_empty();
    let v = v.to_vec();
    match s {
        Store::Char(o) => put_row!(c, char_reg_str, CharRegStr, o, k, &idx, v, e),
        Store::CharTmp(o) => put_row!(c, char_tmp_reg_str, CharTmpRegStr, o, k, &idx, v, e),
        Store::Acc(o) => put_row!(c, acc_reg_str, AccRegStr, o, k, &idx, v, e),
        Store::GlobalAcc(o) => put_row!(c, global_acc_reg_str, GlobalAccRegStr, o, k, &idx, v, e),
        Store::Map => put_row!(c, mapreg_str, MapregStr, &0u32, k, &idx, v, e),
        Store::Npc(o) => put_row!(c, npc_reg_str, NpcRegStr, o, k, &idx, v, e),
        Store::Instance(o) => put_row!(c, instance_reg_str, InstanceRegStr, o, k, &idx, v, e),
        Store::Scope(_) => {}
    }
}

/// The indices of `key` that hold a value (the array's members, sorted).
pub fn store_indices(rt: &Rt, st: &State, s: &Store, key: &str, is_str: bool) -> Vec<u32> {
    if let Store::Scope(id) = s {
        let Some(db) = st.scopes.get(id) else { return Vec::new() };
        let k = key.as_bytes().to_vec();
        return db.vars.range((k.clone(), 0)..=(k, u32::MAX)).map(|((_, i), _)| *i).collect();
    }
    let Some(c) = rt.c else { return Vec::new() };
    let k = &key.to_string();
    macro_rules! idx {
        ($tbl:ident, $o:expr) => {
            c.db().$tbl().by_key().filter(($o, k)).map(|r| r.index).collect::<Vec<u32>>()
        };
    }
    let mut v = match (s, is_str) {
        (Store::Char(o), false) => idx!(char_reg_num, o),
        (Store::Char(o), true) => idx!(char_reg_str, o),
        (Store::CharTmp(o), false) => idx!(char_tmp_reg_num, o),
        (Store::CharTmp(o), true) => idx!(char_tmp_reg_str, o),
        (Store::Acc(o), false) => idx!(acc_reg_num, o),
        (Store::Acc(o), true) => idx!(acc_reg_str, o),
        (Store::GlobalAcc(o), false) => idx!(global_acc_reg_num, o),
        (Store::GlobalAcc(o), true) => idx!(global_acc_reg_str, o),
        (Store::Map, false) => idx!(mapreg_num, &0u32),
        (Store::Map, true) => idx!(mapreg_str, &0u32),
        (Store::Npc(o), false) => idx!(npc_reg_num, o),
        (Store::Npc(o), true) => idx!(npc_reg_str, o),
        (Store::Instance(o), false) => idx!(instance_reg_num, o),
        (Store::Instance(o), true) => idx!(instance_reg_str, o),
        (Store::Scope(_), _) => Vec::new(),
    };
    v.sort_unstable();
    v
}

// ------------------------------------------------------------------------------------------------ get_val / set_reg

/// The string branch of get_val_ (the caller has checked that a player is attached when one is needed).
pub fn read_str(rt: &Rt, st: &mut State, sd: Option<u32>, name: &[u8], idx: u32, rf: &RegRef) -> Vec<u8> {
    match store_for(rt, st, sd, name, rf) {
        Some(s) => store_get_str(rt, st, &s, &key_of(name), idx),
        None => {
            if name.first() == Some(&b'\'') {
                log::warn!("script:get_val: cannot access instance variable '{}', defaulting to \"\"", crate::net::wire::from_latin1(name));
            }
            Vec::new()
        }
    }
}

/// The numeric branch of get_val_ for variables (constants and parameters are handled by the caller).
pub fn read_num(rt: &Rt, st: &mut State, sd: Option<u32>, name: &[u8], idx: u32, rf: &RegRef) -> i64 {
    match store_for(rt, st, sd, name, rf) {
        Some(s) => store_get_num(rt, st, &s, &key_of(name), idx),
        None => {
            if name.first() == Some(&b'\'') {
                log::warn!("script:get_val: cannot access instance variable '{}', defaulting to 0", crate::net::wire::from_latin1(name));
            }
            0
        }
    }
}

/// script_check_RegistryVariableLength(0, name): a key must be shorter than 33.
fn name_ok(sd: Option<u32>, name: &[u8]) -> bool {
    if name.len() >= 33 {
        log::error!("set_reg: Variable name length is too long (aid: {}): '{}' sz={}", sd.map(|a| a as i64).unwrap_or(-1), crate::net::wire::from_latin1(name), name.len());
        return false;
    }
    true
}

/// set_reg_str.
pub fn set_reg_str(rt: &Rt, st: &mut State, sd: Option<u32>, id: i32, idx: u32, name: &[u8], value: &[u8], rf: &RegRef) -> bool {
    if !name_ok(sd, name) {
        return false;
    }
    if !is_string_variable(name) {
        return false;
    }
    let p0 = name.first().copied().unwrap_or(0);
    // pc_setregistry_str: permanent string registries refuse values of 255 bytes or more
    if not_server_variable(p0) && p0 != b'@' && value.len() >= 255 {
        log::error!("pc_check_RegistryVariableLength: Variable value length is too long (aid: {}): '{}' sz={}", sd.unwrap_or(0), crate::net::wire::from_latin1(value), value.len());
        return false;
    }
    let _ = id;
    match store_for(rt, st, sd, name, rf) {
        Some(s) => {
            store_set_str(rt, st, &s, &key_of(name), idx, value);
            true
        }
        None => {
            if p0 == b'\'' {
                log::error!("script_set_reg: cannot write instance variable '{}', NPC not in a instance!", crate::net::wire::from_latin1(name));
                reportsrc(rt, st);
                return true;
            }
            false
        }
    }
}

/// set_reg_num (parameters go through pc_setparam).
pub fn set_reg_num(rt: &Rt, st: &mut State, sd: Option<u32>, id: i32, idx: u32, name: &[u8], value: i64, rf: &RegRef) -> bool {
    if !name_ok(sd, name) {
        return false;
    }
    if is_string_variable(name) {
        return false;
    }
    let (typ, ptype) = str_type(id);
    if typ == C_PARAM {
        let ok = match (rt.c, sd) {
            (Some(c), Some(aid)) => super::param::setparam(c, aid, ptype, value),
            _ => false,
        };
        if !ok {
            log::error!("script_set_reg: failed to set param '{}' to {value}.", crate::net::wire::from_latin1(name));
            reportsrc(rt, st);
            st.state = END;
            return false;
        }
        return true;
    }
    let p0 = name.first().copied().unwrap_or(0);
    match store_for(rt, st, sd, name, rf) {
        Some(s) => {
            store_set_num(rt, st, &s, &key_of(name), idx, value);
            true
        }
        None => {
            if p0 == b'\'' {
                log::error!("script_set_reg: cannot write instance variable '{}', NPC not in a instance!", crate::net::wire::from_latin1(name));
                reportsrc(rt, st);
                return true;
            }
            false
        }
    }
}

/// clear_reg.
pub fn clear_reg(rt: &Rt, st: &mut State, sd: Option<u32>, id: i32, idx: u32, name: &[u8], rf: &RegRef) -> bool {
    if is_string_variable(name) {
        set_reg_str(rt, st, sd, id, idx, name, b"", rf)
    } else {
        set_reg_num(rt, st, sd, id, idx, name, 0, rf)
    }
}

/// script_array_highest_key (after script_array_ensure_zero): highest member + 1, or 0.
pub fn highest_key(rt: &Rt, st: &mut State, sd: Option<u32>, name: &[u8], rf: &RegRef) -> u32 {
    match store_for(rt, st, sd, name, rf) {
        Some(s) => store_indices(rt, st, &s, &key_of(name), is_string_variable(name)).last().map(|&m| m + 1).unwrap_or(0),
        None => 0,
    }
}

/// The members of an array (script_array_cpy_list), sorted.
pub fn members(rt: &Rt, st: &mut State, sd: Option<u32>, name: &[u8], rf: &RegRef) -> Option<Vec<u32>> {
    let s = store_for(rt, st, sd, name, rf)?;
    Some(store_indices(rt, st, &s, &key_of(name), is_string_variable(name)))
}

/// set_var_str (clif_parse_WisMessage's @whispervarN$ and friends): a player variable by name.
pub fn set_var_str(c: &Ctx, aid: u32, name: &str, value: &[u8]) {
    let rt = Rt::new(c);
    let n = crate::net::wire::latin1(name);
    let id = add_str(&n);
    let mut st = State::new(CodeKey::Func(String::new()), std::rc::Rc::new(Default::default()), 0, aid, 0);
    set_reg_str(&rt, &mut st, Some(aid), id, 0, &n, value, &RegRef::None);
}

/// pc_setreg (an `@` number) by name, for the engine's own uses (@menu).
pub fn pc_setreg(c: &Ctx, aid: u32, name: &str, v: i64) {
    let rt = Rt::new(c);
    let st = State::new(CodeKey::Func(String::new()), std::rc::Rc::new(Default::default()), 0, aid, 0);
    let mut st = st;
    store_set_num(&rt, &mut st, &Store::CharTmp(aid), &name.to_ascii_lowercase(), 0, v);
}

/// map_quit / unit_free: a player's `@` variables die with the session.
pub fn clear_tmp_regs(c: &Ctx, aid: u32) {
    let n: Vec<u64> = c.db().char_tmp_reg_num().by_key().filter(&aid).map(|r| r.id).collect();
    for id in n {
        c.db().char_tmp_reg_num().id().delete(id);
    }
    let s: Vec<u64> = c.db().char_tmp_reg_str().by_key().filter(&aid).map(|r| r.id).collect();
    for id in s {
        c.db().char_tmp_reg_str().id().delete(id);
    }
}

/// instance.cpp instance_destroy: `db_destroy(idata->regs.vars)` — the instance's `'` variables go with it.
pub fn clear_instance_regs(c: &Ctx, instance_id: i32) {
    let ids: Vec<u64> = c.db().instance_reg_num().by_key().filter(&instance_id).map(|r| r.id).collect();
    for id in ids {
        c.db().instance_reg_num().id().delete(id);
    }
    let ids: Vec<u64> = c.db().instance_reg_str().by_key().filter(&instance_id).map(|r| r.id).collect();
    for id in ids {
        c.db().instance_reg_str().id().delete(id);
    }
}

/// World rebuild (rAthena restart / @reloadscript): NPC variables (`.`) and temporary map variables (`$@`) are gone;
/// permanent map variables (`$`) stay.
pub fn clear_world_regs(c: &Ctx) {
    let ids: Vec<u64> = c.db().npc_reg_num().iter().map(|r| r.id).collect();
    for id in ids {
        c.db().npc_reg_num().id().delete(id);
    }
    let ids: Vec<u64> = c.db().npc_reg_str().iter().map(|r| r.id).collect();
    for id in ids {
        c.db().npc_reg_str().id().delete(id);
    }
    // instances (and so their `'` variables) do not outlive the map-server
    let ids: Vec<u64> = c.db().instance_reg_num().iter().map(|r| r.id).collect();
    for id in ids {
        c.db().instance_reg_num().id().delete(id);
    }
    let ids: Vec<u64> = c.db().instance_reg_str().iter().map(|r| r.id).collect();
    for id in ids {
        c.db().instance_reg_str().id().delete(id);
    }
    let ids: Vec<u64> = c.db().mapreg_num().iter().filter(|r| r.key.starts_with("$@")).map(|r| r.id).collect();
    for id in ids {
        c.db().mapreg_num().id().delete(id);
    }
    let ids: Vec<u64> = c.db().mapreg_str().iter().filter(|r| r.key.starts_with("$@")).map(|r| r.id).collect();
    for id in ids {
        c.db().mapreg_str().id().delete(id);
    }
}
