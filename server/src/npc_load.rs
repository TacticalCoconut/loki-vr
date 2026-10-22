//! npc.cpp's NPC source files on a running server, and map.cpp map_reloadnpc: npc_src_files (npc_addsrcfile,
//! npc_delsrcfile), npc_parsesrcfile at run time, npc_unloadfile (npc_unload_duplicates, npc_remove_mob_spawns),
//! npc_event_doall_path, npc_reload with script.cpp script_reload (and mapreg.cpp mapreg_reload), and the barter
//! database's reload (BarterDatabase::clear / ~s_npc_barter / loadingFinished). The @commands are atcommand_world.rs's
//! (@loadnpc, @unloadnpc, @reloadnpcfile, @unloadnpcfile) and atcommand_admin.rs's (@reloadscript, `@reload script`,
//! @reloadbarterdb); npc_read_event_script is script/npc_run.rs's. The parsing itself is npc.rs's Builder, the one
//! the start-up uses (Mode::Live / Mode::Reload).
//!
//! **No disk.** rAthena reads the files from its working directory; the module embeds every file rAthena ships under
//! npc/ (data/npcs.bin: the start-up set first, then all the others — tools/datagen.py build_npcs), and
//! check_filepath looks a path up among them ([`check_filepath`]): a path outside that set answers as a missing file
//! does in rAthena. The conf files (npc/re/scripts_main.conf and its imports) are embedded too, but map_reloadnpc does
//! not parse them again: they are the bytes datagen read, so the start-up set it resolved from them is the list
//! map_reloadnpc_sub would make.
//!
//! **Script code of a run-time load.** The script compiler (script/code.rs) compiles an NPC's code from its file's
//! text with userfunc_db as it stood when the NPC was loaded, which it derives from the order of (file index,
//! position). Every run-time load gets a file index of its own past the embedded files ([`NpcSrcVfile`]), so what it
//! defines comes after everything loaded before it, and nothing compiled for one load is reused for another.

use std::cell::RefCell;
use std::collections::HashMap;
use std::rc::Rc;

use spacetimedb::{table, Table};

use crate::mob::{mob_data as _, mob_map as _};
use crate::npc::{npc as _, npc_function as _, npc_shop_family as _, spawn_data as _, Builder, Mode, NPCTYPE_BARTER};
use crate::script::state::script_state as _;
use crate::script::code;
use crate::script::npc_run::{self, npc_event as _};
use crate::script::vm::CodeKey;
use crate::world::Ctx;

// ---------------------------------------------------------------------------------------------- tables

/// npc.cpp npc_src_files: the NPC files the server loads, by the name they were given (an exact, case-sensitive
/// string, util::vector_exists). Only membership matters here: the list's order is read by npc_loadsrcfiles, which
/// runs at start-up and in npc_reload right after map_reloadnpc made the list again (the start-up set, in its order).
#[table(accessor = npc_src_file)]
#[derive(Clone, Debug)]
pub struct NpcSrcFile {
    #[primary_key]
    pub path: String,
}

/// A run-time load's own script file index (see the module notes): `vfile` stands for the embedded file `file`.
#[table(accessor = npc_src_vfile)]
#[derive(Clone, Debug)]
pub struct NpcSrcVfile {
    #[primary_key]
    pub vfile: u32,
    pub file: u32,
}

/// s_npc_barter::npcid: the NPC loadingFinished made for a barter (its destructor unloads it when it still exists).
#[table(accessor = barter_npc)]
#[derive(Clone, Debug)]
pub struct BarterNpc {
    #[primary_key]
    pub name: String,
    pub npc_id: u32,
}

/// The `id_counter` row numbering the run-time loads' file indices (never reset, so an index is never reused).
const VFILE_COUNTER: &str = "npc_src_vfile";

// ---------------------------------------------------------------------------------------------- the world build

/// build_world (a map-server start): map_reloadnpc(false) makes npc_src_files the start-up set; nothing of the
/// previous run's run-time loads is left, and barter_db has had its first load only.
pub fn on_world_build(c: &Ctx) {
    for p in c.db().npc_src_file().iter().map(|r| r.path).collect::<Vec<_>>() {
        c.db().npc_src_file().path().delete(p);
    }
    map_reloadnpc(c, false);
    for v in c.db().npc_src_vfile().iter().map(|r| r.vfile).collect::<Vec<_>>() {
        c.db().npc_src_vfile().vfile().delete(v);
    }
    for n in c.db().barter_npc().iter().map(|r| r.name).collect::<Vec<_>>() {
        c.db().barter_npc().name().delete(n);
    }
    set_barter_reloaded(c, false);
    // the dynamic NPCs went with the old world (their removal timers find nothing)
    for d in c.db().npc_dynamic().iter().collect::<Vec<_>>() {
        if d.removal_tid != 0 {
            crate::timer::delete(c, d.removal_tid);
        }
        c.db().npc_dynamic().npc_id().delete(d.npc_id);
    }
    for a in c.db().pc_npc_dynamic().iter().map(|r| r.account_id).collect::<Vec<_>>() {
        c.db().pc_npc_dynamic().account_id().delete(a);
    }
}

/// BarterDatabase::loadingFinished: the NPC made for `name` (s_npc_barter::npcid).
pub(crate) fn set_barter_npc(c: &Ctx, name: &str, npc_id: u32) {
    let row = BarterNpc { name: name.to_string(), npc_id };
    if c.db().barter_npc().name().find(name.to_string()).is_some() {
        c.db().barter_npc().name().update(row);
    } else {
        c.db().barter_npc().insert(row);
    }
}

// ---------------------------------------------------------------------------------------------- the files

thread_local! {
    /// The embedded files by path (the first of a path wins; paths are unique).
    static INDEX: RefCell<Option<Rc<HashMap<String, u32>>>> = const { RefCell::new(None) };
}

fn index() -> Rc<HashMap<String, u32>> {
    INDEX.with(|i| {
        let mut i = i.borrow_mut();
        if i.is_none() {
            let mut m = HashMap::new();
            for (n, f) in code::files().iter().enumerate() {
                m.entry(f.path.clone()).or_insert(n as u32);
            }
            *i = Some(Rc::new(m));
        }
        i.as_ref().unwrap().clone()
    })
}

/// A directory of the embedded tree (some file lies under it).
fn dir_exists(dir: &str) -> bool {
    let prefix = format!("{dir}/");
    index().keys().any(|p| p.starts_with(&prefix))
}

