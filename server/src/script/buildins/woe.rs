//! The War of Emperium's script commands (script.cpp): agitstart / agitend / agitcheck and their 2 / 3 forms, gvgon /
//! gvgoff / gvgon3 / gvgoff3, maprespawnguildid, flagemblem, getcastlename, getcastledata, setcastledata, guardian and
//! guardianinfo. The castle state is woe.rs's.

use super::*;
use crate::db::misc::castle::castle_db;
use crate::db::mobs::MOBID_EMPERIUM;
use crate::npc::npc as _;
use crate::script::vm::*;
use crate::woe::{self, CD_ENABLED_GUARDIAN00, CD_MAX, CD_NONE, MAX_GUARDIANS};
use crate::world::Ctx;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "agitstart" => agitstart,
        "agitend" => agitend,
        "agitstart2" => agitstart2,
        "agitend2" => agitend2,
        "agitstart3" => agitstart3,
        "agitend3" => agitend3,
        "agitcheck" => agitcheck,
        "agitcheck2" => agitcheck2,
        "agitcheck3" => agitcheck3,
        "gvgon" => gvgon,
        "gvgoff" => gvgoff,
        "gvgon3" => gvgon3,
        "gvgoff3" => gvgoff3,
        "maprespawnguildid" => maprespawnguildid,
        "flagemblem" => flagemblem,
        "getcastlename" => getcastlename,
        "getcastledata" => getcastledata,
        "setcastledata" => setcastledata,
        "guardian" => guardian,
        "guardianinfo" => guardianinfo,
        _ => return None,
    })
}

// ------------------------------------------------------------------------------------------------ siege commands

/// agitstart(): starts WoE FE (guild_agit_start).
fn agitstart(rt: &Rt, _st: &mut State) -> i32 {
    if let Some(c) = rt.c {
        woe::guild_agit_start(c);
    }
    SCRIPT_CMD_SUCCESS
}

/// agitend(): ends WoE FE (guild_agit_end).
fn agitend(rt: &Rt, _st: &mut State) -> i32 {
    if let Some(c) = rt.c {
        woe::guild_agit_end(c);
    }
    SCRIPT_CMD_SUCCESS
}

/// agitstart2(): starts WoE SE (guild_agit2_start).
fn agitstart2(rt: &Rt, _st: &mut State) -> i32 {
    if let Some(c) = rt.c {
        woe::guild_agit2_start(c);
    }
    SCRIPT_CMD_SUCCESS
}

/// agitend2(): ends WoE SE (guild_agit2_end).
fn agitend2(rt: &Rt, _st: &mut State) -> i32 {
    if let Some(c) = rt.c {
        woe::guild_agit2_end(c);
    }
    SCRIPT_CMD_SUCCESS
}

/// agitstart3(): starts WoE TE (guild_agit3_start).
fn agitstart3(rt: &Rt, _st: &mut State) -> i32 {
    if let Some(c) = rt.c {
        woe::guild_agit3_start(c);
    }
    SCRIPT_CMD_SUCCESS
}

/// agitend3(): ends WoE TE (guild_agit3_end).
fn agitend3(rt: &Rt, _st: &mut State) -> i32 {
    if let Some(c) = rt.c {
        woe::guild_agit3_end(c);
    }
    SCRIPT_CMD_SUCCESS
}

/// agitcheck(): whether WoE FE is on (agit_flag).
fn agitcheck(rt: &Rt, st: &mut State) -> i32 {
    st.pushint(rt.c.map(crate::guild::agit_flag).unwrap_or(false) as i64);
    SCRIPT_CMD_SUCCESS
}

/// agitcheck2(): whether WoE SE is on (agit2_flag).
fn agitcheck2(rt: &Rt, st: &mut State) -> i32 {
    st.pushint(rt.c.map(crate::guild::agit2_flag).unwrap_or(false) as i64);
    SCRIPT_CMD_SUCCESS
}

/// agitcheck3(): whether WoE TE is on (agit3_flag).
fn agitcheck3(rt: &Rt, st: &mut State) -> i32 {
    st.pushint(rt.c.map(crate::guild::agit3_flag).unwrap_or(false) as i64);
    SCRIPT_CMD_SUCCESS
}

// ------------------------------------------------------------------------------------------------ GvG flags

