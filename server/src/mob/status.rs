//! Monster status (status.cpp for BL_MOB): status_calc_mob_ (the base status of a monster from its database entry,
//! level, size, master), status_calc_misc for non-players, status_calc_slave_mode, status_set_viewdata's BL_MOB
//! branch, and the status_get_* values of monsters that other systems (combat, scripts) read.

use super::{conf, get, put, MobData, AI_ATTACK, AI_SPHERE};
use crate::db::mobs::{self, StatusData as DbStatus, MD_AGGRESSIVE, MD_CANATTACK, MD_CANMOVE, MD_NORANDOMWALK};
use crate::map::{BL_MOB, BL_PC};
use crate::status::data::{StatusData, WeaponAtk};
use crate::status::defs::{cap_value, SCO_FIRST};
use crate::unit::{self, ViewData};
use crate::world::Ctx;

/// mmo.hpp MIN_WALK_SPEED.
pub const MIN_WALK_SPEED: u16 = 20;
/// mmo.hpp MAX_LEVEL.
pub const MAX_LEVEL: i32 = 275;

/// The database status (db/mobs.rs, after loadingFinished) as a runtime status_data.
pub fn status_from_db(s: &DbStatus) -> StatusData {
    let w = |a: &mobs::WeaponAtk| WeaponAtk { atk: a.atk, atk2: a.atk2, range: a.range, ele: a.ele, matk: a.matk, wlv: a.wlv };
    StatusData {
        hp: s.hp,
        sp: s.sp,
        ap: s.ap,
        max_hp: s.max_hp,
        max_sp: s.max_sp,
        max_ap: s.max_ap,
        str: s.str,
        agi: s.agi,
        vit: s.vit,
        int_: s.int_,
        dex: s.dex,
        luk: s.luk,
        pow: s.pow,
        sta: s.sta,
        wis: s.wis,
        spl: s.spl,
        con: s.con,
        crt: s.crt,
        eatk: s.eatk,
        batk: s.batk,
        watk: s.watk,
        watk2: s.watk2,
        matk_min: s.matk_min,
        matk_max: s.matk_max,
        speed: s.speed,
        amotion: s.amotion,
        clientamotion: s.clientamotion,
        adelay: s.adelay,
        dmotion: s.dmotion,
        mode: s.mode,
        hit: s.hit,
        flee: s.flee,
        cri: s.cri,
        flee2: s.flee2,
        def2: s.def2,
        mdef2: s.mdef2,
        aspd_rate2: s.aspd_rate2,
        aspd_rate: s.aspd_rate,
        patk: s.patk,
        smatk: s.smatk,
        res: s.res,
        mres: s.mres,
        hplus: s.hplus,
        crate_: s.crate_,
        def: s.def,
        mdef: s.mdef,
        def_ele: s.def_ele,
        ele_lv: s.ele_lv,
        size: s.size,
        race: s.race,
        class_: s.class_,
        rhw: w(&s.rhw),
        lhw: w(&s.lhw),
    }
}

/// unit.hpp view_data from the database's look array.
pub fn view_from_db(vd: &mobs::ViewData) -> ViewData {
    use crate::db::mobs::{LOOK_BASE, LOOK_BODY, LOOK_BODY2, LOOK_CLOTHES_COLOR, LOOK_HAIR, LOOK_HAIR_COLOR, LOOK_HEAD_BOTTOM, LOOK_HEAD_MID, LOOK_HEAD_TOP, LOOK_ROBE, LOOK_SHIELD, LOOK_SHOES, LOOK_WEAPON};
    ViewData {
        class: vd.look[LOOK_BASE],
        hair: vd.look[LOOK_HAIR],
        weapon: vd.look[LOOK_WEAPON],
        head_bottom: vd.look[LOOK_HEAD_BOTTOM],
        head_top: vd.look[LOOK_HEAD_TOP],
        head_mid: vd.look[LOOK_HEAD_MID],
        hair_color: vd.look[LOOK_HAIR_COLOR],
        clothes_color: vd.look[LOOK_CLOTHES_COLOR],
        shield: vd.look[LOOK_SHIELD],
        shoes: vd.look[LOOK_SHOES],
        body: vd.look[LOOK_BODY],
        robe: vd.look[LOOK_ROBE],
        body2: vd.look[LOOK_BODY2],
        sex: vd.sex as u8,
        dead_sit: vd.dead_sit,
    }
}