/// The path of an existing file as the file system resolves `path` relative to rAthena's root, without the disk:
/// "." and empty components are skipped, ".." goes up from a directory that exists. None for what stat would not
/// find as a regular file of the embedded tree: an absolute path, a path above the root, a trailing '/', or a
/// ".." after a component that is not a directory of the tree.
pub fn resolve_path(path: &str) -> Option<String> {
    if path.is_empty() || path.starts_with('/') || path.ends_with('/') {
        return None;
    }
    let mut parts: Vec<&str> = Vec::new();
    for seg in path.split('/') {
        match seg {
            "" | "." => {}
            ".." => {
                if parts.is_empty() || !dir_exists(&parts.join("/")) {
                    return None;
                }
                parts.pop();
            }
            s => parts.push(s),
        }
    }
    Some(parts.join("/"))
}

/// common/utils.cpp check_filepath(path) == 2 (a regular file): the embedded file `path` names, if any. rAthena
/// stats the path on its disk; the module has only the files it embeds, which are everything rAthena ships under npc/
/// (and the Loki overlay's), so a path outside that set is a missing file — as are directories (3 in rAthena).
pub fn check_filepath(path: &str) -> Option<u32> {
    let p = resolve_path(path)?;
    index().get(&p).copied()
}

/// script/code.rs: the embedded file a run-time load's file index stands for.
pub fn vfile_source(c: &Ctx, vfile: u32) -> Option<u32> {
    c.db().npc_src_vfile().vfile().find(vfile).map(|r| r.file)
}

/// A new file index for a run-time load of embedded file `file`.
fn new_vfile(c: &Ctx, file: u32) -> u32 {
    let base = code::files().len() as u32;
    let vfile = base + crate::ids::next(c.rc, VFILE_COUNTER, 0);
    c.db().npc_src_vfile().insert(NpcSrcVfile { vfile, file });
    vfile
}

/// Whether npc_src_files holds `name` (util::vector_exists: exact).
pub fn src_listed(c: &Ctx, name: &str) -> bool {
    c.db().npc_src_file().path().find(name.to_string()).is_some()
}

/// npc.cpp npc_addsrcfile: "clear" empties the list (success); a name that is not a file fails ("Can't find source
/// file"); a name already listed fails without loading anything; otherwise the name joins the list and, with
/// `loadscript`, is parsed now (npc_parsesrcfile's result).
pub fn npc_addsrcfile(c: &Ctx, name: &str, loadscript: bool) -> bool {
    if name.eq_ignore_ascii_case("clear") {
        for p in c.db().npc_src_file().iter().map(|r| r.path).collect::<Vec<_>>() {
            c.db().npc_src_file().path().delete(p);
        }
        return true;
    }
    // Check if this is not a file
    if check_filepath(name).is_none() {
        log::error!("npc_addsrcfile: Can't find source file \"{name}\"");
        return false;
    }
    if src_listed(c, name) {
        return false; // found the file, no need to insert it again
    }
    c.db().npc_src_file().insert(NpcSrcFile { path: name.to_string() });
    if loadscript {
        return npc_parsesrcfile(c, name);
    }
    true
}

/// npc.cpp npc_delsrcfile: "all" empties the list, any other name leaves it.
pub fn npc_delsrcfile(c: &Ctx, name: &str) {
    if name.eq_ignore_ascii_case("all") {
        for p in c.db().npc_src_file().iter().map(|r| r.path).collect::<Vec<_>>() {
            c.db().npc_src_file().path().delete(p);
        }
        return;
    }
    c.db().npc_src_file().path().delete(name.to_string());
}

/// npc.cpp npc_parsesrcfile on a running server: the file's lines through the start-up's builder (npc.rs Mode::Live:
/// ids from the NPC id counter, clif_spawn on maps with players, the labels exported as each NPC is made, map flags
/// with their run-time effects), compile errors reported. 0 (false) when the path is not a file or the file starts
/// with a UTF-8 BOM, 1 otherwise — parse errors included, as in rAthena.
pub fn npc_parsesrcfile(c: &Ctx, path: &str) -> bool {
    let Some(file) = check_filepath(path) else {
        log::debug!("npc_parsesrcfile: Path doesn't seem to be a file skipping it : '{path}'.");
        return false;
    };
    let vfile = new_vfile(c, file);
    let files = code::files();
    let Some(f) = files.get(file as usize) else { return false };
    code::set_loading(true);
    // the NPCs' events go into ev_db one by one; its iteration ranks are brought up to date once, at the end
    npc_run::ev_db_lock();
    let mut b = Builder::new(c, Mode::Live);
    let ok = b.parse_file(vfile, &f.text, path);
    npc_run::ev_db_unlock(c);
    code::set_loading(false);
    ok
}

/// map.cpp map_reloadnpc's file (RENEWAL).
pub const SCRIPTS_MAIN_CONF: &str = "npc/re/scripts_main.conf";

/// A setting map_reloadnpc_sub acts on, in file order.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum SrcOp {
    /// `npc: <file>`: npc_addsrcfile(w2, false).
    Npc(String),
    /// `delnpc: <file>`: npc_delsrcfile(w2).
    Delnpc(String),
}

/// fgets(line, 1024, fp) over a whole file: pieces that end after a '\n' or after 1023 bytes.
fn fgets_lines(text: &[u8]) -> Vec<&[u8]> {
    let mut out = Vec::new();
    let mut p = 0;
    while p < text.len() {
        let mut e = p;
        while e < text.len() && e - p < 1023 {
            e += 1;
            if text[e - 1] == b'\n' {
                break;
            }
        }
        out.push(&text[p..e]);
        p = e;
    }
    out
}

/// sscanf(line, "%1023[^:]: %1023[^\t\r\n]", w1, w2) == 2 (a NUL ends the string).
fn scan_setting(line: &[u8]) -> Option<(Vec<u8>, Vec<u8>)> {
    let line = &line[..line.iter().position(|&b| b == 0).unwrap_or(line.len())];
    let mut p = 0;
    while p < line.len() && line[p] != b':' && p < 1023 {
        p += 1;
    }
    if p == 0 || p >= line.len() || line[p] != b':' {
        return None;
    }
    let w1 = line[..p].to_vec();
    p += 1;
    while p < line.len() && matches!(line[p], b' ' | b'\t' | b'\n' | 0x0b | 0x0c | b'\r') {
        p += 1;
    }
    let s = p;
    while p < line.len() && !matches!(line[p], b'\t' | b'\r' | b'\n') && p - s < 1023 {
        p += 1;
    }
    if p == s {
        return None;
    }
    Some((w1, line[s..p].to_vec()))
}

