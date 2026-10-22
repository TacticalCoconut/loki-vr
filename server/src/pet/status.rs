//! The pet parts of status.cpp: status_calc_pet_ (through status_calc_bl_, which stops right after it for pets:
//! "Pets are not affected by statuses"), status_calc_misc for BL_PET (RENEWAL), status_base_atk for pets, and
//! status_set_viewdata's BL_PET branch.

use super::data::{self, PetData, PET_INTIMATE_NONE};
use super::{pet_attack_support, pet_damage_support, pet_hair_style, pet_lv_rate, pet_max_atk1, pet_max_atk2, pet_max_stats, pet_support_min_friendly, pet_support_rate};
use crate::db::mobs::{MD_CANATTACK, MD_CANMOVE};
use crate::map::BL_PET;
use crate::status::data::StatusData;
use crate::status::defs::{cap_value, CLASS_NORMAL, SCO_FIRST};
use crate::unit;
use crate::world::Ctx;

/// status.hpp status_calc_pet(pd, opt) = status_calc_bl_(pd, SCB_ALL, opt): for a pet only status_calc_pet_ runs.
pub fn status_calc_pet(c: &Ctx, pd_id: u32, opt: u8) {
    let Some(mut pd) = data::get(c, pd_id) else { return };
    status_calc_pet_(c, &mut pd, opt);
    data::put(c, &pd);
}

/// status.cpp status_calc_pet_: the first calculation copies the monster's database status (walking only, a normal
/// class, the walk speed of pet_walk_speed); with pet_lv_rate the stats follow the owner's level (off: pet_lv_rate
/// 0), else the first calculation derives the misc values at the database level. Then the support rate.
pub fn status_calc_pet_(c: &Ctx, pd: &mut PetData, opt: u8) {
    let Some(db) = data::mob_db_of(c, pd) else { return };
    if opt & SCO_FIRST != 0 {
        pd.status = crate::mob::status::status_from_db(&db.status);
        pd.status.mode = MD_CANMOVE; // Pets discard all modes, except walking
        pd.status.class_ = CLASS_NORMAL as u8;
        pd.status.speed = data::get_pet_walk_speed(c, pd);
        if pet_attack_support() || pet_damage_support() {
            // Attack support requires the pet to be able to attack
            pd.status.mode |= MD_CANATTACK;
        }
    }
    if pet_lv_rate() != 0 && crate::pc::get(c, pd.master).is_some() {
        let base_level = crate::pc::character_of(c, pd.master).map(|ch| ch.base_level as i32).unwrap_or(1);
        let mut lv = base_level * pet_lv_rate() / 100;
        if lv < 0 {
            lv = 1;
        }
        if lv != pd.pet.level as i32 || opt & SCO_FIRST != 0 {
            let bstat = crate::mob::status::status_from_db(&db.status);
            let dblv = db.lv as i32;
            pd.pet.level = lv as i16;
            if opt & SCO_FIRST == 0 {
                // Lv Up animation
                if let Some(u) = unit::get(c, pd.id) {
                    crate::status::clif::misceffect(c, &u, crate::status::defs::NOTIFYEFFECT_BASE_LEVEL_UP);
                }
            }
            let st = &mut pd.status;
            st.rhw.atk = (bstat.rhw.atk as i32 * lv / dblv) as u16;
            st.rhw.atk2 = (bstat.rhw.atk2 as i32 * lv / dblv) as u16;
            st.str = (bstat.str as i32 * lv / dblv) as i16;
            st.agi = (bstat.agi as i32 * lv / dblv) as i16;
            st.vit = (bstat.vit as i32 * lv / dblv) as i16;
            st.int_ = (bstat.int_ as i32 * lv / dblv) as i16;
            st.dex = (bstat.dex as i32 * lv / dblv) as i16;
            st.luk = (bstat.luk as i32 * lv / dblv) as i16;
            st.rhw.atk = cap_value(st.rhw.atk as i32, 1, pet_max_atk1().min(pet_max_atk2())) as u16;
            st.rhw.atk2 = cap_value(st.rhw.atk2 as i32, 2, pet_max_atk2()) as u16;
            st.str = cap_value(st.str as i32, 1, pet_max_stats()) as i16;
            st.agi = cap_value(st.agi as i32, 1, pet_max_stats()) as i16;
            st.vit = cap_value(st.vit as i32, 1, pet_max_stats()) as i16;
            st.int_ = cap_value(st.int_ as i32, 1, pet_max_stats()) as i16;
            st.dex = cap_value(st.dex as i32, 1, pet_max_stats()) as i16;
            st.luk = cap_value(st.luk as i32, 1, pet_max_stats()) as i16;
            status_calc_misc_pet(&mut pd.status, lv);
            if opt & SCO_FIRST == 0 {
                // Not done the first time because the pet is not visible yet
                super::clif::clif_send_petstatus(c, pd.master, pd);
            }
        }
    } else if opt & SCO_FIRST != 0 {
        status_calc_misc_pet(&mut pd.status, db.lv as i32);
        if pet_lv_rate() == 0 && pd.pet.level as i32 != db.lv as i32 {
            pd.pet.level = db.lv as i16;
        }
    }
    // Support rate modifier (1000 = 100%): uint16 rate_fix = min(…, USHRT_MAX) (a pet below
    // pet_support_min_friendly gives a negative value, which wraps in the uint16)
    let fix = (1000 * (pd.pet.intimate as i32 - pet_support_min_friendly()) / (1000 - pet_support_min_friendly()) + 500).min(u16::MAX as i32);
    pd.rate_fix = fix as u16;
    // apply_rate(rate_fix, pet_support_rate)
    let r = pd.rate_fix as i32;
    let applied = if pet_support_rate() == 100 { r } else if r > 100000 { r / 100 * pet_support_rate() } else { r * pet_support_rate() / 100 };
    pd.rate_fix = applied.min(u16::MAX as i32) as u16;
    let _ = PET_INTIMATE_NONE;
}

