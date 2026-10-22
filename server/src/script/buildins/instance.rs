//! script.cpp's instance commands (instance.rs): instance_create, instance_destroy, instance_enter, instance_npcname,
//! instance_mapname, instance_id, instance_warpall, instance_announce, instance_check_party, instance_check_guild,
//! instance_check_clan, instance_info, instance_live_info, instance_list, getinstancevar (and its deprecated alias
//! getvariableofinstance), setinstancevar.

use crate::instance::{self as inst, IM_CHAR, IM_CLAN, IM_GUILD, IM_MAX, IM_NONE, IM_PARTY};
use crate::script::buildins::*;
use crate::script::vm::*;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "instance_create" => instance_create,
        "instance_destroy" => instance_destroy,
        "instance_id" => instance_id,
        "instance_enter" => instance_enter,
        "instance_npcname" => instance_npcname,
        "instance_mapname" => instance_mapname,
        "instance_warpall" => instance_warpall,
        "instance_announce" => instance_announce,
        "instance_check_party" => instance_check_party,
        "instance_check_guild" => instance_check_guild,
        "instance_check_clan" => instance_check_clan,
        "instance_info" => instance_info,
        "instance_live_info" => instance_live_info,
        "instance_list" => instance_list,
        "getinstancevar" => getinstancevar,
        "setinstancevar" => setinstancevar,
        _ => return None,
    })
}

/// script.hpp enum instance_info_type.
const IIT_ID: i32 = 0;
const IIT_TIME_LIMIT: i32 = 1;
const IIT_IDLE_TIMEOUT: i32 = 2;
const IIT_ENTER_MAP: i32 = 3;
const IIT_ENTER_X: i32 = 4;
const IIT_ENTER_Y: i32 = 5;
const IIT_MAPCOUNT: i32 = 6;
const IIT_MAP: i32 = 7;
/// script.hpp enum e_instance_live_info_type.
const ILI_NAME: i32 = 0;
const ILI_MODE: i32 = 1;
const ILI_OWNER: i32 = 2;
/// script.hpp enum e_instance_warpall_flag.
const IWA_NOTDEAD: i32 = 1;
/// map.hpp MAX_LEVEL.
const MAX_LEVEL: i32 = 275;
/// mmo.hpp NAME_LENGTH.
const NAME_LENGTH: usize = 24;

/// script_instancegetid(st, mode).
fn instancegetid(rt: &Rt, st: &State, mode: u8) -> i32 {
    match rt.c {
        Some(c) => inst::script_instancegetid(c, st.oid, st.rid, mode),
        None => 0,
    }
}

/// script.cpp script_rid2sd(sd): the attached player, or the "no player attached" error.
fn rid2sd_opt(rt: &Rt, st: &mut State, func: &str) -> Option<u32> {
    rid2sd(rt, st, func)
}

/// instance_create("<instance name>"{,<instance mode>{,<owner id>}}): the new instance id, or a negative error.
fn instance_create(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let mut mode = IM_PARTY;
    let mut owner_id = 0;
    if st.hasdata(3) {
        // static_cast<e_instance_mode>: a uint8
        mode = getnum(rt, st, 3) as u8;
        if mode >= IM_MAX {
            let name = lat(&getstr(rt, st, 2));
            log::error!("buildin_instance_create: Unknown instance mode {mode} for '{name}'");
            return SCRIPT_CMD_FAILURE;
        }
    }
    if st.hasdata(4) {
        owner_id = getnum(rt, st, 4);
    } else {
        // If sd is nullptr, instance_create will return -2.
        match mode {
            IM_NONE => owner_id = st.oid as i32,
            IM_CHAR | IM_PARTY | IM_GUILD | IM_CLAN => {
                if let Some(aid) = rid2sd_opt(rt, st, "buildin_instance_create") {
                    if let Some(ch) = crate::pc::character_of(c, aid) {
                        owner_id = match mode {
                            IM_CHAR => ch.id as i32,
                            IM_PARTY => ch.party_id as i32,
                            IM_GUILD => ch.guild_id as i32,
                            _ => ch.clan_id as i32,
                        };
                    }
                }
            }
            _ => {
                let name = lat(&getstr(rt, st, 2));
                log::error!("buildin_instance_create: Invalid instance mode (instance name: {name})");
                return SCRIPT_CMD_FAILURE;
            }
        }
    }
    let name = lat(&getstr(rt, st, 2));
    let r = inst::instance_create(c, owner_id, &name, mode as i32);
    st.pushint(r as i64);
    SCRIPT_CMD_SUCCESS
}

