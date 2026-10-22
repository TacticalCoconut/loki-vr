//! The map_session_data / npc_data / unit_data fields the general script commands (script2) own, kept in their own
//! rows so no other system's writes collide with them, and the pc.cpp functions that go with them: progress bars
//! (sd->progressbar, nd->progressbar; sd->state.workinprogress is itemui's `pc_itemui` row), hat effects
//! (ud->hatEffects), the save point (pc_setsavepoint), night (night_flag, sd->state.night).
//!
//! Every row is dropped when the unit is freed ([`unit_free`], called from pc::quit's unit_free part), as rAthena's
//! map_session_data / unit_data die with the character.

use spacetimedb::{table, Table};

use crate::world::{Ctx, Tick};

/// map_session_data fields (pc.hpp) that belong to the script2 commands.
#[table(accessor = pc_misc)]
#[derive(Clone, Debug, Default)]
pub struct PcMisc {
    #[primary_key]
    pub account_id: u32,
    /// sd->progressbar.npc_id / .timeout (pc.hpp struct s_progressbar).
    pub progressbar_npc_id: u32,
    pub progressbar_timeout: Tick,
    /// sd->state.night: the night effect (EFST_SKE) is shown.
    pub night: bool,
    /// sd->state.autoloot: the autoloot rate (0 off, 1-10000 = 0.01%-100%) set by @autoloot / the autoloot command;
    /// monster drops (mob agent) read it.
    pub autoloot: i32,
    /// sd->followtimer (0 = INVALID_TIMER) / sd->followtarget (pc_follow; -1 after pc_stop_following).
    pub followtimer: u64,
    pub followtarget: i32,
    /// sd->permissions changed by permission_add / permission_remove since login: bits set / bits cleared over the
    /// group's permissions (pc_group_pc_load).
    pub perm_set: u64,
    pub perm_reset: u64,
    /// sd->adopt_invite: the account id of the parent whose adoption request is pending (adopt / CZ_REQ_JOIN_BABY).
    pub adopt_invite: u32,
}

/// map.cpp's `night_flag` (0 day, 1 night) and the day / night timers (pc.cpp do_init_pc). One row, key 0.
#[table(accessor = daynight)]
#[derive(Clone, Debug, Default)]
pub struct DayNight {
    #[primary_key]
    pub key: u8,
    pub night_flag: bool,
}

/// conf/battle/misc.conf `night_at_start: no`, `day_duration: 0`, `night_duration: 0`: no day / night timers are
/// armed (do_init_pc adds them only when both durations are > 0), so only @day / @night and the script commands switch.
pub fn night_at_start() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::NIGHT_AT_START) != 0
}

pub fn night_flag(c: &Ctx) -> bool {
    c.db().daynight().key().find(0).map(|d| d.night_flag).unwrap_or(night_at_start())
}

fn set_night_flag(c: &Ctx, v: bool) {
    let row = DayNight { key: 0, night_flag: v };
    if c.db().daynight().key().find(0).is_some() {
        c.db().daynight().key().update(row);
    } else {
        c.db().daynight().insert(row);
    }
}

/// pc.cpp pc_daynight_timer_sub: a player on a nightenabled map whose night state differs sees the change.
fn daynight_sub(c: &Ctx, aid: u32, night: bool) {
    let Some(u) = crate::unit::get(c, aid) else { return };
    let mut m = pcm(c, aid);
    if m.night != night && crate::npc::mapflag(c, u.map, MF_NIGHTENABLED) {
        crate::pc::status_icon(c, &u, efst_ske(), night); // New night effect by dynamix [Skotlex]
        m.night = night;
        put_pcm(c, &m);
    }
}

/// map.hpp MF_NIGHTENABLED.
pub const MF_NIGHTENABLED: i16 = 28;

fn efst_ske() -> u16 {
    crate::data::constant("EFST_SKE").unwrap_or(0) as u16
}

/// Every online player (map_foreachpc: pc_db, DBIterator order).
fn all_pcs(c: &Ctx) -> Vec<u32> {
    let mut v: Vec<u32> = c.db().pc().iter().map(|p| p.account_id).collect();
    v.sort_by_key(|&a| crate::party::idb_order(a));
    v
}
use crate::pc::pc as _;