/// map.cpp map_reloadnpc_sub over an embedded conf file: its `npc:` / `delnpc:` settings in order, `import:` files
/// read in place ("Map configuration file not found" for one the module does not embed; rAthena recurses without a
/// limit, a cycle stops here after 64 levels). Lines as fgets reads them, "//" lines skipped, a "//" elsewhere
/// replaced by a line end, trailing spaces stripped from the value.
pub fn map_reloadnpc_sub(cfg: &str, out: &mut Vec<SrcOp>, depth: u32) {
    let Some(file) = check_filepath(cfg).filter(|_| depth < 64) else {
        log::error!("Map configuration file not found at: {cfg}");
        return;
    };
    let files = code::files();
    let Some(f) = files.get(file as usize) else { return };
    for raw in fgets_lines(&f.text) {
        if raw.starts_with(b"//") {
            continue;
        }
        let mut line = raw.to_vec();
        if let Some(i) = line.windows(2).position(|w| w == b"//") {
            line[i] = b'\n'; // Strip comments
        }
        let Some((w1, mut w2)) = scan_setting(&line) else { continue };
        // Strip trailing spaces
        while w2.last() == Some(&b' ') {
            w2.pop();
        }
        let w2 = crate::net::wire::from_latin1(&w2);
        if w1.eq_ignore_ascii_case(b"npc") {
            out.push(SrcOp::Npc(w2));
        } else if w1.eq_ignore_ascii_case(b"delnpc") {
            out.push(SrcOp::Delnpc(w2));
        } else if w1.eq_ignore_ascii_case(b"import") {
            map_reloadnpc_sub(&w2, out, depth + 1);
        } else {
            log::warn!("Unknown setting '{}' in file {cfg}", crate::net::wire::from_latin1(&w1));
        }
    }
}

/// npc_src_files as map_reloadnpc(true) leaves it, in order (npc_addsrcfile's rules over map_reloadnpc_sub's settings:
/// "clear", a missing file, a file already listed; npc_delsrcfile's "all"): what npc_loadsrcfiles loads. It is the
/// start-up set datagen embedded first (a unit test checks it).
pub fn reloadnpc_list() -> Vec<String> {
    let mut ops = Vec::new();
    map_reloadnpc_sub(SCRIPTS_MAIN_CONF, &mut ops, 0);
    let mut list: Vec<String> = Vec::new();
    for op in ops {
        match op {
            SrcOp::Npc(name) if name.eq_ignore_ascii_case("clear") => list.clear(),
            SrcOp::Npc(name) => {
                if check_filepath(&name).is_some() && !list.contains(&name) {
                    list.push(name);
                }
            }
            SrcOp::Delnpc(name) if name.eq_ignore_ascii_case("all") => list.clear(),
            SrcOp::Delnpc(name) => list.retain(|n| *n != name),
        }
    }
    list
}

/// map.cpp map_reloadnpc: `if (clear) npc_addsrcfile("clear", false)`, then npc/re/scripts_main.conf through
/// map_reloadnpc_sub, each `npc:` setting going through npc_addsrcfile(w2, false) and each `delnpc:` through
/// npc_delsrcfile(w2). (map_config_read's own `npc:` lines — conf/map_athena.conf and its imports — are only read at
/// start-up; they have none.)
pub fn map_reloadnpc(c: &Ctx, clear: bool) {
    if clear {
        npc_addsrcfile(c, "clear", false);
    }
    let mut ops = Vec::new();
    map_reloadnpc_sub(SCRIPTS_MAIN_CONF, &mut ops, 0);
    for op in ops {
        match op {
            SrcOp::Npc(name) => {
                // a release server's npc/scripts_custom.conf is rAthena's own, which loads nothing (release.rs)
                if !crate::release::skips_npc_file(&name) {
                    npc_addsrcfile(c, &name, false);
                }
            }
            SrcOp::Delnpc(name) => npc_delsrcfile(c, &name),
        }
    }
}

// ---------------------------------------------------------------------------------------------- unloading

/// script_free_code: the `.` variables of a script's code (script_code->local) go with it — an NPC's own code when it
/// is unloaded (not a duplicate's, which shares its source's), a user function's when it is overwritten or
/// userfunc_db is cleared.
pub fn free_code_vars(c: &Ctx, key: &CodeKey) {
    use crate::script::reg::{npc_reg_num, npc_reg_str};
    let owner = key.owner();
    let ids: Vec<u64> = c.db().npc_reg_num().by_key().filter(&owner).map(|r| r.id).collect();
    for id in ids {
        c.db().npc_reg_num().id().delete(id);
    }
    let ids: Vec<u64> = c.db().npc_reg_str().by_key().filter(&owner).map(|r| r.id).collect();
    for id in ids {
        c.db().npc_reg_str().id().delete(id);
    }
}

/// npc.cpp npc_unload_duplicates: every NPC duplicated from `src` goes (npc_unload(nd, true)), in map_foreachnpc's
/// order (id_db, an int-keyed DBMap: party.rs idb_order).
pub fn npc_unload_duplicates(c: &Ctx, src: u32) {
    let mut dups: Vec<u32> = c.db().npc().iter().filter(|n| n.src_id == src).map(|n| n.id).collect();
    dups.sort_by_key(|&id| crate::party::idb_order(id));
    npc_run::ev_db_lock(); // (ev_db's iteration ranks are brought up to date once)
    for id in dups {
        crate::npc::unload(c, id);
    }
    npc_run::ev_db_unlock(c);
}

