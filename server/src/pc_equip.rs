//! Wearing items (pc.cpp): pc_equippoint, pc_setequipindex / pc_calcweapontype, pc_isequip (+ pc_job_can_use_item,
//! pc_isItemClass), pc_checkequip, pc_equipitem, pc_unequipitem, the equip switch (pc_equipswitch*), combos
//! (pc_checkcombo / pc_removecombo / pc_load_combo), the look (map_session_data::update_look, pc_set_costume_view,
//! pc_changelook, pc_equiplookall, clif_changelook for players) and itemdb_isNoEquip.
//!
//! `sd->equip_index[]` / `sd->equip_switch_index[]` are the `pc_items` row's vectors (inventory.rs); `sd->status`
//! (weapon, shield, head_*, robe) is the character row.

use crate::clif::{self, Target};
use crate::db::items::{self as itemdb, ItemData};
use crate::inventory::*;
use crate::pc;
use crate::unit;
use crate::world::Ctx;

/// map.hpp enum _look.
pub const LOOK_BASE: u8 = 0;
pub const LOOK_HAIR: u8 = 1;
pub const LOOK_WEAPON: u8 = 2;
pub const LOOK_HEAD_BOTTOM: u8 = 3;
pub const LOOK_HEAD_TOP: u8 = 4;
pub const LOOK_HEAD_MID: u8 = 5;
pub const LOOK_HAIR_COLOR: u8 = 6;
pub const LOOK_CLOTHES_COLOR: u8 = 7;
pub const LOOK_SHIELD: u8 = 8;
pub const LOOK_SHOES: u8 = 9;
pub const LOOK_BODY: u8 = 10;
pub const LOOK_RESET_COSTUMES: u8 = 11;
pub const LOOK_ROBE: u8 = 12;
pub const LOOK_BODY2: u8 = 13;

/// status.hpp e_option bits the look code tests.
const OPTION_INVISIBLE: i32 = 0x40;
const OPTION_WEDDING: i32 = 0x1000;
const OPTION_XMAS: i32 = 0x10000;
const OPTION_SUMMER: i32 = 0x40000;
const OPTION_HANBOK: i32 = 0x0800_0000;
const OPTION_OKTOBERFEST: i32 = 0x1000_0000;
const OPTION_SUMMER2: i32 = 0x2000_0000;
const OPTION_COSTUME: i32 = OPTION_WEDDING | OPTION_XMAS | OPTION_SUMMER | OPTION_HANBOK | OPTION_OKTOBERFEST | OPTION_SUMMER2;
const OPTION_MADOGEAR: i32 = 0x40_0000;
/// npc.hpp JT_INVISIBLE.
const JT_INVISIBLE: i32 = 32767;
/// conf/battle/client.conf `wedding_ignorepalette` … `oktoberfest_ignorepalette`: all `no`.
const IGNORE_PALETTE: bool = false;
/// conf/battle/battle.conf `ammo_check_weapon: yes`, `ammo_unequip: yes`.
fn ammo_check_weapon() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::AMMO_CHECK_WEAPON) != 0
}
fn ammo_unequip() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::AMMO_UNEQUIP) != 0
}
/// conf/battle/skill.conf `skillrange_from_weapon: 0` (use_weapon_skill_range), misc.conf `battle_log: off`.
fn use_weapon_skill_range() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::SKILLRANGE_FROM_WEAPON) as u16
}
/// conf/battle/skill.conf `dancing_weaponswitch_fix: yes`, `switch_remove_edp: 2`.
fn dancing_weaponswitch_fix() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DANCING_WEAPONSWITCH_FIX) != 0
}
fn switch_remove_edp() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::SWITCH_REMOVE_EDP)
}

use crate::status::consts::{
    SCF_REMOVEONUNEQUIP, SCF_REMOVEONUNEQUIPARMOR, SCF_REMOVEONUNEQUIPWEAPON, SCS_NOEQUIPITEM, SCS_NOUNEQUIPITEM, SC_DANCING, SC_EDP, SC_KYOUGAKU, SC_PYROCLASTIC, SC_P_ALTER,
    SC_SHADOW_STRIP, SC_SIGNUMCRUCIS, SC_SPIRIT, SC_STRIPARMOR, SC_STRIPHELM, SC_STRIPSHIELD, SC_STRIPWEAPON, SC_SUHIDE, SC__STRIPACCESSORY, SC__WEAKNESS,
};

/// status.cpp StatusDatabase::removeByStatusFlag for a player.
fn remove_by_status_flag(c: &Ctx, aid: u32, flags: &[u32]) {
    let has = |t| crate::status::sc::has_sce(c, aid, t);
    for t in crate::db::misc::status::status_db().remove_by_status_flag(&has, flags) {
        crate::status::sc::status_change_end(c, aid, t, 0);
    }
}

// ---------------------------------------------------------------------------------------------- map / class checks

/// itemdb.cpp itemdb_isNoEquip: item_noequip.txt forbids the item on this map.
pub fn itemdb_is_no_equip(c: &Ctx, id: &ItemData, m: u16) -> bool {
    use crate::npc::*;
    if id.flag.no_equip == 0 {
        return false;
    }
    let f = |flag| mapflag(c, m, flag);
    let zone = mapflag_value(c, m, MF_RESTRICTED) as u32;
    let map = itemdb::NoEquipMap {
        vs: f(MF_PVP) || f(MF_GVG_DUNGEON) || f(MF_GVG) || f(MF_GVG_CASTLE) || f(MF_GVG_TE) || f(MF_GVG_TE_CASTLE) || f(MF_BATTLEGROUND),
        pvp: f(MF_PVP),
        gvg_no_te: f(MF_GVG) || f(MF_GVG_CASTLE),
        battleground: f(MF_BATTLEGROUND),
        gvg_te: f(MF_GVG_TE) || f(MF_GVG_TE_CASTLE),
        restricted: zone != 0,
        zone,
    };
    itemdb::itemdb_is_no_equip(id, &map)
}

/// pc.cpp pc_job_can_use_item.
pub fn pc_job_can_use_item(ch: &crate::character::Character, item: &ItemData) -> bool {
    use crate::db::jobs::{JOBL_2_1, JOBL_2_2, MAPID_FIRSTMASK};
    let class = class_mapid(ch);
    let job = 1u64 << (class & MAPID_FIRSTMASK);
    let index = if class & JOBL_2_1 != 0 {
        1
    } else if class & JOBL_2_2 != 0 {
        2
    } else {
        0
    };
    item.class_base[index] & job != 0
}

/// pc.cpp pc_isItemClass (RENEWAL).
pub fn pc_is_item_class(ch: &crate::character::Character, item: &ItemData) -> bool {
    use crate::db::jobs::{JOBL_BABY, JOBL_FOURTH, JOBL_THIRD, JOBL_UPPER};
    use itemdb::*;
    let class = class_mapid(ch);
    let u = item.class_upper;
    if u & ITEMJ_NORMAL != 0 && class & (JOBL_UPPER | JOBL_BABY | JOBL_THIRD | JOBL_FOURTH) == 0 {
        return true;
    }
    if u & ITEMJ_UPPER != 0 && class & JOBL_UPPER != 0 && class & JOBL_THIRD == 0 {
        return true;
    }
    if u & ITEMJ_BABY != 0 && class & JOBL_BABY != 0 && class & JOBL_THIRD == 0 {
        return true;
    }
    if u & ITEMJ_THIRD != 0 && class & JOBL_THIRD != 0 && class & (JOBL_UPPER | JOBL_BABY) == 0 && class & JOBL_FOURTH == 0 {
        return true;
    }
    if u & ITEMJ_THIRD_UPPER != 0 && class & JOBL_THIRD != 0 && class & JOBL_UPPER != 0 && class & JOBL_FOURTH == 0 {
        return true;
    }
    if u & ITEMJ_THIRD_BABY != 0 && class & JOBL_THIRD != 0 && class & JOBL_BABY != 0 && class & JOBL_FOURTH == 0 {
        return true;
    }
    if u & ITEMJ_FOURTH != 0 && class & JOBL_FOURTH != 0 {
        return true;
    }
    false
}

