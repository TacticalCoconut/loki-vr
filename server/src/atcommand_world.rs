//! map/atcommand.cpp: maps, weather, NPCs, jail: @snow, @sakura, @clouds, @clouds2, @fog, @leaves, @fireworks, @clearweather, @skillon, @skilloff, @allowks, @cleanmap, @cleanarea, @mutearea, @tonpc, @shownpc, @hidenpc, @loadnpc, @unloadnpc, @reloadnpcfile, @unloadnpcfile, @addwarp, @jail, @unjail, @jailfor, @jailtime, @macrochecker (atcmd agent, wave D). Routed from atcommand_atcmd.rs `run` by the ACMD_FUNC name.
//!
//! Helpers ported here (no other file had them): clif.cpp clif_weather and clif_macro_checker; npc.cpp npc_add_warp;
//! pc.cpp pc_jail and pc_macro_reporter_process; common/timer.cpp solve_time, split_time, timestamp2string (with a
//! mktime for the server's time zone); atcommand.cpp atcommand_normalize_npc_path, atcommand_cleanfloor_sub,
//! atcommand_mutearea_sub, atcommand_macrochecker_sub. The NPC file commands (@loadnpc, @unloadnpc, @reloadnpcfile,
//! @unloadnpcfile) call npc_load.rs (npc_addsrcfile, npc_unloadfile, npc_unload_duplicates, npc_event_doall_path;
//! wave F npcload).

use crate::clif;
use crate::cstr::Scan;
use crate::data::{self, format_msg, msg_txt};
use crate::npc::{self, Npc, MF_CLOUDS, MF_CLOUDS2, MF_FIREWORKS, MF_FOG, MF_LEAVES, MF_NOSKILL, MF_SAKURA, MF_SNOW};
use crate::script::buildins::script2::mapmisc::{map_setmapflag_sub, MfArgs};
use crate::status::consts::{SC_JAILED, SC_NOCHAT};
use crate::unit::{self, Unit};
use crate::world::Ctx;
use spacetimedb::Table;

use super::Out;

/// Runs `func` when it is one of this file's commands; None otherwise.
pub fn run(c: &Ctx, fd: Option<&Unit>, sd: u32, func: &str, _command: &str, message: &str) -> Option<i32> {
    let out = Out { c, fd };
    Some(match func {
        "snow" => weather(c, &out, sd, MF_SNOW, 1203, 1204),
        "sakura" => weather(c, &out, sd, MF_SAKURA, 1205, 1206),
        "clouds" => weather(c, &out, sd, MF_CLOUDS, 1207, 1208),
        "clouds2" => weather(c, &out, sd, MF_CLOUDS2, 1209, 1210),
        "fog" => weather(c, &out, sd, MF_FOG, 1211, 1212),
        "leaves" => weather(c, &out, sd, MF_LEAVES, 1213, 1214),
        "fireworks" => weather(c, &out, sd, MF_FIREWORKS, 1215, 1216),
        "clearweather" => clearweather(c, &out, sd),
        "skillon" => skillon(c, &out, sd),
        "skilloff" => skilloff(c, &out, sd),
        "allowks" => allowks(c, &out, sd),
        "cleanmap" => cleanmap(c, &out, sd),
        "cleanarea" => cleanarea(c, &out, sd, message),
        "mutearea" => mutearea(c, &out, sd, message),
        "tonpc" => tonpc(c, &out, sd, message),
        "shownpc" => shownpc(c, &out, message),
        "hidenpc" => hidenpc(c, &out, message),
        "loadnpc" => loadnpc(c, &out, message),
        "unloadnpc" => unloadnpc(c, &out, message),
        "reloadnpcfile" => reloadnpcfile(c, &out, message),
        "unloadnpcfile" => unloadnpcfile(c, &out, message),
        "addwarp" => addwarp(c, &out, sd, message),
        "jail" => jail(c, &out, sd, message),
        "unjail" => unjail(c, &out, sd, message),
        "jailfor" => jailfor(c, &out, sd, message),
        "jailtime" => jailtime(c, &out, sd),
        "macrochecker" => macrochecker(c, &out, sd, message),
        _ => return None,
    })
}

fn bytes(s: &str) -> Vec<u8> {
    crate::net::wire::latin1(s)
}

/// sscanf(message, "%<width>[^\n]", buf) < 1 → None (an empty message included).
fn line(message: &str, width: usize) -> Option<String> {
    Scan::new(&bytes(message)).set(width, b"\n")
}

/// clif_displaymessage(pl_sd->fd, text): a message to another player's client.
fn say_to(c: &Ctx, aid: u32, text: &str) {
    if let Some(u) = unit::get(c, aid) {
        clif::displaymessage(c, &u, text);
    }
}

// ---------------------------------------------------------------------------------------------- map flags, weather

/// map.hpp map_setmapflag(m, flag, status): map_setmapflag_sub(m, flag, status, nullptr) — script2's port
/// (mapmisc.rs), which hands the PvP / GvG flags to battle/pvp.rs and the others to npc.rs setmapflag.
fn map_setmapflag(c: &Ctx, m: u16, flag: i16, status: bool) {
    map_setmapflag_sub(c, m, flag, status, &MfArgs::None);
}

/// clif.cpp clif_weather: every player whose sd->m is `m` gets the map's weather effects (clif_weather_check, npc.rs
/// weather_check), in mapit_getallusers order (pc_db, an int-keyed DBMap: party.rs idb_order).
fn clif_weather(c: &Ctx, m: u16) {
    use crate::pc::pc as _;
    let mut ids: Vec<u32> = c.db().pc().iter().map(|p| p.account_id).collect();
    ids.sort_by_key(|&id| crate::party::idb_order(id));
    for aid in ids {
        if let Some(u) = unit::get(c, aid) {
            if u.map == m {
                npc::weather_check(c, &u);
            }
        }
    }
}

