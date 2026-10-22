//! Player / NPC / map information and movement: warp, getmapxy, strcharinfo, strnpcinfo, getcharid, getnpcid,
//! readparam, isloggedin, getusers, getmapusers, jobname, atcommand / charcommand, mapid2name, mapname2id.

use super::*;
use crate::map::{BL_NPC, BL_PC};
use crate::npc::npc;
use crate::script::npc_run;
use crate::script::reg;
use crate::script::vm::*;
use crate::unit;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "warp" => warp,
        "getmapxy" => getmapxy,
        "strcharinfo" => strcharinfo,
        "strnpcinfo" => strnpcinfo,
        "getcharid" => getcharid,
        "getnpcid" => getnpcid,
        "readparam" => readparam,
        "isloggedin" => isloggedin,
        "getusers" => getusers,
        "getmapusers" => getmapusers,
        "jobname" => jobname,
        "atcommand" => atcommand,
        "mapid2name" => mapid2name,
        "mapname2id" => mapname2id,
        _ => return None,
    })
}

/// pc.cpp pc_randomwarp: a random free cell of the current map (0 = warped).
pub(crate) fn pc_randomwarp(c: &crate::world::Ctx, aid: u32, clrtype: u8, ignore_mapflag: bool) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return 3 };
    let m = sd.map;
    let Some(md) = crate::data::maps().get(m) else { return 3 };
    if crate::npc::mapflag(c, m, crate::npc::MF_NOTELEPORT) && !ignore_mapflag {
        return 3;
    }
    let edge = crate::map::map_edge_size() as i32;
    let mut i = 0;
    let (mut x, mut y);
    loop {
        x = c.rnd_value(edge, md.xs as i32 - edge - 1) as i16;
        y = c.rnd_value(edge, md.ys as i32 - edge - 1) as i16;
        let blocked = crate::map::getcellp(md, x, y, crate::map::CellChk::ChkNoPass) || (!crate::conf::teleport_on_portal() && crate::npc::check_areanpc(c, 1, m, x, y, 1));
        let again = blocked && {
            let r = i < 1000;
            i += 1;
            r
        };
        if !again {
            break;
        }
    }
    if i < 1000 {
        return if crate::pc::setpos(c, aid, m, x, y, clrtype) == crate::pc::SetPos::Ok { 0 } else { 1 };
    }
    3
}

/// warp / warpchar.
fn warp(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 5, "buildin_warp") else { return SCRIPT_CMD_SUCCESS };
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let s = getstr(rt, st, 2);
    let x = getnum(rt, st, 3);
    let y = getnum(rt, st, 4);
    let name = lat(&s);
    let ret = if s == b"Random" {
        pc_randomwarp(c, aid, unit::CLR_TELEPORT, true)
    } else if s == b"SavePoint" || s == b"Save" {
        let ch = crate::pc::character_of(c, aid);
        match ch.and_then(|ch| crate::data::maps().id_of(&ch.save_map).map(|m| (m, ch.save_x, ch.save_y))) {
            Some((m, sx, sy)) => (crate::pc::setpos(c, aid, m, sx as i16, sy as i16, unit::CLR_TELEPORT) != crate::pc::SetPos::Ok) as i32,
            None => 1,
        }
    } else {
        match crate::data::maps().id_of(&name) {
            Some(m) => (crate::pc::setpos(c, aid, m, x as i16, y as i16, unit::CLR_OUTSIGHT) != crate::pc::SetPos::Ok) as i32,
            None => 1, // mapindex_name2id: 0 → SETPOS_MAPINDEX
        }
    };
    if ret != 0 {
        let pname = unit::get(c, aid).map(|u| u.name).unwrap_or_default();
        log::error!("buildin_warp: moving player '{pname}' to \"{name}\",{x},{y} failed.");
        return SCRIPT_CMD_FAILURE;
    }
    SCRIPT_CMD_SUCCESS
}

