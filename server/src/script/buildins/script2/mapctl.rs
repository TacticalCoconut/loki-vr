//! Map and area commands: savepoint / save, getsavepoint, mapwarp, areawarp, warpparty.

use crate::script::buildins::*;
use crate::map::CellChk;
use crate::npc::{mapflag, MF_NORETURN, MF_NOTELEPORT, MF_NOWARP};
use crate::party::{party_search, MAX_PARTY};
use crate::pc::{self, SetPos};
use crate::script::buildins::pcinfo::pc_randomwarp;
use crate::script::vm::*;
use crate::unit::{self, CLR_OUTSIGHT, CLR_TELEPORT};
use crate::world::Ctx;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "savepoint" => savepoint,
        "getsavepoint" => getsavepoint,
        "mapwarp" => mapwarp,
        "areawarp" => areawarp,
        "warpparty" => warpparty,
        _ => return None,
    })
}

/// mapindex_name2id / map_mapname2mapid (every map of the index is on this map server).
fn map_id(name: &[u8]) -> Option<u16> {
    crate::data::maps().id_of(&lat(name))
}

/// map_getcell(m, x, y, chk) with rAthena's int16 coordinates.
fn getcell(c: &Ctx, m: u16, x: i32, y: i32, chk: CellChk) -> bool {
    crate::map::getcell(c, m, x as i16, y as i16, chk)
}

/// pc.cpp pc_setpos with int32 coordinates (pc_setpos takes int32 x / y: anything outside the map, even beyond int16,
/// is its "invalid coordinates" case). Returns true for SETPOS_OK.
pub fn setpos32(c: &Ctx, aid: u32, m: u16, x: i32, y: i32, clrtype: u8) -> bool {
    let fit = |v: i32| if (i16::MIN as i32..=i16::MAX as i32).contains(&v) { v as i16 } else { -1 };
    pc::setpos(c, aid, m, fit(x), fit(y), clrtype) == SetPos::Ok
}

/// save / savepoint "<map name>",<x>,<y>{,{<range x>,<range y>,}<char_id>}: pc_setsavepoint.
fn savepoint(rt: &Rt, st: &mut State) -> i32 {
    let mut cid_pos = 5;
    if st.lastdata() > 5 {
        cid_pos = 7;
    }
    let Some(aid) = charid2sd(rt, st, cid_pos, "buildin_savepoint") else {
        return SCRIPT_CMD_FAILURE; // no player attached, report source
    };
    let c = rt.c.unwrap();
    let s = getstr(rt, st, 2);
    let Some(m) = map_id(&s) else { return SCRIPT_CMD_FAILURE };
    let mut x = getnum(rt, st, 3);
    let mut y = getnum(rt, st, 4);
    if cid_pos == 7 {
        let (dx, dy) = (getnum(rt, st, 5), getnum(rt, st, 6));
        let (x1, y1, x0, y0) = (x.wrapping_add(dx), y.wrapping_add(dy), x.wrapping_sub(dx), y.wrapping_sub(dy));
        let mut n: u8 = 10;
        loop {
            x = c.rnd_value(x0, x1);
            y = c.rnd_value(y0, y1);
            n -= 1;
            if !(n > 0 && !getcell(c, m, x, y, CellChk::ChkPass)) {
                break;
            }
        }
    }
    // Check for valid coordinates if map in local map-server
    if !getcell(c, m, x, y, CellChk::ChkPass) {
        log::error!("buildin_savepoint: Invalid coordinates {x},{y} at map {}.", lat(&s));
        return SCRIPT_CMD_FAILURE;
    }
    crate::pc_misc::pc_setsavepoint(c, aid, m, x, y);
    SCRIPT_CMD_SUCCESS
}

/// getsavepoint(<type>{,<char_id>}): 0 map name, 1 x, 2 y.
fn getsavepoint(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_getsavepoint") else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let kind = getnum(rt, st, 2);
    let ch = pc::character_of(rt.c.unwrap(), aid);
    match (kind, ch) {
        (0, Some(ch)) => st.pushstr(crate::net::wire::latin1(&ch.save_map)),
        (1, Some(ch)) => st.pushint(ch.save_x as i16 as i64),
        (2, Some(ch)) => st.pushint(ch.save_y as i16 as i64),
        _ => st.pushint(0),
    }
    SCRIPT_CMD_SUCCESS
}

