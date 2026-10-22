//! status.cpp for BL_HOM: status_calc_homunculus_ (the base status from hd->homunculus), status_calc_misc's
//! homunculus formulas (RENEWAL), status_base_atk / _min / _max and status_base_matk_min / _max for BL_HOM, the
//! status_get_hom* macros, status_calc_regen's homunculus part, status_set_viewdata's BL_HOM branch, and
//! status_calc_bl_ for a homunculus (the base status, status_calc_bl_main's BL_HOM path, the client update).

use super::data::{self, HomData};
use crate::db::skills::ids::*;
use crate::status::consts::*;
use crate::status::data::{RegenData, StatusData};
use crate::status::defs::*;
use crate::world::Ctx;

fn cap_value(v: i32, lo: i32, hi: i32) -> i32 {
    v.clamp(lo, hi)
}

/// status.hpp status_get_homstr … status_get_homluk: the stat of `status` plus hd->homunculus.*_value.
pub fn homstr(hd: &HomData, s: &StatusData) -> i32 {
    s.str as i32 + hd.homunculus.str_value
}
pub fn homagi(hd: &HomData, s: &StatusData) -> i32 {
    s.agi as i32 + hd.homunculus.agi_value
}
pub fn homvit(hd: &HomData, s: &StatusData) -> i32 {
    s.vit as i32 + hd.homunculus.vit_value
}
pub fn homint(hd: &HomData, s: &StatusData) -> i32 {
    s.int_ as i32 + hd.homunculus.int_value
}
pub fn homdex(hd: &HomData, s: &StatusData) -> i32 {
    s.dex as i32 + hd.homunculus.dex_value
}
pub fn homluk(hd: &HomData, s: &StatusData) -> i32 {
    s.luk as i32 + hd.homunculus.luk_value
}

/// status.cpp status_base_atk (RENEWAL) for BL_HOM: 2 × level + status_get_homstr (when enable_baseatk_renewal has
/// BL_HOM).
pub fn status_base_atk_hom(hd: &HomData, s: &StatusData, level: i32) -> u16 {
    if crate::conf::enable_baseatk_renewal() & crate::map::BL_HOM == 0 {
        return 0;
    }
    let str = 2 * level + homstr(hd, s);
    cap_value(str, 0, u16::MAX as i32) as u16
}

/// status.cpp status_base_atk_min (RENEWAL) for BL_HOM: (homstr + homdex) / 5.
pub fn status_base_atk_min_hom(hd: &HomData, s: &StatusData) -> u16 {
    ((homstr(hd, s) + homdex(hd, s)) / 5) as u16
}

/// status.cpp status_base_atk_max (RENEWAL) for BL_HOM: (homluk + homstr + homdex) / 3.
pub fn status_base_atk_max_hom(hd: &HomData, s: &StatusData) -> u16 {
    ((homluk(hd, s) + homstr(hd, s) + homdex(hd, s)) / 3) as u16
}

/// status.cpp status_base_matk_min (RENEWAL) for BL_HOM: homint + level + (homint + homdex) / 5.
pub fn status_base_matk_min_hom(hd: &HomData, s: &StatusData, level: i32) -> u16 {
    (homint(hd, s) + level + (homint(hd, s) + homdex(hd, s)) / 5) as u16
}

/// status.cpp status_base_matk_max (RENEWAL) for BL_HOM: homint + level + (homluk + homint + homdex) / 3.
pub fn status_base_matk_max_hom(hd: &HomData, s: &StatusData, level: i32) -> u16 {
    (homint(hd, s) + level + (homluk(hd, s) + homint(hd, s) + homdex(hd, s)) / 3) as u16
}

