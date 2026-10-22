//! Script commands of the party system and of NPC waiting rooms (script.cpp BUILDIN_FUNC getpartyname …
//! is_party_leader, waitingroom … warpwaitingpc, chatmes, checkchatting).

use super::*;
use crate::npc::npc as _;
use crate::party::{self, MAX_PARTY};
use crate::script::npc_run;
use crate::script::reg;
use crate::script::vm::*;
use crate::unit;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "getpartyname" => getpartyname,
        "getpartymember" => getpartymember,
        "getpartyleader" => getpartyleader,
        "party_create" => party_create,
        "party_addmember" => party_addmember,
        "party_delmember" => party_delmember,
        "party_changeleader" => party_changeleader,
        "party_changeoption" => party_changeoption,
        "party_destroy" => party_destroy,
        "is_party_leader" => is_party_leader,
        "waitingroom" => waitingroom,
        "delwaitingroom" => delwaitingroom,
        "waitingroomkick" => waitingroomkick,
        "getwaitingroomusers" => getwaitingroomusers,
        "waitingroomkickall" => waitingroomkickall,
        "enablewaitingroomevent" => enablewaitingroomevent,
        "disablewaitingroomevent" => disablewaitingroomevent,
        "getwaitingroomstate" => getwaitingroomstate,
        "warpwaitingpc" => warpwaitingpc,
        "chatmes" => chatmes,
        "checkchatting" => checkchatting,
        _ => return None,
    })
}

/// getpartyname <party id>: the name, or "null".
fn getpartyname(rt: &Rt, st: &mut State) -> i32 {
    let party_id = getnum(rt, st, 2) as u32;
    match rt.c.and_then(|c| party::party_search(c, party_id)) {
        Some(p) => st.pushstr(crate::net::wire::latin1(&p.party.name)),
        None => st.pushstr(b"null".to_vec()),
    }
    SCRIPT_CMD_SUCCESS
}

/// getpartymember <party id>{,<type>{,<array_variable>}}: 0 names, 1 char ids, 2 account ids (into the array, or
/// $@partymembername$[] / $@partymembercid[] / $@partymemberaid[]); $@partymembercount and the return value count.
fn getpartymember(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let p = party::party_search(c, getnum(rt, st, 2) as u32);
    let mut j: u32 = 0;
    if let Some(p) = p {
        let kind = if st.hasdata(3) { getnum(rt, st, 3) } else { 0 };
        let mut target: Option<(i32, RegRef, Vec<u8>)> = None;
        if st.hasdata(4) {
            let (id, rf, name) = match st.data(4) {
                Data::Name(id, _, rf) => (*id, rf.clone(), name_of(*id)),
                _ => {
                    log::error!("buildin_getpartymember: Error in argument! Please give a variable to store values in.");
                    return SCRIPT_CMD_FAILURE;
                }
            };
            if kind <= 0 && name.last() != Some(&b'$') {
                log::error!("buildin_getpartymember: The array {} is not string type.", lat(&name));
                return SCRIPT_CMD_FAILURE;
            }
            if not_server_variable(name.first().copied().unwrap_or(0)) && rid2sd(rt, st, "buildin_getpartymember").is_none() {
                log::error!("buildin_getpartymember: Cannot use a player variable '{}' if no player is attached.", lat(&name));
                return SCRIPT_CMD_FAILURE;
            }
            target = Some((id, rf, name));
        }
        for i in 0..MAX_PARTY {
            let m = &p.party.member[i];
            if m.account_id == 0 {
                continue;
            }
            match kind {
                2 => match &target {
                    // setd_sub_num(st, nullptr, …): no player is passed, as in rAthena
                    Some((id, rf, name)) => {
                        reg::set_reg_num(rt, st, None, *id, j, name, m.account_id as i64, rf);
                    }
                    None => party::mapreg_setreg(c, "$@partymemberaid", j, m.account_id as i64),
                },
                1 => match &target {
                    Some((id, rf, name)) => {
                        reg::set_reg_num(rt, st, None, *id, j, name, m.char_id as i64, rf);
                    }
                    None => party::mapreg_setreg(c, "$@partymembercid", j, m.char_id as i64),
                },
                _ => match &target {
                    Some((id, rf, name)) => {
                        reg::set_reg_str(rt, st, None, *id, j, name, &crate::net::wire::latin1(&m.name), rf);
                    }
                    None => party::mapreg_setregstr(c, "$@partymembername$", j, &crate::net::wire::latin1(&m.name)),
                },
            }
            j += 1;
        }
    }
    party::mapreg_setreg(c, "$@partymembercount", 0, j as i64);
    st.pushint(j as i64);
    SCRIPT_CMD_SUCCESS
}

