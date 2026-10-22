//! status.cpp for mercenaries (BL_MER) and elementals (BL_ELEM): status_calc_mercenary_, status_calc_elemental_,
//! status_calc_misc / status_base_atk / status_base_atk_min / _max / status_base_matk_min / _max for the two types
//! (RENEWAL), status_calc_regen and status_calc_regen_rate (BL_REGEN), the non-player path of status_calc_bl_main with
//! the BL_MER / BL_ELEM arms (VIT / INT recalculating max HP / SP, the master's speed), status_calc_bl_'s client
//! updates (clif_mercenary_updatestatus / clif_elemental_updatestatus), and status_set_viewdata's BL_MER / BL_ELEM
//! arms. The per-stat status change modifiers are the shared ones of status/calc_sc.rs.

use super::Comp;
use crate::map::{BL_ELEM, BL_MER};
use crate::status::calc_sc::{self as scc, Who};
use crate::status::consts::*;
use crate::status::data::{RegenData, StatusData};
use crate::status::defs::*;
use crate::status::sc::{self, Sc};
use crate::unit;
use crate::world::Ctx;

fn has(flag: u64, scb: u32) -> bool {
    flag & (1u64 << scb) != 0
}

fn set(flag: &mut u64, scb: u32) {
    *flag |= 1u64 << scb;
}

/// status_get_speed(master): the master's battle speed.
fn master_speed(c: &Ctx, aid: u32) -> Option<u16> {
    crate::status::data::sd_get(c, aid).map(|sd| sd.battle_status.speed)
}

// ---------------------------------------------------------------------------------------------- base status

/// status.cpp status_calc_mercenary_: the first calculation copies the database status (walking and attacking, a
/// normal class, full HP / SP in the base status, the saved HP / SP in the battle status, the master's speed); every
/// calculation then fills the misc values at the database level and copies them to the battle status (status_cpy
/// keeps the battle HP / SP).
pub fn status_calc_mercenary_(c: &Ctx, md: &mut crate::mercenary::data::MercenaryData, opt: u8) -> i32 {
    let Some(db) = crate::mercenary::data::db_of(md) else { return 0 };
    let status = &mut md.base_status;
    if opt & SCO_FIRST != 0 {
        *status = crate::mob::status::status_from_db(&db.status);
        status.class_ = CLASS_NORMAL as u8;
        status.mode = MD_CANMOVE | MD_CANATTACK;
        status.hp = status.max_hp;
        status.sp = status.max_sp;
        md.battle_status.hp = md.mercenary.hp as u32;
        md.battle_status.sp = md.mercenary.sp as u32;
        if let Some(s) = master_speed(c, md.master) {
            md.base_status.speed = s;
        }
    }
    let lv = db.lv as i32;
    status_calc_misc(BL_MER, &mut md.base_status, &mut md.regen, lv);
    let base = md.base_status.clone();
    md.battle_status.cpy_from(&base);
    0
}

/// status.cpp status_calc_elemental_: the first calculation copies the database status, takes the elemental's mode
/// (EL_MODE_PASSIVE when none), runs status_calc_misc at level 0 and then sets the created elemental's own values
/// (max HP / SP, HP / SP, attack, MATK / DEF / MDEF on top, FLEE, HIT) and the master's speed, and copies it all to the
/// battle status; later calculations only redo the misc values (status_cpy). Without a master nothing happens.
pub fn status_calc_elemental_(c: &Ctx, ed: &mut crate::elemental::data::ElementalData, opt: u8) -> i32 {
    if crate::pc::get(c, ed.master).is_none() {
        return 0;
    }
    let Some(db) = crate::elemental::data::db_of(ed) else { return 0 };
    if opt & SCO_FIRST != 0 {
        let ele = ed.elemental.clone();
        let status = &mut ed.base_status;
        *status = crate::mob::status::status_from_db(&db.status);
        status.mode = if ele.mode == 0 { crate::elemental::data::EL_MODE_PASSIVE } else { ele.mode };
        status.class_ = CLASS_NORMAL as u8;
        status_calc_misc(BL_ELEM, status, &mut ed.regen, 0);
        let status = &mut ed.base_status;
        status.max_hp = ele.max_hp as u32;
        status.max_sp = ele.max_sp as u32;
        status.hp = ele.hp as u32;
        status.sp = ele.sp as u32;
        status.rhw.atk = ele.atk as u16;
        status.rhw.atk2 = ele.atk2 as u16;
        status.matk_min = status.matk_min.wrapping_add(ele.matk as u16);
        status.def = status.def.wrapping_add(ele.def);
        status.mdef = status.mdef.wrapping_add(ele.mdef);
        status.flee = ele.flee;
        status.hit = ele.hit;
        if let Some(s) = master_speed(c, ed.master) {
            status.speed = s;
        }
        ed.battle_status = ed.base_status.clone();
    } else {
        status_calc_misc(BL_ELEM, &mut ed.base_status, &mut ed.regen, 0);
        let base = ed.base_status.clone();
        ed.battle_status.cpy_from(&base);
    }
    0
}