// ---------------------------------------------------------------------------------------------- equip points / indexes

/// pc.cpp pc_equippoint_sub: where an item goes (dual wielders put a one-handed dagger / sword / axe in both hands).
pub fn pc_equippoint_sub(c: &Ctx, aid: u32, id: &ItemData) -> u32 {
    use crate::db::jobs::{MAPID_ASSASSIN, MAPID_KAGEROUOBORO, MAPID_SECONDMASK};
    if !itemdb::itemdb_isequip2(id) {
        return 0;
    }
    let ep = id.equip;
    if id.subtype == W_DAGGER || id.subtype == W_1HSWORD || id.subtype == W_1HAXE {
        let class = status(c, aid).map(|ch| class_mapid(&ch)).unwrap_or(0);
        if crate::skill::checkskill(c, aid, crate::db::skills::AS_LEFT) > 0 || class & MAPID_SECONDMASK == MAPID_ASSASSIN || class & MAPID_SECONDMASK == MAPID_KAGEROUOBORO {
            if ep == EQP_WEAPON {
                return EQP_ARMS;
            }
            if ep == EQP_SHADOW_WEAPON {
                return EQP_SHADOW_ARMS;
            }
        }
    }
    ep
}

/// pc.cpp pc_equippoint for an inventory item.
pub fn pc_equippoint(c: &Ctx, aid: u32, it: &Item) -> u32 {
    pc_equippoint_sub(c, aid, data_of(it))
}

/// pc.cpp pc_calcweapontype: sd->status.weapon from the two hands.
fn pc_calcweapontype(s: &PcItems) -> u8 {
    if s.weapontype2 == W_FIST {
        return s.weapontype1;
    }
    if s.weapontype1 == W_FIST {
        return s.weapontype2;
    }
    let w = match (s.weapontype1, s.weapontype2) {
        (W_DAGGER, W_DAGGER) => W_DOUBLE_DD,
        (W_DAGGER, W_1HSWORD) => W_DOUBLE_DS,
        (W_DAGGER, W_1HAXE) => W_DOUBLE_DA,
        (W_1HSWORD, W_DAGGER) => W_DOUBLE_DS,
        (W_1HSWORD, W_1HSWORD) => W_DOUBLE_SS,
        (W_1HSWORD, W_1HAXE) => W_DOUBLE_SA,
        (W_1HAXE, W_DAGGER) => W_DOUBLE_DA,
        (W_1HAXE, W_1HSWORD) => W_DOUBLE_SA,
        (W_1HAXE, W_1HAXE) => W_DOUBLE_AA,
        _ => W_FIST,
    };
    // unknown, default to right hand type
    if w == W_FIST {
        s.weapontype1
    } else {
        w
    }
}

/// Stores pc_calcweapontype's result in sd->status.weapon.
fn set_status_weapon(c: &Ctx, aid: u32) {
    let Some(s) = st(c, aid) else { return };
    if let Some(mut ch) = status(c, aid) {
        ch.weapon = pc_calcweapontype(&s) as u16;
        put_status(c, &ch);
    }
}

/// pc.cpp pc_setequipindex.
pub fn pc_setequipindex(c: &Ctx, aid: u32) {
    let Some(mut s) = st(c, aid) else { return };
    s.equip_index = vec![-1; EQI_MAX];
    s.equip_switch_index = vec![-1; EQI_MAX];
    for r in inv_list(c, aid) {
        let i = r.idx as i16;
        if r.item.equip != 0 {
            for j in 0..EQI_MAX {
                if r.item.equip & EQUIP_BITMASK[j] != 0 {
                    s.equip_index[j] = i;
                }
            }
            let d = data_of(&r.item);
            if r.item.equip & EQP_HAND_R != 0 {
                s.weapontype1 = d.subtype;
            }
            if r.item.equip & EQP_HAND_L != 0 {
                s.weapontype2 = if d.r#type == itemdb::IT_WEAPON { d.subtype } else { W_FIST };
            }
        }
        if r.item.equip_switch != 0 {
            for j in 0..EQI_MAX {
                if r.item.equip_switch & EQUIP_BITMASK[j] != 0 {
                    s.equip_switch_index[j] = i;
                }
            }
        }
    }
    put_st(c, &s);
    set_status_weapon(c, aid);
}

/// pc.cpp pc_is_same_equip_index.
pub fn pc_is_same_equip_index(eqi: usize, equip_index: &[i16], index: i16) -> bool {
    if index < 0 || index as usize >= MAX_INVENTORY {
        return true;
    }
    if eqi == EQI_HAND_R && equip_index[EQI_HAND_L] == index {
        return true;
    }
    if eqi == EQI_HEAD_MID && equip_index[EQI_HEAD_LOW] == index {
        return true;
    }
    if eqi == EQI_HEAD_TOP && (equip_index[EQI_HEAD_MID] == index || equip_index[EQI_HEAD_LOW] == index) {
        return true;
    }
    if eqi == EQI_COSTUME_HEAD_MID && equip_index[EQI_COSTUME_HEAD_LOW] == index {
        return true;
    }
    if eqi == EQI_COSTUME_HEAD_TOP && (equip_index[EQI_COSTUME_HEAD_MID] == index || equip_index[EQI_COSTUME_HEAD_LOW] == index) {
        return true;
    }
    false
}

/// pc.cpp pc_isequipped: is `nameid` worn (or slotted in a worn item)?
pub fn pc_isequipped(c: &Ctx, aid: u32, nameid: u32) -> bool {
    let Some(s) = st(c, aid) else { return false };
    for i in 0..EQI_MAX {
        let index = s.equip_index[i];
        if index < 0 {
            continue;
        }
        if pc_is_same_equip_index(i, &s.equip_index, index) {
            continue;
        }
        let Some(r) = inv_get(c, aid, index as usize) else { continue };
        if r.item.nameid == nameid {
            return true;
        }
        if r.item.cards().contains(&nameid) {
            return true;
        }
    }
    false
}

/// pc.cpp pc_checkequip: the index worn at `pos` (-1 = none). `checkall`: skip empty positions of a multi-position mask.
pub fn pc_checkequip(c: &Ctx, aid: u32, pos: u32, checkall: bool) -> i16 {
    let Some(s) = st(c, aid) else { return -1 };
    for i in 0..EQI_MAX {
        if pos & EQUIP_BITMASK[i] != 0 {
            if checkall && pos & !EQUIP_BITMASK[i] != 0 && s.equip_index[i] == -1 {
                continue;
            }
            return s.equip_index[i];
        }
    }
    -1
}

