//! The homunculus script commands of script.cpp (rAthena e985006, RENEWAL): gethominfo, addhomintimacy,
//! homunculus_evolution ("homevolution"), homunculus_mutate ("hommutate"), morphembryo, homunculus_shuffle
//! ("homshuffle"), checkhomcall, checkidlehom, the RENEWAL homunculus EXP share of getexp ([`getexp_share`]), and the
//! BL_HOM arms of getunitdata / setunitdata ([`getunitdata_hom`], [`setunitdata_hom`]), setunitname
//! ([`setunitname_hom`]) and getmapxy ([`getmapxy_hom`]). unitwarp's BL_HOM arm is unit.rs' unit_warp
//! (homunculus/unit.rs); rid2name / getunitname / getunittype read the unit row, whose name mirrors
//! hd->homunculus.name (data::sync_unit).

use super::data::*;
use super::{clif, status};
use crate::script::buildins::{charid2sd, nick2sd, BuildinFn};
use crate::script::vm::*;
use crate::world::Ctx;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "gethominfo" => gethominfo,
        "addhomintimacy" => addhomintimacy,
        "homunculus_evolution" => homunculus_evolution,
        "homunculus_mutate" => homunculus_mutate,
        "morphembryo" => morphembryo,
        "homunculus_shuffle" => homunculus_shuffle,
        "checkhomcall" => checkhomcall,
        "checkidlehom" => checkidlehom,
        _ => return None,
    })
}

/// itemdb.hpp ITEMID_STRANGE_EMBRYO.
pub const ITEMID_STRANGE_EMBRYO: u32 = 6415;
/// clif.hpp emotion_type ET_SWEAT.
pub const ET_SWEAT: u8 = 4;

/// clif_emotion(*bl, ET_SWEAT) on the unit `id` (the player or its homunculus).
fn sweat(c: &Ctx, id: u32) {
    if let Some(u) = crate::unit::get(c, id) {
        crate::clif::emotion(c, &u, ET_SWEAT);
    }
}