/// pc.cpp map_day_timer (data 1: called by @day / the day script command). Returns whether it switched.
pub fn map_day_timer(c: &Ctx, data: i64) -> bool {
    // (data == 0 && day_duration <= 0: no timer exists here)
    if !night_flag(c) {
        return false; // Already day.
    }
    set_night_flag(c, false); // 0=day, 1=night [Yor]
    for aid in all_pcs(c) {
        daynight_sub(c, aid, false);
    }
    let msg = crate::data::msg_txt(if data == 0 { 502 } else { 60 }); // The day has arrived!
    let b = crate::net::wire::latin1(msg);
    crate::clif::broadcast(c, None, &b, b.len() + 1, 0, crate::clif::Target::AllClient); // intif_broadcast(BC_DEFAULT)
    true
}

/// pc.cpp map_night_timer.
pub fn map_night_timer(c: &Ctx, data: i64) -> bool {
    if night_flag(c) {
        return false; // Already night.
    }
    set_night_flag(c, true);
    for aid in all_pcs(c) {
        daynight_sub(c, aid, true);
    }
    let msg = crate::data::msg_txt(if data == 0 { 503 } else { 59 }); // The night has fallen...
    let b = crate::net::wire::latin1(msg);
    crate::clif::broadcast(c, None, &b, b.len() + 1, 0, crate::clif::Target::AllClient);
    true
}

/// clif_parse_LoadEndAck, first load: "Display night" on a nightenabled map at night.
pub fn loadendack_night_connect(c: &Ctx, aid: u32) {
    let Some(u) = crate::unit::get(c, aid) else { return };
    if night_flag(c) && crate::npc::mapflag(c, u.map, MF_NIGHTENABLED) {
        let mut m = pcm(c, aid);
        m.night = true;
        put_pcm(c, &m);
        crate::pc::status_icon(c, &u, efst_ske(), true);
    }
}

/// clif_parse_LoadEndAck on a map change: show or clear the night display.
pub fn loadendack_night_changemap(c: &Ctx, aid: u32) {
    let Some(u) = crate::unit::get(c, aid) else { return };
    let mut m = pcm(c, aid);
    if night_flag(c) && crate::npc::mapflag(c, u.map, MF_NIGHTENABLED) {
        // Display night.
        if !m.night {
            m.night = true;
            put_pcm(c, &m);
            crate::pc::status_icon(c, &u, efst_ske(), true);
        }
    } else if m.night {
        // Clear night display.
        m.night = false;
        put_pcm(c, &m);
        crate::pc::status_icon(c, &u, efst_ske(), false);
    }
}

/// npc_data fields (npc.hpp) that belong to the script2 commands.
#[table(accessor = npc_misc)]
#[derive(Clone, Debug, Default)]
pub struct NpcMisc {
    #[primary_key]
    pub npc_id: u32,
    /// nd->progressbar.timeout / .color (npc.hpp).
    pub progressbar_timeout: Tick,
    pub progressbar_color: u32,
    /// nd->level, nd->stat_point, nd->params (str … luk): what setunitdata gives an NPC's status.
    pub level: u32,
    pub stat_point: u32,
    pub params: Vec<u16>,
    /// nd->status: status_calc_npc's result (zeroed until a command first calculates it, as rAthena's npc_data).
    pub status: crate::status::data::StatusData,
    /// nd->master_nd of a shop (npcshopattach): the NPC whose OnBuyItem / OnSellItem decide (0: none).
    pub shop_master: u32,
}

/// unit_data::hatEffects (unit.hpp): the hat effects a unit shows, in the order they were enabled.
#[table(accessor = unit_hat)]
#[derive(Clone, Debug, Default)]
pub struct UnitHat {
    #[primary_key]
    pub id: u32,
    pub effects: Vec<i16>,
}

/// pc.hpp e_idletime_option IDLE_NPC_PROGRESS.
pub const IDLE_NPC_PROGRESS: i32 = 0x4000;

pub fn pcm(c: &Ctx, aid: u32) -> PcMisc {
    c.db().pc_misc().account_id().find(aid).unwrap_or(PcMisc { account_id: aid, ..Default::default() })
}