/// script.cpp buildin_areawarp_sub: one player of the area. `index` None = "Random".
fn areawarp_sub(c: &Ctx, aid: u32, index: Option<u16>, x2: i32, y2: i32, x3: i32, y3: i32) {
    match index {
        None => {
            pc_randomwarp(c, aid, CLR_TELEPORT, true);
        }
        Some(m) if x3 != 0 && y3 != 0 => {
            // choose a suitable max number of attempts
            let mut max = (y3 - y2 + 1) * (x3 - x2 + 1) * 3;
            if max > 1000 {
                max = 1000;
            }
            // find a suitable map cell
            let (mut tx, mut ty);
            let mut j = 0;
            loop {
                tx = c.rnd_value(x2, x3);
                ty = c.rnd_value(y2, y3);
                j += 1;
                if !(getcell(c, m, tx, ty, CellChk::ChkNoPass) && j < max) {
                    break;
                }
            }
            setpos32(c, aid, m, tx, ty, CLR_OUTSIGHT);
        }
        Some(m) => {
            setpos32(c, aid, m, x2, y2, CLR_OUTSIGHT);
        }
    }
}

/// areawarp "<from map>",<x1>,<y1>,<x2>,<y2>,"<to map>",<x3>,<y3>{,<x4>,<y4>}: every player in the area.
fn areawarp(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let mapname = getstr(rt, st, 2);
    let x0 = getnum(rt, st, 3) as i16;
    let y0 = getnum(rt, st, 4) as i16;
    let x1 = getnum(rt, st, 5) as i16;
    let y1 = getnum(rt, st, 6) as i16;
    let s = getstr(rt, st, 7);
    let mut x2 = getnum(rt, st, 8) as i16;
    let mut y2 = getnum(rt, st, 9) as i16;
    let (mut x3, mut y3) = (0i16, 0i16);
    if st.hasdata(10) && st.hasdata(11) {
        // Warp area to area
        x3 = getnum(rt, st, 10) as i16;
        let bad = x3 < 0 || {
            y3 = getnum(rt, st, 11) as i16;
            y3 < 0
        };
        if bad {
            x3 = 0;
            y3 = 0;
        } else if x3 != 0 && y3 != 0 {
            // normalize x3/y3 coordinates
            if x3 < x2 {
                std::mem::swap(&mut x3, &mut x2);
            }
            if y3 < y2 {
                std::mem::swap(&mut y3, &mut y2);
            }
        }
    }
    let Some(m) = map_id(&mapname) else { return SCRIPT_CMD_FAILURE };
    let index = if s == b"Random" {
        None
    } else {
        match map_id(&s) {
            Some(i) => Some(i),
            None => return SCRIPT_CMD_FAILURE,
        }
    };
    // map_foreachinallarea(buildin_areawarp_sub, m, x0, y0, x1, y1, BL_PC, …)
    for pl in crate::map::units_in_area(c, m, x0, y0, x1, y1, crate::map::BL_PC) {
        areawarp_sub(c, pl.id, index, x2 as i32, y2 as i32, x3 as i32, y3 as i32);
    }
    SCRIPT_CMD_SUCCESS
}

