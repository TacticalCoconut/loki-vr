//! Script commands of the monster system (script.cpp): monster, areamonster, killmonster, killmonsterall, mobcount,
//! strmobinfo, getmonsterinfo, getmobdrops, getrandmobid, addmonsterdrop, delmonsterdrop, mob_setidleevent, summon,
//! clone. (bg_monster / bg_monster_set_team are battlegrounds', areamobuseskill a monster skill: phase 4.)

use super::*;
use crate::db::mobs::{self, MobDrop};
use crate::mob::{self, rtdb, spawn};
use crate::script::vm::*;
use crate::unit::{self, Unit};
use crate::world::Ctx;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "monster" => monster,
        "areamonster" => areamonster,
        "killmonster" => killmonster,
        "killmonsterall" => killmonsterall,
        "mobcount" => mobcount,
        "strmobinfo" => strmobinfo,
        "getmonsterinfo" => getmonsterinfo,
        "getmobdrops" => getmobdrops,
        "getrandmobid" => getrandmobid,
        "addmonsterdrop" => addmonsterdrop,
        "delmonsterdrop" => delmonsterdrop,
        "mob_setidleevent" => mob_setidleevent,
        "summon" => summon,
        "clone" => clone,
        _ => return None,
    })
}

/// map.hpp enum mob_ai AI_MAX.
const AI_MAX: i32 = 11;
/// mob.hpp MAX_MOB_DROP.
const MAX_MOB_DROP: usize = 10;

/// script.cpp check_event: the old "Label" form (without ::On) is deprecated.
fn check_event(rt: &Rt, st: &State, evt: &[u8]) {
    if !evt.is_empty() && !evt.to_ascii_lowercase().windows(4).any(|w| w == b"::on") {
        log::warn!("NPC event parameter deprecated! Please use 'NPCNAME::OnEVENT' instead of '{}'.", lat(evt));
        reportsrc(rt, st);
    }
}

/// map_id2sd(st->rid).
fn attached_sd(c: &Ctx, st: &State) -> Option<Unit> {
    unit::get(c, st.rid).filter(|u| u.kind == crate::map::BL_PC)
}

/// map_mapname2mapid: -1 when the map is not on this server.
fn mapname2mapid(name: &str) -> i32 {
    crate::data::maps().id_of(name).map(|m| m as i32).unwrap_or(-1)
}

/// The monster class of `monster` / `areamonster` argument `loc`: an AegisName, or an id (negative: a random
/// monster of group -id-1). None after the warning rAthena prints.
fn mob_class_arg(rt: &Rt, st: &mut State, loc: i32, func: &str) -> Option<i32> {
    if isstring(rt, st, loc) {
        let name = lat(&getstr(rt, st, loc));
        match mobs::mobdb_search_aegisname(&name) {
            Some(m) => Some(m.id as i32),
            None => {
                log::warn!("buildin_{func}: Attempted to spawn non-existing monster \"{name}\"");
                None
            }
        }
    } else {
        let class_ = getnum(rt, st, loc);
        if class_ >= 0 && mobs::mobdb_checkid(class_) == 0 {
            log::warn!("buildin_{func}: Attempted to spawn non-existing monster class {class_}");
            return None;
        }
        Some(class_)
    }
}