/// status.cpp status_set_viewdata, BL_MOB branch: a monster class takes that monster's view data; a player class
/// makes the monster's own copy (mob_set_dynamic_viewdata) with that class as base and body.
pub fn status_set_viewdata(c: &Ctx, md: &mut MobData, class_: i32) {
    let vd = if mobs::mobdb_checkid(class_) != 0 || super::rtdb::mob_is_clone(c, class_) != 0 { super::rtdb::mob_get_viewdata(c, class_) } else { None };
    // npc_get_viewdata for an NPC class (npc_viewdb: only look[LOOK_BASE]; script2: setunitdata UMOB_CLASS with an
    // NPC sprite); homdb_checkid: hom_get_viewdata (homunculus.rs); mercenary_db.exists: mercenary_get_viewdata;
    // elemental_db.exists: elemental_get_viewdata (status.cpp:9383-9394, in that order)
    let npc_vd = if vd.is_none() && crate::unit_npc::npcdb_checkid(class_) {
        Some(crate::npc::npc_viewdata(class_))
    } else if vd.is_none() && crate::db::misc::homunculus::homdb_checkid(class_) {
        crate::homunculus::hom_get_viewdata(class_)
    } else if vd.is_none() && crate::db::misc::mercenary::mercenary_db().exists(class_) {
        crate::db::misc::mercenary::mercenary_db().find(class_).map(|m| view_from_db(&m.vd))
    } else if vd.is_none() && crate::db::misc::elemental::elemental_db().exists(class_) {
        crate::db::misc::elemental::elemental_get_viewdata(class_).map(view_from_db)
    } else {
        None
    };
    let Some(mut u) = unit::get(c, md.id) else { return };
    if let Some(vd) = vd {
        // mob_free_dynamic_viewdata
        md.vd_changed = false;
        u.vd = view_from_db(vd);
    } else if let Some(vd) = npc_vd {
        // mob_free_dynamic_viewdata; md->vd = npc_get_viewdata(class_)
        md.vd_changed = false;
        u.vd = vd;
    } else if crate::clif::pcdb_checkid(class_) {
        // mob_set_dynamic_viewdata (a copy of the current view data)
        md.vd_changed = true;
        u.vd.class = class_;
        u.vd.body2 = class_;
        u.vd.hair = u.vd.hair.clamp(crate::conf::min_hair_style(), crate::conf::max_hair_style());
        u.vd.hair_color = u.vd.hair_color.clamp(crate::conf::min_hair_color(), crate::conf::max_hair_color());
    } else {
        log::error!("status_set_viewdata (MOB): No view data for class {class_}");
    }
    unit::save(c, &u);
}

/// status.cpp status_get_base_status for BL_MOB: md->base_status, or the database status.
pub fn base_status(c: &Ctx, md: &MobData) -> StatusData {
    match &md.base_status {
        Some(b) => b.clone(),
        None => super::db(c, md).map(|d| status_from_db(&d.status)).unwrap_or_default(),
    }
}

// ---------------------------------------------------------------------------------------------- status_calc_mob

/// status.hpp status_calc_mob(md, opt) = status_calc_bl_(md, SCB_ALL, opt) for a monster: status_calc_mob_, then (not
/// on the first calculation: "Assume there will be no statuses active") the status-change part
/// ([`status_calc_bl_main_mob`]).
pub fn status_calc_mob(c: &Ctx, id: u32, opt: u8) {
    let Some(mut md) = get(c, id) else { return };
    status_calc_mob_(c, &mut md, opt);
    put(c, &md);
    if opt & SCO_FIRST != 0 {
        return; // Assume there will be no statuses active
    }
    status_calc_bl_main_mob(c, id, u64::MAX);
}

