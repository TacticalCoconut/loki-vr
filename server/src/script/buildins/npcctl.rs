//! NPC control: enablenpc / disablenpc / hideonnpc / hideoffnpc / cloakonnpc / cloakoffnpc (+self) through
//! npc.cpp npc_enable_target, events (donpcevent, doevent, cmdothernpc), NPC timers (initnpctimer … detachnpctimer),
//! player event timers (addtimer, deltimer), npcspeed, npcstop, setnpcdisplay.

use super::*;
use crate::clif;
use crate::npc::{npc, Npc, JT_GUILD_FLAG, JT_WARPNPC, NPCTYPE_SCRIPT, NPCTYPE_WARP};
use crate::script::npc_run;
use crate::script::vm::*;
use crate::unit::{self, OPTION_CLOAK, OPTION_HIDE};
use crate::world::Ctx;
use spacetimedb::Table;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "enablenpc" => enablenpc,
        "donpcevent" => donpcevent,
        "doevent" => doevent,
        "cmdothernpc" => cmdothernpc,
        "addtimer" => addtimer,
        "deltimer" => deltimer,
        "initnpctimer" => initnpctimer,
        "startnpctimer" => startnpctimer,
        "stopnpctimer" => stopnpctimer,
        "getnpctimer" => getnpctimer,
        "setnpctimer" => setnpctimer,
        "attachnpctimer" => attachnpctimer,
        "detachnpctimer" => detachnpctimer,
        "npcspeed" => npcspeed,
        "npcstop" => npcstop,
        "setnpcdisplay" => setnpcdisplay,
        "unloadnpc" => unloadnpc,
        "movenpc" => movenpc,
        "npcshopdelitem" => npcshopdelitem,
        "checkre" => checkre,
        "getbattleflag" => getbattleflag,
        _ => return None,
    })
}

// e_npcv_status (npc.hpp)
pub const NPCVIEW_DISABLE: u8 = 0x01;
pub const NPCVIEW_ENABLE: u8 = 0x02;
pub const NPCVIEW_HIDEOFF: u8 = 0x04;
pub const NPCVIEW_HIDEON: u8 = 0x08;
pub const NPCVIEW_CLOAKOFF: u8 = 0x10;
pub const NPCVIEW_CLOAKON: u8 = 0x20;
pub const NPCVIEW_VISIBLE: u8 = NPCVIEW_ENABLE | NPCVIEW_HIDEOFF | NPCVIEW_CLOAKOFF;
pub const NPCVIEW_CLOAK: u8 = NPCVIEW_CLOAKOFF | NPCVIEW_CLOAKON;