/// ACMD_FUNC(snow) / (sakura) / (clouds) / (clouds2) / (fog) / (leaves) / (fireworks): the weather flag of the map is
/// toggled (map_setmapflag), the map's players are told (clif_weather), then msg `off` / `on`. (`rain` is commented
/// out in rAthena.)
fn weather(c: &Ctx, out: &Out, sd: u32, flag: i16, off: u16, on: u16) -> i32 {
    let Some(u) = unit::get(c, sd) else { return -1 }; // nullpo_retr(-1, sd)
    if npc::mapflag(c, u.map, flag) {
        map_setmapflag(c, u.map, flag, false);
        clif_weather(c, u.map);
        out.say(msg_txt(off));
    } else {
        map_setmapflag(c, u.map, flag, true);
        clif_weather(c, u.map);
        out.say(msg_txt(on));
    }
    0
}

/// ACMD_FUNC(clearweather): every weather flag of the map off (MF_RAIN is commented out in rAthena), clif_weather
/// (which then shows nothing: the client keeps the effects until a warp or refresh, as msg 291 says).
fn clearweather(c: &Ctx, out: &Out, sd: u32) -> i32 {
    let Some(u) = unit::get(c, sd) else { return -1 }; // nullpo_retr(-1, sd)
    for flag in [MF_SNOW, MF_SAKURA, MF_CLOUDS, MF_CLOUDS2, MF_FOG, MF_FIREWORKS, MF_LEAVES] {
        map_setmapflag(c, u.map, flag, false);
    }
    clif_weather(c, u.map);
    out.say(msg_txt(291)); // Weather effects will dispell on warp/refresh
    0
}

/// ACMD_FUNC(skillon): MF_NOSKILL off on this map.
fn skillon(c: &Ctx, out: &Out, sd: u32) -> i32 {
    let Some(u) = unit::get(c, sd) else { return -1 }; // nullpo_retr(-1, sd)
    map_setmapflag(c, u.map, MF_NOSKILL, false);
    out.say(msg_txt(244)); // Skills have been enabled on this map.
    0
}

/// ACMD_FUNC(skilloff): MF_NOSKILL on for this map.
fn skilloff(c: &Ctx, out: &Out, sd: u32) -> i32 {
    let Some(u) = unit::get(c, sd) else { return -1 }; // nullpo_retr(-1, sd)
    map_setmapflag(c, u.map, MF_NOSKILL, true);
    out.say(msg_txt(243)); // Skills have been disabled on this map.
    0
}

/// ACMD_FUNC(allowks): toggles MF_ALLOWKS (kill-steal protection of the map: mob_ksprotected, clif's party / guild
/// damage display read it).
fn allowks(c: &Ctx, out: &Out, sd: u32) -> i32 {
    use crate::mob::MF_ALLOWKS;
    let Some(u) = unit::get(c, sd) else { return -1 }; // nullpo_retr(-1,sd)
    if npc::mapflag(c, u.map, MF_ALLOWKS) {
        map_setmapflag(c, u.map, MF_ALLOWKS, false);
        out.say(msg_txt(1330)); // [ Map K.S Protection Active ]
    } else {
        map_setmapflag(c, u.map, MF_ALLOWKS, true);
        out.say(msg_txt(1331)); // [ Map K.S Protection Inactive ]
    }
    0
}

// ---------------------------------------------------------------------------------------------- floor items, mute

/// atcommand.cpp atcommand_cleanfloor_sub over the floor items met (map_clearflooritem, flooritem.rs), in the order
/// the map_foreachin* loop met them.
fn cleanfloor(c: &Ctx, items: Vec<Unit>) {
    for it in items {
        crate::flooritem::map_clearflooritem(c, it.id);
    }
}

/// ACMD_FUNC(cleanmap): map_foreachinmap(atcommand_cleanfloor_sub, sd->m, BL_ITEM).
fn cleanmap(c: &Ctx, out: &Out, sd: u32) -> i32 {
    let Some(u) = unit::get(c, sd) else { return -1 };
    let (xs, ys) = data::maps().get(u.map).map(|d| (d.xs, d.ys)).unwrap_or((0, 0));
    cleanfloor(c, crate::map::units_in_area(c, u.map, 0, 0, xs - 1, ys - 1, crate::map::BL_ITEM));
    out.say(msg_txt(1221)); // All dropped items have been cleaned up.
    0
}

/// ACMD_FUNC(cleanarea) {<range> | <x0> <y0> <x1> <y1>}: sscanf "%6hd %6hd %6hd %6hd" — no number: the floor items
/// within AREA_SIZE * 2 cells; one: within that range; four: that rectangle. rAthena quirk kept: two or three numbers
/// clean nothing and still say that everything was cleaned. The coordinates are int16 parameters (the int sums wrap).
fn cleanarea(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let Some(u) = unit::get(c, sd) else { return -1 };
    let b = bytes(message);
    let mut v = [0i16; 4];
    let mut n = 0;
    {
        let mut s = Scan::new(&b);
        for slot in v.iter_mut() {
            match s.short(6) {
                Some(x) => {
                    *slot = x;
                    n += 1;
                }
                None => break,
            }
        }
    }
    let [x0, y0, x1, y1] = v;
    let area = |ax0: i32, ay0: i32, ax1: i32, ay1: i32| crate::map::units_in_area(c, u.map, ax0 as i16, ay0 as i16, ax1 as i16, ay1 as i16, crate::map::BL_ITEM);
    let (sx, sy) = (u.x as i32, u.y as i32);
    if message.is_empty() || n < 1 {
        let r = crate::conf::area_size() as i32 * 2;
        cleanfloor(c, area(sx - r, sy - r, sx + r, sy + r));
    } else if n == 1 {
        let r = x0 as i32;
        cleanfloor(c, area(sx - r, sy - r, sx + r, sy + r));
    } else if n == 4 {
        cleanfloor(c, area(x0 as i32, y0 as i32, x1 as i32, y1 as i32));
    }
    out.say(msg_txt(1221)); // All dropped items have been cleaned up.
    0
}

/// atcommand.cpp atcommand_mutearea_sub: a player of group level 0 other than the GM loses `time` manner points
/// (status.manner is an int16: the difference is truncated); a negative manner starts SC_NOCHAT, else SC_NOCHAT ends.
/// (Unit::manner, the copy the unit packets show, is taken at login as everywhere else in the module.)
fn atcommand_mutearea_sub(c: &Ctx, id: u32, pl: u32, time: i32) {
    if id == pl || crate::pc::group_level(c, pl) != 0 {
        return;
    }
    let Some(mut ch) = crate::pc::character_of(c, pl) else { return };
    ch.manner = (ch.manner as i32).wrapping_sub(time) as i16;
    let negative = ch.manner < 0;
    crate::status::put_char(c, &ch);
    if negative {
        if let Some(u) = unit::get(c, pl) {
            crate::status::sc::sc_start(c, None, &u, SC_NOCHAT, 100, 0, 0);
        }
    } else {
        crate::status::sc::status_change_end(c, pl, SC_NOCHAT, 0);
    }
}