/// npc.cpp npc_remove_mob_spawns: the monsters of the file's spawn lines are freed (mapit_geteachmob: id_db order,
/// unit_free CLR_OUTSIGHT), and with dynamic_mobs the file's lines leave their map's mob list (their spawn info is
/// removed: here the `spawn_data` row goes, which mob_get_spawns / mob_has_spawn read) and the map's pending
/// map_removemobs timer is dropped. The paths are compared with strcmp (case matters). rAthena quirks kept: with
/// dynamic_mobs on (conf::DYNAMIC_MOBS) a line that did not fit its map's list keeps its spawn info (its row stays;
/// nothing spawns from it again); npc_cache_mob is a load statistic the module does not keep.
pub fn npc_remove_mob_spawns(c: &Ctx, path: &str) -> bool {
    let mut spawn_count: i64 = 0;
    let mut unit_count = 0;
    // Remove spawned mobs
    let mut mobs: Vec<u32> = c.db().mob_data().iter().map(|m| m.id).collect();
    mobs.sort_by_key(|&id| crate::party::idb_order(id));
    for id in mobs {
        let Some(md) = crate::mob::get(c, id) else { continue }; // freed with an earlier one
        if md.spawn == 0 {
            continue;
        }
        let Some(sp) = c.db().spawn_data().id().find(md.spawn) else { continue };
        if sp.path != path {
            continue;
        }
        // if (!battle_config.dynamic_mobs) remove_spawn_info(*md->spawn, 1): dynamic_mobs is on (conf::DYNAMIC_MOBS)
        crate::mob::unit::unit_free(c, id, crate::unit::CLR_OUTSIGHT);
        unit_count += 1;
    }
    // dynamic mobs cleaning: every map's mob list (the `dynamic` spawn lines), map by map
    if crate::conf::dynamic_mobs() {
        let mut lines: Vec<(u16, u32)> = c.db().spawn_data().iter().filter(|s| s.dynamic).map(|s| (s.map, s.id)).collect();
        lines.sort_unstable();
        for (m, sid) in lines {
            let Some(sp) = c.db().spawn_data().id().find(sid) else { continue };
            if sp.path != path {
                continue;
            }
            // remove_spawn_info(*mob, mob->num): the line's quantity leaves mob_spawn_data (the row carries it)
            spawn_count += sp.num as i64;
            c.db().spawn_data().id().delete(sid);
            let mut r = crate::mob::mob_map(c, m);
            if r.mob_delete_timer != 0 {
                crate::timer::delete(c, r.mob_delete_timer);
                r.mob_delete_timer = 0;
                crate::mob::put_mob_map(c, r);
            }
        }
    }
    // (Sort spawns by spawn quantity: mob_get_spawns sorts when it is read)
    if spawn_count > 0 || unit_count > 0 {
        log::info!("{unit_count} mobs and {spawn_count} spawns were removed.");
    }
    spawn_count > 0 || unit_count > 0
}

/// npc.cpp npc_unloadfile: every NPC whose file is `path` (strcasecmp) goes with its duplicates, then the file's
/// monsters (npc_remove_mob_spawns); when anything went, npc_read_event_script; and the file leaves npc_src_files
/// (npc_delsrcfile) either way. The NPCs are met in npcname_db's DBIterator order: bucket by bucket (the hash of the
/// unique name), each bucket's tree in key order — which does not depend on the tree's shape, so it is rebuilt here
/// from the current NPCs (script/dbmap.rs StrDb, maxlen NPC_NAME_LENGTH + 1). An NPC unloaded meanwhile (a duplicate
/// of an earlier one) is skipped, as the iterator skips removed nodes. An NPC without a file (nd->path nullptr:
/// @addwarp's warps, the fake NPC) is "" here and never matches.
pub fn npc_unloadfile(c: &Ctx, path: &str) -> bool {
    let bytes = crate::net::wire::latin1;
    let mut db: crate::script::dbmap::StrDb<u32> = crate::script::dbmap::StrDb::new(crate::npc::NPC_NAME_LENGTH as u16 + 1, false);
    for n in c.db().npc().iter() {
        db.put(&bytes(&n.exname), n.id);
    }
    let order: Vec<u32> = db.iter_inorder().into_iter().map(|(_, &id)| id).collect();
    let pb = bytes(path);
    let mut found = false;
    npc_run::ev_db_lock(); // (the ranks of what is left are brought up to date once)
    for id in order {
        let Some(nd) = c.db().npc().id().find(id) else { continue };
        if !nd.path.is_empty() && bytes(&nd.path).eq_ignore_ascii_case(&pb) {
            found = true;
            npc_unload_duplicates(c, nd.id); // unload any npcs which could duplicate this but be in a different file
            crate::npc::unload(c, nd.id);
        }
    }
    npc_run::ev_db_unlock(c);
    if npc_remove_mob_spawns(c, path) {
        found = true;
    }
    if found {
        // refresh event cache
        npc_run::npc_read_event_script(c);
    }
    npc_delsrcfile(c, path);
    found
}

// ---------------------------------------------------------------------------------------------- events

/// npc.cpp npc_event_doall_path: every NPC whose file is `path` (strcasecmp; mapit_geteachnpc: id_db order) runs its
/// "<exname>::<event>" (npc_event_do: every ev_db key equal to it, case-insensitively). Returns how many ran.
pub fn npc_event_doall_path(c: &Ctx, event_name: &str, path: &str) -> i32 {
    let bytes = crate::net::wire::latin1;
    let mut ids: Vec<u32> = c.db().npc().iter().map(|n| n.id).collect();
    ids.sort_by_key(|&id| crate::party::idb_order(id));
    let pb = bytes(path);
    let mut count = 0;
    for id in ids {
        let Some(nd) = c.db().npc().id().find(id) else { continue };
        if !nd.path.is_empty() && bytes(&nd.path).eq_ignore_ascii_case(&pb) {
            let name: String = format!("{}::{}", nd.exname, event_name).chars().take(npc_run::EVENT_NAME_LENGTH - 1).collect();
            count += npc_run::npc_event_do(c, &name);
        }
    }
    log::info!("Event '{event_name}' executed with '{count}' NPCs.");
    count
}

// ---------------------------------------------------------------------------------------------- @reloadscript

/// script.cpp script_reload: userfunc_db cleared (every function's code and its `.` variables), scriptlabel_db (the
/// compiler's scratch: nothing kept), the @command bindings (bindatcmd), every script state (script_free_state: a
/// player's attached script is let go — the fake NPC vanishes from its client — and sleep timers stop), and
/// mapreg_reload: the map variables are saved and read back, so the permanent ones (`$`) stay and the temporary ones
/// (`$@`) are gone.
pub fn script_reload(c: &Ctx) {
    // userfunc_db->clear(userfunc_db, db_script_free_code_sub)
    let funcs: Vec<String> = c.db().npc_function().iter().map(|f| f.name).collect();
    for name in funcs {
        free_code_vars(c, &CodeKey::Func(name.clone()));
        c.db().npc_function().name().delete(name);
    }
    code::clear_functions(c);
    // @commands (script based): clear bindings
    crate::script::buildins::script2::admin::on_world_rebuilt(c);
    // st_db: every state (an int DBMap by state id)
    let mut states: Vec<u64> = c.db().script_state().iter().map(|r| r.id).collect();
    states.sort_unstable();
    for id in states {
        npc_run::script_free_state_id(c, id);
    }
    // mapreg_reload
    use crate::script::reg::{mapreg_num, mapreg_str};
    let ids: Vec<u64> = c.db().mapreg_num().iter().filter(|r| r.key.starts_with("$@")).map(|r| r.id).collect();
    for id in ids {
        c.db().mapreg_num().id().delete(id);
    }
    let ids: Vec<u64> = c.db().mapreg_str().iter().filter(|r| r.key.starts_with("$@")).map(|r| r.id).collect();
    for id in ids {
        c.db().mapreg_str().id().delete(id);
    }
}

