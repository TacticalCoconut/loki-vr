//! script.cpp's BL_MER / BL_ELEM arms of getunitdata and setunitdata (the UMER_* / UELE_* fields), called by the
//! script2 unit commands (script/buildins/script2/unitinfo.rs).

use super::Comp;
use crate::script::vm::*;
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// A script constant (UMER_*, UELE_*) by name.
fn k(name: &str) -> i32 {
    crate::data::constant(name).map(|v| v as i32).unwrap_or(i32::MIN)
}

/// script.cpp buildin_getunitdata, BL_MER / BL_ELEM: every field into the array variable, in rAthena's order (the
/// elemental's UELE_SP slot takes its speed after its SP: rAthena writes `getunitdata_sub(UELE_SP, speed)`).
pub fn getunitdata_comp(rt: &Rt, st: &mut State, u: &Unit, sd: Option<u32>, id: i32, name: &[u8], rf: &RegRef) {
    let Some(c) = rt.c else { return };
    let Some(x) = super::get(c, u.id) else { return };
    let ud = &u.ud;
    let vals: Vec<(&str, i64)> = match &x {
        Comp::Mer(mc) => {
            let s = &mc.base_status;
            vec![
                ("UMER_SIZE", s.size as i64),
                ("UMER_HP", s.hp as i64),
                ("UMER_MAXHP", s.max_hp as i64),
                ("UMER_MASTERCID", mc.mercenary.char_id as i64),
                ("UMER_MAPID", u.map as i64),
                ("UMER_X", u.x as i64),
                ("UMER_Y", u.y as i64),
                ("UMER_KILLCOUNT", mc.mercenary.kill_count as i64),
                ("UMER_LIFETIME", mc.mercenary.life_time),
                ("UMER_SPEED", s.speed as i64),
                ("UMER_LOOKDIR", ud.dir as i64),
                ("UMER_CANMOVETICK", ud.canmove_tick),
                ("UMER_STR", s.str as i64),
                ("UMER_AGI", s.agi as i64),
                ("UMER_VIT", s.vit as i64),
                ("UMER_INT", s.int_ as i64),
                ("UMER_DEX", s.dex as i64),
                ("UMER_LUK", s.luk as i64),
                ("UMER_DMGIMMUNE", ud.immune_attack as i64),
                ("UMER_ATKRANGE", s.rhw.range as i64),
                ("UMER_ATKMIN", s.rhw.atk as i64),
                ("UMER_ATKMAX", s.rhw.atk2 as i64),
                ("UMER_MATKMIN", s.matk_min as i64),
                ("UMER_MATKMAX", s.matk_max as i64),
                ("UMER_DEF", s.def as i64),
                ("UMER_MDEF", s.mdef as i64),
                ("UMER_HIT", s.hit as i64),
                ("UMER_FLEE", s.flee as i64),
                ("UMER_PDODGE", s.flee2 as i64),
                ("UMER_CRIT", s.cri as i64),
                ("UMER_RACE", s.race as i64),
                ("UMER_ELETYPE", s.def_ele as i64),
                ("UMER_ELELEVEL", s.ele_lv as i64),
                ("UMER_AMOTION", s.amotion as i64),
                ("UMER_ADELAY", s.adelay as i64),
                ("UMER_DMOTION", s.dmotion as i64),
                ("UMER_TARGETID", ud.target as i32 as i64),
                ("UMER_GROUP_ID", ud.group_id as i64),
            ]
        }
        Comp::Elem(ed) => {
            let s = &ed.base_status;
            let e = &ed.elemental;
            vec![
                ("UELE_SIZE", s.size as i64),
                ("UELE_HP", e.hp as i64),
                ("UELE_MAXHP", e.max_hp as i64),
                ("UELE_SP", e.sp as i64),
                ("UELE_MAXSP", e.max_sp as i64),
                ("UELE_MASTERCID", e.char_id as i64),
                ("UELE_MAPID", u.map as i64),
                ("UELE_X", u.x as i64),
                ("UELE_Y", u.y as i64),
                ("UELE_LIFETIME", e.life_time),
                ("UELE_MODE", e.mode as i64),
                ("UELE_SP", s.speed as i64),
                ("UELE_LOOKDIR", ud.dir as i64),
                ("UELE_CANMOVETICK", ud.canmove_tick),
                ("UELE_STR", s.str as i64),
                ("UELE_AGI", s.agi as i64),
                ("UELE_VIT", s.vit as i64),
                ("UELE_INT", s.int_ as i64),
                ("UELE_DEX", s.dex as i64),
                ("UELE_LUK", s.luk as i64),
                ("UELE_DMGIMMUNE", ud.immune_attack as i64),
                ("UELE_ATKRANGE", s.rhw.range as i64),
                ("UELE_ATKMIN", s.rhw.atk as i64),
                ("UELE_ATKMAX", s.rhw.atk2 as i64),
                ("UELE_MATKMIN", s.matk_min as i64),
                ("UELE_MATKMAX", s.matk_max as i64),
                ("UELE_DEF", s.def as i64),
                ("UELE_MDEF", s.mdef as i64),
                ("UELE_HIT", s.hit as i64),
                ("UELE_FLEE", s.flee as i64),
                ("UELE_PDODGE", s.flee2 as i64),
                ("UELE_CRIT", s.cri as i64),
                ("UELE_RACE", s.race as i64),
                ("UELE_ELETYPE", s.def_ele as i64),
                ("UELE_ELELEVEL", s.ele_lv as i64),
                ("UELE_AMOTION", s.amotion as i64),
                ("UELE_ADELAY", s.adelay as i64),
                ("UELE_DMOTION", s.dmotion as i64),
                ("UELE_TARGETID", ud.target as i32 as i64),
                ("UELE_GROUP_ID", ud.group_id as i64),
            ]
        }
    };
    for (key, v) in vals {
        crate::script::reg::set_reg_num(rt, st, sd, id, k(key) as u32, name, v, rf);
    }
}