/// ACMD_FUNC(mutearea) <time>: atcommand_mutearea_sub for the players within AREA_SIZE (map_foreachinallarea, BL_PC).
/// The time is atoi(message): anything that is not a number is 0, which ends the SC_NOCHAT of every player around
/// whose manner is not negative (rAthena quirk kept). No message on success.
fn mutearea(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let Some(u) = unit::get(c, sd) else { return 0 }; // nullpo_ret(sd)
    if message.is_empty() {
        out.say(msg_txt(1297)); // Please enter a time in minutes (usage: @mutearea/@stfu <time in minutes>).
        return -1;
    }
    let time = crate::cstr::atoi(message);
    let r = crate::conf::area_size();
    for pl in crate::map::units_in_area(c, u.map, u.x - r, u.y - r, u.x + r, u.y + r, crate::map::BL_PC) {
        atcommand_mutearea_sub(c, sd, pl.id, time);
    }
    0
}

// ---------------------------------------------------------------------------------------------- NPCs

/// npc_name2id (script/npc_run.rs).
fn npc_name2id(c: &Ctx, name: &str) -> Option<Npc> {
    crate::script::npc_run::npc_name2id(c, name)
}

/// ACMD_FUNC(tonpc) <NPC name>: pc_setpos to the NPC's cell (CLR_TELEPORT), "Warped." when it worked.
fn tonpc(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let Some(npcname) = line(message, 49) else {
        out.say(msg_txt(1129)); // Please enter a NPC name (usage: @tonpc <NPC_name>).
        return -1;
    };
    let Some(nd) = npc_name2id(c, &npcname) else {
        out.say(msg_txt(111)); // This NPC doesn't exist.
        return -1;
    };
    if nd.map < 0 {
        // A floating NPC (nd->m == -1): map_id2index(-1) reads map[-1], outside rAthena's map array (undefined
        // behaviour); the module takes it as a pc_setpos that fails.
        return -1;
    }
    if crate::pc::setpos(c, sd, nd.map as u16, nd.x, nd.y, unit::CLR_TELEPORT) == crate::pc::SetPos::Ok {
        out.say(msg_txt(0)); // Warped.
    } else {
        return -1;
    }
    0
}

/// ACMD_FUNC(shownpc) <NPC name>: npc_enable(nd, NPCVIEW_ENABLE) (npc_enable_target with no character, npcctl.rs).
fn shownpc(c: &Ctx, out: &Out, message: &str) -> i32 {
    let Some(name) = line(message, 49) else {
        out.say(msg_txt(1130)); // Please enter a NPC name (usage: @enablenpc <NPC_name>).
        return -1;
    };
    match npc_name2id(c, &name) {
        Some(nd) => {
            crate::script::buildins::npcctl::npc_enable_target(c, nd.id, 0, npc::NPCVIEW_ENABLE);
            out.say(msg_txt(110)); // Npc Enabled.
        }
        None => {
            out.say(msg_txt(111)); // This NPC doesn't exist.
            return -1;
        }
    }
    0
}

/// ACMD_FUNC(hidenpc) <NPC name>: npc_enable(nd, NPCVIEW_DISABLE).
fn hidenpc(c: &Ctx, out: &Out, message: &str) -> i32 {
    let Some(name) = line(message, 49) else {
        out.say(msg_txt(1131)); // Please enter a NPC name (usage: @hidenpc <NPC_name>).
        return -1;
    };
    let Some(nd) = npc_name2id(c, &name) else {
        out.say(msg_txt(111)); // This NPC doesn't exist.
        return -1;
    };
    crate::script::buildins::npcctl::npc_enable_target(c, nd.id, 0, npc::NPCVIEW_DISABLE);
    out.say(msg_txt(112)); // Npc Disabled.
    0
}

/// atcommand.cpp atcommand_normalize_npc_path: safestrncpy into a 1024-byte buffer (a message is at most 255
/// characters: nothing is cut), then every '\' becomes '/'.
fn atcommand_normalize_npc_path(message: &str) -> String {
    message.replace('\\', "/")
}

/// ACMD_FUNC(loadnpc) <file>: npc_addsrcfile(path, true) (npc_load.rs: the file must be one the module embeds —
/// everything rAthena ships under npc/ — and not already listed in npc_src_files), then npc_read_event_script and the
/// file's OnInit events (npc_event_doall_path).
fn loadnpc(c: &Ctx, out: &Out, message: &str) -> i32 {
    if message.is_empty() {
        out.say(msg_txt(1132)); // Please enter a script file name (usage: @loadnpc <file name>).
        return -1;
    }
    let path = atcommand_normalize_npc_path(message);
    if !crate::npc_load::npc_addsrcfile(c, &path, true) {
        out.say(msg_txt(261)); // Script could not be loaded.
        return -1;
    }
    crate::script::npc_run::npc_read_event_script(c);
    log::info!("NPC file '{path}' was loaded.");
    crate::npc_load::npc_event_doall_path(c, crate::script::npc_run::INIT_EVENT_NAME, &path);
    out.say(msg_txt(262)); // Script loaded.
    0
}

/// ACMD_FUNC(unloadnpc) <NPC name>: the NPC's duplicates, then the NPC, leave the world (npc_unload_duplicates,
/// npc_unload(nd, true)), then npc_read_event_script.
fn unloadnpc(c: &Ctx, out: &Out, message: &str) -> i32 {
    let Some(name) = line(message, 49) else {
        out.say(msg_txt(1133)); // Please enter a NPC name (usage: @unloadnpc <NPC_name>).
        return -1;
    };
    let Some(nd) = npc_name2id(c, &name) else {
        out.say(msg_txt(111)); // This NPC doesn't exist.
        return -1;
    };
    crate::npc_load::npc_unload_duplicates(c, nd.id);
    npc::unload(c, nd.id);
    crate::script::npc_run::npc_read_event_script(c);
    out.say(msg_txt(112)); // Npc Disabled.
    0
}