/// pc.cpp pc_checkequip2: is `nameid` worn in the equip indexes min..max?
pub fn pc_checkequip2(c: &Ctx, aid: u32, nameid: u32, min: usize, max: usize) -> bool {
    let Some(s) = st(c, aid) else { return false };
    for i in min..max.min(EQI_MAX) {
        if EQUIP_BITMASK[i] != 0 {
            let idx = s.equip_index[i];
            // (rAthena reads items_inventory[-1] for an empty position: its nameid is never the one asked for)
            if idx >= 0 && inv_get(c, aid, idx as usize).map(|r| r.item.nameid == nameid).unwrap_or(false) {
                return true;
            }
        }
    }
    false
}

// ---------------------------------------------------------------------------------------------- pc_isequip

/// pc.cpp pc_isequip: ITEM_EQUIP_ACK_OK, _FAIL or _FAILLEVEL.
pub fn pc_isequip(c: &Ctx, aid: u32, n: usize) -> u8 {
    let Some(r) = inv_get(c, aid, n) else { return ITEM_EQUIP_ACK_FAIL };
    if pc::has_permission(c, aid, crate::rules::PC_PERM_USE_ALL_EQUIPMENT) {
        return ITEM_EQUIP_ACK_OK;
    }
    let item = data_of(&r.item);
    let Some(ch) = status(c, aid) else { return ITEM_EQUIP_ACK_FAIL };
    let Some(sd) = unit::get(c, aid) else { return ITEM_EQUIP_ACK_FAIL };
    let scl = crate::status::sc::load(c, aid);
    if scl.get(SC_SPIRIT).map(|e| e.val2 == crate::db::skills::SL_SUPERNOVICE as i32).unwrap_or(false) {
        // Spirit of Super Novice equip bonuses. [Skotlex]
        if ch.base_level >= 90 && item.equip & EQP_HELM != 0 {
            return ITEM_EQUIP_ACK_OK; // Can equip all helms
        }
        if ch.base_level >= 96 && item.equip & EQP_ARMS != 0 && item.r#type == itemdb::IT_WEAPON && item.weapon_level == 4 {
            // In weapons, the look determines type of weapon.
            if matches!(item.subtype, W_DAGGER | W_1HSWORD | W_1HAXE | W_2HAXE | W_MACE | W_STAFF | W_2HSTAFF) {
                return ITEM_EQUIP_ACK_OK;
            }
        }
    }
    if item.elv != 0 && (ch.base_level as u32) < item.elv as u32 {
        return ITEM_EQUIP_ACK_FAILLEVEL;
    }
    if item.elvmax != 0 && (ch.base_level as u32) > item.elvmax as u32 {
        return ITEM_EQUIP_ACK_FAILLEVEL;
    }
    if item.sex != itemdb::SEX_BOTH && ch.sex != item.sex {
        return ITEM_EQUIP_ACK_FAIL;
    }
    // Broken equip
    if r.item.attribute == 1 {
        return ITEM_EQUIP_ACK_FAIL;
    }
    // fail to equip if item is restricted
    if !allow_equip_restricted_item() && itemdb_is_no_equip(c, item, sd.map) {
        return ITEM_EQUIP_ACK_FAIL;
    }
    if item.equip & EQP_AMMO != 0 {
        let weapon = ch.weapon as u8;
        match item.subtype {
            itemdb::AMMO_ARROW => {
                if ammo_check_weapon() && weapon != W_BOW && weapon != W_MUSICAL && weapon != W_WHIP {
                    crate::clif_items::msg(c, &sd, crate::clif_items::MSI_FAIL_NEED_EQUIPPED_BOW);
                    return ITEM_EQUIP_ACK_FAIL;
                }
            }
            itemdb::AMMO_DAGGER => {
                if crate::skill::checkskill(c, aid, crate::db::skills::AS_VENOMKNIFE) == 0 {
                    return ITEM_EQUIP_ACK_FAIL;
                }
            }
            itemdb::AMMO_BULLET | itemdb::AMMO_SHELL => {
                if ammo_check_weapon() && weapon != W_REVOLVER && weapon != W_RIFLE && weapon != W_GATLING && weapon != W_SHOTGUN && weapon != W_GRENADE {
                    crate::clif_items::msg(c, &sd, crate::clif_items::MSI_WRONG_BULLET);
                    return ITEM_EQUIP_ACK_FAIL;
                }
            }
            itemdb::AMMO_CANNONBALL => {
                use crate::db::jobs::{JOB_GENETIC, JOB_GENETIC_T, JOB_MECHANIC, JOB_MECHANIC_T};
                let class = ch.class as i32;
                if sd.sc.option & OPTION_MADOGEAR == 0 && (class == JOB_MECHANIC_T || class == JOB_MECHANIC) {
                    crate::clif_items::msg(c, &sd, crate::clif_items::MSI_USESKILL_FAIL_MADOGEAR);
                    return ITEM_EQUIP_ACK_FAIL;
                }
                let active = pc::get(c, aid).map(|p| p.active).unwrap_or(false);
                if active && !pc_iscarton(c, aid) && (class == JOB_GENETIC_T || class == JOB_GENETIC) {
                    crate::clif_items::msg(c, &sd, crate::clif_items::MSI_USESKILL_FAIL_CART);
                    return ITEM_EQUIP_ACK_FAIL;
                }
            }
            _ => {}
        }
    }
    if !scl.empty() {
        if item.equip & EQP_ARMS != 0 && item.r#type == itemdb::IT_WEAPON && scl.has(SC_STRIPWEAPON) {
            // Also works with left-hand weapons [DracoRPG]
            return ITEM_EQUIP_ACK_FAIL;
        }
        if item.equip & EQP_SHIELD != 0 && item.r#type == itemdb::IT_ARMOR && scl.has(SC_STRIPSHIELD) {
            return ITEM_EQUIP_ACK_FAIL;
        }
        if item.equip & EQP_ARMOR != 0 && scl.has(SC_STRIPARMOR) {
            return ITEM_EQUIP_ACK_FAIL;
        }
        if item.equip & EQP_HEAD_TOP != 0 && scl.has(SC_STRIPHELM) {
            return ITEM_EQUIP_ACK_FAIL;
        }
        if item.equip & EQP_ACC != 0 && scl.has(SC__STRIPACCESSORY) {
            return ITEM_EQUIP_ACK_FAIL;
        }
        if item.equip & EQP_ARMS != 0 && scl.has(SC__WEAKNESS) {
            return ITEM_EQUIP_ACK_FAIL;
        }
        if item.equip & EQP_SHADOW_GEAR != 0 && scl.has(SC_SHADOW_STRIP) {
            return ITEM_EQUIP_ACK_FAIL;
        }
        if item.equip != 0 && (scl.has(SC_KYOUGAKU) || scl.has(SC_SUHIDE)) {
            return ITEM_EQUIP_ACK_FAIL;
        }
    }
    // Not equipable by class. [Skotlex]
    if !pc_job_can_use_item(&ch, item) {
        return ITEM_EQUIP_ACK_FAIL;
    }
    if !pc_is_item_class(&ch, item) {
        return ITEM_EQUIP_ACK_FAIL;
    }
    ITEM_EQUIP_ACK_OK
}

// ---------------------------------------------------------------------------------------------- equip / unequip