/// map.cpp map_flags_init: every map (instance maps included) has its flags back at their defaults (initMapFlags;
/// BEXP / JEXP 100 through map_setmapflag_sub, which is no row here; the restricted zone and the nocommand level are
/// flag values), its skill duration list cleared (the unordered_map keeps its bucket count: skillunit's clear) and
/// its questinfo freed (map_free_questinfo). mapdata->save stays (it is read only while nosave is on, and every
/// nosave line sets it). pk_mode is off (conf::PK_MODE), so no map is made PvP.
fn map_flags_init(c: &Ctx) {
    use crate::npc::mapflag;
    use crate::skillunit::data::{map_skill_duration, map_skill_duration_order};
    for k in c.db().mapflag().iter().map(|f| f.key).collect::<Vec<_>>() {
        c.db().mapflag().key().delete(k);
    }
    let mut maps: Vec<u16> = c.db().map_skill_duration().iter().map(|r| r.map).collect();
    maps.extend(c.db().map_skill_duration_order().iter().map(|r| r.map));
    maps.sort_unstable();
    maps.dedup();
    for m in maps {
        crate::skillunit::data::map_skill_duration_clear(c, m);
    }
    // mapdata->damage_adjust = {}; mapdata->skill_damage.clear() (map_skill_damage.rs)
    for m in crate::map_skill_damage::maps_with_data(c) {
        crate::map_skill_damage::clear(c, m);
    }
    crate::quest::clear_questinfo(c);
}

/// map.cpp map_data_copyall: each instance map (whose instance does not keep its own flags: nomapflag) takes its
/// source map's data again (map_data_copy), which map_flags_init cleared.
fn map_data_copyall(c: &Ctx) {
    for im in crate::instance_map::all(c) {
        let nomapflag = crate::instance::find(c, im.instance_id).map(|i| i.nomapflag).unwrap_or(false);
        if nomapflag {
            continue;
        }
        crate::instance_map::map_data_copy(c, im.m, im.src_m);
    }
}

/// npc.cpp npc_reload (after atcommand.cpp's player loops, map_reloadnpc(true) and script_reload): the whole world
/// of NPCs is made again from the start-up files on the running server.
pub fn npc_reload(c: &Ctx) {
    // clear guild flag cache
    crate::woe::guild_flags_clear(c);
    // npc_clear_pathlist / db_clear(npc_path_db): the paths are the NPC rows' own strings
    // db_clear(npcname_db): the NPCs leave it as they are unloaded below. (rAthena's fake NPC leaves npcname_db too
    // and is not put back, so npc_name2id("FAKE_NPC") fails after a reload; the module keeps its row findable.)
    // db_clear(ev_db)
    npc_run::ev_db_clear(c);
    // Remove all npcs/mobs. [Skotlex]
    let markets = crate::shop::npc_market_fromsql(c);
    let fake = npc_run::fake_npc_id(c);
    let mut units: Vec<(u32, bool)> = c.db().npc().iter().filter(|n| n.id != fake).map(|n| (n.id, true)).collect();
    units.extend(c.db().mob_data().iter().map(|m| (m.id, false)));
    units.sort_by_key(|&(id, _)| crate::party::idb_order(id));
    for (id, is_npc) in units {
        if is_npc {
            if c.db().npc().id().find(id).is_some() {
                crate::npc::unload_ex(c, id, false);
            }
        } else if crate::mob::get(c, id).is_some() {
            crate::mob::unit::unit_free(c, id, crate::unit::CLR_OUTSIGHT);
        }
    }
    // the shops' shared item lists and the barter NPC ids went with the NPCs
    for root in c.db().npc_shop_family().iter().map(|r| r.root).collect::<Vec<_>>() {
        c.db().npc_shop_family().root().delete(root);
    }
    for n in c.db().barter_npc().iter().map(|r| r.name).collect::<Vec<_>>() {
        c.db().barter_npc().name().delete(n);
    }
    // dynamic check by [random]: every map's mob list goes (with the spawn info below) and its removal timer
    if crate::conf::dynamic_mobs() {
        let maps: Vec<u16> = c.db().mob_map().iter().filter(|r| r.mob_delete_timer != 0).map(|r| r.map).collect();
        for m in maps {
            let mut r = crate::mob::mob_map(c, m);
            // Mobs were removed anyway,so delete the timer [Inkfish]
            crate::timer::delete(c, r.mob_delete_timer);
            r.mob_delete_timer = 0;
            crate::mob::put_mob_map(c, r);
        }
    }
    // clear mob spawn lookup index (mob_clear_spawninfo) — and the spawn lines themselves
    for id in c.db().spawn_data().iter().map(|s| s.id).collect::<Vec<_>>() {
        c.db().spawn_data().id().delete(id);
    }
    // reset mapflags
    map_flags_init(c);
    // npc_loadsrcfiles: npc_src_files as map_reloadnpc(true) made it — the start-up set, in its order — each file at
    // its embedded index, the start-up's (so its code compiles as the start-up's did)
    code::set_loading(true);
    let files = code::files();
    let mut b = Builder::new(c, Mode::Reload);
    for path in reloadnpc_list() {
        let Some(i) = check_filepath(&path) else { continue };
        let Some(f) = files.get(i as usize) else { continue };
        b.parse_file(i, &f.text, &path);
    }
    b.finish_touch_order();
    let (warps, shops, scripts, mobs) = b.totals();
    log::info!("Done loading '{}' NPCs: {warps} warps, {shops} shops, {scripts} scripts, {mobs} spawn sets", warps + shops + scripts);
    // stylist_db.reload(): data only. barter_db.reload(): clear() — the old barter NPCs were unloaded above, so
    // ~s_npc_barter finds nothing to unload — then load(), whose loadingFinished makes the NPCs in the order the
    // database iterates after a reload
    b.create_barters(barter_reload_order());
    set_barter_reloaded(c, true);
    // Re-read the NPC Script Events cache: the labels of the NPCs just made go into ev_db as rAthena's parse put them
    npc_run::export_all_labels(c);
    code::set_loading(false);
    // refresh guild castle flags on both woe setups
    npc_run::npc_event_doall(c, crate::woe::AGIT_INIT_EVENT_NAME);
    npc_run::npc_event_doall(c, crate::woe::AGIT_INIT2_EVENT_NAME);
    npc_run::npc_event_doall(c, crate::woe::AGIT_INIT3_EVENT_NAME);
    // Execute the OnInit event for freshly loaded npcs. [Skotlex]
    npc_run::npc_event_runall(c, npc_run::INIT_EVENT_NAME);
    map_data_copyall(c);
    crate::instance::do_reload_instance(c);
    // Execute rest of the startup events if connected to char-server. [Lance] (it always is here)
    npc_run::npc_event_runall(c, npc_run::INTER_INIT_EVENT_NAME);
    crate::shop::npc_market_checkall(c, &markets);
}

