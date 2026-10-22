//! Compiled scripts, per module instance. rAthena compiles every script at startup and keeps the bytecode in memory;
//! here the bytecode is rebuilt on demand in each module instance (it is never stored), from the same source text
//! and in the same conditions (the `str_data` table, and userfunc_db as it stood when the script was loaded — kept
//! in the `userfunc_def` table by the world build), so it is byte-for-byte what rAthena produced and saved
//! positions stay valid.
//!
//! Other systems compile their own scripts (item scripts, …) through [`register_provider`]: a provider turns an
//! `ExtCode { kind, key }` into source text, so a suspended item script can be recompiled after a restart.

use std::cell::RefCell;
use std::collections::{HashMap, HashSet};
use std::rc::Rc;

use spacetimedb::{table, Table};

use super::compiler::{self, ScriptCode, StrTable, SCRIPT_RETURN_EMPTY_SCRIPT, SCRIPT_USE_LABEL_DB};
use super::vm::{CodeKey, ExtCode};
use crate::npc::{npc, npc_function};
use crate::world::Ctx;

/// Every `function script` definition in load order (npc_parse_function; a later definition overwrites an earlier
/// one in userfunc_db, but the name exists from its first definition on).
#[table(accessor = userfunc_def)]
#[derive(Clone, Debug)]
pub struct UserfuncDef {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub name: String,
    pub script_file: u32,
    pub script_start: u32,
}

/// Source text for a script compiled by another system.
pub struct ExtSource {
    pub text: Vec<u8>,
    pub file: String,
    pub line: u32,
    /// compiler options (item scripts: SCRIPT_IGNORE_EXTERNAL_BRACKETS).
    pub options: u32,
    /// Function names a direct call may use (item scripts load before NPCs: none).
    pub userfuncs: Vec<String>,
}

pub type Provider = fn(Option<&Ctx>, &str) -> Option<ExtSource>;

thread_local! {
    static TABLE: RefCell<StrTable> = RefCell::new(StrTable::new());
    static CACHE: RefCell<HashMap<CodeKey, Option<Rc<ScriptCode>>>> = RefCell::new(HashMap::new());
    static SPANS: RefCell<HashMap<(u32, u32), Option<Rc<ScriptCode>>>> = RefCell::new(HashMap::new());
    static FILES: RefCell<Option<Rc<Vec<crate::data::NpcFile>>>> = const { RefCell::new(None) };
    static PROVIDERS: RefCell<HashMap<String, Provider>> = RefCell::new(default_providers());
    static DEFS: RefCell<Option<(u32, Rc<Vec<(u32, u32, String)>>)>> = const { RefCell::new(None) };
    /// Set while the world build loads the scripts: only then are compile errors reported (rAthena reports them
    /// once, at load; a later lazy compile of the same text in another module instance stays quiet).
    static LOADING: std::cell::Cell<bool> = const { std::cell::Cell::new(false) };
}

/// The providers every module instance has from the start (a suspended script of theirs can be resumed at any time):
/// the item scripts (inventory.rs, item_db's Script / EquipScript / UnEquipScript and combo scripts).
fn default_providers() -> HashMap<String, Provider> {
    let mut m: HashMap<String, Provider> = HashMap::new();
    m.insert("item".to_string(), crate::inventory::item_script_source as Provider);
    m
}

/// Marks the world build's script loading (compile errors are reported while it is on).
pub fn set_loading(on: bool) {
    LOADING.with(|l| l.set(on));
}

fn parse(src: &[u8], file: &str, line: u32, options: u32, is_fn: &dyn Fn(&[u8]) -> bool) -> Option<Rc<ScriptCode>> {
    let quiet = !LOADING.with(|l| l.get());
    compiler::QUIET.with(|q| q.set(quiet));
    let r = with_table(|t| compiler::parse_script(t, src, file, line, options, is_fn)).ok().flatten().map(Rc::new);
    compiler::QUIET.with(|q| q.set(false));
    r
}

/// Runs `f` with the str_data table.
pub fn with_table<R>(f: impl FnOnce(&mut StrTable) -> R) -> R {
    TABLE.with(|t| f(&mut t.borrow_mut()))
}

/// Lets another system compile its scripts: `kind` names the provider in `CodeKey::Ext`.
pub fn register_provider(kind: &str, f: Provider) {
    PROVIDERS.with(|p| p.borrow_mut().insert(kind.to_string(), f));
}