/// pc.cpp pc_equipitem: wear inventory item `n` at `req_pos` (or put it into the equip switch window).
pub fn pc_equipitem(c: &Ctx, aid: u32, n: i16, req_pos: u32, equipswitch: bool) -> bool {
    use crate::clif_items as ci;
    if n < 0 || n as usize >= MAX_INVENTORY {
        if equipswitch {
            ci::equipswitch_add(c, aid, n as u16, req_pos, ITEM_EQUIP_ACK_FAIL);
        }
        // Does not deserve an answer... [Lemongrass]
        return false;
    }
    let nu = n as usize;
    let Some(s0) = st(c, aid) else { return false };
    if s0.canequip_tick - c.tick() > 0 {
        if equipswitch {
            ci::equipswitch_add(c, aid, n as u16, req_pos, ITEM_EQUIP_ACK_FAIL);
        } else {
            ci::equipitemack(c, aid, ITEM_EQUIP_ACK_FAIL, n as i32, 0);
        }
        return false;
    }
    let Some(r) = inv_get(c, aid, nu) else { return false };
    let id = data_of(&r.item);
    let mut pos = pc_equippoint(c, aid, &r.item);
    // battle_log: off
    let res = pc_isequip(c, aid, nu);
    if res != ITEM_EQUIP_ACK_OK {
        if equipswitch {
            ci::equipswitch_add(c, aid, n as u16, req_pos, res);
        } else {
            ci::equipitemack(c, aid, res, n as i32, 0);
        }
        return false;
    }
    if equipswitch && id.r#type == itemdb::IT_AMMO {
        ci::equipswitch_add(c, aid, n as u16, req_pos, ITEM_EQUIP_ACK_FAIL);
        return false;
    }
    if pos & req_pos == 0 || r.item.equip != 0 {
        if equipswitch {
            ci::equipswitch_add(c, aid, n as u16, req_pos, ITEM_EQUIP_ACK_FAIL);
        } else {
            ci::equipitemack(c, aid, ITEM_EQUIP_ACK_FAIL, n as i32, 0);
        }
        return false;
    }
    if crate::status::sc::cant(c, aid, SCS_NOEQUIPITEM) || (crate::status::sc::has_sce(c, aid, SC_PYROCLASTIC) && id.r#type == itemdb::IT_WEAPON) {
        // status change that makes player cannot equip equipment (pc.cpp:12072-12079)
        if equipswitch {
            ci::equipswitch_add(c, aid, n as u16, req_pos, ITEM_EQUIP_ACK_FAIL);
        } else {
            ci::equipitemack(c, aid, ITEM_EQUIP_ACK_FAIL, n as i32, 0); //Fail
        }
        return false;
    }
    let equip_index = if equipswitch { s0.equip_switch_index.clone() } else { s0.equip_index.clone() };
    if !equipswitch && id.flag.bind_on_equip && r.item.bound == 0 {
        let mut r2 = r.clone();
        r2.item.bound = default_bind_on_equip();
        inv_put(c, &r2);
        ci::notify_bind_on_equip(c, aid, n);
    }
    if pos == EQP_ACC {
        // Accessories should only go in one of the two.
        pos = req_pos & EQP_ACC;
        if pos == EQP_ACC {
            // User specified both slots.
            pos = if equip_index[EQI_ACC_R] >= 0 && equip_index[EQI_ACC_L] < 0 { EQP_ACC_L } else { EQP_ACC_R };
        }
        for i in 0..(id.slots as usize).min(MAX_SLOTS) {
            let card = r.item.card(i);
            if card == 0 {
                continue;
            }
            if let Some(card_data) = itemdb::itemdb_exists(card) {
                let card_pos = card_data.equip;
                if card_pos == EQP_ACC_L || card_pos == EQP_ACC_R {
                    pos = card_pos; // Use the card's equip position
                    break;
                }
            }
        }
    } else if pos == EQP_ARMS && id.equip == EQP_HAND_R {
        // Dual wield capable weapon.
        pos = req_pos & EQP_ARMS;
        if pos == EQP_ARMS {
            // User specified both slots, pick one for them (RENEWAL).
            pos = if equip_index[EQI_HAND_R] >= 0 && equip_index[EQI_HAND_L] < 0 { EQP_HAND_L } else { EQP_HAND_R };
        }
    } else if pos == EQP_SHADOW_ACC {
        pos = req_pos & EQP_SHADOW_ACC;
        if pos == EQP_SHADOW_ACC {
            pos = if equip_index[EQI_SHADOW_ACC_L] >= 0 && equip_index[EQI_SHADOW_ACC_R] < 0 { EQP_SHADOW_ACC_R } else { EQP_SHADOW_ACC_L };
        }
    } else if pos == EQP_SHADOW_ARMS && id.equip == EQP_SHADOW_WEAPON {
        pos = req_pos & EQP_SHADOW_ARMS;
        if pos == EQP_SHADOW_ARMS {
            pos = if equip_index[EQI_SHADOW_WEAPON] >= 0 { EQP_SHADOW_SHIELD } else { EQP_SHADOW_WEAPON };
        }
    }
    let mut flag = false;
    if pos & EQP_HAND_R != 0 && use_weapon_skill_range() & crate::map::BL_PC != 0 {
        // Update skill-block range database when weapon range changes. [Skotlex]
        let i = equip_index[EQI_HAND_R];
        flag = match (i >= 0).then(|| inv_get(c, aid, i as usize)).flatten() {
            None => true,
            Some(w) => id.range != data_of(&w.item).range,
        };
    }
    if equipswitch {
        for i in 0..EQI_MAX {
            if pos & EQUIP_BITMASK[i] != 0 {
                // If there was already an item assigned to this slot
                let cur = st(c, aid).map(|s| s.equip_switch_index[i]).unwrap_or(-1);
                if cur >= 0 {
                    pc_equipswitch_remove(c, aid, cur as usize);
                }
                // Assign the new index to it
                if let Some(mut s) = st(c, aid) {
                    s.equip_switch_index[i] = n;
                    put_st(c, &s);
                }
            }
        }
        if let Some(mut r2) = inv_get(c, aid, nu) {
            r2.item.equip_switch = pos;
            inv_put(c, &r2);
        }
        ci::equipswitch_add(c, aid, n as u16, pos, ITEM_EQUIP_ACK_OK);
        return true;
    }
    for i in 0..EQI_MAX {
        if pos & EQUIP_BITMASK[i] != 0 {
            let cur = st(c, aid).map(|s| s.equip_index[i]).unwrap_or(-1);
            if cur >= 0 {
                // Slot taken, remove item from there.
                pc_unequipitem(c, aid, cur as i32, 1 | 2 | 4);
            }
            if let Some(mut s) = st(c, aid) {
                s.equip_index[i] = n;
                put_st(c, &s);
            }
        }
    }
    pc_equipswitch_remove(c, aid, nu);
    if pos == EQP_AMMO {
        ci::arrowequip(c, aid);
        ci::arrow_fail(c, aid, 3); // ARROWFAIL_SUCCESS
    } else {
        ci::equipitemack(c, aid, ITEM_EQUIP_ACK_OK, n as i32, pos);
    }
    if let Some(mut r2) = inv_get(c, aid, nu) {
        r2.item.equip = pos;
        inv_put(c, &r2);
    }
    if pos & EQP_HAND_R != 0 {
        if let Some(mut s) = st(c, aid) {
            s.weapontype1 = id.subtype;
            put_st(c, &s);
        }
        set_status_weapon(c, aid);
        let w = status(c, aid).map(|ch| ch.weapon as i32).unwrap_or(0);
        clif_changelook(c, aid, LOOK_WEAPON, w);
    }
    if pos & EQP_HAND_L != 0 {
        if let (Some(mut s), Some(mut ch)) = (st(c, aid), status(c, aid)) {
            if id.r#type == itemdb::IT_WEAPON {
                ch.shield = W_FIST as u16;
                s.weapontype2 = id.subtype;
            } else if id.r#type == itemdb::IT_ARMOR {
                ch.shield = id.look as u16;
                s.weapontype2 = W_FIST;
            }
            put_st(c, &s);
            put_status(c, &ch);
        }
        set_status_weapon(c, aid);
        let sh = status(c, aid).map(|ch| ch.shield as i32).unwrap_or(0);
        clif_changelook(c, aid, LOOK_SHIELD, sh);
    }
    if pos & EQP_SHOES != 0 {
        clif_changelook(c, aid, LOOK_SHOES, 0);
    }
    if ammo_unequip() && pos & EQP_ARMS != 0 && id.r#type == itemdb::IT_WEAPON {
        let idx = st(c, aid).map(|s| s.equip_index[EQI_AMMO]).unwrap_or(-1);
        if idx >= 0 {
            if let Some(ammo) = inv_get(c, aid, idx as usize) {
                match data_of(&ammo.item).subtype {
                    itemdb::AMMO_ARROW => {
                        if id.subtype != W_BOW && id.subtype != W_MUSICAL && id.subtype != W_WHIP {
                            pc_unequipitem(c, aid, idx as i32, 2 | 4);
                        }
                    }
                    itemdb::AMMO_BULLET | itemdb::AMMO_SHELL => {
                        if id.subtype != W_REVOLVER && id.subtype != W_RIFLE && id.subtype != W_GATLING && id.subtype != W_SHOTGUN && id.subtype != W_GRENADE {
                            pc_unequipitem(c, aid, idx as i32, 2 | 4);
                        }
                    }
                    _ => {}
                }
            }
        }
    }
    pc_set_costume_view(c, aid);
    crate::skilltree::pc_checkallowskill(c, aid); // Check if status changes should be halted (rA pc.cpp:12231)
    let iflag = crate::script::npc_run::sdx(c, aid).npc_item_flag;
    // Check for combos (MUST be before status_calc_pc)
    let it = inv_get(c, aid, nu).map(|r| r.item).unwrap_or_else(|| r.item.clone());
    if !id.combos.is_empty() {
        pc_checkcombo(c, aid, id);
    }
    if !itemdb::itemdb_isspecial(it.card0) {
        for i in 0..MAX_SLOTS {
            let card = it.card(i);
            if card == 0 {
                continue;
            }
            if let Some(data) = itemdb::itemdb_exists(card) {
                if !data.combos.is_empty() {
                    pc_checkcombo(c, aid, data);
                }
            }
        }
    }
    crate::status::calc_pc(c, aid, crate::status::SCO_NONE);
    if flag {
        // clif_skillinfoblock: skills (never with skillrange_from_weapon: 0)
    }
    // OnEquip script [Skotlex]
    let fake = crate::script::fake_npc_id(c);
    set_current_equip(c, aid, n, 0);
    let all_equipment = pc::has_permission(c, aid, crate::rules::PC_PERM_USE_ALL_EQUIPMENT);
    let m = unit::get(c, aid).map(|u| u.map).unwrap_or(0);
    if has_item_script(id.nameid, ItemScript::Equip) && (all_equipment || !itemdb_is_no_equip(c, id, m)) {
        run_item_script(c, aid, id.nameid, ItemScript::Equip, fake);
    }
    if !itemdb::itemdb_isspecial(it.card0) {
        for i in 0..MAX_SLOTS {
            let card = it.card(i);
            if card == 0 {
                continue;
            }
            if let Some(data) = itemdb::itemdb_exists(card) {
                if has_item_script(card, ItemScript::Equip) && (all_equipment || !itemdb_is_no_equip(c, data, m)) {
                    set_current_equip(c, aid, n, card);
                    run_item_script(c, aid, card, ItemScript::Equip, fake);
                }
            }
            set_current_equip(c, aid, n, 0);
        }
    }
    set_current_equip(c, aid, -1, 0);
    let mut x = crate::script::npc_run::sdx(c, aid);
    if x.npc_item_flag != iflag {
        x.npc_item_flag = iflag;
        crate::script::npc_run::put_sdx(c, &x);
    }
    true
}