/// status.cpp status_calc_mob_: the base status of a monster (level-ups, size, guardians, slaves' speed and mode,
/// special summons). Returns 1 when a custom base status was made.
pub fn status_calc_mob_(c: &Ctx, md: &mut MobData, opt: u8) -> i32 {
    let Some(db) = super::db(c, md) else { return 0 };
    if opt & SCO_FIRST != 0 {
        // Set basic level on respawn.
        if md.level > 0 && md.level <= MAX_LEVEL && md.level != db.lv as i32 {
        } else {
            md.level = db.lv as i32;
        }
        if let Some(mut u) = unit::get(c, md.id) {
            u.ud.title = super::spawn::truncate_name(&db.title);
            u.ud.group_id = db.group_id;
            unit::save(c, &u);
        }
        md.damagetaken = db.damagetaken;
    }
    let mut flag = 0;
    // Check if we need custom base-status
    if conf::mobs_level_up() && md.level > db.lv as i32 {
        flag |= 1;
    }
    if md.special_size != 0 {
        flag |= 2;
    }
    if crate::woe::guardian::guardup_lv(c, md.id) != 0 {
        flag |= 4; // md->guardian_data && md->guardian_data->guardup_lv
    }
    if md.mob_id == mobs::MOBID_EMPERIUM as i32 {
        flag |= 4;
    }
    if conf::slaves_inherit_speed() != 0 && md.master_id != 0 {
        flag |= 8;
    }
    if md.master_id != 0 && md.special_ai > AI_ATTACK {
        flag |= 16;
    }
    if md.master_id != 0 && conf::slaves_inherit_mode() != 0 {
        flag |= 32;
    }
    if flag == 0 {
        // No special status required.
        md.base_status = None;
        if opt & SCO_FIRST != 0 {
            md.status = status_from_db(&db.status);
        }
        return 0;
    }
    let mut status = status_from_db(&db.status);
    let mbl = if flag & (8 | 16) != 0 { unit::get(c, md.master_id) } else { None };
    if flag & 8 != 0 {
        if let Some(m) = &mbl {
            let mstatus = base_status_of(c, m);
            let canmove = mstatus.mode & MD_CANMOVE == MD_CANMOVE;
            if md.special_ai != AI_SPHERE && conf::slaves_inherit_speed() & (if canmove { 1 } else { 2 }) != 0 {
                status.speed = mstatus.speed;
            }
            if status.speed < MIN_WALK_SPEED {
                status.speed = MIN_WALK_SPEED;
            }
        }
    }
    if flag & 32 != 0 {
        // status_calc_slave_mode works on the monster's battle status through status changes
        put(c, md);
        status_calc_slave_mode(c, md.id);
        if let Some(fresh) = get(c, md.id) {
            *md = fresh;
        }
    }
    if flag & 1 != 0 {
        // Increase from mobs leveling up [Valaris]
        let diff = md.level - db.lv as i32;
        status.str = (status.str as i32 + diff) as i16;
        status.agi = (status.agi as i32 + diff) as i16;
        status.vit = (status.vit as i32 + diff) as i16;
        status.int_ = (status.int_ as i32 + diff) as i16;
        status.dex = (status.dex as i32 + diff) as i16;
        status.luk = (status.luk as i32 + diff) as i16;
        status.max_hp = status.max_hp.wrapping_add((diff * status.vit as i32) as u32);
        status.max_sp = status.max_sp.wrapping_add((diff * status.int_ as i32) as u32);
        status.hp = status.max_hp;
        status.sp = status.max_sp;
        status.speed = status.speed.wrapping_sub(cap_value(diff, 0, status.speed as i32 - 10) as u16);
    }
    if flag & 2 != 0 && conf::mob_size_influence() {
        // Change for sized monsters [Valaris]
        if md.special_size as i32 == crate::status::defs::SZ_MEDIUM {
            status.max_hp /= 2;
            status.max_sp /= 2;
            if status.max_hp == 0 {
                status.max_hp = 1;
            }
            if status.max_sp == 0 {
                status.max_sp = 1;
            }
            status.hp = status.max_hp;
            status.sp = status.max_sp;
            for v in [&mut status.str, &mut status.agi, &mut status.vit, &mut status.int_, &mut status.dex, &mut status.luk] {
                *v /= 2;
                if *v == 0 {
                    *v = 1;
                }
            }
        } else if md.special_size as i32 == crate::status::defs::SZ_BIG {
            status.max_hp = status.max_hp.wrapping_mul(2);
            status.max_sp = status.max_sp.wrapping_mul(2);
            status.hp = status.max_hp;
            status.sp = status.max_sp;
            for v in [&mut status.str, &mut status.agi, &mut status.vit, &mut status.int_, &mut status.dex, &mut status.luk] {
                *v = v.wrapping_mul(2);
            }
        }
    }
    status_calc_misc_mob(&mut status, md.level);
    if flag & 4 != 0 {
        // Strengthen Guardians - custom value +10% / lv (woe/guardian.rs)
        let m = unit::get(c, md.id).map(|u| u.map).unwrap_or(0);
        crate::woe::guardian::status_calc_mob_castle(c, md, m, &mut status);
    }
    if flag & 16 != 0 {
        if let Some(m) = &mbl {
            status_calc_mob_special_summon(c, md, m, &mut status);
        }
    }
    if opt & SCO_FIRST != 0 {
        // Initial battle status
        md.status = status.clone();
    }
    md.base_status = Some(status);
    1
}

