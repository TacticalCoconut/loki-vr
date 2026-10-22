//! The mercenary script commands of script.cpp: getmercinfo, mercenary_create, mercenary_delete, mercenary_heal,
//! mercenary_sc_start, mercenary_get_calls, mercenary_set_calls, mercenary_get_faith, mercenary_set_faith,
//! checkidlemer.

use super::data::{self, ARCH_MERC_GUILD, SPEAR_MERC_GUILD, SWORD_MERC_GUILD};
use crate::script::buildins::{charid2sd, nick2sd, BuildinFn};
use crate::script::vm::*;
use crate::status::consts::SP_MERCFAITH;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "getmercinfo" => getmercinfo,
        "mercenary_create" => mercenary_create,
        "mercenary_delete" => mercenary_delete,
        "mercenary_heal" => mercenary_heal,
        "mercenary_sc_start" => mercenary_sc_start,
        "mercenary_get_calls" => mercenary_get_calls,
        "mercenary_set_calls" => mercenary_set_calls,
        "mercenary_get_faith" => mercenary_get_faith,
        "mercenary_set_faith" => mercenary_set_faith,
        "checkidlemer" => checkidlemer,
        _ => return None,
    })
}

/// getmercinfo <type>[,<char id>]: information about the character's mercenary (0 id, 1 class, 2 name, 3 faith,
/// 4 calls, 5 kills, 6 contract time left, 7 level, 8 block id); without one 0 (type 2: "").
fn getmercinfo(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else {
        st.pushnil();
        return SCRIPT_CMD_FAILURE;
    };
    let Some(sd) = charid2sd(rt, st, 3, "getmercinfo") else {
        st.pushnil();
        return SCRIPT_CMD_FAILURE;
    };
    let type_ = getnum(rt, st, 2);
    let Some(md) = data::of_master(c, sd) else {
        if type_ == 2 {
            st.pushstr(Vec::new());
        } else {
            st.pushint(0);
        }
        return SCRIPT_CMD_SUCCESS;
    };
    match type_ {
        0 => st.pushint(md.mercenary.mercenary_id as i64),
        1 => st.pushint(md.mercenary.class_ as i64),
        2 => st.pushstr(crate::net::wire::latin1(data::db_of(&md).map(|d| d.name.as_str()).unwrap_or(""))),
        3 => st.pushint(super::mercenary_get_faith(c, &md) as i64),
        4 => st.pushint(super::mercenary_get_calls(c, &md) as i64),
        5 => st.pushint(md.mercenary.kill_count as i64),
        6 => st.pushint(super::mercenary_get_lifetime(c, &md)),
        7 => st.pushint(data::db_of(&md).map(|d| d.lv as i64).unwrap_or(0)),
        8 => st.pushint(md.id as i64),
        _ => {
            let char_id = crate::pc::get(c, sd).map(|p| p.char_id).unwrap_or(0);
            log::error!("buildin_getmercinfo: Invalid type {type_} (char_id={char_id}).");
            st.pushnil();
            return SCRIPT_CMD_FAILURE;
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// mercenary_create <class>,<contract time>: a new mercenary for the attached player (none when he already has one or
/// a mercenary id, or the class is unknown).
fn mercenary_create(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(sd) = rid2sd(rt, st, "mercenary_create") else { return SCRIPT_CMD_SUCCESS };
    if data::of_master(c, sd).is_some() || data::mer_id_of(c, sd) != 0 {
        return SCRIPT_CMD_SUCCESS;
    }
    let class_ = getnum(rt, st, 2);
    if !crate::db::misc::mercenary::mercenary_db().exists(class_) {
        return SCRIPT_CMD_SUCCESS;
    }
    let contract_time = getnum(rt, st, 3);
    super::mercenary_create(c, sd, class_, contract_time as u32);
    SCRIPT_CMD_SUCCESS
}

/// mercenary_delete [<char id>[,<type>]]: mercenary_delete with the reply type (0..3, default 0).
fn mercenary_delete(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let Some(sd) = charid2sd(rt, st, 2, "mercenary_delete") else { return SCRIPT_CMD_FAILURE };
    let Some(md) = data::of_master(c, sd) else {
        let (name, cid) = crate::pc::character_of(c, sd).map(|ch| (ch.name.clone(), ch.id)).unwrap_or_default();
        log::warn!("buildin_mercenary_delete: Tried to delete a non existant mercenary from player '{name}' (AID: {sd}, CID: {cid})");
        return SCRIPT_CMD_FAILURE;
    };
    let mut type_ = 0;
    if st.hasdata(3) {
        type_ = getnum(rt, st, 3);
        if !(0..=3).contains(&type_) {
            log::warn!("buildin_mercenary_delete: invalid type value of {type_}.");
            return SCRIPT_CMD_FAILURE;
        }
    }
    super::mercenary_delete(c, md.id, type_);
    SCRIPT_CMD_SUCCESS
}

/// mercenary_heal <hp>,<sp>: status_heal of the attached player's mercenary.
fn mercenary_heal(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(sd) = rid2sd(rt, st, "mercenary_heal") else { return SCRIPT_CMD_SUCCESS };
    let Some(md) = data::of_master(c, sd) else { return SCRIPT_CMD_SUCCESS };
    let hp = getnum(rt, st, 2);
    let sp = getnum(rt, st, 3);
    crate::status::hp::status_heal(c, md.id, hp as i64, sp as i64, 0, 0);
    SCRIPT_CMD_SUCCESS
}

/// mercenary_sc_start <type>,<tick>,<val1>: status_change_start on the mercenary (rate 10000, SCSTART_NOTICKDEF).
fn mercenary_sc_start(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(sd) = rid2sd(rt, st, "mercenary_sc_start") else { return SCRIPT_CMD_SUCCESS };
    let Some(md) = data::of_master(c, sd) else { return SCRIPT_CMD_SUCCESS };
    let type_ = getnum(rt, st, 2);
    let tick = getnum(rt, st, 3);
    let val1 = getnum(rt, st, 4);
    if let Some(u) = crate::unit::get(c, md.id) {
        crate::status::sc::status_change_start(c, None, &u, type_, 10000, val1, 0, 0, 0, tick as i64, crate::status::defs::SCSTART_NOTICKDEF, 0);
    }
    SCRIPT_CMD_SUCCESS
}

/// mercenary_get_calls <guild>: the attached player's calls of that guild (0 for another value).
fn mercenary_get_calls(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(sd) = rid2sd(rt, st, "mercenary_get_calls") else { return SCRIPT_CMD_SUCCESS };
    let o = data::owner(c, sd);
    let v = match getnum(rt, st, 2) {
        ARCH_MERC_GUILD => o.arch_calls,
        SPEAR_MERC_GUILD => o.spear_calls,
        SWORD_MERC_GUILD => o.sword_calls,
        _ => 0,
    };
    st.pushint(v as i64);
    SCRIPT_CMD_SUCCESS
}

/// mercenary_set_calls <guild>,<value>: adds to the attached player's calls of that guild (0..INT_MAX).
fn mercenary_set_calls(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(sd) = rid2sd(rt, st, "mercenary_set_calls") else { return SCRIPT_CMD_SUCCESS };
    let guild = getnum(rt, st, 2);
    let value = getnum(rt, st, 3);
    let mut o = data::owner(c, sd);
    let calls = match guild {
        ARCH_MERC_GUILD => &mut o.arch_calls,
        SPEAR_MERC_GUILD => &mut o.spear_calls,
        SWORD_MERC_GUILD => &mut o.sword_calls,
        _ => {
            log::error!("buildin_mercenary_set_calls: Invalid guild '{guild}'.");
            return SCRIPT_CMD_SUCCESS; // Invalid Guild
        }
    };
    *calls = calls.wrapping_add(value).clamp(0, i32::MAX);
    data::put_owner(c, &o);
    SCRIPT_CMD_SUCCESS
}

/// mercenary_get_faith <guild>: the attached player's faith with that guild (0 for another value).
fn mercenary_get_faith(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(sd) = rid2sd(rt, st, "mercenary_get_faith") else { return SCRIPT_CMD_SUCCESS };
    let o = data::owner(c, sd);
    let v = match getnum(rt, st, 2) {
        ARCH_MERC_GUILD => o.arch_faith,
        SPEAR_MERC_GUILD => o.spear_faith,
        SWORD_MERC_GUILD => o.sword_faith,
        _ => 0,
    };
    st.pushint(v as i64);
    SCRIPT_CMD_SUCCESS
}

/// mercenary_set_faith <guild>,<value>: adds to the attached player's faith with that guild (0..INT_MAX); the current
/// mercenary's master sees it when it is of that guild.
fn mercenary_set_faith(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(sd) = rid2sd(rt, st, "mercenary_set_faith") else { return SCRIPT_CMD_SUCCESS };
    let guild = getnum(rt, st, 2);
    let value = getnum(rt, st, 3);
    let mut o = data::owner(c, sd);
    let faith = match guild {
        ARCH_MERC_GUILD => &mut o.arch_faith,
        SPEAR_MERC_GUILD => &mut o.spear_faith,
        SWORD_MERC_GUILD => &mut o.sword_faith,
        _ => {
            log::error!("buildin_mercenary_set_faith: Invalid guild '{guild}'.");
            return SCRIPT_CMD_SUCCESS; // Invalid Guild
        }
    };
    *faith = faith.wrapping_add(value).clamp(0, i32::MAX);
    data::put_owner(c, &o);
    // mercenary_get_guild(sd->md): NONE_MERC_GUILD without a mercenary
    let g = data::of_master(c, sd).map(|md| super::mercenary_get_guild(&md)).unwrap_or(data::NONE_MERC_GUILD);
    if g == guild {
        super::clif::clif_mercenary_updatestatus(c, sd, SP_MERCFAITH);
    }
    SCRIPT_CMD_SUCCESS
}

// mer_idle_no_share is off (drops.conf `mer_idle_no_share: no`): every `sd->idletime_mer = last_tick` of clif.cpp /
// atcommand.cpp is behind `battle_config.mer_idle_no_share &&`, so sd->idletime_mer keeps the 0 of the calloc'd
// map_session_data (clif.cpp CREATE + placement new; pc_setnewpc never sets it). checkidlemer relies on that.
const _: () = assert!(crate::mob::conf::MER_IDLE_NO_SHARE == 0, "mer_idle_no_share on: sd->idletime_mer has to become a pc field");

/// script.cpp BUILDIN_FUNC(checkidlemer) (script.cpp:18834-18843): `DIFF_TICK(last_tick, sd->idletime_mer)` of the
/// player named (or attached), 0 when not found. With mer_idle_no_share off idletime_mer is never set (see above), so
/// the answer is last_tick itself (time(nullptr), seconds), exactly what rAthena answers.
fn checkidlemer(rt: &Rt, st: &mut State) -> i32 {
    match nick2sd(rt, st, 2, "buildin_checkidlemer") {
        Some(_sd) => {
            let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
            let idletime_mer: i64 = 0;
            st.pushint(crate::pc::last_tick(c) - idletime_mer);
        }
        None => st.pushint(0),
    }
    SCRIPT_CMD_SUCCESS
}
