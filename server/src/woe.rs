//! The War of Emperium: map/guild.cpp's castle and agit parts (the run-time half of castle_db, guild_castledatasave,
//! guild_castledataloadack, guild_castle_map_init, guild_agit_start / end for FE, SE and TE, guild_checkcastles,
//! castle_guild_broken_sub, the guardian / flag half of guild_emblem_changed, guild_flag_add / remove / clear) plus
//! the castle rules other files call into: the castle regen of pc_setpos, and the guild id / emblem id the unit
//! packets carry (status_get_guild_id / status_get_emblem_id for flags and guardians).
//!
//! Submodules:
//! - `guardian`: mob.cpp's guardians and the Emperium (guardian_data, mob_spawn_guardian(_sub),
//!   mob_guardian_guildchange, the castle bonus of status_calc_mob_, clif_name for guardians, unit_free / mob_dead).
//! - `int_castle`: char/int_guild.cpp's castle data (the `guild_castle` SQL table, the char server's castle cache,
//!   mapif_guild_castle_dataload, mapif_parse_GuildCastleDataLoad / DataSave, BreakGuild's castle rows).
//! - `clif`: clif.cpp's castle packets (ZC_GUILD_AGIT_INFO, ZC_REQ_ACK_AGIT_INVESTMENT, ZC_REQ_ACK_MOVE_GUILD_AGIT)
//!   and their parsers (CZ_REQ_AGIT_INVESTMENT, CZ_REQ_MOVE_GUILD_AGIT).
//! - `atcommand`: @agitstart / @agitend and their 2 / 3 forms.
//! The script commands are `script/buildins/woe.rs`.
//!
//! castle_db's static half (castle_db.yml) is `db::misc::castle`; its run-time half (mmo.hpp struct guild_castle from
//! `guild_id` on) is the `guild_castle_rt` table here. rAthena keeps both halves in one shared_ptr per castle, zeroed
//! at the map server's start and filled from the char server by guild_castledataloadack; the module rebuilds that
//! state with the world (npc.rs build_world → [`on_world_rebuild`], npc_run.rs on_world_built →
//! [`guild_castle_map_init`]). The agit flags (map.cpp agit_flag, agit2_flag, agit3_flag) are guild.rs's `agit_state`
//! row, which this file writes.
//!
//! Deliberately different: rAthena's map server queues castle saves while the char server is away
//! (guild_castle_reconnect); the module's char server is always there, so every save goes through at once.

pub mod atcommand;
pub mod clif;
pub mod guardian;
pub mod int_castle;

use std::sync::OnceLock;

use spacetimedb::{table, Table};

use crate::db::misc::castle::{castle_db, GuildCastle};
use crate::guild::agit_state as _;
use guardian::guardian_data as _;
use crate::intif::{self, Inter};
use crate::unit::Unit;
use crate::world::Ctx;

// ------------------------------------------------------------------------------------------------ mmo.hpp

/// mmo.hpp MAX_GUARDIANS (local max per castle).
pub const MAX_GUARDIANS: usize = 8;

/// mmo.hpp enum e_castle_data.
pub const CD_NONE: i32 = 0;
pub const CD_GUILD_ID: i32 = 1;
pub const CD_CURRENT_ECONOMY: i32 = 2;
pub const CD_CURRENT_DEFENSE: i32 = 3;
pub const CD_INVESTED_ECONOMY: i32 = 4;
pub const CD_INVESTED_DEFENSE: i32 = 5;
pub const CD_NEXT_TIME: i32 = 6;
pub const CD_PAY_TIME: i32 = 7;
pub const CD_CREATE_TIME: i32 = 8;
pub const CD_ENABLED_KAFRA: i32 = 9;
pub const CD_ENABLED_GUARDIAN00: i32 = 10;
pub const CD_MAX: i32 = CD_ENABLED_GUARDIAN00 + MAX_GUARDIANS as i32;

