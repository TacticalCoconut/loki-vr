//! The NPC script engine: rAthena's script.cpp (compiler, virtual machine, variables, buildins) and npc.cpp's run
//! model (clicks, dialogs, events, timers).
//!
//! Layout:
//! - `compiler.rs` — parse_script and everything under it (str_data, labels, the bytecode writer).
//! - `vm.rs` — script_state, the stack, run_script_main / execute, operators, get_val / set_reg.
//! - `reg.rs` — variable storage (character / account / global / map / NPC / temporary registers).
//! - `param.rs` — character parameters (readparam / setparam).
//! - `code.rs` — per-instance bytecode cache: scripts are compiled from their source span on first use.
//! - `state.rs` — the `script_state` table: every live state (running, sleeping or waiting for a player).
//! - `npc_run.rs` — npc.cpp: npc_click, npc_scriptcont, events, NPC timers, clock events, OnInit, whispers.
//! - `buildins/` — the ported buildin functions; `buildin_table.rs` is the full buildin list with argument specs.
//! - `cfmt.rs` — C printf / sscanf / strftime behaviour for sprintf, sscanf and gettimestr.
//!
//! Other systems:
//! - run a script they own (an item script, …) with [`run_ext`] after registering a source provider with
//!   [`code::register_provider`];
//! - add buildins in a file of their own under `buildins/` (see `buildins/mod.rs`).

pub mod battle_flags;
pub mod buildin_table;
pub mod buildins;
pub mod cfmt;
pub mod code;
pub mod compiler;
pub mod dbmap;
pub mod dummy_sd;
pub mod npc_run;
pub mod param;
pub mod reg;
pub mod state;
pub mod vm;

#[cfg(test)]
mod tests;

use crate::npc::Npc;
use crate::world::Ctx;

/// npc.cpp npc_touch_areanpc's script branch (OnTouch_ / OnTouch, or a click when the NPC has neither).
pub fn ontouch(c: &Ctx, aid: u32, n: &Npc) {
    npc_run::touch_script(c, aid, n);
}

/// clif_parse_WisMessage → the NPC's OnWhisperGlobal. Returns true when an NPC of that name exists.
pub fn whisper_npc(c: &Ctx, aid: u32, npc_name: &str, message: &str) -> bool {
    npc_run::whisper_npc(c, aid, npc_name, message)
}

/// run_script for a script another system owns: the code is `CodeKey::Ext { kind, key }`, compiled from the text
/// that system's provider (registered with [`code::register_provider`]) returns. `rid` is the attached player
/// (0 for none), `oid` the object running it (item scripts: [`fake_npc_id`]). Returns false when the script does
/// not exist or does not compile.
pub fn run_ext(c: &Ctx, kind: &str, key: &str, rid: u32, oid: u32) -> bool {
    let k = vm::CodeKey::Ext(vm::ExtCode { kind: kind.to_string(), key: key.to_string() });
    if code::get(Some(c), &k).is_none() {
        return false;
    }
    vm::run_script(&vm::Rt::new(c), &k, 0, rid, oid);
    true
}

/// Item scripts, as pc.cpp runs them (`run_script(item->script, 0, sd->id, fake_nd->id)` for use / equip / unequip;
/// status_calc_pc's bonus runs pass oid 0): the item system registers a provider for kind "item" whose keys name
/// its scripts (e.g. "501" or "1201:equip") and returns their text with `SCRIPT_IGNORE_EXTERNAL_BRACKETS`, the item
/// db file / line, and no user functions (item_db loads before the NPC files, so userfunc_db is empty then).
pub fn run_item_script(c: &Ctx, aid: u32, key: &str, oid: u32) -> bool {
    run_ext(c, "item", key, aid, oid)
}

/// The fake NPC's id (npc.cpp fake_nd): the object item scripts and `FAKE_NPC` dialogs run as.
pub fn fake_npc_id(c: &Ctx) -> u32 {
    npc_run::fake_npc_id(c)
}

/// npc.cpp npc_event: runs `NPC::OnLabel` for a player (0 = ran, 1 = not found / busy, 2 = OnTouch busy).
pub fn npc_event(c: &Ctx, aid: u32, eventname: &str) -> i32 {
    npc_run::npc_event(c, aid, eventname, 0)
}

/// npc.cpp npc_script_event: the global player events (OnPCLoginEvent, OnPCBaseLvUpEvent, …; `npc_run::NPCE_*`).
pub fn npc_script_event(c: &Ctx, aid: u32, kind: usize) -> usize {
    npc_run::npc_script_event(c, aid, kind)
}