/// status_calc_mob_'s `flag&16` branch: the Max HP (and more) of summons made by skills (Summon Flora / Marine
/// Sphere, Zanzou, Legion, Silver Sniper, Magic Decoy, ABRs, Bionics), from the master's skill in use.
fn status_calc_mob_special_summon(c: &Ctx, md: &mut MobData, mbl: &unit::Unit, status: &mut StatusData) {
    use crate::db::skills::ids::*;
    // Remove special AI when this is used by regular mobs.
    if mbl.kind == BL_MOB && get(c, mbl.id).map(|m| m.special_ai == 0).unwrap_or(false) {
        md.special_ai = super::AI_NONE;
    }
    // Different levels of HP according to skill level
    let (mut skill_id, skill_lv) = (mbl.ud.skill_id, mbl.ud.skill_lv as i32);
    if skill_id == 0 {
        // !FIXME: We lost the unit data for magic decoy in somewhere before this
        // `ud->skill_id = ((TBL_PC*)mbl)->menuskill_id`: written into the master's unit_data (rAthena casts any master
        // to a player here; only a player master has a menu skill, the others read garbage: players only)
        if mbl.kind == crate::map::BL_PC {
            skill_id = crate::itemui::menu(c, mbl.id).0 as u16;
            if let Some(mut u) = unit::get(c, mbl.id) {
                u.ud.skill_id = skill_id;
                unit::save(c, &u);
            }
        }
    }
    let mstatus = status_of(c, mbl);
    let mlv = crate::status::status_get_lv(c, mbl);
    match skill_id {
        AM_SPHEREMINE => status.max_hp = (2000 + 400 * skill_lv) as u32,
        KO_ZANZOU => status.max_hp = (3000 + 3000 * skill_lv) as u32,
        AM_CANNIBALIZE => {
            status.max_hp = (1500 + 200 * skill_lv + 10 * mlv) as u32;
            status.mode |= MD_CANATTACK | MD_AGGRESSIVE;
        }
        MH_SUMMON_LEGION => {
            status.max_hp = (10 * (100 * (skill_lv + 2) + mlv)) as u32;
            status.batk = 100 * (skill_lv + 5) / 2;
            status.def = (10 * (100 * (skill_lv + 2) + mlv)) as i16;
        }
        NC_SILVERSNIPER => {
            status.max_hp = ((1000 * skill_lv) as u32).wrapping_add(mstatus.hp / 3).wrapping_add((mlv * 12) as u32);
            status.batk = 200 * skill_lv;
        }
        NC_MAGICDECOY => {
            // `((TBL_PC*)mbl)->menuskill_val` (skill_magicdecoy zeroed it before the spawn: rAthena's own order)
            let val = if mbl.kind == crate::map::BL_PC { crate::itemui::menu(c, mbl.id).1 } else { 0 };
            status.max_hp = ((1000 * val) as u32).wrapping_add(mstatus.sp.wrapping_mul(4)).wrapping_add((mlv * 12) as u32);
            status.matk_min = (250 + 50 * val) as u16;
            status.matk_max = status.matk_min;
        }
        MT_SUMMON_ABR_BATTLE_WARIOR | MT_SUMMON_ABR_DUAL_CANNON | MT_SUMMON_ABR_MOTHER_NET | MT_SUMMON_ABR_INFINITY => {
            if mbl.kind == BL_PC {
                let abr_mastery = crate::skill::checkskill(c, mbl.id, MT_ABR_M) as i32;
                status.max_hp = ((5000 + 40000 * abr_mastery) * mstatus.vit as i32 / 100) as u32;
                status.rhw.atk = ((2 * mstatus.batk + 200 + 600 * abr_mastery) * 70 / 100) as u16;
                status.rhw.atk2 = (2 * mstatus.batk + 200 + 600 * abr_mastery) as u16;
                status.def = (mstatus.def as i32 + 20 * abr_mastery) as i16;
                status.mdef = (mstatus.mdef as i32 + 4 * abr_mastery) as i16;
                status.hit = (mstatus.hit as i32 + 5 * abr_mastery / 2) as i16;
                status.flee = (mstatus.flee as i32 + 10 * abr_mastery) as i16;
                status.speed = mstatus.speed;
                if skill_id == MT_SUMMON_ABR_INFINITY {
                    status.max_hp += 20000;
                    status.rhw.atk += 1400; // 70% of 2000
                    status.rhw.atk2 += 2000;
                }
            }
        }
        BO_WOODENWARRIOR | BO_WOODEN_FAIRY | BO_CREEPER | BO_HELLTREE => {
            if mbl.kind == BL_PC {
                let bionic_mastery = crate::skill::checkskill(c, mbl.id, BO_BIONICS_M) as i32;
                status.max_hp = ((5000 + 40000 * bionic_mastery) * mstatus.vit as i32 / 100) as u32;
                status.rhw.atk = ((2 * mstatus.batk + 600 * bionic_mastery) * 70 / 100) as u16;
                status.rhw.atk2 = (2 * mstatus.batk + 600 * bionic_mastery) as u16;
                status.def = (mstatus.def as i32 + 20 * bionic_mastery) as i16;
                status.mdef = (mstatus.mdef as i32 + 4 * bionic_mastery) as i16;
                status.hit = (mstatus.hit as i32 + 5 * bionic_mastery / 2) as i16;
                status.flee = (mstatus.flee as i32 + 10 * bionic_mastery) as i16;
                status.speed = mstatus.speed;
                if skill_id == BO_HELLTREE {
                    status.max_hp += 20000;
                    status.rhw.atk += 1400; // 70% of 2000
                    status.rhw.atk2 += 2000;
                }
            }
        }
        _ => {}
    }
    status.hp = status.max_hp;
}

