//! status.cpp status_change_start_post_delay: the first `switch (type)`, "Check for immunities / sc fails"
//! (status.cpp:10317-10662): per-status conditions that make the status fail (or succeed without being stored) after
//! the start delay, and the equipment the strip statuses take off.

use super::consts::*;
use super::defs::*;
use super::sc::{get_sce, has_sce, status_change_end};
use super::sc_ext::{pc_check_weapontype, pc_checkskill, skill_check_camouflage, skill_check_cloaking};
use super::sc_st::{Flow, StartSt};
use crate::db::jobs::ids::*;
use crate::db::skills::ids::*;
use crate::inventory::{EQI_ACC_L, EQI_ACC_R, EQI_ARMOR, EQI_HAND_L, EQI_HAND_R, EQI_HEAD_TOP, EQI_SHADOW_ACC_L, EQI_SHADOW_ARMOR};
use crate::inventory::{EQP_ACC_L, EQP_ACC_R, EQP_ARMOR, EQP_HELM, EQP_SHADOW_GEAR, EQP_SHIELD, EQP_WEAPON};
use crate::world::Ctx;

/// Every status the switch has a case for.
pub const CASES: &[i32] = &[
    SC_VACUUM_EXTREME,
    SC_ALL_RIDING,
    SC_SIGNUMCRUCIS,
    SC_KYRIE,
    SC_TUNAPARTY,
    SC_ADRENALINE,
    SC_ADRENALINE2,
    SC_CLOAKING,
    SC_MODECHANGE,
    SC_STRIPWEAPON,
    SC_STRIPSHIELD,
    SC_STRIPARMOR,
    SC_STRIPHELM,
    SC_SHADOW_STRIP,
    SC_MERC_FLEEUP,
    SC_MERC_ATKUP,
    SC_MERC_HPUP,
    SC_MERC_SPUP,
    SC_MERC_HITUP,
    SC_STRFOOD,
    SC_AGIFOOD,
    SC_VITFOOD,
    SC_INTFOOD,
    SC_DEXFOOD,
    SC_LUKFOOD,
    SC_FOOD_STR_CASH,
    SC_FOOD_AGI_CASH,
    SC_FOOD_VIT_CASH,
    SC_FOOD_INT_CASH,
    SC_FOOD_DEX_CASH,
    SC_FOOD_LUK_CASH,
    SC_CAMOUFLAGE,
    SC__STRIPACCESSORY,
    SC_C_MARKER,
    SC_MADNESSCANCEL,
    SC_TOXIN,
    SC_PARALYSE,
    SC_VENOMBLEED,
    SC_MAGICMUSHROOM,
    SC_DEATHHURT,
    SC_PYREXIA,
    SC_OBLIVIONCURSE,
    SC_LEECHESEND,
    SC_SPIRIT,
    SC_SOULGOLEM,
    SC_SOULSHADOW,
    SC_SOULFALCON,
    SC_SOULFAIRY,
];

/// `sd->equip_index[i]` (-1: nothing there).
fn equip_index(c: &Ctx, aid: u32, i: usize) -> i16 {
    crate::inventory::st(c, aid).and_then(|s| s.equip_index.get(i).copied()).unwrap_or(-1)
}