/// npc.cpp npc_enable_target.
pub fn npc_enable_target(c: &Ctx, nd_id: u32, char_id: u32, flag: u8) -> bool {
    let Some(mut nd) = c.db().npc().id().find(nd_id) else { return false };
    let unit_row = unit::get(c, nd_id);
    if char_id > 0 && flag & NPCVIEW_CLOAK != 0 {
        let Some(sdu) = crate::pc::charid2sd(c, char_id) else {
            log::error!("npc_enable: Attempted to {} a NPC '{}' on an invalid target {char_id}.", if flag & NPCVIEW_VISIBLE != 0 { "show" } else { "hide" }, nd.name);
            return false;
        };
        let Some(mut u) = unit_row else { return true };
        let option = u.sc.option;
        if flag & NPCVIEW_CLOAKOFF != 0 {
            u.sc.option &= !OPTION_CLOAK;
        } else {
            u.sc.option |= OPTION_CLOAK;
        }
        if let Some(mut p) = crate::pc::get(c, sdu.id) {
            let found = p.cloaked_npc.iter().position(|&id| id == nd_id);
            match found {
                None if option != u.sc.option => p.cloaked_npc.push(nd_id),
                Some(i) if option == u.sc.option => {
                    p.cloaked_npc.remove(i);
                }
                _ => {}
            }
            crate::pc::put(c, &p);
        }
        if nd.class != JT_WARPNPC && nd.class != JT_GUILD_FLAG {
            clif::changeoption(c, &u, u.sc.option, Some(&sdu));
        } else if u.sc.option & (OPTION_HIDE | OPTION_CLOAK) != 0 {
            clif::clearunit_single(c, nd_id, unit::CLR_OUTSIGHT, &sdu);
        } else {
            clif::spawn(c, &u, false);
        }
        // (nd.sc.option is restored: the change was for this player only)
    } else {
        let Some(mut u) = unit_row else {
            // a floating NPC: only its enabled state exists
            if flag & NPCVIEW_ENABLE != 0 {
                nd.is_invisible = false;
            } else if flag & (NPCVIEW_HIDEOFF | NPCVIEW_HIDEON | NPCVIEW_CLOAKOFF | NPCVIEW_CLOAKON) == 0 {
                nd.is_invisible = true;
            }
            c.db().npc().id().update(nd);
            return true;
        };
        if flag & NPCVIEW_ENABLE != 0 {
            nd.is_invisible = false;
            u.invisible = false;
            unit::save(c, &u);
            clif::spawn(c, &u, false);
            u.sc.option &= !OPTION_HIDE;
        } else if flag & NPCVIEW_HIDEOFF != 0 {
            u.sc.option &= !OPTION_HIDE;
        } else if flag & NPCVIEW_HIDEON != 0 {
            u.sc.option |= OPTION_HIDE;
        } else if flag & NPCVIEW_CLOAKOFF != 0 {
            u.sc.option &= !OPTION_CLOAK;
        } else if flag & NPCVIEW_CLOAKON != 0 {
            u.sc.option |= OPTION_CLOAK;
        } else {
            u.sc.option |= OPTION_HIDE;
            nd.is_invisible = true;
            u.invisible = true;
        }
        c.db().npc().id().update(nd.clone());
        unit::save(c, &u);
        if nd.class != JT_WARPNPC && nd.class != JT_GUILD_FLAG {
            clif::changeoption(c, &u, u.sc.option, None);
            if nd.is_invisible {
                clif::clearunit_area(c, &u, unit::CLR_OUTSIGHT);
            }
        } else if u.sc.option & (OPTION_HIDE | OPTION_CLOAK) != 0 {
            clif::clearunit_area(c, &u, unit::CLR_OUTSIGHT);
        } else {
            clif::spawn(c, &u, false);
        }
        // npc_cloaked_sub: the option changed, so no player keeps a private view of it
        if nd.map >= 0 {
            for pl in crate::map::players_on_map(c, nd.map as u16) {
                if let Some(mut p) = crate::pc::get(c, pl.id) {
                    if let Some(i) = p.cloaked_npc.iter().position(|&id| id == nd_id) {
                        p.cloaked_npc.remove(i);
                        crate::pc::put(c, &p);
                    }
                }
            }
        }
    }
    if flag & NPCVIEW_VISIBLE != 0 {
        // players standing on its OnTouch area
        let nd = c.db().npc().id().find(nd_id).unwrap_or(nd);
        let (xs, ys) = match nd.subtype {
            NPCTYPE_SCRIPT | NPCTYPE_WARP => (nd.xs, nd.ys),
            _ => (-1, -1),
        };
        if xs > -1 && ys > -1 && nd.map >= 0 {
            for pl in crate::map::units_in_area(c, nd.map as u16, nd.x - xs, nd.y - ys, nd.x + xs, nd.y + ys, crate::map::BL_PC) {
                crate::npc::touch_areanpc(c, pl.id, pl.map, pl.x, pl.y, &nd);
            }
        }
    }
    true
}