pub fn put_pcm(c: &Ctx, m: &PcMisc) {
    if c.db().pc_misc().account_id().find(m.account_id).is_some() {
        c.db().pc_misc().account_id().update(m.clone());
    } else {
        c.db().pc_misc().insert(m.clone());
    }
}

pub fn npcm(c: &Ctx, npc_id: u32) -> NpcMisc {
    c.db().npc_misc().npc_id().find(npc_id).unwrap_or(NpcMisc { npc_id, ..Default::default() })
}

pub fn put_npcm(c: &Ctx, m: &NpcMisc) {
    if c.db().npc_misc().npc_id().find(m.npc_id).is_some() {
        c.db().npc_misc().npc_id().update(m.clone());
    } else {
        c.db().npc_misc().insert(m.clone());
    }
}

pub fn hat_effects(c: &Ctx, id: u32) -> Vec<i16> {
    c.db().unit_hat().id().find(id).map(|h| h.effects).unwrap_or_default()
}

pub fn set_hat_effects(c: &Ctx, id: u32, effects: Vec<i16>) {
    if effects.is_empty() {
        c.db().unit_hat().id().delete(id);
    } else if c.db().unit_hat().id().find(id).is_some() {
        c.db().unit_hat().id().update(UnitHat { id, effects });
    } else {
        c.db().unit_hat().insert(UnitHat { id, effects });
    }
}

/// unit.cpp unit_free (`ud->hatEffects.clear()`) and the end of the map_session_data: the rows of a unit that leaves
/// the world.
pub fn unit_free(c: &Ctx, id: u32) {
    // unit.cpp unit_free (BL_PC): if (sd->followtimer != INVALID_TIMER) pc_stop_following(sd);
    let t = pcm(c, id).followtimer;
    if t != 0 {
        crate::timer::delete(c, t);
    }
    c.db().pc_misc().account_id().delete(id);
    c.db().unit_hat().id().delete(id);
}

/// clif_authfail_fd(fd, code) for a player the server throws out (chrif_changesex): SC_NOTIFY_BAN, then set_eof —
/// nothing more reaches that client, not even what map_quit sends — and clif_parse's eof handling after the current
/// work (clif_quitsave: map_quit, or clif_delayquit 10 s later while prevent_logout holds). crate::session.
pub fn authfail_kick(c: &Ctx, aid: u32, code: u8) {
    crate::session::clif_authfail_fd(c, aid, code);
}

/// pc.hpp pc_has_permission: sd->permissions — the group's permissions with the session's permission_add /
/// permission_remove changes. Every system checks a player's permissions through this (crate::pc::has_permission).
pub fn pc_has_permission(c: &Ctx, aid: u32, perm: u32) -> bool {
    let m = pcm(c, aid);
    let bit = 1u64 << perm;
    if m.perm_reset & bit != 0 {
        return false;
    }
    if m.perm_set & bit != 0 {
        return true;
    }
    crate::pc::get(c, aid).map(|p| crate::rules::has_permission(p.group_id, perm)).unwrap_or(false)
}

/// npc.cpp npc_unload(nd, true) frees the npc_data: its script2 fields (status, progress bar, hat effects) and, with
/// npc_chat_finalize, its chat pattern sets go with it (the instance system unloads its NPC copies).
pub fn npc_unload(c: &Ctx, npc_id: u32) {
    c.db().npc_misc().npc_id().delete(npc_id);
    c.db().unit_hat().id().delete(npc_id);
    for id in c.db().npc_chat_set().iter().filter(|s| s.npc_id == npc_id).map(|s| s.id).collect::<Vec<_>>() {
        c.db().npc_chat_set().id().delete(id);
    }
}

/// The world is rebuilt (rAthena restarts): the old NPCs' rows go with them, and bindatcmd's bindings.
pub fn on_world_rebuilt(c: &Ctx, old_npcs: &[u32]) {
    crate::script::buildins::script2::admin::on_world_rebuilt(c);
    crate::script::buildins::script2::npcchat::on_world_rebuilt(c);
    for id in c.db().npc_misc().iter().map(|m| m.npc_id).collect::<Vec<_>>() {
        c.db().npc_misc().npc_id().delete(id);
    }
    for &id in old_npcs {
        c.db().unit_hat().id().delete(id);
    }
}