/// ACMD_FUNC(reloadnpcfile) <file>: npc_unloadfile (msg 1386 when it found something), then as @loadnpc:
/// npc_addsrcfile(path, true), npc_read_event_script, the file's OnInit events.
fn reloadnpcfile(c: &Ctx, out: &Out, message: &str) -> i32 {
    if message.is_empty() {
        out.say(msg_txt(733)); // Please enter a NPC file name (usage: @reloadnpcfile <file name>).
        return -1;
    }
    let path = atcommand_normalize_npc_path(message);
    if crate::npc_load::npc_unloadfile(c, &path) {
        out.say(msg_txt(1386)); // File unloaded. Be aware that mapflags and monsters spawned directly are not removed.
    }
    if !crate::npc_load::npc_addsrcfile(c, &path, true) {
        out.say(msg_txt(261)); // Script could not be loaded.
        return -1;
    }
    crate::script::npc_run::npc_read_event_script(c);
    log::info!("NPC file '{path}' was reloaded.");
    crate::npc_load::npc_event_doall_path(c, crate::script::npc_run::INIT_EVENT_NAME, &path);
    out.say(msg_txt(262)); // Script loaded.
    0
}

/// ACMD_FUNC(unloadnpcfile) <file>: npc_unloadfile (npc_load.rs; msg 1386, or 1387 and failure when the file had
/// nothing — it leaves npc_src_files either way).
fn unloadnpcfile(c: &Ctx, out: &Out, message: &str) -> i32 {
    if message.is_empty() {
        out.say(msg_txt(1385)); // Usage: @unloadnpcfile <file name>
        return -1;
    }
    let path = atcommand_normalize_npc_path(message);
    if crate::npc_load::npc_unloadfile(c, &path) {
        out.say(msg_txt(1386)); // File unloaded. Be aware that mapflags and monsters spawned directly are not removed.
        log::info!("NPC file '{path}' was unloaded.");
    } else {
        out.say(msg_txt(1387)); // File not found.
        return -1;
    }
    0
}

/// map.hpp MAX_NPC_PER_MAP.
const MAX_NPC_PER_MAP: usize = 512;

/// npc.cpp npc_add_warp: a warp NPC (JT_WARPNPC: misc.conf `warp_point_debug: no`) named `name` — or
/// "warp_<m>_<x>_<y>" when that unique name is taken, then "warp<i>_<m>_<x>_<y>" — at (from_x, from_y) of map
/// `from_mapid` with a (xs, ys) trigger area, to (to_x, to_y) of `to_mapindex` (the module's map ids stand for
/// mapindexes). npc_create_npc takes the next NPC id (mob/spawn.rs npc_get_new_npc_id); map_addnpc puts it after the
/// map's other warps in the trigger-area section (the first script there moves to the section's end; npc_setcells'
/// CELL_NPC is npc.rs has_touch_area_at, read from that section); map_addblock; status_set_viewdata; unit_dataset
/// (direction 0, the timers' ticks now); clif_spawn when the map has players; npcname_db (the `npc` row). The NPC has
/// no file (nd->path nullptr: "" here). rAthena returns nullptr only when map_addblock fails.
#[allow(clippy::too_many_arguments)]
fn npc_add_warp(c: &Ctx, name: &str, from_mapid: u16, from_x: i16, from_y: i16, xs: i16, ys: i16, to_mapindex: u16, to_x: i16, to_y: i16) -> Option<Npc> {
    use crate::npc::npc as _;
    use crate::unit::unit as _;
    // npc_create_npc(from_mapid, from_x, from_y)
    let id = crate::mob::spawn::npc_get_new_npc_id(c);
    if id == 0 {
        return None; // rAthena: ShowFatalError("npc_get_new_npc_id: All ids are taken. Exiting...") and exit(1)
    }
    // safestrncpy(nd->exname, name, ARRAYLENGTH(nd->exname))
    let mut exname: String = name.chars().take(npc::NPC_NAME_LENGTH).collect();
    if npc_name2id(c, &exname).is_some() {
        exname = format!("warp_{from_mapid}_{from_x}_{from_y}");
    }
    let mut i = 0;
    while npc_name2id(c, &exname).is_some() {
        exname = format!("warp{i}_{from_mapid}_{from_x}_{from_y}");
        i += 1;
    }
    let class = npc::JT_WARPNPC; // battle_config.warp_point_debug (misc.conf: no) would make it JT_GUILD_FLAG
    // map_addnpc(from_mapid, nd): its return is not checked (a full map only leaves the warp out of mapdata->npc)
    let md_name = data::maps().get(from_mapid).map(|d| d.name.clone()).unwrap_or_default();
    let npc_num = c.db().npc().by_map().filter(from_mapid as i32).filter(|n| unit::get(c, n.id).map(|u| u.on_map).unwrap_or(false)).count();
    let in_npc_list = if npc_num >= MAX_NPC_PER_MAP {
        log::warn!("too many NPCs in one map {md_name}");
        false
    } else {
        true
    };
    // map_addblock(nd); status_set_viewdata(nd, nd->class_); unit_dataset(nd)
    let tick = c.tick();
    let ud = unit::UnitData { attackabletime: tick, canact_tick: tick, canmove_tick: tick, endure_tick: tick, dmg_tick: 0, sx: 8, sy: 8, ..Default::default() };
    let mut u = Unit {
        id,
        kind: crate::map::BL_NPC,
        map: from_mapid,
        x: from_x,
        y: from_y,
        on_map: false,
        name: exname.clone(),
        vd: npc::npc_viewdata(class),
        ud,
        sc: unit::ScView::default(),
        speed: npc::DEFAULT_NPC_WALK_SPEED,
        mode: unit::MD_CANMOVE,
        level: 0,
        char_id: 0,
        head_dir: 0,
        font: 0,
        manner: 0,
        karma: false,
        guild_id: 0,
        emblem_id: 0,
        hp: 0,
        max_hp: 0,
        dead: false,
        size: 0,
        boss: 0,
        invisible: false,
        npc_owner_char: 0,
    };
    c.db().unit().insert(u.clone());
    if !unit::addblock(c, &mut u) {
        return None; // (sd's own cell: cannot fail from @addwarp)
    }
    // the trigger-area section of mapdata->npc: npc[npc_num_area] = npc[npc_num_warp]; npc[npc_num_warp] = nd;
    let mut touch_order = 0;
    if in_npc_list && xs >= 0 && ys >= 0 {
        let area = npc::touch_npcs(c, from_mapid);
        let num_warp = area.iter().filter(|n| n.subtype == npc::NPCTYPE_WARP).count();
        if num_warp < area.len() {
            let moved = area[num_warp].clone();
            c.db().npc().id().update(Npc { touch_order: area.len() as u32 + 1, ..moved });
        }
        touch_order = num_warp as u32 + 1;
    }
    // strdb_put(npcname_db, nd->exname, nd)
    let nd = Npc {
        id,
        exname: exname.clone(),
        name: exname, // safestrncpy(nd->name, nd->exname, ARRAYLENGTH(nd->name))
        subtype: npc::NPCTYPE_WARP,
        map: from_mapid as i32,
        x: from_x,
        y: from_y,
        class,
        src_id: 0,
        xs,
        ys,
        warp_map: to_mapindex as i32,
        warp_x: to_x,
        warp_y: to_y,
        trigger_on_hidden: false,
        state: npc::NPCVIEW_ENABLE,
        is_invisible: false,
        path: String::new(),
        script_file: 0,
        script_start: 0,
        script_end: 0,
        script_line: 0,
        shop: Vec::new(),
        shop_discount: false,
        itemshop_nameid: 0,
        pointshop_str: String::new(),
        owner_char: 0,
        touch_order,
    };
    c.db().npc().insert(nd.clone());
    if crate::mob::map_users(c, from_mapid) > 0 {
        if let Some(u) = unit::get(c, id) {
            clif::spawn(c, &u, false);
        }
    }
    Some(nd)
}