/// status.cpp status_calc_misc for BL_HOM (RENEWAL): def2 / mdef2 / def / mdef / hit / flee from the hom* stats, the
/// base atk range, matk, critical (enable_critical has BL_HOM), perfect flee (enable_perfect_flee does not), base atk,
/// the critical rate and the regen values.
pub fn status_calc_misc_hom(hd: &mut HomData, level: i32) {
    let snapshot = hd.clone();
    let st = &mut hd.base_status;
    // Non players get the value set, players need to stack with previous bonuses.
    st.batk = 0;
    st.hit = 0;
    st.flee = 0;
    st.def2 = 0;
    st.mdef2 = 0;
    st.cri = 0;
    st.flee2 = 0;
    st.patk = 0;
    st.smatk = 0;
    st.hplus = 0;
    st.crate_ = 0;
    // (bl->type != BL_MOB)
    st.res = 0;
    st.mres = 0;
    let s = st.clone();
    let h = &snapshot;
    // Def2
    let stat = homvit(h, &s) + homagi(h, &s) / 2;
    st.def2 = cap_value(stat, 0, i16::MAX as i32) as i16;
    // Mdef2
    let stat = (homvit(h, &s) + homint(h, &s)) / 2;
    st.mdef2 = cap_value(stat, 0, i16::MAX as i32) as i16;
    // Def
    let stat = st.def as i32 + homvit(h, &s) + level / 2;
    st.def = cap_value(stat, 0, i16::MAX as i32) as i16;
    // Mdef
    let stat = ((homvit(h, &s) as f32 + level as f32) / 4.0 + homint(h, &s) as f32 / 2.0) as i32;
    st.mdef = cap_value(stat, 0, i16::MAX as i32) as i16;
    // Hit
    let stat = level + st.dex as i32 + 150;
    st.hit = cap_value(stat, 1, i16::MAX as i32) as i16;
    // Flee
    let stat = level + homagi(h, &s);
    st.flee = cap_value(stat, 1, i16::MAX as i32) as i16;
    // ATK
    let s = st.clone();
    st.rhw.atk2 = status_base_atk_max_hom(h, &s);
    st.rhw.atk = status_base_atk_min_hom(h, &s);
    // MAtk
    st.matk_min = status_base_matk_min_hom(h, &s, level);
    st.matk_max = status_base_matk_max_hom(h, &s, level);
    //Critical
    if crate::map::BL_HOM & crate::conf::enable_critical() != 0 {
        let stat = st.cri as i32 + level / 10 + 10 + st.luk as i32 * 3; // (every 10 BaseLevel = +0.1 critical) (every 1 luk = +0.3 critical)
        st.cri = cap_value(stat, 1, i16::MAX as i32) as i16;
    } else {
        st.cri = 0;
    }
    if crate::map::BL_HOM & crate::conf::enable_perfect_flee() != 0 {
        let stat = st.flee2 as i32 + st.luk as i32 + 10; // (every 10 luk = +1 perfect flee)
        st.flee2 = cap_value(stat, 0, i16::MAX as i32) as i16;
    } else {
        st.flee2 = 0;
    }
    let s = st.clone();
    st.batk += status_base_atk_hom(h, &s, level) as i32;
    if st.cri != 0 {
        // default (not BL_MOB / BL_PC): battle_config.critical_rate (battle.conf `critical_rate: 100`)
        let critical_rate: i32 = crate::battle_config::get(crate::battle_config::Bc::CRITICAL_RATE);
        if critical_rate != 100 {
            st.cri = cap_value(st.cri as i32 * critical_rate / 100, 1, i16::MAX as i32) as i16;
        }
        if st.cri == 0 && critical_rate != 0 {
            st.cri = 10;
        }
    }
    // bl->type & BL_REGEN: status_calc_regen(bl, status, status_get_regen_data(bl))
    let s = hd.base_status.clone();
    let mut regen = hd.regen.clone();
    status_calc_regen_hom(hd, &s, &mut regen);
    hd.regen = regen;
}

/// status.cpp status_calc_regen for a homunculus: the natural HP / SP amounts (reg_flag 1: at least 1), Adamantium
/// Skin and Brain Surgery.
pub fn status_calc_regen_hom(hd: &HomData, status: &StatusData, regen: &mut RegenData) {
    let val = (status.vit as i32 / 5) + (status.max_hp as i32 / 200).max(1);
    regen.hp = cap_value(val, 1, i16::MAX as i32) as u16;
    let mut val = 1 + (status.int_ as i32 / 6) + (status.max_sp as i32 / 100);
    if status.int_ >= 120 {
        val += ((status.int_ as i32 - 120) / 2) + 4;
    }
    regen.sp = cap_value(val, 1, i16::MAX as i32) as u16;
    let skill = super::hom_checkskill(Some(hd), HAMI_SKIN) as i32;
    if skill > 0 {
        let val = regen.hp as i32 * (100 + 5 * skill) / 100;
        regen.hp = cap_value(val, 1, i16::MAX as i32) as u16;
    }
    let skill = super::hom_checkskill(Some(hd), HLIF_BRAIN) as i32;
    if skill > 0 {
        let val = regen.sp as i32 * (100 + 3 * skill) / 100;
        regen.sp = cap_value(val, 1, i16::MAX as i32) as u16;
    }
}

