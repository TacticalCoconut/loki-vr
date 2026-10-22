//! status.cpp status_calc_bl_ / status_calc_bl_main for a monster: what its status changes do to its battle
//! status. The per-stat modifier functions are the shared ones of `status/calc_sc.rs`; this is the non-player path
//! of status_calc_bl_main (the player path is `status/calc.rs`).

use super::{get, put};
use crate::status::calc_sc::{self as scc, Who};
use crate::status::consts::*;
use crate::status::defs::*;
use crate::status::sc;
use crate::unit;
use crate::world::Ctx;

fn has(flag: u64, scb: u32) -> bool {
    flag & (1u64 << scb) != 0
}

fn set(flag: &mut u64, scb: u32) {
    *flag |= 1u64 << scb;
}

/// status.cpp status_calc_bl_ for BL_MOB: the base status when SCB_BASE is asked for (status_calc_mob_), then —
/// except on the first calculation ("Assume there will be no statuses active") — status_calc_bl_main. Monsters get
/// no client status updates.
pub fn status_calc_bl_mob(c: &Ctx, id: u32, flag: u64, opt: u8) {
    if has(flag, SCB_BASE) {
        let Some(mut md) = get(c, id) else { return };
        super::status::status_calc_mob_(c, &mut md, opt);
        put(c, &md);
    }
    if opt & SCO_FIRST != 0 {
        return; // Assume there will be no statuses active
    }
    status_calc_bl_main_mob(c, id, flag);
}

/// status.cpp status_calc_bl_main, the path a monster takes (bl.type == BL_MOB; sd == nullptr).
pub fn status_calc_bl_main_mob(c: &Ctx, id: u32, flag: u64) {
    let Some(mut md) = get(c, id) else { return };
    let Some(u) = unit::get(c, id) else { return };
    let b = super::status::base_status(c, &md);
    let scl = sc::load(c, id);
    let w = Who { c, kind: BL_MOB, id, map: u.map, pc: None };
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
    let st = &mut md.status;
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
    }
    if has(flag, SCB_INT) {
        st.int_ = scc::status_calc_int(&w, &scl, b.int_ as i32) as i16;
        set(&mut flag, SCB_MATK);
        set(&mut flag, SCB_MDEF2);
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
    let lv = md.level;
    let st = &mut md.status;
    if has(flag, SCB_BATK) && b.batk != 0 {
        st.batk = base_atk_mob(st.str, lv);
        // Calculate the difference of old and new base attack in the base status, then add the same difference
        // to the base attack of battle status
        let temp = b.batk - base_atk_mob(b.str, lv);
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
        // MATK = StatusMATK + WeaponMATK + ExtraMATK; StatusMATK for BL_MOB: int + level + 70% / 130% of Attack2
        let mut matk_min = st.int_ as i32 + lv + st.rhw.matk as i32 * 70 / 100;
        let mut matk_max = st.int_ as i32 + lv + st.rhw.matk as i32 * 130 / 100;
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
    // (regen data: players, homunculi, mercenaries and elementals only)
    put(c, &md);
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

/// status.cpp status_base_atk (RENEWAL) for BL_MOB ("Others"): str + level, when enable_baseatk_renewal has BL_MOB.
fn base_atk_mob(str: i16, level: i32) -> i32 {
    if crate::conf::enable_baseatk_renewal() & BL_MOB == 0 {
        return 0;
    }
    cap_value(str as i32 + level, 0, u16::MAX as i32)
}