/// ACMD_FUNC(addwarp) <map> <x> <y> <NPC name>: a warp NPC on the GM's cell (a 2×2 trigger area) to that place
/// (sscanf "%15s %6hd %6hd %49[^\n]"; mapindex_name2id: data::maps().id_of).
fn addwarp(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let Some(me) = unit::get(c, sd) else { return -1 }; // nullpo_retr(-1, sd)
    let b = bytes(message);
    let mut s = Scan::new(&b);
    let mapname = s.word(15);
    let x = mapname.as_ref().and_then(|_| s.short(6));
    let y = x.and_then(|_| s.short(6));
    let warpname = y.and_then(|_| {
        s.ws();
        s.set(49, b"\n")
    });
    let (Some(mapname), Some(x), Some(y), Some(warpname)) = (mapname, x, y, warpname) else {
        out.say(msg_txt(1156)); // Usage: @addwarp <mapname> <X> <Y> <npc name>
        return -1;
    };
    let Some(m) = data::maps().id_of(&mapname) else {
        out.say(&format_msg(msg_txt(1157), &[&mapname])); // Unknown map '%s'.
        return -1;
    };
    let Some(nd) = npc_add_warp(c, &warpname, me.map, me.x, me.y, 2, 2, m, x, y) else { return -1 };
    out.say(&format_msg(msg_txt(1158), &[&nd.exname])); // New warp NPC '%s' created.
    0
}

// ---------------------------------------------------------------------------------------------- jail

/// common/mapindex.hpp MAP_JAIL.
const MAP_JAIL: &str = "sec_pri";

/// pc.cpp pc_jail(sd, duration): SC_JAILED for `duration` minutes (i32::MAX: until released) with the jail map and
/// one of its two cells (rnd() % 2: 49,75 or 24,75; the roll is made even for a release), checked every minute
/// (60000); 0 or less ends SC_JAILED (the release). SC_JAILED's value setup (the warp into jail and the return
/// point), its minute timer and its end (the warp back) are status_change_start / _timer / _end cases: status2
/// agent — the atcmd branch's status_change_start refuses SC_JAILED until status2's value setup is merged.
pub(crate) fn pc_jail(c: &Ctx, aid: u32, duration: i32) {
    // mapindex_name2id(MAP_JAIL) (the module's map id stands for the mapindex; sec_pri is a map of this server)
    let m_index = data::maps().id_of(MAP_JAIL).map(|m| m as i32).unwrap_or(0);
    let (x, y) = match c.rnd() % 2 {
        0 => (49, 75), // Jail #1
        _ => (24, 75), // Default Jail
    };
    let duration = duration.max(0); // Can't be less than 0 seconds.
    if duration > 0 {
        if let Some(u) = unit::get(c, aid) {
            crate::status::sc::sc_start4(c, None, &u, SC_JAILED, 100, duration, m_index, x, y, 60000);
        }
    } else {
        crate::status::sc::status_change_end(c, aid, SC_JAILED, 0);
    }
}

/// ACMD_FUNC(jail) <name>: pc_jail(pl_sd) (until released). Special warp: no check of nowarp / nowarpto.
fn jail(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let Some(name) = line(message, 23) else {
        out.say(msg_txt(1134)); // Please enter a player name (usage: @jail <char_name>).
        return -1;
    };
    let Some(pl) = crate::pc::nick2sd(c, &name, false) else {
        out.say(msg_txt(3)); // Character not found.
        return -1;
    };
    if crate::pc::group_level(c, sd) < crate::pc::group_level(c, pl.id) {
        // you can jail only lower or same GM
        out.say(msg_txt(81)); // Your GM level don't authorise you to do this action on this player.
        return -1;
    }
    if crate::status::sc::has_sce(c, pl.id, SC_JAILED) {
        out.say(msg_txt(118)); // Player warped in jails.
        return -1;
    }
    pc_jail(c, pl.id, i32::MAX);
    say_to(c, pl.id, msg_txt(117)); // GM has send you in jails.
    out.say(msg_txt(118)); // Player warped in jails.
    0
}