/// status.cpp status_calc_misc (RENEWAL) for a monster: the values derived from its stats and level. The database
/// loader already ran it once (MobDatabase::loadingFinished); status_calc_mob_ runs it again on its copy, so the
/// 80% / 120% attack variance is applied a second time there, as in rAthena.
pub fn status_calc_misc_mob(status: &mut StatusData, level: i32) {
    // Non players get the value set, players need to stack with previous bonuses.
    status.batk = 0;
    status.hit = 0;
    status.flee = 0;
    status.def2 = 0;
    status.mdef2 = 0;
    status.cri = 0;
    status.flee2 = 0;
    status.patk = 0;
    status.smatk = 0;
    status.hplus = 0;
    status.crate_ = 0;
    // (BL_MOB keeps res / mres: they are set when loading mob_db)
    let cap = |v: i32, lo: i32| cap_value(v, lo, i16::MAX as i32) as i16;
    // Hit
    let mut stat = status.hit as i32 + level + status.dex as i32 + 150;
    stat += 2 * status.con as i32;
    status.hit = cap(stat, 1);
    // Flee
    stat = status.flee as i32 + level + status.agi as i32 + 100;
    stat += 2 * status.con as i32;
    status.flee = cap(stat, 1);
    // Def2
    stat = status.def2 as i32 + ((level as f32 + status.vit as f32) / 2.0) as i32;
    status.def2 = cap(stat, 0);
    // Mdef2
    stat = status.mdef2 as i32 + ((status.int_ as i32 + level) as f32 / 4.0) as i32;
    status.mdef2 = cap(stat, 0);
    // PAtk
    stat = status.patk as i32 + status.pow as i32 / 3 + status.con as i32 / 5;
    status.patk = cap(stat, 0);
    // SMatk
    stat = status.smatk as i32 + status.spl as i32 / 3 + status.con as i32 / 5;
    status.smatk = cap(stat, 0);
    // Res
    stat = status.res as i32 + status.sta as i32 + status.sta as i32 / 3 * 5;
    status.res = cap(stat, 0);
    // Mres
    stat = status.mres as i32 + status.wis as i32 + status.wis as i32 / 3 * 5;
    status.mres = cap(stat, 0);
    // HPlus
    stat = status.hplus as i32 + status.crt as i32;
    status.hplus = cap(stat, 0);
    // CRate
    stat = status.crate_ as i32 + status.crt as i32 / 3;
    status.crate_ = cap(stat, 0);
    // ATK: status_base_atk_max / status_base_atk_min for BL_MOB (120% / 80% of rhw.atk)
    status.rhw.atk2 = (status.rhw.atk as i32 * 120 / 100) as u16;
    status.rhw.atk = (status.rhw.atk as i32 * 80 / 100) as u16;
    // MAtk: status_base_matk_min / _max for BL_MOB
    status.matk_min = (status.int_ as i32 + level + status.rhw.matk as i32 * 70 / 100) as u16;
    status.matk_max = (status.int_ as i32 + level + status.rhw.matk as i32 * 130 / 100) as u16;
    // Critical: battle.conf enable_critical 17 (BL_PC | BL_MER) does not include BL_MOB
    if BL_MOB & crate::conf::enable_critical() != 0 {
        stat = status.cri as i32 + level / 10 + 10 + status.luk as i32 * 3;
        status.cri = cap(stat, 1);
    } else {
        status.cri = 0;
    }
    // Perfect flee: enable_perfect_flee 1 (BL_PC) does not include BL_MOB
    if BL_MOB & crate::conf::enable_perfect_flee() != 0 {
        stat = status.flee2 as i32 + status.luk as i32 + 10;
        status.flee2 = cap(stat, 0);
    } else {
        status.flee2 = 0;
    }
    // status_base_atk (RENEWAL, others): str + level when enable_baseatk_renewal has BL_MOB
    if BL_MOB & crate::conf::enable_baseatk_renewal() != 0 {
        status.batk += cap_value(status.str as i32 + level, 0, u16::MAX as i32);
    }
    if status.cri != 0 {
        // BL_MOB: mob_critical_rate
        if conf::mob_critical_rate() != 100 {
            status.cri = cap_value(status.cri as i32 * conf::mob_critical_rate() / 100, 1, i16::MAX as i32) as i16;
        }
        if status.cri == 0 && conf::mob_critical_rate() != 0 {
            status.cri = 10;
        }
    }
    // (monsters have no regen data)
}