/// status.cpp status_calc_homunculus_: the base status from hd->homunculus (stats / 10, the level stat weight, on the
/// first calculation the database's element / race / size / mode and speed, ASPD from AttackDelay, max HP / SP, the
/// skill tree and the passive skills), status_calc_misc, then battle_status = base_status (status_cpy: HP / SP kept,
/// from hd->homunculus on the first calculation).
pub fn status_calc_homunculus_(c: &Ctx, hd: &mut HomData, opt: u8) {
    let Some(db) = hd.db() else { return };
    let hom = hd.homunculus.clone();
    {
        let st = &mut hd.base_status;
        st.str = (hom.str / 10) as i16;
        st.agi = (hom.agi / 10) as i16;
        st.vit = (hom.vit / 10) as i16;
        st.dex = (hom.dex / 10) as i16;
        st.int_ = (hom.int_ / 10) as i16;
        st.luk = (hom.luk / 10) as i16;
    }
    // APPLY_HOMUN_LEVEL_STATWEIGHT()
    super::apply_homun_level_statweight(&mut hd.homunculus);
    let hom = hd.homunculus.clone();
    if opt & SCO_FIRST != 0 {
        let has_master = data::master_sd(c, hd).is_some();
        let master_speed = crate::unit::get(c, hd.master).map(|u| crate::unit::speed(&u) as u16);
        let st = &mut hd.base_status;
        st.def_ele = db.element as u8;
        st.ele_lv = 1;
        st.race = db.race as u8;
        st.class_ = CLASS_NORMAL as u8;
        st.size = if hom.class_ as i32 == db.evo_class { db.evo_size } else { db.base_size };
        st.rhw.range = 1 + st.size as u16;
        st.mode = MD_CANMOVE | MD_CANATTACK;
        st.speed = DEFAULT_WALK_SPEED as u16;
        if super::hom_setting() & data::HOMSET_COPY_SPEED != 0 {
            if let Some(sp) = master_speed.filter(|_| has_master) {
                st.speed = sp;
            }
        }
        st.hp = 1;
        st.sp = 1;
    }
    let st = &mut hd.base_status;
    st.aspd_rate = 1000;
    // RENEWAL
    let mut amotion = db.base_aspd as i32;
    amotion = amotion - amotion * (st.dex as i32 + hom.dex_value) / 1000 - (st.agi as i32 + hom.agi_value) * amotion / 250;
    st.def = 0;
    st.mdef = 0;
    st.amotion = cap_value(amotion, MAX_ASPD_NOPC / AMOTION_DIVIDER_NOPC, MIN_ASPD / AMOTION_DIVIDER_NOPC) as u16;
    st.adelay = (AMOTION_DIVIDER_NOPC * st.amotion as i32) as u16; //It seems adelay = amotion for Homunculus.
    st.max_hp = hom.max_hp;
    st.max_sp = hom.max_sp;
    // hom_calc_skilltree(hd): the tree here, its packet once the row is saved (status_calc_bl_hom)
    super::hom_calc_skilltree_data(hd);
    let chk = |hd: &HomData, id: u16| super::hom_checkskill(Some(hd), id) as i32;
    let skill_lv = chk(hd, HAMI_SKIN);
    if skill_lv > 0 {
        hd.base_status.def += (skill_lv * 4) as i16;
    }
    let skill_lv = chk(hd, HVAN_INSTRUCT);
    if skill_lv > 0 {
        const BONUS_INT: [i16; 5] = [1, 2, 2, 4, 5];
        const BONUS_STR: [i16; 5] = [1, 1, 3, 4, 4];
        let i = (skill_lv - 1).clamp(0, 4) as usize;
        hd.base_status.int_ += BONUS_INT[i];
        hd.base_status.str += BONUS_STR[i];
    }
    let skill_lv = chk(hd, HAMI_SKIN);
    if skill_lv > 0 {
        let st = &mut hd.base_status;
        st.max_hp = st.max_hp.wrapping_add((skill_lv as u32 * 2).wrapping_mul(st.max_hp) / 100);
    }
    let skill_lv = chk(hd, HLIF_BRAIN);
    if skill_lv > 0 {
        let st = &mut hd.base_status;
        st.max_sp = st.max_sp.wrapping_add((skill_lv as u32).wrapping_mul(st.max_sp) / 100);
    }
    let skill_lv = chk(hd, MH_CLASSY_FLUTTER);
    if skill_lv > 0 {
        let st = &mut hd.base_status;
        st.matk_min = st.matk_min.wrapping_add((100 + 60 * skill_lv) as u16);
        st.matk_max = st.matk_max.wrapping_add((100 + 60 * skill_lv) as u16);
    }
    let skill_lv = chk(hd, MH_BRUSHUP_CLAW);
    if skill_lv > 0 {
        hd.base_status.batk += 100 + 60 * skill_lv;
    }
    let skill_lv = chk(hd, MH_POLISHING_NEEDLE);
    if skill_lv > 0 {
        let st = &mut hd.base_status;
        st.matk_min = st.matk_min.wrapping_add((50 + 20 * skill_lv) as u16);
        st.matk_max = st.matk_max.wrapping_add((50 + 20 * skill_lv) as u16);
        st.batk += 100 + 40 * skill_lv;
    }
    let skill_lv = chk(hd, MH_LICHT_GEHORN);
    if skill_lv > 0 {
        let st = &mut hd.base_status;
        st.matk_min = st.matk_min.wrapping_add((100 + 30 * skill_lv) as u16);
        st.matk_max = st.matk_max.wrapping_add((100 + 30 * skill_lv) as u16);
        st.batk += 100 + 30 * skill_lv;
    }
    let skill_lv = chk(hd, MH_BLAZING_LAVA);
    if skill_lv > 0 {
        hd.base_status.batk += 100 + 60 * skill_lv;
    }
    let mut eleanor = false;
    if opt & SCO_FIRST != 0 {
        hd.battle_status.hp = hom.hp;
        hd.battle_status.sp = hom.sp;
        eleanor = hom.class_ as i32 == data::MER_ELEANOR; // Eleanor
    }
    status_calc_misc_hom(hd, hom.level as i32);
    // status_cpy(&hd->battle_status, status)
    let b = hd.base_status.clone();
    hd.battle_status.cpy_from(&b);
    if eleanor {
        // sc_start(hd, hd, SC_STYLE_CHANGE, 100, MH_MD_FIGHTING, INFINITE_TICK): after the row is saved
        // (status_calc_homunculus_'s caller saves it: see status_calc_bl_hom)
        ELEANOR_PENDING.with(|p| p.borrow_mut().push(hd.id));
    }
}