/// status.cpp status_base_atk (RENEWAL) for BL_MER / BL_ELEM ("Others": str + level) when enable_baseatk_renewal
/// (0x29F) has the type.
fn status_base_atk(kind: u16, status: &StatusData, level: i32) -> i32 {
    if crate::conf::enable_baseatk_renewal() & kind == 0 {
        return 0;
    }
    cap_value(status.str as i32 + level, 0, u16::MAX as i32)
}

/// status.cpp status_base_matk_min / _max (RENEWAL) for BL_MER / BL_ELEM.
fn status_base_matk_min(status: &StatusData, level: i32) -> i32 {
    status.int_ as i32 + level + status.rhw.matk as i32 * 70 / 100
}
fn status_base_matk_max(status: &StatusData, level: i32) -> i32 {
    status.int_ as i32 + level + status.rhw.matk as i32 * 130 / 100
}

/// status.cpp status_calc_misc (RENEWAL) for BL_MER / BL_ELEM: the values are set (not stacked), mercenaries have
/// their own FLEE / DEF2 / MDEF2 formulas, the attack variance is 80% / 120% of the database ATK, critical only for the
/// types of enable_critical (17: BL_PC | BL_MER), perfect flee only for enable_perfect_flee (1: BL_PC), the base attack
/// of status_base_atk, then status_calc_regen (BL_REGEN).
pub fn status_calc_misc(kind: u16, status: &mut StatusData, regen: &mut RegenData, level: i32) {
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
    // (bl->type != BL_MOB)
    status.res = 0;
    status.mres = 0;
    let cap = |v: i32, lo: i32| cap_value(v, lo, i16::MAX as i32) as i16;
    // Hit
    let mut stat = status.hit as i32;
    stat += level + status.dex as i32 + 150;
    stat += 2 * status.con as i32;
    status.hit = cap(stat, 1);
    // Flee
    stat = status.flee as i32;
    stat += level + status.agi as i32 + 100;
    stat += 2 * status.con as i32;
    status.flee = cap(stat, 1);
    // Def2
    if kind == BL_MER {
        stat = (status.vit as f32 + (level as f32 / 10.0) + (status.vit as f32 / 5.0)) as i32;
    } else {
        stat = status.def2 as i32;
        stat += ((level as f32 + status.vit as f32) / 2.0) as i32;
    }
    status.def2 = cap(stat, 0);
    // Mdef2
    if kind == BL_MER {
        stat = ((level as f32 / 10.0) + (status.int_ as f32 / 5.0)) as i32;
    } else {
        stat = status.mdef2 as i32;
        stat += ((status.int_ as i32 + level) as f32 / 4.0) as i32;
    }
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
    // ATK: status_base_atk_max / status_base_atk_min (80% / 120% of the database ATK)
    status.rhw.atk2 = (status.rhw.atk as i32 * 120 / 100) as u16;
    status.rhw.atk = (status.rhw.atk as i32 * 80 / 100) as u16;
    // MAtk
    status.matk_min = status_base_matk_min(status, level) as u16;
    status.matk_max = status_base_matk_max(status, level) as u16;
    //Critical
    if kind & crate::conf::enable_critical() != 0 {
        stat = status.cri as i32;
        stat += level / 10; // (every 10 BaseLevel = +0.1 critical)
        stat += 10 + (status.luk as i32 * 3); // (every 1 luk = +0.3 critical)
        status.cri = cap(stat, 1);
    } else {
        status.cri = 0;
    }
    if kind & crate::conf::enable_perfect_flee() != 0 {
        stat = status.flee2 as i32 + status.luk as i32 + 10; // (every 10 luk = +1 perfect flee)
        status.flee2 = cap(stat, 0);
    } else {
        status.flee2 = 0;
    }
    status.batk += status_base_atk(kind, status, level);
    if status.cri != 0 {
        // default (not BL_MOB / BL_PC): battle_config.critical_rate (battle.conf: 100)
        let critical_rate: i32 = crate::battle_config::get(crate::battle_config::Bc::CRITICAL_RATE);
        if critical_rate != 100 {
            status.cri = cap_value(status.cri as i32 * critical_rate / 100, 1, i16::MAX as i32) as i16;
        }
        if status.cri == 0 && critical_rate != 0 {
            status.cri = 10;
        }
    }
    // (bl->type & BL_REGEN)
    status_calc_regen(kind, status, regen);
}