/// status.cpp status_calc_slave_mode (slaves_inherit_mode) through SC_MODECHANGE.
pub fn status_calc_slave_mode(c: &Ctx, id: u32) {
    use crate::status::consts::SC_MODECHANGE;
    use crate::status::sc::sc_start4;
    let Some(md) = get(c, id) else { return };
    let Some(u) = unit::get(c, id) else { return };
    match conf::slaves_inherit_mode() {
        1 => {
            // Always aggressive
            if !super::has_mode(&md, MD_AGGRESSIVE) {
                sc_start4(c, None, &u, SC_MODECHANGE, 100, 1, 0, MD_AGGRESSIVE, 0, 0);
            }
        }
        2 => {
            // Always passive
            if super::has_mode(&md, MD_AGGRESSIVE) {
                sc_start4(c, None, &u, SC_MODECHANGE, 100, 1, 0, 0, MD_AGGRESSIVE, 0);
            }
        }
        4 => {
            // Overwrite with slave mode
            sc_start4(c, None, &u, SC_MODECHANGE, 100, 1, MD_CANMOVE | MD_NORANDOMWALK | MD_CANATTACK, 0, 0, 0);
        }
        _ => {
            // Copy master
            let master_aggressive = unit::get(c, md.master_id).map(|m| status_of(c, &m).mode & MD_AGGRESSIVE == MD_AGGRESSIVE).unwrap_or(false);
            if master_aggressive {
                sc_start4(c, None, &u, SC_MODECHANGE, 100, 1, 0, MD_AGGRESSIVE, 0, 0);
            } else {
                sc_start4(c, None, &u, SC_MODECHANGE, 100, 1, 0, 0, MD_AGGRESSIVE, 0);
            }
        }
    }
}