/// The NPC source files (inflated once per instance).
pub fn files() -> Rc<Vec<crate::data::NpcFile>> {
    FILES.with(|f| {
        let mut f = f.borrow_mut();
        if f.is_none() {
            *f = Some(Rc::new(crate::data::npc_files()));
        }
        f.as_ref().unwrap().clone()
    })
}

/// Forgets every compiled script (world rebuild).
pub fn reset() {
    CACHE.with(|c| c.borrow_mut().clear());
    SPANS.with(|c| c.borrow_mut().clear());
    DEFS.with(|d| *d.borrow_mut() = None);
}

/// The generation of `userfunc_def`: bumped whenever it changes (the world build, a run-time load's functions,
/// script_reload), so every module instance knows when its cached copy ([`defs`]) is stale — a run-time load happens
/// in one instance while others keep their caches.
fn defs_gen(c: &Ctx) -> u32 {
    use crate::ids::id_counter;
    c.db().id_counter().kind().find(&DEFS_GEN.to_string()).map(|r| r.next).unwrap_or(0)
}

const DEFS_GEN: &str = "userfunc_def_gen";

fn bump_defs_gen(c: &Ctx) {
    crate::ids::next(c.rc, DEFS_GEN, 1);
    DEFS.with(|d| *d.borrow_mut() = None);
}

fn defs(c: &Ctx) -> Rc<Vec<(u32, u32, String)>> {
    let gen = defs_gen(c);
    DEFS.with(|d| {
        let mut d = d.borrow_mut();
        if d.as_ref().map(|(g, _)| *g != gen).unwrap_or(true) {
            let mut v: Vec<(u32, u32, String)> = c.db().userfunc_def().iter().map(|r| (r.script_file, r.script_start, r.name)).collect();
            v.sort();
            *d = Some((gen, Rc::new(v)));
        }
        d.as_ref().unwrap().1.clone()
    })
}

/// The embedded file a script's file index names: the index itself for an embedded file, the file a run-time load
/// parsed for a run-time load's own index (npc_load.rs: every run-time load has an index of its own past the
/// embedded files, so userfunc_db "as it stood when the script was loaded" — the functions defined at lower (file,
/// position) — holds for them too, and a compiled span is never reused for another load).
fn real_file(c: Option<&Ctx>, file: u32) -> Option<u32> {
    let n = files().len() as u32;
    if file < n {
        return Some(file);
    }
    crate::npc_load::vfile_source(c?, file)
}

/// userfunc_db at the moment the script at (file, start) was loaded: the functions defined before it.
fn userfuncs_before(c: &Ctx, file: u32, start: u32) -> HashSet<Vec<u8>> {
    defs(c).iter().filter(|(f, s, _)| (*f, *s) < (file, start)).map(|(_, _, n)| crate::net::wire::latin1(n)).collect()
}

/// Compiles the script body that starts at `start` in NPC file `file`: npc_parse_script (options
/// SCRIPT_USE_LABEL_DB) or npc_parse_function (SCRIPT_RETURN_EMPTY_SCRIPT).
pub fn compile_span(c: &Ctx, file: u32, start: u32, line: u32, options: u32) -> Option<Rc<ScriptCode>> {
    if let Some(hit) = SPANS.with(|s| s.borrow().get(&(file, start)).cloned()) {
        return hit;
    }
    let names = userfuncs_before(c, file, start);
    let r = compile_with(Some(c), file, start, line, options, &names);
    SPANS.with(|s| s.borrow_mut().insert((file, start), r.clone()));
    r
}

fn compile_with(c: Option<&Ctx>, file: u32, start: u32, line: u32, options: u32, names: &HashSet<Vec<u8>>) -> Option<Rc<ScriptCode>> {
    let files = files();
    let f = files.get(real_file(c, file)? as usize)?;
    let is_fn = compiler::userfunc_set(names);
    let src = &f.text[(start as usize).min(f.text.len())..];
    parse(src, &f.path, line, options, &is_fn)
}