/// mapwarp "<from map>","<to map>",<x>,<y>{,<type>,<ID>}: type 0 everyone, 1 a guild's members, 2 a party's.
fn mapwarp(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let mapname = getstr(rt, st, 2);
    let s = getstr(rt, st, 3);
    let x = getnum(rt, st, 4);
    let y = getnum(rt, st, 5);
    let (mut check_val, mut check_id) = (0, 0);
    if st.hasdata(7) {
        check_val = getnum(rt, st, 6);
        check_id = getnum(rt, st, 7);
    }
    let Some(m) = map_id(&mapname) else { return SCRIPT_CMD_SUCCESS };
    let Some(index) = map_id(&s) else { return SCRIPT_CMD_SUCCESS };
    match check_val {
        1 => {
            if let Some(g) = crate::guild::guild_search(c, check_id as i32) {
                for i in 0..g.guild.max_member.max(0) as usize {
                    let sd = g.guild.member.get(i).map(|m| m.sd).unwrap_or(0);
                    if sd != 0 && unit::get(c, sd).map(|u| u.on_map && u.map == m).unwrap_or(false) {
                        setpos32(c, sd, index, x, y, CLR_TELEPORT);
                    }
                }
            }
        }
        2 => {
            if let Some(p) = party_search(c, check_id as u32) {
                for i in 0..MAX_PARTY {
                    let sd = p.data.get(i).map(|d| d.sd).unwrap_or(0);
                    if sd != 0 && unit::get(c, sd).map(|u| u.on_map && u.map == m).unwrap_or(false) {
                        setpos32(c, sd, index, x, y, CLR_TELEPORT);
                    }
                }
            }
        }
        _ => {
            // map_foreachinmap(buildin_areawarp_sub, m, BL_PC, index, x, y, 0, 0)
            for pl in crate::map::players_on_map(c, m) {
                areawarp_sub(c, pl.id, Some(index), x, y, 0, 0);
            }
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// script.cpp e_warpparty_target.
#[derive(Clone, Copy, PartialEq, Eq)]
enum WarpParty {
    Random,
    SavePointAll,
    SavePoint,
    Leader,
    RandomAll,
    RandomAllArea,
}

/// warpparty "<to map>",<x>,<y>,<party id>{,"<from map>",<range x>,<range y>}.
fn warpparty(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let s = getstr(rt, st, 2);
    let mut x = getnum(rt, st, 3);
    let mut y = getnum(rt, st, 4);
    let p_id = getnum(rt, st, 5);
    let str2 = if st.hasdata(6) { Some(getstr(rt, st, 6)) } else { None };
    let rx = if st.hasdata(7) { getnum(rt, st, 7) } else { 0 };
    let ry = if st.hasdata(8) { getnum(rt, st, 8) } else { 0 };
    let Some(p) = party_search(c, p_id as u32) else { return SCRIPT_CMD_SUCCESS };
    let kind = match &s[..] {
        b"Random" => WarpParty::Random,
        b"SavePointAll" => WarpParty::SavePointAll,
        b"SavePoint" => WarpParty::SavePoint,
        b"Leader" => WarpParty::Leader,
        b"RandomAll" => WarpParty::RandomAll,
        _ => WarpParty::RandomAllArea,
    };
    let mut sd: Option<u32> = None;
    let mut mapindex: Option<u16> = None;
    match kind {
        WarpParty::SavePoint => {
            // "SavePoint" uses save point of the currently attached player
            sd = rid2sd(rt, st, "buildin_warpparty");
            if sd.is_none() {
                return SCRIPT_CMD_FAILURE;
            }
        }
        WarpParty::Leader => {
            let i = p.party.member.iter().take(MAX_PARTY).position(|m| m.leader);
            let leader_sd = i.and_then(|i| p.data.get(i)).map(|d| d.sd).unwrap_or(0);
            // Leader not found / not online
            let Some(lu) = (if leader_sd != 0 { unit::get(c, leader_sd) } else { None }) else { return SCRIPT_CMD_FAILURE };
            mapindex = pc::get(c, leader_sd).and_then(|pp| pp.mapindex);
            x = lu.x as i32;
            y = lu.y as i32;
        }
        WarpParty::RandomAll => {
            sd = rid2sd(rt, st, "buildin_warpparty");
            let Some(aid) = sd else { return SCRIPT_CMD_FAILURE };
            mapindex = pc::get(c, aid).and_then(|pp| pp.mapindex);
            let Some(m) = mapindex else { return SCRIPT_CMD_FAILURE };
            let Some(md) = crate::data::maps().get(m) else { return SCRIPT_CMD_FAILURE };
            if mapflag(c, m, MF_NOWARP) || mapflag(c, m, MF_NOTELEPORT) {
                return SCRIPT_CMD_FAILURE;
            }
            let mut i = 0;
            loop {
                x = c.rnd_value(1, md.xs as i32 - 1);
                y = c.rnd_value(1, md.ys as i32 - 1);
                let blocked = getcell(c, m, x, y, CellChk::ChkNoPass) || (!crate::conf::teleport_on_portal() && crate::npc::check_areanpc(c, 1, m, x as i16, y as i16, 1));
                let again = blocked && {
                    let r = i < 1000;
                    i += 1;
                    r
                };
                if !again {
                    break;
                }
            }
            if i >= 1000 {
                let name = unit::get(c, aid).map(|u| u.name).unwrap_or_default();
                log::error!("buildin_warpparty: moving player '{name}' to \"{}\",{x},{y} failed.", lat(&s));
                return SCRIPT_CMD_FAILURE;
            }
        }
        WarpParty::RandomAllArea => {
            mapindex = map_id(&s);
            if mapindex.is_none() {
                // Invalid map
                return SCRIPT_CMD_FAILURE;
            }
        }
        _ => {}
    }
    let m = mapindex;
    let save_of = |aid: u32| pc::character_of(c, aid).and_then(|ch| crate::data::maps().id_of(&ch.save_map).map(|sm| (sm, ch.save_x as i16 as i32, ch.save_y as i16 as i32)));
    for i in 0..MAX_PARTY {
        let pl = p.data.get(i).map(|d| d.sd).unwrap_or(0);
        if pl == 0 || crate::party::party_id_of(c, pl) != p_id as u32 {
            continue;
        }
        let Some(pu) = unit::get(c, pl) else { continue };
        let pl_map_name = crate::data::maps().get(pu.map).map(|d| d.name.clone()).unwrap_or_default();
        if let Some(s2) = &str2 {
            if s2[..] != crate::net::wire::latin1(&pl_map_name)[..] {
                continue;
            }
        }
        if pc::isdead(c, pl) {
            continue;
        }
        let mut ok = true;
        let mut fall_area = false;
        match kind {
            WarpParty::Random => {
                if !mapflag(c, pu.map, MF_NOWARP) {
                    ok = pc_randomwarp(c, pl, CLR_TELEPORT, false) == 0;
                }
            }
            WarpParty::SavePointAll => {
                if !mapflag(c, pu.map, MF_NORETURN) {
                    ok = match save_of(pl) {
                        Some((sm, sx, sy)) => setpos32(c, pl, sm, sx, sy, CLR_TELEPORT),
                        None => false, // mapindex_name2id: 0 → SETPOS_MAPINDEX
                    };
                }
            }
            WarpParty::SavePoint => {
                if !mapflag(c, pu.map, MF_NORETURN) {
                    ok = match save_of(sd.unwrap_or(0)) {
                        Some((sm, sx, sy)) => setpos32(c, pl, sm, sx, sy, CLR_TELEPORT),
                        None => false,
                    };
                }
            }
            WarpParty::Leader => {
                if p.party.member.get(i).map(|mm| mm.leader).unwrap_or(false) {
                    continue;
                }
                fall_area = true;
            }
            WarpParty::RandomAll => {
                if Some(pl) == sd {
                    ok = match m {
                        Some(mi) => setpos32(c, pl, mi, x, y, CLR_TELEPORT),
                        None => false,
                    };
                } else {
                    fall_area = true;
                }
            }
            WarpParty::RandomAllArea => fall_area = true,
        }
        if fall_area {
            let class = pc::character_of(c, pl).map(|ch| ch.class as i32).unwrap_or(0);
            let can_enter = m.map(|mi| pc::job_can_entermap(c, class, mi, pc::group_level(c, pl))).unwrap_or(false);
            if !mapflag(c, pu.map, MF_NORETURN) && !mapflag(c, pu.map, MF_NOWARP) && can_enter {
                let mi = m.unwrap();
                let (mut nx, mut ny) = (x, y);
                if rx != 0 || ry != 0 {
                    let mut attempts: u8 = 10;
                    loop {
                        nx = x + c.rnd_value(-rx, rx);
                        ny = y + c.rnd_value(-ry, ry);
                        attempts -= 1;
                        if !(attempts > 0 && !getcell(c, mi, nx, ny, CellChk::ChkPass)) {
                            break;
                        }
                    }
                    if attempts == 0 {
                        // Keep the original coordinates if fails to find a valid cell within the range
                        nx = x;
                        ny = y;
                    }
                }
                ok = setpos32(c, pl, mi, nx, ny, CLR_TELEPORT);
            }
        }
        if !ok {
            log::error!("buildin_warpparty: moving player '{}' to \"{}\",{x},{y} failed.", pu.name, lat(&s));
            if (kind == WarpParty::RandomAll || kind == WarpParty::RandomAllArea) && (rx != 0 || ry != 0) {
                continue;
            } else {
                return SCRIPT_CMD_FAILURE;
            }
        }
    }
    SCRIPT_CMD_SUCCESS
}