/// script.cpp script_config: the event names of the War of Emperium (the defaults; conf/script_athena.conf does not
/// change them).
pub const GUILD_BREAK_EVENT_NAME: &str = "OnGuildBreak";
pub const AGIT_START_EVENT_NAME: &str = "OnAgitStart";
pub const AGIT_INIT_EVENT_NAME: &str = "OnAgitInit";
pub const AGIT_END_EVENT_NAME: &str = "OnAgitEnd";
pub const AGIT_START2_EVENT_NAME: &str = "OnAgitStart2";
pub const AGIT_INIT2_EVENT_NAME: &str = "OnAgitInit2";
pub const AGIT_END2_EVENT_NAME: &str = "OnAgitEnd2";
pub const AGIT_START3_EVENT_NAME: &str = "OnAgitStart3";
pub const AGIT_INIT3_EVENT_NAME: &str = "OnAgitInit3";
pub const AGIT_END3_EVENT_NAME: &str = "OnAgitEnd3";

/// mmo.hpp EVENT_NAME_LENGTH (NPC_NAME_LENGTH + 2 + NAME_LENGTH + 1).
const EVENT_NAME_LENGTH: usize = crate::script::npc_run::EVENT_NAME_LENGTH;

/// The run-time data of one castle as the char server keeps and sends it (mmo.hpp struct guild_castle from `guild_id`
/// to `visibleC`, and guardian[].visible — a 1-bit field: whatever is stored keeps only its lowest bit).
#[derive(spacetimedb::SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct CastleData {
    pub castle_id: i32,
    pub guild_id: i32,
    pub economy: i32,
    pub defense: i32,
    /// `triggerE`: times the economy was invested in today.
    pub trigger_e: i32,
    /// `triggerD`: times the defense was invested in today.
    pub trigger_d: i32,
    pub next_time: i32,
    pub pay_time: i32,
    pub create_time: i32,
    /// `visibleC`: a Kafra was hired.
    pub visible_c: i32,
    /// guardian[MAX_GUARDIANS].visible.
    pub visible_g: Vec<bool>,
}

impl CastleData {
    pub fn empty(castle_id: i32) -> CastleData {
        CastleData { castle_id, visible_g: vec![false; MAX_GUARDIANS], ..Default::default() }
    }
}

/// The map server's run-time half of castle_db (mmo.hpp struct guild_castle from `guild_id` on): the castle data, the
/// object ids of its permanent guardians (guardian[].id, 0 = none) and its temporary guardians (temp_guardians, whose
/// length is temp_guardians_max; a freed slot holds 0). A castle without a row has all of it zeroed, as castle_db's
/// entries are at the map server's start.
#[table(accessor = guild_castle_rt)]
#[derive(Clone, Debug)]
pub struct CastleRt {
    #[primary_key]
    pub castle_id: i32,
    pub data: CastleData,
    pub guardian_id: Vec<u32>,
    pub temp_guardians: Vec<u32>,
}

/// guild.cpp guild_flags / guild_flags_count: the NPCs flagemblem attached to a guild (npc ids; 0 = the nullptr
/// guild_flag_remove leaves). The vector's length is guild_flags_count.
#[table(accessor = guild_flags)]
#[derive(Clone, Debug)]
pub struct GuildFlags {
    #[primary_key]
    pub key: u8,
    pub flags: Vec<u32>,
}

/// npc_data u.scr.guild_id: the guild flagemblem set on a script NPC (no row = 0).
#[table(accessor = npc_guild)]
#[derive(Clone, Debug)]
pub struct NpcGuild {
    #[primary_key]
    pub npc_id: u32,
    pub guild_id: i32,
}

// ------------------------------------------------------------------------------------------------ castle_db order