/// The player has a progress bar running (`sd->progressbar.npc_id != 0`): the first walk / item use / damage
/// cancels it with clif_progressbar_abort. Returns true when a bar was running (and the abort was sent).
pub fn progressbar_abort_if_running(c: &Ctx, aid: u32) -> bool {
    if pcm(c, aid).progressbar_npc_id == 0 {
        return false;
    }
    if let Some(sd) = crate::unit::get(c, aid) {
        crate::clif_misc::progressbar_abort(c, &sd);
    }
    true
}

/// npc.cpp npc_scriptcont: "WPE can get to this point with a progressbar; we deny it."
pub fn progressbar_blocks_scriptcont(c: &Ctx, aid: u32) -> bool {
    let m = pcm(c, aid);
    m.progressbar_npc_id != 0 && m.progressbar_timeout - c.tick() > 0
}

/// clif.cpp clif_parse_NpcClicked: "Progressbar is running" on the NPC (nd->progressbar.timeout > 0).
pub fn npc_progressbar_running(c: &Ctx, npc_id: u32) -> bool {
    npcm(c, npc_id).progressbar_timeout > 0
}

/// clif.cpp clif_parse_progressbar (CZ_PROGRESS 0x02f1): the client's progress bar reached 100% (or was cancelled).
pub fn parse_progressbar(c: &Ctx, aid: u32) {
    let mut m = pcm(c, aid);
    // No progressbar active, ignore it
    if m.progressbar_npc_id == 0 {
        return;
    }
    let npc_id = m.progressbar_npc_id;
    let mut closing = false;
    // Check if the progress was canceled
    let st_id = crate::script::npc_run::sdx(c, aid).st;
    if c.tick() < m.progressbar_timeout && st_id != 0 {
        if let Some(mut st) = crate::script::state::load(c, st_id) {
            closing = true;
            st.state = crate::script::vm::CLOSE; // will result in END in npc_scriptcont
            // If a message window was open, offer a close button to the user
            if st.mes_active {
                if let Some(sd) = crate::unit::get(c, aid) {
                    crate::clif::scriptclose(c, &sd, npc_id);
                }
            }
            crate::script::state::save(c, &st);
        }
    }
    m.progressbar_npc_id = 0;
    m.progressbar_timeout = 0;
    put_pcm(c, &m);
    crate::itemui::set_workinprogress(c, aid, crate::itemui::WIP_DISABLE_NONE);
    if crate::conf::idletime_option() & IDLE_NPC_PROGRESS != 0 {
        if let Some(mut p) = crate::pc::get(c, aid) {
            p.idletime = crate::pc::last_tick(c);
            crate::pc::put(c, &p);
        }
    }
    crate::script::npc_run::npc_scriptcont(c, aid, npc_id, closing);
}

/// pc.cpp pc_setsavepoint: `sd->status.save_point` = (mapindex_id2name(mapindex), x, y). The character row is the
/// saved status here, so the point is written at once (rAthena saves it with the next chrif_save).
pub fn pc_setsavepoint(c: &Ctx, aid: u32, map: u16, x: i32, y: i32) {
    let Some(mut ch) = crate::pc::character_of(c, aid) else { return };
    // safestrncpy(save_point.map, mapindex_id2name(mapindex), MAP_NAME_LENGTH_EXT)
    ch.save_map = crate::data::maps().get(map).map(|d| d.name.clone()).unwrap_or_default();
    // save_point.x / .y are int16
    ch.save_x = x as i16 as u16;
    ch.save_y = y as i16 as u16;
    c.db().character().id().update(ch);
}

use crate::character::character as _;

// ---------------------------------------------------------------------------------------------- script2 admin tables

// (log.cpp's `npclog` table, what logmes writes, is log.rs's: crate::map_log::NpcLog)

/// script.cpp's atcmd_binding[] (struct atcmd_binding_data): commands bound to NPC events by bindatcmd, in array
/// order (`pos`). Server state: emptied when the world is rebuilt (a server restart).
#[table(accessor = atcmd_binding)]
#[derive(Clone, Debug)]
pub struct AtcmdBinding {
    /// The array index order (entries are compacted in place when one is removed).
    #[primary_key]
    pub pos: u32,
    /// safestrncpy(command, atcmd, 50).
    pub command: String,
    pub npc_event: String,
    pub level: i32,
    pub level2: i32,
}