thread_local! {
    /// Eleanors whose first calculation starts SC_STYLE_CHANGE (the status change needs the saved row).
    static ELEANOR_PENDING: std::cell::RefCell<Vec<u32>> = const { std::cell::RefCell::new(Vec::new()) };
}

/// status.hpp status_calc_homunculus(hd, opt) = status_calc_bl_(hd, SCB_ALL, opt).
pub fn status_calc_homunculus(c: &Ctx, hd_id: u32, opt: u8) {
    status_calc_bl_hom(c, hd_id, crate::db::misc::status::status_db().get_scb_all(), opt);
}

/// status.cpp status_calc_bl_ for BL_HOM: the base status when SCB_BASE is asked for (status_calc_homunculus_),
/// status_calc_bl_main's BL_HOM path, then (not on the first calculation: "Client update handled by caller") the
/// master's homunculus window when anything changed.
pub fn status_calc_bl_hom(c: &Ctx, hd_id: u32, flag: u64, opt: u8) {
    let Some(mut hd) = data::get(c, hd_id) else { return };
    // Previous battle status
    let b_status = hd.battle_status.clone();
    if flag & (1u64 << SCB_BASE) != 0 {
        status_calc_homunculus_(c, &mut hd, opt);
        data::put(c, &hd);
        // hom_calc_skilltree's clif_homskillinfoblock (status_calc_homunculus_ calls hom_calc_skilltree before the
        // Eleanor's SC_STYLE_CHANGE; the skill tree was computed on the row: hom_calc_skilltree_data)
        super::clif::clif_homskillinfoblock(c, hd_id);
        let pending: Vec<u32> = ELEANOR_PENDING.with(|p| std::mem::take(&mut *p.borrow_mut()));
        for id in pending {
            if let Some(u) = crate::unit::get(c, id) {
                crate::status::sc::sc_start(c, Some(&u), &u, SC_STYLE_CHANGE, 100, data::MH_MD_FIGHTING, crate::status::sc::INFINITE_TICK);
            }
        }
    }
    status_calc_bl_main_hom(c, hd_id, flag);
    if opt & SCO_FIRST != 0 {
        return; // Client update handled by caller
    }
    let Some(hd) = data::get(c, hd_id) else { return };
    if data::master_sd(c, &hd).is_some() && b_status != hd.battle_status {
        super::clif::clif_hominfo(c, hd.master, hd_id, 0);
    }
}