/// castle_db's iteration order. CastleDatabase is a TypesafeYamlDatabase (an std::unordered_map<int32, …>), so every
/// `for (const auto &it : castle_db)` walks libstdc++'s bucket order of the keys inserted in the yml files' order;
/// datagen's YamlDb already writes the castles in that order (db::misc::castle).
pub fn castle_order() -> &'static [i32] {
    static ORDER: OnceLock<Vec<i32>> = OnceLock::new();
    ORDER.get_or_init(|| castle_db().iter().map(|gc| gc.castle_id).collect())
}

/// castle_db's entries in iteration order.
pub fn castles_in_order() -> Vec<&'static GuildCastle> {
    castle_order().iter().filter_map(|&id| castle_db().find(id)).collect()
}

/// CastleDatabase::find_by_clientid (first match in iteration order).
pub fn find_by_clientid(client_id: u16) -> Option<&'static GuildCastle> {
    castles_in_order().into_iter().find(|gc| gc.client_id == client_id)
}

/// CastleDatabase::mapindex2gc for a map of this server (castle maps are unique, so the order does not matter).
pub fn castle_of_map(m: u16) -> Option<&'static GuildCastle> {
    let name = crate::data::maps().get(m)?.name.clone();
    castle_db().mapname2gc(&name)
}

// ------------------------------------------------------------------------------------------------ run-time rows

/// The castle's run-time half (zeroed when the castle has none).
pub fn castle_rt(c: &Ctx, castle_id: i32) -> CastleRt {
    c.db().guild_castle_rt().castle_id().find(castle_id).unwrap_or(CastleRt {
        castle_id,
        data: CastleData::empty(castle_id),
        guardian_id: vec![0; MAX_GUARDIANS],
        temp_guardians: Vec::new(),
    })
}

pub fn put_castle_rt(c: &Ctx, rt: CastleRt) {
    if c.db().guild_castle_rt().castle_id().find(rt.castle_id).is_some() {
        c.db().guild_castle_rt().castle_id().update(rt);
    } else {
        c.db().guild_castle_rt().insert(rt);
    }
}

/// gc->guild_id.
pub fn castle_guild_id(c: &Ctx, castle_id: i32) -> i32 {
    castle_rt(c, castle_id).data.guild_id
}

/// A castle map's owner (0: not a castle, or no owner).
pub fn map_castle_owner(c: &Ctx, m: u16) -> i32 {
    castle_of_map(m).map(|gc| castle_guild_id(c, gc.castle_id)).unwrap_or(0)
}

/// The map server's start (npc.rs build_world, the module's restart): castle_db's run-time halves are zero again
/// (CastleDatabase::load makes new entries), the guild flag cache is empty (do_init_guild: guild_flags_count = 0; the
/// NPCs that had a guild are gone with the old world) and no War of Emperium runs (map.cpp's agit flags start false).
/// Called after the old world's monsters are freed.
pub fn on_world_rebuild(c: &Ctx) {
    for id in c.db().guild_castle_rt().iter().map(|r| r.castle_id).collect::<Vec<_>>() {
        c.db().guild_castle_rt().castle_id().delete(id);
    }
    for id in c.db().npc_guild().iter().map(|r| r.npc_id).collect::<Vec<_>>() {
        c.db().npc_guild().npc_id().delete(id);
    }
    // (unit_free of every monster already freed their guardian data: nothing of a monster outlives the old world)
    for id in c.db().guardian_data().iter().map(|g| g.id).collect::<Vec<_>>() {
        c.db().guardian_data().id().delete(id);
    }
    guild_flags_clear(c);
    set_agit(c, |s| {
        s.agit = false;
        s.agit2 = false;
        s.agit3 = false;
    });
}

// ------------------------------------------------------------------------------------------------ agit flags

fn set_agit(c: &Ctx, f: impl FnOnce(&mut crate::guild::AgitState)) {
    let mut s = c.db().agit_state().key().find(0).unwrap_or(crate::guild::AgitState { key: 0, agit: false, agit2: false, agit3: false });
    f(&mut s);
    if c.db().agit_state().key().find(0).is_some() {
        c.db().agit_state().key().update(s);
    } else {
        c.db().agit_state().insert(s);
    }
}