/// status.cpp status_calc_misc (RENEWAL) for a pet: non-players get the values set (res / mres too: only BL_MOB
/// keeps them), the attack variance 80% / 120% of status_base_atk_min / _max, status_base_matk_min / _max, no
/// critical (enable_critical 17 has no BL_PET) and no perfect flee (enable_perfect_flee 1), the base attack of
/// status_base_atk (enable_baseatk_renewal 0x29F has BL_PET: str + level). Pets have no regen data.
pub fn status_calc_misc_pet(status: &mut StatusData, level: i32) {
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
    status.res = 0;
    status.mres = 0;
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
    // ATK: status_base_atk_max / status_base_atk_min for BL_PET
    status.rhw.atk2 = (status.rhw.atk as i32 * 120 / 100) as u16;
    status.rhw.atk = (status.rhw.atk as i32 * 80 / 100) as u16;
    // MAtk: status_base_matk_min / _max for BL_PET
    status.matk_min = (status.int_ as i32 + level + status.rhw.matk as i32 * 70 / 100) as u16;
    status.matk_max = (status.int_ as i32 + level + status.rhw.matk as i32 * 130 / 100) as u16;
    //Critical
    if BL_PET & crate::conf::enable_critical() != 0 {
        stat = status.cri as i32 + level / 10 + 10 + status.luk as i32 * 3;
        status.cri = cap(stat, 1);
    } else {
        status.cri = 0;
    }
    if BL_PET & crate::conf::enable_perfect_flee() != 0 {
        stat = status.flee2 as i32 + status.luk as i32 + 10;
        status.flee2 = cap(stat, 0);
    } else {
        status.flee2 = 0;
    }
    // status_base_atk (RENEWAL): pets are in enable_baseatk_renewal; "Others": str + level
    if BL_PET & crate::conf::enable_baseatk_renewal() != 0 {
        status.batk += cap_value(status.str as i32 + level, 0, u16::MAX as i32);
    }
    if status.cri != 0 {
        // default (not BL_MOB / BL_PC): battle_config.critical_rate (100)
        let critical_rate: i32 = crate::battle_config::get(crate::battle_config::Bc::CRITICAL_RATE);
        if critical_rate != 100 {
            status.cri = cap_value(status.cri as i32 * critical_rate / 100, 1, i16::MAX as i32) as i16;
        }
        if status.cri == 0 && critical_rate != 0 {
            status.cri = 10;
        }
    }
    // (bl->type & BL_REGEN: pets have no regen data)
}

/// status.cpp status_set_viewdata, BL_PET branch: the monster's view data, with the pet hair style and the accessory's
/// view id (or its item id) as the head-bottom look for a monster sprite.
pub fn status_set_viewdata(c: &Ctx, pd_id: u32, class_: i32) {
    let Some(pd) = data::get(c, pd_id) else { return };
    let Some(mut u) = unit::get(c, pd_id) else { return };
    let mob_vd = if crate::db::mobs::mobdb_checkid(class_) != 0 || crate::mob::rtdb::mob_is_clone(c, class_) != 0 {
        crate::mob::rtdb::mob_get_viewdata(c, class_).map(crate::mob::status::view_from_db)
    } else if crate::unit_npc::npcdb_checkid(class_) {
        Some(crate::npc::npc_viewdata(class_))
    } else {
        // hom_get_viewdata (homunculus.rs: look[LOOK_BASE] = the class only) / mercenary_get_viewdata /
        // elemental_get_viewdata (status.cpp:9387-9392)
        if crate::db::misc::homunculus::homdb_checkid(class_) {
            crate::homunculus::hom_get_viewdata(class_)
        } else if crate::db::misc::mercenary::mercenary_db().exists(class_) {
            crate::db::misc::mercenary::mercenary_db().find(class_).map(|m| crate::mob::status::view_from_db(&m.vd))
        } else {
            crate::db::misc::elemental::elemental_get_viewdata(class_).map(crate::mob::status::view_from_db)
        }
    };
    let Some(vd) = mob_vd else {
        log::error!("status_set_viewdata (PET): No view data for class {class_}");
        return;
    };
    u.vd = vd;
    if !crate::clif::pcdb_checkid(u.vd.class) {
        u.vd.hair = pet_hair_style();
        if pd.pet.equip != 0 {
            u.vd.head_bottom = crate::db::items::itemdb_viewid(pd.pet.equip) as i32;
            if u.vd.head_bottom == 0 {
                u.vd.head_bottom = pd.pet.equip as i32;
            }
        }
    }
    unit::save(c, &u);
}
