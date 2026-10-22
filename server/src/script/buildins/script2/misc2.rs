//! Player information and small utilities: isdead, getvar, getexp2, getbaseexp_ratio, getjobexp_ratio, recovery,
//! areapercentheal, getareadropitem, getscrate.

use crate::flooritem::flooritem as _;
use crate::script::buildins::*;
use crate::script::reg;
use crate::script::vm::*;
use crate::unit;
use crate::world::Ctx;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "isdead" => isdead,
        "getvar" => getvar,
        "getexp2" => getexp2,
        "getbaseexp_ratio" => getbaseexp_ratio,
        "getjobexp_ratio" => getjobexp_ratio,
        "recovery" => recovery,
        "areapercentheal" => areapercentheal,
        "getareadropitem" => getareadropitem,
        "getscrate" => getscrate,
        "vip_time" => vip_time,
        _ => return None,
    })
}

/// isdead({<account id>}): pc_isdead.
fn isdead(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = super::unitctl::script_mapid2sd(rt, st, 2, "buildin_isdead") else { return SCRIPT_CMD_FAILURE };
    st.pushint(crate::pc::isdead(rt.c.unwrap(), aid) as i64);
    SCRIPT_CMD_SUCCESS
}

/// getvar(<variable>,<char id>): another player's character / account variable (not `.`, `$`, `'` or a parameter).
fn getvar(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let char_id = getnum(rt, st, 3);
    let Some(sd) = crate::pc::charid2sd(c, char_id as u32).map(|u| u.id) else {
        log::error!("buildin_getvar: No player found with char id '{char_id}'.");
        return SCRIPT_CMD_FAILURE;
    };
    let d = st.data(2).clone();
    let Data::Name(id, idx, rf) = d.clone() else {
        log::error!("buildin_getvar: Not a variable");
        reportdata(&d);
        st.pushnil();
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    };
    let name = name_of(id);
    if str_type(id).0 == crate::script::compiler::C_PARAM {
        log::error!("buildin_getvar: '{}' is a parameter - please use readparam instead", lat(&name));
        reportdata(&d);
        st.pushnil();
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    if matches!(name.first(), Some(b'.') | Some(b'$') | Some(b'\'')) {
        // Not a PC variable
        log::error!("buildin_getvar: Invalid scope (not PC variable)");
        reportdata(&d);
        st.pushnil();
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    if is_string_variable(&name) {
        let v = reg::read_str(rt, st, Some(sd), &name, idx, &rf);
        st.pushstr(v);
    } else {
        let v = reg::read_num(rt, st, Some(sd), &name, idx, &rf);
        st.pushint(v);
    }
    SCRIPT_CMD_SUCCESS
}

/// getexp2 <base exp>,<job exp>{,<char id>}: pc_gainexp (flag 2: no bonuses) or pc_lostexp for negative values.
fn getexp2(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let base_exp = getnum64(rt, st, 2);
    let job_exp = getnum64(rt, st, 3);
    let Some(aid) = charid2sd(rt, st, 4, "buildin_getexp2") else { return SCRIPT_CMD_FAILURE };
    if base_exp == 0 && job_exp == 0 {
        log::error!("buildin_getexp2: Called with base and job exp 0.");
        return SCRIPT_CMD_FAILURE;
    }
    use crate::status::progress::{pc_gainexp, pc_lostexp};
    if base_exp > 0 {
        pc_gainexp(c, aid, None, base_exp as u64, 0, 2);
    } else if base_exp < 0 {
        pc_lostexp(c, aid, base_exp.unsigned_abs(), 0);
    }
    if job_exp > 0 {
        pc_gainexp(c, aid, None, 0, job_exp as u64, 2);
    } else if job_exp < 0 {
        pc_lostexp(c, aid, 0, job_exp.unsigned_abs());
    }
    SCRIPT_CMD_SUCCESS
}

/// getbaseexp_ratio(<percent>{,<base level>{,<char id>}}) / getjobexp_ratio: percent of the class's exp for that level.
fn exp_ratio(rt: &Rt, st: &mut State, base: bool) -> i32 {
    let func = if base { "getbaseexp_ratio" } else { "getjobexp_ratio" };
    let c = rt.c.unwrap();
    let Some(aid) = charid2sd(rt, st, 4, &format!("buildin_{func}")) else { return SCRIPT_CMD_FAILURE };
    let Some(ch) = crate::pc::character_of(c, aid) else { return SCRIPT_CMD_FAILURE };
    let percent = getnum(rt, st, 2);
    if !(0..=100).contains(&percent) {
        log::error!("{func}: percentage is out of range.");
        return SCRIPT_CMD_FAILURE;
    }
    let db = crate::db::jobs::job_db();
    let job_name = || crate::data::msg_txt(crate::db::jobs::ids::job_name_msg(ch.class as i32));
    let level: i32 = if st.hasdata(3) {
        let l = getnum(rt, st, 3);
        if l < 1 {
            log::error!("{func}: level cannot be below 1.");
            return SCRIPT_CMD_FAILURE;
        }
        let max = if base { db.get_max_base_lv(ch.class) } else { db.get_max_job_lv(ch.class) };
        if l as u32 > max {
            log::error!("{func}: level is above maximum {} level {max} for job {}.", if base { "base" } else { "job" }, job_name());
            return SCRIPT_CMD_FAILURE;
        }
        l
    } else if base {
        ch.base_level as i32
    } else {
        ch.job_level as i32
    };
    let class_exp = if base { db.get_base_exp(ch.class, level as u32) } else { db.get_job_exp(ch.class, level as u32) };
    if class_exp == 0 {
        log::error!("{func}: No {} experience defined for class {} at {} level {level}.", if base { "base" } else { "job" }, job_name(), if base { "base" } else { "job" });
        return SCRIPT_CMD_FAILURE;
    }
    let result = (class_exp as f64 * percent as f64) / 100.0;
    st.pushint(result as i64);
    SCRIPT_CMD_SUCCESS
}

fn getbaseexp_ratio(rt: &Rt, st: &mut State) -> i32 {
    exp_ratio(rt, st, true)
}

fn getjobexp_ratio(rt: &Rt, st: &mut State) -> i32 {
    exp_ratio(rt, st, false)
}

/// script.cpp recovery_sub: revive (flag &1|4) a dead player, or fully heal (flag &1|2) a living one.
fn recovery_sub(c: &Ctx, aid: u32, revive: i32) {
    let dead = crate::pc::isdead(c, aid);
    if revive & (1 | 4) != 0 && dead {
        crate::status::hp::status_revive(c, aid, 100, 100, 0);
        if let Some(u) = unit::get(c, aid) {
            crate::clif::displaymessage(c, &u, crate::data::msg_txt(16)); // You've been revived!
            let ef = crate::data::constant("EF_RESURRECTION").unwrap_or(0) as i32;
            crate::clif::specialeffect(c, &u, ef, crate::clif::Target::Area);
        }
    } else if revive & (1 | 2) != 0 && !dead {
        crate::status::hp::status_percent_heal(c, aid, 100, 100, 0);
        if let Some(u) = unit::get(c, aid) {
            crate::clif::displaymessage(c, &u, crate::data::msg_txt(680)); // You have been recovered!
        }
    }
}

/// recovery <type>{,<id>{,<revive flag>{,"<map>"}}}: 0 a player, 1 a party, 2 a guild, 3 a map, 4 everyone. Pushes 1
/// when done (nothing when the player / party is not found, as rAthena).
fn recovery(rt: &Rt, st: &mut State) -> i32 {
    use crate::pc::pc as _;
    use spacetimedb::Table;
    let c = rt.c.unwrap();
    let kind = getnum(rt, st, 2);
    let mut revive = if st.hasdata(4) { getnum(rt, st, 4) } else { 1 };
    let mapname2mapid = |n: &[u8]| crate::data::maps().id_of(&lat(n)).map(|m| m as i32).unwrap_or(-1);
    match kind {
        0 => {
            let Some(aid) = charid2sd(rt, st, 3, "buildin_recovery") else { return SCRIPT_CMD_SUCCESS };
            recovery_sub(c, aid, revive);
        }
        1 => {
            // When no party given, we use invoker party
            let mut map_idx = 0;
            if st.hasdata(5) {
                map_idx = mapname2mapid(&getstr(rt, st, 5));
                if map_idx < 1 {
                    log::debug!("recovery: bad map name given ({})", lat(&getstr(rt, st, 5)));
                    return SCRIPT_CMD_FAILURE;
                }
            }
            let p_id = if st.hasdata(3) {
                getnum(rt, st, 3) as u32
            } else {
                rid2sd(rt, st, "buildin_recovery").and_then(|aid| crate::pc::character_of(c, aid)).map(|ch| ch.party_id).unwrap_or(0)
            };
            let Some(p) = crate::party::party_search(c, p_id) else { return SCRIPT_CMD_SUCCESS };
            for m in &p.data {
                if m.sd == 0 {
                    continue;
                }
                let Some(u) = unit::get(c, m.sd) else { continue };
                let pid = crate::pc::character_of(c, m.sd).map(|ch| ch.party_id).unwrap_or(0);
                if pid != p_id || (map_idx != 0 && u.map as i32 != map_idx) {
                    continue;
                }
                recovery_sub(c, m.sd, revive);
            }
        }
        2 => {
            // When no guild given, we use invoker guild
            let mut map_idx = 0;
            if st.hasdata(5) {
                map_idx = mapname2mapid(&getstr(rt, st, 5));
                if map_idx < 1 {
                    log::debug!("recovery: bad map name given ({})", lat(&getstr(rt, st, 5)));
                    return SCRIPT_CMD_FAILURE;
                }
            }
            let g_id = if st.hasdata(3) {
                getnum(rt, st, 3) as i32
            } else {
                rid2sd(rt, st, "buildin_recovery").and_then(|aid| crate::pc::character_of(c, aid)).map(|ch| ch.guild_id as i32).unwrap_or(0)
            };
            let Some(g) = crate::guild::guild_search(c, g_id) else { return SCRIPT_CMD_SUCCESS };
            for m in g.guild.member.iter().take(crate::int_guild::MAX_GUILD) {
                if m.sd == 0 {
                    continue;
                }
                let Some(u) = unit::get(c, m.sd) else { continue };
                let gid = crate::pc::character_of(c, m.sd).map(|ch| ch.guild_id as i32).unwrap_or(0);
                if gid != g_id || (map_idx != 0 && u.map as i32 != map_idx) {
                    continue;
                }
                recovery_sub(c, m.sd, revive);
            }
        }
        3 | 4 => {
            let mut map_idx = 0;
            if kind == 3 {
                if st.hasdata(3) {
                    map_idx = mapname2mapid(&getstr(rt, st, 3));
                } else if let Some(aid) = rid2sd(rt, st, "buildin_recovery") {
                    map_idx = unit::get(c, aid).map(|u| u.map as i32).unwrap_or(0);
                }
                if map_idx < 1 {
                    return SCRIPT_CMD_FAILURE; // No sd and no map given - return
                }
            }
            if st.hasdata(3) && !isstring(rt, st, 3) {
                revive = getnum(rt, st, 3); // recovery 4,<revive_flag>;
            }
            // mapit_getallusers
            let mut ids: Vec<u32> = c.db().pc().iter().map(|p| p.account_id).collect();
            ids.sort_by_key(|&id| crate::party::idb_order(id));
            for aid in ids {
                if kind == 3 && unit::get(c, aid).map(|u| u.map as i32 != map_idx).unwrap_or(true) {
                    continue;
                }
                recovery_sub(c, aid, revive);
            }
        }
        _ => {
            log::warn!("script: buildin_recovery: Invalid type {kind}");
            st.pushint(-1);
            return SCRIPT_CMD_FAILURE;
        }
    }
    st.pushint(1); // Successfully executed without errors
    SCRIPT_CMD_SUCCESS
}

/// areapercentheal "<map>",<x0>,<y0>,<x1>,<y1>,<hp%>,<sp%>: pc_percentheal for the players of the area.
fn areapercentheal(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let mapname = getstr(rt, st, 2);
    let (x0, y0, x1, y1) = (getnum(rt, st, 3), getnum(rt, st, 4), getnum(rt, st, 5), getnum(rt, st, 6));
    let hp = getnum(rt, st, 7);
    let sp = getnum(rt, st, 8);
    let Some(m) = crate::data::maps().id_of(&lat(&mapname)) else { return SCRIPT_CMD_FAILURE };
    for u in crate::map::units_in_area(c, m, x0 as i16, y0 as i16, x1 as i16, y1 as i16, crate::map::BL_PC) {
        crate::status::hp::pc_percentheal(c, u.id, hp, sp);
    }
    SCRIPT_CMD_SUCCESS
}

/// getareadropitem("<map>",<x0>,<y0>,<x1>,<y1>,<item>): the amount of that item lying in the area (-1: no map).
fn getareadropitem(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let mapname = getstr(rt, st, 2);
    let (x0, y0, x1, y1) = (getnum(rt, st, 3) as i16, getnum(rt, st, 4) as i16, getnum(rt, st, 5) as i16, getnum(rt, st, 6) as i16);
    let nameid = if isstring(rt, st, 7) {
        let name = lat(&getstr(rt, st, 7));
        match crate::db::items::itemdb_searchname(&name) {
            Some(d) => d.nameid,
            None => {
                log::error!("buildin_getareadropitem: Unknown item {name}");
                return SCRIPT_CMD_FAILURE;
            }
        }
    } else {
        let id = getnum(rt, st, 7) as u32;
        if crate::db::items::itemdb_exists(id).is_none() {
            log::error!("buildin_getareadropitem: Unknown item id {id}");
            return SCRIPT_CMD_FAILURE;
        }
        id
    };
    let Some(m) = crate::data::maps().id_of(&lat(&mapname)) else {
        st.pushint(-1);
        return SCRIPT_CMD_SUCCESS;
    };
    let mut amount: u16 = 0;
    for u in crate::map::units_in_area(c, m, x0, y0, x1, y1, crate::map::BL_ITEM) {
        if let Some(f) = c.db().flooritem().id().find(u.id) {
            if f.item.nameid == nameid {
                amount = amount.wrapping_add(f.item.amount as u16);
            }
        }
    }
    st.pushint(amount as i64);
    SCRIPT_CMD_SUCCESS
}

/// getscrate(<type>,<rate>{,<unit id>}): status_get_sc_def's answer for a 100% / 10 s status, or `rate` when the unit
/// is not found.
fn getscrate(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let kind = getnum(rt, st, 2);
    let mut rate = getnum(rt, st, 3) as i64;
    let id = if st.hasdata(4) { getnum(rt, st, 4) as u32 } else { st.rid };
    if let Some(bl) = unit::get(c, id) {
        rate = crate::status::sc::status_get_sc_def(c, None, &bl, kind, 10000, 10000, 0);
    }
    st.pushint(rate);
    SCRIPT_CMD_SUCCESS
}

/// vip_time <minutes>{,"<player>"}: chrif_req_login_operation(CHRIF_OP_LOGIN_VIP) — only with VIP_ENABLE, which
/// rAthena's config/core.hpp leaves off: nothing happens.
fn vip_time(_rt: &Rt, _st: &mut State) -> i32 {
    SCRIPT_CMD_SUCCESS
}