/// unloadnpc "<NPC name>": the NPC and its duplicates leave the world (npc_unload_duplicates, npc_unload,
/// npc_read_event_script).
fn unloadnpc(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let name = getstr(rt, st, 2);
    let Some(nd) = npc_run::npc_name2id(c, &lat(&name)) else {
        log::error!("buildin_unloadnpc: npc '{}' was not found.", lat(&name));
        return SCRIPT_CMD_FAILURE;
    };
    if nd.id == st.oid {
        // Supporting self-unload isn't worth the problem it may cause.
        log::error!("buildin_unloadnpc: You cannot self-unload NPC '{}'.\n.", lat(&name));
        return SCRIPT_CMD_FAILURE;
    }
    // map_foreachnpc walks id_db with a DBIterator: bucket (id % HASH_SIZE), then id
    let hs = crate::script::dbmap::HASH_SIZE as u32;
    let mut dups: Vec<u32> = c.db().npc().iter().filter(|n| n.src_id == nd.id).map(|n| n.id).collect();
    dups.sort_by_key(|&id| (id % hs, id));
    for id in dups {
        crate::npc::unload(c, id);
    }
    crate::npc::unload(c, nd.id);
    npc_run::npc_read_event_script(c);
    SCRIPT_CMD_SUCCESS
}

/// movenpc "<NPC name>",<x>,<y>{,<dir>}.
fn movenpc(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let name = getstr(rt, st, 2);
    let x = getnum(rt, st, 3);
    let y = getnum(rt, st, 4);
    let Some(nd) = npc_run::npc_name2id(c, &lat(&name)) else {
        log::error!("script: movenpc: NPC with ID '{}' was not found!", lat(&name));
        return -1;
    };
    if st.hasdata(5) {
        let dir = getnum(rt, st, 5) % 8;
        if let Some(mut u) = unit::get(c, nd.id) {
            u.ud.dir = dir as u8;
            unit::save(c, &u);
        }
    }
    npc_movenpc(c, &nd, x as i16, y as i16);
    SCRIPT_CMD_SUCCESS
}

/// npc.cpp npc_movenpc. (npc_setcells is not redone: the OnTouch cells stay where they were; here CELL_CHKNPC
/// follows the NPC's area wherever it is.)
pub fn npc_movenpc(c: &Ctx, nd: &Npc, x: i16, y: i16) -> bool {
    let Some(mut u) = unit::get(c, nd.id) else { return false };
    if nd.map < 0 || !u.on_map {
        return false; // not on a map
    }
    let Some(md) = crate::data::maps().get(nd.map as u16) else { return false };
    let x = x.clamp(0, md.xs - 1);
    let y = y.clamp(0, md.ys - 1);
    let r = crate::conf::area_size();
    for pl in crate::map::units_in_area(c, u.map, u.x - r, u.y - r, u.x + r, u.y + r, crate::map::BL_PC) {
        clif::outsight(c, &pl, &u);
    }
    unit::moveblock(c, &mut u, x, y, c.tick());
    unit::save(c, &u);
    if let Some(cur) = c.db().npc().id().find(nd.id) {
        c.db().npc().id().update(Npc { x, y, ..cur });
    }
    for pl in crate::map::units_in_area(c, u.map, x - r, y - r, x + r, y + r, crate::map::BL_PC) {
        clif::insight(c, &pl, &u);
    }
    true
}

/// npcshopdelitem "<NPC name>",<item id>{,<item id>...}: removes items from a shop's list.
fn npcshopdelitem(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let name = getstr(rt, st, 2);
    let nd = npc_run::npc_name2id(c, &lat(&name));
    use crate::npc::{NPCTYPE_CASHSHOP, NPCTYPE_ITEMSHOP, NPCTYPE_MARKETSHOP, NPCTYPE_POINTSHOP, NPCTYPE_SHOP};
    let Some(mut nd) = nd.filter(|n| [NPCTYPE_SHOP, NPCTYPE_CASHSHOP, NPCTYPE_ITEMSHOP, NPCTYPE_POINTSHOP, NPCTYPE_MARKETSHOP].contains(&n.subtype)) else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let amount = st.lastdata() - 2;
    for i in 3..3 + amount {
        let nameid = getnum(rt, st, i) as u32;
        if let Some(n) = nd.shop.iter().position(|it| it.nameid == nameid) {
            nd.shop.remove(n);
            if nd.subtype == NPCTYPE_MARKETSHOP {
                crate::shop::npc_market_delfromsql(c, &nd.exname, nameid, false);
            }
        }
    }
    c.db().npc().id().update(nd);
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}