/// getmapxy.
fn getmapxy(rt: &Rt, st: &mut State) -> i32 {
    for (i, what) in [(2, "mapname"), (3, "mapx"), (4, "mapy")] {
        if !st.data(i).is_reference() {
            log::warn!("script: buildin_getmapxy: {what} value is not a variable.");
            st.pushint(-1);
            return SCRIPT_CMD_FAILURE;
        }
    }
    let name_at = |st: &State, i: i32| match st.data(i) {
        Data::Name(id, ..) => name_of(*id),
        _ => Vec::new(),
    };
    if !is_string_variable(&name_at(st, 2)) {
        log::warn!("script: buildin_getmapxy: {} is not a string variable.", lat(&name_at(st, 2)));
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    }
    for i in [3, 4] {
        if is_string_variable(&name_at(st, i)) {
            log::warn!("script: buildin_getmapxy: {} is a string variable, should be an INT.", lat(&name_at(st, i)));
            st.pushint(-1);
            return SCRIPT_CMD_FAILURE;
        }
    }
    let Some(c) = rt.c else {
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    let kind = if st.hasdata(5) { getnum(rt, st, 5) as u16 } else { BL_PC };
    let pc_target = |st: &mut State| -> Option<u32> {
        if st.hasdata(6) && isstring(rt, st, 6) {
            if let Some(a) = nick2sd(rt, st, 6, "buildin_getmapxy") {
                return Some(a);
            }
        }
        accid2sd(rt, st, 6, "buildin_getmapxy")
    };
    // the object's bl->m (-1 for a floating NPC and the fake NPC; a player's map is set from pc_setpos on, loading or
    // not) and position
    let bl: Option<(i32, i64, i64)> = match kind {
        BL_PC => pc_target(st).and_then(|a| unit::get(c, a)).map(|u| (u.map as i32, u.x as i64, u.y as i64)),
        BL_NPC => {
            let nd = if st.hasdata(6) {
                if isstring(rt, st, 6) {
                    let n = getstr(rt, st, 6);
                    npc_run::npc_name2id(c, &lat(&n))
                } else {
                    // map_id2nd
                    let id = getnum(rt, st, 6) as u32;
                    c.db().npc().id().find(id)
                }
            } else {
                //In case the origin is not an NPC? (map_id2bl(st->oid))
                c.db().npc().id().find(st.oid)
            };
            match nd {
                Some(nd) => Some((nd.map, nd.x as i64, nd.y as i64)),
                None if !st.hasdata(6) => unit::get(c, st.oid).map(|u| (u.map as i32, u.x as i64, u.y as i64)),
                None => None,
            }
        }
        crate::map::BL_PET => {
            // Get Pet Position: sd->pd (pet/data.rs)
            pc_target(st).and_then(|a| crate::pet::data::of_master(c, a)).and_then(|pd| unit::get(c, pd.id)).map(|u| (u.map as i32, u.x as i64, u.y as i64))
        }
        crate::map::BL_HOM => {
            // Get Homun Position: sd->hd (script.cpp:16523-16526, homunculus/buildins.rs)
            let sd = pc_target(st);
            crate::homunculus::buildins::getmapxy_hom(c, sd)
        }
        crate::map::BL_MER => {
            //Get Mercenary Position: sd->md (script.cpp:16527-16530, mercenary/data.rs)
            pc_target(st).and_then(|a| crate::mercenary::data::of_master(c, a)).and_then(|md| unit::get(c, md.id)).map(|u| (u.map as i32, u.x as i64, u.y as i64))
        }
        crate::map::BL_ELEM => {
            //Get Elemental Position: sd->ed (script.cpp:16531-16534, elemental/data.rs)
            pc_target(st).and_then(|a| crate::elemental::data::of_master(c, a)).and_then(|ed| unit::get(c, ed.id)).map(|u| (u.map as i32, u.x as i64, u.y as i64))
        }
        _ => {
            log::warn!("script: buildin_getmapxy: Invalid type {kind}.");
            st.pushint(-1);
            return SCRIPT_CMD_FAILURE;
        }
    };
    let Some((m, x, y)) = bl else {
        //No object found.
        st.pushint(-1);
        return SCRIPT_CMD_SUCCESS;
    };
    // script.cpp:16547-16550: the map's name, or "" when bl->m < 0
    let mapname: Vec<u8> = if m >= 0 {
        crate::data::maps().get(m as u16).map(|d| crate::net::wire::latin1(&d.name)).unwrap_or_default()
    } else {
        Vec::new()
    };
    for (i, val) in [(2, None), (3, Some(x)), (4, Some(y))] {
        let Data::Name(id, idx, rf) = st.data(i).clone() else { continue };
        let name = name_of(id);
        let mut sd = None;
        if not_server_variable(name[0]) {
            sd = rid2sd(rt, st, "buildin_getmapxy");
            if sd.is_none() {
                log::error!("buildin_getmapxy: variable '{}' is not a server variable, but no player is attached!", lat(&name));
                return SCRIPT_CMD_FAILURE;
            }
        }
        match val {
            None => {
                reg::set_reg_str(rt, st, sd, id, idx, &name, &mapname, &rf);
            }
            Some(v) => {
                reg::set_reg_num(rt, st, sd, id, idx, &name, v, &rf);
            }
        }
    }
    st.pushint(0);
    SCRIPT_CMD_SUCCESS
}

/// strcharinfo.
fn strcharinfo(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_strcharinfo") else {
        st.pushstr(Vec::new());
        return SCRIPT_CMD_FAILURE;
    };
    let c = rt.c.unwrap();
    let num = getnum(rt, st, 2);
    let v = match num {
        0 => unit::get(c, aid).map(|u| crate::net::wire::latin1(&u.name)).unwrap_or_default(),
        // party name (party_search(sd->status.party_id) ? p->party.name : "")
        1 => crate::net::wire::latin1(&crate::party::party_name_of(c, aid)),
        // guild name (sd->guild ? sd->guild->guild.name : "")
        2 => crate::guild::sd_guild(c, aid).map(|g| crate::net::wire::latin1(&g.guild.name)).unwrap_or_default(),
        3 => unit::get(c, aid).and_then(|u| crate::data::maps().get(u.map).map(|d| crate::net::wire::latin1(&d.name))).unwrap_or_default(),
        _ => {
            log::warn!("buildin_strcharinfo: unknown parameter.");
            Vec::new()
        }
    };
    st.pushstr(v);
    SCRIPT_CMD_SUCCESS
}

