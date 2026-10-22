//! Script commands of the guild system (script.cpp BUILDIN_FUNC warpguild, getguildname, getguildmaster,
//! getguildmasterid, getguildinfo, guildskill, getgdskilllv, guildopenstorage, guildopenstorage_log,
//! guild_has_permission, getmapguildusers, requestguildinfo, guildgetexp, guildchangegm, getguildmember,
//! getguildalliance, is_guild_leader). The guild storage variants of the item commands (guildstoragecountitem,
//! guildstoragedelitem) are in items.rs with their inventory / cart / storage siblings. The War of Emperium commands
//! (agitstart…, flagemblem, maprespawnguildid, castle data) come with the War of Emperium phase.

use super::*;
use crate::guild;
use crate::int_guild::{GUILD_PERM_ALL, MAX_GUILD, MAX_GUILDALLIANCE};
use crate::script::reg;
use crate::script::vm::*;
use crate::unit;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "warpguild" => warpguild,
        "getguildname" => getguildname,
        "getguildmaster" => getguildmaster,
        "getguildmasterid" => getguildmasterid,
        "getguildinfo" => getguildinfo,
        "guildskill" => guildskill,
        "getgdskilllv" => getgdskilllv,
        "guildopenstorage" => guildopenstorage,
        "guildopenstorage_log" => guildopenstorage_log,
        "guild_has_permission" => guild_has_permission,
        "getmapguildusers" => getmapguildusers,
        "requestguildinfo" => requestguildinfo,
        "guildgetexp" => guildgetexp,
        "guildchangegm" => guildchangegm,
        "getguildmember" => getguildmember,
        "getguildalliance" => getguildalliance,
        "is_guild_leader" => is_guild_leader,
        _ => return None,
    })
}