/// status.cpp current_equip_item_index / current_equip_card_id (status/calc.rs; read by getequipid(EQI_COMPOUND_ON)
/// and the item script commands while an equip / unequip script runs).
fn set_current_equip(_c: &Ctx, _aid: u32, index: i16, card: u32) {
    crate::status::calc::set_item_index(index);
    crate::status::calc::set_card_id(card as i32);
}

/// pc.cpp pc_unequipitem_sub: recalculation, combos and the OnUnEquip scripts.
fn pc_unequipitem_sub(c: &Ctx, aid: u32, n: usize, flag: i32) {
    let Some(mut r) = inv_get(c, aid, n) else { return };
    for which in 1..=3 {
        crate::status::bonus::pc_deleteautobonus(c, aid, which, r.item.equip);
    }
    r.item.equip = 0;
    inv_put(c, &r);
    if flag & 4 == 0 {
        crate::skilltree::pc_checkallowskill(c, aid); // rA pc.cpp:12320-12321
    }
    let iflag = crate::script::npc_run::sdx(c, aid).npc_item_flag;
    let d = data_of(&r.item);
    let mut status_calc = false;
    if !d.combos.is_empty() && pc_removecombo(c, aid, d) > 0 {
        status_calc = true;
    }
    if !itemdb::itemdb_isspecial(r.item.card0) {
        for i in 0..MAX_SLOTS {
            let card = r.item.card(i);
            if card == 0 {
                continue;
            }
            if let Some(data) = itemdb::itemdb_exists(card) {
                if !data.combos.is_empty() && pc_removecombo(c, aid, data) > 0 {
                    status_calc = true;
                }
            }
        }
    }
    if flag & 1 != 0 || status_calc {
        crate::status::calc_pc(c, aid, crate::status::SCO_FORCE);
    }
    if crate::status::sc::has_sce(c, aid, SC_SIGNUMCRUCIS) {
        let bs = crate::status::data::sd_get(c, aid).map(|sd| sd.battle_status).unwrap_or_default();
        if !crate::status::sc::battle_check_undead(bs.race as i32, bs.def_ele as i32) {
            crate::status::sc::status_change_end(c, aid, SC_SIGNUMCRUCIS, 0);
        }
    }
    // OnUnEquip script [Skotlex]
    let fake = crate::script::fake_npc_id(c);
    set_current_equip(c, aid, n as i16, 0);
    run_item_script(c, aid, d.nameid, ItemScript::Unequip, fake);
    if !itemdb::itemdb_isspecial(r.item.card0) {
        for i in 0..MAX_SLOTS {
            let card = r.item.card(i);
            if card == 0 {
                continue;
            }
            if itemdb::itemdb_exists(card).is_some() && has_item_script(card, ItemScript::Unequip) {
                set_current_equip(c, aid, n as i16, card);
                run_item_script(c, aid, card, ItemScript::Unequip, fake);
            }
            set_current_equip(c, aid, n as i16, 0);
        }
    }
    set_current_equip(c, aid, -1, 0);
    let mut x = crate::script::npc_run::sdx(c, aid);
    if x.npc_item_flag != iflag {
        x.npc_item_flag = iflag;
        crate::script::npc_run::put_sdx(c, &x);
    }
}