/// status.cpp status_calc_regen for BL_MER / BL_ELEM (no player rates or skills): the generic values, then both types'
/// own formulas replace them.
pub fn status_calc_regen(kind: u16, status: &StatusData, regen: &mut RegenData) {
    let reg_flag = 1; // bl->type != BL_PC
    let mut val: i32 = (status.vit as i32 / 5) + 1.max(status.max_hp as i32 / 200);
    regen.hp = cap_value(val, reg_flag, i16::MAX as i32) as u16;
    val = 1 + (status.int_ as i32 / 6) + (status.max_sp as i32 / 100);
    if status.int_ >= 120 {
        val += ((status.int_ as i32 - 120) / 2) + 4;
    }
    regen.sp = cap_value(val, reg_flag, i16::MAX as i32) as u16;
    if kind == BL_MER || kind == BL_ELEM {
        // (status->max_hp * status->vit: uint32 * uint16 in unsigned arithmetic, then / 10000.0)
        let v = ((status.max_hp.wrapping_mul(status.vit as u16 as u32)) as f64 / 10000.0 + 1.0) * 6.0;
        regen.hp = cap_value(v as i32, 1, i16::MAX as i32) as u16;
        let v = (status.max_sp as f64 * (status.int_ as f64 + 10.0) / 750.0) + 1.0;
        regen.sp = cap_value(v as i32, 1, i16::MAX as i32) as u16;
    }
}

/// status.cpp status_calc_regen_rate for BL_MER / BL_ELEM (no sregen / ssregen): the status changes that stop or
/// speed up regeneration, the elementals' insignia bonus.
pub fn status_calc_regen_rate(kind: u16, class: i32, regen: &mut RegenData, sc: &Sc) {
    use crate::elemental::data::*;
    regen.flag = RGN_HP | RGN_SP;
    regen.rate_hp = 100;
    regen.rate_sp = 100;
    if sc.empty() {
        return;
    }
    // No HP or SP regen
    if (sc.has(SC_POISON) && !sc.has(SC_SLOWPOISON))
        || (sc.has(SC_DPOISON) && !sc.has(SC_SLOWPOISON))
        || sc.has(SC_BERSERK)
        || sc.has(SC_TRICKDEAD)
        || sc.has(SC_BLEEDING)
        || (sc.has(SC_MAGICMUSHROOM) && sc.v3(SC_MAGICMUSHROOM) == 1)
        || sc.has(SC_SATURDAYNIGHTFEVER)
        || sc.has(SC_REBOUND)
        || sc.has(SC_NORECOVER_STATE)
    {
        regen.flag = RGN_NONE;
    }
    // No natural SP regen (the Monk arm is for players)
    if sc.has(SC_DANCING) || sc.has(SC_MAXIMIZEPOWER) || (sc.has(SC_OBLIVIONCURSE) && sc.v3(SC_OBLIVIONCURSE) == 1) {
        regen.flag &= !RGN_SP;
    }
    if sc.has(SC_TENSIONRELAX) {
        if regen.overweight {
            regen.overweight = false; // 1x HP regen
        } else {
            regen.rate_hp += 200;
        }
    }
    if sc.has(SC_MAGNIFICAT) {
        regen.rate_sp += 100;
    }
    if let Some(sce) = sc.get(SC_REGENERATION) {
        if sce.val4 == 0 {
            regen.rate_hp = (regen.rate_hp as i32 + sce.val2 * 100) as u16;
            regen.rate_sp = (regen.rate_sp as i32 + sce.val3 * 100) as u16;
        } else {
            regen.flag &= !(sce.val4 as u8); // Remove regen as specified by val4
        }
    }
    if sc.has(SC_GT_REVITALIZE) {
        regen.hp = (regen.hp as i32 + cap_value(regen.hp as i32 * sc.v3(SC_GT_REVITALIZE) / 100, 1, i16::MAX as i32)) as u16;
        regen.walk = true;
    }
    if kind == BL_ELEM {
        // Recovery bonus only applies to the Elementals.
        let insignia = match class {
            ELEMENTALID_AGNI_S | ELEMENTALID_AGNI_M | ELEMENTALID_AGNI_L | ELEMENTALID_ARDOR => Some(SC_FIRE_INSIGNIA),
            ELEMENTALID_AQUA_S | ELEMENTALID_AQUA_M | ELEMENTALID_AQUA_L | ELEMENTALID_DILUVIO => Some(SC_WATER_INSIGNIA),
            ELEMENTALID_VENTUS_S | ELEMENTALID_VENTUS_M | ELEMENTALID_VENTUS_L | ELEMENTALID_PROCELLA => Some(SC_WIND_INSIGNIA),
            ELEMENTALID_TERA_S | ELEMENTALID_TERA_M | ELEMENTALID_TERA_L | ELEMENTALID_TERREMOTUS | ELEMENTALID_SERPENS => Some(SC_EARTH_INSIGNIA),
            _ => None,
        };
        if let Some(t) = insignia {
            if sc.has(t) && sc.v1(t) == 1 {
                regen.rate_hp += 100;
            }
        }
    }
    if sc.has(SC_CATNIPPOWDER) {
        regen.rate_hp = regen.rate_hp.wrapping_mul(2);
        regen.rate_sp = regen.rate_sp.wrapping_mul(2);
    }
    if sc.has(SC_SHRIMPBLESSING) {
        regen.rate_sp += 50;
    }
    if sc.has(SC_NIBELUNGEN) {
        if sc.v2(SC_NIBELUNGEN) == RINGNBL_HPREGEN {
            regen.rate_hp += 100;
        } else if sc.v2(SC_NIBELUNGEN) == RINGNBL_SPREGEN {
            regen.rate_sp += 100;
        }
    }
    if sc.has(SC_SIRCLEOFNATURE) {
        regen.rate_hp = (regen.rate_hp as i32 + sc.v2(SC_SIRCLEOFNATURE)) as u16;
    }
    if sc.has(SC_SONGOFMANA) {
        regen.rate_sp = (regen.rate_sp as i32 + sc.v3(SC_SONGOFMANA)) as u16;
    }
}