/// ACMD_FUNC(unjail) <name> (@discharge): pc_jail(pl_sd, 0) ends SC_JAILED (its end warps the player back).
fn unjail(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let Some(name) = line(message, 23) else {
        out.say(msg_txt(1135)); // Please enter a player name (usage: @unjail/@discharge <char_name>).
        return -1;
    };
    let Some(pl) = crate::pc::nick2sd(c, &name, false) else {
        out.say(msg_txt(3)); // Character not found.
        return -1;
    };
    if crate::pc::group_level(c, sd) < crate::pc::group_level(c, pl.id) {
        // you can jail only lower or same GM
        out.say(msg_txt(81)); // Your GM level don't authorise you to do this action on this player.
        return -1;
    }
    if !crate::status::sc::has_sce(c, pl.id, SC_JAILED) {
        out.say(msg_txt(119)); // This player is not in jails.
        return -1;
    }
    // Reset jail time to 1 sec.
    pc_jail(c, pl.id, 0);
    say_to(c, pl.id, msg_txt(120)); // A GM has discharged you from jail.
    out.say(msg_txt(121)); // Player unjailed.
    0
}

/// common/timer.cpp split_time: seconds into years (31556926 s), months (2629743 s), days, hours, minutes, seconds,
/// each at least 0.
fn split_time(timein: i32) -> (i32, i32, i32, i32, i32, i32) {
    const FACTOR_MIN: i32 = 60;
    const FACTOR_HOUR: i32 = FACTOR_MIN * 60;
    const FACTOR_DAY: i32 = FACTOR_HOUR * 24;
    const FACTOR_MONTH: i32 = 2629743; // Approx  (30.44 days)
    const FACTOR_YEAR: i32 = 31556926; // Approx (365.24 days)
    let mut timein = timein;
    let year = timein / FACTOR_YEAR;
    timein -= year * FACTOR_YEAR;
    let month = timein / FACTOR_MONTH;
    timein -= month * FACTOR_MONTH;
    let day = timein / FACTOR_DAY;
    timein -= day * FACTOR_DAY;
    let hour = timein / FACTOR_HOUR;
    timein -= hour * FACTOR_HOUR;
    let minute = timein / FACTOR_MIN;
    timein -= minute * FACTOR_MIN;
    let second = timein;
    (year.max(0), month.max(0), day.max(0), hour.max(0), minute.max(0), second.max(0))
}

/// Days from 1970-01-01 to the proleptic Gregorian date (y, m 1..12, d).
fn days_from_civil(y: i64, m: i64, d: i64) -> i64 {
    let y = if m <= 2 { y - 1 } else { y };
    let era = y.div_euclid(400);
    let yoe = y - era * 400;
    let mp = (m + 9) % 12; // March = 0
    let doy = (153 * mp + 2) / 5 + d - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    era * 146_097 + doe - 719_468
}

/// mktime() of a `struct tm` in the server's time zone, with its normalisation of out-of-range fields (months carry
/// into years, days / hours / minutes / seconds into the date). The module's local time is a fixed UTC-3
/// (script/npc_run.rs LOCAL_TZ_OFFSET_SECS: the oracle runs with TZ=America/Sao_Paulo, which has had no daylight
/// saving since 2019): a date inside one of Brazil's older DST periods would be an hour off from glibc's.
fn mktime_local(t: &crate::script::npc_run::Tm) -> i64 {
    let mon = t.mon as i64;
    let year = t.year as i64 + 1900 + mon.div_euclid(12);
    let mon = mon.rem_euclid(12);
    let days = days_from_civil(year, mon + 1, 1) + (t.mday as i64 - 1);
    days * 86_400 + t.hour as i64 * 3600 + t.min as i64 * 60 + t.sec as i64 - crate::script::npc_run::LOCAL_TZ_OFFSET_SECS
}

/// common/timer.cpp solve_time(modif_p) at unix time `now`: each "<number><unit>" of the text moves the local time
/// (s seconds, n / mn minutes, h hours, d / j days, m months, y / a years; the units are tested in that order, so
/// "mn" is minutes and a lone "m" months); a character that does not start a non-zero number is skipped (atoi is 0),
/// and so is a unit letter it does not know. Returns difftime(mktime(then), now). The fields are ints: their sums
/// wrap.
fn solve_time_at(now: i64, modif: &str) -> f64 {
    let mut t = crate::script::npc_run::localtime(now);
    let b = bytes(modif);
    let mut p = 0;
    while p < b.len() && b[p] != 0 {
        let rest: String = b[p..].iter().map(|&x| x as char).collect();
        let value = crate::cstr::atoi(&rest);
        if value == 0 {
            p += 1;
            continue;
        }
        if b[p] == b'-' || b[p] == b'+' {
            p += 1;
        }
        while p < b.len() && b[p].is_ascii_digit() {
            p += 1;
        }
        let at = |i: usize| b.get(i).copied().unwrap_or(0);
        match at(p) {
            b's' => {
                t.sec = t.sec.wrapping_add(value);
                p += 1;
            }
            b'n' => {
                t.min = t.min.wrapping_add(value);
                p += 1;
            }
            b'm' if at(p + 1) == b'n' => {
                t.min = t.min.wrapping_add(value);
                p += 2;
            }
            b'h' => {
                t.hour = t.hour.wrapping_add(value);
                p += 1;
            }
            b'd' | b'j' => {
                t.mday = t.mday.wrapping_add(value);
                p += 1;
            }
            b'm' => {
                t.mon = t.mon.wrapping_add(value);
                p += 1;
            }
            b'y' | b'a' => {
                t.year = t.year.wrapping_add(value);
                p += 1;
            }
            0 => {}
            _ => p += 1,
        }
    }
    let then = mktime_local(&t);
    (then - now) as f64
}

/// common/timer.cpp solve_time: [`solve_time_at`] now (time(nullptr): the reducer's clock).
fn solve_time(c: &Ctx, modif: &str) -> f64 {
    solve_time_at(c.tick().div_euclid(1000), modif)
}

/// common/timer.cpp timestamp2string(str, size, timestamp, format): strftime of localtime(timestamp) into `size`
/// bytes; a result that does not fit (with its NUL) leaves the string empty (strftime returns 0, then the memset).
fn timestamp2string(size: usize, timestamp: i64, format: &str) -> String {
    let tm = crate::script::npc_run::localtime(timestamp);
    let s = crate::script::cfmt::strftime(format.as_bytes(), usize::MAX, &tm);
    if s.len() + 1 > size {
        return String::new();
    }
    crate::net::wire::from_latin1(&s)
}