/// warpguild "<map>"|"Random"|"SavePointAll"|"SavePoint",<x>,<y>,<guild id>: every online member of the guild.
fn warpguild(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let s = getstr(rt, st, 2);
    let x = getnum(rt, st, 3);
    let y = getnum(rt, st, 4);
    let gid = getnum(rt, st, 5);
    if guild::guild_search(c, gid).is_none() {
        return SCRIPT_CMD_SUCCESS;
    }
    let kind = match s.as_slice() {
        b"Random" => 0,
        b"SavePointAll" => 1,
        b"SavePoint" => 2,
        _ => 3,
    };
    let mut sd = None;
    if kind == 2 {
        // "SavePoint" uses save point of the currently attached player
        sd = rid2sd(rt, st, "buildin_warpguild");
        if sd.is_none() {
            return SCRIPT_CMD_SUCCESS;
        }
    }
    let mut m = None;
    if kind == 3 {
        match crate::data::maps().id_of(&lat(&s)) {
            Some(mm) => m = Some(mm),
            None => return SCRIPT_CMD_FAILURE,
        }
    }
    let mut users: Vec<u32> = {
        use crate::pc::pc as _;
        use spacetimedb::Table as _;
        c.db().pc().iter().map(|p| p.account_id).collect()
    };
    users.sort_by_key(|&a| crate::party::idb_order(a));
    for pl in users {
        if guild::guild_id_of(c, pl) != gid {
            continue;
        }
        let Some(pu) = unit::get(c, pl) else { continue };
        let flag = |f| crate::npc::mapflag(c, pu.map, f);
        match kind {
            0 => {
                // Random
                if !flag(crate::npc::MF_NOWARP) {
                    super::pcinfo::pc_randomwarp(c, pl, unit::CLR_TELEPORT, false);
                }
            }
            1 | 2 => {
                // SavePointAll: the member's save point; SavePoint: the attached player's
                if !flag(crate::npc::MF_NORETURN) {
                    let src = if kind == 1 { pl } else { sd.unwrap_or(pl) };
                    if let Some(ch) = crate::pc::character_of(c, src) {
                        if let Some(sm) = crate::data::maps().id_of(&ch.save_map) {
                            crate::pc::setpos(c, pl, sm, ch.save_x as i16, ch.save_y as i16, unit::CLR_TELEPORT);
                        }
                    }
                }
            }
            _ => {
                // m,x,y
                let Some(mm) = m else { continue };
                let class = crate::pc::character_of(c, pl).map(|ch| ch.class as i32).unwrap_or(0);
                if !flag(crate::npc::MF_NORETURN) && !flag(crate::npc::MF_NOWARP) && crate::pc::job_can_entermap(c, class, mm, crate::pc::group_level(c, pl)) {
                    crate::pc::setpos(c, pl, mm, x as i16, y as i16, unit::CLR_TELEPORT);
                }
            }
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// getguildname(<guild id>): the name, or "null".
fn getguildname(rt: &Rt, st: &mut State) -> i32 {
    let gid = getnum(rt, st, 2);
    match rt.c.and_then(|c| guild::guild_search(c, gid)) {
        Some(g) => st.pushstr(crate::net::wire::latin1(&g.guild.name)),
        None => st.pushstr(b"null".to_vec()),
    }
    SCRIPT_CMD_SUCCESS
}

/// getguildmaster(<guild id>): member[0]'s name, or "null".
fn getguildmaster(rt: &Rt, st: &mut State) -> i32 {
    let gid = getnum(rt, st, 2);
    match rt.c.and_then(|c| guild::guild_search(c, gid)) {
        Some(g) => st.pushstr(crate::net::wire::latin1(&g.guild.member[0].name)),
        None => st.pushstr(b"null".to_vec()),
    }
    SCRIPT_CMD_SUCCESS
}

/// getguildmasterid(<guild id>): member[0]'s char id, or 0.
fn getguildmasterid(rt: &Rt, st: &mut State) -> i32 {
    let gid = getnum(rt, st, 2);
    match rt.c.and_then(|c| guild::guild_search(c, gid)) {
        Some(g) => st.pushint(g.guild.member[0].char_id as i64),
        None => st.pushint(0),
    }
    SCRIPT_CMD_SUCCESS
}

/// script.hpp e_guildinfo_types.
const GUILDINFO_NAME: i32 = 1;
const GUILDINFO_LEVEL: i32 = 2;
const GUILDINFO_AVERAGELEVEL: i32 = 3;
const GUILDINFO_ONLINECOUNT: i32 = 4;
const GUILDINFO_MEMBERCOUNT: i32 = 5;
const GUILDINFO_MAXMEMBERCOUNT: i32 = 6;
const GUILDINFO_EXP: i32 = 7;
const GUILDINFO_NEXTEXP: i32 = 8;
const GUILDINFO_MASTERID: i32 = 9;
const GUILDINFO_MASTERNAME: i32 = 10;

/// getguildinfo(<guild id>,<type>): the guild must be loaded on the map server (else the script ends).
fn getguildinfo(rt: &Rt, st: &mut State) -> i32 {
    let gid = getnum(rt, st, 2);
    let Some(g) = rt.c.and_then(|c| guild::guild_search(c, gid)) else {
        log::error!("buildin_getguildinfo: The guild ID '{gid}' doesn't exist or is not yet loaded into the map-server. ");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    };
    let g = &g.guild;
    match getnum(rt, st, 3) {
        GUILDINFO_NAME => st.pushstr(crate::net::wire::latin1(&g.name)),
        GUILDINFO_LEVEL => st.pushint(g.guild_lv as i64),
        GUILDINFO_AVERAGELEVEL => st.pushint(g.average_lv as i64),
        GUILDINFO_ONLINECOUNT => st.pushint(g.connect_member as i64),
        GUILDINFO_MEMBERCOUNT => st.pushint((0..g.max()).filter(|&i| g.member[i].account_id > 0).count() as i64),
        GUILDINFO_MAXMEMBERCOUNT => st.pushint(g.max_member as i64),
        GUILDINFO_EXP => st.pushint(g.exp as i64),
        GUILDINFO_NEXTEXP => st.pushint(g.next_exp as i64),
        GUILDINFO_MASTERID => st.pushint(g.member[0].char_id as i64),
        GUILDINFO_MASTERNAME => st.pushstr(crate::net::wire::latin1(&g.member[0].name)),
        _ => {
            log::error!("buildin_getguildinfo: Invalid guildinfo type. ");
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// The skill argument of guildskill / getgdskilllv: a name (skill_name2id) or an id.
fn skill_arg(rt: &Rt, st: &mut State, i: i32) -> i32 {
    if isstring(rt, st, i) {
        crate::db::skills::skill_name2id(&lat(&getstr(rt, st, i))) as i32
    } else {
        getnum(rt, st, i)
    }
}

/// guildskill <skill id>|"<skill name>",<level>: guild_skillup `level` times for the attached player.
fn guildskill(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_guildskill") else { return SCRIPT_CMD_SUCCESS };
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let id = skill_arg(rt, st, 2);
    let level = getnum(rt, st, 3);
    for _ in 0..level.max(0) {
        guild::guild_skillup(c, aid, id as u16);
    }
    SCRIPT_CMD_SUCCESS
}

/// getgdskilllv(<guild id>,<skill id>|"<skill name>"): the guild's level of the skill, -1 without the guild.
fn getgdskilllv(rt: &Rt, st: &mut State) -> i32 {
    let gid = getnum(rt, st, 2);
    let skill_id = skill_arg(rt, st, 3) as u16;
    match rt.c.and_then(|c| guild::guild_search(c, gid)) {
        None => st.pushint(-1),
        Some(g) => st.pushint(guild::guild_checkskill(&g.guild, skill_id as i32) as i64),
    }
    SCRIPT_CMD_SUCCESS
}

/// guildopenstorage(): storage_guild_storageopen's result (GSTORAGE_*).
fn guildopenstorage(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_guildopenstorage") else { return SCRIPT_CMD_SUCCESS };
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let ret = crate::guild_storage::storage_guild_storageopen(c, aid);
    st.pushint(ret as i64);
    SCRIPT_CMD_SUCCESS
}

/// guildopenstorage_log({<char id>}): the guild storage log window (PACKETVER >= 20140205); returns
/// e_guild_storage_log.
fn guildopenstorage_log(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 2, "buildin_guildopenstorage_log") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let ret = crate::guild_storage::storage_guild_log_read(c, aid);
    st.pushint(ret as i64);
    SCRIPT_CMD_SUCCESS
}

/// guild_has_permission(<permission>{,<char id>}): every bit of `permission` must be in the player's position.
fn guild_has_permission(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_guild_has_permission") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let permission = getnum(rt, st, 2);
    if permission == 0 {
        log::error!("buildin_guild_has_permission: No permission given.");
        return SCRIPT_CMD_FAILURE;
    }
    if permission & GUILD_PERM_ALL == 0 {
        log::error!("buildin_guild_has_permission: Invalid permission '{permission}'.");
        return SCRIPT_CMD_FAILURE;
    }
    let Some(g) = guild::sd_guild(c, aid) else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let position = guild::guild_getposition(c, aid);
    if position < 0 || g.guild.position[position as usize].mode & permission != permission {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    }
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}

/// getmapguildusers("<map>",<guild id>): the guild's online members on the map (-1 for an unknown map).
fn getmapguildusers(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let name = lat(&getstr(rt, st, 2));
    let gid = getnum(rt, st, 3);
    let Some(m) = crate::data::maps().id_of(&name) else {
        st.pushint(-1);
        return SCRIPT_CMD_SUCCESS;
    };
    let mut n = 0;
    if let Some(g) = guild::guild_search(c, gid) {
        for i in 0..g.guild.max() {
            let sd = g.guild.member[i].sd;
            if sd != 0 && unit::get(c, sd).map(|u| u.map == m).unwrap_or(false) {
                n += 1;
            }
        }
    }
    st.pushint(n);
    SCRIPT_CMD_SUCCESS
}

/// requestguildinfo <guild id>{,"<event>"}: asks the char server for the guild (the event runs once it arrives).
fn requestguildinfo(rt: &Rt, st: &mut State) -> i32 {
    let gid = getnum(rt, st, 2);
    let mut event = String::new();
    if st.hasdata(3) {
        event = lat(&getstr(rt, st, 3));
        // check_event
        if !event.is_empty() && !event.to_ascii_lowercase().contains("::on") {
            log::warn!("NPC event parameter deprecated! Please use 'NPCNAME::OnEVENT' instead of '{event}'.");
        }
    }
    if gid > 0 {
        if let Some(c) = rt.c {
            guild::guild_npc_request_info(c, gid, &event);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// guildgetexp <exp>: the attached player gives `exp` to its guild (guild_getexp).
fn guildgetexp(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_guildgetexp") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let exp = getnum64(rt, st, 2);
    if exp <= 0 {
        log::error!("buildin_guildgetexp: Called with exp <= 0.");
        return SCRIPT_CMD_FAILURE;
    }
    if guild::guild_id_of(c, aid) <= 0 {
        let name = crate::pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
        let cid = crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
        log::error!("buildin_guildgetexp: Called for player {name} (AID: {aid}, CID: {cid}) without a guild.");
        return SCRIPT_CMD_FAILURE;
    }
    guild::guild_getexp(c, aid, exp as u64);
    SCRIPT_CMD_SUCCESS
}

/// guildchangegm(<guild id>,"<new master's name>"): 1 when the change was asked for.
fn guildchangegm(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let gid = getnum(rt, st, 2);
    let name = lat(&getstr(rt, st, 3));
    match crate::pc::nick2sd(c, &name, false) {
        None => st.pushint(0),
        Some(u) => st.pushint(guild::guild_gm_change(c, gid, u.char_id) as i64),
    }
    SCRIPT_CMD_SUCCESS
}

/// getguildmember <guild id>{,<type>{,<array_variable>}}: 0 names, 1 char ids, 2 account ids (into the array, or
/// $@guildmembername$[] / $@guildmembercid[] / $@guildmemberaid[]); $@guildmembercount and the return value count.
fn getguildmember(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let g = guild::guild_search(c, getnum(rt, st, 2));
    let mut j: u32 = 0;
    if let Some(g) = g {
        let kind = if st.hasdata(3) { getnum(rt, st, 3) as u8 as i32 } else { 0 };
        let mut target: Option<(i32, RegRef, Vec<u8>)> = None;
        if st.hasdata(4) {
            let (id, rf, name) = match st.data(4) {
                Data::Name(id, _, rf) => (*id, rf.clone(), name_of(*id)),
                _ => {
                    log::error!("buildin_getguildmember: Error in argument! Please give a variable to store values in.");
                    return SCRIPT_CMD_FAILURE;
                }
            };
            if kind <= 0 && name.last() != Some(&b'$') {
                log::error!("buildin_getguildmember: The array {} is not string type.", lat(&name));
                return SCRIPT_CMD_FAILURE;
            }
            if not_server_variable(name.first().copied().unwrap_or(0)) && rid2sd(rt, st, "buildin_getguildmember").is_none() {
                log::error!("buildin_getguildmember: Cannot use a player variable '{}' if no player is attached.", lat(&name));
                return SCRIPT_CMD_FAILURE;
            }
            target = Some((id, rf, name));
        }
        for i in 0..MAX_GUILD {
            let m = &g.guild.member[i];
            if m.account_id == 0 {
                continue;
            }
            match kind {
                2 => match &target {
                    Some((id, rf, name)) => {
                        reg::set_reg_num(rt, st, None, *id, j, name, m.account_id as i64, rf);
                    }
                    None => crate::party::mapreg_setreg(c, "$@guildmemberaid", j, m.account_id as i64),
                },
                1 => match &target {
                    Some((id, rf, name)) => {
                        reg::set_reg_num(rt, st, None, *id, j, name, m.char_id as i64, rf);
                    }
                    None => crate::party::mapreg_setreg(c, "$@guildmembercid", j, m.char_id as i64),
                },
                _ => match &target {
                    Some((id, rf, name)) => {
                        reg::set_reg_str(rt, st, None, *id, j, name, &crate::net::wire::latin1(&m.name), rf);
                    }
                    None => crate::party::mapreg_setregstr(c, "$@guildmembername$", j, &crate::net::wire::latin1(&m.name)),
                },
            }
            j += 1;
        }
    }
    crate::party::mapreg_setreg(c, "$@guildmembercount", 0, j as i64);
    st.pushint(j as i64);
    SCRIPT_CMD_SUCCESS
}

/// getguildalliance(<guild id 1>,<guild id 2>): 0 none, 1 allied (or the same guild), 2 enemies; -2 / -1 when the
/// first / second guild is not loaded.
fn getguildalliance(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let g1 = getnum(rt, st, 2);
    let g2 = getnum(rt, st, 3);
    if g1 < 1 || g2 < 1 {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    }
    if g1 == g2 {
        st.pushint(1);
        return SCRIPT_CMD_SUCCESS;
    }
    let Some(d1) = guild::guild_search(c, g1) else {
        log::warn!("buildin_getguildalliance: Requesting non-existent GuildID1 '{g1}'.");
        st.pushint(-2);
        return SCRIPT_CMD_FAILURE;
    };
    if guild::guild_search(c, g2).is_none() {
        log::warn!("buildin_getguildalliance: Requesting non-existent GuildID2 '{g2}'.");
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    }
    match (0..MAX_GUILDALLIANCE).find(|&i| d1.guild.alliance[i].guild_id == g2) {
        None => st.pushint(0),
        Some(i) => st.pushint(if d1.guild.alliance[i].opposition != 0 { 2 } else { 1 }),
    }
    SCRIPT_CMD_SUCCESS
}

/// is_guild_leader({<guild id>}): the attached player is member[0] of the guild (its own by default).
fn is_guild_leader(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_is_guild_leader") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let gid = if st.hasdata(2) { getnum(rt, st, 2) } else { guild::guild_id_of(c, aid) };
    let cid = crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
    match guild::guild_search(c, gid) {
        Some(g) => st.pushint((g.guild.member[0].char_id == cid) as i64),
        None => st.pushint(0),
    }
    SCRIPT_CMD_SUCCESS
}