/// monster "<map name>",<x>,<y>,"<name to show>",<mob id / "mob name">,<amount>{,"<event label>",<size>,<ai>};
fn monster(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let mapn = lat(&getstr(rt, st, 2));
    let x = getnum(rt, st, 3);
    let y = getnum(rt, st, 4);
    let str_ = lat(&getstr(rt, st, 5));
    let amount = getnum(rt, st, 7);
    let Some(class_) = mob_class_arg(rt, st, 6, "monster") else { return SCRIPT_CMD_FAILURE };
    let mut event = String::new();
    if st.hasdata(8) {
        let e = getstr(rt, st, 8);
        check_event(rt, st, &e);
        event = lat(&e);
    }
    let mut size = crate::status::defs::SZ_SMALL as u32;
    if st.hasdata(9) {
        size = getnum(rt, st, 9) as u32;
        if size > crate::status::defs::SZ_BIG as u32 {
            log::warn!("buildin_monster: Attempted to spawn non-existing size {size} for monster class {class_}");
            return SCRIPT_CMD_FAILURE;
        }
    }
    let mut ai = mob::AI_NONE as i32;
    if st.hasdata(10) {
        ai = getnum(rt, st, 10);
        if ai >= AI_MAX {
            log::warn!("buildin_monster: Attempted to spawn non-existing ai {ai} for monster class {class_}");
            return SCRIPT_CMD_FAILURE;
        }
    }
    let sd = attached_sd(c, st);
    let m = if sd.is_some() && mapn == "this" { sd.as_ref().map(|s| s.map as i32).unwrap_or(-1) } else { mapname2mapid(&mapn) };
    for i in 0..amount.max(0) {
        // not optimised
        let mobid = spawn::mob_once_spawn(c, sd.as_ref(), m, x as i16, y as i16, Some(&str_), class_, 1, Some(&event), size as u8, ai as u8);
        if mobid > 0 {
            if let Some(mut md) = mob::get(c, mobid) {
                md.next_walktime = -1; // INVALID_TIMER
                mob::put(c, &md);
            }
            crate::party::mapreg_setreg(c, "$@mobid", i as u32, mobid as i64);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// areamonster "<map name>",<x1>,<y1>,<x2>,<y2>,"<name to show>",<mob id / "mob name">,<amount>{,"<event label>",
/// <size>,<ai>};
fn areamonster(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let mapn = lat(&getstr(rt, st, 2));
    let x0 = getnum(rt, st, 3);
    let y0 = getnum(rt, st, 4);
    let x1 = getnum(rt, st, 5);
    let y1 = getnum(rt, st, 6);
    let str_ = lat(&getstr(rt, st, 7));
    let amount = getnum(rt, st, 9);
    let Some(class_) = mob_class_arg(rt, st, 8, "areamonster") else { return SCRIPT_CMD_FAILURE };
    let mut event = String::new();
    if st.hasdata(10) {
        let e = getstr(rt, st, 10);
        check_event(rt, st, &e);
        event = lat(&e);
    }
    let mut size = crate::status::defs::SZ_SMALL as u32;
    if st.hasdata(11) {
        size = getnum(rt, st, 11) as u32;
        if size > 3 {
            log::warn!("buildin_areamonster: Attempted to spawn non-existing size {size} for monster class {class_}");
            return SCRIPT_CMD_FAILURE;
        }
    }
    let mut ai = mob::AI_NONE as i32;
    if st.hasdata(12) {
        ai = getnum(rt, st, 12);
        if ai >= AI_MAX {
            log::warn!("buildin_areamonster: Attempted to spawn non-existing ai {ai} for monster class {class_}");
            return SCRIPT_CMD_FAILURE;
        }
    }
    let sd = attached_sd(c, st);
    let m = if sd.is_some() && mapn == "this" { sd.as_ref().map(|s| s.map as i32).unwrap_or(-1) } else { mapname2mapid(&mapn) };
    for i in 0..amount.max(0) {
        // not optimised
        let mobid = spawn::mob_once_spawn_area(c, sd.as_ref(), m, x0 as i16, y0 as i16, x1 as i16, y1 as i16, Some(&str_), class_, 1, Some(&event), size as u8, ai as u8);
        if mobid > 0 {
            if let Some(mut md) = mob::get(c, mobid) {
                md.next_walktime = -1; // INVALID_TIMER
                mob::put(c, &md);
            }
            crate::party::mapreg_setreg(c, "$@mobid", i as u32, mobid as i64);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// The monsters of map m in map_foreachinmap order, each still in the block list when its turn comes.
fn each_mob_on_map(c: &Ctx, m: u16, mut f: impl FnMut(u32)) {
    for bl in mob::mobs_foreachinmap(c, m) {
        if unit::get(c, bl.id).map(|u| u.on_map).unwrap_or(false) {
            f(bl.id);
        }
    }
}

/// killmonster "<map name>","<event label>"{,<type>}; ("All": every monster without a spawn line). type 1 keeps
/// the monsters' kill events (buildin_killmonster_sub); otherwise they are stripped for the kill (_strip).
fn killmonster(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let mapname = lat(&getstr(rt, st, 2));
    let event_b = getstr(rt, st, 3);
    let event = lat(&event_b);
    let allflag = event == "All";
    if !allflag {
        check_event(rt, st, &event_b);
    }
    let m = mapname2mapid(&mapname);
    if m < 0 {
        return SCRIPT_CMD_SUCCESS;
    }
    let strip = !(st.hasdata(4) && getnum(rt, st, 4) == 1);
    each_mob_on_map(c, m as u16, |id| {
        let Some(mut md) = mob::get(c, id) else { return };
        if strip {
            md.npc_killmonster = true;
            mob::put(c, &md);
        }
        let kill = if !allflag { event == md.npc_event } else { md.spawn == 0 };
        if kill {
            crate::status::hp::status_kill(c, id);
        }
        if strip {
            if let Some(mut md) = mob::get(c, id) {
                md.npc_killmonster = false;
                mob::put(c, &md);
            }
        }
    });
    SCRIPT_CMD_SUCCESS
}

/// killmonsterall "<map name>"{,<type>}; type 1 keeps the kill events; otherwise they are cleared first.
fn killmonsterall(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let mapname = lat(&getstr(rt, st, 2));
    let m = mapname2mapid(&mapname);
    if m < 0 {
        return SCRIPT_CMD_SUCCESS;
    }
    let strip = !(st.hasdata(3) && getnum(rt, st, 3) == 1);
    each_mob_on_map(c, m as u16, |id| {
        if strip {
            // Strips the event from the mob if it's killed the old method.
            if let Some(mut md) = mob::get(c, id) {
                md.npc_event.clear();
                mob::put(c, &md);
            }
        }
        crate::status::hp::status_kill(c, id);
    });
    SCRIPT_CMD_SUCCESS
}

/// mobcount("<map name>","<event label>") -> the living monsters of the map with that event ("all": any);
/// -1 for an unknown map ("this" without an attached player also ends the script).
fn mobcount(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let mapname = lat(&getstr(rt, st, 2));
    let event_b = getstr(rt, st, 3);
    let event = if event_b.as_slice() == b"all" {
        None
    } else {
        check_event(rt, st, &event_b);
        Some(lat(&event_b))
    };
    let m = if mapname == "this" {
        match rid2sd(rt, st, "buildin_mobcount") {
            Some(aid) => unit::get(c, aid).map(|u| u.map as i32).unwrap_or(-1),
            None => {
                st.pushint(-1);
                return SCRIPT_CMD_SUCCESS;
            }
        }
    } else {
        mapname2mapid(&mapname)
    };
    if m < 0 {
        st.pushint(-1);
        return SCRIPT_CMD_SUCCESS;
    }
    let mut n = 0;
    each_mob_on_map(c, m as u16, |id| {
        if let Some(md) = mob::get(c, id) {
            if md.status.hp > 0 && event.as_ref().map(|e| *e == md.npc_event).unwrap_or(true) {
                n += 1;
            }
        }
    });
    st.pushint(n);
    SCRIPT_CMD_SUCCESS
}

/// strmobinfo(<type>,<monster id>) (the deprecated "ii" getmonsterinfo): 1 name, 2 jname, 3 level, 4 max HP,
/// 5 max SP, 6 base EXP, 7 job EXP.
fn strmobinfo(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let num = getnum(rt, st, 2);
    let class_ = getnum(rt, st, 3);
    let mob = if mobs::mobdb_checkid(class_) != 0 { mob::db_find(c, class_) } else { None };
    let Some(mob) = mob else {
        if num < 3 {
            st.pushstr(Vec::new()); // requested a string
        } else {
            st.pushint(0);
        }
        return SCRIPT_CMD_SUCCESS;
    };
    match num {
        1 => st.pushstr(crate::net::wire::latin1(&mob.name)),
        2 => st.pushstr(crate::net::wire::latin1(&mob.jname)),
        3 => st.pushint(mob.lv as i64),
        4 => st.pushint(mob.status.max_hp as i64),
        5 => st.pushint(mob.status.max_sp as i64),
        6 => st.pushint(mob.base_exp as i64),
        7 => st.pushint(mob.job_exp as i64),
        _ => st.pushint(0),
    }
    SCRIPT_CMD_SUCCESS
}

/// getmonsterinfo(<monster id / "AegisName">,<MOB_* type>).
fn getmonsterinfo(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let mob = if isstring(rt, st, 2) {
        mobs::mobdb_search_aegisname(&lat(&getstr(rt, st, 2)))
    } else {
        let mob_id = getnum(rt, st, 2) as u16;
        if rtdb::mob_is_clone(c, mob_id as i32) == 0 {
            mob::db_find(c, mob_id as i32)
        } else {
            None
        }
    };
    let type_ = getnum(rt, st, 3);
    const MOB_NAME: i32 = 1;
    let Some(mob) = mob else {
        if type_ == MOB_NAME {
            st.pushstr(b"null".to_vec()); // requested the name
        } else {
            st.pushint(-1);
        }
        return SCRIPT_CMD_SUCCESS;
    };
    let s = &mob.status;
    let v: i64 = match type_ {
        MOB_NAME => {
            st.pushstr(crate::net::wire::latin1(&mob.jname));
            return SCRIPT_CMD_SUCCESS;
        }
        2 => mob.lv as i64,              // MOB_LV
        3 => s.max_hp as i64,            // MOB_MAXHP
        4 => s.max_sp as i64,            // MOB_MAXSP
        5 => mob.base_exp as i64,        // MOB_BASEEXP
        6 => mob.job_exp as i64,         // MOB_JOBEXP
        7 => s.rhw.atk as i64,           // MOB_ATKMIN
        8 => s.rhw.atk2 as i64,          // MOB_ATKMAX
        9 => s.def as i64,               // MOB_DEF
        10 => s.mdef as i64,             // MOB_MDEF
        11 => s.res as i64,              // MOB_RES
        12 => s.mres as i64,             // MOB_MRES
        13 => s.str as i64,              // MOB_STR
        14 => s.agi as i64,              // MOB_AGI
        15 => s.vit as i64,              // MOB_VIT
        16 => s.int_ as i64,             // MOB_INT
        17 => s.dex as i64,              // MOB_DEX
        18 => s.luk as i64,              // MOB_LUK
        19 => s.speed as i64,            // MOB_SPEED
        20 => s.rhw.range as i64,        // MOB_ATKRANGE
        21 => mob.range2 as i64,         // MOB_SKILLRANGE
        22 => mob.range3 as i64,         // MOB_CHASERANGE
        23 => s.size as i64,             // MOB_SIZE
        24 => s.race as i64,             // MOB_RACE
        25 => s.def_ele as i64,          // MOB_ELEMENT
        26 => s.ele_lv as i64,           // MOB_ELEMENTLV
        27 => s.mode as i64,             // MOB_MODE
        28 => mob.mexp as i64,           // MOB_MVPEXP
        29 => mob.id as i64,             // MOB_ID
        _ => {
            log::error!("buildin_getmonsterinfo: Invalid getmonsterinfo type '{type_}'.");
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
    };
    st.pushint(v);
    SCRIPT_CMD_SUCCESS
}

/// getmobdrops(<monster id>): fills $@MobDrop_item[] / _rate[] / _nosteal[] / _randomopt[] and $@MobDrop_count;
/// 1 on success, 0 for an unknown monster.
fn getmobdrops(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let class_ = getnum(rt, st, 2);
    if mobs::mobdb_checkid(class_) == 0 {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    }
    let drops = rtdb::dropitem(c, class_ as u32);
    let mut j = 0u32;
    for entry in drops {
        if entry.nameid == 0 {
            continue;
        }
        if crate::db::items::itemdb_exists(entry.nameid).is_none() {
            continue;
        }
        crate::party::mapreg_setreg(c, "$@MobDrop_item", j, entry.nameid as i64);
        crate::party::mapreg_setreg(c, "$@MobDrop_rate", j, entry.rate as i64);
        crate::party::mapreg_setreg(c, "$@MobDrop_nosteal", j, entry.steal_protected as i64);
        crate::party::mapreg_setreg(c, "$@MobDrop_randomopt", j, entry.randomopt_group as i64);
        j += 1;
    }
    crate::party::mapreg_setreg(c, "$@MobDrop_count", 0, j as i64);
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}

/// getrandmobid(<type>{,<flag>{,<level>}}) -> a random monster id of a MOBG_ group.
fn getrandmobid(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let type_ = getnum(rt, st, 2);
    if !(mobs::MOBG_BRANCH_OF_DEAD_TREE..mobs::MOBG_MAX).contains(&type_) {
        log::warn!("buildin_getrandmobid: Invalid type {type_}.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    }
    let mut flag = if st.hasdata(3) { getnum(rt, st, 3) } else { mobs::RMF_MOB_NOT_BOSS as i32 };
    if flag < mobs::RMF_NONE as i32 || flag > mobs::RMF_ALL as i32 {
        log::warn!("buildin_getrandmobid: Invalid flag {flag}.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    }
    let lv;
    if st.hasdata(4) {
        lv = getnum(rt, st, 4);
        if lv <= 0 {
            log::warn!("buildin_getrandmobid: Invalid level {lv}.");
            st.pushint(0);
            return SCRIPT_CMD_FAILURE;
        }
        // If a level is provided, make sure it is respected
        flag |= mobs::RMF_CHECK_MOB_LV as i32;
    } else {
        lv = mob::status::MAX_LEVEL;
    }
    st.pushint(spawn::get_random_id(c, type_, flag as u32, lv) as i64);
    SCRIPT_CMD_SUCCESS
}

/// The monster of addmonsterdrop / delmonsterdrop argument 2: a name (mobdb_searchname) or an id.
fn drop_mob_arg(rt: &Rt, st: &mut State, c: &Ctx) -> Option<u32> {
    let id = if isstring(rt, st, 2) {
        let name = lat(&getstr(rt, st, 2));
        crate::atcommand_atcmd::admin::mobdb_searchname(c, &name) as i32
    } else {
        getnum(rt, st, 2)
    };
    mob::db_find(c, id).map(|m| m.id)
}

/// addmonsterdrop <mob id / "mob name">,<item id>,<rate>{,<steal protected>{,<random option group id>}};
/// adds or updates a drop until the server restarts (mob_reload_itemmob_data refreshes item_data's mob list).
fn addmonsterdrop(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let Some(mob_id) = drop_mob_arg(rt, st, c) else {
        if isstring(rt, st, 2) {
            log::error!("addmonsterdrop: bad mob name given {}", lat(&getstr(rt, st, 2)));
        } else {
            log::error!("addmonsterdrop: bad mob id given {}", getnum(rt, st, 2));
        }
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let item_id = getnum(rt, st, 3) as u32;
    let Some(itm) = crate::db::items::itemdb_exists(item_id) else {
        log::error!("addmonsterdrop: Nonexistant item {item_id} requested.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let rate = getnum(rt, st, 4);
    if !(1..=10000).contains(&rate) {
        log::error!("addmonsterdrop: Invalid rate {rate} (min 1, max 10000).");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    }
    let mut drops: Vec<MobDrop> = rtdb::dropitem(c, mob_id);
    // If it equals item_id we update that drop
    let existing = drops.iter().position(|d| d.nameid == item_id);
    if existing.is_none() && drops.len() == MAX_MOB_DROP {
        // No place to put the new drop
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    }
    let mut steal_protected = 0;
    let mut group = 0;
    if st.hasdata(5) {
        steal_protected = getnum(rt, st, 5);
    }
    if st.hasdata(6) {
        group = getnum(rt, st, 6);
        if crate::db::items::randomopt::random_option_group(group as u16).is_none() || group < 0 || group > u16::MAX as i32 {
            log::error!("buildin_addmonsterdrop: Unknown random option group {group}.");
            st.pushint(0);
            return SCRIPT_CMD_FAILURE;
        }
        use crate::db::items::{IT_ARMOR, IT_SHADOWGEAR, IT_WEAPON};
        if itm.r#type != IT_WEAPON && itm.r#type != IT_ARMOR && itm.r#type != IT_SHADOWGEAR {
            log::error!("buildin_addmonsterdrop: Random option group can't be used with this type of item (item Id: {item_id}).");
            st.pushint(0);
            return SCRIPT_CMD_FAILURE;
        }
    }
    // Fill in the slot with the item and rate
    let drop = MobDrop { nameid: item_id, rate: rate as u32, steal_protected: steal_protected > 0, randomopt_group: group as u16 };
    match existing {
        Some(i) => drops[i] = drop,
        None => drops.push(drop),
    }
    rtdb::set_dropitem(c, mob_id, &drops);
    // Reload the mob search data stored in the item_data
    rtdb::mob_reload_itemmob_data(c);
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}

/// delmonsterdrop <mob id / "mob name">,<item id>; -> 1 when the monster dropped it (the drop is removed until the
/// server restarts), 0 otherwise.
fn delmonsterdrop(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let mob_id = drop_mob_arg(rt, st, c);
    let item_id = getnum(rt, st, 3) as u32;
    if crate::db::items::itemdb_exists(item_id).is_none() {
        log::error!("delmonsterdrop: Nonexistant item {item_id} requested.");
        return SCRIPT_CMD_FAILURE;
    }
    let Some(mob_id) = mob_id else {
        log::warn!("delmonsterdrop: bad mob id given {}", getnum(rt, st, 2));
        return SCRIPT_CMD_FAILURE;
    };
    // We got a valid monster, check for item drop on monster
    let mut drops = rtdb::dropitem(c, mob_id);
    let before = drops.len();
    drops.retain(|d| d.nameid != item_id);
    if drops.len() != before {
        rtdb::set_dropitem(c, mob_id, &drops);
        // Reload the mob search data stored in the item_data
        rtdb::mob_reload_itemmob_data(c);
        st.pushint(1);
    } else {
        // No drop on that monster
        st.pushint(0);
    }
    SCRIPT_CMD_SUCCESS
}

/// mob_setidleevent( <monster game ID>, "<event label>" ): the event the monster runs while idle.
fn mob_setidleevent(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let Some(bl) = rid2bl(rt, st, 2, "buildin_mob_setidleevent") else {
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    };
    if bl.kind != crate::map::BL_MOB {
        log::error!("buildin_mob_setidleevent: the target GID was not a monster.");
        return SCRIPT_CMD_FAILURE;
    }
    let Some(mut md) = mob::get(c, bl.id) else { return SCRIPT_CMD_FAILURE };
    let idle_event = getstr(rt, st, 3);
    check_event(rt, st, &idle_event);
    // safestrncpy(md->idle_event, idle_event, EVENT_NAME_LENGTH)
    md.idle_event = lat(&idle_event).chars().take(crate::script::npc_run::EVENT_NAME_LENGTH - 1).collect();
    mob::put(c, &md);
    SCRIPT_CMD_SUCCESS
}

/// summon "<monster name>",<monster id>{,<time out>{,"<event label>"}}; -> the id of a monster that fights for
/// the attached player (AI_ATTACK) and vanishes after the time out (default 60 s).
fn summon(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let tick = c.tick();
    let Some(aid) = rid2sd(rt, st, "buildin_summon") else { return SCRIPT_CMD_FAILURE };
    let Some(sd) = unit::get(c, aid) else { return SCRIPT_CMD_FAILURE };
    let str_ = lat(&getstr(rt, st, 2));
    let class_ = getnum(rt, st, 3);
    let mut timeout = 0;
    if st.hasdata(4) {
        timeout = getnum(rt, st, 4);
    }
    let mut event = String::new();
    if st.hasdata(5) {
        let e = getstr(rt, st, 5);
        check_event(rt, st, &e);
        event = lat(&e);
    }
    let md = spawn::mob_once_spawn_sub(c, Some(&sd), sd.map, sd.x, sd.y, Some(&str_), class_, Some(&event), crate::status::defs::SZ_SMALL as u8, mob::AI_NONE);
    let Some(mut md) = md else {
        log::error!("buildin_summon: Invalid mob ID {class_}.");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    };
    mob::clif::skill_poseffect(c, &sd, crate::db::skills::ids::AM_CALLHOMUN, 1, sd.x, sd.y, tick);
    md.master_id = sd.id;
    md.special_ai = mob::AI_ATTACK;
    if md.deletetimer != 0 {
        crate::timer::delete(c, md.deletetimer);
    }
    md.deletetimer = crate::timer::add(c, tick + if timeout > 0 { timeout as i64 } else { 60000 }, crate::timer::Func::MobTimerDelete, md.id, 0);
    mob::put(c, &md);
    // Now it is ready for spawning.
    spawn::mob_spawn(c, md.id);
    if let Some(u) = unit::get(c, md.id) {
        crate::clif::specialeffect(c, &u, crate::data::constant("EF_ENTRY2").unwrap_or(0) as i32, crate::clif::Target::Area);
        crate::status::sc::sc_start4(c, None, &u, crate::status::consts::SC_MODECHANGE, 100, 1, 0, mobs::MD_AGGRESSIVE, 0, 60000);
    }
    st.pushint(md.id as i64);
    SCRIPT_CMD_SUCCESS
}

/// clone "<map name>",<x>,<y>,"<event label>",<char id>{,<master id>{,<mode>{,<flag>,<duration>}}} -> the clone's
/// id (0: failed).
fn clone(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let mapname = lat(&getstr(rt, st, 2));
    let x = getnum(rt, st, 3) as u32;
    let y = getnum(rt, st, 4) as u32;
    let event_b = getstr(rt, st, 5);
    let char_id = getnum(rt, st, 6) as u32;
    let mut master_id: u32 = 0;
    let mut mode = mobs::MD_NONE;
    let mut flag: u32 = 0;
    let mut duration: u32 = 0;
    if st.hasdata(7) {
        master_id = getnum(rt, st, 7) as u32;
    }
    if st.hasdata(8) {
        mode = getnum(rt, st, 8);
    }
    if st.hasdata(9) {
        flag = getnum(rt, st, 9) as u32;
    }
    if st.hasdata(10) {
        duration = getnum(rt, st, 10) as u32;
    }
    check_event(rt, st, &event_b);
    let m = mapname2mapid(&mapname);
    if m < 0 {
        return SCRIPT_CMD_SUCCESS;
    }
    let sd = crate::pc::charid2sd(c, char_id);
    if master_id != 0 {
        master_id = crate::pc::charid2sd(c, master_id).map(|msd| msd.id).unwrap_or(0);
    }
    match sd {
        // Return ID of newly crafted clone.
        Some(sd) => {
            let id = spawn::mob_clone_spawn(c, sd.id, m, x as i16, y as i16, &lat(&event_b), master_id, mode, flag as i32, duration.wrapping_mul(1000));
            st.pushint(id as i64);
        }
        // Failed to create clone.
        None => st.pushint(0),
    }
    SCRIPT_CMD_SUCCESS
}
