//! Map flags, cells, walls and other map-wide commands: setmapflagnosave, getmapflag, setmapflag, removemapflag,
//! pvpon, pvpoff, checkcell, setcell, getfreecell, setwall, delwall, checkwall, cleanmap / cleanarea, addrid, viewpointmap,
//! naviregisterwarp, navihide, isnight, isday, night, day, getareausers.

use crate::map_cell;
use crate::npc::*;
use crate::pc_misc;
use crate::script::buildins::*;
use crate::script::vm::*;
use crate::world::Ctx;
use spacetimedb::Table;
use crate::pc::pc as _;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "setmapflagnosave" => setmapflagnosave,
        "getmapflag" => getmapflag,
        "setmapflag" => setmapflag,
        "removemapflag" => removemapflag,
        "pvpon" => pvpon,
        "pvpoff" => pvpoff,
        "checkcell" => checkcell,
        "setcell" => setcell,
        "getfreecell" => getfreecell,
        "setwall" => setwall,
        "delwall" => delwall,
        "checkwall" => checkwall,
        "cleanmap" => cleanmap,
        "addrid" => addrid,
        "viewpointmap" => viewpointmap,
        "naviregisterwarp" => naviregisterwarp,
        "navihide" => navihide,
        "isnight" => isnight,
        "isday" => isday,
        "night" => night,
        "day" => day,
        "getareausers" => getareausers,
        _ => return None,
    })
}

fn map_id(name: &[u8]) -> Option<u16> {
    crate::data::maps().id_of(&lat(name))
}

// ---------------------------------------------------------------------------------------------- map_setmapflag_sub

/// map.hpp e_mapflag MF_MAX (MF_INVINCIBLE_TIME + 1).
pub const MF_MAX: i32 = MF_INVINCIBLE_TIME as i32 + 1;
/// map.hpp e_skill_damage_type.
const SKILLDMG_MAX: i32 = 4;
const SKILLDMG_CASTER: i32 = 5;

/// map.hpp union u_mapflag_args, as far as the script commands fill it.
#[derive(Clone, Debug)]
pub enum MfArgs {
    None,
    Val(i32),
    /// args.nosave: the return map (None: "SavePoint", mapindex 0) and cell.
    NoSave(Option<u16>, i16, i16),
    /// args.skill_duration: the skill id and the percentage (both uint16).
    SkillDuration(u16, u16),
    /// args.skill_damage and args.flag_val (0: the every-skill adjustment; 1: a single skill, whose entry the caller
    /// adds with map_skill_damage_add).
    SkillDamage(crate::map_skill_damage::SkillDamage, i32),
}

impl MfArgs {
    fn val(&self) -> i32 {
        match self {
            MfArgs::Val(v) => *v,
            _ => 0,
        }
    }
}

/// map.cpp map_setmapflag_sub: sets or clears a flag with its runtime effects. MF_NOSAVE keeps its save point here; the
/// PvP / GvG flags go to the battle agent's port (battle/pvp.rs map_setmapflag: map property, PvP rank timers,
/// unit_stopattack, the conflicting flags); MF_SKILL_DURATION keeps skillunit's map_skill_duration list; MF_SKILL_DAMAGE
/// map_skill_damage.rs's damage_adjust; the others keep npc.rs's storage rules. The pvp_nightmaredrop list is an npc.rs
/// stub.
pub fn map_setmapflag_sub(c: &Ctx, m: u16, flag: i16, status: bool, args: &MfArgs) -> bool {
    match flag {
        MF_NOSAVE => {
            if status {
                let MfArgs::NoSave(sm, sx, sy) = args else { return false };
                let row = MapSavePoint { map: m, save_map: sm.map(|v| v as i32).unwrap_or(-1), x: *sx, y: *sy };
                if c.db().map_save_point().map().find(m).is_some() {
                    c.db().map_save_point().map().update(row);
                } else {
                    c.db().map_save_point().insert(row);
                }
            }
            crate::npc::setmapflag(c, m, flag, status, 0);
        }
        MF_SKILL_DURATION => {
            if !status {
                crate::skillunit::data::map_skill_duration_clear(c, m);
            } else {
                let MfArgs::SkillDuration(skill_id, per) = *args else { return false };
                crate::skillunit::data::map_skill_duration_add(c, m, skill_id, per);
            }
            crate::npc::setmapflag(c, m, flag, status, 0);
        }
        MF_SKILL_DAMAGE => {
            if !status {
                crate::map_skill_damage::clear(c, m);
            } else {
                let MfArgs::SkillDamage(d, flag_val) = args else { return false };
                // Signifies if it's a single skill or global damage adjustment
                if *flag_val == 0 && !crate::map_skill_damage::set_damage_adjust(c, m, d) {
                    return false;
                }
            }
            crate::npc::setmapflag(c, m, flag, status, 0);
        }
        MF_PVP | MF_GVG | MF_GVG_TE => crate::battle::pvp::map_setmapflag(c, m, flag, status, args.val()),
        _ => crate::npc::setmapflag(c, m, flag, status, args.val()),
    }
    true
}

