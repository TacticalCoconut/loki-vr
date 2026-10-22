//! Loki Online — the Ragnarok Online (Renewal) server as one SpacetimeDB module.
//!
//! Every rule here is ported from rAthena e985006 (the same server Loki has been played against); comments cite the
//! rAthena file and function each rule comes from. Phase 0: accounts and characters (the login and char servers);
//! phase 1: the map server's presence (maps, walking, other players, chat, warps, emotes) over the RO packet protocol.
//!
//! Clients never pass identities: `ctx.sender()` is the only principal. Reducers fail with `Err(text)`; the text is
//! what the Loki login screen shows.

pub mod account;
pub mod achievement;
pub mod atcommand;
pub mod atcommand_status;
pub mod atcommand_guild;
pub mod atcommand_items;
pub mod atcommand_quest;
pub mod atcommand_script2;
pub mod atcommand_social;
pub mod atcommand_extras;
pub mod atcommand_instance;
pub mod attendance;
pub mod atcommand_social2;
pub mod atcommand_atcmd; // wave D atcmd agent: the generic @commands (group files declared inside)
#[path = "atcommand_disguise.rs"]
pub mod atcmd_disguise; // wave D atcmd agent: pc_disguise and the disguise commands
pub mod atcmd_pc; // wave D atcmd agent: the map_session_data fields of the @commands (autoloot lists, show toggles …)
pub mod session; // wave D atcmd agent: set_eof, clif_authfail_fd, clif_GM_kick, clif_parse's eof handling
pub mod autosave;
pub mod channel;
pub mod chrif;
pub mod chat;
pub mod bank;
pub mod barter;
pub mod battle;
pub mod battleground; // battlegrounds (wave D bg): battleground.rs, clif_bg.rs, script/buildins/bg.rs
pub mod clif_bg;
pub mod buyingstore;
pub mod cart;
pub mod cashshop;
pub mod character;
pub mod clan;
pub mod clif;
pub mod clif_guild;
pub mod clif_items;
pub mod clif_misc;
pub mod clif_option; // wave E archer: clif_parse_RemoveOption
pub mod battle_config; // port wave I iconf: battle.cpp battle_data / battle_set_value / battle_config_read at run time
pub mod conf;
pub mod cstr;
pub mod data;
pub mod db;
pub mod fame;
pub mod flooritem;
pub mod guild;
pub mod guild_storage;
pub mod ids;
pub mod int_mail;
pub mod int_guild;
pub mod int_party;
pub mod int_pet;
pub mod int_mercenary; // wave E mercelem: char/int_mercenary.cpp
pub mod int_elemental; // wave E mercelem: char/int_elemental.cpp
pub mod mercenary; // wave E mercelem: mercenary.cpp
pub mod elemental; // wave E mercelem: elemental.cpp
pub mod companion; // wave E mercelem: the BL_MER / BL_ELEM arms of status.cpp / unit.cpp
pub mod intif;
pub mod instance; // instances (wave D instance): instance.rs, instance_map.rs, instance_npc.rs, clif_instance.rs
pub mod instance_map;
pub mod instance_npc;
pub mod clif_instance;
pub mod inventory;
pub mod itemgroup;
pub mod mail;
pub mod itemui;
pub mod map;
pub mod map_cell;
pub mod mob;
pub mod net;
pub mod npc;
pub mod parse;
pub mod parse_items;
pub mod party;
pub mod path;
pub mod pc;
pub mod prof;
pub mod pc_equip;
pub mod pet;
pub mod homunculus; // homunculi (wave E homun): homunculus.rs + homunculus/, int_homun.rs
pub mod int_homun;
pub mod pc_misc;
pub mod pc_taekwon; // the Taekwon family's session fields and packets (feel / hate maps, TK_MISSION): wave E taekwon
pub mod quest;
pub mod release; // wave H haccount: the release switch (test / release rules) and the client version handshake
pub mod reputation;
pub mod rules;
pub mod script;
pub mod searchstore;
pub mod shop;
pub mod skill;
pub mod skillunit; // ground skill units (wave D skillunit): skillunit.rs + skillunit/
pub mod skills; // the SkillImpl classes of src/map/skills/ (wave D skillcore)
pub mod skilltree; // skill tree, job change, hotkeys (wave C skilltree): skilltree.rs, clif_skill.rs, hotkey.rs, atcommand_skill.rs
pub mod clif_skill;
pub mod hotkey;
pub mod atcommand_skill;
pub mod social;
pub mod status;
pub mod storage;
pub mod timer;
pub mod trade;
pub mod unit;
pub mod unit_npc;
pub mod vending; // vending, autotrade, duels (wave D vending): vending.rs, autotrade.rs, duel.rs, atcommand_vending.rs
pub mod autotrade;
pub mod duel;
pub mod atcommand_vending;
pub mod woe; // War of Emperium (wave D woe): woe.rs, woe/*.rs, script/buildins/woe.rs
pub mod world;
pub mod macro_detector; // the captcha / macro detector (pc.cpp pc_macro_*, clif.cpp captcha packets): cloud wave F
pub mod roulette; // wave F roulette agent: the Roulette of Fortune (clif.cpp roulette system, pc.cpp points, @roulette)
#[path = "log.rs"]
pub mod map_log; // wave F log: map/log.cpp (named map_log: a `log` module would shadow the `log` crate's macros)
pub mod npc_load;
pub mod map_skill_damage; // the skill_damage map flag's data (map.hpp s_skill_damage, map.cpp map_skill_damage_add): cloud wave F // run-time NPC files: @loadnpc, @unloadnpcfile, @reloadnpcfile, @reloadscript (wave F npcload)
pub mod clif_gm; // wave F gmpackets: clif.cpp's GM packets (the slash commands that run @commands, /kill, /check, …)
pub mod clif_client; // wave F gmpackets: clif.cpp's remaining client requests (favorites, airship, expansion, nick_db, …)
pub mod auction; // wave G gauction: the auction, map side (clif.cpp clif_Auction_* / clif_parse_Auction_*, intif.cpp intif_Auction_*)
pub mod int_auction; // wave G gauction: the auction, char side (char/int_auction.cpp: the table, the end timer, the mails)
pub mod presence; // wave H hpresence: floating headsets, voice and the game-data check (Loki's own side channel, outside the RO protocol)
pub mod horde; // wave I ihorde: Horde, the co-op side mode (Loki's own; the C# engine of client waves 8-9 ported, outside the RO protocol)
pub mod cutover; // wave I icut: updates without wiping (the owner's export / import of everything players own)