/// gvgon / gvgoff / gvgon3 / gvgoff3 "<map name>": sets or clears MF_GVG / MF_GVG_TE through map_setmapflag (the map
/// property packets and the attack stops of the battle agent's port), only when the flag changes.
fn gvg_switch(rt: &Rt, st: &mut State, flag: i16, on: bool, func: &str) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let s = lat(&getstr(rt, st, 2));
    let Some(m) = crate::data::maps().id_of(&s) else {
        log::warn!("buildin_{func}: Unknown map '{s}'.");
        return SCRIPT_CMD_FAILURE;
    };
    if crate::npc::mapflag(c, m, flag) != on {
        crate::battle::pvp::map_setmapflag(c, m, flag, on, 0);
    }
    SCRIPT_CMD_SUCCESS
}

/// gvgon "<map name>";
fn gvgon(rt: &Rt, st: &mut State) -> i32 {
    gvg_switch(rt, st, crate::npc::MF_GVG, true, "gvgon")
}

/// gvgoff "<map name>";
fn gvgoff(rt: &Rt, st: &mut State) -> i32 {
    gvg_switch(rt, st, crate::npc::MF_GVG, false, "gvgoff")
}

/// gvgon3 "<map name>";
fn gvgon3(rt: &Rt, st: &mut State) -> i32 {
    gvg_switch(rt, st, crate::npc::MF_GVG_TE, true, "gvgon3")
}

/// gvgoff3 "<map name>";
fn gvgoff3(rt: &Rt, st: &mut State) -> i32 {
    gvg_switch(rt, st, crate::npc::MF_GVG_TE, false, "gvgoff3")
}

// ------------------------------------------------------------------------------------------------ maprespawnguildid