/// map.cpp map_getmapflag_sub.
pub fn map_getmapflag_sub(c: &Ctx, m: u16, flag: i32, args: &MfArgs) -> i32 {
    if !(0..MF_MAX).contains(&flag) {
        let name = crate::data::maps().get(m).map(|d| d.name.clone()).unwrap_or_default();
        log::warn!("map_getmapflag: Invalid mapflag {flag} on map {name}.");
        return -1;
    }
    let flag = flag as i16;
    if flag == MF_SKILL_DAMAGE {
        let v = args.val();
        if (0..SKILLDMG_MAX).contains(&v) {
            return crate::map_skill_damage::damage_adjust(c, m).rate[v as usize];
        }
        if v == SKILLDMG_CASTER {
            return crate::map_skill_damage::damage_adjust(c, m).caster as i32;
        }
    }
    // MF_RESTRICTED: the zone is the flag's stored value; NOLOOT / NOPENALTY / NOEXP: their pairs (npc.rs)
    mapflag_value(c, m, flag)
}

/// setmapflagnosave "<map>","<return map>",<x>,<y>.
fn setmapflagnosave(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let s = getstr(rt, st, 2);
    let s2 = getstr(rt, st, 3);
    let x = getnum(rt, st, 4) as i16;
    let y = getnum(rt, st, 5) as i16;
    let m = map_id(&s);
    let mapindex = map_id(&s2);
    let Some(m) = m else {
        log::warn!("buildin_setmapflagnosave: Invalid map name {}.", lat(&s));
        return SCRIPT_CMD_FAILURE;
    };
    map_setmapflag_sub(c, m, MF_NOSAVE, true, &MfArgs::NoSave(mapindex, x, y));
    SCRIPT_CMD_SUCCESS
}

/// getmapflag("<map>",<flag>{,<type>}).
fn getmapflag(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let s = getstr(rt, st, 2);
    let Some(m) = map_id(&s) else {
        log::warn!("buildin_getmapflag: Invalid map name {}.", lat(&s));
        return SCRIPT_CMD_FAILURE;
    };
    let mf = getnum(rt, st, 3);
    if !(0..=MF_MAX).contains(&mf) {
        log::error!("buildin_getmapflag: Unsupported mapflag '{mf}'.");
        return SCRIPT_CMD_FAILURE;
    }
    let args = if mf == MF_SKILL_DAMAGE as i32 && !st.hasdata(4) {
        MfArgs::Val(SKILLDMG_MAX)
    } else if st.hasdata(4) {
        MfArgs::Val(getnum(rt, st, 4))
    } else {
        MfArgs::Val(0)
    };
    let v = map_getmapflag_sub(c, m, mf, &args);
    st.pushint(v as i64);
    SCRIPT_CMD_SUCCESS
}