/// guild.cpp guild_agit_start: starts WoE FE and triggers every NPC's OnAgitStart.
pub fn guild_agit_start(c: &Ctx) -> bool {
    if crate::guild::agit_flag(c) {
        return false;
    }
    set_agit(c, |s| s.agit = true);
    crate::script::npc_run::npc_event_runall(c, AGIT_START_EVENT_NAME);
    true
}

/// guild.cpp guild_agit_end: ends WoE FE and triggers every NPC's OnAgitEnd.
pub fn guild_agit_end(c: &Ctx) -> bool {
    if !crate::guild::agit_flag(c) {
        return false;
    }
    set_agit(c, |s| s.agit = false);
    crate::script::npc_run::npc_event_runall(c, AGIT_END_EVENT_NAME);
    true
}

/// guild.cpp guild_agit2_start: starts WoE SE and triggers every NPC's OnAgitStart2.
pub fn guild_agit2_start(c: &Ctx) -> bool {
    if crate::guild::agit2_flag(c) {
        return false;
    }
    set_agit(c, |s| s.agit2 = true);
    crate::script::npc_run::npc_event_runall(c, AGIT_START2_EVENT_NAME);
    true
}

/// guild.cpp guild_agit2_end: ends WoE SE and triggers every NPC's OnAgitEnd2.
pub fn guild_agit2_end(c: &Ctx) -> bool {
    if !crate::guild::agit2_flag(c) {
        return false;
    }
    set_agit(c, |s| s.agit2 = false);
    crate::script::npc_run::npc_event_runall(c, AGIT_END2_EVENT_NAME);
    true
}

/// guild.cpp guild_agit3_start: starts WoE TE and triggers every NPC's OnAgitStart3.
pub fn guild_agit3_start(c: &Ctx) -> bool {
    if crate::guild::agit3_flag(c) {
        return false;
    }
    set_agit(c, |s| s.agit3 = true);
    crate::script::npc_run::npc_event_runall(c, AGIT_START3_EVENT_NAME);
    true
}

/// guild.cpp guild_agit3_end: ends WoE TE and triggers every NPC's OnAgitEnd3.
pub fn guild_agit3_end(c: &Ctx) -> bool {
    if !crate::guild::agit3_flag(c) {
        return false;
    }
    set_agit(c, |s| s.agit3 = false);
    crate::script::npc_run::npc_event_runall(c, AGIT_END3_EVENT_NAME);
    true
}

// ------------------------------------------------------------------------------------------------ castle data

/// guild.cpp guild_castle_map_init: asks the char server for every castle of castle_db (in its iteration order).
/// rAthena calls it from chrif_connectack right after OnInterIfInitOnce (npc_run.rs on_world_built).
pub fn guild_castle_map_init(c: &Ctx) {
    let castle_ids: Vec<i32> = castle_order().to_vec();
    let _ = c;
    // intif_guild_castle_dataload (0x3040): the char server is always connected here
    if !castle_ids.is_empty() {
        log::info!("Requested '{}' guild castles from char-server...", castle_ids.len());
        intif::send(Inter::GuildCastleDataLoad { castle_ids });
    }
}

/// intif.cpp intif_guild_castle_datasave (0x3041): WFIFOW(castle_id), WFIFOB(index), WFIFOL(value).
pub fn intif_guild_castle_datasave(castle_id: i32, index: i32, value: i32) {
    intif::send(Inter::GuildCastleDataSave { castle_id: castle_id as u16, index: index as u8, value });
}