// ---------------------------------------------------------------------------------------------- barter_db

/// The barters in barter_db's iteration order now: the first load's (the dataset's order) until the database has
/// been reloaded, then the reload order (tools/datagen_mods/items.py: clear() keeps the bucket count).
fn barter_order(c: &Ctx) -> Vec<&'static crate::db::items::barter::Barter> {
    if barter_reloaded(c) {
        barter_reload_order().collect()
    } else {
        crate::db::items::barter::barter_db().iter().collect()
    }
}

fn barter_reload_order() -> impl Iterator<Item = &'static crate::db::items::barter::Barter> {
    crate::atcommand_atcmd::admin::reload_orders().barter_db.iter().filter_map(|n| crate::db::items::barter::barter(n))
}

const BARTER_DB: &str = "barter_db";

fn barter_reloaded(c: &Ctx) -> bool {
    use crate::atcommand_atcmd::admin::db_reloaded;
    c.db().db_reloaded().db().find(BARTER_DB.to_string()).is_some()
}

fn set_barter_reloaded(c: &Ctx, on: bool) {
    use crate::atcommand_atcmd::admin::{db_reloaded, DbReloaded};
    let found = c.db().db_reloaded().db().find(BARTER_DB.to_string()).is_some();
    if on && !found {
        c.db().db_reloaded().insert(DbReloaded { db: BARTER_DB.to_string() });
    } else if !on && found {
        c.db().db_reloaded().db().delete(BARTER_DB.to_string());
    }
}

/// barter_db.reload() (@reloadbarterdb): BarterDatabase::clear destroys every s_npc_barter in the database's current
/// iteration order, and ~s_npc_barter unloads the NPC made for it when that NPC still exists (npc_unload(nd, true),
/// npc_read_event_script); load() reads the same files again and loadingFinished makes a new NPC per barter (new ids,
/// clif_spawn on maps with players) and reads its stock back, in the reload order.
pub fn barter_db_reload(c: &Ctx) {
    for barter in barter_order(c) {
        let Some(row) = c.db().barter_npc().name().find(barter.name.clone()) else { continue };
        c.db().barter_npc().name().delete(barter.name.clone());
        // Check if the NPC still exists or has been removed already
        let nd = c.db().npc().id().find(row.npc_id).filter(|n| n.subtype == NPCTYPE_BARTER);
        if nd.is_some() {
            crate::npc::unload(c, row.npc_id);
            npc_run::npc_read_event_script(c);
        }
    }
    let mut b = Builder::new(c, Mode::Live);
    b.create_barters(barter_reload_order());
    set_barter_reloaded(c, true);
}

// ---------------------------------------------------------------------------------------------- run-time duplicates

/// npc_data::dynamicnpc of an NPC made by npc_duplicate_npc_for_player (its owner is the `npc` row's `owner_char`,
/// mirrored in its unit for the packets): the last time its owner dealt with it and its removal timer.
#[table(accessor = npc_dynamic)]
#[derive(Clone, Debug)]
pub struct NpcDynamic {
    #[primary_key]
    pub npc_id: u32,
    pub last_interaction: i64,
    pub removal_tid: u64,
}

/// map_session_data::npc_id_dynamic: the dynamic NPCs a player owns, in the order they were made.
#[table(accessor = pc_npc_dynamic)]
#[derive(Clone, Debug)]
pub struct PcNpcDynamic {
    #[primary_key]
    pub account_id: u32,
    pub ids: Vec<u32>,
}

/// clif.hpp e_dynamicnpc_result.
pub const DYNAMICNPC_RESULT_DUPLICATE: u32 = 3;

/// common/timer.cpp:24 `const t_tick TIMER_MIN_INTERVAL = 20;`: the shortest wait between two passes of rAthena's main
/// loop (do_timer's return value, timer.cpp:411), i.e. how soon a timer added for "now" inside a callback runs.
const TIMER_MIN_INTERVAL: i64 = 20;

fn npc_id_dynamic(c: &Ctx, aid: u32) -> Vec<u32> {
    c.db().pc_npc_dynamic().account_id().find(aid).map(|r| r.ids).unwrap_or_default()
}

fn put_npc_id_dynamic(c: &Ctx, aid: u32, ids: Vec<u32>) {
    if ids.is_empty() {
        c.db().pc_npc_dynamic().account_id().delete(aid);
    } else if c.db().pc_npc_dynamic().account_id().find(aid).is_some() {
        c.db().pc_npc_dynamic().account_id().update(PcNpcDynamic { account_id: aid, ids });
    } else {
        c.db().pc_npc_dynamic().insert(PcNpcDynamic { account_id: aid, ids });
    }
}

/// npc_parse_duplicate with an owner: `owner->npc_id_dynamic.push_back(nd->id)` (npc.rs Builder, before the NPC is
/// placed).
pub(crate) fn owner_push(c: &Ctx, owner_char: u32, npc_id: u32) {
    let Some(sd) = crate::pc::charid2sd(c, owner_char) else { return };
    let mut ids = npc_id_dynamic(c, sd.id);
    ids.push(npc_id);
    put_npc_id_dynamic(c, sd.id, ids);
}

/// `if (nd->dynamicnpc.owner_char_id != 0) nd->dynamicnpc.last_interaction = gettick();` (npc_click, npc_scriptcont,
/// buildin close / close3 and end).
pub fn dynamicnpc_touch(c: &Ctx, npc_id: u32) {
    if let Some(d) = c.db().npc_dynamic().npc_id().find(npc_id) {
        c.db().npc_dynamic().npc_id().update(NpcDynamic { last_interaction: c.tick(), ..d });
    }
}

/// npc_unload's dynamic NPC part: its removal timer stops and its owner (when online) no longer lists it.
pub(crate) fn on_npc_unload(c: &Ctx, nd: &crate::npc::Npc) {
    if let Some(d) = c.db().npc_dynamic().npc_id().find(nd.id) {
        if d.removal_tid != 0 {
            crate::timer::delete(c, d.removal_tid);
        }
        c.db().npc_dynamic().npc_id().delete(nd.id);
    }
    if nd.owner_char != 0 {
        if let Some(owner) = crate::pc::charid2sd(c, nd.owner_char) {
            let mut ids = npc_id_dynamic(c, owner.id);
            if let Some(i) = ids.iter().position(|&x| x == nd.id) {
                ids.remove(i);
                put_npc_id_dynamic(c, owner.id, ids);
            }
        }
    }
}