/// checkre(<type>): the renewal switches the server was built with (RENEWAL, RENEWAL_CAST, RENEWAL_DROP,
/// RENEWAL_EXP, RENEWAL_LVDMG, RENEWAL_ASPD: all on). An unknown type pushes nothing.
fn checkre(rt: &Rt, st: &mut State) -> i32 {
    let num = getnum(rt, st, 2);
    match num {
        0..=5 => st.pushint(1),
        _ => log::warn!("buildin_checkre: unknown parameter."),
    }
    SCRIPT_CMD_SUCCESS
}

/// getbattleflag "<config name>": battle_get_value (battle.cpp:8939): the member's value now (battle_config/mod.rs).
fn getbattleflag(rt: &Rt, st: &mut State) -> i32 {
    let flag = getstr(rt, st, 2);
    let name = lat(&flag);
    let v = crate::battle_config::battle_get_value(&name);
    st.pushint(v as i64);
    SCRIPT_CMD_SUCCESS
}

/// enablenpc and its aliases.
fn enablenpc(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let command = st.funcname.clone();
    let nd: Option<Npc> = if st.hasdata(2) {
        let n = getstr(rt, st, 2);
        npc_run::npc_name2id(c, &lat(&n))
    } else {
        c.db().npc().id().find(st.oid)
    };
    let flag = match command.as_str() {
        "enablenpc" => NPCVIEW_ENABLE,
        "disablenpc" => NPCVIEW_DISABLE,
        "hideoffnpc" => NPCVIEW_HIDEOFF,
        "hideonnpc" => NPCVIEW_HIDEON,
        "cloakoffnpc" | "cloakoffnpcself" => NPCVIEW_CLOAKOFF,
        "cloakonnpc" | "cloakonnpcself" => NPCVIEW_CLOAKON,
        _ => {
            log::error!("buildin_enablenpc: Undefined command \"{command}\".");
            return SCRIPT_CMD_FAILURE;
        }
    };
    let Some(nd) = nd else {
        let what = if flag & NPCVIEW_VISIBLE != 0 { "show" } else { "hide" };
        if st.hasdata(2) {
            let n = getstr(rt, st, 2);
            log::error!("buildin_{command}: Attempted to {what} a non-existing NPC '{}' (flag={flag}).", lat(&n));
        } else {
            log::error!("buildin_{command}: Attempted to {what} a non-existing NPC (flag={flag}).");
        }
        return SCRIPT_CMD_FAILURE;
    };
    let mut char_id = 0u32;
    if st.hasdata(3) {
        char_id = getnum(rt, st, 3) as u32;
    } else if command == "cloakoffnpcself" || command == "cloakonnpcself" {
        let Some(aid) = rid2sd(rt, st, "buildin_enablenpc") else { return SCRIPT_CMD_SUCCESS };
        char_id = crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
    }
    if npc_enable_target(c, nd.id, char_id, flag) {
        SCRIPT_CMD_SUCCESS
    } else {
        SCRIPT_CMD_FAILURE
    }
}

/// check_event: the old "Label" form (without ::On) is deprecated.
fn check_event(rt: &Rt, st: &State, evt: &[u8]) {
    if !evt.is_empty() && !evt.to_ascii_lowercase().windows(4).any(|w| w == b"::on") {
        log::warn!("NPC event parameter deprecated! Please use 'NPCNAME::OnEVENT' instead of '{}'.", lat(evt));
        reportsrc(rt, st);
    }
}

/// donpcevent.
fn donpcevent(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let event = getstr(rt, st, 2);
    check_event(rt, st, &event);
    if npc_run::npc_event_do(c, &lat(&event)) == 0 {
        let src = c.db().npc().id().find(st.oid).map(|n| n.name).unwrap_or_else(|| "Unknown".into());
        log::debug!("NPCEvent '{}' not found! (source: {src})", lat(&event));
        st.pushint(0);
    } else {
        st.pushint(1);
    }
    SCRIPT_CMD_SUCCESS
}