// ---------------------------------------------------------------------------------------------- status_calc_bl_

/// status.cpp status_calc_bl_ for BL_MER / BL_ELEM: the base status when SCB_BASE is asked for, status_calc_bl_main,
/// then the client updates to the master (clif_mercenary_updatestatus / clif_elemental_updatestatus for each changed
/// value).
pub fn status_calc_bl(c: &Ctx, id: u32, flag: u64, opt: u8) {
    let Some(mut x) = super::get(c, id) else { return };
    // Previous battle status
    let b_status = x.battle_status().clone();
    if has(flag, SCB_BASE) {
        match &mut x {
            Comp::Mer(md) => {
                status_calc_mercenary_(c, md, opt);
            }
            Comp::Elem(ed) => {
                status_calc_elemental_(c, ed, opt);
            }
        }
        super::put(c, &x);
    }
    status_calc_bl_main(c, id, flag);
    let Some(x) = super::get(c, id) else { return };
    let status = x.battle_status();
    let master = x.master();
    if crate::pc::get(c, master).is_none() {
        return;
    }
    match x {
        Comp::Mer(_) => {
            use crate::mercenary::clif::clif_mercenary_updatestatus as up;
            if b_status.rhw.atk != status.rhw.atk || b_status.rhw.atk2 != status.rhw.atk2 {
                up(c, master, SP_ATK1);
            }
            if b_status.matk_max != status.matk_max {
                up(c, master, SP_MATK1);
            }
            if b_status.hit != status.hit {
                up(c, master, SP_HIT);
            }
            if b_status.cri != status.cri {
                up(c, master, SP_CRITICAL);
            }
            if b_status.def != status.def {
                up(c, master, SP_DEF1);
            }
            if b_status.mdef != status.mdef {
                up(c, master, SP_MDEF1);
            }
            if b_status.flee != status.flee {
                up(c, master, SP_MERCFLEE);
            }
            if b_status.amotion != status.amotion {
                up(c, master, SP_ASPD);
            }
            if b_status.max_hp != status.max_hp {
                up(c, master, SP_MAXHP);
            }
            if b_status.max_sp != status.max_sp {
                up(c, master, SP_MAXSP);
            }
            if b_status.hp != status.hp {
                up(c, master, SP_HP);
            }
            if b_status.sp != status.sp {
                up(c, master, SP_SP);
            }
        }
        Comp::Elem(_) => {
            use crate::elemental::clif::clif_elemental_updatestatus as up;
            if b_status.max_hp != status.max_hp {
                up(c, master, SP_MAXHP);
            }
            if b_status.max_sp != status.max_sp {
                up(c, master, SP_MAXSP);
            }
            if b_status.hp != status.hp {
                up(c, master, SP_HP);
            }
            if b_status.sp != status.sp {
                up(c, master, SP_SP);
            }
        }
    }
}