/// script.cpp buildin_setunitdata, BL_MER / BL_ELEM: one UMER_* / UELE_* field changes (the stats redo the base
/// status' misc values; the battle fields recalculate the battle status: SCB_BATTLE), then the master is shown the
/// mercenary's info and skills (clif_mercenary_info / skillblock) or the elemental's info (clif_elemental_info).
pub fn setunitdata_comp(c: &Ctx, id: u32, t: i32, value: i32, mapname: Option<&[u8]>) -> i32 {
    let Some(x) = super::get(c, id) else { return SCRIPT_CMD_FAILURE };
    let Some(u) = unit::get(c, id) else { return SCRIPT_CMD_FAILURE };
    let kind = x.kind();
    let (p, lv) = match &x {
        Comp::Mer(md) => ("UMER_", crate::mercenary::data::db_of(md).map(|d| d.lv as i32).unwrap_or(1)),
        Comp::Elem(ed) => ("UELE_", crate::elemental::data::db_of(ed).map(|d| d.lv as i32).unwrap_or(1)),
    };
    let is = |n: &str| t == k(&format!("{p}{n}"));
    let mut calc_status = false;
    // the base status / its misc values, stored back
    let base = |f: &dyn Fn(&mut crate::status::data::StatusData), misc: bool| {
        if let Some(mut x) = super::get(c, id) {
            match &mut x {
                Comp::Mer(md) => {
                    f(&mut md.base_status);
                    if misc {
                        super::status::status_calc_misc(kind, &mut md.base_status, &mut md.regen, lv);
                    }
                }
                Comp::Elem(ed) => {
                    f(&mut ed.base_status);
                    if misc {
                        super::status::status_calc_misc(kind, &mut ed.base_status, &mut ed.regen, lv);
                    }
                }
            }
            super::put(c, &x);
        }
    };
    let v = value;
    if is("SIZE") {
        if let Some(mut x) = super::get(c, id) {
            x.battle_status_mut().size = v as u8;
            x.base_status_mut().size = v as u8;
            super::put(c, &x);
        }
    } else if is("HP") {
        base(&|s| s.hp = v as u32, false);
        crate::status::hp::status_set_hp(c, id, v as u32, 0);
    } else if is("MAXHP") {
        base(&|s| {
            s.hp = v as u32;
            s.max_hp = v as u32
        }, false);
        crate::status::hp::status_set_maxhp(c, id, v as u32, 0);
    } else if kind == crate::map::BL_ELEM && is("SP") {
        base(&|s| s.sp = v as u32, false);
        crate::status::hp::status_set_sp(c, id, v as u32, 0);
    } else if kind == crate::map::BL_ELEM && is("MAXSP") {
        base(&|s| {
            s.sp = v as u32;
            s.max_sp = v as u32
        }, false);
        crate::status::hp::status_set_maxsp(c, id, v as u32, 0);
    } else if is("MASTERCID") {
        if let Some(mut x) = super::get(c, id) {
            match &mut x {
                Comp::Mer(md) => md.mercenary.char_id = v as u32,
                Comp::Elem(ed) => ed.elemental.char_id = v as u32,
            }
            super::put(c, &x);
        }
    } else if is("MAPID") {
        let m = match mapname {
            Some(n) => crate::data::maps().id_of(&crate::net::wire::from_latin1(n)).map(|m| m as i32).unwrap_or(-1),
            None => v,
        };
        super::unit::unit_warp(c, id, m as i16 as i32, 0, 0, unit::CLR_TELEPORT);
    } else if is("X") || is("Y") {
        let (xx, yy) = if is("X") { (v as i16, u.y) } else { (u.x, v as i16) };
        if !unit::walktoxy(c, id, xx, yy, 2) {
            unit::movepos(c, id, xx, yy, 0, false);
        }
    } else if kind == crate::map::BL_MER && is("KILLCOUNT") {
        if let Some(mut md) = crate::mercenary::data::get(c, id) {
            md.mercenary.kill_count = v as u32;
            crate::mercenary::data::put(c, &md);
        }
    } else if is("LIFETIME") {
        if let Some(mut x) = super::get(c, id) {
            match &mut x {
                Comp::Mer(md) => md.mercenary.life_time = v as u32 as i64,
                Comp::Elem(ed) => ed.elemental.life_time = v as u32 as i64,
            }
            super::put(c, &x);
        }
    } else if kind == crate::map::BL_ELEM && is("MODE") {
        if let Some(mut ed) = crate::elemental::data::get(c, id) {
            ed.elemental.mode = v;
            crate::elemental::data::put(c, &ed);
        }
        calc_status = true;
    } else if is("SPEED") {
        base(&|s| s.speed = v as u16, true);
        calc_status = true;
    } else if is("LOOKDIR") {
        if let Some(mut u) = unit::get(c, id) {
            unit::setdir(c, &mut u, v as u8, true);
            unit::save(c, &u);
        }
    } else if is("CANMOVETICK") {
        if let Some(mut u) = unit::get(c, id) {
            u.ud.canmove_tick = if v > 0 { v as u32 as i64 } else { 0 };
            unit::save(c, &u);
        }
    } else if is("STR") {
        base(&|s| s.str = v as u16 as i16, true);
        calc_status = true;
    } else if is("AGI") {
        base(&|s| s.agi = v as u16 as i16, true);
        calc_status = true;
    } else if is("VIT") {
        base(&|s| s.vit = v as u16 as i16, true);
        calc_status = true;
    } else if is("INT") {
        base(&|s| s.int_ = v as u16 as i16, true);
        calc_status = true;
    } else if is("DEX") {
        base(&|s| s.dex = v as u16 as i16, true);
        calc_status = true;
    } else if is("LUK") {
        base(&|s| s.luk = v as u16 as i16, true);
        calc_status = true;
    } else if is("DMGIMMUNE") {
        if let Some(mut u) = unit::get(c, id) {
            u.ud.immune_attack = v > 0;
            unit::save(c, &u);
        }
    } else if is("ATKRANGE") {
        base(&|s| s.rhw.range = v as u16, false);
        calc_status = true;
    } else if is("ATKMIN") {
        base(&|s| s.rhw.atk = v as u16, false);
        calc_status = true;
    } else if is("ATKMAX") {
        base(&|s| s.rhw.atk2 = v as u16, false);
        calc_status = true;
    } else if is("MATKMIN") {
        base(&|s| s.matk_min = v as u16, false);
        calc_status = true;
    } else if is("MATKMAX") {
        base(&|s| s.matk_max = v as u16, false);
        calc_status = true;
    } else if is("DEF") {
        base(&|s| s.def = v as i16, false);
        calc_status = true;
    } else if is("MDEF") {
        base(&|s| s.mdef = v as i16, false);
        calc_status = true;
    } else if is("HIT") {
        base(&|s| s.hit = v as i16, false);
        calc_status = true;
    } else if is("FLEE") {
        base(&|s| s.flee = v as i16, false);
        calc_status = true;
    } else if is("PDODGE") {
        base(&|s| s.flee2 = v as i16, false);
        calc_status = true;
    } else if is("CRIT") {
        base(&|s| s.cri = v as i16, false);
        calc_status = true;
    } else if is("RACE") {
        if let Some(mut x) = super::get(c, id) {
            x.battle_status_mut().race = v as u8;
            x.base_status_mut().race = v as u8;
            super::put(c, &x);
        }
    } else if is("ELETYPE") {
        base(&|s| s.def_ele = v as u8, false);
        calc_status = true;
    } else if is("ELELEVEL") {
        base(&|s| s.ele_lv = v as u8, false);
        calc_status = true;
    } else if is("AMOTION") {
        base(&|s| s.amotion = v as i16 as u16, false);
        calc_status = true;
    } else if is("ADELAY") {
        base(&|s| s.adelay = v as i16 as u16, false);
        calc_status = true;
    } else if is("DMOTION") {
        base(&|s| s.dmotion = v as i16 as u16, false);
        calc_status = true;
    } else if is("TARGETID") {
        if v == 0 {
            if let Some(mut u) = unit::get(c, id) {
                unit::stop_attack(c, &mut u);
                unit::save(c, &u);
            }
        } else {
            let Some(target) = unit::get(c, v as u32) else {
                log::warn!("buildin_setunitdata: Error in finding target for {}!", if kind == crate::map::BL_MER { "BL_MER" } else { "BL_ELEM" });
                return SCRIPT_CMD_FAILURE;
            };
            if kind == crate::map::BL_ELEM {
                crate::elemental::elemental_change_mode(c, id, crate::elemental::data::EL_MODE_AGGRESSIVE);
            }
            crate::battle::unit_attack::unit_attack(c, id, target.id, 1);
        }
    } else if is("GROUP_ID") {
        if let Some(mut u) = unit::get(c, id) {
            u.ud.group_id = v;
            unit::save(c, &u);
            crate::unit_npc::unit_refresh(c, &u, false);
        }
    } else {
        log::error!("buildin_setunitdata: Unknown data identifier {t} for {}.", if kind == crate::map::BL_MER { "BL_MER" } else { "BL_ELEM" });
        return SCRIPT_CMD_FAILURE;
    }
    if calc_status {
        crate::status::calc_bl(c, id, crate::db::misc::status::status_db().get_scb_battle(), crate::status::SCO_NONE);
    }
    // Client information update
    match super::get(c, id) {
        Some(Comp::Mer(md)) => {
            if let Some(sd) = crate::pc::charid2sd(c, md.mercenary.char_id) {
                crate::mercenary::clif::clif_mercenary_info(c, sd.id);
                crate::mercenary::clif::clif_mercenary_skillblock(c, sd.id);
            }
        }
        Some(Comp::Elem(ed)) => crate::elemental::clif::clif_elemental_info(c, ed.master),
        None => {}
    }
    SCRIPT_CMD_SUCCESS
}