/// doevent.
fn doevent(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_doevent") else { return SCRIPT_CMD_SUCCESS };
    let event = getstr(rt, st, 2);
    check_event(rt, st, &event);
    npc_run::npc_event(rt.c.unwrap(), aid, &lat(&event), 0);
    SCRIPT_CMD_SUCCESS
}

/// cmdothernpc.
fn cmdothernpc(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let npc = getstr(rt, st, 2);
    let command = getstr(rt, st, 3);
    let event: String = format!("{}::{}{}", lat(&npc), npc_run::ONCOMMAND_EVENT_NAME, lat(&command)).chars().take(npc_run::EVENT_NAME_LENGTH - 1).collect();
    check_event(rt, st, event.as_bytes());
    if npc_run::npc_event_do(c, &event) != 0 {
        st.pushint(1);
    } else {
        let src = c.db().npc().id().find(st.oid).map(|n| n.name).unwrap_or_else(|| "Unknown".into());
        log::debug!("NPCEvent '{event}' not found! (source: {src})");
        st.pushint(0);
    }
    SCRIPT_CMD_SUCCESS
}

/// addtimer.
fn addtimer(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_addtimer") else { return SCRIPT_CMD_SUCCESS };
    let tick = getnum(rt, st, 2);
    let event = getstr(rt, st, 3);
    check_event(rt, st, &event);
    let c = rt.c.unwrap();
    if !npc_run::pc_addeventtimer(c, aid, tick as i64, &lat(&event)) {
        log::warn!("buildin_addtimer: Event timer is full, can't add new event timer. (aid:{aid} timer:{})", lat(&event));
        return SCRIPT_CMD_FAILURE;
    }
    SCRIPT_CMD_SUCCESS
}

/// deltimer.
fn deltimer(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_deltimer") else { return SCRIPT_CMD_SUCCESS };
    let event = getstr(rt, st, 2);
    check_event(rt, st, &event);
    npc_run::pc_deleventtimer(rt.c.unwrap(), aid, &lat(&event));
    SCRIPT_CMD_SUCCESS
}

/// The NPC and flag arguments shared by initnpctimer / startnpctimer / stopnpctimer.
fn timer_args(rt: &Rt, st: &mut State) -> (Option<Npc>, i32) {
    let c = rt.c.unwrap();
    if st.hasdata(3) {
        let n = getstr(rt, st, 2);
        (npc_run::npc_name2id(c, &lat(&n)), getnum(rt, st, 3))
    } else if st.hasdata(2) {
        if isstring(rt, st, 2) {
            let n = getstr(rt, st, 2);
            (npc_run::npc_name2id(c, &lat(&n)), 0)
        } else {
            (c.db().npc().id().find(st.oid), getnum(rt, st, 2))
        }
    } else {
        (c.db().npc().id().find(st.oid), 0)
    }
}

/// initnpctimer.
fn initnpctimer(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let (nd, flag) = timer_args(rt, st);
    let Some(nd) = nd else { return SCRIPT_CMD_SUCCESS };
    if flag != 0 {
        let Some(aid) = rid2sd(rt, st, "buildin_initnpctimer") else { return SCRIPT_CMD_SUCCESS };
        npc_run::set_npc_timer_rid(c, nd.id, aid);
    }
    npc_run::reset_npc_timertick(c, nd.id);
    npc_run::npc_settimerevent_tick(c, nd.id, 0);
    npc_run::npc_timerevent_start(c, nd.id);
    SCRIPT_CMD_SUCCESS
}

/// startnpctimer.
fn startnpctimer(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let (nd, flag) = timer_args(rt, st);
    let Some(nd) = nd else { return SCRIPT_CMD_SUCCESS };
    if flag != 0 {
        let Some(aid) = rid2sd(rt, st, "buildin_startnpctimer") else { return SCRIPT_CMD_SUCCESS };
        npc_run::set_npc_timer_rid(c, nd.id, aid);
    }
    npc_run::npc_timerevent_start(c, nd.id);
    SCRIPT_CMD_SUCCESS
}