// ---------------------------------------------------------------------------------------------- memo points

/// mmo.hpp MAX_MEMOPOINTS.
pub const MAX_MEMOPOINTS: usize = 3;

/// One sd->status.memo_point[] (struct s_point_str; map "" = void).
#[derive(spacetimedb::SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct MemoPoint {
    pub map: String,
    pub x: i16,
    pub y: i16,
}

/// The char server's `memo` table: a character's Warp Portal memo points (MAX_MEMOPOINTS, in order).
#[table(accessor = char_memo)]
#[derive(Clone, Debug)]
pub struct CharMemo {
    #[primary_key]
    pub char_id: u32,
    pub points: Vec<MemoPoint>,
}

/// sd->status.memo_point of a character (always MAX_MEMOPOINTS entries).
pub fn memo_points(c: &Ctx, char_id: u32) -> Vec<MemoPoint> {
    let mut v = c.db().char_memo().char_id().find(char_id).map(|m| m.points).unwrap_or_default();
    v.resize(MAX_MEMOPOINTS, MemoPoint::default());
    v
}

pub fn put_memo_points(c: &Ctx, char_id: u32, points: Vec<MemoPoint>) {
    let row = CharMemo { char_id, points };
    if c.db().char_memo().char_id().find(char_id).is_some() {
        c.db().char_memo().char_id().update(row);
    } else {
        c.db().char_memo().insert(row);
    }
}

/// clif.cpp clif_skill_teleportmessage: ZC_NOTIFY_MAPINFO (0x0189) type.W.
pub fn clif_skill_teleportmessage(c: &Ctx, aid: u32, kind: u16) {
    if let Some(sd) = crate::unit::get(c, aid) {
        let mut w = Vec::with_capacity(4);
        w.extend_from_slice(&0x0189u16.to_le_bytes());
        w.extend_from_slice(&kind.to_le_bytes());
        crate::clif::send(c, &w, &sd, crate::clif::Target::SelfOnly);
    }
}

/// clif.cpp clif_skill_memomessage: ZC_ACK_REMEMBER_WARPPOINT (0x011e) type.B (0 success, 1 skill level too low,
/// 2 Warp not learned).
pub fn clif_skill_memomessage(c: &Ctx, aid: u32, result: u8) {
    if let Some(sd) = crate::unit::get(c, aid) {
        crate::clif::send(c, &[0x1e, 0x01, result], &sd, crate::clif::Target::SelfOnly);
    }
}

/// pc.cpp pc_memo: memorizes the current map at `pos` (-1: the first slot, shifting the others down unless the map is
/// already memorized); needs Warp Portal level pos + 2.
pub fn pc_memo(c: &Ctx, aid: u32, pos: i32) -> bool {
    use crate::npc::{mapflag, MF_NOMEMO, MF_NOWARPTO};
    let Some(sd) = crate::unit::get(c, aid) else { return false };
    let Some(ch) = crate::pc::character_of(c, aid) else { return false };
    // check mapflags
    if (mapflag(c, sd.map, MF_NOMEMO) || mapflag(c, sd.map, MF_NOWARPTO)) && !pc_has_permission(c, aid, crate::rules::PC_PERM_WARP_ANYWHERE) {
        clif_skill_teleportmessage(c, aid, 1); // NOTIFY_MAPINFO_CANT_MEMO: "Saved point cannot be memorized."
        return false;
    }
    // check inputs
    if pos < -1 || pos >= MAX_MEMOPOINTS as i32 {
        return false; // invalid input
    }
    // check required skill level
    let skill = crate::skill::checkskill(c, aid, crate::db::skills::ids::AL_WARP) as i32;
    if skill < 1 {
        clif_skill_memomessage(c, aid, 2); // WARPPOINT_NOT_LEARNED: "You haven't learned Warp."
        return false;
    }
    if skill < 2 || skill - 2 < pos {
        clif_skill_memomessage(c, aid, 1); // WARPPOINT_LOW_LEVEL: "Skill Level is not high enough."
        return false;
    }
    // map_mapid2mapname(sd->m): an instance map's source map
    let mapname = crate::instance_map::map_mapid2mapname(c, sd.map as i32);
    let mut points = memo_points(c, ch.id);
    let mut pos = pos;
    if pos == -1 {
        // prevent memo-ing the same map multiple times
        let i = points.iter().position(|p| p.map == mapname).unwrap_or(MAX_MEMOPOINTS);
        let n = i.min(MAX_MEMOPOINTS - 1);
        // memmove(&memo_point[1], &memo_point[0], n entries)
        for k in (0..n).rev() {
            points[k + 1] = points[k].clone();
        }
        pos = 0;
    }
    if crate::instance_map::map_instance_id(c, sd.map) != 0 {
        // the memmove above already shifted sd->status.memo_point: it stays shifted
        put_memo_points(c, ch.id, points);
        crate::clif::displaymessage(c, &sd, crate::data::msg_txt(384)); // You cannot create a memo in an instance.
        return false;
    }
    points[pos as usize] = MemoPoint { map: mapname, x: sd.x, y: sd.y };
    put_memo_points(c, ch.id, points);
    clif_skill_memomessage(c, aid, 0); // WARPPOINT_SUCCESS
    true
}