fn has(flag: u64, scb: u32) -> bool {
    flag & (1u64 << scb) != 0
}

fn set(flag: &mut u64, scb: u32) {
    *flag |= 1u64 << scb;
}

/// status.cpp status_calc_bl_main (status.cpp:5837-6515), the path a homunculus takes (bl.type == BL_HOM; sd ==
/// nullptr; RENEWAL, RENEWAL_ASPD): every SCB_* flag with its BL_HOM branches. `b_status` is hd->base_status, `status`
/// hd->battle_status, `sc` hd->sc. The status_get_hom* macros read `status` (the battle status being built).
pub fn status_calc_bl_main_hom(c: &Ctx, hd_id: u32, flag: u64) {
    use crate::status::calc_sc::{self as scc, Who};
    let Some(mut hd) = data::get(c, hd_id) else { return };
    let Some(u) = crate::unit::get(c, hd_id) else { return };
    // hd->homunculus (str_value … luk_value, level) and hd->homunculusDB, unchanged by this function
    let h = hd.clone();
    let b = hd.base_status.clone();
    let scl = crate::status::sc::load(c, hd_id);
    let w = Who { c, kind: BL_HOM, id: hd_id, map: u.map, pc: None };
    let mut flag = flag;
    // This needs to be done even if there is currently no status change active, because we need to update the
    // speed on the client when the last status change ends.
    if has(flag, SCB_SPEED) {
        // Re-walk to adjust speed (we do not check if walktimer != INVALID_TIMER because if you step on something
        // while walking, the moment this piece of code triggers the walk-timer is set on INVALID_TIMER)
        if let Some(mut u) = crate::unit::get(c, hd_id) {
            u.ud.change_walk_target = true;
            crate::unit::save(c, &u);
        }
    }
    let st = &mut hd.battle_status;
    if has(flag, SCB_STR) {
        st.str = scc::status_calc_str(&w, &scl, b.str as i32) as i16;
        set(&mut flag, SCB_BATK);
        // bl.type == BL_HOM
        set(&mut flag, SCB_WATK);
    }
    if has(flag, SCB_AGI) {
        st.agi = scc::status_calc_agi(&w, &scl, b.agi as i32) as i16;
        set(&mut flag, SCB_FLEE);
        set(&mut flag, SCB_DEF2); // RENEWAL
        // bl.type & (BL_PC | BL_HOM)
        set(&mut flag, SCB_ASPD);
        set(&mut flag, SCB_DSPD);
    }
    if has(flag, SCB_VIT) {
        st.vit = scc::status_calc_vit(&w, &scl, b.vit as i32) as i16;
        set(&mut flag, SCB_DEF2);
        set(&mut flag, SCB_MDEF2);
        // bl.type & (BL_PC | BL_HOM | BL_MER | BL_ELEM)
        set(&mut flag, SCB_MAXHP);
        // bl.type == BL_HOM
        set(&mut flag, SCB_DEF);
    }
    if has(flag, SCB_INT) {
        st.int_ = scc::status_calc_int(&w, &scl, b.int_ as i32) as i16;
        set(&mut flag, SCB_MATK);
        set(&mut flag, SCB_MDEF2);
        // bl.type & (BL_PC | BL_HOM | BL_MER | BL_ELEM)
        set(&mut flag, SCB_MAXSP);
        // bl.type == BL_HOM
        set(&mut flag, SCB_MDEF);
    }
    if has(flag, SCB_DEX) {
        st.dex = scc::status_calc_dex(&w, &scl, b.dex as i32) as i16;
        set(&mut flag, SCB_BATK);
        set(&mut flag, SCB_HIT);
        set(&mut flag, SCB_MATK); // RENEWAL
        set(&mut flag, SCB_MDEF2); // RENEWAL
        // bl.type & (BL_PC | BL_HOM)
        set(&mut flag, SCB_ASPD);
        // bl.type == BL_HOM
        set(&mut flag, SCB_WATK);
    }
    if has(flag, SCB_LUK) {
        st.luk = scc::status_calc_luk(&w, &scl, b.luk as i32) as i16;
        set(&mut flag, SCB_BATK);
        set(&mut flag, SCB_CRI);
        set(&mut flag, SCB_FLEE2);
        // RENEWAL
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
    // status_get_lv(&bl): hd->homunculus.level
    let lv = h.homunculus.level as i32;
    if has(flag, SCB_BATK) && b.batk != 0 {
        st.batk = status_base_atk_hom(&h, st, lv) as i32;
        // Calculate the difference of old and new base attack in the base status
        // Then add the same difference to the base attack of battle status
        let temp = b.batk - status_base_atk_hom(&h, &b, lv) as i32;
        st.batk += temp;
        st.batk = scc::status_calc_batk(&w, &scl, st.batk);
    }
    if has(flag, SCB_WATK) {
        // RENEWAL
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
        // bl.type == BL_HOM (defType: int16 in RENEWAL)
        st.def = (st.def as i32 + (st.vit as i32 / 5 - b.vit as i32 / 5)) as i16;
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
        // bl.type == BL_HOM
        st.mdef = (st.mdef as i32 + (st.int_ as i32 / 5 - b.int_ as i32 / 5)) as i16;
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
        // case BL_HOM (status.cpp:6108-6118)
        if data::master_sd(c, &h).is_some() {
            if super::hom_setting() & data::HOMSET_COPY_SPEED != 0 {
                // status_get_speed(hd->master): the master's battle_status.speed
                if let Some(msd) = crate::status::data::sd_get(c, h.master) {
                    st.speed = msd.battle_status.speed;
                }
            }
            // Homunculus speed buff/debuffs applies over the current speed
            st.speed = scc::status_calc_speed(&w, &scl, st.speed as i32);
        }
    }
    if has(flag, SCB_CRI) && b.cri != 0 {
        if st.luk == b.luk {
            st.cri = scc::status_calc_critical(&w, &scl, b.cri as i32);
        } else {
            st.cri = scc::status_calc_critical(&w, &scl, b.cri as i32 + 3 * (st.luk as i32 - b.luk as i32));
        }
        // (the Katar bonus: players only)
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
        // Since mode changed, reset their state. (rAthena stops the unit here; it is done once the row is saved
        // below: unit_stop_attack / unit_stop_walking read nothing this function computes afterwards)
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
        // RENEWAL: MATK = StatusMATK + WeaponMATK + ExtraMATK (StatusMATK: status_base_matk_min / _max for BL_HOM)
        let mut matk_min = status_base_matk_min_hom(&h, st, lv) as i32;
        let mut matk_max = status_base_matk_max_hom(&h, st, lv) as i32;
        // Apply Recognized Spell buff
        // Also update homunculus MATK, hom Min Matk is always the same as Max Matk
        if super::hom_setting() & data::HOMSET_SAME_MATK != 0 || scl.has(SC_RECOGNIZEDSPELL) {
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
        // bl.type == BL_HOM, RENEWAL_ASPD (status.cpp:6373-6388)
        let base_aspd = h.db().map(|d| d.base_aspd as i32).unwrap_or(0);
        let mut amotion = base_aspd;
        amotion = amotion - amotion * homdex(&h, st) / 1000 - homagi(&h, st) * amotion / 250;
        amotion = (amotion * scc::status_calc_aspd(&w, &scl, true) as i32 + scc::status_calc_aspd(&w, &scl, false) as i32) / -100 + amotion;
        amotion = scc::status_calc_fix_aspd(&w, &scl, amotion) as i32;
        st.amotion = cap_value(amotion, MAX_ASPD_NOPC / AMOTION_DIVIDER_NOPC, MIN_ASPD / AMOTION_DIVIDER_NOPC) as u16;
        st.adelay = (AMOTION_DIVIDER_NOPC * st.amotion as i32) as u16;
    }
    if has(flag, SCB_DSPD) {
        // bl.type == BL_HOM
        let dmotion = 800 - st.agi as i32 * 4;
        st.dmotion = cap_value(dmotion, 400, 800) as u16;
    }
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
    // bl.type & BL_REGEN: status_calc_regen(&bl, status, status_get_regen_data(&bl)) from the battle status
    if has(flag, SCB_VIT) || has(flag, SCB_MAXHP) || has(flag, SCB_INT) || has(flag, SCB_MAXSP) {
        let s = hd.battle_status.clone();
        let mut regen = hd.regen.clone();
        status_calc_regen_hom(&hd, &s, &mut regen);
        hd.regen = regen;
    }
    if has(flag, SCB_REGEN) {
        status_calc_regen_rate_hom(&mut hd.regen, &scl);
    }
    data::put(c, &hd);
    if stop_attack || stop_walking {
        if let Some(mut u) = crate::unit::get(c, hd_id) {
            if stop_attack {
                crate::unit::stop_attack(c, &mut u);
            }
            if stop_walking {
                crate::unit::stop_walking(c, &mut u, crate::unit::USW_FIXPOS, 0);
            }
            crate::unit::save(c, &u);
        }
    }
}

/// status.cpp status_calc_regen_rate (status.cpp:5377-5508) for a homunculus: no sregen / ssregen (only players have
/// them), no elemental insignia bonus (BL_ELEM only), no Monk rule (BL_PC only).
pub fn status_calc_regen_rate_hom(regen: &mut RegenData, sc: &crate::status::sc::Sc) {
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
    // No natural SP regen
    if sc.has(SC_DANCING) || sc.has(SC_MAXIMIZEPOWER) || (sc.has(SC_OBLIVIONCURSE) && sc.v3(SC_OBLIVIONCURSE) == 1) {
        regen.flag &= !RGN_SP;
    }
    if sc.has(SC_TENSIONRELAX) {
        if regen.overweight {
            regen.overweight = false; // 1x HP regen
        } else {
            regen.rate_hp = regen.rate_hp.wrapping_add(200);
        }
    }
    if sc.has(SC_MAGNIFICAT) {
        regen.rate_sp = regen.rate_sp.wrapping_add(100);
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
    if sc.has(SC_CATNIPPOWDER) {
        regen.rate_hp = regen.rate_hp.wrapping_mul(2);
        regen.rate_sp = regen.rate_sp.wrapping_mul(2);
    }
    if sc.has(SC_SHRIMPBLESSING) {
        regen.rate_sp = regen.rate_sp.wrapping_add(50);
    }
    // RENEWAL
    if sc.has(SC_NIBELUNGEN) {
        if sc.v2(SC_NIBELUNGEN) == RINGNBL_HPREGEN {
            regen.rate_hp = regen.rate_hp.wrapping_add(100);
        } else if sc.v2(SC_NIBELUNGEN) == RINGNBL_SPREGEN {
            regen.rate_sp = regen.rate_sp.wrapping_add(100);
        }
    }
    if sc.has(SC_SIRCLEOFNATURE) {
        regen.rate_hp = (regen.rate_hp as i32 + sc.v2(SC_SIRCLEOFNATURE)) as u16;
    }
    if sc.has(SC_SONGOFMANA) {
        regen.rate_sp = (regen.rate_sp as i32 + sc.v3(SC_SONGOFMANA)) as u16;
    }
}

/// status.cpp status_base_matk_min (RENEWAL) for a BL_HOM unit by id (callers that hold a block_list: skill.cpp
/// skill_calc_heal, status.cpp SC_FRESHSHRIMP). The status_get_hom* macros read `status`; without a hom_data row the
/// *_value parts are 0.
pub fn status_base_matk_min_of(c: &Ctx, hd_id: u32, status: &StatusData, level: i32) -> u16 {
    match data::get(c, hd_id) {
        Some(hd) => status_base_matk_min_hom(&hd, status, level),
        None => (status.int_ as i32 + level + (status.int_ as i32 + status.dex as i32) / 5) as u16,
    }
}

/// status.cpp status_base_matk_max (RENEWAL) for a BL_HOM unit by id (see [`status_base_matk_min_of`]).
pub fn status_base_matk_max_of(c: &Ctx, hd_id: u32, status: &StatusData, level: i32) -> u16 {
    match data::get(c, hd_id) {
        Some(hd) => status_base_matk_max_hom(&hd, status, level),
        None => (status.int_ as i32 + level + (status.luk as i32 + status.int_ as i32 + status.dex as i32) / 3) as u16,
    }
}

/// status.cpp status_get_guild_id, BL_HOM arm (status.cpp:9183-9186): hd->master->status.guild_id (0 without a
/// master).
pub fn status_get_guild_id_hom(c: &Ctx, hd_id: u32) -> u32 {
    data::get(c, hd_id).filter(|hd| crate::pc::get(c, hd.master).is_some()).and_then(|hd| crate::pc::character_of(c, hd.master)).map(|ch| ch.guild_id).unwrap_or(0)
}

/// status.cpp status_get_emblem_id, BL_HOM arm (status.cpp:9232-9235): hd->master->guild_emblem_id (the master's
/// unit row keeps sd->guild_emblem_id: guild.rs emblem_id_of).
pub fn status_get_emblem_id_hom(c: &Ctx, hd_id: u32) -> i32 {
    data::get(c, hd_id).filter(|hd| crate::pc::get(c, hd.master).is_some()).map(|hd| crate::guild::emblem_id_of(c, hd.master)).unwrap_or(0)
}

/// status.cpp status_calc_bl_main, the player part at status.cpp:6089-6097 (RENEWAL): "Recalculate homunculus speed
/// if the player receives a speed buff/debuff". `master_speed` is the player's new battle_status.speed
/// (status_get_speed(sd)). Only the homunculus' battle_status.speed changes (no client update, as in rAthena).
pub fn master_speed_changed(c: &Ctx, aid: u32, master_speed: u16) {
    let Some(mut hd) = data::of_master(c, aid) else { return };
    if !data::hom_is_active(Some(&hd)) {
        return;
    }
    if super::hom_setting() & data::HOMSET_COPY_SPEED != 0 {
        hd.battle_status.speed = master_speed;
    }
    // Homunculus speed buff/debuffs applies over the current speed
    let map = crate::unit::get(c, hd.id).map(|u| u.map).unwrap_or(0);
    let w = crate::status::calc_sc::Who { c, kind: BL_HOM, id: hd.id, map, pc: None };
    let scl = crate::status::sc::load(c, hd.id);
    hd.battle_status.speed = crate::status::calc_sc::status_calc_speed(&w, &scl, hd.battle_status.speed as i32);
    data::put(c, &hd);
}

/// status.cpp status_set_viewdata, BL_HOM branch: hom_get_viewdata (or the monster / NPC view data of such a class).
pub fn status_set_viewdata(c: &Ctx, hd_id: u32, class_: i32) {
    let vd = if crate::db::mobs::mobdb_checkid(class_) != 0 || crate::mob::rtdb::mob_is_clone(c, class_) != 0 {
        crate::mob::rtdb::mob_get_viewdata(c, class_).map(crate::mob::status::view_from_db)
    } else if crate::unit_npc::npcdb_checkid(class_) {
        Some(crate::npc::npc_viewdata(class_))
    } else {
        super::hom_get_viewdata(class_)
    };
    let Some(vd) = vd else {
        log::error!("status_set_viewdata (HOMUNCULUS): No view data for class {class_}");
        return;
    };
    if let Some(mut u) = crate::unit::get(c, hd_id) {
        u.vd = vd;
        crate::unit::save(c, &u);
    }
}