/// pc.cpp pc_unequipitem. flag: 1 recalculate status, 2 force (ignore status changes that lock equipment),
/// 4 equip switch (keep status changes that need the weapon / armor).
pub fn pc_unequipitem(c: &Ctx, aid: u32, n: i32, flag: i32) -> bool {
    use crate::clif_items as ci;
    if n < 0 || n as usize >= MAX_INVENTORY {
        ci::unequipitemack(c, aid, 0, 0, false);
        return false;
    }
    let nu = n as usize;
    let Some(r) = inv_get(c, aid, nu) else {
        ci::unequipitemack(c, aid, n as u16, 0, false);
        return false;
    };
    let pos = r.item.equip;
    if pos == 0 {
        ci::unequipitemack(c, aid, n as u16, 0, false);
        return false; // Nothing to unequip
    }
    let d = data_of(&r.item);
    // status change that makes player cannot unequip equipment
    if flag & 2 == 0 && (crate::status::sc::cant(c, aid, SCS_NOUNEQUIPITEM) || (crate::status::sc::has_sce(c, aid, SC_PYROCLASTIC) && d.r#type == itemdb::IT_WEAPON)) {
        // can't switch weapon
        ci::unequipitemack(c, aid, n as u16, 0, false);
        return false;
    }
    if let Some(mut s) = st(c, aid) {
        for i in 0..EQI_MAX {
            if pos & EQUIP_BITMASK[i] != 0 {
                s.equip_index[i] = -1;
            }
        }
        put_st(c, &s);
    }
    if pos & EQP_HAND_R != 0 {
        if let Some(mut s) = st(c, aid) {
            s.weapontype1 = W_FIST;
            put_st(c, &s);
            if let Some(mut ch) = status(c, aid) {
                ch.weapon = s.weapontype2 as u16;
                put_status(c, &ch);
            }
        }
        set_status_weapon(c, aid);
        let w = status(c, aid).map(|ch| ch.weapon as i32).unwrap_or(0);
        clif_changelook(c, aid, LOOK_WEAPON, w);
        if !dancing_weaponswitch_fix() {
            crate::status::sc::status_change_end(c, aid, SC_DANCING, 0); // Unequipping => stop dancing.
        }
        // RENEWAL: switch_remove_edp & 2
        if switch_remove_edp() & 2 != 0 {
            crate::status::sc::status_change_end(c, aid, SC_EDP, 0);
        }
    }
    if pos & EQP_HAND_L != 0 {
        // Shield Spell cancel on a shield swap (skills)
        if let Some(mut s) = st(c, aid) {
            s.weapontype2 = W_FIST;
            put_st(c, &s);
        }
        if let Some(mut ch) = status(c, aid) {
            ch.shield = W_FIST as u16;
            put_status(c, &ch);
        }
        set_status_weapon(c, aid);
        let sh = status(c, aid).map(|ch| ch.shield as i32).unwrap_or(0);
        clif_changelook(c, aid, LOOK_SHIELD, sh);
    }
    if pos & EQP_SHOES != 0 {
        clif_changelook(c, aid, LOOK_SHOES, 0);
    }
    ci::unequipitemack(c, aid, n as u16, pos, true);
    pc_set_costume_view(c, aid);
    remove_by_status_flag(c, aid, &[SCF_REMOVEONUNEQUIP]);
    // On weapon change (right and left hand)
    if pos & EQP_ARMS != 0 && d.r#type == itemdb::IT_WEAPON {
        if ammo_unequip() && flag & 4 == 0 {
            match d.subtype {
                W_BOW | W_MUSICAL | W_WHIP | W_REVOLVER | W_RIFLE | W_GATLING | W_SHOTGUN | W_GRENADE => {
                    let idx = st(c, aid).map(|s| s.equip_index[EQI_AMMO]).unwrap_or(-1);
                    if idx >= 0 {
                        if let Some(mut s) = st(c, aid) {
                            s.equip_index[EQI_AMMO] = -1;
                            put_st(c, &s);
                        }
                        let ammo_pos = inv_get(c, aid, idx as usize).map(|a| a.item.equip).unwrap_or(0);
                        ci::unequipitemack(c, aid, idx as u16, ammo_pos, true);
                        pc_unequipitem_sub(c, aid, idx as usize, 0);
                    }
                }
                _ => {}
            }
        }
        remove_by_status_flag(c, aid, &[SCF_REMOVEONUNEQUIPWEAPON]);
    }
    // On armor change
    if pos & EQP_ARMOR != 0 {
        remove_by_status_flag(c, aid, &[SCF_REMOVEONUNEQUIPARMOR]);
    }
    // On ammo change (rAthena's condition: one of the three `!=` always holds)
    const ITEMID_SILVER_BULLET: u32 = 13201;
    const ITEMID_PURIFICATION_BULLET: u32 = 13220;
    const ITEMID_SILVER_BULLET_: u32 = 13221;
    if d.r#type == itemdb::IT_AMMO && (d.nameid != ITEMID_SILVER_BULLET || d.nameid != ITEMID_PURIFICATION_BULLET || d.nameid != ITEMID_SILVER_BULLET_) {
        crate::status::sc::status_change_end(c, aid, SC_P_ALTER, 0);
    }
    pc_unequipitem_sub(c, aid, nu, flag);
    true
}

// ---------------------------------------------------------------------------------------------- equip switch

/// pc.cpp pc_equipswitch: swap the items of the switch window at `index`'s mask with the worn ones.
pub fn pc_equipswitch(c: &Ctx, aid: u32, index: usize) -> u32 {
    let Some(r) = inv_get(c, aid, index) else { return 0 };
    let position = r.item.equip_switch;
    let equipped = pc_checkequip(c, aid, position, true);
    if equipped == -1 {
        pc_equipswitch_remove(c, aid, index);
        pc_equipitem(c, aid, index as i16, position, false);
        return position;
    }
    let mut unequipped: std::collections::BTreeMap<i32, u32> = std::collections::BTreeMap::new();
    let mut unequipped_position = 0u32;
    for i in 0..EQI_MAX {
        let unequip_index = st(c, aid).map(|s| s.equip_index[i]).unwrap_or(-1);
        if unequip_index >= 0 && position & EQUIP_BITMASK[i] != 0 {
            let e = inv_get(c, aid, unequip_index as usize).map(|u| u.item.equip).unwrap_or(0);
            unequipped.insert(unequip_index as i32, e);
            unequipped_position |= e;
            pc_unequipitem(c, aid, unequip_index as i32, 0);
        }
    }
    let all_position = position | unequipped_position;
    for i in 0..EQI_MAX {
        let exchange_index = st(c, aid).map(|s| s.equip_switch_index[i]).unwrap_or(-1);
        if exchange_index >= 0 && all_position & EQUIP_BITMASK[i] != 0 {
            let exchange_position = inv_get(c, aid, exchange_index as usize).map(|x| x.item.equip_switch).unwrap_or(0);
            pc_equipswitch_remove(c, aid, exchange_index as usize);
            pc_equipitem(c, aid, exchange_index, exchange_position, false);
        }
    }
    for (unequipped_index, unequipped_pos) in unequipped {
        if let Some(mut s) = st(c, aid) {
            for i in 0..EQI_MAX {
                if unequipped_pos & EQUIP_BITMASK[i] != 0 {
                    s.equip_switch_index[i] = unequipped_index as i16;
                }
            }
            put_st(c, &s);
        }
        if let Some(mut u) = inv_get(c, aid, unequipped_index as usize) {
            u.item.equip_switch = unequipped_pos;
            inv_put(c, &u);
        }
        crate::clif_items::equipswitch_add(c, aid, unequipped_index as u16, unequipped_pos, ITEM_EQUIP_ACK_OK);
    }
    all_position
}