/// `sd->inventory_data[n]` present, and its item type.
fn inventory_type(c: &Ctx, aid: u32, n: i16) -> Option<u8> {
    if n < 0 {
        return None;
    }
    crate::inventory::inv_get(c, aid, n as usize).map(|it| crate::inventory::data_of(&it.item).r#type)
}

/// `sd->bonus.unstripable_equip`.
fn unstripable_equip(c: &Ctx, aid: u32) -> u32 {
    super::data::sd_get(c, aid).map(|sd| sd.bonus.unstripable_equip as u32).unwrap_or(0)
}

/// `pc_unequipitem(sd, n, 3)`.
fn unequip(c: &Ctx, aid: u32, n: i16) {
    crate::pc_equip::pc_unequipitem(c, aid, n as i32, 3);
}

/// The stat foods: fail when the cash food of the same stat is stronger (and the other way round).
fn food_fails(st: &StartSt, other: i32) -> bool {
    st.sce(other).map(|e| e.val1 > st.val1).unwrap_or(false)
}

/// The immunity / fail switch. `Flow::Continue` = `break` (go on with the status), `Flow::Return(r)` = `return r`.
pub fn immunity(st: &mut StartSt) -> Flow {
    let c = st.c;
    let bl_id = st.bl.id;
    let is_pc = st.is_pc();
    let loaded = st.flag & SCSTART_LOADED != 0;
    match st.t {
        SC_VACUUM_EXTREME => {
            // Ignore post delay from other vacuum (this will make stack effect enabled)
            if get_sce(c, bl_id, SC_VACUUM_EXTREME_POSTDELAY).map(|e| e.val2 == st.val2).unwrap_or(false) {
                return Flow::Return(false);
            }
        }
        SC_ALL_RIDING => {
            let option = st.bl_now().sc.option;
            if !is_pc || option & (OPTION_RIDING | OPTION_DRAGON | OPTION_WUG | OPTION_MADOGEAR) != 0 {
                return Flow::Return(false);
            }
        }
        SC_SIGNUMCRUCIS => {
            // Only affects demons and undead element (but not players)
            if (!st.undead_flag && st.status().race as i32 != RC_DEMON) || is_pc {
                return Flow::Return(false);
            }
        }
        SC_KYRIE | SC_TUNAPARTY => {
            if st.bl.kind == BL_MOB {
                return Flow::Return(false);
            }
        }
        SC_ADRENALINE => {
            if is_pc && !pc_check_weapontype(c, bl_id, crate::db::skills::skill_get_weapontype(BS_ADRENALINE)) {
                return Flow::Return(false);
            }
        }
        SC_ADRENALINE2 => {
            if is_pc && !pc_check_weapontype(c, bl_id, crate::db::skills::skill_get_weapontype(BS_ADRENALINE2)) {
                return Flow::Return(false);
            }
        }
        SC_CLOAKING => {
            // Avoid cloaking with no wall and low skill level. [Skotlex]
            // Due to the cloaking card, we have to check the wall versus to known
            // skill level rather than the used one. [Skotlex]
            if is_pc && pc_checkskill(c, bl_id, AS_CLOAKING) < 3 && !skill_check_cloaking(c, &st.bl_now(), false) {
                return Flow::Return(false);
            }
        }
        SC_MODECHANGE => {
            // `if (!bstatus) return false;`: status_get_base_status has none for an NPC whose class is a monster id
            if st.bl.kind == BL_NPC {
                use crate::npc::npc as _;
                if c.db().npc().id().find(bl_id).map(|n| crate::db::mobs::mobdb_checkid(n.class) != 0).unwrap_or(false) {
                    return Flow::Return(false);
                }
            }
            let bstatus = super::status_get_base_status(c, &st.bl);
            if let Some(e) = get_sce(c, bl_id, st.t) {
                // Pile up with previous values.
                if st.val2 == 0 {
                    st.val2 = e.val2;
                }
                st.val3 |= e.val3;
                st.val4 |= e.val4;
            }
            let mut mode = if st.val2 != 0 { (st.val2 & !MD_MASK) | st.val2 } else { bstatus.mode }; // Base mode
            if st.val4 != 0 {
                mode &= !st.val4; // Del mode
            }
            if st.val3 != 0 {
                mode |= st.val3; // Add mode
            }
            if mode == bstatus.mode {
                // No change.
                if has_sce(c, bl_id, st.t) {
                    // Abort previous status
                    return Flow::Return(status_change_end(c, bl_id, st.t, 0) != 0);
                }
                return Flow::Return(false);
            }
        }
        // Strip skills, need to divest something or it fails.
        SC_STRIPWEAPON => {
            if st.val2 == 1 {
                st.val2 = 0; // Brandish Spear/Bowling Bash effet. Do not take weapon off.
            } else if is_pc && !loaded {
                // Apply sc anyway if loading saved sc_data
                let mut success_flag: u8 = 0;
                if unstripable_equip(c, bl_id) & EQP_WEAPON != 0 {
                    return Flow::Return(false);
                }
                let i = equip_index(c, bl_id, EQI_HAND_L);
                if inventory_type(c, bl_id, i) == Some(crate::db::items::IT_WEAPON) {
                    success_flag |= 1;
                    unequip(c, bl_id, i); // Left-hand weapon
                }
                let i = equip_index(c, bl_id, EQI_HAND_R);
                if inventory_type(c, bl_id, i) == Some(crate::db::items::IT_WEAPON) {
                    success_flag |= 2;
                    unequip(c, bl_id, i);
                }
                if success_flag == 0 {
                    return Flow::Return(false);
                }
            }
            if st.tick == 1 {
                return Flow::Return(true); // Minimal duration: Only strip without causing the SC
            }
        }
        SC_STRIPSHIELD => {
            if st.val2 == 1 {
                st.val2 = 0; // GX effect. Do not take shield off..
            } else if is_pc && !loaded {
                if unstripable_equip(c, bl_id) & EQP_SHIELD != 0 {
                    return Flow::Return(false);
                }
                let i = equip_index(c, bl_id, EQI_HAND_L);
                if inventory_type(c, bl_id, i) != Some(crate::db::items::IT_ARMOR) {
                    return Flow::Return(false);
                }
                unequip(c, bl_id, i);
            }
            if st.tick == 1 {
                return Flow::Return(true); // Minimal duration: Only strip without causing the SC
            }
        }
        SC_STRIPARMOR => {
            if is_pc && !loaded {
                if unstripable_equip(c, bl_id) & EQP_ARMOR != 0 {
                    return Flow::Return(false);
                }
                let i = equip_index(c, bl_id, EQI_ARMOR);
                if inventory_type(c, bl_id, i).is_none() {
                    return Flow::Return(false);
                }
                unequip(c, bl_id, i);
            }
            if st.tick == 1 {
                return Flow::Return(true); // Minimal duration: Only strip without causing the SC
            }
        }
        SC_STRIPHELM => {
            if is_pc && !loaded {
                if unstripable_equip(c, bl_id) & EQP_HELM != 0 {
                    return Flow::Return(false);
                }
                let i = equip_index(c, bl_id, EQI_HEAD_TOP);
                if inventory_type(c, bl_id, i).is_none() {
                    return Flow::Return(false);
                }
                unequip(c, bl_id, i);
            }
            if st.tick == 1 {
                return Flow::Return(true); // Minimal duration: Only strip without causing the SC
            }
        }
        SC_SHADOW_STRIP => {
            if is_pc && !loaded {
                if unstripable_equip(c, bl_id) & EQP_SHADOW_GEAR != 0 {
                    return Flow::Return(false);
                }
                let mut success_flag = false;
                for i in EQI_SHADOW_ARMOR..=EQI_SHADOW_ACC_L {
                    let index = equip_index(c, bl_id, i);
                    if inventory_type(c, bl_id, index).is_some() {
                        unequip(c, bl_id, index);
                        success_flag = true;
                    }
                }
                if !success_flag {
                    return Flow::Return(false);
                }
            }
            if st.tick == 1 {
                return Flow::Return(true);
            }
        }
        SC_MERC_FLEEUP | SC_MERC_ATKUP | SC_MERC_HPUP | SC_MERC_SPUP | SC_MERC_HITUP => {
            if st.bl.kind != BL_MER {
                return Flow::Return(false); // Stats only for Mercenaries
            }
        }
        SC_STRFOOD if food_fails(st, SC_FOOD_STR_CASH) => return Flow::Return(false),
        SC_AGIFOOD if food_fails(st, SC_FOOD_AGI_CASH) => return Flow::Return(false),
        SC_VITFOOD if food_fails(st, SC_FOOD_VIT_CASH) => return Flow::Return(false),
        SC_INTFOOD if food_fails(st, SC_FOOD_INT_CASH) => return Flow::Return(false),
        SC_DEXFOOD if food_fails(st, SC_FOOD_DEX_CASH) => return Flow::Return(false),
        SC_LUKFOOD if food_fails(st, SC_FOOD_LUK_CASH) => return Flow::Return(false),
        SC_FOOD_STR_CASH if food_fails(st, SC_STRFOOD) => return Flow::Return(false),
        SC_FOOD_AGI_CASH if food_fails(st, SC_AGIFOOD) => return Flow::Return(false),
        SC_FOOD_VIT_CASH if food_fails(st, SC_VITFOOD) => return Flow::Return(false),
        SC_FOOD_INT_CASH if food_fails(st, SC_INTFOOD) => return Flow::Return(false),
        SC_FOOD_DEX_CASH if food_fails(st, SC_DEXFOOD) => return Flow::Return(false),
        SC_FOOD_LUK_CASH if food_fails(st, SC_LUKFOOD) => return Flow::Return(false),
        SC_CAMOUFLAGE => {
            if is_pc && pc_checkskill(c, bl_id, RA_CAMOUFLAGE) < 3 && !skill_check_camouflage(c, &st.bl_now(), false) {
                return Flow::Return(false);
            }
        }
        SC__STRIPACCESSORY => {
            if is_pc {
                let mut i: i16 = -1;
                let unstrip = unstripable_equip(c, bl_id);
                if unstrip & EQP_ACC_L == 0 {
                    i = equip_index(c, bl_id, EQI_ACC_L);
                    if inventory_type(c, bl_id, i) == Some(crate::db::items::IT_ARMOR) {
                        unequip(c, bl_id, i); // Left-Accessory
                    }
                }
                if unstrip & EQP_ACC_R == 0 {
                    i = equip_index(c, bl_id, EQI_ACC_R);
                    if inventory_type(c, bl_id, i) == Some(crate::db::items::IT_ARMOR) {
                        unequip(c, bl_id, i); // Right-Accessory
                    }
                }
                if i < 0 {
                    return Flow::Return(false);
                }
            }
            if st.tick == 1 {
                return Flow::Return(true); // Minimal duration: Only strip without causing the SC
            }
        }
        SC_C_MARKER => {
            // `src == bl`: a missing source is not the target (rAthena's callers always pass the caster)
            if st.src.as_ref().map(|s| s.id == bl_id).unwrap_or(false) {
                return Flow::Return(false);
            }
            // Failed if the target is already marked and the new marker that isn't same marker
            if get_sce(c, bl_id, st.t).map(|e| e.val2 != st.src_id() as i32).unwrap_or(false) {
                return Flow::Return(false);
            }
        }
        SC_MADNESSCANCEL => {
            if has_sce(c, bl_id, st.t) {
                // Toggle the status but still consume requirements.
                status_change_end(c, bl_id, st.t, 0);
                return Flow::Return(false);
            }
        }
        SC_TOXIN | SC_PARALYSE | SC_VENOMBLEED | SC_MAGICMUSHROOM | SC_DEATHHURT | SC_PYREXIA | SC_OBLIVIONCURSE | SC_LEECHESEND => {
            if st.val3 == 0 {
                // Don't display icon on self
                st.flag |= SCSTART_NOICON;
            }
            for i in SC_TOXIN..=SC_LEECHESEND {
                match get_sce(c, bl_id, i) {
                    // It doesn't stack or even renew on the target
                    Some(e) if e.val3 == 1 => return Flow::Return(false),
                    // End the bonus part on the caster
                    Some(e) if e.val3 == 0 => {
                        status_change_end(c, bl_id, i, 0);
                    }
                    _ => {}
                }
            }
        }
        SC_SPIRIT => {
            if is_pc {
                let class_ = super::data::sd_get(c, bl_id).map(|sd| sd.class_).unwrap_or(0);
                let mut mask: u64 = MAPID_SECONDMASK;
                let target_class: u64 = match u16::try_from(st.val2).unwrap_or(0) {
                    SL_ALCHEMIST => MAPID_ALCHEMIST,
                    SL_ASSASIN => MAPID_ASSASSIN,
                    SL_BARDDANCER => MAPID_BARDDANCER,
                    SL_BLACKSMITH => MAPID_BLACKSMITH,
                    SL_CRUSADER => MAPID_CRUSADER,
                    SL_HUNTER => MAPID_HUNTER,
                    SL_KNIGHT => MAPID_KNIGHT,
                    SL_MONK => MAPID_MONK,
                    SL_PRIEST => MAPID_PRIEST,
                    SL_ROGUE => MAPID_ROGUE,
                    SL_SAGE => MAPID_SAGE,
                    SL_SOULLINKER => MAPID_SOUL_LINKER,
                    SL_STAR => MAPID_STAR_GLADIATOR,
                    SL_SUPERNOVICE => MAPID_SUPER_NOVICE,
                    SL_WIZARD => MAPID_WIZARD,
                    SL_HIGH => {
                        let base_level = crate::pc::character_of(c, bl_id).map(|ch| ch.base_level).unwrap_or(0);
                        if base_level >= 70 {
                            return Flow::Return(false);
                        }
                        match class_ {
                            // Only these classes are allowed.
                            MAPID_SWORDMAN_HIGH | MAPID_MAGE_HIGH | MAPID_ARCHER_HIGH | MAPID_ACOLYTE_HIGH | MAPID_MERCHANT_HIGH | MAPID_THIEF_HIGH => {}
                            _ => return Flow::Return(false),
                        }
                        // Set these to pass the check below.
                        mask = class_;
                        class_
                    }
                    _ => {
                        log::error!("Unknown skill id {} for SC_SPIRIT.", st.val2);
                        return Flow::Return(false);
                    }
                };
                if (class_ & mask) != target_class {
                    return Flow::Return(false);
                }
            } else {
                // Status change is only applicable for players
                return Flow::Return(false);
            }
        }
        SC_SOULGOLEM | SC_SOULSHADOW | SC_SOULFALCON | SC_SOULFAIRY => {
            if !is_pc {
                // Status change is only applicable for players
                return Flow::Return(false);
            }
        }
        _ => {}
    }
    Flow::Continue
}