/// unit.cpp unit_free for a player: every dynamic NPC it owns is unloaded — its owner erased first, "to prevent loops
/// from npc_unload" — then npc_read_event_script, and the list is emptied.
pub fn unit_free_pc(c: &Ctx, aid: u32) {
    let ids = npc_id_dynamic(c, aid);
    if ids.is_empty() {
        return;
    }
    npc_run::ev_db_lock();
    for id in ids {
        let Some(mut nd) = c.db().npc().id().find(id) else { continue };
        // Erase the owner first to prevent loops from npc_unload
        nd.owner_char = 0;
        c.db().npc().id().update(nd);
        if let Some(mut u) = crate::unit::get(c, id) {
            u.npc_owner_char = 0;
            crate::unit::save(c, &u);
        }
        // Delete the NPC
        crate::npc::unload(c, id);
    }
    npc_run::ev_db_unlock(c);
    // Update NPC event database
    npc_run::npc_read_event_script(c);
    c.db().pc_npc_dynamic().account_id().delete(aid);
}

/// npc.cpp npc_dynamicnpc_removal_timer.
pub fn npc_dynamicnpc_removal_timer(c: &Ctx, _tid: u64, id: u32) {
    let Some(nd) = c.db().npc().id().find(id) else { return };
    let Some(mut d) = c.db().npc_dynamic().npc_id().find(id) else { return };
    d.removal_tid = 0;
    if let Some(sd) = crate::pc::charid2sd(c, nd.owner_char) {
        let p = crate::pc::get(c, sd.id);
        // Still talking to the NPC
        if p.map(|p| p.npc_id == nd.id || p.npc_shopid == nd.id).unwrap_or(false) {
            // Retry later
            d.last_interaction = c.tick();
            d.removal_tid = crate::timer::add(c, d.last_interaction + crate::conf::feature_dynamicnpc_timeout(), crate::timer::Func::NpcDynamicnpcRemoval, nd.id, 0);
            c.db().npc_dynamic().npc_id().update(d);
            return;
        }
        // Last interaction is not long enough in the past
        if c.tick() - d.last_interaction < crate::conf::feature_dynamicnpc_timeout() {
            // rAthena's due tick is last_interaction + DIFF_TICK(gettick(), last_interaction) = gettick(): the clock
            // at this call, which is past the tick do_timer runs with (or equal to it within the same millisecond), so
            // the timer runs again at the main loop's NEXT pass — core.cpp:389-432 waits in do_sockets for
            // do_timer's return value, at least TIMER_MIN_INTERVAL — and so on, every pass, until the timeout is
            // reached. Here gettick() inside a callback is the callback's own tick (world.rs Ctx::tick), so the same
            // expression would put the timer back at the tick it just ran with: do_timer would run it again at once,
            // 20,000 times per wakeup (its runaway guard), with the database stalled for as long as the last
            // interaction is younger than the timeout (seen by wave G's npc_dynamic scenario: every wakeup 1.2-1.6 s
            // late). The next pass is therefore written out: the reducer's clock plus TIMER_MIN_INTERVAL.
            let next_pass = c.tick().max(c.real_tick()) + TIMER_MIN_INTERVAL;
            d.removal_tid = crate::timer::add(c, next_pass, crate::timer::Func::NpcDynamicnpcRemoval, nd.id, 0);
            c.db().npc_dynamic().npc_id().update(d);
            return;
        }
        // npc id from sd->npc_id_dynamic is removed in npc_unload
    }
    c.db().npc_dynamic().npc_id().update(d);
    // Delete the NPC
    crate::npc::unload(c, nd.id);
    // Update NPC event database
    npc_run::npc_read_event_script(c);
}

/// npc.cpp npc_duplicate_npc: a copy of `nd` named `name` at (x, y) of map `mapid`, through npc_parse_duplicate with
/// the lines it writes (w1 "<map>,<x>,<y>,<dir>", w2 "duplicate(<exname>)", w3 "<name>::<unique name>" — the first
/// "<i>_<map>_<x>_<y>" free, w4 "<class>[,<xs>,<ys>]"), `owner` its owner's char id (npc_duplicate_npc_for_player);
/// then the copy's OnInit when ev_db has "<unique name>::OnInit". None when npc_parse_duplicate made nothing.
#[allow(clippy::too_many_arguments)]
pub fn npc_duplicate_npc(c: &Ctx, nd: &crate::npc::Npc, name: &str, mapid: u16, x: i16, y: i16, class: i32, dir: u8, xs: i16, ys: i16, owner_char: u32) -> Option<crate::npc::Npc> {
    let mapname = crate::data::maps().get(mapid)?.name.clone();
    let w1 = format!("{mapname},{x},{y},{dir}");
    let w2 = format!("duplicate({})", nd.exname);
    // Making sure the generated name is not used for another npc.
    let mut i = 0;
    let mut exname = format!("{i}_{mapid}_{x}_{y}");
    while npc_run::npc_name2id(c, &exname).is_some() {
        i += 1;
        exname = format!("{i}_{mapid}_{x}_{y}");
    }
    let w3 = format!("{name}::{exname}");
    let w4 = if xs >= 0 && ys >= 0 { format!("{class},{xs},{ys}") } else { format!("{class}") };
    let latin1 = crate::net::wire::latin1;
    npc_run::ev_db_lock();
    let mut b = Builder::new(c, Mode::Live);
    b.set_owner(owner_char);
    b.duplicate_line(&latin1(&w1), &latin1(&w2), &latin1(&w3), &latin1(&w4)); // DUPLICATE means nothing for now.
    npc_run::ev_db_unlock(c);
    let dnd = npc_run::npc_name2id(c, &exname)?;
    // run OnInit Events
    let evname: String = format!("{exname}::{}", npc_run::INIT_EVENT_NAME).chars().take(npc_run::EVENT_NAME_LENGTH - 1).collect();
    if c.db().npc_event().name().find(&evname).is_some() {
        npc_run::npc_event_do(c, &evname);
    }
    c.db().npc().id().find(dnd.id)
}