/// pc.cpp pc_equipswitch_remove.
pub fn pc_equipswitch_remove(c: &Ctx, aid: u32, index: usize) {
    let Some(mut r) = inv_get(c, aid, index) else { return };
    if r.item.equip_switch == 0 {
        return;
    }
    if let Some(mut s) = st(c, aid) {
        for e in s.equip_switch_index.iter_mut() {
            if *e == index as i16 {
                *e = -1;
            }
        }
        put_st(c, &s);
    }
    crate::clif_items::equipswitch_remove(c, aid, index as u16, r.item.equip_switch, false);
    r.item.equip_switch = 0;
    inv_put(c, &r);
}

// ---------------------------------------------------------------------------------------------- combos

/// pc.cpp pc_checkcombo: combos of `data` that are now complete join sd->combos.
fn pc_checkcombo(c: &Ctx, aid: u32, data: &ItemData) -> i32 {
    let mut success = 0;
    for &combo_id in &data.combos {
        let Some(item_combo) = itemdb::combo::combo(combo_id) else { continue };
        let Some(s) = st(c, aid) else { return success };
        // Ensure this isn't a duplicate combo
        if s.combos.iter().any(|pc| pc.id == item_combo.id) {
            continue;
        }
        let nb = item_combo.nameid.len();
        if nb < 2 {
            continue;
        }
        #[derive(Clone, Default)]
        struct Chk {
            idx: i32,
            nameid: u32,
            card: [u32; MAX_SLOTS],
        }
        let mut combo_idx = vec![Chk::default(); nb];
        let mut pos = 0u32;
        let mut j = 0;
        while j < nb {
            let id = item_combo.nameid[j];
            let mut found = false;
            for k in 0..EQI_MAX {
                let index = s.equip_index[k];
                if index < 0 {
                    continue;
                }
                if pc_is_same_equip_index(k, &s.equip_index, index) {
                    continue;
                }
                let Some(r) = inv_get(c, aid, index as usize) else { continue };
                if itemdb::itemdb_type(id) != itemdb::IT_CARD {
                    if r.item.nameid != id {
                        continue;
                    }
                    if j > 0 {
                        // Check if this item not already used
                        if (0..nb - 1).any(|z| combo_idx[z].idx == index as i32 && combo_idx[z].nameid == id) {
                            continue;
                        }
                    }
                    combo_idx[j].nameid = id;
                    combo_idx[j].idx = index as i32;
                    pos |= r.item.equip;
                    found = true;
                    break;
                } else {
                    // Cards and enchants
                    if itemdb::itemdb_isspecial(r.item.card0) {
                        continue;
                    }
                    for z in 0..MAX_SLOTS {
                        if r.item.card(z) != id {
                            continue;
                        }
                        if j > 0 {
                            let already = (0..nb - 1).any(|c1| combo_idx[c1].idx == index as i32 && combo_idx[c1].nameid == id && combo_idx[c1].card.contains(&id));
                            if already {
                                continue;
                            }
                        }
                        combo_idx[j].nameid = id;
                        combo_idx[j].idx = index as i32;
                        combo_idx[j].card[z] = id;
                        pos |= r.item.equip;
                        found = true;
                        break;
                    }
                    if found {
                        break;
                    }
                }
            }
            if !found {
                break;
            }
            j += 1;
        }
        if j < nb {
            continue;
        }
        if let Some(mut s2) = st(c, aid) {
            s2.combos.push(PcCombo { id: item_combo.id, pos });
            put_st(c, &s2);
        }
        success += 1;
    }
    success
}

/// pc.cpp pc_removecombo.
fn pc_removecombo(c: &Ctx, aid: u32, data: &ItemData) -> i32 {
    let Some(s) = st(c, aid) else { return 0 };
    if s.combos.is_empty() {
        return 0;
    }
    let mut retval = 0;
    for &combo_id in &data.combos {
        let Some(mut s) = st(c, aid) else { return retval };
        let Some(p) = s.combos.iter().position(|pc| pc.id == combo_id) else { continue };
        s.combos.remove(p);
        put_st(c, &s);
        retval += 1;
        // Check if combo requirements still fit
        if pc_checkcombo(c, aid, data) > 0 {
            continue;
        }
        if st(c, aid).map(|s| s.combos.is_empty()).unwrap_or(true) {
            return retval;
        }
    }
    retval
}

/// pc.cpp pc_load_combo: every combo the worn items complete.
pub fn pc_load_combo(c: &Ctx, aid: u32) -> i32 {
    let mut ret = 0;
    for i in 0..EQI_MAX {
        let idx = st(c, aid).map(|s| s.equip_index[i]).unwrap_or(-1);
        if idx < 0 {
            continue;
        }
        let Some(r) = inv_get(c, aid, idx as usize) else { continue };
        let id = data_of(&r.item);
        if !id.combos.is_empty() {
            ret += pc_checkcombo(c, aid, id);
        }
        if !itemdb::itemdb_isspecial(r.item.card0) {
            for j in 0..MAX_SLOTS {
                let card = r.item.card(j);
                if card == 0 {
                    continue;
                }
                if let Some(data) = itemdb::itemdb_exists(card) {
                    if !data.combos.is_empty() {
                        ret += pc_checkcombo(c, aid, data);
                    }
                }
            }
        }
    }
    ret
}

// ---------------------------------------------------------------------------------------------- look

/// map_session_data::update_look for LOOK_WEAPON / LOOK_SHIELD: the view id (or item id) of the item in that hand.
fn update_look(c: &Ctx, aid: u32, vd: &mut unit::ViewData, option: i32, look: u8) {
    let s = st(c, aid);
    let eq = |eqi: usize| s.as_ref().map(|s| s.equip_index[eqi]).unwrap_or(-1);
    let view = |idx: i16| -> Option<i32> {
        let r = inv_get(c, aid, idx as usize)?;
        let id = data_of(&r.item);
        Some(if id.view_id != 0 { id.view_id as i32 } else { id.nameid as i32 })
    };
    match look {
        LOOK_WEAPON => {
            vd.weapon = if option & OPTION_COSTUME != 0 {
                0
            } else {
                let i = eq(EQI_HAND_R);
                if i >= 0 { view(i).unwrap_or(0) } else { 0 }
            };
        }
        LOOK_SHIELD => {
            vd.shield = if option & OPTION_COSTUME != 0 {
                0
            } else {
                let i = eq(EQI_HAND_L);
                if i >= 0 {
                    if i == eq(EQI_HAND_R) {
                        // 2-handed weapons or 2-handed shields are only sent on LOOK_WEAPON
                        0
                    } else {
                        view(i).unwrap_or(0)
                    }
                } else {
                    0
                }
            };
        }
        _ => {}
    }
}

/// The item part of status.cpp status_set_viewdata for a player: vd.look[LOOK_WEAPON] / [LOOK_SHIELD] (update_look)
/// and the headgear / robe looks from sd->status.
pub fn status_set_viewdata_items(c: &Ctx, aid: u32) {
    let Some(mut u) = unit::get(c, aid) else { return };
    let Some(ch) = status(c, aid) else { return };
    let option = u.sc.option;
    update_look(c, aid, &mut u.vd, option, LOOK_WEAPON);
    update_look(c, aid, &mut u.vd, option, LOOK_SHIELD);
    u.vd.head_top = ch.head_top as i32;
    u.vd.head_mid = ch.head_mid as i32;
    u.vd.head_bottom = ch.head_bottom as i32;
    u.vd.robe = ch.robe as i32;
    unit::save(c, &u);
}

