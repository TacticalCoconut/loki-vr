//! script.cpp's BattleGround commands (BUILDIN_FUNC waitingroom2bg â€¦ bg_info, script.cpp:21208-21639), over
//! battleground.rs.

use crate::battleground as bg;
use crate::db::misc::battleground::{battleground_db, bg_search_name, BattlegroundTeam};
use crate::script::buildins::*;
use crate::script::reg;
use crate::script::vm::*;
use crate::world::Ctx;
use crate::battleground::bg_team as _;
use crate::npc::npc as _;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "waitingroom2bg" => waitingroom2bg,
        "waitingroom2bg_single" => waitingroom2bg_single,
        "bg_create" => bg_create,
        "bg_join" => bg_join,
        "bg_team_setxy" => bg_team_setxy,
        "bg_warp" => bg_warp,
        "bg_monster" => bg_monster,
        "bg_monster_set_team" => bg_monster_set_team,
        "bg_leave" => bg_leave,
        "bg_destroy" => bg_destroy,
        "bg_getareausers" => bg_getareausers,
        "bg_updatescore" => bg_updatescore,
        "bg_get_data" => bg_get_data,
        "bg_reserve" => bg_reserve,
        "bg_unbook" => bg_unbook,
        "bg_info" => bg_info,
        _ => return None,
    })
}

/// script.cpp check_event.
fn check_event(rt: &Rt, st: &State, evt: &[u8]) {
    if !evt.is_empty() && !evt.to_ascii_lowercase().windows(4).any(|w| w == b"::on") {
        log::warn!("NPC event parameter deprecated! Please use 'NPCNAME::OnEVENT' instead of '{}'.", lat(evt));
        reportsrc(rt, st);
    }
}

/// mapindex_name2id: Some(map) for a known map.
fn mapindex(name: &[u8]) -> Option<u16> {
    crate::data::maps().id_of(&lat(name))
}

/// map_getmapflag(map_mapindex2mapid(mapindex), MF_BATTLEGROUND): mapindex 0 (None) is map id -1, for which
/// map_getmapflag_sub answers -1 ("Invalid map ID"), a true value.
fn bg_flag(c: &Ctx, m: Option<u16>) -> bool {
    match m {
        Some(m) => crate::npc::mapflag(c, m, crate::npc::MF_BATTLEGROUND),
        None => {
            log::warn!("map_getmapflag: Invalid map ID -1.");
            true
        }
    }
}

/// pc_setpos(sd, mapindex, x, y, CLR_TELEPORT) == SETPOS_OK (mapindex 0 is refused).
fn setpos_ok(c: &Ctx, aid: u32, m: Option<u16>, x: i16, y: i16) -> bool {
    match m {
        Some(m) => crate::pc::setpos(c, aid, m, x, y, crate::unit::CLR_TELEPORT) == crate::pc::SetPos::Ok,
        None => false,
    }
}

/// The team definition of waitingroom2bg / bg_create (args 3, 4 respawn cell, 5 quit event, 6 death event).
fn team_args(rt: &Rt, st: &mut State) -> BattlegroundTeam {
    let mut team = BattlegroundTeam { warp_x: getnum(rt, st, 3) as u16, warp_y: getnum(rt, st, 4) as u16, ..Default::default() };
    if st.hasdata(5) {
        let ev = getstr(rt, st, 5); // Logout Event
        check_event(rt, st, &ev);
        team.quit_event = lat(&ev);
    }
    if st.hasdata(6) {
        let ev = getstr(rt, st, 6); // Die Event
        check_event(rt, st, &ev);
        team.death_event = lat(&ev);
    }
    team
}

/// waitingroom2bg "<map>",<x>,<y>{,"<quit event>","<death event>"{,"<npc>"}}: a team of the NPC's waiting room.
fn waitingroom2bg(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let npc_id = if st.hasdata(7) {
        let n = getstr(rt, st, 7);
        crate::script::npc_run::npc_name2id(c, &lat(&n)).map(|nd| nd.id)
    } else {
        c.db().npc().id().find(st.oid).map(|nd| nd.id)
    };
    let cd = npc_id.and_then(|id| crate::chat::get(c, crate::chat::npc_chat_id(c, id)));
    let Some(cd) = cd else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let map_name = getstr(rt, st, 2);
    let m = if map_name != b"-" {
        match mapindex(&map_name) {
            Some(m) => Some(m),
            None => {
                // Invalid Map
                st.pushint(0);
                return SCRIPT_CMD_SUCCESS;
            }
        }
    } else {
        None
    };
    let team = team_args(rt, st);
    let bg_id = bg::bg_create(c, m, &team);
    // (bg_create never fails)
    let mut n: u8 = 0;
    // Only add those who are in the chat room (cd->users is read again each round)
    let mut i: u8 = 0;
    loop {
        let users = crate::chat::get(c, cd.id).map(|r| r.users).unwrap_or_default();
        if (i as usize) >= users.len() {
            break;
        }
        let aid = users[i as usize];
        if bg::bg_team_join(c, bg_id, aid, false) {
            crate::party::mapreg_setreg(c, "$@arenamembers", n as u32, aid as i64);
            n = n.wrapping_add(1);
        }
        i = i.wrapping_add(1);
    }
    crate::party::mapreg_setreg(c, "$@arenamembersnum", 0, n as i64);
    st.pushint(bg_id as i64);
    SCRIPT_CMD_SUCCESS
}