/// clif.cpp clif_parse_RequestMemo (CZ_REMEMBER_WARPPOINT 0x011d).
pub fn clif_parse_request_memo(c: &Ctx, aid: u32) {
    if !crate::pc::isdead(c, aid) {
        pc_memo(c, aid, -1);
    }
}

/// pc.cpp pc_lastpoint_special at login: Some(new last point) when the last map needs special handling — a nosave
/// map sends the player to its return point, or to the save point ("SavePoint"); the jail map never does; a last point
/// in an instance (last_point_instanceid) becomes that instance's copy of the map, or the save point when the
/// instance is gone or no longer the character's (instance.rs lastpoint_special).
pub fn pc_lastpoint_special(c: &Ctx, ch: &crate::character::Character) -> Option<(String, i16, i16)> {
    let Some(m) = crate::data::maps().id_of(&ch.last_map) else {
        // Return the player to his savepoint
        return Some((ch.save_map.clone(), ch.save_x as i16, ch.save_y as i16));
    };
    if ch.last_map.eq_ignore_ascii_case("sec_pri") {
        return None; // Don't return jailed player to save point. (MAP_JAIL)
    }
    // Maybe since the player's logout the nosave mapflag was added to the map
    if crate::npc::mapflag(c, m, crate::npc::MF_NOSAVE) {
        return match crate::npc::mapflag_nosave_point(c, m) {
            // The map has a specific return point
            Some((map, x, y)) => Some((map, x as i16, y as i16)),
            // Return the user to his save point
            None => Some((ch.save_map.clone(), ch.save_x as i16, ch.save_y as i16)),
        };
    }
    match crate::instance::lastpoint_special(c, ch, m) {
        Ok(None) => None,
        // Overwrite the stored "source mapname" with the real mapname (X/Y coordinates are already correct)
        Ok(Some(name)) => Some((name, ch.last_x as i16, ch.last_y as i16)),
        // Return the player to his savepoint
        Err(()) => Some((ch.save_map.clone(), ch.save_x as i16, ch.save_y as i16)),
    }
}

// ---------------------------------------------------------------------------------------------- npc_chat (script2/npcchat.rs)

/// struct pcrematch_entry: a pattern (compiled PCRE_CASELESS) and its label.
#[derive(spacetimedb::SpacetimeType, Clone, Debug)]
pub struct ChatPattern {
    pub pattern: Vec<u8>,
    pub label: String,
}

/// struct pcrematch_set: one set of an NPC's patterns (nd->chatdb's active / inactive lists). A list is ordered by
/// `seq`, the most recently inserted set first (sets are always put at the head).
#[table(accessor = npc_chat_set, index(accessor = by_npc, btree(columns = [npc_id])))]
#[derive(Clone, Debug)]
pub struct NpcChatSet {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub npc_id: u32,
    pub setid: i64,
    pub active: bool,
    pub seq: u64,
    /// In definition order (create_pcrematch_entry appends: "the items defined first have a higher priority").
    pub patterns: Vec<ChatPattern>,
}