/// instance_destroy {<instance id>}: destroys an instance (the NPC's own by default).
fn instance_destroy(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let instance_id = if st.hasdata(2) { getnum(rt, st, 2) } else { instancegetid(rt, st, IM_NONE) };
    if instance_id == 0 {
        log::error!("buildin_instance_destroy: Trying to destroy invalid instance {instance_id}.");
        return SCRIPT_CMD_FAILURE;
    }
    inst::instance_destroy(c, instance_id);
    SCRIPT_CMD_SUCCESS
}

/// instance_enter("<instance name>",{<x>,<y>,<char_id>,<instance id>}): IE_OK / IE_NOMEMBER / IE_NOINSTANCE / IE_OTHER.
fn instance_enter(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let x = if st.hasdata(3) { getnum(rt, st, 3) } else { -1 };
    let y = if st.hasdata(4) { getnum(rt, st, 4) } else { -1 };
    let instance_id = if st.hasdata(6) { getnum(rt, st, 6) } else { instancegetid(rt, st, IM_PARTY) };
    let Some(aid) = charid2sd(rt, st, 5, "buildin_instance_enter") else { return SCRIPT_CMD_FAILURE };
    let name = lat(&getstr(rt, st, 2));
    // instance_enter's x / y are int16
    let r = inst::instance_enter(c, aid, instance_id, &name, x as i16, y as i16);
    st.pushint(r as i64);
    SCRIPT_CMD_SUCCESS
}

/// instance_npcname("<npc name>"{,<instance id>}): the unique name of the NPC's copy in the instance.
fn instance_npcname(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let s = lat(&getstr(rt, st, 2));
    let instance_id = if st.hasdata(3) { getnum(rt, st, 3) } else { instancegetid(rt, st, IM_NONE) };
    let nd = if instance_id > 0 { crate::script::npc_run::npc_name2id(c, &s) } else { None };
    match nd {
        Some(nd) => {
            // static char npcname[NAME_LENGTH]
            let name: String = crate::instance_npc::dup_name(instance_id, nd.id).chars().take(NAME_LENGTH - 1).collect();
            st.pushstr(crate::net::wire::latin1(&name));
            SCRIPT_CMD_SUCCESS
        }
        None => {
            log::error!("buildin_instance_npcname: Invalid instance NPC (instance_id: {instance_id}, NPC name: \"{s}\".)");
            st.state = END;
            SCRIPT_CMD_FAILURE
        }
    }
}

/// instance_mapname("<map name>"{,<instance id>}): the name of the map's copy in the instance ("" when there is none).
fn instance_mapname(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let s = lat(&getstr(rt, st, 2));
    let instance_id = if st.hasdata(3) { getnum(rt, st, 3) } else { instancegetid(rt, st, IM_NONE) };
    // Check that instance mapname is a valid map
    let m = if instance_id <= 0 {
        -1
    } else {
        let src = crate::data::maps().id_of(&s).map(|v| v as i32).unwrap_or(-1);
        inst::instance_mapid(c, src, instance_id)
    };
    if m < 0 {
        st.pushstr(Vec::new());
    } else {
        let name = crate::data::maps().get(m as u16).map(|d| d.name.clone()).unwrap_or_default();
        st.pushstr(crate::net::wire::latin1(&name));
    }
    SCRIPT_CMD_SUCCESS
}

/// instance_id({<instance mode>}): the instance of the NPC (IM_NONE, the default) or of the attached player's
/// character / party / guild / clan.
fn instance_id(rt: &Rt, st: &mut State) -> i32 {
    let mut mode = IM_NONE as i32; // Default to the attached NPC
    if st.hasdata(2) {
        mode = getnum(rt, st, 2);
        if mode <= IM_NONE as i32 || mode >= IM_MAX as i32 {
            log::error!("buildin_instance_id: Unknown instance mode {mode}.");
            st.pushint(0);
            return SCRIPT_CMD_SUCCESS;
        }
    }
    let r = instancegetid(rt, st, mode as u8);
    st.pushint(r as i64);
    SCRIPT_CMD_SUCCESS
}