/// status.cpp status_calc_bl_main, the path a mercenary or an elemental takes (not a player, not a homunculus).
pub fn status_calc_bl_main(c: &Ctx, id: u32, flag: u64) {
    let Some(mut x) = super::get(c, id) else { return };
    let Some(u) = unit::get(c, id) else { return };
    let kind = x.kind();
    let b = x.base_status().clone();
    let scl = sc::load(c, id);
    let w = Who { c, kind, id, map: u.map, pc: None };
    let mut flag = flag;
    // This needs to be done even if there is currently no status change active, because we need to update the
    // speed on the client when the last status change ends.
    if has(flag, SCB_SPEED) {
        // Re-walk to adjust speed
        if let Some(mut u) = unit::get(c, id) {
            u.ud.change_walk_target = true;
            unit::save(c, &u);
        }
    }
    let lv = x.level();
    let master = x.master();
    let class = x.class();
    let (st, regen) = match &mut x {
        Comp::Mer(m) => (&mut m.battle_status, &mut m.regen),
        Comp::Elem(e) => (&mut e.battle_status, &mut e.regen),
    };
    if has(flag, SCB_STR) {
        st.str = scc::status_calc_str(&w, &scl, b.str as i32) as i16;
        set(&mut flag, SCB_BATK);
    }
    if has(flag, SCB_AGI) {
        st.agi = scc::status_calc_agi(&w, &scl, b.agi as i32) as i16;
        set(&mut flag, SCB_FLEE);
        set(&mut flag, SCB_DEF2);
    }
    if has(flag, SCB_VIT) {
        st.vit = scc::status_calc_vit(&w, &scl, b.vit as i32) as i16;
        set(&mut flag, SCB_DEF2);
        set(&mut flag, SCB_MDEF2);
        // bl.type & (BL_PC|BL_HOM|BL_MER|BL_ELEM)
        set(&mut flag, SCB_MAXHP);
    }
    if has(flag, SCB_INT) {
        st.int_ = scc::status_calc_int(&w, &scl, b.int_ as i32) as i16;
        set(&mut flag, SCB_MATK);
        set(&mut flag, SCB_MDEF2);
        set(&mut flag, SCB_MAXSP);
    }
    if has(flag, SCB_DEX) {
        st.dex = scc::status_calc_dex(&w, &scl, b.dex as i32) as i16;
        set(&mut flag, SCB_BATK);
        set(&mut flag, SCB_HIT);
        set(&mut flag, SCB_MATK);
        set(&mut flag, SCB_MDEF2);
    }
    if has(flag, SCB_LUK) {
        st.luk = scc::status_calc_luk(&w, &scl, b.luk as i32) as i16;
        set(&mut flag, SCB_BATK);
        set(&mut flag, SCB_CRI);
        set(&mut flag, SCB_FLEE2);
        set(&mut flag, SCB_MATK);
        set(&mut flag, SCB_HIT);
        set(&mut flag, SCB_FLEE);
    }
    if has(flag, SCB_POW) {
        st.pow = scc::status_calc_pow(&w, &scl, b.pow as i32) as i16;
        set(&mut flag, SCB_BATK);
        set(&mut flag, SCB_PATK);
    }
    if has(flag, SCB_STA) {
        st.sta = scc::status_calc_sta(&w, &scl, b.sta as i32) as i16;
        set(&mut flag, SCB_RES);
    }
    if has(flag, SCB_WIS) {
        st.wis = scc::status_calc_wis(&w, &scl, b.wis as i32) as i16;
        set(&mut flag, SCB_MRES);
    }
    if has(flag, SCB_SPL) {
        st.spl = scc::status_calc_spl(&w, &scl, b.spl as i32) as i16;
        set(&mut flag, SCB_MATK);
        set(&mut flag, SCB_SMATK);
    }
    if has(flag, SCB_CON) {
        st.con = scc::status_calc_con(&w, &scl, b.con as i32) as i16;
        set(&mut flag, SCB_HIT);
        set(&mut flag, SCB_FLEE);
        set(&mut flag, SCB_PATK);
        set(&mut flag, SCB_SMATK);
    }
    if has(flag, SCB_CRT) {
        st.crt = scc::status_calc_crt(&w, &scl, b.crt as i32) as i16;
        set(&mut flag, SCB_HPLUS);
        set(&mut flag, SCB_CRATE);
    }
    if has(flag, SCB_BATK) && b.batk != 0 {
        st.batk = status_base_atk(kind, st, lv);
        // Calculate the difference of old and new base attack in the base status, then add the same difference
        // to the base attack of battle status
        let temp = b.batk - status_base_atk(kind, &b, lv);
        st.batk += temp;
        st.batk = scc::status_calc_batk(&w, &scl, st.batk);
    }
    if has(flag, SCB_WATK) {
        if b.watk == 0 {
            // We only have left-hand weapon
            st.watk = 0;
            st.watk2 = scc::status_calc_watk(&w, &scl, b.watk2 as i32);
        } else {
            st.watk = scc::status_calc_watk(&w, &scl, b.watk as i32);
        }
    }
    if has(flag, SCB_HIT) {
        if st.dex == b.dex && st.luk == b.luk && st.con == b.con {
            st.hit = scc::status_calc_hit(&w, &scl, b.hit as i32);
        } else {
            st.hit = scc::status_calc_hit(&w, &scl, b.hit as i32 + (st.dex as i32 - b.dex as i32) + (st.luk as i32 / 3 - b.luk as i32 / 3) + 2 * (st.con as i32 - b.con as i32));
        }
    }
    if has(flag, SCB_FLEE) {
        if st.agi == b.agi && st.luk == b.luk && st.con == b.con {
            st.flee = scc::status_calc_flee(&w, &scl, b.flee as i32);
        } else {
            st.flee = scc::status_calc_flee(&w, &scl, b.flee as i32 + (st.agi as i32 - b.agi as i32) + (st.luk as i32 / 5 - b.luk as i32 / 5) + 2 * (st.con as i32 - b.con as i32));
        }
    }
    if has(flag, SCB_DEF) {
        st.def = scc::status_calc_def(&w, &scl, b.def as i32);
    }
    if has(flag, SCB_DEF2) {
        let vit = st.vit as i32;
        if st.vit == b.vit && st.agi == b.agi {
            st.def2 = scc::status_calc_def2(&w, &scl, b.def2 as i32, vit);
        } else {
            let d = ((st.vit as f32 / 2.0 - b.vit as f32 / 2.0) + (st.agi as f32 / 5.0 - b.agi as f32 / 5.0)) as i32;
            st.def2 = scc::status_calc_def2(&w, &scl, b.def2 as i32 + d, vit);
        }
    }
    if has(flag, SCB_MDEF) {
        st.mdef = scc::status_calc_mdef(&w, &scl, b.mdef as i32);
    }
    if has(flag, SCB_MDEF2) {
        if st.int_ == b.int_ && st.vit == b.vit && st.dex == b.dex {
            st.mdef2 = scc::status_calc_mdef2(&w, &scl, b.mdef2 as i32);
        } else {
            let d = ((st.dex as f32 / 5.0 - b.dex as f32 / 5.0) + (st.vit as f32 / 5.0 - b.vit as f32 / 5.0)) as i32;
            st.mdef2 = scc::status_calc_mdef2(&w, &scl, b.mdef2 as i32 + (st.int_ as i32 - b.int_ as i32) + d);
        }
    }
    if has(flag, SCB_SPEED) {
        st.speed = scc::status_calc_speed(&w, &scl, b.speed as i32);
        // BL_MER / BL_ELEM: `if (master != nullptr) status->speed = status_get_speed(master)`
        if let Some(s) = master_speed(c, master) {
            st.speed = s;
        }
    }
    if has(flag, SCB_CRI) && b.cri != 0 {
        if st.luk == b.luk {
            st.cri = scc::status_calc_critical(&w, &scl, b.cri as i32);
        } else {
            st.cri = scc::status_calc_critical(&w, &scl, b.cri as i32 + 3 * (st.luk as i32 - b.luk as i32));
        }
    }
    if has(flag, SCB_FLEE2) && b.flee2 != 0 {
        if st.luk == b.luk {
            st.flee2 = scc::status_calc_flee2(&w, &scl, b.flee2 as i32);
        } else {
            st.flee2 = scc::status_calc_flee2(&w, &scl, b.flee2 as i32 + (st.luk as i32 - b.luk as i32));
        }
    }
    if has(flag, SCB_ATK_ELE) {
        st.rhw.ele = scc::status_calc_attack_element(&scl, b.rhw.ele as i32);
        st.lhw.ele = scc::status_calc_attack_element(&scl, b.lhw.ele as i32);
    }
    if has(flag, SCB_DEF_ELE) {
        st.def_ele = scc::status_calc_element(&w, &scl, b.def_ele as i32);
        st.ele_lv = scc::status_calc_element_lv(&w, &scl, b.ele_lv as i32);
    }
    let mut stop_attack = false;
    let mut stop_walking = false;
    if has(flag, SCB_MODE) {
        st.mode = scc::status_calc_mode(&w, &scl, b.mode);
        let hm = |m: i32| (st.mode & m) == m;
        st.class_ = if hm(MD_STATUSIMMUNE | MD_SKILLIMMUNE) {
            CLASS_BATTLEFIELD as u8
        } else if hm(MD_STATUSIMMUNE | MD_KNOCKBACKIMMUNE | MD_DETECTOR) {
            CLASS_BOSS as u8
        } else if hm(MD_STATUSIMMUNE) {
            CLASS_GUARDIAN as u8
        } else {
            CLASS_NORMAL as u8
        };
        // Since mode changed, reset their state.
        stop_attack = st.mode & MD_CANATTACK != MD_CANATTACK;
        stop_walking = st.mode & MD_CANMOVE != MD_CANMOVE;
    }
    // No status changes alter these yet: SCB_SIZE, SCB_RACE, SCB_RANGE.
    if has(flag, SCB_MAXHP) {
        st.max_hp = scc::status_calc_maxhp(&w, &scl, b.max_hp as u64);
        if st.hp > st.max_hp {
            // !FIXME: Should perhaps a status_zap should be issued?
            st.hp = st.max_hp;
        }
    }
    if has(flag, SCB_MAXSP) {
        st.max_sp = scc::status_calc_maxsp(&w, &scl, b.max_sp as u64);
        if st.sp > st.max_sp {
            st.sp = st.max_sp;
        }
    }
    if has(flag, SCB_MATK) {
        // MATK = StatusMATK + WeaponMATK + ExtraMATK
        let mut matk_min = status_base_matk_min(st, lv);
        let mut matk_max = status_base_matk_max(st, lv);
        // Apply Recognized Spell buff
        if scl.has(SC_RECOGNIZEDSPELL) {
            matk_min = matk_min.max(matk_max);
        }
        // Apply MATK % from skill Mystical Amplification
        if scl.has(SC_MAGICPOWER) {
            matk_min += matk_min * scl.v3(SC_MAGICPOWER) / 100;
            matk_max += matk_max * scl.v3(SC_MAGICPOWER) / 100;
        }
        // PseudoBuffMATK (flat MATK from skills)
        matk_min = scc::status_calc_pseudobuff_matk(&w, &scl, matk_min) as i32;
        matk_max = scc::status_calc_pseudobuff_matk(&w, &scl, matk_max) as i32;
        // ConsumableMATK (flat MATK from consumables)
        matk_min = scc::status_calc_consumablematk(&scl, matk_min) as i32;
        matk_max = scc::status_calc_consumablematk(&scl, matk_max) as i32;
        st.matk_min = cap_value(matk_min, 0, u16::MAX as i32) as u16;
        st.matk_max = cap_value(matk_max, 0, u16::MAX as i32) as u16;
    }
    if has(flag, SCB_ASPD) {
        // Mercenary and mobs
        let mut amotion = b.amotion as i32;
        st.aspd_rate = scc::status_calc_aspd_rate(&w, &scl, b.aspd_rate as i32);
        amotion = amotion * st.aspd_rate as i32 / 1000;
        amotion = scc::status_calc_fix_aspd(&w, &scl, amotion) as i32;
        st.amotion = cap_value(amotion, MAX_ASPD_NOPC / AMOTION_DIVIDER_NOPC, MIN_ASPD / AMOTION_DIVIDER_NOPC) as u16;
        // FIXME: Officially, adelay only considers a few buffs and is not affected by ASPD debuffs at all
        let temp = b.adelay as i32 * (st.aspd_rate as i32).min(1000) / 1000;
        st.adelay = cap_value(temp, AMOTION_DIVIDER_NOPC * st.amotion as i32, MIN_ASPD) as u16;
    }
    // SCB_DSPD: players and homunculi only
    if has(flag, SCB_PATK) {
        if st.pow == b.pow && st.con == b.con {
            st.patk = scc::status_calc_patk(&w, &scl, b.patk as i32);
        } else {
            st.patk = scc::status_calc_patk(&w, &scl, b.patk as i32 + (st.pow as i32 - b.pow as i32) / 3 + (st.con as i32 - b.con as i32) / 5);
        }
    }
    if has(flag, SCB_SMATK) {
        if st.spl == b.spl && st.con == b.con {
            st.smatk = scc::status_calc_smatk(&w, &scl, b.smatk as i32);
        } else {
            // (rAthena adds the stat difference outside of status_calc_smatk here)
            st.smatk = (scc::status_calc_smatk(&w, &scl, b.smatk as i32) as i32 + (st.spl as i32 - b.spl as i32) / 3 + (st.con as i32 - b.con as i32) / 5) as i16;
        }
    }
    if has(flag, SCB_RES) {
        if st.sta == b.sta {
            st.res = scc::status_calc_res(&w, &scl, b.res as i32);
        } else {
            st.res = scc::status_calc_res(&w, &scl, b.res as i32 + (st.sta as i32 - b.sta as i32) + (st.sta as i32 - b.sta as i32) / 3 * 5);
        }
    }
    if has(flag, SCB_MRES) {
        if st.wis == b.wis {
            st.mres = scc::status_calc_mres(&w, &scl, b.mres as i32);
        } else {
            st.mres = scc::status_calc_mres(&w, &scl, b.mres as i32 + (st.wis as i32 - b.wis as i32) + (st.wis as i32 - b.wis as i32) / 3 * 5);
        }
    }
    if has(flag, SCB_HPLUS) {
        if st.crt == b.crt {
            st.hplus = scc::status_calc_hplus(&w, &scl, b.hplus as i32);
        } else {
            st.hplus = scc::status_calc_hplus(&w, &scl, b.hplus as i32 + (st.crt as i32 - b.crt as i32));
        }
    }
    if has(flag, SCB_CRATE) {
        if st.crt == b.crt {
            st.crate_ = scc::status_calc_crate(&w, &scl, b.crate_ as i32);
        } else {
            st.crate_ = scc::status_calc_crate(&w, &scl, b.crate_ as i32 + (st.crt as i32 - b.crt as i32) / 3);
        }
    }
    if has(flag, SCB_MAXAP) {
        st.max_ap = scc::status_calc_maxap(&w, &scl, b.max_ap as u64);
        if st.ap > st.max_ap {
            st.ap = st.max_ap;
        }
    }
    if has(flag, SCB_VIT) || has(flag, SCB_MAXHP) || has(flag, SCB_INT) || has(flag, SCB_MAXSP) {
        let st2 = st.clone();
        status_calc_regen(kind, &st2, regen);
    }
    if has(flag, SCB_REGEN) {
        status_calc_regen_rate(kind, class, regen, &scl);
    }
    super::put(c, &x);
    if stop_attack || stop_walking {
        if let Some(mut u) = unit::get(c, id) {
            if stop_attack {
                unit::stop_attack(c, &mut u);
            }
            if stop_walking {
                unit::stop_walking(c, &mut u, unit::USW_FIXPOS, 0);
            }
            unit::save(c, &u);
        }
    }
}