/// strnpcinfo.
fn strnpcinfo(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else {
        st.pushstr(Vec::new());
        return SCRIPT_CMD_SUCCESS;
    };
    let id = if st.hasdata(3) { getnum(rt, st, 3) as u32 } else { st.oid };
    let Some(nd) = c.db().npc().id().find(id) else {
        st.pushstr(Vec::new());
        return SCRIPT_CMD_SUCCESS;
    };
    let num = getnum(rt, st, 2);
    let name = crate::net::wire::latin1(&nd.name);
    let v = match num {
        0 => name,
        1 => match name.iter().position(|&b| b == b'#') {
            Some(i) => name[..i].to_vec(),
            None => name,
        },
        2 => match name.iter().position(|&b| b == b'#') {
            Some(i) => name[i + 1..].to_vec(),
            None => Vec::new(),
        },
        3 => crate::net::wire::latin1(&nd.exname),
        4 => {
            if nd.map >= 0 {
                crate::data::maps().get(nd.map as u16).map(|d| crate::net::wire::latin1(&d.name)).unwrap_or_default()
            } else {
                Vec::new()
            }
        }
        5 => crate::net::wire::latin1(&nd.path),
        _ => Vec::new(),
    };
    st.pushstr(v);
    SCRIPT_CMD_SUCCESS
}

/// getcharid.
fn getcharid(rt: &Rt, st: &mut State) -> i32 {
    let num = getnum(rt, st, 2);
    let Some(aid) = nick2sd(rt, st, 3, "buildin_getcharid") else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let c = rt.c.unwrap();
    let ch = crate::pc::character_of(c, aid);
    let v = match num {
        0 => ch.map(|c| c.id as i64).unwrap_or(0),
        1 => ch.map(|c| c.party_id as i64).unwrap_or(0),
        2 => ch.map(|c| c.guild_id as i64).unwrap_or(0),
        3 => aid as i64,
        4 => crate::battleground::bg_id_of(c, aid) as i64, // sd->bg_id
        5 => ch.map(|c| c.clan_id as i64).unwrap_or(0),
        _ => {
            log::error!("buildin_getcharid: invalid parameter ({num}).");
            0
        }
    };
    st.pushint(v);
    SCRIPT_CMD_SUCCESS
}