/// guild.cpp guild_castledatasave: sets one field of a castle's run-time data with its side effects (a new owner
/// updates or removes the guardians, a new defense recalculates their status) and has the char server save it.
pub fn guild_castledatasave(c: &Ctx, castle_id: i32, index: i32, value: i32) -> i32 {
    if castle_db().find(castle_id).is_none() {
        log::warn!("guild_castledatasave: guild castle '{castle_id}' not found");
        return 0;
    }
    let mut rt = castle_rt(c, castle_id);
    match index {
        CD_GUILD_ID => {
            // The castle's owner has changed? Update or remove Guardians too. [Skotlex]
            rt.data.guild_id = value;
            put_castle_rt(c, rt);
            for i in 0..MAX_GUARDIANS {
                // (re-read: mob_guardian_guildchange may have saved the castle or freed a guardian)
                let rt = castle_rt(c, castle_id);
                if rt.data.visible_g[i] && rt.guardian_id[i] != 0 && crate::mob::get(c, rt.guardian_id[i]).is_some() {
                    guardian::mob_guardian_guildchange(c, rt.guardian_id[i]);
                }
            }
        }
        CD_CURRENT_ECONOMY => {
            rt.data.economy = value;
            put_castle_rt(c, rt);
        }
        CD_CURRENT_DEFENSE => {
            // defense invest change -> recalculate guardian hp
            rt.data.defense = value;
            put_castle_rt(c, rt);
            for i in 0..MAX_GUARDIANS {
                let rt = castle_rt(c, castle_id);
                if rt.data.visible_g[i] && rt.guardian_id[i] != 0 && crate::mob::get(c, rt.guardian_id[i]).is_some() {
                    crate::mob::status::status_calc_mob(c, rt.guardian_id[i], crate::status::SCO_NONE);
                }
            }
        }
        CD_INVESTED_ECONOMY => {
            rt.data.trigger_e = value;
            put_castle_rt(c, rt);
        }
        CD_INVESTED_DEFENSE => {
            rt.data.trigger_d = value;
            put_castle_rt(c, rt);
        }
        CD_NEXT_TIME => {
            rt.data.next_time = value;
            put_castle_rt(c, rt);
        }
        CD_PAY_TIME => {
            rt.data.pay_time = value;
            put_castle_rt(c, rt);
        }
        CD_CREATE_TIME => {
            rt.data.create_time = value;
            put_castle_rt(c, rt);
        }
        CD_ENABLED_KAFRA => {
            rt.data.visible_c = value;
            put_castle_rt(c, rt);
        }
        _ => {
            if (CD_ENABLED_GUARDIAN00..CD_MAX).contains(&index) {
                // guardian[].visible is `unsigned visible : 1`
                rt.data.visible_g[(index - CD_ENABLED_GUARDIAN00) as usize] = value & 1 != 0;
                put_castle_rt(c, rt);
            } else {
                log::warn!("guild_castledatasave: index = '{index}' is out of allowed range");
                return 0;
            }
        }
    }
    intif_guild_castle_datasave(castle_id, index, value);
    0
}