/// setmapflag "<map>",<flag>{,<value>{,<type>}}.
fn setmapflag(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let s = getstr(rt, st, 2);
    let Some(m) = map_id(&s) else {
        log::warn!("buildin_setmapflag: Invalid map name {}.", lat(&s));
        return SCRIPT_CMD_FAILURE;
    };
    let mf = getnum(rt, st, 3);
    if !(0..=MF_MAX).contains(&mf) {
        log::error!("buildin_setmapflag: Unsupported mapflag '{mf}'.");
        return SCRIPT_CMD_FAILURE;
    }
    let flag = mf as i16;
    let args = match flag {
        MF_SKILL_DAMAGE => {
            if st.hasdata(4) && st.hasdata(5) {
                // args.skill_damage.rate[type] = value; the caster stays 0, so map_setmapflag_sub refuses ("Skill
                // damage adjustment without casting type") and the flag is not set, as in rAthena. A type outside
                // SKILLDMG_PC..SKILLDMG_OTHER writes outside rate[] there; here it is ignored.
                let (ty, value) = (getnum(rt, st, 5), getnum(rt, st, 4));
                let mut d = crate::map_skill_damage::SkillDamage::default();
                if (0..SKILLDMG_MAX).contains(&ty) {
                    d.rate[ty as usize] = value;
                }
                MfArgs::SkillDamage(d, 0)
            } else {
                log::warn!("buildin_setmapflag: Unable to set skill_damage mapflag as flag data is missing.");
                return SCRIPT_CMD_FAILURE;
            }
        }
        MF_SKILL_DURATION => {
            if st.hasdata(4) && st.hasdata(5) {
                let skill_id = getnum(rt, st, 4);
                if crate::db::skills::skill_get_index(skill_id as u16) == 0 {
                    log::error!("buildin_setmapflag: Invalid skill ID {skill_id} for skill_duration mapflag.");
                    return SCRIPT_CMD_FAILURE;
                }
                MfArgs::SkillDuration(skill_id as u16, getnum(rt, st, 5) as u16)
            } else {
                log::warn!("buildin_setmapflag: Unable to set skill_duration mapflag as flag data is missing.");
                return SCRIPT_CMD_FAILURE;
            }
        }
        // Assume setting "SavePoint"
        MF_NOSAVE => MfArgs::NoSave(None, -1, -1),
        // Assume setting standard drops (the drop list: npc.rs stub, with PvP drops)
        MF_PVP_NIGHTMAREDROP => MfArgs::Val(0),
        _ => {
            if st.hasdata(4) {
                MfArgs::Val(getnum(rt, st, 4))
            } else {
                MfArgs::Val(0)
            }
        }
    };
    if mf == MF_MAX {
        let name = crate::data::maps().get(m).map(|d| d.name.clone()).unwrap_or_default();
        log::warn!("map_setmapflag: Invalid mapflag {mf} on map {name}.");
        return SCRIPT_CMD_SUCCESS;
    }
    map_setmapflag_sub(c, m, flag, true, &args);
    SCRIPT_CMD_SUCCESS
}

/// removemapflag "<map>",<flag>{,<zone>}.
fn removemapflag(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let s = getstr(rt, st, 2);
    let Some(m) = map_id(&s) else {
        log::warn!("buildin_removemapflag: Invalid map name {}.", lat(&s));
        return SCRIPT_CMD_FAILURE;
    };
    let mf = getnum(rt, st, 3);
    if !(0..=MF_MAX).contains(&mf) {
        log::error!("buildin_removemapflag: Unsupported mapflag '{mf}'.");
        return SCRIPT_CMD_FAILURE;
    }
    let v = if st.hasdata(4) { getnum(rt, st, 4) } else { 0 };
    if mf == MF_MAX {
        let name = crate::data::maps().get(m).map(|d| d.name.clone()).unwrap_or_default();
        log::warn!("map_setmapflag: Invalid mapflag {mf} on map {name}.");
        return SCRIPT_CMD_SUCCESS;
    }
    map_setmapflag_sub(c, m, mf as i16, false, &MfArgs::Val(v));
    SCRIPT_CMD_SUCCESS
}

/// Whether pvpon / pvpoff change the flag: map_setmapflag is only called when MF_PVP differs from the wanted state
/// (so an already-PvP map gets no map property packets and no new PvP timers).
pub fn pvp_switch_needed(current: bool, on: bool) -> bool {
    current != on
}

