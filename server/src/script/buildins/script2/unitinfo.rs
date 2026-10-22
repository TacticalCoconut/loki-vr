//! Unit information and NPC unit commands: unitexists, getunittype, getunitname, setunitname, setunittitle,
//! getunittitle, rid2name, unitkill, unitstopwalk, sit, stand, getunits / getmapunits / getareaunits, npcwalkto,
//! getunitdata, setunitdata.

use super::unitctl::{has_ud, map_id2bl, script_rid2bl, Bl};
use crate::npc::npc;
use crate::pc_misc;
use crate::script::buildins::*;
use crate::script::vm::*;
use crate::unit;
use crate::world::Ctx;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "unitexists" => unitexists,
        "getunittype" => getunittype,
        "getunitname" => getunitname,
        "setunitname" => setunitname,
        "setunittitle" => setunittitle,
        "getunittitle" => getunittitle,
        "rid2name" => rid2name,
        "unitkill" => unitkill,
        "unitstopwalk" => unitstopwalk,
        "sit" => sit,
        "stand" => stand,
        "getunits" => getunits,
        "npcwalkto" => npcwalkto,
        "getunitdata" => getunitdata,
        "setunitdata" => setunitdata,
        _ => return None,
    })
}

/// A script constant (UNPC_*, UMOB_*, …) by name.
fn k(name: &str) -> i32 {
    crate::data::constant(name).map(|v| v as i32).unwrap_or(i32::MIN)
}

/// NAME_LENGTH - 1 bytes (safestrncpy into a NAME_LENGTH buffer).
fn name24(s: &[u8]) -> String {
    lat(&s.iter().copied().take(crate::party::NAME_LENGTH - 1).collect::<Vec<u8>>())
}

/// status.cpp status_get_name: a player's name (its fakename when set), an NPC's display name, the others' names
/// (their unit rows).
pub fn status_get_name(c: &Ctx, bl: &Bl) -> Vec<u8> {
    use crate::map::*;
    match bl {
        Bl::FloatingNpc(n) => crate::net::wire::latin1(&n.name),
        Bl::Unit(u) if u.kind == BL_NPC => crate::net::wire::latin1(&c.db().npc().id().find(u.id).map(|n| n.name).unwrap_or_else(|| u.name.clone())),
        Bl::Unit(u) if u.kind == BL_PC => {
            let f = crate::atcmd_pc::get(c, u.id).fakename;
            crate::net::wire::latin1(if !f.is_empty() { &f } else { &u.name })
        }
        Bl::Unit(u) => match u.kind {
            BL_MOB | BL_PET | BL_HOM | BL_MER | BL_ELEM => crate::net::wire::latin1(&u.name),
            _ => b"Unknown".to_vec(),
        },
    }
}

/// unitexists <unit id>.
fn unitexists(rt: &Rt, st: &mut State) -> i32 {
    let id = getnum(rt, st, 2) as u32;
    let there = rt.c.map(|c| map_id2bl(c, id).is_some()).unwrap_or(false);
    st.pushint(there as i64);
    SCRIPT_CMD_SUCCESS
}