/// guild.cpp guild_castledataloadack: the castles arrived from the char server. Without an owned castle, OnAgitInit /
/// 2 / 3 run at once and nothing is copied (the castles' data stays zero on the map server); otherwise each castle's
/// data replaces the map server's (the memcpy from `guild_id` on, which also clears the guardian ids), the owners'
/// guilds are requested, and the last owned castle in the list runs OnAgitInit / 2 / 3 once its guild's info is here.
///
/// At this point of a real start rAthena's guild_db is empty, so guild_npc_request_info always takes its request
/// branch; the module's guild_db outlives a world rebuild (players stay connected), so the request branch is taken
/// explicitly ([`crate::guild::guild_npc_queue_info_event`]) — calling guild_npc_request_info with a guild already
/// loaded would run npc_event_doall("::OnAgitInit"), which matches no event.
pub fn guild_castledataloadack(c: &Ctx, castles: &[CastleData]) {
    let n = castles.len();
    // Last owned castle in the list invokes ::OnAgitInit
    let ev = castles.iter().rposition(|gc| gc.guild_id != 0);
    match ev {
        None => {
            // No castles owned, invoke OnAgitInit as it is.
            crate::script::npc_run::npc_event_doall(c, AGIT_INIT_EVENT_NAME);
            crate::script::npc_run::npc_event_doall(c, AGIT_INIT2_EVENT_NAME);
            crate::script::npc_run::npc_event_doall(c, AGIT_INIT3_EVENT_NAME);
        }
        Some(ev) => {
            // load received castles into memory, one by one
            for (i, gc) in castles.iter().enumerate() {
                if castle_db().find(gc.castle_id).is_none() {
                    log::error!("guild_castledataloadack: castle id={} not found.", gc.castle_id);
                    continue;
                }
                // update map-server castle data with new info
                let mut rt = castle_rt(c, gc.castle_id);
                rt.data = CastleData { castle_id: gc.castle_id, ..gc.clone() };
                rt.guardian_id = vec![0; MAX_GUARDIANS];
                rt.temp_guardians = Vec::new();
                let gid = rt.data.guild_id;
                put_castle_rt(c, rt);
                if gid != 0 {
                    if i != ev {
                        crate::guild::guild_request_info(gid);
                    } else {
                        // last owned one
                        for name in [AGIT_INIT_EVENT_NAME, AGIT_INIT2_EVENT_NAME, AGIT_INIT3_EVENT_NAME] {
                            let event_name: String = format!("::{name}").chars().take(EVENT_NAME_LENGTH - 1).collect();
                            crate::guild::guild_npc_queue_info_event(c, gid, &event_name);
                        }
                    }
                }
            }
        }
    }
    log::info!("Received '{n}' guild castles from char-server.");
}

/// guild.cpp guild_checkcastles: how many castles the guild owns.
pub fn guild_checkcastles(c: &Ctx, guild_id: i32) -> i32 {
    castles_in_order().iter().filter(|gc| castle_guild_id(c, gc.castle_id) == guild_id).count() as i32
}

/// guild.cpp castle_guild_broken_sub: every castle of a broken guild runs its `<castle_event>::OnGuildBreak` and
/// gets owner 0 (which removes its guardians).
pub fn castle_guild_broken_sub(c: &Ctx, guild_id: i32) {
    for gc in castles_in_order() {
        if castle_guild_id(c, gc.castle_id) != guild_id {
            continue;
        }
        // We call castle_event::OnGuildBreak of all castles of the guild
        let name: String = format!("{}::{}", gc.castle_event, GUILD_BREAK_EVENT_NAME).chars().take(EVENT_NAME_LENGTH - 1).collect();
        crate::script::npc_run::npc_event_do(c, &name);
        // Save the new 'owner', this should invoke guardian clean up and other such things.
        guild_castledatasave(c, gc.castle_id, CD_GUILD_ID, 0);
    }
}

/// guild.cpp guild_emblem_changed's second half: the permanent and temporary guardians of the guild's castles and the
/// NPCs flagemblem attached to it show the new emblem (ZC_CHANGE_GUILD to their area).
pub fn guild_emblem_changed_castles(c: &Ctx, guild_id: i32, emblem_id: i32) {
    // update guardians (mobs)
    for gc in castles_in_order() {
        let rt = castle_rt(c, gc.castle_id);
        if rt.data.guild_id != guild_id {
            continue;
        }
        // update permanent guardians, then temporary guardians
        for id in rt.guardian_id.iter().copied().chain(rt.temp_guardians.iter().copied()) {
            if id == 0 {
                continue;
            }
            let Some(mut gd) = guardian::get(c, id) else { continue };
            gd.emblem_id = emblem_id;
            guardian::put(c, &gd);
            if let Some(u) = crate::unit::get(c, id) {
                crate::clif_guild::clif_guild_emblem_area(c, &u);
            }
        }
    }
    // update npcs (flags or other npcs that used flagemblem to attach to this guild)
    let flags = guild_flags_list(c);
    for id in flags {
        if id != 0 && npc_guild_id(c, id) == guild_id {
            if let Some(u) = crate::unit::get(c, id) {
                crate::clif_guild::clif_guild_emblem_area(c, &u);
            }
        }
    }
}