/// script.cpp BUILDIN_FUNC(homunculus_evolution) (script.cpp:12700-12716), "homevolution": an active homunculus
/// with homunculus_evo_intimacy_need intimacy evolves (hom_evolution); below it, it sweats.
fn homunculus_evolution(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(sd) = rid2sd(rt, st, "homunculus_evolution") else { return SCRIPT_CMD_SUCCESS };
    let hd = of_master(c, sd);
    if hom_is_active(hd.as_ref()) {
        let Some(hd) = hd else { return SCRIPT_CMD_SUCCESS };
        if hd.homunculus.intimacy >= super::homunculus_evo_intimacy_need() {
            super::hom_evolution(c, hd.id);
        } else {
            sweat(c, hd.id);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// script.cpp BUILDIN_FUNC(homunculus_mutate) (script.cpp:12721-12752), "hommutate {<homunculus id>}": a homunculus in
/// the morph state (morphembryo) whose class is evolved, level 99 or more, with a Strange Embryo in the inventory,
/// becomes the homunculus S `homun_id` (random Eira..Eleanor by default): the morph state is left for the rest state,
/// hom_call brings it back, hom_mutate changes it, the embryo is used up; 1. Otherwise the player sweats; 0. Without a
/// player or a homunculus nothing is pushed (as rAthena).
fn homunculus_mutate(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(sd) = rid2sd(rt, st, "homunculus_mutate") else { return SCRIPT_CMD_SUCCESS };
    let Some(hd) = of_master(c, sd) else { return SCRIPT_CMD_SUCCESS };
    // uint16 homun_id = script_getnum(st,2) / rnd_value<uint16>(MER_EIRA, MER_ELEANOR)
    let homun_id: i32 = if st.hasdata(2) { getnum(rt, st, 2) as u16 as i32 } else { c.rnd_value(MER_EIRA, MER_ELEANOR) as u16 as i32 };
    if hd.homunculus.vaporize as i32 == HOM_ST_MORPH {
        let m_class = super::hom_class2mapid(hd.homunculus.class_ as i32);
        let m_id = super::hom_class2mapid(homun_id);
        let i = crate::inventory::pc_search_inventory(c, sd, ITEMID_STRANGE_EMBRYO);
        if m_class != -1 && m_id != -1 && m_class & HOM_EVO != 0 && m_id & HOM_S != 0 && hd.homunculus.level >= 99 && i >= 0 {
            with(c, hd.id, |hd| hd.homunculus.vaporize = HOM_ST_REST as i16); // Remove morph state.
            super::hom_call(c, sd); // Respawn homunculus.
            if let Some(hd) = of_master(c, sd) {
                super::hom_mutate(c, hd.id, homun_id);
            }
            // pc_delitem(sd, i, 1, 0, 0, LOG_TYPE_SCRIPT)
            crate::inventory::pc_delitem(c, sd, i as i32, 1, 0, 0, crate::map_log::LOG_TYPE_SCRIPT);
            st.pushint(1);
            return SCRIPT_CMD_SUCCESS;
        } else {
            sweat(c, sd);
        }
    } else {
        sweat(c, sd);
    }
    st.pushint(0);
    SCRIPT_CMD_SUCCESS
}

/// script.cpp BUILDIN_FUNC(morphembryo) (script.cpp:12760-12793): an active evolved homunculus of level 99 or more
/// becomes a Strange Embryo (the item first; then hom_vaporize(HOM_ST_MORPH)); 1. When the item cannot be added the
/// player gets the pickup failure and sweats (no drop: "Fail to avoid item drop exploit."); a homunculus that does not
/// qualify sweats, an inactive one makes the player sweat; 0. Without a player or a homunculus nothing is pushed.
fn morphembryo(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(sd) = rid2sd(rt, st, "morphembryo") else { return SCRIPT_CMD_SUCCESS };
    let Some(hd) = of_master(c, sd) else { return SCRIPT_CMD_SUCCESS };
    if hom_is_active(Some(&hd)) {
        let m_class = super::hom_class2mapid(hd.homunculus.class_ as i32);
        if m_class != -1 && m_class & HOM_EVO != 0 && hd.homunculus.level >= 99 {
            let mut item_tmp = crate::inventory::Item::new(ITEMID_STRANGE_EMBRYO);
            item_tmp.identify = 1;
            // (i = pc_additem(sd, &item_tmp, 1, LOG_TYPE_SCRIPT)): the flag is kept in a char
            let i = crate::inventory::pc_additem(c, sd, &mut item_tmp, 1, crate::map_log::LOG_TYPE_SCRIPT);
            if i != 0 {
                crate::clif_items::additem(c, sd, 0, 0, i);
                sweat(c, sd); // Fail to avoid item drop exploit.
            } else {
                super::hom_vaporize(c, sd, HOM_ST_MORPH);
                st.pushint(1);
                return SCRIPT_CMD_SUCCESS;
            }
        } else {
            sweat(c, hd.id);
        }
    } else {
        sweat(c, sd);
    }
    st.pushint(0);
    SCRIPT_CMD_SUCCESS
}

/// script.cpp BUILDIN_FUNC(homunculus_shuffle) (script.cpp:12796-12807), "homshuffle": hom_shuffle of an active
/// homunculus.
fn homunculus_shuffle(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(sd) = rid2sd(rt, st, "homunculus_shuffle") else { return SCRIPT_CMD_SUCCESS };
    if let Some(hd) = of_master(c, sd).filter(|hd| hom_is_active(Some(hd))) {
        super::hom_shuffle(c, hd.id);
    }
    SCRIPT_CMD_SUCCESS
}

/// script.cpp BUILDIN_FUNC(checkhomcall) (script.cpp:12816-12833): -1 without a homunculus, else its state (0 active,
/// 1 rest, 2 morph).
fn checkhomcall(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(sd) = rid2sd(rt, st, "checkhomcall") else { return SCRIPT_CMD_SUCCESS };
    match of_master(c, sd) {
        None => st.pushint(-1),
        Some(hd) => st.pushint(hd.homunculus.vaporize as i64),
    }
    SCRIPT_CMD_SUCCESS
}

// hom_idle_no_share is off (homunc.conf `hom_idle_no_share: no`): every `sd->idletime_hom = last_tick` of clif.cpp /
// atcommand.cpp is behind `battle_config.hom_idle_no_share &&`, so sd->idletime_hom keeps the 0 of the calloc'd
// map_session_data (clif.cpp:10721 CREATE + placement new; pc_setnewpc never sets it). checkidlehom relies on that.
const _: () = assert!(super::HOM_IDLE_NO_SHARE == 0, "hom_idle_no_share on: sd->idletime_hom has to become a pc field");

/// script.cpp BUILDIN_FUNC(checkidlehom) (script.cpp:18823-18832): `DIFF_TICK(last_tick, sd->idletime_hom)` of the
/// player named (or attached), 0 when not found. With hom_idle_no_share off idletime_hom is never set (see above), so
/// the answer is last_tick itself (time(nullptr), seconds), exactly what rAthena answers.
fn checkidlehom(rt: &Rt, st: &mut State) -> i32 {
    match nick2sd(rt, st, 2, "buildin_checkidlehom") {
        Some(_sd) => {
            let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
            let idletime_hom: i64 = 0;
            st.pushint(crate::pc::last_tick(c) - idletime_hom);
        }
        None => st.pushint(0),
    }
    SCRIPT_CMD_SUCCESS
}

/// script.cpp BUILDIN_FUNC(gethominfo) (script.cpp:15998-16025), "gethominfo <type>{,<char_id>}": 0 hom_id, 1 class,
/// 2 name, 3 intimacy, 4 hunger, 5 rename flag, 6 level, 7 game id; without a player or a homunculus "null" (type 2)
/// or 0.
fn gethominfo(rt: &Rt, st: &mut State) -> i32 {
    let type_ = getnum(rt, st, 2);
    let hd = match rt.c {
        Some(c) => charid2sd(rt, st, 3, "buildin_gethominfo").and_then(|sd| of_master(c, sd)),
        None => None,
    };
    let Some(hd) = hd else {
        if type_ == 2 {
            st.pushstr(b"null".to_vec());
        } else {
            st.pushint(0);
        }
        return SCRIPT_CMD_SUCCESS;
    };
    let h = &hd.homunculus;
    match type_ {
        0 => st.pushint(h.hom_id as i64),
        1 => st.pushint(h.class_ as i64),
        2 => st.pushstr(crate::net::wire::latin1(&h.name)),
        3 => st.pushint(h.intimacy as i64),
        4 => st.pushint(h.hunger as i64),
        5 => st.pushint(h.rename_flag as i64),
        6 => st.pushint(h.level as i64),
        7 => st.pushint(hd.id as i64),
        _ => st.pushint(0),
    }
    SCRIPT_CMD_SUCCESS
}

/// script.cpp BUILDIN_FUNC(addhomintimacy) (script.cpp:16027-16048), "addhomintimacy <amount>{,<char_id>}": the
/// homunculus' intimacy goes up (hom_increase_intimacy: homunculus_friendly_rate, at most 100000) or down
/// (hom_decrease_intimacy, at least 0), then the master gets it (clif_send_homdata SP_INTIMATE).
fn addhomintimacy(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(sd) = charid2sd(rt, st, 3, "buildin_addhomintimacy") else { return SCRIPT_CMD_SUCCESS };
    let Some(mut hd) = of_master(c, sd) else { return SCRIPT_CMD_SUCCESS };
    let value = getnum(rt, st, 2);
    if value == 0 {
        // Nothing to change
        return SCRIPT_CMD_SUCCESS;
    }
    if value > 0 {
        super::hom_increase_intimacy(&mut hd, value as u32);
    } else {
        // (uint32)abs(value): abs(INT_MIN) stays INT_MIN, 0x80000000 as uint32
        super::hom_decrease_intimacy(&mut hd, value.unsigned_abs());
    }
    put(c, &hd);
    clif::clif_send_homdata(c, hd.id, SP_INTIMATE);
    SCRIPT_CMD_SUCCESS
}

/// script.cpp BUILDIN_FUNC(getexp) (script.cpp:11177-11180, RENEWAL): after pc_gainexp, `if (base && sd->hd)
/// hom_gainexp(sd->hd, base * battle_config.homunculus_exp_gain / 100)` ("Homunculus only receive 10% of EXP"; a
/// vaporized homunculus gets nothing: hom_gainexp returns). `base` is the EXP after quest_exp_rate.
pub fn getexp_share(c: &Ctx, sd: u32, base: i64) {
    if base == 0 {
        return;
    }
    if let Some(hd) = of_master(c, sd) {
        super::hom_gainexp(c, hd.id, (base * super::homunculus_exp_gain() as i64 / 100) as u64);
    }
}

/// script.cpp buildin_getmapxy, BL_HOM (script.cpp:16523-16526): the homunculus of the player named / with that
/// account id (or the attached one): sd->hd's map (bl->m, kept off the map too) and cell. None without one.
pub fn getmapxy_hom(c: &Ctx, sd: Option<u32>) -> Option<(i32, i64, i64)> {
    let hd = of_master(c, sd?)?;
    crate::unit::get(c, hd.id).map(|u| (u.map as i32, u.x as i64, u.y as i64))
}

/// script.cpp buildin_setunitname, BL_HOM (script.cpp:19917-19923): safestrncpy(hd->homunculus.name, name,
/// NAME_LENGTH) (put copies it to the unit row clif_name reads). False when map_id2hd finds nothing ("Error in finding
/// object BL_HOM!").
pub fn setunitname_hom(c: &Ctx, id: u32, name: &[u8]) -> bool {
    let Some(mut hd) = get(c, id) else {
        log::warn!("buildin_setunitname: Error in finding object BL_HOM!");
        return false;
    };
    let n: Vec<u8> = name.iter().copied().take_while(|&b| b != 0).take(crate::mob::NAME_LENGTH - 1).collect();
    hd.homunculus.name = crate::net::wire::from_latin1(&n);
    put(c, &hd);
    true
}

/// A script constant (UHOM_*) by name.
fn k(name: &str) -> i32 {
    crate::data::constant(name).map(|v| v as i32).unwrap_or(i32::MIN)
}

/// script.cpp buildin_getunitdata, BL_HOM (script.cpp:19157-19200): the homunculus' data into array[UHOM_*]
/// (getunitdata_sub: setd_sub_num of the C value).
#[allow(clippy::too_many_arguments)]
pub fn getunitdata_hom(rt: &Rt, st: &mut State, u: &crate::unit::Unit, sd: Option<u32>, id: i32, name: &[u8], rf: &RegRef) {
    let Some(c) = rt.c else { return };
    let Some(hd) = get(c, u.id) else { return };
    let (h, b, s, ud) = (&hd.homunculus, &hd.base_status, &hd.battle_status, &u.ud);
    let vals: Vec<(&str, i64)> = vec![
        ("UHOM_SIZE", b.size as i64),
        ("UHOM_LEVEL", h.level as i64),
        ("UHOM_HP", h.hp as i64),
        ("UHOM_MAXHP", h.max_hp as i64),
        ("UHOM_SP", h.sp as i64),
        ("UHOM_MAXSP", h.max_sp as i64),
        ("UHOM_MASTERCID", h.char_id as i64),
        ("UHOM_MAPID", u.map as i64),
        ("UHOM_X", u.x as i64),
        ("UHOM_Y", u.y as i64),
        ("UHOM_HUNGER", h.hunger as i64),
        ("UHOM_INTIMACY", h.intimacy as i64),
        ("UHOM_SPEED", b.speed as i64),
        ("UHOM_LOOKDIR", ud.dir as i64),
        ("UHOM_CANMOVETICK", ud.canmove_tick),
        ("UHOM_STR", b.str as i64),
        ("UHOM_AGI", b.agi as i64),
        ("UHOM_VIT", b.vit as i64),
        ("UHOM_INT", b.int_ as i64),
        ("UHOM_DEX", b.dex as i64),
        ("UHOM_LUK", b.luk as i64),
        ("UHOM_DMGIMMUNE", ud.immune_attack as i64),
        ("UHOM_ATKRANGE", s.rhw.range as i64),
        ("UHOM_ATKMIN", b.rhw.atk as i64),
        ("UHOM_ATKMAX", b.rhw.atk2 as i64),
        ("UHOM_MATKMIN", b.matk_min as i64),
        ("UHOM_MATKMAX", b.matk_max as i64),
        ("UHOM_DEF", s.def as i64),
        ("UHOM_MDEF", s.mdef as i64),
        ("UHOM_HIT", s.hit as i64),
        ("UHOM_FLEE", s.flee as i64),
        ("UHOM_PDODGE", s.flee2 as i64),
        ("UHOM_CRIT", s.cri as i64),
        ("UHOM_RACE", s.race as i64),
        ("UHOM_ELETYPE", s.def_ele as i64),
        ("UHOM_ELELEVEL", s.ele_lv as i64),
        ("UHOM_AMOTION", s.amotion as i64),
        ("UHOM_ADELAY", s.adelay as i64),
        ("UHOM_DMOTION", s.dmotion as i64),
        // ud.target (int32)
        ("UHOM_TARGETID", ud.target as i32 as i64),
        ("UHOM_GROUP_ID", ud.group_id as i64),
    ];
    for (key, v) in vals {
        crate::script::reg::set_reg_num(rt, st, sd, id, k(key) as u32, name, v, rf);
    }
}

/// script.cpp buildin_setunitdata, BL_HOM (script.cpp:19539-19602): one UHOM_* field changes (the stats, speed and
/// battle values on hd->base_status followed by status_calc_bl_(hd, SCB_BATTLE)), then the master gets
/// clif_send_homdata(SP_ACK). `mapname` is set for UHOM_MAPID given as a map name.
pub fn setunitdata_hom(c: &Ctx, id: u32, t: i32, value: i32, mapname: Option<&[u8]>) -> i32 {
    let Some(mut hd) = get(c, id) else { return SCRIPT_CMD_FAILURE };
    let mut calc_status = false;
    let lv = hd.homunculus.level as i32;
    // status_calc_misc(bl, &hd->base_status, hd->homunculus.level) after a stat / speed change
    let stat = |hd: &mut HomData, f: fn(&mut crate::status::data::StatusData) -> &mut i16| {
        *f(&mut hd.base_status) = value as u16 as i16;
        status::status_calc_misc_hom(hd, lv);
    };
    if t == k("UHOM_SIZE") {
        hd.battle_status.size = value as u8;
        hd.base_status.size = value as u8;
        put(c, &hd);
    } else if t == k("UHOM_LEVEL") {
        hd.homunculus.level = value as u16 as i16;
        put(c, &hd);
    } else if t == k("UHOM_HP") {
        hd.base_status.hp = value as u32;
        put(c, &hd);
        crate::status::hp::status_set_hp(c, id, value as u32, 0);
    } else if t == k("UHOM_MAXHP") {
        hd.base_status.hp = value as u32;
        hd.base_status.max_hp = value as u32;
        put(c, &hd);
        crate::status::hp::status_set_maxhp(c, id, value as u32, 0);
    } else if t == k("UHOM_SP") {
        hd.base_status.sp = value as u32;
        put(c, &hd);
        crate::status::hp::status_set_sp(c, id, value as u32, 0);
    } else if t == k("UHOM_MAXSP") {
        hd.base_status.sp = value as u32;
        hd.base_status.max_sp = value as u32;
        put(c, &hd);
        crate::status::hp::status_set_maxsp(c, id, value as u32, 0);
    } else if t == k("UHOM_MASTERCID") {
        hd.homunculus.char_id = value as u32;
        put(c, &hd);
    } else if t == k("UHOM_MAPID") {
        let m = match mapname {
            Some(n) => crate::data::maps().id_of(&crate::net::wire::from_latin1(n)).map(|m| m as i32).unwrap_or(-1),
            None => value,
        };
        super::unit::unit_warp(c, id, m as i16 as i32, 0, 0, crate::unit::CLR_TELEPORT);
    } else if t == k("UHOM_X") || t == k("UHOM_Y") {
        let Some(u) = crate::unit::get(c, id) else { return SCRIPT_CMD_FAILURE };
        let (x, y) = if t == k("UHOM_X") { (value as i16, u.y) } else { (u.x, value as i16) };
        if !crate::unit::walktoxy(c, id, x, y, 2) {
            crate::unit::movepos(c, id, x, y, 0, false);
        }
    } else if t == k("UHOM_HUNGER") {
        hd.homunculus.hunger = value as i16;
        put(c, &hd);
        clif::clif_send_homdata(c, id, SP_HUNGRY);
    } else if t == k("UHOM_INTIMACY") {
        super::hom_increase_intimacy(&mut hd, value as u32);
        put(c, &hd);
        clif::clif_send_homdata(c, id, SP_INTIMATE);
    } else if t == k("UHOM_SPEED") {
        hd.base_status.speed = value as u16;
        status::status_calc_misc_hom(&mut hd, lv);
        calc_status = true;
        put(c, &hd);
    } else if t == k("UHOM_LOOKDIR") {
        if let Some(mut u) = crate::unit::get(c, id) {
            crate::unit::setdir(c, &mut u, value as u8, true);
            crate::unit::save(c, &u);
        }
    } else if t == k("UHOM_CANMOVETICK") {
        if let Some(mut u) = crate::unit::get(c, id) {
            u.ud.canmove_tick = if value > 0 { value as u32 as i64 } else { 0 };
            crate::unit::save(c, &u);
        }
    } else if t == k("UHOM_STR") {
        stat(&mut hd, |s| &mut s.str);
        calc_status = true;
        put(c, &hd);
    } else if t == k("UHOM_AGI") {
        stat(&mut hd, |s| &mut s.agi);
        calc_status = true;
        put(c, &hd);
    } else if t == k("UHOM_VIT") {
        stat(&mut hd, |s| &mut s.vit);
        calc_status = true;
        put(c, &hd);
    } else if t == k("UHOM_INT") {
        stat(&mut hd, |s| &mut s.int_);
        calc_status = true;
        put(c, &hd);
    } else if t == k("UHOM_DEX") {
        stat(&mut hd, |s| &mut s.dex);
        calc_status = true;
        put(c, &hd);
    } else if t == k("UHOM_LUK") {
        stat(&mut hd, |s| &mut s.luk);
        calc_status = true;
        put(c, &hd);
    } else if t == k("UHOM_DMGIMMUNE") {
        if let Some(mut u) = crate::unit::get(c, id) {
            u.ud.immune_attack = value > 0;
            crate::unit::save(c, &u);
        }
    } else if t == k("UHOM_RACE") {
        hd.battle_status.race = value as u8;
        hd.base_status.race = value as u8;
        put(c, &hd);
    } else if t == k("UHOM_TARGETID") {
        if value == 0 {
            if let Some(mut u) = crate::unit::get(c, id) {
                crate::unit::stop_attack(c, &mut u);
                crate::unit::save(c, &u);
            }
        } else {
            // map_id2bl(value): a floating NPC (no unit row) is found too, and unit_attack answers for it
            let Some(target) = crate::script::buildins::script2::unitctl::map_id2bl(c, value as u32) else {
                log::warn!("buildin_setunitdata: Error in finding target for BL_HOM!");
                return SCRIPT_CMD_FAILURE;
            };
            crate::battle::unit_attack::unit_attack(c, id, target.id(), 1);
        }
    } else if t == k("UHOM_GROUP_ID") {
        if let Some(mut u) = crate::unit::get(c, id) {
            u.ud.group_id = value;
            crate::unit::save(c, &u);
            crate::unit_npc::unit_refresh(c, &u, false);
        }
    } else {
        // the base-status values followed by a battle recalculation
        let b = &mut hd.base_status;
        if t == k("UHOM_ATKRANGE") {
            b.rhw.range = value as u16;
        } else if t == k("UHOM_ATKMIN") {
            b.rhw.atk = value as u16;
        } else if t == k("UHOM_ATKMAX") {
            b.rhw.atk2 = value as u16;
        } else if t == k("UHOM_MATKMIN") {
            b.matk_min = value as u16;
        } else if t == k("UHOM_MATKMAX") {
            b.matk_max = value as u16;
        } else if t == k("UHOM_DEF") {
            b.def = value as i16;
        } else if t == k("UHOM_MDEF") {
            b.mdef = value as i16;
        } else if t == k("UHOM_HIT") {
            b.hit = value as i16;
        } else if t == k("UHOM_FLEE") {
            b.flee = value as i16;
        } else if t == k("UHOM_PDODGE") {
            b.flee2 = value as i16;
        } else if t == k("UHOM_CRIT") {
            b.cri = value as i16;
        } else if t == k("UHOM_ELETYPE") {
            b.def_ele = value as u8;
        } else if t == k("UHOM_ELELEVEL") {
            b.ele_lv = value as u8;
        } else if t == k("UHOM_AMOTION") {
            b.amotion = value as i16 as u16;
        } else if t == k("UHOM_ADELAY") {
            b.adelay = value as i16 as u16;
        } else if t == k("UHOM_DMOTION") {
            b.dmotion = value as i16 as u16;
        } else {
            log::error!("buildin_setunitdata: Unknown data identifier {t} for BL_HOM.");
            return SCRIPT_CMD_FAILURE;
        }
        calc_status = true;
        put(c, &hd);
    }
    if calc_status {
        status::status_calc_bl_hom(c, id, crate::db::misc::status::status_db().get_scb_battle(), crate::status::defs::SCO_NONE);
    }
    // Client information update
    clif::clif_send_homdata(c, id, SP_ACK);
    SCRIPT_CMD_SUCCESS
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Every homunculus command of script.cpp's buildin_func[] resolves to its port (the script names, which the
    /// table maps to the C functions).
    #[test]
    fn homunculus_commands_resolve() {
        let ported = crate::script::buildins::ported();
        for name in ["gethominfo", "addhomintimacy", "homevolution", "hommutate", "morphembryo", "homshuffle", "checkhomcall", "checkidlehom"] {
            assert!(ported.contains(&name), "{name} is not ported");
        }
        for cfunc in ["gethominfo", "addhomintimacy", "homunculus_evolution", "homunculus_mutate", "morphembryo", "homunculus_shuffle", "checkhomcall", "checkidlehom"] {
            assert!(lookup(cfunc).is_some(), "{cfunc}");
        }
        assert!(lookup("checkidlemer").is_none()); // the mercenaries' (not a homunculus command)
    }
}