/// BUILDIN_FUNC(pvpon) / BUILDIN_FUNC(pvpoff) (script.cpp:13712-13742): "<map name>" — an unknown map: "Unknown map"
/// and SCRIPT_CMD_FAILURE; else MF_PVP is set / cleared through map_setmapflag(m, MF_PVP, on) (map_setmapflag_sub
/// with no args: the battle agent's port sends the map property, starts or stops every player's PvP timer and stops
/// the attacks, and removes the GvG / battleground flags), only when the flag changes.
fn pvp_switch(rt: &Rt, st: &mut State, on: bool, func: &str) -> i32 {
    let s = getstr(rt, st, 2);
    let Some(m) = map_id(&s) else {
        log::warn!("buildin_{func}: Unknown map '{}'.", lat(&s));
        return SCRIPT_CMD_FAILURE;
    };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    if pvp_switch_needed(mapflag(c, m, MF_PVP), on) {
        map_setmapflag_sub(c, m, MF_PVP, on, &MfArgs::None);
    }
    SCRIPT_CMD_SUCCESS
}

/// pvpon "<map name>";
fn pvpon(rt: &Rt, st: &mut State) -> i32 {
    pvp_switch(rt, st, true, "pvpon")
}

/// pvpoff "<map name>";
fn pvpoff(rt: &Rt, st: &mut State) -> i32 {
    pvp_switch(rt, st, false, "pvpoff")
}

// ---------------------------------------------------------------------------------------------- cells and walls

/// checkcell("<map>",<x>,<y>,<type>): map_getcell with a cell_chk (a uint8 enum).
fn checkcell(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let s = getstr(rt, st, 2);
    let m = map_id(&s);
    let x = getnum(rt, st, 3) as i16;
    let y = getnum(rt, st, 4) as i16;
    let kind = getnum(rt, st, 5) as u8 as i32;
    st.pushint(map_cell::getcell(c, m, x, y, kind) as i64);
    SCRIPT_CMD_SUCCESS
}