/// Bumped whenever a code change alters what the world build (npc.rs build_world) produces, so a published module
/// rebuilds the world even when the embedded data did not change.
pub const WORLD_CODE_VERSION: u64 = 17; // 17: wave G ggm (the test NPC file ggm_tests.txt in npcs.bin); 16: npcload (the build seeds npc_src_files and the barter NPC ids, npc_load.rs); 15: homun (NPCs with a homunculus class get its view data); 14: wave D merged (instances 11, battlegrounds 12, castles 13)

use spacetimedb::{reducer, ReducerContext};

/// First publish: builds the world (NPCs, warps, map flags, spawns) from the embedded rAthena data.
#[reducer(init)]
pub fn init(ctx: &ReducerContext) {
    log::info!("loki-server initialised (rules: rAthena e985006, renewal)");
    release::on_init(ctx); // the publisher is the owner; the server starts in test mode
    world::run(ctx, npc::ensure_world);
}

/// A client connected. One identity is one connection (account.rs): a character of this identity that is still in
/// the world, not waiting for clif_delayquit, lost its connection without a disconnect call (the database stopped
/// before it could run, or the disconnect reducer failed) and is taken out as rAthena's closed socket is
/// (clif_quitsave). rAthena starts clean after a restart (char.cpp char_set_all_offline at start, an empty map
/// server); SpacetimeDB runs client_disconnected for every client itself when a publish disconnects them ("Disconnecting
/// all users", checked on loki-fixes), so this only catches what that misses.
#[reducer(client_connected)]
pub fn on_connect(ctx: &ReducerContext) {
    world::run(ctx, |c| pc::on_client_gone(c, c.sender()));
    account::on_connect(ctx);
}