/// getnpcid.
fn getnpcid(rt: &Rt, st: &mut State) -> i32 {
    let num = getnum(rt, st, 2);
    let mut nd_id = None;
    if st.hasdata(3) {
        let n = getstr(rt, st, 3);
        match rt.c.and_then(|c| npc_run::npc_name2id(c, &lat(&n))) {
            Some(nd) => nd_id = Some(nd.id),
            None => {
                st.pushint(0);
                return SCRIPT_CMD_SUCCESS;
            }
        }
    }
    match num {
        0 => st.pushint(nd_id.unwrap_or(st.oid) as i64),
        _ => {
            log::error!("buildin_getnpcid: invalid parameter ({num}).");
            st.pushint(0);
            return SCRIPT_CMD_FAILURE;
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// readparam.
fn readparam(rt: &Rt, st: &mut State) -> i32 {
    let sd = if st.hasdata(3) {
        if isint(rt, st, 3) {
            charid2sd(rt, st, 3, "buildin_readparam")
        } else {
            nick2sd(rt, st, 3, "buildin_readparam")
        }
    } else {
        rid2sd(rt, st, "buildin_readparam")
    };
    let Some(aid) = sd else {
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    let c = rt.c.unwrap();
    let param = match st.data(2) {
        Data::Name(id, ..) if str_type(*id).0 == crate::script::compiler::C_PARAM => Some(str_type(*id).1),
        _ => None,
    };
    let typ = match param {
        Some(t) => t,
        None => getnum(rt, st, 2) as i64,
    };
    match crate::script::param::readparam(c, aid, typ) {
        Some(v) => st.pushint(v as i32 as i64),
        None => {
            log::error!("readparam: parameter {typ} is not available on this server yet");
            reportsrc(rt, st);
            st.state = END;
            st.pushint(0);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// isloggedin.
fn isloggedin(rt: &Rt, st: &mut State) -> i32 {
    let aid = getnum(rt, st, 2) as u32;
    let Some(c) = rt.c else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let mut there = crate::pc::get(c, aid);
    if st.hasdata(3) {
        let cid = getnum(rt, st, 3) as u32;
        if there.as_ref().map(|p| p.char_id != cid).unwrap_or(false) {
            there = None;
        }
    }
    st.pushint(there.is_some() as i64);
    SCRIPT_CMD_SUCCESS
}

/// map_getmapdata(m)->users: players who have loaded the map.
fn map_users(c: &crate::world::Ctx, m: u16) -> i64 {
    crate::map::players_on_map(c, m).len() as i64
}

/// getusers.
fn getusers(rt: &Rt, st: &mut State) -> i32 {
    let flag = getnum(rt, st, 2);
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let val = match flag & 0x07 {
        0 => {
            let bl = if flag & 0x8 != 0 { unit::get(c, st.oid) } else { rid2sd(rt, st, "buildin_getusers").and_then(|a| unit::get(c, a)) };
            bl.map(|b| map_users(c, b.map)).unwrap_or(0)
        }
        // map_getusers: the char server's online count
        1 => c.db().pc().iter().count() as i64,
        _ => {
            log::warn!("buildin_getusers: Unknown type {flag}.");
            st.pushint(0);
            return SCRIPT_CMD_FAILURE;
        }
    };
    st.pushint(val);
    SCRIPT_CMD_SUCCESS
}

use crate::pc::pc as _;
use spacetimedb::Table as _;

/// getmapusers.
fn getmapusers(rt: &Rt, st: &mut State) -> i32 {
    let s = getstr(rt, st, 2);
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    match crate::data::maps().id_of(&lat(&s)) {
        Some(m) => st.pushint(map_users(c, m)),
        None => st.pushint(-1),
    }
    SCRIPT_CMD_SUCCESS
}

/// pc.cpp job_name: the msg_txt name of a class.
pub fn job_name(class: i32) -> &'static str {
    let k = |n: &str| crate::data::constant(&format!("JOB_{n}")).unwrap_or(-9999) as i32;
    let groups: &[(&[&str], i32, Option<&str>)] = &[
        (&["NOVICE", "SWORDMAN", "MAGE", "ARCHER", "ACOLYTE", "MERCHANT", "THIEF"], 550, Some("NOVICE")),
        (&["KNIGHT", "PRIEST", "WIZARD", "BLACKSMITH", "HUNTER", "ASSASSIN"], 557, Some("KNIGHT")),
        (&["KNIGHT2"], 557, None),
        (&["CRUSADER", "MONK", "SAGE", "ROGUE", "ALCHEMIST", "BARD", "DANCER"], 563, Some("CRUSADER")),
        (&["CRUSADER2"], 563, None),
        (&["WEDDING", "SUPER_NOVICE", "GUNSLINGER", "NINJA", "XMAS"], 570, Some("WEDDING")),
        (&["SUMMER", "SUMMER2"], 621, None),
        (&["HANBOK"], 694, None),
        (&["OKTOBERFEST"], 696, None),
        (&["NOVICE_HIGH", "SWORDMAN_HIGH", "MAGE_HIGH", "ARCHER_HIGH", "ACOLYTE_HIGH", "MERCHANT_HIGH", "THIEF_HIGH"], 575, Some("NOVICE_HIGH")),
        (&["LORD_KNIGHT", "HIGH_PRIEST", "HIGH_WIZARD", "WHITESMITH", "SNIPER", "ASSASSIN_CROSS"], 582, Some("LORD_KNIGHT")),
        (&["LORD_KNIGHT2"], 582, None),
        (&["PALADIN", "CHAMPION", "PROFESSOR", "STALKER", "CREATOR", "CLOWN", "GYPSY"], 588, Some("PALADIN")),
        (&["PALADIN2"], 588, None),
        (&["BABY", "BABY_SWORDMAN", "BABY_MAGE", "BABY_ARCHER", "BABY_ACOLYTE", "BABY_MERCHANT", "BABY_THIEF"], 595, Some("BABY")),
        (&["BABY_KNIGHT", "BABY_PRIEST", "BABY_WIZARD", "BABY_BLACKSMITH", "BABY_HUNTER", "BABY_ASSASSIN"], 602, Some("BABY_KNIGHT")),
        (&["BABY_KNIGHT2"], 602, None),
        (&["BABY_CRUSADER", "BABY_MONK", "BABY_SAGE", "BABY_ROGUE", "BABY_ALCHEMIST", "BABY_BARD", "BABY_DANCER"], 608, Some("BABY_CRUSADER")),
        (&["BABY_CRUSADER2"], 608, None),
        (&["SUPER_BABY"], 615, None),
        (&["TAEKWON"], 616, None),
        (&["STAR_GLADIATOR", "STAR_GLADIATOR2"], 617, None),
        (&["SOUL_LINKER"], 618, None),
        (&["GANGSI", "DEATH_KNIGHT", "DARK_COLLECTOR"], 622, Some("GANGSI")),
        (&["RUNE_KNIGHT", "WARLOCK", "RANGER", "ARCH_BISHOP", "MECHANIC", "GUILLOTINE_CROSS"], 625, Some("RUNE_KNIGHT")),
        (&["RUNE_KNIGHT_T", "WARLOCK_T", "RANGER_T", "ARCH_BISHOP_T", "MECHANIC_T", "GUILLOTINE_CROSS_T"], 681, Some("RUNE_KNIGHT_T")),
        (&["ROYAL_GUARD", "SORCERER", "MINSTREL", "WANDERER", "SURA", "GENETIC", "SHADOW_CHASER"], 631, Some("ROYAL_GUARD")),
        (&["ROYAL_GUARD_T", "SORCERER_T", "MINSTREL_T", "WANDERER_T", "SURA_T", "GENETIC_T", "SHADOW_CHASER_T"], 687, Some("ROYAL_GUARD_T")),
        (&["RUNE_KNIGHT2", "RUNE_KNIGHT_T2"], 625, None),
        (&["ROYAL_GUARD2", "ROYAL_GUARD_T2"], 631, None),
        (&["RANGER2", "RANGER_T2"], 627, None),
        (&["MECHANIC2", "MECHANIC_T2"], 629, None),
        (
            &[
                "BABY_RUNE_KNIGHT", "BABY_WARLOCK", "BABY_RANGER", "BABY_ARCH_BISHOP", "BABY_MECHANIC", "BABY_GUILLOTINE_CROSS", "BABY_ROYAL_GUARD", "BABY_SORCERER",
                "BABY_MINSTREL", "BABY_WANDERER", "BABY_SURA", "BABY_GENETIC", "BABY_SHADOW_CHASER",
            ],
            638,
            Some("BABY_RUNE_KNIGHT"),
        ),
        (&["BABY_RUNE_KNIGHT2"], 638, None),
        (&["BABY_ROYAL_GUARD2"], 644, None),
        (&["BABY_RANGER2"], 640, None),
        (&["BABY_MECHANIC2"], 642, None),
        (&["SUPER_NOVICE_E", "SUPER_BABY_E"], 651, Some("SUPER_NOVICE_E")),
        (&["KAGEROU", "OBORO"], 653, Some("KAGEROU")),
        (&["REBELLION"], 695, None),
        (&["SUMMONER"], 697, None),
        (&["BABY_SUMMONER"], 698, None),
        (&["BABY_NINJA"], 699, None),
        (&["BABY_KAGEROU", "BABY_OBORO", "BABY_TAEKWON", "BABY_STAR_GLADIATOR", "BABY_SOUL_LINKER", "BABY_GUNSLINGER", "BABY_REBELLION"], 753, Some("BABY_KAGEROU")),
        (&["BABY_STAR_GLADIATOR2"], 756, None),
        (&["STAR_EMPEROR", "SOUL_REAPER", "BABY_STAR_EMPEROR", "BABY_SOUL_REAPER"], 782, Some("STAR_EMPEROR")),
        (&["STAR_EMPEROR2"], 782, None),
        (&["BABY_STAR_EMPEROR2"], 784, None),
        (
            &[
                "DRAGON_KNIGHT", "MEISTER", "SHADOW_CROSS", "ARCH_MAGE", "CARDINAL", "WINDHAWK", "IMPERIAL_GUARD", "BIOLO", "ABYSS_CHASER", "ELEMENTAL_MASTER", "INQUISITOR",
                "TROUBADOUR", "TROUVERE",
            ],
            800,
            Some("DRAGON_KNIGHT"),
        ),
        (&["WINDHAWK2"], 805, None),
        (&["MEISTER2"], 801, None),
        (&["DRAGON_KNIGHT2"], 800, None),
        (&["IMPERIAL_GUARD2"], 806, None),
        (&["SKY_EMPEROR", "SOUL_ASCETIC", "SHINKIRO", "SHIRANUI", "NIGHT_WATCH", "HYPER_NOVICE", "SPIRIT_HANDLER"], 813, Some("SKY_EMPEROR")),
        (&["SKY_EMPEROR2"], 813, None),
    ];
    for (names, base, first) in groups {
        if names.iter().any(|n| k(n) == class) {
            let msg = match first {
                Some(f) => base - k(f) + class,
                None => *base,
            };
            return crate::data::msg_txt(msg as u16);
        }
    }
    crate::data::msg_txt(655)
}

/// jobname.
fn jobname(rt: &Rt, st: &mut State) -> i32 {
    let class = getnum(rt, st, 2);
    st.pushstr(crate::net::wire::latin1(job_name(class)));
    SCRIPT_CMD_SUCCESS
}

/// atcommand / charcommand: atcommand_sub with type 0 (script.cpp:15787-15789), as the attached player or, with none,
/// as rAthena's dummy character (script/dummy_sd.rs).
fn atcommand(rt: &Rt, st: &mut State) -> i32 {
    crate::script::dummy_sd::atcommand_sub(rt, st, 0)
}

/// mapid2name.
fn mapid2name(rt: &Rt, st: &mut State) -> i32 {
    let m = getnum(rt, st, 2) as u16;
    match crate::data::maps().get(m) {
        Some(d) => st.pushstr(crate::net::wire::latin1(&d.name)),
        None => {
            st.pushstr(Vec::new());
            return SCRIPT_CMD_FAILURE;
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// mapname2id.
fn mapname2id(rt: &Rt, st: &mut State) -> i32 {
    let s = getstr(rt, st, 2);
    match crate::data::maps().id_of(&lat(&s)) {
        Some(m) => st.pushint(m as i64),
        None => {
            st.pushint(-1);
            return SCRIPT_CMD_FAILURE;
        }
    }
    SCRIPT_CMD_SUCCESS
}