// ------------------------------------------------------------------------------------------------ guild flags

fn guild_flags_list(c: &Ctx) -> Vec<u32> {
    c.db().guild_flags().key().find(0).map(|r| r.flags).unwrap_or_default()
}

fn put_guild_flags(c: &Ctx, flags: Vec<u32>) {
    let row = GuildFlags { key: 0, flags };
    if c.db().guild_flags().key().find(0).is_some() {
        c.db().guild_flags().key().update(row);
    } else {
        c.db().guild_flags().insert(row);
    }
}

/// guild.cpp guild_flag_add.
pub fn guild_flag_add(c: &Ctx, npc_id: u32) {
    let mut flags = guild_flags_list(c);
    // check
    if flags.iter().any(|&f| f != 0 && f == npc_id) {
        return; // exists, most likely updated the id.
    }
    flags.push(npc_id);
    put_guild_flags(c, flags);
}

/// guild.cpp guild_flag_remove: the NPC's slot is cleared and the list compacted pointer by pointer; guild_flags_count
/// does not shrink, so the last entries stay where they were (a compacted list of 3 with one removed reads A, C, C).
pub fn guild_flag_remove(c: &Ctx, npc_id: u32) {
    let mut flags = guild_flags_list(c);
    if flags.is_empty() {
        return;
    }
    flag_list_remove(&mut flags, npc_id);
    put_guild_flags(c, flags);
}

/// guild_flag_remove's list work: clear the NPC's slot, then move every remaining pointer down (memmove of one
/// pointer each) without clearing the tail.
fn flag_list_remove(flags: &mut [u32], npc_id: u32) {
    // find it
    if let Some(i) = flags.iter().position(|&f| f != 0 && f == npc_id) {
        flags[i] = 0; // found
    }
    // compact list
    let mut cursor = 0;
    for i in 0..flags.len() {
        if flags[i] == 0 {
            continue;
        }
        if cursor != i {
            flags[cursor] = flags[i];
        }
        cursor += 1;
    }
}

/// guild.cpp guild_flags_clear (npc_reload; do_init_guild's guild_flags_count = 0).
pub fn guild_flags_clear(c: &Ctx) {
    put_guild_flags(c, Vec::new());
}

/// npc.cpp npc_unload's script part: `if( nd->u.scr.guild_id ) guild_flag_remove(nd);` (the npc_data goes, so its
/// guild id with it).
pub fn npc_unload(c: &Ctx, nd: &crate::npc::Npc) {
    if nd.subtype == crate::npc::NPCTYPE_SCRIPT && npc_guild_id(c, nd.id) != 0 {
        guild_flag_remove(c, nd.id);
    }
    c.db().npc_guild().npc_id().delete(nd.id);
}

/// nd->u.scr.guild_id.
pub fn npc_guild_id(c: &Ctx, npc_id: u32) -> i32 {
    c.db().npc_guild().npc_id().find(npc_id).map(|r| r.guild_id).unwrap_or(0)
}

pub fn set_npc_guild_id(c: &Ctx, npc_id: u32, guild_id: i32) {
    let row = NpcGuild { npc_id, guild_id };
    if c.db().npc_guild().npc_id().find(npc_id).is_some() {
        c.db().npc_guild().npc_id().update(row);
    } else {
        c.db().npc_guild().insert(row);
    }
}

// ------------------------------------------------------------------------------------------------ unit packets