/// stopnpctimer.
fn stopnpctimer(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let (nd, flag) = timer_args(rt, st);
    let Some(nd) = nd else { return SCRIPT_CMD_SUCCESS };
    if flag != 0 {
        npc_run::set_npc_timer_rid(c, nd.id, 0);
    }
    npc_run::npc_timerevent_stop(c, nd.id);
    SCRIPT_CMD_SUCCESS
}

/// getnpctimer.
fn getnpctimer(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let kind = getnum(rt, st, 2);
    let nd = if st.hasdata(3) {
        let n = getstr(rt, st, 3);
        npc_run::npc_name2id(c, &lat(&n))
    } else {
        c.db().npc().id().find(st.oid)
    };
    let Some(nd) = nd else {
        st.pushint(0);
        log::error!("getnpctimer: Invalid NPC.");
        return SCRIPT_CMD_FAILURE;
    };
    let (rid, timerid, amount) = npc_run::npc_timer_info(c, nd.id);
    let val: i64 = match kind {
        0 => npc_run::npc_gettimerevent_tick(c, nd.id),
        1 => {
            if rid != 0 {
                if crate::pc::get(c, rid).is_none() {
                    log::error!("buildin_getnpctimer: Attached player not found!");
                    0
                } else {
                    (npc_run::sdx(c, rid).npc_timer_id != 0) as i64
                }
            } else {
                (timerid != 0) as i64
            }
        }
        2 => amount as i64,
        _ => 0,
    };
    st.pushint(val);
    SCRIPT_CMD_SUCCESS
}

/// setnpctimer.
fn setnpctimer(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let tick = getnum(rt, st, 2);
    let nd = if st.hasdata(3) {
        let n = getstr(rt, st, 3);
        npc_run::npc_name2id(c, &lat(&n))
    } else {
        c.db().npc().id().find(st.oid)
    };
    let Some(nd) = nd else {
        st.pushint(1);
        log::error!("setnpctimer: Invalid NPC.");
        return SCRIPT_CMD_FAILURE;
    };
    npc_run::npc_settimerevent_tick(c, nd.id, tick);
    st.pushint(0);
    SCRIPT_CMD_SUCCESS
}

/// attachnpctimer.
fn attachnpctimer(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let Some(nd) = c.db().npc().id().find(st.oid) else {
        st.pushint(1);
        log::error!("setnpctimer: Invalid NPC.");
        return SCRIPT_CMD_FAILURE;
    };
    let Some(aid) = nick2sd(rt, st, 2, "buildin_attachnpctimer") else {
        st.pushint(1);
        log::warn!("attachnpctimer: Invalid player.");
        return SCRIPT_CMD_FAILURE;
    };
    npc_run::set_npc_timer_rid(c, nd.id, aid);
    st.pushint(0);
    SCRIPT_CMD_SUCCESS
}

/// detachnpctimer.
fn detachnpctimer(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let nd = if st.hasdata(2) {
        let n = getstr(rt, st, 2);
        npc_run::npc_name2id(c, &lat(&n))
    } else {
        c.db().npc().id().find(st.oid)
    };
    let Some(nd) = nd else {
        st.pushint(1);
        log::error!("detachnpctimer: Invalid NPC.");
        return SCRIPT_CMD_FAILURE;
    };
    npc_run::set_npc_timer_rid(c, nd.id, 0);
    st.pushint(0);
    SCRIPT_CMD_SUCCESS
}

fn target_npc(rt: &Rt, st: &mut State, i: i32, func: &str) -> Option<Npc> {
    let c = rt.c?;
    let nd = if st.hasdata(i) {
        let n = getstr(rt, st, i);
        npc_run::npc_name2id(c, &lat(&n))
    } else {
        c.db().npc().id().find(st.oid)
    };
    if nd.is_none() {
        if st.hasdata(i) {
            let n = getstr(rt, st, i);
            log::error!("buildin_{func}: {} is a non-existing NPC.", lat(&n));
        } else {
            log::error!("buildin_{func}: non-existing NPC.");
        }
    }
    nd
}