/// setcell "<map>",<x1>,<y1>,<x2>,<y2>,<type>,<flag>: map_setcell on every cell of the area (the clients are not
/// told: rAthena sends no packet for it).
fn setcell(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let s = getstr(rt, st, 2);
    let m = map_id(&s);
    let mut x1 = getnum(rt, st, 3) as i16;
    let mut y1 = getnum(rt, st, 4) as i16;
    let mut x2 = getnum(rt, st, 5) as i16;
    let mut y2 = getnum(rt, st, 6) as i16;
    let kind = getnum(rt, st, 7);
    let flag = getnum(rt, st, 8) != 0;
    if x1 > x2 {
        std::mem::swap(&mut x1, &mut x2);
    }
    if y1 > y2 {
        std::mem::swap(&mut y1, &mut y2);
    }
    for y in y1 as i32..=y2 as i32 {
        for x in x1 as i32..=x2 as i32 {
            map_cell::setcell(c, m, x as i16, y as i16, kind, flag);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// map.cpp map_search_freecell (50 tries): the one port is the mob agent's crate::mob::spawn::map_search_freecell.
/// Returns (found, x, y): on failure x / y are the base cell, as rAthena writes them back.
pub fn map_search_freecell(c: &Ctx, src: Option<&crate::unit::Unit>, m: Option<u16>, x: i16, y: i16, rx: i16, ry: i16, flag: i32) -> (bool, i16, i16) {
    let (bx, by) = match src {
        Some(s) if flag & 1 == 0 => (s.x, s.y),
        _ => (x, y),
    };
    let m = m.map(|m| m as i32).unwrap_or(-1);
    match crate::mob::spawn::map_search_freecell(c, src.map(|s| (s.map, s.x, s.y)), m, x, y, rx, ry, flag, 50) {
        Some((cx, cy)) => (true, cx, cy),
        None => (false, bx, by),
    }
}

/// getfreecell "<map>",<rX>,<rY>{,<x>,<y>,<rangeX>,<rangeY>,<flag>}.
fn getfreecell(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let mapn = getstr(rt, st, 2);
    let (dx, dy) = (st.data(3).clone(), st.data(4).clone());
    let bl = crate::unit::get(c, st.rid);
    let Data::Name(idx_id, idx_x, rfx) = dx else {
        log::warn!("script: buildin_getfreecell: rX is not a variable.");
        return SCRIPT_CMD_FAILURE;
    };
    let Data::Name(idy_id, idy_y, rfy) = dy else {
        log::warn!("script: buildin_getfreecell: rY is not a variable.");
        return SCRIPT_CMD_FAILURE;
    };
    let (name_x, name_y) = (name_of(idx_id), name_of(idy_id));
    if is_string_variable(&name_x) {
        log::warn!("script: buildin_getfreecell: rX is a string, must be an INT.");
        return SCRIPT_CMD_FAILURE;
    }
    if is_string_variable(&name_y) {
        log::warn!("script: buildin_getfreecell: rY is a string, must be an INT.");
        return SCRIPT_CMD_FAILURE;
    }
    let mut sd = None;
    let (px, py) = (not_server_variable(name_x[0]), not_server_variable(name_y[0]));
    if px || py {
        sd = rid2sd(rt, st, "buildin_getfreecell");
        if sd.is_none() {
            if px {
                log::error!("buildin_getfreecell: variable '{}' for mapX is not a server variable, but no player is attached!", lat(&name_x));
            } else {
                log::error!("buildin_getfreecell: variable '{}' for mapY is not a server variable, but no player is attached!", lat(&name_y));
            }
            return SCRIPT_CMD_FAILURE;
        }
    }
    let x = if st.hasdata(5) { getnum(rt, st, 5) as i16 } else { 0 };
    let y = if st.hasdata(6) { getnum(rt, st, 6) as i16 } else { 0 };
    let rx = if st.hasdata(7) { getnum(rt, st, 7) as i16 } else { -1 };
    let ry = if st.hasdata(8) { getnum(rt, st, 8) as i16 } else { -1 };
    let flag = if st.hasdata(9) { getnum(rt, st, 9) } else { 1 };
    let m = if bl.is_some() && mapn == b"this" { bl.as_ref().map(|u| u.map) } else { map_id(&mapn) };
    let (_, fx, fy) = map_search_freecell(c, bl.as_ref(), m, x, y, rx, ry, flag);
    crate::script::reg::set_reg_num(rt, st, sd, idx_id, idx_x, &name_x, fx as i64, &rfx);
    crate::script::reg::set_reg_num(rt, st, sd, idy_id, idy_y, &name_y, fy as i64, &rfy);
    SCRIPT_CMD_SUCCESS
}

/// setwall "<map>",<x>,<y>,<size>,<dir>,<shootable>,"<name>".
fn setwall(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let mapname = getstr(rt, st, 2);
    let x = getnum(rt, st, 3);
    let y = getnum(rt, st, 4);
    let size = getnum(rt, st, 5);
    let dir = getnum(rt, st, 6);
    let shootable = getnum(rt, st, 7) != 0;
    let name = getstr(rt, st, 8);
    let Some(m) = map_id(&mapname) else { return SCRIPT_CMD_SUCCESS }; // Invalid Map
    map_cell::iwall_set(c, m, x as i16, y as i16, size, dir as i8, shootable, &lat(&name));
    SCRIPT_CMD_SUCCESS
}

/// delwall "<name>".
fn delwall(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let name = getstr(rt, st, 2);
    if !map_cell::iwall_remove(c, &lat(&name)) {
        log::error!("buildin_delwall: wall \"{}\" does not exist.", lat(&name));
        return SCRIPT_CMD_FAILURE;
    }
    SCRIPT_CMD_SUCCESS
}

/// checkwall("<name>").
fn checkwall(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let name = getstr(rt, st, 2);
    st.pushint(map_cell::iwall_exist(c, &lat(&name)) as i64);
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- other map commands

/// cleanmap "<map>" / cleanarea "<map>",<x1>,<y1>,<x2>,<y2>: the floor items go (map_clearflooritem).
fn cleanmap(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let mapname = getstr(rt, st, 2);
    let m = map_id(&mapname);
    // `if (!m)`: map id 0 fails, an unknown map (-1) goes on and finds nothing
    if m == Some(0) {
        return SCRIPT_CMD_FAILURE;
    }
    let items: Vec<u32> = if st.lastdata() - 2 < 4 {
        match m.and_then(|m| crate::data::maps().get(m)) {
            Some(md) => crate::map::units_in_area(c, md.id, 0, 0, md.xs - 1, md.ys - 1, crate::map::BL_ITEM).iter().map(|u| u.id).collect(),
            None => Vec::new(),
        }
    } else {
        let x0 = getnum(rt, st, 3) as i16;
        let y0 = getnum(rt, st, 4) as i16;
        let x1 = getnum(rt, st, 5) as i16;
        let y1 = getnum(rt, st, 6) as i16;
        if x0 > 0 && y0 > 0 && x1 > 0 && y1 > 0 {
            match m {
                Some(m) => crate::map::units_in_area(c, m, x0, y0, x1, y1, crate::map::BL_ITEM).iter().map(|u| u.id).collect(),
                None => Vec::new(),
            }
        } else {
            log::error!("cleanarea: invalid coordinate defined!");
            return SCRIPT_CMD_FAILURE;
        }
    };
    for id in items {
        crate::flooritem::map_clearflooritem(c, id); // atcommand_cleanfloor_sub
    }
    SCRIPT_CMD_SUCCESS
}

/// addrid <type>{,<flag>{,<parameters>}}: the script continues, from here, for other players (run_script with the
/// same code and position): 0 everyone, 1 the map, 2 a party, 3 a guild, 4 an area, 5 a named map, 6 a battleground,
/// 7 a clan, else one account id.
fn addrid(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let bl = if st.rid < 1 {
        st.state = END;
        crate::unit::get(c, st.oid)
    } else {
        crate::unit::get(c, st.rid)
    };
    let kind = getnum(rt, st, 2);
    let forceflag = if st.hasdata(3) { getnum(rt, st, 3) != 0 } else { false };
    let (code, pos, rid, oid) = (st.code.clone(), st.pos, st.rid, st.oid);
    // mapit_getallusers: pc_db order
    let mut users: Vec<u32> = c.db().pc().iter().map(|p| p.account_id).collect();
    users.sort_by_key(|&a| crate::party::idb_order(a));
    let has_st = |aid: u32| crate::script::npc_run::sdx(c, aid).st != 0;
    let run = |aid: u32| run_script(rt, &code, pos, aid, oid);
    let ch = |aid: u32| crate::pc::character_of(c, aid);
    match kind {
        0 => {
            for aid in users {
                if (!forceflag || !has_st(aid)) && aid != rid {
                    // attached player already runs.
                    run(aid);
                }
            }
        }
        1 => {
            let Some(bm) = bl.as_ref().map(|b| b.map) else { return SCRIPT_CMD_FAILURE };
            for aid in users {
                if (!forceflag || !has_st(aid)) && crate::unit::get(c, aid).map(|u| u.map == bm).unwrap_or(false) && aid != rid {
                    run(aid);
                }
            }
        }
        2 | 3 | 7 => {
            let id = getnum(rt, st, 4);
            if id == 0 {
                st.pushint(0);
                return if kind == 7 { SCRIPT_CMD_FAILURE } else { SCRIPT_CMD_SUCCESS };
            }
            for aid in users {
                let v = ch(aid).map(|ch| match kind {
                    2 => ch.party_id,
                    3 => ch.guild_id,
                    _ => ch.clan_id,
                });
                if (!forceflag || !has_st(aid)) && aid != rid && v == Some(id as u32) {
                    run(aid);
                }
            }
        }
        4 => {
            let Some(bm) = bl.as_ref().map(|b| b.map) else { return SCRIPT_CMD_FAILURE };
            let (x0, y0, x1, y1) = (getnum(rt, st, 4) as i16, getnum(rt, st, 5) as i16, getnum(rt, st, 6) as i16, getnum(rt, st, 7) as i16);
            for u in crate::map::units_in_area(c, bm, x0, y0, x1, y1, crate::map::BL_PC) {
                // buildin_addrid_sub
                if (!forceflag || !has_st(u.id)) && u.id != rid {
                    run(u.id);
                }
            }
        }
        5 => {
            let name = getstr(rt, st, 4);
            let Some(m) = map_id(&name) else {
                st.pushint(0);
                return SCRIPT_CMD_FAILURE;
            };
            // map_foreachinmap(buildin_addrid_sub, m, BL_PC, st, script_getnum(st, 3)): the flag is argument 3 read
            // again (0 when it is missing)
            let ff = if st.hasdata(3) { getnum(rt, st, 3) != 0 } else { false };
            for u in crate::map::players_on_map(c, m) {
                if (!ff || !has_st(u.id)) && u.id != rid {
                    run(u.id);
                }
            }
        }
        6 => {
            if getnum(rt, st, 4) == 0 {
                st.pushint(0);
                return SCRIPT_CMD_FAILURE;
            }
            let id = getnum(rt, st, 4);
            for aid in users {
                // attached player already runs.
                if (!forceflag || !has_st(aid)) && aid != rid && crate::battleground::bg_id_of(c, aid) == id {
                    run(aid);
                }
            }
        }
        _ => {
            if crate::pc::get(c, kind as u32).is_none() {
                // Player not found.
                st.pushint(0);
                return SCRIPT_CMD_SUCCESS;
            }
            if !forceflag || !has_st(kind as u32) {
                run(kind as u32);
                st.pushint(1);
            }
            return SCRIPT_CMD_SUCCESS;
        }
    }
    let _ = rid;
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}

/// viewpointmap "<map>",<type>,<x>,<y>,<id>,<color>: clif_viewpoint for every player of the map.
fn viewpointmap(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let map = getstr(rt, st, 2);
    let Some(m) = map_id(&map) else {
        log::error!("buildin_viewpointmap: Unknown map name {}.", lat(&map));
        return SCRIPT_CMD_FAILURE;
    };
    let (kind, x, y, id, color) = (getnum(rt, st, 3), getnum(rt, st, 4), getnum(rt, st, 5), getnum(rt, st, 6), getnum(rt, st, 7));
    for u in crate::map::players_on_map(c, m) {
        crate::clif::viewpoint(c, &u, st.oid, kind, x as u16, y as u16, id, color as u32);
    }
    SCRIPT_CMD_SUCCESS
}

/// naviregisterwarp / navihide: only with MAP_GENERATOR (the navigation data generator), which this build is not.
fn naviregisterwarp(_rt: &Rt, _st: &mut State) -> i32 {
    SCRIPT_CMD_SUCCESS
}
fn navihide(_rt: &Rt, _st: &mut State) -> i32 {
    SCRIPT_CMD_SUCCESS
}

/// isnight.
fn isnight(rt: &Rt, st: &mut State) -> i32 {
    let v = rt.c.map(pc_misc::night_flag).unwrap_or(false);
    st.pushint(v as i64);
    SCRIPT_CMD_SUCCESS
}

/// isday.
fn isday(rt: &Rt, st: &mut State) -> i32 {
    let v = rt.c.map(pc_misc::night_flag).unwrap_or(false);
    st.pushint(!v as i64);
    SCRIPT_CMD_SUCCESS
}

/// night: map_night_timer(night_timer_tid, 0, 0, 1).
fn night(rt: &Rt, _st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    if !pc_misc::night_flag(c) {
        pc_misc::map_night_timer(c, 1);
    }
    SCRIPT_CMD_SUCCESS
}

/// day: map_day_timer(day_timer_tid, 0, 0, 1).
fn day(rt: &Rt, _st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    if pc_misc::night_flag(c) {
        pc_misc::map_day_timer(c, 1);
    }
    SCRIPT_CMD_SUCCESS
}

/// getareausers("<map>",<x1>,<y1>,<x2>,<y2>): the players in the area (-1: unknown map).
fn getareausers(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let s = getstr(rt, st, 2);
    let x0 = getnum(rt, st, 3) as i16;
    let y0 = getnum(rt, st, 4) as i16;
    let x1 = getnum(rt, st, 5) as i16;
    let y1 = getnum(rt, st, 6) as i16;
    let Some(m) = map_id(&s) else {
        st.pushint(-1);
        return SCRIPT_CMD_SUCCESS;
    };
    let n = crate::map::units_in_area(c, m, x0, y0, x1, y1, crate::map::BL_PC).len();
    st.pushint(n as i64);
    SCRIPT_CMD_SUCCESS
}