/// ACMD_FUNC(jailfor) <time> <name>: jails for solve_time(time) / 60 minutes, or changes the sentence of a player
/// jailed for a time (Coltaro's part: the new total, or the release when it is 0 or less). rAthena quirks kept: a
/// player not jailed yet (or jailed without end) gets no message; an int32 sentence wraps.
fn jailfor(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let b = bytes(message);
    let mut s = Scan::new(&b);
    let modif = s.word(255);
    let name = modif.as_ref().and_then(|_| {
        s.ws();
        s.set(23, b"\n")
    });
    let (Some(modif), Some(name)) = (modif, name) else {
        out.say(msg_txt(400)); // Usage: @jailfor <time> <character name>
        return -1;
    };
    let mut jailtime = super::c_double_to_i32(solve_time(c, &modif)) / 60; // Change to minutes
    if jailtime == 0 {
        out.say(msg_txt(1136)); // Invalid time for jail command.
        out.say(msg_txt(702)); // Time parameter format is +/-<value> to alter. y/a = Year, m = Month, d/j = Day, h = Hour, n/mn = Minute, s = Second.
        return -1;
    }
    let Some(pl) = crate::pc::nick2sd(c, &name, false) else {
        out.say(msg_txt(3)); // Character not found.
        return -1;
    };
    if crate::pc::group_level(c, pl.id) > crate::pc::group_level(c, sd) {
        out.say(msg_txt(81)); // Your GM level don't authorise you to do this action on this player.
        return -1;
    }
    // Added by Coltaro
    match crate::status::sc::get_sce(c, pl.id, SC_JAILED).filter(|e| e.val1 != i32::MAX) {
        Some(sce) => {
            // Update the player's jail time
            jailtime = jailtime.wrapping_add(sce.val1);
            if jailtime <= 0 {
                jailtime = 0;
                say_to(c, pl.id, msg_txt(120)); // GM has discharge you.
                out.say(msg_txt(121)); // Player unjailed
            } else {
                let now = c.tick().div_euclid(1000);
                let (year, month, day, hour, minute, _second) = split_time(jailtime.wrapping_mul(60));
                // %s in jail for %d years, %d months, %d days, %d hours and %d minutes
                let args: [&dyn std::fmt::Display; 6] = [&msg_txt(1137), &year, &month, &day, &hour, &minute];
                say_to(c, pl.id, &format_msg(msg_txt(402), &args)); // You are now
                let args: [&dyn std::fmt::Display; 6] = [&msg_txt(1138), &year, &month, &day, &hour, &minute];
                out.say(&format_msg(msg_txt(402), &args)); // This player is now
                let timestr = timestamp2string(20, now + jailtime.wrapping_mul(60) as i64, "%Y-%m-%d %H:%M");
                let release = format!("Release date is: {timestr}");
                say_to(c, pl.id, &release);
                out.say(&release);
            }
        }
        None if jailtime < 0 => {
            out.say(msg_txt(1136)); // Invalid time for jail command.
            return -1;
        }
        None => {}
    }
    pc_jail(c, pl.id, jailtime);
    0
}

/// ACMD_FUNC(jailtime): the invoker's remaining sentence (SC_JAILED's val1, in minutes) and its release date.
fn jailtime(c: &Ctx, out: &Out, sd: u32) -> i32 {
    let now = c.tick().div_euclid(1000);
    let Some(sce) = crate::status::sc::get_sce(c, sd, SC_JAILED) else {
        out.say(msg_txt(1139)); // You are not in jail.
        return -1;
    };
    if sce.val1 == i32::MAX {
        out.say(msg_txt(1140)); // You have been jailed indefinitely.
        return 0;
    }
    if sce.val1 <= 0 {
        // Was not jailed with @jailfor (maybe @jail? or warped there? or got recalled?)
        out.say(msg_txt(1141)); // You have been jailed for an unknown amount of time.
        return -1;
    }
    // Get remaining jail time
    let (year, month, day, hour, minute, _second) = split_time(sce.val1.wrapping_mul(60));
    let args: [&dyn std::fmt::Display; 6] = [&msg_txt(1142), &year, &month, &day, &hour, &minute];
    out.say(&format_msg(msg_txt(402), &args)); // You will remain in jail for %d years, %d months, %d days, %d hours and %d minutes
    let timestr = timestamp2string(20, now + sce.val1.wrapping_mul(60) as i64, "%Y-%m-%d %H:%M");
    out.say(&format!("Release date is: {timestr}"));
    0
}

// ---------------------------------------------------------------------------------------------- macro checker

/// map.hpp e_mapflag MF_NOMACROCHECKER.
const MF_NOMACROCHECKER: i16 = 79;
/// conf/battle/client.conf `macrochecker_delay: 600000` (ms between two macro checks of a map).
fn macrochecker_delay() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::MACROCHECKER_DELAY) as i64
}

/// clif.hpp e_macro_checker_result.
const MACROCHECKER_MAPFLAG: i16 = 1;
const MACROCHECKER_COOLDOWN: i16 = 2;
const MACROCHECKER_UNKNOWN_MAP: i16 = 3;
const MACROCHECKER_SUCCESS: i16 = 4;

/// clif.cpp clif_macro_checker: ZC_GM_CHECKER (0x0c0c <result>.W) exists from PACKETVER_MAIN_NUM 20240502; this
/// server is PACKETVER 20211103, so it sends nothing.
fn clif_macro_checker(_c: &Ctx, _sd: u32, _result: i16) {}

/// mapdata->last_macrocheck (map.hpp map_data): the tick of the map's last macro check that reached a player
/// (atcmd_pc.rs `atcmd_map`; 0 = never).
fn last_macrocheck(c: &Ctx, m: u16) -> i64 {
    crate::atcmd_pc::map_get(c, m).last_macrocheck
}

/// mapdata->last_macrocheck = gettick().
fn set_last_macrocheck(c: &Ctx, m: u16, tick: i64) {
    let mut r = crate::atcmd_pc::map_get(c, m);
    r.last_macrocheck = tick;
    crate::atcmd_pc::map_put(c, &r);
}