// ---------------------------------------------------------------------------------------------- view data

/// status.cpp status_set_viewdata's BL_MER / BL_ELEM arms: the view data of the class (mob / NPC / mercenary /
/// elemental database, in rAthena's lookup order); none: an error and the old view data stays.
pub fn status_set_viewdata(c: &Ctx, id: u32, class_: i32) {
    let Some(mut u) = unit::get(c, id) else { return };
    let vd = if crate::db::mobs::mobdb_checkid(class_) != 0 || crate::mob::rtdb::mob_is_clone(c, class_) != 0 {
        crate::mob::rtdb::mob_get_viewdata(c, class_).map(crate::mob::status::view_from_db)
    } else if crate::unit_npc::npcdb_checkid(class_) {
        Some(crate::npc::npc_viewdata(class_))
    } else if crate::db::misc::homunculus::homdb_checkid(class_) {
        // hom_get_viewdata: hom_viewdb[class_ - HM_CLASS_BASE], whose only field is look[LOOK_BASE] = the class
        // (homunculus.cpp do_init_homunculus). The mercenary classes 6017-6046 are inside the homunculus range
        // (HM_CLASS_BASE 6001 .. HM_CLASS_MAX 6052), so a mercenary takes this view, not mercenary_db's empty one.
        Some(crate::unit::ViewData { class: class_, ..Default::default() })
    } else if crate::db::misc::mercenary::mercenary_db().exists(class_) {
        crate::db::misc::mercenary::mercenary_db().find(class_).map(|m| crate::mob::status::view_from_db(&m.vd))
    } else if crate::db::misc::elemental::elemental_db().exists(class_) {
        crate::db::misc::elemental::elemental_get_viewdata(class_).map(crate::mob::status::view_from_db)
    } else {
        None
    };
    match vd {
        Some(vd) => {
            u.vd = vd;
            unit::save(c, &u);
        }
        None => {
            if u.kind == BL_MER {
                log::error!("status_set_viewdata (MERCENARY): No view data for class {class_}");
            } else {
                log::error!("status_set_viewdata (ELEMENTAL): No view data for class {class_}");
            }
        }
    }
}