/// maprespawnguildid "<map name>",<guild id>,<flag>; — flag&1: the guild's members on the map go to their save points,
/// &2: everyone else (guildless too), &4: the map's monsters die (not guardians, not the Emperium, not clones with
/// guild_maprespawn_clones off). An unknown map does nothing.
fn maprespawnguildid(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let mapname = lat(&getstr(rt, st, 2));
    let g_id = getnum(rt, st, 3);
    let flag = getnum(rt, st, 4);
    let Some(m) = crate::data::maps().id_of(&mapname) else { return SCRIPT_CMD_SUCCESS };
    // Catch ALL players (in case some are 'between maps' on execution time)
    for u in crate::social::all_pc(c) {
        maprespawnguildid_sub_pc(c, u.id, m, g_id, flag);
    }
    if flag & 4 != 0 {
        // Remove script mobs.
        for u in crate::mob::mobs_foreachinmap(c, m) {
            if !crate::unit::get(c, u.id).map(|x| x.on_map).unwrap_or(false) {
                continue;
            }
            maprespawnguildid_sub_mob(c, u.id);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// script.cpp buildin_maprespawnguildid_sub_pc.
fn maprespawnguildid_sub_pc(c: &Ctx, aid: u32, m: u16, g_id: i32, flag: i32) -> i32 {
    let Some(u) = crate::unit::get(c, aid) else { return 0 };
    if u.map != m {
        return 0;
    }
    let gid = crate::guild::guild_id_of(c, aid);
    if (gid == g_id && flag & 1 != 0) // Warp out owners
        || (gid != g_id && flag & 2 != 0) // Warp out outsiders
        || (gid == 0 && flag & 2 != 0)
    // Warp out players not in guild
    {
        if let Some(ch) = crate::pc::character_of(c, aid) {
            // pc_setpos(sd, mapindex_name2id(sd->status.save_point.map), …): an unknown save map fails there
            if let Some(sm) = crate::data::maps().id_of(&ch.save_map) {
                crate::pc::setpos(c, aid, sm, ch.save_x as i16, ch.save_y as i16, crate::unit::CLR_TELEPORT);
            }
        }
    }
    1
}

/// battle_config.guild_maprespawn_clones (conf/battle/guild.conf: no).
fn guild_maprespawn_clones() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::GUILD_MAPRESPAWN_CLONES) != 0
}

/// script.cpp buildin_maprespawnguildid_sub_mob.
fn maprespawnguildid_sub_mob(c: &Ctx, id: u32) -> i32 {
    let Some(md) = crate::mob::get(c, id) else { return 0 };
    if woe::guardian::get(c, id).is_none() && md.mob_id != MOBID_EMPERIUM as i32 && (crate::mob::rtdb::mob_is_clone(c, md.mob_id) == 0 || guild_maprespawn_clones()) {
        crate::status::hp::status_kill(c, id);
    }
    1
}

// ------------------------------------------------------------------------------------------------ flagemblem

/// flagemblem <guild id>; — the NPC running the script shows that guild's emblem (nd->u.scr.guild_id, the emblem
/// update to its area, the guild flag cache). A negative id does nothing.
fn flagemblem(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let g_id = getnum(rt, st, 2);
    if g_id < 0 {
        return SCRIPT_CMD_SUCCESS;
    }
    let Some(nd) = c.db().npc().id().find(st.oid) else {
        log::error!("script:flagemblem: npc {} not found", st.oid);
        return SCRIPT_CMD_SUCCESS;
    };
    if nd.subtype != crate::npc::NPCTYPE_SCRIPT {
        log::error!("script:flagemblem: unexpected subtype {} for npc {} '{}'", nd.subtype, st.oid, nd.exname);
        return SCRIPT_CMD_SUCCESS;
    }
    let changed = woe::npc_guild_id(c, nd.id) != g_id;
    woe::set_npc_guild_id(c, nd.id, g_id);
    if let Some(u) = crate::unit::get(c, nd.id) {
        crate::clif_guild::clif_guild_emblem_area(c, &u);
    }
    // guild flag caching
    if g_id != 0 {
        woe::guild_flag_add(c, nd.id); // adding a id
    } else if changed {
        woe::guild_flag_remove(c, nd.id); // removing a flag
    }
    SCRIPT_CMD_SUCCESS
}

// ------------------------------------------------------------------------------------------------ castle data

/// mapindex_getmapname(script_getstr(st, 2)) → castle_db.mapname2gc.
fn castle_arg(rt: &Rt, st: &mut State) -> (String, Option<&'static crate::db::misc::castle::GuildCastle>) {
    let mapname = crate::db::misc::mapindex_getmapname(&lat(&getstr(rt, st, 2)));
    let gc = castle_db().mapname2gc(&mapname);
    (mapname, gc)
}

/// getcastlename("<map name>"): the castle's name ("" when the map has no castle).
fn getcastlename(rt: &Rt, st: &mut State) -> i32 {
    let (_, gc) = castle_arg(rt, st);
    let name = gc.map(|g| g.castle_name.clone()).unwrap_or_default();
    st.pushstr(crate::net::wire::latin1(&name));
    SCRIPT_CMD_SUCCESS
}

/// getcastledata("<map name>",<type>): one CD_* field of the castle (guardian slots: whether it is installed).
fn getcastledata(rt: &Rt, st: &mut State) -> i32 {
    let (mapname, gc) = castle_arg(rt, st);
    let index = getnum(rt, st, 3);
    let (Some(gc), Some(c)) = (gc, rt.c) else {
        st.pushint(0);
        log::warn!("buildin_getcastledata: guild castle for map '{mapname}' not found");
        return SCRIPT_CMD_FAILURE;
    };
    let d = woe::castle_rt(c, gc.castle_id).data;
    let v = match index {
        woe::CD_GUILD_ID => d.guild_id,
        woe::CD_CURRENT_ECONOMY => d.economy,
        woe::CD_CURRENT_DEFENSE => d.defense,
        woe::CD_INVESTED_ECONOMY => d.trigger_e,
        woe::CD_INVESTED_DEFENSE => d.trigger_d,
        woe::CD_NEXT_TIME => d.next_time,
        woe::CD_PAY_TIME => d.pay_time,
        woe::CD_CREATE_TIME => d.create_time,
        woe::CD_ENABLED_KAFRA => d.visible_c,
        _ => {
            if (CD_ENABLED_GUARDIAN00..CD_MAX).contains(&index) {
                d.visible_g[(index - CD_ENABLED_GUARDIAN00) as usize] as i32
            } else {
                st.pushint(0);
                log::warn!("buildin_getcastledata: index = '{index}' is out of allowed range");
                return SCRIPT_CMD_FAILURE;
            }
        }
    };
    st.pushint(v as i64);
    SCRIPT_CMD_SUCCESS
}

/// setcastledata "<map name>",<type>,<value>; (guild_castledatasave).
fn setcastledata(rt: &Rt, st: &mut State) -> i32 {
    let (mapname, gc) = castle_arg(rt, st);
    let index = getnum(rt, st, 3);
    let value = getnum(rt, st, 4);
    let (Some(gc), Some(c)) = (gc, rt.c) else {
        log::warn!("buildin_setcastledata: guild castle for map '{mapname}' not found");
        return SCRIPT_CMD_FAILURE;
    };
    if index <= CD_NONE || index >= CD_MAX {
        log::warn!("buildin_setcastledata: index = '{index}' is out of allowed range");
        return SCRIPT_CMD_FAILURE;
    }
    woe::guild_castledatasave(c, gc.castle_id, index, value);
    SCRIPT_CMD_SUCCESS
}

// ------------------------------------------------------------------------------------------------ guardians

/// script.cpp check_event: the old "Label" form (without ::On) is deprecated.
fn check_event(rt: &Rt, st: &State, evt: &[u8]) {
    if !evt.is_empty() && !evt.to_ascii_lowercase().windows(4).any(|w| w == b"::on") {
        log::warn!("NPC event parameter deprecated! Please use 'NPCNAME::OnEVENT' instead of '{}'.", lat(evt));
        reportsrc(rt, st);
    }
}

/// guardian("<map name>",<x>,<y>,"<name to show>",<mob id>{,"<event label>"}{,<guardian index>}) -> <id>: summons a
/// guardian (mob_spawn_guardian); without an index it is a temporary guardian.
fn guardian(rt: &Rt, st: &mut State) -> i32 {
    let mapname = lat(&getstr(rt, st, 2));
    let x = getnum(rt, st, 3);
    let y = getnum(rt, st, 4);
    let s = lat(&getstr(rt, st, 5));
    let class_ = getnum(rt, st, 6);
    let mut evt: Vec<u8> = Vec::new();
    let mut guardian = 0;
    let mut has_index = false;
    if st.hasdata(8) {
        // "<event label>",<guardian index>
        evt = getstr(rt, st, 7);
        guardian = getnum(rt, st, 8);
        has_index = true;
    } else if st.hasdata(7) {
        if isstring(rt, st, 7) {
            // "<event label>"
            evt = getstr(rt, st, 7);
        } else {
            // <guardian index>
            guardian = getnum(rt, st, 7);
            has_index = true;
        }
    }
    check_event(rt, st, &evt);
    let id = match rt.c {
        Some(c) => woe::guardian::mob_spawn_guardian(c, &mapname, x as i16, y as i16, &s, class_, &lat(&evt), guardian, has_index),
        None => 0,
    };
    st.pushint(id as i64);
    SCRIPT_CMD_SUCCESS
}

/// guardianinfo("<map name>",<index>,<type>) -> <value>: type 0 whether the slot's guardian is installed, 1 its max HP,
/// 2 its HP (-1: no castle, bad index, not installed or not spawned).
fn guardianinfo(rt: &Rt, st: &mut State) -> i32 {
    let (_, gc) = castle_arg(rt, st);
    let id = getnum(rt, st, 3);
    let type_ = getnum(rt, st, 4);
    let (Some(gc), Some(c)) = (gc, rt.c) else {
        st.pushint(-1);
        return SCRIPT_CMD_SUCCESS;
    };
    if id < 0 || id >= MAX_GUARDIANS as i32 {
        st.pushint(-1);
        return SCRIPT_CMD_SUCCESS;
    }
    let cr = woe::castle_rt(c, gc.castle_id);
    let visible = cr.data.visible_g[id as usize];
    let v: i64 = if type_ == 0 {
        visible as i64
    } else if !visible {
        -1
    } else {
        let gid = cr.guardian_id[id as usize];
        match (gid != 0).then(|| crate::mob::get(c, gid)).flatten() {
            None => -1,
            Some(gd) => match type_ {
                1 => gd.status.max_hp as i64,
                2 => gd.status.hp as i64,
                _ => -1,
            },
        }
    };
    st.pushint(v);
    SCRIPT_CMD_SUCCESS
}