/// pc.cpp pc_macro_reporter_process (macro_detector.rs): a captcha for the player, if captcha_db has any (only the
/// ones registered with /macro_register at run time: rAthena's db/captcha_db.yml lists none).
fn pc_macro_reporter_process(c: &Ctx, aid: u32, reporter_account_id: u32) {
    crate::macro_detector::pc_macro_reporter_process(c, aid, reporter_account_id as i32);
}

/// atcommand.cpp atcommand_macrochecker_sub: 1 for a player other than the reporter, of a lower group level, whose
/// client is there (session_isActive: not an autotrader), after pc_macro_reporter_process.
fn atcommand_macrochecker_sub(c: &Ctx, tsd: u32, reporter_aid: u32, reporter_gmlv: u32) -> i32 {
    // Dont start the macro checking on self
    if tsd == reporter_aid {
        return 0;
    }
    // Dont start it on other GMs with same or higher level (int32 compared with uint32: unsigned comparison)
    if crate::pc::group_level(c, tsd) as u32 >= reporter_gmlv {
        return 0;
    }
    // Dont start it on autotraders
    if !crate::pc::is_active_session(c, tsd) {
        return 0;
    }
    // Start the macro checking on the player
    pc_macro_reporter_process(c, tsd, reporter_aid);
    1
}

/// ACMD_FUNC(macrochecker) {<map>}: a macro check (captcha) of every lower-level player of the map (this one without
/// a name), refused on a nomacrochecker map and within macrochecker_delay of the map's last check; msg 1538 with the
/// number of players. The clif_macro_checker answers send nothing at this PACKETVER, so an unknown map, the map flag
/// and the cooldown only show the dispatcher's "%s failed.".
fn macrochecker(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let Some(me) = unit::get(c, sd) else { return -1 };
    let mapid = if message.is_empty() {
        me.map
    } else {
        match data::maps().id_of(message) {
            // map_mapname2mapid
            Some(m) => m,
            None => {
                clif_macro_checker(c, sd, MACROCHECKER_UNKNOWN_MAP);
                return -1;
            }
        }
    };
    let Some(md) = data::maps().get(mapid) else {
        return -1; // Should not happen
    };
    if npc::mapflag(c, mapid, MF_NOMACROCHECKER) {
        clif_macro_checker(c, sd, MACROCHECKER_MAPFLAG);
        return -1;
    }
    if c.tick() - last_macrocheck(c, mapid) < macrochecker_delay() {
        clif_macro_checker(c, sd, MACROCHECKER_COOLDOWN);
        return -1;
    }
    // map_foreachinmap(atcommand_macrochecker_sub, mapid, BL_PC, account_id, pc_get_group_level(sd))
    let reporter_gmlv = crate::pc::group_level(c, sd) as u32;
    let mut count = 0;
    for pl in crate::map::units_in_area(c, mapid, 0, 0, md.xs - 1, md.ys - 1, crate::map::BL_PC) {
        count += atcommand_macrochecker_sub(c, pl.id, sd, reporter_gmlv);
    }
    clif_macro_checker(c, sd, MACROCHECKER_SUCCESS);
    out.say(&format_msg(msg_txt(1538), &[&count])); // Macro detection has been started on %d players.
    if count > 0 {
        set_last_macrocheck(c, mapid, c.tick());
    }
    0
}

#[cfg(test)]
mod tests {
    use super::*;

    /// 2026-09-28 12:00:00 UTC.
    const NOW: i64 = 1_790_596_800;

    #[test]
    fn split_time_parts() {
        assert_eq!(split_time(3900), (0, 0, 0, 1, 5, 0));
        assert_eq!(split_time(300), (0, 0, 0, 0, 5, 0));
        assert_eq!(split_time(31556926 + 2629743 + 86400 + 61), (1, 1, 1, 0, 1, 1));
        assert_eq!(split_time(-600), (0, 0, 0, 0, 0, 0));
    }

    #[test]
    fn solve_time_units() {
        assert_eq!(solve_time_at(NOW, "+5mn"), 300.0);
        assert_eq!(solve_time_at(NOW, "5n"), 300.0);
        assert_eq!(solve_time_at(NOW, "+1h"), 3600.0);
        assert_eq!(solve_time_at(NOW, "-2h"), -7200.0);
        assert_eq!(solve_time_at(NOW, "10s"), 10.0);
        assert_eq!(solve_time_at(NOW, "1d"), 86400.0);
        assert_eq!(solve_time_at(NOW, "1j"), 86400.0);
        // September 28 → October 28: 30 days; 2026-09-28 → 2027-09-28: 365 days
        assert_eq!(solve_time_at(NOW, "1m"), 30.0 * 86400.0);
        assert_eq!(solve_time_at(NOW, "+1y"), 365.0 * 86400.0);
        assert_eq!(solve_time_at(NOW, "1a"), 365.0 * 86400.0);
        // no unit, not a number, zero
        assert_eq!(solve_time_at(NOW, "5"), 0.0);
        assert_eq!(solve_time_at(NOW, "abc"), 0.0);
        assert_eq!(solve_time_at(NOW, "0h"), 0.0);
        // several parts; an unknown unit letter is skipped
        assert_eq!(solve_time_at(NOW, "1h30mn"), 5400.0);
        assert_eq!(solve_time_at(NOW, "2x3s"), 3.0);
    }

    #[test]
    fn mktime_matches_localtime() {
        let t = crate::script::npc_run::localtime(NOW);
        assert_eq!(mktime_local(&t), NOW);
        // 61 minutes carry into the hour, 13 months into the year
        let mut u = t;
        u.min += 61;
        assert_eq!(mktime_local(&u), NOW + 61 * 60);
        let mut v = t;
        v.mon += 12;
        v.mday -= 1;
        assert_eq!(mktime_local(&v), NOW + (365 - 1) * 86400);
    }

    #[test]
    fn release_date_string() {
        // 12:00 UTC is 09:00 in the server's zone (UTC-3)
        assert_eq!(timestamp2string(20, NOW, "%Y-%m-%d %H:%M"), "2026-09-28 09:00");
        assert_eq!(timestamp2string(10, NOW, "%Y-%m-%d %H:%M"), "");
    }

    #[test]
    fn npc_path() {
        assert_eq!(atcommand_normalize_npc_path("npc\\custom\\a.txt"), "npc/custom/a.txt");
    }
}