/// npc_parse_function's parse_script + userfunc_db put (the world build, or a run-time load): compiles the function
/// with the functions registered so far and, when it compiles, records the definition (a function that fails to
/// compile is not registered, and an earlier definition of the name stays). Returns whether it was registered.
pub fn define_function(c: &Ctx, name: &str, file: u32, start: u32, line: u32) -> bool {
    let names: HashSet<Vec<u8>> = c.db().userfunc_def().iter().map(|r| crate::net::wire::latin1(&r.name)).collect();
    let r = compile_with(Some(c), file, start, line, SCRIPT_RETURN_EMPTY_SCRIPT, &names);
    SPANS.with(|s| s.borrow_mut().insert((file, start), r.clone()));
    if r.is_none() {
        return false;
    }
    c.db().userfunc_def().insert(UserfuncDef { id: 0, name: name.to_string(), script_file: file, script_start: start });
    bump_defs_gen(c);
    CACHE.with(|m| m.borrow_mut().remove(&CodeKey::Func(name.to_string())));
    true
}

/// The world build (and script_reload's userfunc_db->clear) starts over: no function is defined.
pub fn clear_functions(c: &Ctx) {
    for id in c.db().userfunc_def().iter().map(|r| r.id).collect::<Vec<_>>() {
        c.db().userfunc_def().id().delete(id);
    }
    reset();
    bump_defs_gen(c);
}

/// The code of an NPC (its own or its source's): (key, bytecode). None for NPCs without a script.
pub fn npc_code(c: &Ctx, npc_id: u32) -> Option<(CodeKey, Rc<ScriptCode>)> {
    let nd = c.db().npc().id().find(npc_id)?;
    if nd.subtype != crate::npc::NPCTYPE_SCRIPT || nd.script_end == 0 {
        return None;
    }
    let root = if nd.src_id != 0 { nd.src_id } else { nd.id };
    let key = CodeKey::Npc(root);
    let code = get(Some(c), &key)?;
    Some((key, code))
}

/// The bytecode for a code key (compiled on first use in this instance). None when the script does not exist or
/// failed to compile.
pub fn get(c: Option<&Ctx>, key: &CodeKey) -> Option<Rc<ScriptCode>> {
    // a user function is looked up by name each time (its definition can change at run time — a run-time load
    // overwrites it, in whichever module instance ran the load); its code is cached by (file, position), which never
    // changes meaning
    if let (Some(c), CodeKey::Func(name)) = (c, key) {
        let f = c.db().npc_function().name().find(name)?;
        return compile_span(c, f.script_file, f.script_start, f.script_line, SCRIPT_RETURN_EMPTY_SCRIPT);
    }
    if let Some(hit) = CACHE.with(|m| m.borrow().get(key).cloned()) {
        return hit;
    }
    let c_opt = c;
    let r = match key {
        CodeKey::Npc(id) => {
            let c = c_opt?;
            let nd = c.db().npc().id().find(*id)?;
            if nd.script_end == 0 {
                return None;
            }
            compile_span(c, nd.script_file, nd.script_start, nd.script_line, SCRIPT_USE_LABEL_DB)
        }
        CodeKey::Func(name) => {
            let c = c_opt?;
            let f = c.db().npc_function().name().find(name)?;
            compile_span(c, f.script_file, f.script_start, f.script_line, SCRIPT_RETURN_EMPTY_SCRIPT)
        }
        CodeKey::Ext(ExtCode { kind, key: k }) => {
            let p = PROVIDERS.with(|p| p.borrow().get(kind).copied())?;
            let src = p(c_opt, k)?;
            let names: HashSet<Vec<u8>> = src.userfuncs.iter().map(|n| crate::net::wire::latin1(n)).collect();
            let is_fn = compiler::userfunc_set(&names);
            parse(&src.text, &src.file, src.line, src.options, &is_fn)
        }
    };
    CACHE.with(|m| m.borrow_mut().insert(key.clone(), r.clone()));
    r
}

/// Puts compiled code in the cache directly (native unit tests, which have no database).
pub fn insert(key: CodeKey, code: ScriptCode) {
    CACHE.with(|m| m.borrow_mut().insert(key, Some(Rc::new(code))));
}

/// userfunc_db: does a function of this name exist (is_function, callfunc)?
pub fn userfunc_exists(c: Option<&Ctx>, name: &str) -> bool {
    match c {
        Some(c) => c.db().npc_function().name().find(&name.to_string()).is_some(),
        None => CACHE.with(|m| m.borrow().get(&CodeKey::Func(name.to_string())).map(|v| v.is_some()).unwrap_or(false)),
    }
}