/// The guild id and emblem id a unit packet carries (clif.cpp clif_set_unit_idle / clif_spawn_unit /
/// clif_set_unit_walking: status_get_guild_id / status_get_emblem_id at send time). Players carry their unit row's
/// mirror of sd->status.guild_id / sd->guild_emblem_id; script NPCs the guild flagemblem gave them (and that guild's
/// current emblem, 0 while the guild is not loaded); monsters their guardian data or a special-AI master's guild.
pub fn unit_guild_ids(c: &Ctx, u: &Unit) -> (i32, i32) {
    match u.kind {
        crate::map::BL_PC => (u.guild_id as i32, u.emblem_id),
        // (BL_PET / BL_HOM / BL_MER / BL_ELEM: the master's guild and emblem, status_get_guild_id /
        // status_get_emblem_id's companion arms; clif.cpp:1048 / 1081 read them for every unit kind. A pet's are
        // status.cpp:9168-9171 and 9218-9221: without BL_PET here a guild member's pet was sent with guild id 0,
        // found by tools/host/proof's look on rAthena, wave I icut)
        crate::map::BL_NPC | crate::map::BL_MOB | crate::map::BL_PET | crate::map::BL_HOM | crate::map::BL_MER | crate::map::BL_ELEM => {
            (crate::battle::target::status_get_guild_id(c, u), crate::battle::target::status_get_emblem_id(c, u))
        }
        _ => (u.guild_id as i32, u.emblem_id),
    }
}

// ------------------------------------------------------------------------------------------------ pc.cpp

/// pc.cpp pc_setpos, on a map change: `if (sd->regen.state.gc) sd->regen.state.gc = 0;`
pub fn pc_setpos_changemap(c: &Ctx, aid: u32) {
    crate::status::data::with_sd(c, aid, |sd| {
        if sd.regen.gc {
            sd.regen.gc = false;
        }
    });
}

/// pc.cpp pc_setpos: increased guild castle regen [Valaris] for a member of the guild that owns the castle.
pub fn pc_setpos_castle_regen(c: &Ctx, aid: u32, m: u16) {
    let guild_id = crate::guild::guild_id_of(c, aid);
    if guild_id > 0 && crate::npc::mapflag(c, m, crate::npc::MF_GVG_CASTLE) {
        if let Some(gc) = castle_of_map(m) {
            if castle_guild_id(c, gc.castle_id) == guild_id {
                crate::status::data::with_sd(c, aid, |sd| sd.regen.gc = true);
            }
        }
    }
}

// ------------------------------------------------------------------------------------------------ intif

/// intif.rs dispatch of the castle messages (both directions).
pub fn intif_dispatch(c: &Ctx, m: Inter) {
    match m {
        Inter::GuildCastleDataLoad { castle_ids } => int_castle::mapif_parse_guild_castle_data_load(c, &castle_ids),
        Inter::GuildCastleDataSave { castle_id, index, value } => {
            int_castle::mapif_parse_guild_castle_data_save(c, castle_id as i32, index as i32, value);
        }
        Inter::GuildCastleDataLoadAck { castles } => guild_castledataloadack(c, &castles),
        _ => {}
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// castle_db's 44 castles (ids 0-43 inserted in order) in libstdc++'s order, as a GCC 13.3 build of
    /// `std::unordered_map<int32_t, …>` with operator[] inserts prints it (the oracle's compiler).
    #[test]
    fn castle_iteration_order() {
        let want: Vec<i32> = vec![
            43, 42, 41, 40, 39, 38, 37, 36, 35, 34, 33, 32, 31, 30, 29, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 13, 14, 15, 16,
            17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28,
        ];
        assert_eq!(castle_order(), want.as_slice());
    }

    /// guild_flag_remove compacts without shrinking guild_flags_count: the tail keeps its old pointers.
    #[test]
    fn flag_list_compaction() {
        let mut flags = vec![5u32, 6, 7];
        flag_list_remove(&mut flags, 6);
        assert_eq!(flags, vec![5, 7, 7]);
        flag_list_remove(&mut flags, 5);
        assert_eq!(flags, vec![7, 7, 7]);
        // an NPC that is not in the list only compacts
        let mut f2 = vec![0u32, 8, 9];
        flag_list_remove(&mut f2, 4);
        assert_eq!(f2, vec![8, 9, 9]);
    }
}