// ---------------------------------------------------------------------------------------------- status_calc_bl_main

/// status.cpp status_calc_bl_main for a monster: the status changes' effects on its battle status (the SCB_* parts
/// in `flag`). Status changes on monsters come from skills and combat effects.
pub fn status_calc_bl_main_mob(c: &Ctx, id: u32, flag: u64) {
    super::status_bl::status_calc_bl_main_mob(c, id, flag);
}

// ---------------------------------------------------------------------------------------------- status_get_*

/// status.cpp status_get_status_data for any unit, with the monster branch (md->status).
pub fn status_of(c: &Ctx, u: &unit::Unit) -> StatusData {
    crate::status::status_get_status_data(c, u)
}

/// status.cpp status_get_base_status for any unit, with the monster branch.
pub fn base_status_of(c: &Ctx, u: &unit::Unit) -> StatusData {
    crate::status::status_get_base_status(c, u)
}

/// status.cpp status_get_class for BL_MOB: the view class (vd->look[LOOK_BASE]).
pub fn status_get_class(c: &Ctx, md: &MobData) -> i32 {
    unit::get(c, md.id).map(|u| u.vd.class).unwrap_or(md.mob_id)
}

/// status.cpp status_get_race2 for BL_MOB (md->db->race2).
pub fn status_get_race2(c: &Ctx, md: &MobData) -> Vec<u8> {
    super::db(c, md).map(|d| d.race2.clone()).unwrap_or_default()
}

/// status.cpp status_get_party_id for BL_MOB: a slave's master's party (special-AI summons of a player), else
/// -master_id; 0 for free monsters.
pub fn status_get_party_id(c: &Ctx, md: &MobData) -> i32 {
    if md.master_id > 0 {
        if md.special_ai != 0 {
            if let Some(p) = crate::pc::get(c, md.master_id) {
                let _ = p;
                return crate::party::party_id_of(c, md.master_id) as i32;
            }
        }
        return -(md.master_id as i32);
    }
    0
}

/// status.cpp status_get_guild_id for BL_MOB: a guardian's guild (woe/guardian.rs) or a special-AI summon's
/// master's guild, else 0.
pub fn status_get_guild_id(c: &Ctx, md: &MobData) -> i32 {
    if let Some(gd) = crate::woe::guardian::get(c, md.id) {
        return gd.guild_id; // Guardian's guild [Skotlex]
    }
    if md.special_ai != 0 && crate::pc::get(c, md.master_id).is_some() {
        if let Some(u) = unit::get(c, md.master_id) {
            return u.guild_id as i32;
        }
    }
    0
}

/// status.cpp status_get_emblem_id for BL_MOB: a guardian's emblem (woe/guardian.rs) or a special-AI summon's
/// master's emblem, else 0.
pub fn status_get_emblem_id(c: &Ctx, md: &MobData) -> i32 {
    if let Some(gd) = crate::woe::guardian::get(c, md.id) {
        return gd.emblem_id; // Guardian's guild [Skotlex]
    }
    if md.special_ai != 0 && crate::pc::get(c, md.master_id).is_some() {
        if let Some(u) = unit::get(c, md.master_id) {
            return u.emblem_id;
        }
    }
    0
}

/// status.cpp status_isendure's RENEWAL rule: bosses (Class Boss) cannot be stopped by damage.
pub fn is_class_boss(md: &MobData) -> bool {
    md.status.class_ == mobs::CLASS_BOSS
}