/// clif.cpp clif_changelook for a player (PACKETVER >= 4).
pub fn clif_changelook(c: &Ctx, aid: u32, look: u8, val: i32) {
    let Some(mut u) = unit::get(c, aid) else { return };
    let option = u.sc.option;
    let mut val = val;
    match look {
        LOOK_WEAPON | LOOK_SHIELD => {
            update_look(c, aid, &mut u.vd, option, LOOK_WEAPON);
            update_look(c, aid, &mut u.vd, option, LOOK_SHIELD);
        }
        LOOK_BASE => {
            if val == JT_INVISIBLE {
                return;
            }
            update_look(c, aid, &mut u.vd, option, LOOK_WEAPON);
            update_look(c, aid, &mut u.vd, option, LOOK_SHIELD);
            // (*_ignorepalette: no)
            let _ = IGNORE_PALETTE;
            if u.vd.body2 != 0 && option & OPTION_COSTUME != 0 {
                u.vd.body2 = 0;
            }
        }
        LOOK_HAIR => u.vd.hair = val,
        LOOK_HEAD_BOTTOM => u.vd.head_bottom = val,
        LOOK_HEAD_TOP => u.vd.head_top = val,
        LOOK_HEAD_MID => u.vd.head_mid = val,
        LOOK_HAIR_COLOR => u.vd.hair_color = val,
        LOOK_CLOTHES_COLOR => u.vd.clothes_color = val,
        LOOK_SHOES => {
            let idx = st(c, aid).map(|s| s.equip_index[EQI_SHOES]).unwrap_or(-1);
            val = if idx >= 0 {
                match inv_get(c, aid, idx as usize) {
                    Some(r) => {
                        let d = data_of(&r.item);
                        if d.view_id > 0 { d.view_id as i32 } else { r.item.nameid as i32 }
                    }
                    None => 0,
                }
            } else {
                0
            };
            // Shoes? No packet uses this....
        }
        LOOK_BODY => return, // unknown purpose, dont send something
        LOOK_ROBE => u.vd.robe = val,
        LOOK_BODY2 => {
            if option & OPTION_COSTUME != 0 {
                val = status(c, aid).map(|ch| ch.class as i32).unwrap_or(val);
            }
            u.vd.body2 = val;
        }
        _ => {}
    }
    unit::save(c, &u);
    // prevent leaking the presence of GM-hidden objects
    let target = if option & OPTION_INVISIBLE != 0 { Target::SelfOnly } else { Target::Area };
    let (mut t, mut val2) = (look, 0);
    if t == LOOK_WEAPON || t == LOOK_SHIELD {
        t = LOOK_WEAPON;
        val = u.vd.weapon;
        val2 = u.vd.shield;
    }
    if crate::atcmd_disguise::disguised(c, &u) {
        clif::sprite_change(c, &u, u.id, t, val, val2, Target::AreaWos);
        clif::sprite_change(c, &u, crate::atcmd_disguise::disguised_bl_id(u.id), t, val, val2, Target::SelfOnly);
    } else {
        clif::sprite_change(c, &u, u.id, t, val, val2, target);
    }
}

/// pc.cpp pc_set_costume_view: the headgear / garment looks from the worn (costume) items and setlook overrides.
pub fn pc_set_costume_view(c: &Ctx, aid: u32) {
    let Some(s) = st(c, aid) else { return };
    let Some(mut ch) = status(c, aid) else { return };
    let (head_low, head_mid, head_top, robe) = (ch.head_bottom, ch.head_mid, ch.head_top, ch.robe);
    ch.head_bottom = 0;
    ch.head_mid = 0;
    ch.head_top = 0;
    ch.robe = 0;
    let data = |eqi: usize| -> Option<&'static ItemData> {
        let i = s.equip_index[eqi];
        if i == -1 {
            return None;
        }
        inv_get(c, aid, i as usize).map(|r| data_of(&r.item))
    };
    if let Some(id) = data(EQI_HEAD_LOW) {
        ch.head_bottom = if id.equip & (EQP_HEAD_MID | EQP_HEAD_TOP) == 0 { id.look as u16 } else { 0 };
    }
    if let Some(id) = data(EQI_HEAD_MID) {
        ch.head_mid = if id.equip & EQP_HEAD_TOP == 0 { id.look as u16 } else { 0 };
    }
    if let Some(id) = data(EQI_HEAD_TOP) {
        ch.head_top = id.look as u16;
    }
    if let Some(id) = data(EQI_GARMENT) {
        ch.robe = id.look as u16;
    }
    let m = unit::get(c, aid).map(|u| u.map).unwrap_or(0);
    if !crate::npc::mapflag(c, m, crate::npc::MF_NOCOSTUME) && ch.disable_showcostumes == 0 {
        if let Some(id) = data(EQI_COSTUME_HEAD_LOW) {
            ch.head_bottom = if id.equip & (EQP_COSTUME_HEAD_MID | EQP_COSTUME_HEAD_TOP) == 0 { id.look as u16 } else { 0 };
        }
        if let Some(id) = data(EQI_COSTUME_HEAD_MID) {
            ch.head_mid = if id.equip & EQP_COSTUME_HEAD_TOP == 0 { id.look as u16 } else { 0 };
        }
        if let Some(id) = data(EQI_COSTUME_HEAD_TOP) {
            ch.head_top = id.look as u16;
        }
        if let Some(id) = data(EQI_COSTUME_GARMENT) {
            ch.robe = id.look as u16;
        }
    }
    if s.setlook_head_bottom != 0 {
        ch.head_bottom = s.setlook_head_bottom as u16;
    }
    if s.setlook_head_mid != 0 {
        ch.head_mid = s.setlook_head_mid as u16;
    }
    if s.setlook_head_top != 0 {
        ch.head_top = s.setlook_head_top as u16;
    }
    if s.setlook_robe != 0 {
        ch.robe = s.setlook_robe as u16;
    }
    put_status(c, &ch);
    if head_low != ch.head_bottom {
        clif_changelook(c, aid, LOOK_HEAD_BOTTOM, ch.head_bottom as i32);
    }
    if head_mid != ch.head_mid {
        clif_changelook(c, aid, LOOK_HEAD_MID, ch.head_mid as i32);
    }
    if head_top != ch.head_top {
        clif_changelook(c, aid, LOOK_HEAD_TOP, ch.head_top as i32);
    }
    if robe != ch.robe {
        clif_changelook(c, aid, LOOK_ROBE, ch.robe as i32);
    }
}

/// pc.cpp pc_equiplookall.
pub fn pc_equiplookall(c: &Ctx, aid: u32) {
    let Some(ch) = status(c, aid) else { return };
    clif_changelook(c, aid, LOOK_WEAPON, 0);
    clif_changelook(c, aid, LOOK_SHOES, 0);
    clif_changelook(c, aid, LOOK_HEAD_BOTTOM, ch.head_bottom as i32);
    clif_changelook(c, aid, LOOK_HEAD_TOP, ch.head_top as i32);
    clif_changelook(c, aid, LOOK_HEAD_MID, ch.head_mid as i32);
    clif_changelook(c, aid, LOOK_ROBE, ch.robe as i32);
}