/// getunittype <unit id>: the BL_* type of players, NPCs, pets, monsters, homunculi, mercenaries, elementals; -1 else.
fn getunittype(rt: &Rt, st: &mut State) -> i32 {
    let Some(bl) = script_rid2bl(rt, st, 2, "buildin_getunittype") else {
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    let value = if has_ud(bl.kind()) { bl.kind() as i64 } else { -1 };
    st.pushint(value);
    SCRIPT_CMD_SUCCESS
}

/// getunitname <unit id>.
fn getunitname(rt: &Rt, st: &mut State) -> i32 {
    let Some(bl) = script_rid2bl(rt, st, 2, "buildin_getunitname") else {
        st.pushstr(b"Unknown".to_vec());
        return SCRIPT_CMD_FAILURE;
    };
    let name = status_get_name(rt.c.unwrap(), &bl);
    st.pushstr(name);
    SCRIPT_CMD_SUCCESS
}

/// setunitname <unit id>,"<name>": monsters, homunculi and pets only (NPCs use setnpcdisplay).
fn setunitname(rt: &Rt, st: &mut State) -> i32 {
    use crate::map::*;
    let Some(bl) = script_rid2bl(rt, st, 2, "buildin_setunitname") else {
        st.pushstr(b"Unknown".to_vec());
        return SCRIPT_CMD_FAILURE;
    };
    let c = rt.c.unwrap();
    let Bl::Unit(mut u) = bl else {
        log::warn!("buildin_setunitname: Invalid object type!");
        return SCRIPT_CMD_FAILURE;
    };
    match u.kind {
        BL_MOB => {
            // safestrncpy(md->name, name, NAME_LENGTH) (mob::put copies it to the unit row clif_name reads)
            let name = getstr(rt, st, 3);
            match crate::mob::get(c, u.id) {
                Some(mut md) => {
                    md.name = name24(&name);
                    crate::mob::put(c, &md);
                }
                None => {
                    log::warn!("buildin_setunitname: Error in finding object BL_MOB!");
                    return SCRIPT_CMD_FAILURE;
                }
            }
            u = unit::get(c, u.id).unwrap_or(u);
        }
        BL_PET => {
            // safestrncpy(pd->pet.name, name, NAME_LENGTH) (pet/data.rs put copies it to the unit row)
            let name = getstr(rt, st, 3);
            match crate::pet::data::get(c, u.id) {
                Some(mut pd) => {
                    pd.pet.name = name24(&name);
                    crate::pet::data::put(c, &pd);
                }
                None => {
                    log::warn!("buildin_setunitname: Error in finding object BL_PET!");
                    return SCRIPT_CMD_FAILURE;
                }
            }
            u = unit::get(c, u.id).unwrap_or(u);
        }
        BL_HOM => {
            // safestrncpy(hd->homunculus.name, name, NAME_LENGTH) (script.cpp:19917-19923, homunculus/buildins.rs)
            let name = getstr(rt, st, 3);
            if !crate::homunculus::buildins::setunitname_hom(c, u.id, &name) {
                return SCRIPT_CMD_FAILURE;
            }
            u = unit::get(c, u.id).unwrap_or(u);
        }
        _ => {
            log::warn!("buildin_setunitname: Invalid object type!");
            return SCRIPT_CMD_FAILURE;
        }
    }
    crate::clif::name_area(c, &u); // Send update to client.
    SCRIPT_CMD_SUCCESS
}

/// setunittitle <GID>,"<title>": ud->title (NAME_LENGTH) and clif_name_area.
fn setunittitle(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let gid = getnum(rt, st, 2);
    let Some(bl) = map_id2bl(c, gid as u32) else {
        log::warn!("buildin_setunittitle: Unable to find object with given game ID {gid}!");
        return SCRIPT_CMD_FAILURE;
    };
    if !has_ud(bl.kind()) {
        log::warn!("buildin_setunittitle: Unable to find unit_data for given game ID {gid}!");
        return SCRIPT_CMD_FAILURE;
    }
    let title = getstr(rt, st, 3);
    match bl {
        Bl::Unit(mut u) => {
            u.ud.title = name24(&title);
            unit::save(c, &u);
            crate::clif::name_area(c, &u);
        }
        // a floating NPC's unit_data has no row here (and clif_name_area reaches nobody for it)
        Bl::FloatingNpc(_) => {}
    }
    SCRIPT_CMD_SUCCESS
}

/// getunittitle <GID>.
fn getunittitle(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let gid = getnum(rt, st, 2);
    let Some(bl) = map_id2bl(c, gid as u32) else {
        log::warn!("buildin_getunittitle: Unable to find object with given game ID {gid}!");
        return SCRIPT_CMD_FAILURE;
    };
    if !has_ud(bl.kind()) {
        log::warn!("buildin_getunittitle: Unable to find unit_data for given game ID {gid}!");
        return SCRIPT_CMD_FAILURE;
    }
    let title = bl.unit().map(|u| crate::net::wire::latin1(&u.ud.title)).unwrap_or_default();
    st.pushstr(title);
    SCRIPT_CMD_SUCCESS
}

/// rid2name <unit id>: a monster's / player's / pet's / homunculus' name, an NPC's unique name (exname), a mercenary's
/// database name.
fn rid2name(rt: &Rt, st: &mut State) -> i32 {
    use crate::map::*;
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let rid = getnum(rt, st, 2);
    match map_id2bl(c, rid as u32) {
        Some(bl) => {
            let v = match &bl {
                Bl::FloatingNpc(n) => Some(crate::net::wire::latin1(&n.exname)),
                Bl::Unit(u) => match u.kind {
                    BL_NPC => Some(crate::net::wire::latin1(&c.db().npc().id().find(u.id).map(|n| n.exname).unwrap_or_default())),
                    BL_MOB | BL_PC | BL_PET | BL_HOM | BL_MER => Some(crate::net::wire::latin1(&u.name)),
                    _ => None,
                },
            };
            match v {
                Some(v) => st.pushstr(v),
                None => {
                    log::error!("buildin_rid2name: BL type unknown.");
                    st.pushstr(Vec::new());
                }
            }
        }
        None => {
            log::error!("buildin_rid2name: invalid RID");
            st.pushstr(b"(null)".to_vec());
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// unitkill <unit id>: status_kill (each unit type's damage path; monsters with the mob agent's mob_damage).
fn unitkill(rt: &Rt, st: &mut State) -> i32 {
    if let Some(bl) = script_rid2bl(rt, st, 2, "buildin_unitkill") {
        crate::status::hp::status_kill(rt.c.unwrap(), bl.id());
    }
    SCRIPT_CMD_SUCCESS
}

/// unitstopwalk <unit id>{,<flag>}.
fn unitstopwalk(rt: &Rt, st: &mut State) -> i32 {
    let mut flag = unit::USW_NONE;
    if st.hasdata(3) {
        flag = getnum(rt, st, 3);
    }
    let Some(bl) = script_rid2bl(rt, st, 2, "buildin_unitstopwalk") else { return SCRIPT_CMD_FAILURE };
    let c = rt.c.unwrap();
    let stopped = match bl {
        Bl::Unit(mut u) => {
            u.ud.force_walk = false;
            let r = unit::stop_walking(c, &mut u, flag, 0);
            unit::save(c, &u);
            r
        }
        Bl::FloatingNpc(_) => false, // never walking
    };
    if !stopped && flag != unit::USW_FORCE_STOP {
        log::warn!("buildin_unitstopwalk: Unable to find unit or unit is not walking.");
        return SCRIPT_CMD_FAILURE;
    }
    SCRIPT_CMD_SUCCESS
}

/// sit {"<character name>"}: pc_setsit, skill_sit(sd, 1), clif_sitting.
fn sit(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = nick2sd(rt, st, 2, "buildin_sit") else { return SCRIPT_CMD_FAILURE };
    let c = rt.c.unwrap();
    let Some(mut u) = unit::get(c, aid) else { return SCRIPT_CMD_FAILURE };
    if u.vd.dead_sit != 2 {
        // pc_setsit
        unit::stop_walking(c, &mut u, unit::USW_FIXPOS | unit::USW_MOVE_FULL_CELL, 0);
        unit::stop_attack(c, &mut u);
        u.vd.dead_sit = 2;
        unit::save(c, &u);
        if let Some(mut p) = crate::pc::get(c, aid) {
            p.dead_sit = 2;
            crate::pc::put(c, &p);
        }
        crate::status::sc_ext::skill_sit(c, aid, true);
        if let Some(u) = unit::get(c, aid) {
            crate::clif::sitting(c, &u);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// stand {"<character name>"}: pc_setstand, skill_sit(sd, 0), clif_standing.
fn stand(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = nick2sd(rt, st, 2, "buildin_stand") else { return SCRIPT_CMD_FAILURE };
    let c = rt.c.unwrap();
    let sitting = unit::get(c, aid).map(|u| u.vd.dead_sit == 2).unwrap_or(false);
    if sitting && crate::pc::setstand(c, aid, false) {
        crate::status::sc_ext::skill_sit(c, aid, false);
        if let Some(u) = unit::get(c, aid) {
            crate::clif::standing(c, &u);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// The objects of id_db of the given types: every unit row but warp NPCs, plus the NPCs without a unit row (floating
/// ones, the fake NPC), in DBIterator order (bucket id % 283, then ascending id).
pub fn id_db_units(c: &Ctx, kind: u16) -> Vec<Bl> {
    use spacetimedb::Table;
    let mut all: Vec<Bl> = Vec::new();
    if kind == 0 {
        return all;
    }
    let warps: std::collections::HashSet<u32> = c.db().npc().iter().filter(|n| n.subtype == crate::npc::NPCTYPE_WARP).map(|n| n.id).collect();
    for u in c.db().unit().iter() {
        if u.kind & kind != 0 && !(u.kind == crate::map::BL_NPC && warps.contains(&u.id)) {
            all.push(Bl::Unit(u));
        }
    }
    if kind & crate::map::BL_NPC != 0 {
        for n in c.db().npc().iter() {
            if n.subtype != crate::npc::NPCTYPE_WARP && unit::get(c, n.id).is_none() {
                all.push(Bl::FloatingNpc(n));
            }
        }
    }
    all.sort_by_key(|b| crate::party::idb_order(b.id()));
    all
}
use crate::unit::unit as _;

/// getunits / getmapunits / getareaunits <type>{,"<map>"{,<x1>,<y1>,<x2>,<y2>}}{,<array>}: the objects of id_db of the
/// given types (on the map / in the area), their ids (or names, into a string array); returns the count.
fn getunits(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let command = st.funcname.clone();
    let kind = getnum(rt, st, 2) as u16;
    let mut m: i32 = -1;
    let (mut x0, mut y0, mut x1, mut y1) = (0i16, 0i16, 0i16, 0i16);
    let mut data_at: Option<i32> = None;
    if command == "getmapunits" || command == "getareaunits" {
        let s = getstr(rt, st, 3);
        match crate::data::maps().id_of(&lat(&s)) {
            Some(mm) => m = mm as i32,
            None => {
                st.pushint(-1);
                st.state = END;
                log::warn!("buildin_{command}: Unknown map '{}'.", lat(&s));
                return SCRIPT_CMD_FAILURE;
            }
        }
        if command == "getareaunits" {
            let (a, b, cc, d) = (getnum(rt, st, 4), getnum(rt, st, 5), getnum(rt, st, 6), getnum(rt, st, 7));
            x0 = a.min(cc) as i16;
            y0 = b.min(d) as i16;
            x1 = a.max(cc) as i16;
            y1 = b.max(d) as i16;
            if st.hasdata(8) {
                data_at = Some(8);
            }
        } else if st.hasdata(4) {
            data_at = Some(4);
        }
    } else if st.hasdata(3) {
        data_at = Some(3);
    }
    let mut target: Option<(i32, u32, RegRef, Vec<u8>)> = None;
    let mut sd = None;
    if let Some(i) = data_at {
        let Data::Name(id, idx, rf) = st.data(i).clone() else {
            log::error!("buildin_{command}: not a variable");
            reportdata(&st.data(i).clone());
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        };
        let name = name_of(id);
        if not_server_variable(name.first().copied().unwrap_or(0)) {
            sd = rid2sd(rt, st, &format!("buildin_{command}"));
            if sd.is_none() {
                log::error!("buildin_{command}: Cannot use a player variable '{}' if no player is attached.", lat(&name));
                return SCRIPT_CMD_FAILURE;
            }
        }
        target = Some((id, idx, rf, name));
    }
    let mut size: u32 = 0;
    for bl in id_db_units(c, kind) {
        let (bm, bx, by) = match &bl {
            Bl::Unit(u) => (u.map as i32, u.x, u.y),
            Bl::FloatingNpc(n) => (n.map, n.x, n.y),
        };
        let area_zero = x0 == 0 && y0 == 0 && x1 == 0 && y1 == 0;
        if m == -1 || (m == bm && area_zero) || (bm == m && bx >= x0 && by >= y0 && bx <= x1 && by <= y1) {
            if let Some((id, idx, rf, name)) = &target {
                if is_string_variable(name) {
                    let v = status_get_name(c, &bl);
                    crate::script::reg::set_reg_str(rt, st, sd, *id, idx + size, name, &v, rf);
                } else {
                    crate::script::reg::set_reg_num(rt, st, sd, *id, idx + size, name, bl.id() as i64, rf);
                }
            }
            size += 1;
        }
    }
    st.pushint(size as i64);
    SCRIPT_CMD_SUCCESS
}

/// status_calc_npc(nd, nd->status.hp ? SCO_NONE : SCO_FIRST), which npcwalkto, unitwalk and setunitdata start with.
pub fn npc_status_calc_first_or_none(c: &Ctx, nd_id: u32) {
    let first = pc_misc::npcm(c, nd_id).status.hp == 0;
    crate::unit_npc::status_calc_npc(c, nd_id, if first { crate::status::SCO_FIRST } else { crate::status::SCO_NONE });
}

/// npcwalkto <x>,<y>{,"<NPC name>"}: the NPC walks (status_calc_npc first, then unit_walktoxy).
fn npcwalkto(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let nd = if st.hasdata(4) {
        let n = getstr(rt, st, 4);
        crate::script::npc_run::npc_name2id(c, &lat(&n))
    } else {
        c.db().npc().id().find(st.oid)
    };
    let Some(nd) = nd else {
        if st.hasdata(4) {
            let n = getstr(rt, st, 4);
            log::error!("buildin_npcwalkto: {} is a non-existing NPC.", lat(&n));
        } else {
            log::error!("buildin_npcwalkto: non-existing NPC.");
        }
        return SCRIPT_CMD_FAILURE;
    };
    if nd.map < 0 {
        log::error!("buildin_npcwalkto: NPC is not on a map.");
        return SCRIPT_CMD_FAILURE;
    }
    let Some(md) = crate::data::maps().get(nd.map as u16) else { return SCRIPT_CMD_FAILURE };
    let x = getnum(rt, st, 2);
    let y = getnum(rt, st, 3);
    if x < 0 || x >= md.xs as i32 || y < 0 || y >= md.ys as i32 {
        log::warn!("buildin_npcwalkto: coordinates {x}/{y} are out of bounds in map {}({}x{}).", md.name, md.xs, md.ys);
        return SCRIPT_CMD_FAILURE;
    }
    npc_status_calc_first_or_none(c, nd.id);
    unit::walktoxy(c, nd.id, x as i16, y as i16, 0);
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- getunitdata / setunitdata

/// The NPC's view data: its unit row's, or (a floating NPC) the class' view data.
fn npc_vd(bl: &Bl) -> crate::unit::ViewData {
    match bl {
        Bl::Unit(u) => u.vd.clone(),
        Bl::FloatingNpc(n) => crate::npc::npc_viewdata(n.class),
    }
}

/// getunitdata <unit id>,<array>: the unit's live data into array[U*_*]. NPCs here; monsters (mob agent), pets
/// (pet/buildins.rs), homunculi (homunculus/buildins.rs), mercenaries / elementals (companion/buildins.rs); players
/// are no supported type.
fn getunitdata(rt: &Rt, st: &mut State) -> i32 {
    use crate::map::*;
    let Some(bl) = script_rid2bl(rt, st, 2, "buildin_getunitdata") else {
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    let c = rt.c.unwrap();
    let Data::Name(id, _idx, rf) = st.data(3).clone() else {
        log::warn!("buildin_getunitdata: Error in argument! Please give a variable to store values in.");
        return SCRIPT_CMD_FAILURE;
    };
    let name = name_of(id);
    if not_server_variable(name.first().copied().unwrap_or(0)) && rid2sd(rt, st, "buildin_getunitdata").is_none() {
        log::error!("buildin_getunitdata: Cannot use a player variable '{}' if no player is attached.", lat(&name));
        return SCRIPT_CMD_FAILURE;
    }
    // (rAthena sets sd = nullptr here in every case: the values are written without a player)
    let sd: Option<u32> = None;
    match bl.kind() {
        BL_NPC => {
            let nd = c.db().npc().id().find(bl.id());
            let m = pc_misc::npcm(c, bl.id());
            let s = m.status.clone();
            let vd = npc_vd(&bl);
            let ud = bl.unit().map(|u| u.ud.clone()).unwrap_or_default();
            let (nm, nx, ny) = nd.as_ref().map(|n| (n.map, n.x, n.y)).unwrap_or((-1, 0, 0));
            let vals: Vec<(&str, i64)> = vec![
                ("UNPC_LEVEL", m.level as i64),
                ("UNPC_HP", s.hp as i64),
                ("UNPC_MAXHP", s.max_hp as i64),
                ("UNPC_MAPID", nm as i64),
                ("UNPC_X", nx as i64),
                ("UNPC_Y", ny as i64),
                ("UNPC_LOOKDIR", ud.dir as i64),
                ("UNPC_STR", s.str as u16 as i64),
                ("UNPC_AGI", s.agi as u16 as i64),
                ("UNPC_VIT", s.vit as u16 as i64),
                ("UNPC_INT", s.int_ as u16 as i64),
                ("UNPC_DEX", s.dex as u16 as i64),
                ("UNPC_LUK", s.luk as u16 as i64),
                ("UNPC_PLUSALLSTAT", m.stat_point as i64),
                ("UNPC_DMGIMMUNE", ud.immune_attack as i64),
                ("UNPC_ATKRANGE", s.rhw.range as i64),
                ("UNPC_ATKMIN", s.rhw.atk as i64),
                ("UNPC_ATKMAX", s.rhw.atk2 as i64),
                ("UNPC_MATKMIN", s.matk_min as i64),
                ("UNPC_MATKMAX", s.matk_max as i64),
                ("UNPC_DEF", s.def as i64),
                ("UNPC_MDEF", s.mdef as i64),
                ("UNPC_HIT", s.hit as i64),
                ("UNPC_FLEE", s.flee as i64),
                ("UNPC_PDODGE", s.flee2 as i64),
                ("UNPC_CRIT", s.cri as i64),
                ("UNPC_RACE", s.race as i64),
                ("UNPC_ELETYPE", s.def_ele as i64),
                ("UNPC_ELELEVEL", s.ele_lv as i64),
                ("UNPC_AMOTION", s.amotion as i16 as i64),
                ("UNPC_ADELAY", s.adelay as i16 as i64),
                ("UNPC_DMOTION", s.dmotion as i16 as i64),
                ("UNPC_SEX", vd.sex as i8 as i64),
                ("UNPC_CLASS", vd.class as i64),
                ("UNPC_HAIRSTYLE", vd.hair as i64),
                ("UNPC_HAIRCOLOR", vd.hair_color as i64),
                ("UNPC_HEADBOTTOM", vd.head_bottom as i64),
                ("UNPC_HEADMIDDLE", vd.head_mid as i64),
                ("UNPC_HEADTOP", vd.head_top as i64),
                ("UNPC_CLOTHCOLOR", vd.clothes_color as i64),
                ("UNPC_SHIELD", vd.shield as i64),
                ("UNPC_WEAPON", vd.weapon as i64),
                ("UNPC_ROBE", vd.robe as i64),
                ("UNPC_BODY2", vd.body2 as i64),
                ("UNPC_DEADSIT", vd.dead_sit as i8 as i64),
                ("UNPC_GROUP_ID", ud.group_id as i64),
            ];
            for (key, v) in vals {
                crate::script::reg::set_reg_num(rt, st, sd, id, k(key) as u32, &name, v, &rf);
            }
        }
        BL_MOB => {
            if let Some(u) = bl.unit() {
                super::unitmob::getunitdata_mob(rt, st, u, sd, id, &name, &rf);
            }
        }
        BL_PET => {
            if let Some(u) = bl.unit() {
                crate::pet::buildins::getunitdata_pet(rt, st, u, sd, id, &name, &rf);
            }
        }
        BL_HOM => {
            // script.cpp:19157-19200 (homunculus/buildins.rs)
            if let Some(u) = bl.unit() {
                crate::homunculus::buildins::getunitdata_hom(rt, st, u, sd, id, &name, &rf);
            }
        }
        BL_MER | BL_ELEM => {
            // mercenaries, elementals (companion/buildins.rs)
            if let Some(u) = bl.unit() {
                crate::companion::buildins::getunitdata_comp(rt, st, u, sd, id, &name, &rf);
            }
        }
        _ => {
            log::warn!("buildin_getunitdata: Unknown object type!");
            return SCRIPT_CMD_FAILURE;
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// setunitdata <unit id>,<type>,<value>: changes a unit's live data (NPCs here; see getunitdata for the others).
fn setunitdata(rt: &Rt, st: &mut State) -> i32 {
    use crate::map::*;
    use crate::pc_equip::*;
    let Some(bl) = script_rid2bl(rt, st, 2, "buildin_setunitdata") else {
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    let c = rt.c.unwrap();
    let t = getnum(rt, st, 3);
    let mut value: i32 = 0;
    let mapids = ["UMOB_MAPID", "UHOM_MAPID", "UPET_MAPID", "UMER_MAPID", "UELE_MAPID", "UNPC_MAPID"];
    let mut mapname: Option<Vec<u8>> = None;
    if mapids.iter().any(|n| k(n) == t) && isstring(rt, st, 4) {
        mapname = Some(getstr(rt, st, 4)); // map_mapname2mapid(mapname) for unit_warp
    } else {
        value = getnum(rt, st, 4);
    }
    match bl.kind() {
        BL_NPC => {}
        BL_MOB => return super::unitmob::setunitdata_mob(c, bl.id(), t, value, mapname.as_deref()),
        BL_PET => return crate::pet::buildins::setunitdata_pet(c, bl.id(), t, value, mapname.as_deref()),
        // script.cpp:19539-19602 (homunculus/buildins.rs)
        BL_HOM => return crate::homunculus::buildins::setunitdata_hom(c, bl.id(), t, value, mapname.as_deref()),
        BL_MER | BL_ELEM => return crate::companion::buildins::setunitdata_comp(c, bl.id(), t, value, mapname.as_deref()),
        _ => {
            log::warn!("buildin_setunitdata: Unknown object type!");
            return SCRIPT_CMD_FAILURE;
        }
    }
    let nd_id = bl.id();
    npc_status_calc_first_or_none(c, nd_id);
    let mut m = pc_misc::npcm(c, nd_id);
    let (nx, ny) = c.db().npc().id().find(nd_id).map(|n| (n.x, n.y)).unwrap_or((0, 0));
    let set_param = |m: &mut pc_misc::NpcMisc, i: usize| {
        if m.params.len() < 6 {
            m.params.resize(6, 0);
        }
        m.params[i] = value as u16;
        let lv = m.level as i32;
        crate::unit_npc::status_calc_misc(&mut m.status, lv);
    };
    let looks = [
        ("UNPC_HAIRSTYLE", LOOK_HAIR),
        ("UNPC_HAIRCOLOR", LOOK_HAIR_COLOR),
        ("UNPC_HEADBOTTOM", LOOK_HEAD_BOTTOM),
        ("UNPC_HEADMIDDLE", LOOK_HEAD_MID),
        ("UNPC_HEADTOP", LOOK_HEAD_TOP),
        ("UNPC_CLOTHCOLOR", LOOK_CLOTHES_COLOR),
        ("UNPC_SHIELD", LOOK_SHIELD),
        ("UNPC_WEAPON", LOOK_WEAPON),
        ("UNPC_ROBE", LOOK_ROBE),
        ("UNPC_BODY2", LOOK_BODY2),
    ];
    if t == k("UNPC_LEVEL") {
        m.level = value as u32;
    } else if t == k("UNPC_HP") {
        // nd->status.hp = value; status_set_hp(bl, value, 0): with the status an NPC of a monster class does not
        // have (dummy status) nothing more; otherwise a value above max_hp is zapped down to it
        m.status.hp = value as u32;
        let class = c.db().npc().id().find(nd_id).map(|n| n.class).unwrap_or(0);
        if value as u32 >= 1 && !crate::data::mobdb_checkid(class) && m.status.hp > m.status.max_hp {
            m.status.hp = m.status.max_hp;
        }
    } else if t == k("UNPC_MAXHP") {
        m.status.hp = value as u32;
        m.status.max_hp = value as u32;
    } else if t == k("UNPC_MAPID") {
        pc_misc::put_npcm(c, &m);
        if let Some(n) = &mapname {
            value = crate::data::maps().id_of(&lat(n)).map(|m| m as i32).unwrap_or(-1);
        }
        super::unitmove::unit_warp(c, &bl, value as i16 as i32, 0, 0, unit::CLR_TELEPORT);
        return SCRIPT_CMD_SUCCESS;
    } else if t == k("UNPC_X") {
        pc_misc::put_npcm(c, &m);
        if !unit::walktoxy(c, nd_id, value as i16, ny, 2) {
            // (rAthena passes nd->x as the y coordinate here)
            unit::movepos(c, nd_id, value as i16, nx, 0, false);
        }
        return SCRIPT_CMD_SUCCESS;
    } else if t == k("UNPC_Y") {
        pc_misc::put_npcm(c, &m);
        if !unit::walktoxy(c, nd_id, nx, value as i16, 2) {
            unit::movepos(c, nd_id, nx, value as i16, 0, false);
        }
        return SCRIPT_CMD_SUCCESS;
    } else if t == k("UNPC_LOOKDIR") {
        pc_misc::put_npcm(c, &m);
        if let Some(mut u) = unit::get(c, nd_id) {
            unit::setdir(c, &mut u, value as u8, true);
            unit::save(c, &u);
        }
        return SCRIPT_CMD_SUCCESS;
    } else if t == k("UNPC_STR") {
        set_param(&mut m, 0);
    } else if t == k("UNPC_AGI") {
        set_param(&mut m, 1);
    } else if t == k("UNPC_VIT") {
        set_param(&mut m, 2);
    } else if t == k("UNPC_INT") {
        set_param(&mut m, 3);
    } else if t == k("UNPC_DEX") {
        set_param(&mut m, 4);
    } else if t == k("UNPC_LUK") {
        set_param(&mut m, 5);
    } else if t == k("UNPC_PLUSALLSTAT") {
        m.stat_point = value as u32;
    } else if t == k("UNPC_ATKRANGE") {
        m.status.rhw.range = value as u16;
    } else if t == k("UNPC_ATKMIN") {
        m.status.rhw.atk = value as u16;
    } else if t == k("UNPC_ATKMAX") {
        m.status.rhw.atk2 = value as u16;
    } else if t == k("UNPC_MATKMIN") {
        m.status.matk_min = value as u16;
    } else if t == k("UNPC_MATKMAX") {
        m.status.matk_max = value as u16;
    } else if t == k("UNPC_DEF") {
        m.status.def = value as i16;
    } else if t == k("UNPC_MDEF") {
        m.status.mdef = value as i16;
    } else if t == k("UNPC_HIT") {
        m.status.hit = value as i16;
    } else if t == k("UNPC_FLEE") {
        m.status.flee = value as i16;
    } else if t == k("UNPC_PDODGE") {
        m.status.flee2 = value as i16;
    } else if t == k("UNPC_CRIT") {
        m.status.cri = value as i16;
    } else if t == k("UNPC_RACE") {
        m.status.race = value as u8;
    } else if t == k("UNPC_ELETYPE") {
        m.status.def_ele = value as u8;
    } else if t == k("UNPC_ELELEVEL") {
        m.status.ele_lv = value as u8;
    } else if t == k("UNPC_AMOTION") {
        m.status.amotion = value as i16 as u16;
    } else if t == k("UNPC_ADELAY") {
        m.status.adelay = value as i16 as u16;
    } else if t == k("UNPC_DMOTION") {
        m.status.dmotion = value as i16 as u16;
    } else if t == k("UNPC_SEX") {
        pc_misc::put_npcm(c, &m);
        if let Some(mut u) = unit::get(c, nd_id) {
            u.vd.sex = value as u8;
            unit::save(c, &u);
            crate::unit_npc::unit_refresh(c, &u, false);
        }
        return SCRIPT_CMD_SUCCESS;
    } else if t == k("UNPC_CLASS") {
        pc_misc::put_npcm(c, &m);
        crate::unit_npc::npc_setclass(c, nd_id, value as i16 as i32);
        return SCRIPT_CMD_SUCCESS;
    } else if let Some(look) = looks.iter().find(|(n, _)| k(n) == t).map(|(_, l)| *l) {
        pc_misc::put_npcm(c, &m);
        crate::unit_npc::changelook_npc(c, nd_id, look, value as u16 as i32);
        return SCRIPT_CMD_SUCCESS;
    } else if t == k("UNPC_DEADSIT") {
        pc_misc::put_npcm(c, &m);
        if let Some(mut u) = unit::get(c, nd_id) {
            u.vd.dead_sit = value as u8;
            unit::save(c, &u);
            crate::unit_npc::unit_refresh(c, &u, false);
        }
        return SCRIPT_CMD_SUCCESS;
    } else if t == k("UNPC_GROUP_ID") {
        pc_misc::put_npcm(c, &m);
        if let Some(mut u) = unit::get(c, nd_id) {
            u.ud.group_id = value;
            unit::save(c, &u);
            crate::unit_npc::unit_refresh(c, &u, false);
        }
        return SCRIPT_CMD_SUCCESS;
    } else {
        log::error!("buildin_setunitdata: Unknown data identifier {t} for BL_NPC.");
        return SCRIPT_CMD_FAILURE;
    }
    pc_misc::put_npcm(c, &m);
    SCRIPT_CMD_SUCCESS
}
