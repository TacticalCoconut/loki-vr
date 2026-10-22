//! The ported script commands (BUILDIN_FUNC), grouped by subject. A command is found by the C function name of its
//! buildin_func[] entry (so aliases like `disablenpc` → `enablenpc` share one Rust function, which reads
//! `st.funcname` exactly where rAthena reads script_getfuncname).
//!
//! Adding a command from another system: write `fn(rt: &Rt, st: &mut State) -> i32` in a file of its own under
//! src/script/buildins/ (e.g. `items.rs` with `pub fn lookup(cfunc: &str) -> Option<BuildinFn>`), and add that
//! lookup to the chain in [`lookup_cfunc`]. Commands that stay unported compile, and at run time they log
//! "buildin X: not ported yet" and end the script.

pub mod bg;
pub mod core;
pub mod dialog;
pub mod extras;
pub mod guild;
pub mod instance;
pub mod items;
pub mod mob;
pub mod npcctl;
pub mod party;
pub mod pcinfo;
pub mod quest;
pub mod script2;
pub mod skilltree;
pub mod skilluse;
pub mod social2;
pub mod status;
pub mod strings;
pub mod woe;

use std::sync::OnceLock;

use super::buildin_table::BUILDIN;
use super::vm::*;

pub type BuildinFn = fn(&Rt, &mut State) -> i32;

/// Every system's lookup, in turn.
fn lookup_cfunc(cfunc: &str) -> Option<BuildinFn> {
    core::lookup(cfunc).or_else(|| strings::lookup(cfunc)).or_else(|| dialog::lookup(cfunc)).or_else(|| npcctl::lookup(cfunc)).or_else(|| pcinfo::lookup(cfunc))
        .or_else(|| status::lookup(cfunc))
        .or_else(|| items::lookup(cfunc))
        .or_else(|| quest::lookup(cfunc))
        .or_else(|| party::lookup(cfunc))
        .or_else(|| guild::lookup(cfunc))
        .or_else(|| skilltree::lookup(cfunc))
        .or_else(|| extras::lookup(cfunc))
        .or_else(|| social2::lookup(cfunc))
        .or_else(|| crate::itemui::buildins::lookup(cfunc))
        .or_else(|| script2::lookup(cfunc))
        .or_else(|| mob::lookup(cfunc))
        .or_else(|| skilluse::lookup(cfunc))
        .or_else(|| crate::skillunit::buildin::lookup(cfunc))
        .or_else(|| crate::pet::buildins::lookup(cfunc))
        .or_else(|| crate::homunculus::buildins::lookup(cfunc))
        .or_else(|| crate::mercenary::buildins::lookup(cfunc).or_else(|| crate::elemental::buildins::lookup(cfunc)))
        .or_else(|| crate::atcmd_disguise::lookup(cfunc))
        .or_else(|| instance::lookup(cfunc))
        .or_else(|| woe::lookup(cfunc))
        .or_else(|| bg::lookup(cfunc))
        .or_else(|| crate::roulette::buildin_lookup(cfunc))
}

struct Table(Vec<Option<BuildinFn>>);
// SpacetimeDB runs a module on one thread.
unsafe impl Sync for Table {}
unsafe impl Send for Table {}

static TABLE: OnceLock<Table> = OnceLock::new();

/// The implementation of buildin_func[idx], if ported.
pub fn lookup(idx: usize) -> Option<BuildinFn> {
    let t = TABLE.get_or_init(|| Table(BUILDIN.iter().map(|&(_, _, cfunc)| lookup_cfunc(cfunc)).collect()));
    t.0.get(idx).copied().flatten()
}

/// Names of the ported commands (for the report / tests).
pub fn ported() -> Vec<&'static str> {
    BUILDIN.iter().enumerate().filter(|(i, _)| lookup(*i).is_some()).map(|(_, b)| b.0).collect()
}

// ------------------------------------------------------------------------------------------------ helpers

/// script_charid2sd: the player whose char id is argument `loc`, or the attached one.
pub fn charid2sd(rt: &Rt, st: &mut State, loc: i32, func: &str) -> Option<u32> {
    if st.hasdata(loc) {
        let id = getnum(rt, st, loc);
        let aid = rt.c.and_then(|c| crate::pc::charid2sd(c, id as u32)).map(|u| u.id);
        if aid.is_none() {
            log::error!("{func}: Player with char id '{id}' is not found.");
        }
        aid
    } else {
        rid2sd(rt, st, func)
    }
}

/// script_accid2sd / script_mapid2sd.
pub fn accid2sd(rt: &Rt, st: &mut State, loc: i32, func: &str) -> Option<u32> {
    if st.hasdata(loc) {
        let id = getnum(rt, st, loc);
        let ok = rt.c.map(|c| crate::pc::get(c, id as u32).is_some()).unwrap_or(false);
        if !ok {
            log::error!("{func}: Player with account id '{id}' is not found.");
            return None;
        }
        Some(id as u32)
    } else {
        rid2sd(rt, st, func)
    }
}

/// script_nick2sd.
pub fn nick2sd(rt: &Rt, st: &mut State, loc: i32, func: &str) -> Option<u32> {
    if st.hasdata(loc) {
        let name = getstr(rt, st, loc);
        let n = crate::net::wire::from_latin1(&name);
        let aid = rt.c.and_then(|c| crate::pc::nick2sd(c, &n, false)).map(|u| u.id);
        if aid.is_none() {
            log::error!("{func}: Player with nick '{n}' is not found.");
        }
        aid
    } else {
        rid2sd(rt, st, func)
    }
}

/// script_rid2bl: the unit whose id is argument `loc` (0 / missing: the attached player).
pub fn rid2bl(rt: &Rt, st: &mut State, loc: i32, func: &str) -> Option<crate::unit::Unit> {
    let mut id = if st.hasdata(loc) { getnum(rt, st, loc) as u32 } else { 0 };
    if id == 0 {
        id = st.rid;
    }
    let u = rt.c.and_then(|c| crate::unit::get(c, id));
    if u.is_none() {
        log::error!("{func}: Unit with ID '{id}' is not found.");
    }
    u
}

pub fn lat(s: &[u8]) -> String {
    crate::net::wire::from_latin1(s)
}