/// waitingroom2bg_single <team>{,"<map>",<x>,<y>},"<npc>": the first player of the NPC's waiting room joins and warps.
fn waitingroom2bg_single(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let bg_id = getnum(rt, st, 2);
    let Some(t) = bg::team(c, bg_id) else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let (m, x, y) = if st.hasdata(3) {
        let map_name = getstr(rt, st, 3);
        let Some(m) = mapindex(&map_name) else {
            st.pushint(0);
            return SCRIPT_CMD_SUCCESS; // Invalid Map
        };
        (Some(m), getnum(rt, st, 4) as i16, getnum(rt, st, 5) as i16)
    } else {
        (t.cemetery_map, t.cemetery_x, t.cemetery_y)
    };
    if !bg_flag(c, m) {
        let name = m.and_then(|m| crate::data::maps().get(m)).map(|d| d.name.clone()).unwrap_or_default();
        log::warn!("buildin_waitingroom2bg_single: Map {name} requires the mapflag MF_BATTLEGROUND.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    }
    let n = getstr(rt, st, 6);
    let Some(nd) = crate::script::npc_run::npc_name2id(c, &lat(&n)) else { return SCRIPT_CMD_SUCCESS };
    let Some(cd) = crate::chat::get(c, crate::chat::npc_chat_id(c, nd.id)) else { return SCRIPT_CMD_SUCCESS };
    let Some(&aid) = cd.users.first() else { return SCRIPT_CMD_SUCCESS };
    let ok = bg::bg_team_join(c, bg_id, aid, false) && setpos_ok(c, aid, m, x, y);
    st.pushint(ok as i64);
    SCRIPT_CMD_SUCCESS
}

/// bg_create("<map>",<x>,<y>{,"<quit event>","<death event>"}): a new, empty team.
fn bg_create(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let map_name = getstr(rt, st, 2);
    let m = if map_name != b"-" {
        match mapindex(&map_name) {
            Some(m) => Some(m),
            None => {
                // Invalid Map
                st.pushint(0);
                return SCRIPT_CMD_SUCCESS;
            }
        }
    } else {
        None
    };
    let team = team_args(rt, st);
    st.pushint(bg::bg_create(c, m, &team) as i64);
    SCRIPT_CMD_SUCCESS
}

/// bg_join(<team>{,"<map>",<x>,<y>{,<char id>}}): the player joins the team and warps (to the team's respawn point
/// without a map).
fn bg_join(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let bg_id = getnum(rt, st, 2);
    let Some(t) = bg::team(c, bg_id) else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let (m, x, y) = if st.hasdata(3) {
        let map_name = getstr(rt, st, 3);
        let Some(m) = mapindex(&map_name) else {
            st.pushint(0);
            return SCRIPT_CMD_SUCCESS; // Invalid Map
        };
        (Some(m), getnum(rt, st, 4) as i16, getnum(rt, st, 5) as i16)
    } else {
        (t.cemetery_map, t.cemetery_x, t.cemetery_y)
    };
    let Some(aid) = charid2sd(rt, st, 6, "buildin_bg_join") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    if !bg_flag(c, m) {
        let name = m.and_then(|m| crate::data::maps().get(m)).map(|d| d.name.clone()).unwrap_or_default();
        log::warn!("buildin_bg_join: Map {name} requires the mapflag MF_BATTLEGROUND.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    }
    let ok = bg::bg_team_join(c, bg_id, aid, false) && setpos_ok(c, aid, m, x, y);
    st.pushint(ok as i64);
    SCRIPT_CMD_SUCCESS
}

/// bg_team_setxy <team>,<x>,<y>: the team's respawn cell.
fn bg_team_setxy(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let bg_id = getnum(rt, st, 2);
    if let Some(mut t) = bg::team(c, bg_id) {
        t.cemetery_x = getnum(rt, st, 3) as i16;
        t.cemetery_y = getnum(rt, st, 4) as i16;
        c.db().bg_team().id().update(t);
    }
    SCRIPT_CMD_SUCCESS
}

/// bg_warp <team>,"<map>",<x>,<y>: every member warps.
fn bg_warp(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let bg_id = getnum(rt, st, 2);
    let map_name = getstr(rt, st, 3);
    let Some(m) = mapindex(&map_name) else { return SCRIPT_CMD_SUCCESS }; // Invalid Map
    let x = getnum(rt, st, 4) as i16;
    let y = getnum(rt, st, 5) as i16;
    bg::bg_team_warp(c, bg_id, m, x, y);
    SCRIPT_CMD_SUCCESS
}

/// bg_monster <team>,"<map>",<x>,<y>,"<name>",<class>{,"<event>"}: a monster of the team (mob_spawn_bg).
fn bg_monster(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let bg_id = getnum(rt, st, 2);
    let mapname = getstr(rt, st, 3);
    let x = getnum(rt, st, 4) as i16;
    let y = getnum(rt, st, 5) as i16;
    let name = getstr(rt, st, 6);
    let class_ = getnum(rt, st, 7);
    let evt = if st.hasdata(8) { getstr(rt, st, 8) } else { Vec::new() };
    check_event(rt, st, &evt);
    let id = bg::mob_spawn_bg(c, &lat(&mapname), x, y, &lat(&name), class_, &lat(&evt), bg_id as u32);
    st.pushint(id as i64);
    SCRIPT_CMD_SUCCESS
}

/// bg_monster_set_team <monster id>,<team>: the monster changes side and forgets its target.
fn bg_monster_set_team(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let id = getnum(rt, st, 2) as u32;
    let bg_id = getnum(rt, st, 3);
    if id == 0 {
        return SCRIPT_CMD_SUCCESS;
    }
    let Some(mut u) = crate::unit::get(c, id).filter(|u| u.kind == crate::map::BL_MOB) else { return SCRIPT_CMD_SUCCESS };
    let Some(mut md) = crate::mob::get(c, id) else { return SCRIPT_CMD_SUCCESS };
    md.bg_id = bg_id as u32;
    crate::mob::put(c, &md);
    crate::unit::stop_attack(c, &mut u);
    crate::unit::stop_walking(c, &mut u, crate::unit::USW_NONE, 0);
    crate::unit::save(c, &u);
    if let Some(mut md) = crate::mob::get(c, id) {
        md.target_id = 0;
        md.attacked_id = 0;
        crate::mob::put(c, &md);
    }
    if let Some(u) = crate::unit::get(c, id) {
        crate::clif::name_area(c, &u);
    }
    SCRIPT_CMD_SUCCESS
}

/// bg_leave {<char id>} / bg_desert {<char id>}: the player leaves its team (bg_desert: as a deserter).
fn bg_leave(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let Some(aid) = charid2sd(rt, st, 2, "buildin_bg_leave") else { return SCRIPT_CMD_SUCCESS };
    if bg::bg_id_of(c, aid) == 0 {
        return SCRIPT_CMD_SUCCESS;
    }
    let deserter = st.funcname == "bg_desert";
    bg::bg_team_leave(c, aid, false, deserter);
    SCRIPT_CMD_SUCCESS
}

/// bg_destroy <team>.
fn bg_destroy(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let bg_id = getnum(rt, st, 2);
    bg::bg_team_delete(c, bg_id);
    SCRIPT_CMD_SUCCESS
}

/// bg_getareausers(<team>,"<map>",<x0>,<y0>,<x1>,<y1>): members inside the rectangle.
fn bg_getareausers(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let s = getstr(rt, st, 3);
    let bg_id = getnum(rt, st, 2);
    let t = bg::team(c, bg_id);
    let m = mapindex(&s);
    let (Some(t), Some(m)) = (t, m) else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let x0 = getnum(rt, st, 4) as i16;
    let y0 = getnum(rt, st, 5) as i16;
    let x1 = getnum(rt, st, 6) as i16;
    let y1 = getnum(rt, st, 7) as i16;
    let mut n = 0;
    for mem in &t.members {
        let Some(u) = crate::unit::get(c, mem.aid) else { continue };
        if u.map != m || u.x < x0 || u.y < y0 || u.x > x1 || u.y > y1 {
            continue;
        }
        n += 1;
    }
    st.pushint(n);
    SCRIPT_CMD_SUCCESS
}

/// bg_updatescore "<map>",<lion>,<eagle>: the map's score board, shown to everyone on it.
fn bg_updatescore(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let s = getstr(rt, st, 2);
    let Some(m) = mapindex(&s) else { return SCRIPT_CMD_SUCCESS };
    let lion = getnum(rt, st, 3) as i16;
    let eagle = getnum(rt, st, 4) as i16;
    bg::set_score(c, m, lion, eagle);
    crate::clif_bg::clif_bg_updatescore(c, m);
    SCRIPT_CMD_SUCCESS
}

/// bg_get_data(<team>,<type>): 0 the member count; 1 the members' account ids into $@arenamembers[] (count in
/// $@arenamemberscount).
fn bg_get_data(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let bg_id = getnum(rt, st, 2);
    let kind = getnum(rt, st, 3);
    match bg::team(c, bg_id) {
        Some(t) => match kind {
            0 => st.pushint(t.members.len() as i64),
            1 => {
                let mut i = 0u32;
                for mem in &t.members {
                    crate::party::mapreg_setreg(c, "$@arenamembers", i, mem.aid as i64);
                    i += 1;
                }
                crate::party::mapreg_setreg(c, "$@arenamemberscount", 0, i as i64);
                st.pushint(i as i64);
            }
            _ => log::error!("script:bg_get_data: unknown data identifier {kind}"),
        },
        None => st.pushint(0),
    }
    SCRIPT_CMD_SUCCESS
}

/// bg_reserve("<battleground map>"{,<ended>}).
fn bg_reserve(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let s = lat(&getstr(rt, st, 2));
    let ended = if st.hasdata(3) { getnum(rt, st, 3) != 0 } else { false };
    if !bg::bg_queue_reserve(c, &s, ended) {
        log::warn!("buildin_bg_reserve: Could not reserve battleground with name {s}");
    }
    SCRIPT_CMD_SUCCESS
}

/// bg_unbook("<battleground map>").
fn bg_unbook(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let s = lat(&getstr(rt, st, 2));
    if !bg::bg_queue_unbook(c, &s) {
        log::warn!("buildin_bg_unbook: Could not unreserve battleground with name {s}");
    }
    SCRIPT_CMD_SUCCESS
}

/// battleground.hpp e_bg_info.
const BG_INFO_ID: i32 = 0;
const BG_INFO_REQUIRED_PLAYERS: i32 = 1;
const BG_INFO_MAX_PLAYERS: i32 = 2;
const BG_INFO_MIN_LEVEL: i32 = 3;
const BG_INFO_MAX_LEVEL: i32 = 4;
const BG_INFO_MAPS: i32 = 5;
const BG_INFO_DESERTER_TIME: i32 = 6;

/// bg_info("<battleground name>",<type>): the battleground database (BG_INFO_MAPS fills .@bgmaps$[] and .@bgmapscount).
fn bg_info(rt: &Rt, st: &mut State) -> i32 {
    let name = lat(&getstr(rt, st, 2));
    let Some(b) = bg_search_name(&name) else {
        log::error!("bg_info: Invalid Battleground name {name}.");
        return SCRIPT_CMD_FAILURE;
    };
    let kind = getnum(rt, st, 3);
    match kind {
        BG_INFO_ID => st.pushint(b.id as i64),
        BG_INFO_REQUIRED_PLAYERS => st.pushint(b.required_players as i64),
        BG_INFO_MAX_PLAYERS => st.pushint(b.max_players as i64),
        BG_INFO_MIN_LEVEL => st.pushint(b.min_lvl as i64),
        BG_INFO_MAX_LEVEL => st.pushint(b.max_lvl as i64),
        BG_INFO_MAPS => {
            let arr = b".@bgmaps$";
            let id = add_str(arr);
            for (i, m) in b.maps.iter().enumerate() {
                // setd_sub_str(st, nullptr, ".@bgmaps$", i, mapindex_id2name(...), nullptr)
                reg::set_reg_str(rt, st, None, id, i as u32, arr, &crate::net::wire::latin1(&m.map_name), &RegRef::None);
            }
            let cnt = b".@bgmapscount";
            reg::set_reg_num(rt, st, None, add_str(cnt), 0, cnt, b.maps.len() as i64, &RegRef::None);
            st.pushint(b.maps.len() as i64);
        }
        BG_INFO_DESERTER_TIME => st.pushint(b.deserter_time as i64),
        _ => {
            log::error!("bg_info: Unknown battleground info type {kind} given.");
            return SCRIPT_CMD_FAILURE;
        }
    }
    let _ = battleground_db;
    SCRIPT_CMD_SUCCESS
}