/// getpartyleader <party id>{,<type>}: 1 account, 2 char id, 3 class, 4 map, 5 level, else the name.
fn getpartyleader(rt: &Rt, st: &mut State) -> i32 {
    let party_id = getnum(rt, st, 2) as u32;
    let kind = if st.hasdata(3) { getnum(rt, st, 3) } else { 0 };
    let p = rt.c.and_then(|c| party::party_search(c, party_id));
    let leader = p.as_ref().and_then(|p| p.party.member.iter().find(|m| m.leader).cloned());
    let Some(m) = leader else {
        // leader not found
        if kind != 0 {
            st.pushint(-1);
        } else {
            st.pushstr(b"null".to_vec());
        }
        return SCRIPT_CMD_SUCCESS;
    };
    match kind {
        1 => st.pushint(m.account_id as i64),
        2 => st.pushint(m.char_id as i64),
        3 => st.pushint(m.class as i64),
        4 => st.pushstr(crate::net::wire::latin1(&m.map)),
        5 => st.pushint(m.lv as i64),
        _ => st.pushstr(crate::net::wire::latin1(&m.name)),
    }
    SCRIPT_CMD_SUCCESS
}

/// party_create "<name>"{,<char id>{,<item share>,<item share type>}}: 1 sent (id in $@party_create_id), -1 no
/// player, -2 already in a party, -3 name taken, 0 error.
fn party_create(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_party_create") else {
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    if party::party_id_of(c, aid) != 0 {
        st.pushint(-2);
        return SCRIPT_CMD_FAILURE;
    }
    let raw = getstr(rt, st, 2);
    let name: Vec<u8> = raw.iter().copied().take(party::NAME_LENGTH - 1).collect();
    let name = trim(&name);
    if party::party_searchname(c, &lat(&name)).is_some() {
        st.pushint(-3);
        return SCRIPT_CMD_FAILURE;
    }
    let item1 = (st.hasdata(4) && getnum(rt, st, 4) != 0) as i32;
    let item2 = (st.hasdata(5) && getnum(rt, st, 5) != 0) as i32;
    party::set_create_byscript(c);
    let r = party::party_create(c, aid, &name, item1, item2);
    st.pushint(r as i64);
    SCRIPT_CMD_SUCCESS
}

/// strlib.cpp trim.
fn trim(b: &[u8]) -> Vec<u8> {
    let mut s = 0;
    let mut e = b.len();
    while s < e && crate::cstr::is_space(b[s]) {
        s += 1;
    }
    while e > s && crate::cstr::is_space(b[e - 1]) {
        e -= 1;
    }
    b[s..e].to_vec()
}