/// npc.cpp npc_duplicate_npc_for_player: a copy of `nd` next to the player that only the player sees and uses,
/// removed feature.dynamicnpc_timeout after the player last dealt with it. None (and no copy) when the player already
/// has a copy of the same source (clif_dynamicnpc_result DUPLICATE), on a nodynamicnpc map (no answer), or when no
/// free cell is found around the player.
pub fn npc_duplicate_npc_for_player(c: &Ctx, nd: &crate::npc::Npc, aid: u32) -> Option<crate::npc::Npc> {
    let sd = crate::unit::get(c, aid)?;
    // A duplicate of a duplicate is still a duplicate of the same NPC
    let src_id = if nd.src_id > 0 { nd.src_id } else { nd.id };
    for it in npc_id_dynamic(c, aid) {
        // Check if the source NPC id of currently active duplicates already exists.
        if c.db().npc().id().find(it).map(|s| s.src_id == src_id).unwrap_or(false) {
            let p = crate::net::packets::PACKET_ZC_DYNAMICNPC_CREATE_RESULT {
                PacketType: crate::net::packets::PACKET_ZC_DYNAMICNPC_CREATE_RESULT::ID,
                result: DYNAMICNPC_RESULT_DUPLICATE,
            };
            crate::clif::send(c, &p.to_vec(), &sd, crate::clif::Target::SelfOnly);
            return None;
        }
    }
    if crate::npc::mapflag(c, sd.map, MF_NODYNAMICNPC) {
        // It has been confirmed that there is no reply to the client
        return None;
    }
    let (found, new_x, new_y) = crate::script::buildins::script2::mapmisc::map_search_freecell(c, Some(&sd), None, 0, 0, crate::conf::feature_dynamicnpc_rangex(), crate::conf::feature_dynamicnpc_rangey(), 0);
    if !found {
        let ch = crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
        log::error!("npc_duplicate_npc_for_player: Unable to find a free cell to duplicate NPC \"{}\" for player \"{}\"(AID: {aid}, CID: {ch})", nd.exname, sd.name);
        return None;
    }
    let dir = if crate::conf::feature_dynamicnpc_direction() {
        // Let the NPC look to the player
        crate::map::calc_dir_xy(new_x, new_y, sd.x, sd.y, crate::path::DIR_CENTER as u8)
    } else {
        // Use original NPCs direction
        crate::unit::get(c, nd.id).map(|u| u.ud.dir).unwrap_or(0)
    };
    let owner_char = crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
    let dnd = npc_duplicate_npc(c, nd, &nd.name, sd.map, new_x, new_y, nd.class, dir, nd.xs, nd.ys, owner_char)?;
    let last_interaction = c.tick();
    let removal_tid = crate::timer::add(c, last_interaction + crate::conf::feature_dynamicnpc_timeout(), crate::timer::Func::NpcDynamicnpcRemoval, dnd.id, 0);
    let row = NpcDynamic { npc_id: dnd.id, last_interaction, removal_tid };
    if c.db().npc_dynamic().npc_id().find(dnd.id).is_some() {
        c.db().npc_dynamic().npc_id().update(row);
    } else {
        c.db().npc_dynamic().insert(row);
    }
    Some(dnd)
}

/// map.hpp e_mapflag MF_NODYNAMICNPC.
const MF_NODYNAMICNPC: i16 = 76;

#[cfg(test)]
mod tests {
    use super::*;

    /// data/npcs.bin holds the start-up set first, then every other shipped file: paths are unique, each file is found
    /// at its own index, and map_reloadnpc over the embedded conf files lists exactly the start-up set, in order.
    #[test]
    fn embedded_files_start_with_the_start_up_set() {
        let files = code::files();
        let boot = crate::data::npc_boot_count();
        assert!(boot > 0 && boot < files.len(), "boot {boot} of {}", files.len());
        let mut seen = std::collections::HashSet::new();
        for (i, f) in files.iter().enumerate() {
            assert!(seen.insert(f.path.clone()), "path {} twice", f.path);
            assert_eq!(check_filepath(&f.path), Some(i as u32), "{}", f.path);
        }
        let start_up: Vec<String> = files[..boot].iter().map(|f| f.path.clone()).collect();
        assert_eq!(reloadnpc_list(), start_up);
        // the conf files are shipped files too, outside the start-up set
        let conf = check_filepath(SCRIPTS_MAIN_CONF).expect("scripts_main.conf is embedded");
        assert!(conf as usize >= boot);
    }

    /// check_filepath answers as stat() on rAthena's tree would: "." / "//" / ".." resolve, a directory, a trailing '/',
    /// an absolute path, another case or a path above the root are not a file.
    #[test]
    fn check_filepath_resolves_like_the_file_system() {
        let boot = crate::data::npc_boot_count() as u32;
        let smoke = check_filepath("npc/custom/loki_smoke.txt").expect("the Loki smoke file is embedded");
        assert!(smoke < boot);
        for same in ["./npc/custom/loki_smoke.txt", "npc//custom/./loki_smoke.txt", "npc/re/../custom/loki_smoke.txt"] {
            assert_eq!(check_filepath(same), Some(smoke), "{same}");
        }
        for missing in [
            "",
            "npc/custom",
            "npc/custom/",
            "npc/custom/loki_smoke.txt/",
            "/npc/custom/loki_smoke.txt",
            "NPC/custom/loki_smoke.txt",
            "../npc/custom/loki_smoke.txt",
            "npc/nowhere/../custom/loki_smoke.txt",
            "npc/custom/loki_smoke.txt/../loki_smoke.txt",
            "npc/custom/no_such_file.txt",
        ] {
            assert_eq!(check_filepath(missing), None, "{missing}");
        }
        // a shipped file that the start-up does not load
        let other = check_filepath("npc/custom/breeder.txt").expect("npc/custom/breeder.txt is embedded");
        assert!(other >= boot);
    }

    /// map_reloadnpc_sub's line rules: sscanf("%1023[^:]: %1023[^\t\r\n]") after the "//" handling, fgets' pieces.
    #[test]
    fn conf_lines_scan_like_sscanf() {
        assert_eq!(scan_setting(b"npc: npc/a.txt\r\n"), Some((b"npc".to_vec(), b"npc/a.txt".to_vec())));
        assert_eq!(scan_setting(b"npc:npc/a.txt"), Some((b"npc".to_vec(), b"npc/a.txt".to_vec())));
        assert_eq!(scan_setting(b"import:\t npc/b.conf  \n"), Some((b"import".to_vec(), b"npc/b.conf  ".to_vec())));
        assert_eq!(scan_setting(b"npc:\n"), None);
        assert_eq!(scan_setting(b"npc npc/a.txt\n"), None);
        assert_eq!(scan_setting(b":x\n"), None);
        let lines = fgets_lines(b"a\nbb\r\n\nc");
        assert_eq!(lines, vec![&b"a\n"[..], &b"bb\r\n"[..], &b"\n"[..], &b"c"[..]]);
        let long = vec![b'x'; 2050];
        assert_eq!(fgets_lines(&long).iter().map(|l| l.len()).collect::<Vec<_>>(), vec![1023, 1023, 4]);
    }
}