/// buildin_instance_warpall_sub: an owner's player (alive, with IWA_NOTDEAD) goes to (m, x, y).
fn instance_warpall_sub(c: &crate::world::Ctx, aid: u32, m: u16, x: i32, y: i32, instance_id: i32, flag: i32) -> i32 {
    if flag & IWA_NOTDEAD != 0 && crate::pc::isdead(c, aid) {
        return 0;
    }
    let Some(idata) = inst::find(c, instance_id) else { return 0 };
    let owner_id = idata.owner_id;
    let Some(ch) = crate::pc::character_of(c, aid) else { return 0 };
    match idata.mode {
        IM_CHAR if ch.id as i32 != owner_id => return 0,
        IM_PARTY if ch.party_id as i32 != owner_id => return 0,
        IM_GUILD if ch.guild_id as i32 != owner_id => return 0,
        IM_CLAN if ch.clan_id as i32 != owner_id => return 0,
        _ => {}
    }
    crate::script::buildins::script2::mapctl::setpos32(c, aid, m, x, y, crate::unit::CLR_TELEPORT);
    1
}

/// instance_warpall "<map name>",<x>,<y>{,<instance id>{,<flag>}}: every owner player on the instance's maps.
fn instance_warpall(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let mapn = lat(&getstr(rt, st, 2));
    let instance_id = if st.hasdata(5) { getnum(rt, st, 5) } else { instancegetid(rt, st, IM_PARTY) };
    let m = if instance_id <= 0 {
        -1
    } else {
        match crate::data::maps().id_of(&mapn) {
            Some(m) => inst::instance_mapid(c, m as i32, instance_id),
            None => -1,
        }
    };
    if m < 0 {
        log::error!("buildin_instance_warpall: Instance map for instance ID {instance_id} is not found.");
        return SCRIPT_CMD_FAILURE;
    }
    let Some(idata) = inst::find(c, instance_id) else {
        log::error!("buildin_instance_warpall: Instance is not found.");
        return SCRIPT_CMD_FAILURE;
    };
    let x = getnum(rt, st, 3);
    let y = getnum(rt, st, 4);
    let flag = if st.hasdata(6) { getnum(rt, st, 6) } else { 0 };
    for it in &idata.map {
        let Some(md) = crate::data::maps().get(it.m) else { continue };
        for u in crate::map::units_in_area(c, it.m, 0, 0, md.xs, md.ys, crate::map::BL_PC) {
            if !crate::unit::get(c, u.id).map(|x| x.on_map && x.map == it.m).unwrap_or(false) {
                continue; // bl->prev: warped meanwhile
            }
            instance_warpall_sub(c, u.id, m as u16, x, y, instance_id, flag);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// instance_announce <instance id>,"<text>",<flag>{,<fontColor>{,<fontType>{,<fontSize>{,<fontAlign>{,<fontY>}}}}}:
/// to every player on the instance's maps (0: the NPC's instance).
fn instance_announce(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let mut instance_id = getnum(rt, st, 2);
    let mes = getstr(rt, st, 3);
    let flag = getnum(rt, st, 4);
    let font = crate::script::buildins::dialog::font_args(rt, st, 5);
    if instance_id <= 0 {
        instance_id = instancegetid(rt, st, IM_NONE);
    }
    let idata = inst::find(c, instance_id);
    let Some(idata) = idata.filter(|_| instance_id > 0) else {
        log::error!("buildin_instance_announce: Instance not found.");
        return SCRIPT_CMD_FAILURE;
    };
    for it in &idata.map {
        let Some(md) = crate::data::maps().get(it.m) else { continue };
        let players = crate::map::units_in_area(c, it.m, 0, 0, md.xs, md.ys, crate::map::BL_PC);
        crate::script::buildins::dialog::announce_to(c, players, &mes, flag, &font);
    }
    SCRIPT_CMD_SUCCESS
}

/// instance_check_party(<party id>{,<amount>{,<min level>{,<max level>}}}): the party exists, its online members are
/// all within the levels and at least `amount` of them are online.
fn instance_check_party(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let max_party = crate::party::MAX_PARTY as i32;
    let amount = if !st.hasdata(3) {
        1
    } else {
        let a = getnum(rt, st, 3);
        if a < 1 || a > max_party {
            log::error!("buildin_instance_check_party: Invalid amount {a}. Min: 1, max: {max_party}.");
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
        a
    };
    let min = if !st.hasdata(4) {
        1
    } else {
        let v = getnum(rt, st, 4);
        if v < 1 || v > MAX_LEVEL {
            log::error!("buildin_instance_check_party: Invalid min level {v}. Min: 1, max: {MAX_LEVEL}.");
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
        v
    };
    let max = if !st.hasdata(5) {
        MAX_LEVEL
    } else {
        let v = getnum(rt, st, 5);
        if v < 1 || v > MAX_LEVEL {
            log::error!("buildin_instance_check_party: Invalid max level {v}. Min: 1, max: {MAX_LEVEL}.");
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
        v
    };
    let party_id = getnum(rt, st, 2);
    let Some(p) = crate::party::party_search(c, party_id as u32) else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let mut count = 0;
    for i in 0..crate::party::MAX_PARTY {
        let sd = p.data.get(i).map(|d| d.sd).unwrap_or(0);
        if sd == 0 {
            continue;
        }
        if crate::autotrade::autotrade(c, sd) != 0 {
            continue;
        }
        let lv = crate::pc::character_of(c, sd).map(|ch| ch.base_level as i32).unwrap_or(0);
        if lv < min || lv > max {
            st.pushint(0);
            return SCRIPT_CMD_SUCCESS;
        }
        count += 1;
    }
    st.pushint((count >= amount) as i64);
    SCRIPT_CMD_SUCCESS
}

/// The level / count test of instance_check_guild / instance_check_clan over the online members' account ids.
fn check_members(c: &crate::world::Ctx, members: impl Iterator<Item = u32>, min: i32, max: i32) -> Option<i32> {
    let mut n = 0;
    for aid in members {
        if aid == 0 {
            continue;
        }
        // map_id2bl(pl_sd->id) && !pl_sd->state.autotrade
        if crate::pc::get(c, aid).is_none() || crate::autotrade::autotrade(c, aid) != 0 {
            continue;
        }
        let lv = crate::pc::character_of(c, aid).map(|ch| ch.base_level as i32).unwrap_or(0);
        if lv < min || lv > max {
            return None;
        }
        n += 1;
    }
    Some(n)
}

/// instance_check_guild(<guild id>{,<amount>{,<min level>{,<max level>}}}).
fn instance_check_guild(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let amount = if st.hasdata(3) { getnum(rt, st, 3) } else { 1 };
    let min = if st.hasdata(4) { getnum(rt, st, 4) } else { 1 };
    let max = if st.hasdata(5) { getnum(rt, st, 5) } else { MAX_LEVEL };
    if min < 1 || min > MAX_LEVEL {
        log::error!("buildin_instance_check_guild: Invalid min level, {min}");
        return SCRIPT_CMD_FAILURE;
    } else if max < 1 || max > MAX_LEVEL {
        log::error!("buildin_instance_check_guild: Invalid max level, {max}");
        return SCRIPT_CMD_FAILURE;
    }
    if !st.hasdata(2) {
        return SCRIPT_CMD_FAILURE;
    }
    let guild_id = getnum(rt, st, 2);
    let Some(g) = crate::guild::guild_search(c, guild_id) else {
        st.pushint(0); // Returns false if guild does not exist.
        return SCRIPT_CMD_FAILURE;
    };
    let members = g.guild.member.iter().take(crate::int_guild::MAX_GUILD).map(|m| m.sd).collect::<Vec<_>>();
    match check_members(c, members.into_iter(), min, max) {
        None => st.pushint(0),
        Some(n) => st.pushint((n >= amount) as i64), // Not enough Members in the Guild to join Instance.
    }
    SCRIPT_CMD_SUCCESS
}

/// instance_check_clan(<clan id>{,<amount>{,<min level>{,<max level>}}}).
fn instance_check_clan(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let amount = if st.hasdata(3) { getnum(rt, st, 3) } else { 1 };
    let min = if st.hasdata(4) { getnum(rt, st, 4) } else { 1 };
    let max = if st.hasdata(5) { getnum(rt, st, 5) } else { MAX_LEVEL };
    if min < 1 || min > MAX_LEVEL {
        log::error!("buildin_instance_check_clan: Invalid min level, {min}");
        return SCRIPT_CMD_FAILURE;
    } else if max < 1 || max > MAX_LEVEL {
        log::error!("buildin_instance_check_clan: Invalid max level, {max}");
        return SCRIPT_CMD_FAILURE;
    }
    if !st.hasdata(2) {
        return SCRIPT_CMD_FAILURE;
    }
    let clan_id = getnum(rt, st, 2);
    if crate::clan::clan_search(clan_id).is_none() {
        st.pushint(0); // Returns false if clan does not exist.
        return SCRIPT_CMD_FAILURE;
    }
    let members = crate::clan::member_slots(c, clan_id);
    match check_members(c, members.into_iter(), min, max) {
        None => st.pushint(0),
        Some(n) => st.pushint((n >= amount) as i64), // Not enough Members in the Clan to join Instance.
    }
    SCRIPT_CMD_SUCCESS
}

/// instance_info("<instance name>",<info type>{,<map index>}): the instance database's values.
fn instance_info(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let name = lat(&getstr(rt, st, 2));
    let kind = getnum(rt, st, 3);
    let Some(db) = inst::search_db_name(&name) else {
        log::error!("buildin_instance_info: Unknown instance name \"{name}\".");
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    let mapname = |m: i16| crate::net::wire::latin1(&crate::instance_map::map_mapid2mapname(c, m as i32));
    match kind {
        IIT_ID => st.pushint(db.id as i64),
        IIT_TIME_LIMIT => st.pushint(db.limit),
        IIT_IDLE_TIMEOUT => st.pushint(db.timeout),
        IIT_ENTER_MAP => st.pushstr(mapname(db.enter.map)),
        IIT_ENTER_X => st.pushint(db.enter.x as i64),
        IIT_ENTER_Y => st.pushint(db.enter.y as i64),
        IIT_MAPCOUNT => st.pushint(db.maplist.len() as i64),
        IIT_MAP => {
            if !st.hasdata(4) || isstring(rt, st, 4) {
                log::error!("buildin_instance_info: Type IIT_MAP requires a numeric index argument.");
                st.pushstr(Vec::new());
                return SCRIPT_CMD_FAILURE;
            }
            let index = getnum(rt, st, 4);
            if index < 0 {
                log::error!("buildin_instance_info: Type IIT_MAP does not support a negative index argument.");
                st.pushstr(Vec::new());
                return SCRIPT_CMD_FAILURE;
            }
            if index > u8::MAX as i32 {
                log::error!("buildin_instance_info: Type IIT_MAP does only support up to index {}.", u8::MAX);
                st.pushstr(Vec::new());
                return SCRIPT_CMD_FAILURE;
            }
            // db->maplist[index]: rAthena reads past the list for an index beyond it (undefined behaviour); here that
            // index gives "" (what map_mapid2mapname answers for an unknown map id)
            match db.maplist.get(index as usize) {
                Some(&m) => st.pushstr(mapname(m)),
                None => st.pushstr(Vec::new()),
            }
        }
        _ => {
            log::error!("buildin_instance_info: Unknown instance information type \"{kind}\".");
            st.pushint(-1);
            return SCRIPT_CMD_FAILURE;
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// instance_live_info(<info type>{,<instance id>}): ILI_NAME, ILI_MODE or ILI_OWNER of a live instance ("" / -1 when
/// there is none).
fn instance_live_info(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let kind = getnum(rt, st, 2);
    if !(ILI_NAME..=ILI_OWNER).contains(&kind) {
        log::error!("buildin_instance_live_info: Unknown instance information type \"{kind}\".");
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    }
    let id = if !st.hasdata(3) { instancegetid(rt, st, IM_NONE) } else { getnum(rt, st, 3) };
    let im = if id > 0 && id < i32::MAX { inst::find(c, id) } else { None };
    let db = im.as_ref().and_then(|im| crate::db::misc::instance::instance_db().find(im.id));
    let (Some(im), Some(db)) = (im, db) else {
        if kind == ILI_NAME {
            st.pushstr(Vec::new());
        } else {
            st.pushint(-1);
        }
        return SCRIPT_CMD_SUCCESS;
    };
    match kind {
        ILI_NAME => st.pushstr(crate::net::wire::latin1(&db.name)),
        ILI_MODE => st.pushint(im.mode as i64),
        _ => st.pushint(im.owner_id as i64), // ILI_OWNER
    }
    SCRIPT_CMD_SUCCESS
}

/// instance_list(<"map name">{,<instance mode>}): `.@instance_list` gets the ids of the live instances with a copy of
/// the map (of that mode), in map id order; returns their count.
fn instance_list(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let name = lat(&getstr(rt, st, 2));
    let src_id = crate::data::maps().id_of(&name).map(|v| v as i32).unwrap_or(-1);
    if src_id == 0 {
        log::error!("buildin_instance_list: map '{name}' doesn't exist");
        return SCRIPT_CMD_FAILURE;
    }
    let mut mode = IM_MAX;
    if st.hasdata(3) {
        // static_cast<e_instance_mode>: a uint8
        mode = getnum(rt, st, 3) as u8;
        if mode >= IM_MAX {
            let s = lat(&getstr(rt, st, 3));
            log::error!("buildin_instance_list: Unknown instance mode {mode} for '{s}'");
            return SCRIPT_CMD_FAILURE;
        }
    }
    let var = b".@instance_list".to_vec();
    let id = add_str(&var);
    let mut j: u32 = 0;
    for r in crate::instance_map::all(c) {
        if r.src_m as i32 != src_id {
            continue;
        }
        let Some(idata) = inst::find(c, r.instance_id) else { continue };
        if mode == IM_MAX || idata.mode == mode {
            // setd_sub_num(st, nullptr, ".@instance_list", j, mapdata->instance_id, nullptr)
            crate::script::reg::set_reg_num(rt, st, None, id, j, &var, r.instance_id as i64, &RegRef::None);
            j += 1;
        }
    }
    st.pushint(j as i64);
    SCRIPT_CMD_SUCCESS
}

/// The checks getinstancevar / setinstancevar make: a `'` variable reference and a busy instance. Returns the
/// reference (str_data id, index, name) and the instance id, or None after the error (nil pushed, script ended).
fn instance_var_args(rt: &Rt, st: &mut State, command: &str, id_pos: i32) -> Option<(i32, u32, Vec<u8>, i32)> {
    let (vid, idx, name) = match st.data(2) {
        Data::Name(id, idx, _) => (*id, *idx, name_of(*id)),
        _ => {
            let s = lat(&getstr(rt, st, 2));
            log::error!("buildin_{command}: {s} is not a variable.");
            reportdata(&st.data(2).clone());
            st.pushnil();
            st.state = END;
            return None;
        }
    };
    if name.first() != Some(&b'\'') {
        log::error!("buildin_{command}: Invalid scope. {} is not an instance variable.", lat(&name));
        reportdata(&st.data(2).clone());
        st.pushnil();
        st.state = END;
        return None;
    }
    let instance_id = getnum(rt, st, id_pos);
    if instance_id <= 0 {
        log::error!("buildin_{command}: Invalid instance ID {instance_id}.");
        st.pushnil();
        st.state = END;
        return None;
    }
    // im->state != INSTANCE_BUSY (an unknown id dereferences a null shared_ptr in rAthena: here it is the same error)
    let busy = rt.c.and_then(|c| inst::find(c, instance_id)).map(|im| im.state == inst::INSTANCE_BUSY).unwrap_or(false);
    if !busy {
        log::error!("buildin_{command}: Unknown instance ID {instance_id}.");
        st.pushnil();
        st.state = END;
        return None;
    }
    Some((vid, idx, name, instance_id))
}

/// getinstancevar(<variable>,<instance id>): a reference to the variable in that instance's variables.
fn getinstancevar(rt: &Rt, st: &mut State) -> i32 {
    let Some((vid, idx, _, instance_id)) = instance_var_args(rt, st, "getinstancevar", 3) else { return SCRIPT_CMD_FAILURE };
    st.push(Data::Name(vid, idx, RegRef::Instance(instance_id)));
    SCRIPT_CMD_SUCCESS
}

/// setinstancevar(<variable>,<value>,<instance id>): sets the variable in that instance's variables; returns the
/// variable (script_pushcopy).
fn setinstancevar(rt: &Rt, st: &mut State) -> i32 {
    let command = st.funcname.clone();
    let Some((vid, idx, name, instance_id)) = instance_var_args(rt, st, &command, 4) else { return SCRIPT_CMD_FAILURE };
    st.pushcopy(2);
    let rf = RegRef::Instance(instance_id);
    if is_string_variable(&name) {
        let v = getstr(rt, st, 3);
        crate::script::reg::set_reg_str(rt, st, None, vid, idx, &name, &v, &rf);
    } else {
        let v = getnum64(rt, st, 3);
        crate::script::reg::set_reg_num(rt, st, None, vid, idx, &name, v, &rf);
    }
    SCRIPT_CMD_SUCCESS
}