/// party_addmember <party id>,<char id>: 1 sent, -1 no player, -2 in a party, -3 no party, -4 full, -5 same account.
fn party_addmember(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let party_id = getnum(rt, st, 2) as u32;
    let Some(sd) = crate::pc::charid2sd(c, getnum(rt, st, 3) as u32) else {
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    if party::party_id_of(c, sd.id) != 0 {
        st.pushint(-2);
        return SCRIPT_CMD_FAILURE;
    }
    let Some(p) = party::party_search(c, party_id) else {
        st.pushint(-3);
        return SCRIPT_CMD_FAILURE;
    };
    if party::block_account_in_same_party() && p.party.member.iter().any(|m| m.account_id == sd.id) {
        st.pushint(-5);
        return SCRIPT_CMD_FAILURE;
    }
    if p.party.count as usize >= MAX_PARTY {
        st.pushint(-4);
        return SCRIPT_CMD_FAILURE;
    }
    let mut x = crate::social::sx(c, sd.id);
    x.party_invite = party_id;
    crate::social::put_sx(c, &x);
    let r = party::party_reply_invite(c, sd.id, party_id, 1);
    st.pushint(r as i64);
    SCRIPT_CMD_SUCCESS
}

/// party_delmember {<char id>,<party id>}: party_removemember2's result.
fn party_delmember(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let mut sd = None;
    if !st.hasdata(2) && !st.hasdata(3) {
        sd = rid2sd(rt, st, "buildin_party_delmember");
        if sd.is_none() {
            st.pushint(-1);
            return SCRIPT_CMD_FAILURE;
        }
    }
    if sd.is_none() {
        sd = charid2sd(rt, st, 2, "buildin_party_delmember");
    }
    let r = match sd {
        Some(a) => party::party_removemember2(c, Some(a), 0, 0),
        None => {
            let cid = getnum(rt, st, 2) as u32;
            let pid = getnum(rt, st, 3) as u32;
            party::party_removemember2(c, None, cid, pid)
        }
    };
    st.pushint(r as i64);
    SCRIPT_CMD_SUCCESS
}

/// party_changeleader <party id>,<char id>: 1 done, -1 no party, -2 no player, -3 not a member, -4 already leader.
fn party_changeleader(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let party_id = getnum(rt, st, 2) as u32;
    let Some(p) = party::party_search(c, party_id) else {
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    let Some(tsd) = crate::pc::charid2sd(c, getnum(rt, st, 3) as u32) else {
        st.pushint(-2);
        return SCRIPT_CMD_FAILURE;
    };
    if party::party_id_of(c, tsd.id) != party_id {
        st.pushint(-3);
        return SCRIPT_CMD_FAILURE;
    }
    let Some(i) = p.party.member.iter().position(|m| m.leader) else {
        // this is should impossible!
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    if p.data[i].sd == tsd.id {
        st.pushint(-4);
        return SCRIPT_CMD_FAILURE;
    }
    let r = party::party_changeleader(c, None, Some(tsd.id), Some(p));
    st.pushint(r as i64);
    SCRIPT_CMD_SUCCESS
}

/// party_changeoption <party id>,<option>,<flag>: -1 no party, 0 bad option, 1 sent.
fn party_changeoption(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let Some(p) = party::party_search(c, getnum(rt, st, 2) as u32) else {
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    let option = getnum(rt, st, 3);
    let flag = getnum(rt, st, 4);
    let r = party::party_setoption(c, &p, option, flag);
    st.pushint(r as i64);
    SCRIPT_CMD_SUCCESS
}

/// party_destroy <party id>: the leader leaves (breaking the party); with the leader offline every member is
/// withdrawn here and the party broken on this map server.
fn party_destroy(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let Some(p) = party::party_search(c, getnum(rt, st, 2) as u32) else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let li = p.party.member.iter().position(|m| m.leader);
    match li {
        Some(i) if p.data[i].sd != 0 => {
            // leader leave = party broken
            let r = party::party_leave(c, p.data[i].sd, false);
            st.pushint(r as i64);
        }
        _ => {
            // leader not online
            for j in 0..MAX_PARTY {
                let Some(cur) = party::party_search(c, p.party_id) else { break };
                let sd = cur.data[j].sd;
                if sd != 0 {
                    if let Some(ch) = crate::pc::character_of(c, sd) {
                        party::party_member_withdraw(c, p.party_id, ch.account_id, ch.id, &ch.name, party::PARTY_MEMBER_WITHDRAW_LEAVE);
                    }
                } else if cur.party.member[j].char_id != 0 {
                    let m = &cur.party.member[j];
                    party::intif_party_leave(p.party_id, m.account_id, m.char_id, &m.name, party::PARTY_MEMBER_WITHDRAW_LEAVE);
                }
            }
            party::party_broken(c, p.party_id);
            st.pushint(1);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// is_party_leader {<party id>}: whether the attached player leads that party (default: their own).
fn is_party_leader(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_is_party_leader") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let p_id = if st.hasdata(2) { getnum(rt, st, 2) as u32 } else { party::party_id_of(c, aid) };
    if let Some(p) = party::party_search(c, p_id) {
        if let Some(i) = p.data.iter().position(|d| d.sd == aid) {
            st.pushint(p.party.member[i].leader as i64);
            return SCRIPT_CMD_SUCCESS;
        }
    }
    st.pushint(0);
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- waiting rooms

/// The NPC a waiting-room command acts on: argument `i` (by name), else the running NPC (map_id2bl(st->oid)).
fn npc_arg(rt: &Rt, st: &mut State, i: i32) -> Option<u32> {
    let c = rt.c?;
    if st.hasdata(i) {
        let n = getstr(rt, st, i);
        npc_run::npc_name2id(c, &lat(&n)).map(|n| n.id)
    } else {
        // (a floating NPC has an npc row and no unit row)
        unit::get(c, st.oid).filter(|u| u.kind == crate::map::BL_NPC).map(|u| u.id).or_else(|| c.db().npc().id().find(st.oid).map(|n| n.id))
    }
}

/// waitingroom "<title>",<limit>{,"<event>"{,<trigger>{,<zeny>{,<minlvl>{,<maxlvl>}}}}}.
fn waitingroom(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let title = getstr(rt, st, 2);
    let limit = getnum(rt, st, 3);
    let ev = if st.hasdata(4) { lat(&getstr(rt, st, 4)) } else { String::new() };
    let trigger = if st.hasdata(5) { getnum(rt, st, 5) } else { limit };
    let zeny = if st.hasdata(6) { getnum(rt, st, 6) } else { 0 };
    let min_lvl = if st.hasdata(7) { getnum(rt, st, 7) } else { 1 };
    let max_lvl = if st.hasdata(8) { getnum(rt, st, 8) } else { crate::chat::MAX_LEVEL as i32 };
    // nd = map_id2bl(st->oid): id_db also holds the floating NPCs ("-" scripts: an npc row without a unit row), whose
    // waitingroom takes an object id like any other (npc/other/pvp.txt PVPSpectator's OnInit; chat.rs)
    if unit::get(c, st.oid).is_some() || c.db().npc().id().find(st.oid).is_some() {
        crate::chat::chat_createnpcchat(c, st.oid, &title, limit, true, trigger, &ev, zeny, min_lvl, max_lvl);
    }
    SCRIPT_CMD_SUCCESS
}

/// delwaitingroom {"<npc name>"}.
fn delwaitingroom(rt: &Rt, st: &mut State) -> i32 {
    if let (Some(c), Some(nd)) = (rt.c, npc_arg(rt, st, 2)) {
        crate::chat::chat_deletenpcchat(c, nd);
    }
    SCRIPT_CMD_SUCCESS
}

/// waitingroomkick "<npc name>","<player name>".
fn waitingroomkick(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let n = getstr(rt, st, 2);
    let kick = getstr(rt, st, 3);
    if let Some(nd) = npc_run::npc_name2id(c, &lat(&n)) {
        let chat = crate::chat::npc_chat_id(c, nd.id);
        if chat != 0 {
            crate::chat::chat_npckickchat(c, chat, &kick);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// getwaitingroomusers {"<npc name>"}: .@waitingroom_users[] (account ids) and .@waitingroom_usercount.
fn getwaitingroomusers(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    if let Some(cd) = npc_arg(rt, st, 2).and_then(|nd| crate::chat::get(c, crate::chat::npc_chat_id(c, nd))) {
        let users_name = b".@waitingroom_users".to_vec();
        let id = add_str(&users_name);
        let mut j = 0u32;
        for &u in cd.users.iter() {
            reg::set_reg_num(rt, st, None, id, j, &users_name, u as i64, &RegRef::None);
            j += 1;
        }
        let count_name = b".@waitingroom_usercount".to_vec();
        let cid = add_str(&count_name);
        reg::set_reg_num(rt, st, None, cid, 0, &count_name, j as i64, &RegRef::None);
    }
    SCRIPT_CMD_SUCCESS
}

/// kickwaitingroomall {"<npc name>"}.
fn waitingroomkickall(rt: &Rt, st: &mut State) -> i32 {
    if let (Some(c), Some(nd)) = (rt.c, npc_arg(rt, st, 2)) {
        let chat = crate::chat::npc_chat_id(c, nd);
        if chat != 0 {
            crate::chat::chat_npckickall(c, chat);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// enablewaitingroomevent {"<npc name>"} (enablearena).
fn enablewaitingroomevent(rt: &Rt, st: &mut State) -> i32 {
    if let (Some(c), Some(nd)) = (rt.c, npc_arg(rt, st, 2)) {
        let chat = crate::chat::npc_chat_id(c, nd);
        if chat != 0 {
            crate::chat::chat_enableevent(c, chat);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// disablewaitingroomevent {"<npc name>"} (disablearena).
fn disablewaitingroomevent(rt: &Rt, st: &mut State) -> i32 {
    if let (Some(c), Some(nd)) = (rt.c, npc_arg(rt, st, 2)) {
        let chat = crate::chat::npc_chat_id(c, nd);
        if chat != 0 {
            crate::chat::chat_disableevent(c, chat);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// getwaitingroomstate(<type>{,"<npc name>"}).
fn getwaitingroomstate(rt: &Rt, st: &mut State) -> i32 {
    let kind = getnum(rt, st, 2);
    let cd = match rt.c {
        Some(c) => npc_arg(rt, st, 3).and_then(|nd| crate::chat::get(c, crate::chat::npc_chat_id(c, nd))),
        None => None,
    };
    let Some(cd) = cd else {
        st.pushint(-1);
        return SCRIPT_CMD_SUCCESS;
    };
    let users = cd.users.len() as i64;
    match kind {
        0 => st.pushint(users),
        1 => st.pushint(cd.limit as i64),
        2 => st.pushint((cd.trigger & 0x7f) as i64),
        3 => st.pushint((cd.trigger & 0x80 != 0) as i64),
        4 => st.pushstr(cd.title.clone()),
        5 => st.pushstr(cd.pass.clone()),
        16 => st.pushstr(crate::net::wire::latin1(&cd.npc_event)),
        32 => st.pushint((users >= cd.limit as i64) as i64),
        33 => st.pushint((users >= cd.trigger as i64) as i64),
        _ => st.pushint(-1),
    }
    SCRIPT_CMD_SUCCESS
}

/// warpwaitingpc "<map name>",<x>,<y>{,<number of players>}: warps the room's first players ($@warpwaitingpc[],
/// $@warpwaitingpcnum); "Random" and "SavePoint" as warp does.
fn warpwaitingpc(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let chat_id = crate::chat::npc_chat_id(c, st.oid);
    let Some(cd) = crate::chat::get(c, chat_id) else { return SCRIPT_CMD_SUCCESS };
    let map_name = getstr(rt, st, 2);
    let x = getnum(rt, st, 3);
    let y = getnum(rt, st, 4);
    let mut n = (cd.trigger & 0x7f) as i32;
    if st.hasdata(5) {
        n = getnum(rt, st, 5);
    }
    let mut i = 0;
    while i < n {
        let Some(cd) = crate::chat::get(c, chat_id) else { break };
        let Some(&aid) = cd.users.first() else { break };
        let Some(sd) = unit::get(c, aid) else { break };
        if map_name == b"SavePoint" && crate::npc::mapflag(c, sd.map, crate::npc::MF_NOTELEPORT) {
            break; // can't teleport on this map
        }
        if cd.zeny != 0 {
            // fee set
            if crate::pc::character_of(c, aid).map(|ch| ch.zeny).unwrap_or(0) < cd.zeny {
                break; // no zeny to cover set fee
            }
            crate::inventory::pc_payzeny(c, aid, cd.zeny as i64, crate::map_log::LOG_TYPE_NPC, 0);
        }
        party::mapreg_setreg(c, "$@warpwaitingpc", i as u32, aid as i64);
        if map_name == b"Random" {
            crate::script::buildins::pcinfo::pc_randomwarp(c, aid, unit::CLR_TELEPORT, true);
        } else if map_name == b"SavePoint" {
            if let Some(ch) = crate::pc::character_of(c, aid) {
                if let Some(m) = crate::data::maps().id_of(&ch.save_map) {
                    crate::pc::setpos(c, aid, m, ch.save_x as i16, ch.save_y as i16, unit::CLR_TELEPORT);
                }
            }
        } else if let Some(m) = crate::data::maps().id_of(&lat(&map_name)) {
            crate::pc::setpos(c, aid, m, x as i16, y as i16, unit::CLR_OUTSIGHT);
        }
        // pc_setpos → unit_remove_map → chat_leavechat took the player out of the room (a failed warp leaves them
        // in, and the next round picks the same player again, as in rAthena)
        i += 1;
    }
    party::mapreg_setreg(c, "$@warpwaitingpcnum", 0, i as i64);
    SCRIPT_CMD_SUCCESS
}

/// chatmes "<message>"{,"<npc name>"}: a message in the NPC's waiting room.
fn chatmes(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let s = getstr(rt, st, 2);
    if let Some(nd) = npc_arg(rt, st, 3) {
        let chat = crate::chat::npc_chat_id(c, nd);
        if chat != 0 {
            let msg: Vec<u8> = s.iter().copied().take(255).collect();
            crate::chat::chatmes(c, chat, &msg);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// checkchatting {"<player name>"}: whether the player is in a chat room.
fn checkchatting(rt: &Rt, st: &mut State) -> i32 {
    match nick2sd(rt, st, 2, "buildin_checkchatting") {
        Some(aid) => {
            let v = rt.c.and_then(|c| crate::pc::get(c, aid)).map(|p| p.chat_id != 0).unwrap_or(false);
            st.pushint(v as i64);
        }
        None => st.pushint(0),
    }
    SCRIPT_CMD_SUCCESS
}