/// The client's connection closed: its character leaves the world as when rAthena's socket closes (clif_quitsave,
/// with prevent_logout's delay), then the login binding is marked offline.
#[reducer(client_disconnected)]
pub fn on_disconnect(ctx: &ReducerContext) {
    // an older connection of an identity that is already back on a new one (account.rs): only its own hello goes
    if account::stale_disconnect(ctx) {
        release::on_disconnect(ctx);
        return;
    }
    world::run(ctx, |c| pc::on_client_gone(c, c.sender()));
    account::on_disconnect(ctx);
}

#[cfg(test)]
mod lint {
    /// `spacetime publish` (from source) rejects a module whose `src/` contains any print macro, even in tests or
    /// comments (spacetimedb-cli tasks/rust.rs build_rust lint). The names are assembled so this file passes too.
    #[test]
    fn no_print_macros_in_src() {
        let bad: Vec<String> = ["print", "println", "eprint", "eprintln", "dbg"].iter().map(|m| format!("{m}!")).collect();
        let mut stack = vec![std::path::PathBuf::from(concat!(env!("CARGO_MANIFEST_DIR"), "/src"))];
        let mut hits = Vec::new();
        while let Some(dir) = stack.pop() {
            for e in std::fs::read_dir(&dir).unwrap().flatten() {
                let p = e.path();
                if p.is_dir() { stack.push(p); continue; }
                if p.extension().map_or(true, |x| x != "rs") { continue; }
                for (i, line) in std::fs::read_to_string(&p).unwrap().lines().enumerate() {
                    if bad.iter().any(|b| line.contains(b.as_str())) { hits.push(format!("{}:{}", p.display(), i + 1)); }
                }
            }
        }
        assert!(hits.is_empty(), "print macros that `spacetime publish` rejects: {hits:?}");
    }
}

/// Native (non-wasm) test builds on Windows: MSVC's link.exe resolves every host import the spacetimedb runtime
/// refers to, even from code the test binary never runs (GNU ld discards those sections first, which is why the
/// Linux rig linked without this). These never-called bodies stand in for the host. Signatures: spacetimedb-bindings-sys
/// 2.10.1 src/lib.rs, `mod raw` (BytesSink / BytesSource are `#[repr(transparent)]` u32 newtypes).
#[cfg(all(test, not(target_arch = "wasm32")))]
mod native_host_stubs {
    #[no_mangle]
    pub unsafe extern "C" fn bytes_sink_write(_sink: u32, _buffer_ptr: *const u8, _buffer_len_ptr: *mut usize) -> u16 {
        unreachable!("spacetimedb host import called outside the wasm host")
    }
    #[no_mangle]
    pub unsafe extern "C" fn bytes_source_read(_source: u32, _buffer_ptr: *mut u8, _buffer_len_ptr: *mut usize) -> i16 {
        unreachable!("spacetimedb host import called outside the wasm host")
    }
    #[no_mangle]
    pub unsafe extern "C" fn bytes_source_remaining_length(_source: u32, _out: *mut u32) -> i16 {
        unreachable!("spacetimedb host import called outside the wasm host")
    }
    #[no_mangle]
    pub unsafe extern "C" fn console_log(
        _level: u8,
        _target_ptr: *const u8,
        _target_len: usize,
        _filename_ptr: *const u8,
        _filename_len: usize,
        _line_number: u32,
        _message_ptr: *const u8,
        _message_len: usize,
    ) {
        unreachable!("spacetimedb host import called outside the wasm host")
    }
    #[no_mangle]
    pub unsafe extern "C" fn console_timer_start(_name_ptr: *const u8, _name_len: usize) -> u32 {
        unreachable!("spacetimedb host import called outside the wasm host")
    }
    #[no_mangle]
    pub unsafe extern "C" fn console_timer_end(_timer_id: u32) -> u16 {
        unreachable!("spacetimedb host import called outside the wasm host")
    }
    #[no_mangle]
    pub unsafe extern "C" fn get_jwt(_connection_id_ptr: *const u8, _bytes_source_id: *mut u32) -> u16 {
        unreachable!("spacetimedb host import called outside the wasm host")
    }
    #[no_mangle]
    pub unsafe extern "C" fn procedure_sleep_until(_wake_at_micros_since_unix_epoch: i64) -> i64 {
        unreachable!("spacetimedb host import called outside the wasm host")
    }
}