/// npcspeed.
fn npcspeed(rt: &Rt, st: &mut State) -> i32 {
    let Some(nd) = target_npc(rt, st, 3, "npcspeed") else { return SCRIPT_CMD_FAILURE };
    let speed = getnum(rt, st, 2);
    if !(20..=1000).contains(&speed) {
        log::error!("buildin_npcspeed: invalid speed {speed} (min: 20, max: 1000).");
        return SCRIPT_CMD_FAILURE;
    }
    let c = rt.c.unwrap();
    if let Some(mut u) = unit::get(c, nd.id) {
        u.speed = speed as u16;
        unit::save(c, &u);
    }
    SCRIPT_CMD_SUCCESS
}

/// npcstop.
fn npcstop(rt: &Rt, st: &mut State) -> i32 {
    let Some(nd) = target_npc(rt, st, 2, "npcstop") else { return SCRIPT_CMD_FAILURE };
    let c = rt.c.unwrap();
    let mut flag = unit::USW_FIXPOS | unit::USW_MOVE_FULL_CELL | unit::USW_FORCE_STOP;
    let Some(mut u) = unit::get(c, nd.id) else { return SCRIPT_CMD_SUCCESS };
    if st.hasdata(3) {
        flag = getnum(rt, st, 3);
        if !(0..=0x1f).contains(&flag) {
            log::error!("buildin_npcstop: invalid flag {flag}.");
            return SCRIPT_CMD_FAILURE;
        }
        if flag & unit::USW_FORCE_STOP != 0 {
            u.ud.force_walk = false;
        }
    }
    unit::stop_walking(c, &mut u, flag, 0);
    unit::save(c, &u);
    SCRIPT_CMD_SUCCESS
}

/// setnpcdisplay.
fn setnpcdisplay(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let name = getstr(rt, st, 2);
    let mut class = crate::npc::JT_FAKENPC;
    let mut size = -1;
    if st.hasdata(4) {
        class = getnum(rt, st, 4);
    }
    if st.hasdata(5) {
        size = getnum(rt, st, 5);
    }
    let mut newname = None;
    if isstring(rt, st, 3) {
        newname = Some(getstr(rt, st, 3));
    } else {
        class = getnum(rt, st, 3);
    }
    let Some(mut nd) = npc_run::npc_name2id(c, &lat(&name)) else {
        st.pushint(1);
        return SCRIPT_CMD_SUCCESS;
    };
    let users = |m: i32| m >= 0 && !crate::map::players_on_map(c, m as u16).is_empty();
    if let Some(n) = newname {
        // npc_setdisplayname
        let s: String = lat(&n).chars().take(crate::npc::NPC_NAME_LENGTH).collect();
        nd.name = s.clone();
        c.db().npc().id().update(nd.clone());
        if let Some(mut u) = unit::get(c, nd.id) {
            u.name = s;
            unit::save(c, &u);
            if users(nd.map) {
                clif::name_area(c, &u);
            }
        }
    }
    let cur_size = unit::get(c, nd.id).map(|u| u.size as i32).unwrap_or(0);
    if size != -1 && size != cur_size {
        if let Some(mut u) = unit::get(c, nd.id) {
            u.size = size as u8;
            unit::save(c, &u);
        }
    } else {
        size = -1;
    }
    if class != crate::npc::JT_FAKENPC && nd.class != class {
        // npc_setclass: status_set_viewdata + unit_refresh
        nd.class = class;
        c.db().npc().id().update(nd.clone());
        if let Some(mut u) = unit::get(c, nd.id) {
            u.vd = crate::npc::npc_viewdata(class);
            unit::save(c, &u);
            if users(nd.map) {
                clif::clearunit_area(c, &u, unit::CLR_TRICKDEAD);
                clif::spawn(c, &u, false);
            }
        }
    } else if size != -1 {
        if let Some(u) = unit::get(c, nd.id) {
            if users(nd.map) {
                clif::clearunit_area(c, &u, unit::CLR_TRICKDEAD);
                clif::spawn(c, &u, false);
            }
        }
    }
    st.pushint(0);
    SCRIPT_CMD_SUCCESS
}
