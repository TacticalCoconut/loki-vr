//! status.cpp status_calc_bl_ / status_calc_pc_ / status_calc_pc_sub / status_calc_bl_main for players, with
//! status_calc_misc, status_base_atk / matk, status_base_amotion_pc, status_calc_maxhp_pc / maxsp_pc / maxap_pc,
//! status_calc_weight / status_calc_cart_weight, status_calc_regen / status_calc_regen_rate, and the pc.cpp helpers they
//! use (pc_setequipindex, pc_calcweapontype, pc_is_same_equip_index, pc_load_combo / pc_checkcombo / pc_removecombo,
//! pc_updateweightstatus, pc_maxaspd, pc_checkskill_summoner / imperial_guard, pc_famerank).

#![allow(clippy::collapsible_if, clippy::collapsible_else_if)]

use std::cell::Cell;

use super::calc_sc::{self as scc, PcBonusIn, PcView, Who};
use super::consts::*;
use super::data::{self, PcStatus, StatusData};
use super::defs::*;
use super::sc::{self, Sc};
use crate::character::{character, Character};
use crate::inventory::Item;
use crate::pc_equip::{itemdb_is_no_equip, pc_is_same_equip_index};
use crate::db::items::{self as itemdb, ItemData, IT_ARMOR, IT_SHADOWGEAR, IT_WEAPON};
use crate::db::jobs::{self, *};
use crate::db::skills::*;
use crate::unit::{self, Unit};
use crate::world::Ctx;

thread_local! {
    /// status_calc_pc_sub's `static int32 calculating` (recursive call preemption).
    static CALCULATING: Cell<i32> = const { Cell::new(0) };
    /// status.cpp current_equip_item_index: the inventory index of the item whose script runs (-1 = none). Set by
    /// status_calc_pc for the bonus scripts and by pc_equipitem / pc_unequipitem for the OnEquip / OnUnequip scripts.
    static CUR_ITEM_INDEX: Cell<i16> = const { Cell::new(-1) };
    /// status.cpp current_equip_combo_pos.
    static CUR_COMBO_POS: Cell<u32> = const { Cell::new(0) };
    /// status.cpp current_equip_card_id.
    static CUR_CARD_ID: Cell<i32> = const { Cell::new(0) };
    /// status.cpp current_equip_opt_index.
    static CUR_OPT_INDEX: Cell<i16> = const { Cell::new(-1) };
}

pub fn current_equip_item_index() -> i16 {
    CUR_ITEM_INDEX.with(|c| c.get())
}
pub fn current_equip_combo_pos() -> u32 {
    CUR_COMBO_POS.with(|c| c.get())
}
pub fn current_equip_card_id() -> i32 {
    CUR_CARD_ID.with(|c| c.get())
}
pub fn current_equip_opt_index() -> i16 {
    CUR_OPT_INDEX.with(|c| c.get())
}
pub fn set_item_index(v: i16) {
    CUR_ITEM_INDEX.with(|c| c.set(v));
}
pub fn set_combo_pos(v: u32) {
    CUR_COMBO_POS.with(|c| c.set(v));
}
pub fn set_card_id(v: i32) {
    CUR_CARD_ID.with(|c| c.set(v));
}
fn set_opt_index(v: i16) {
    CUR_OPT_INDEX.with(|c| c.set(v));
}
/// The globals above back to their idle values (a reducer call starts with no calculation or item script running;
/// one that failed part-way may have left them set).
pub fn reset_globals() {
    super::reset_in_pc_base_calc();
    set_calculating(0);
    set_item_index(-1);
    set_combo_pos(0);
    set_card_id(0);
    set_opt_index(-1);
}
fn calculating() -> i32 {
    CALCULATING.with(|c| c.get())
}
fn set_calculating(v: i32) {
    CALCULATING.with(|c| c.set(v));
}

// ---------------------------------------------------------------------------------------------- equipment

/// An equipped inventory slot and its item data (sd->inventory.u.items_inventory[index] / sd->inventory_data[index]).
/// The inventory, sd->equip_index and sd->combos are the items system's (inventory.rs `inventory` / `pc_items`,
/// pc_equip.rs pc_setequipindex / pc_checkcombo / pc_load_combo).
pub struct Equip {
    /// The inventory index.
    pub index: i16,
    pub item: Item,
    pub data: &'static ItemData,
}

/// sd->equip_index[EQI_MAX] (inventory indexes, -1 = nothing there).
pub fn equip_index(c: &Ctx, aid: u32) -> Vec<i16> {
    crate::inventory::st(c, aid).map(|s| s.equip_index).unwrap_or_else(|| vec![-1; EQI_MAX])
}

/// The equipped slot at inventory index `index` with its item data (sd->inventory_data[index]: itemdb_search, the
/// dummy item for an unknown id, as pc_setinventorydata makes it).
fn equip_at(c: &Ctx, aid: u32, index: i16) -> Option<Equip> {
    if index < 0 {
        return None;
    }
    let row = crate::inventory::inv_get(c, aid, index as usize)?;
    if row.item.nameid == 0 {
        return None;
    }
    let data = itemdb::itemdb_search(row.item.nameid);
    Some(Equip { index, item: row.item, data })
}

// ---------------------------------------------------------------------------------------------- small pc.cpp helpers

/// pc.cpp pc_checkskill_summoner.
pub fn pc_checkskill_summoner(w: &Who, kind: i32) -> i32 {
    let s = |id| w.checkskill(id);
    // uint8 count
    let count = match kind {
        SUMMONER_POWER_SEA => s(SU_TUNABELLY) + s(SU_TUNAPARTY) + s(SU_BUNCHOFSHRIMP) + s(SU_FRESHSHRIMP) + s(SU_GROOMING) + s(SU_PURRING) + s(SU_SHRIMPARTY),
        SUMMONER_POWER_LAND => s(SU_SV_STEMSPEAR) + s(SU_CN_POWDERING) + s(SU_CN_METEOR) + s(SU_SV_ROOTTWIST) + s(SU_CHATTERING) + s(SU_MEOWMEOW) + s(SU_NYANGGRASS),
        SUMMONER_POWER_LIFE => s(SU_SCAROFTAROU) + s(SU_PICKYPECK) + s(SU_ARCLOUSEDASH) + s(SU_LUNATICCARROTBEAT) + s(SU_HISS) + s(SU_POWEROFFLOCK) + s(SU_SVG_SPIRIT),
        _ => 0,
    };
    count as u8 as i32
}

/// pc.cpp pc_checkskill_imperial_guard (flag&1 IG_SHIELD_MASTERY, flag&2 IG_SPEAR_SWORD_M).
fn pc_checkskill_imperial_guard(w: &Who, shield: u16, weapon: i32, flag: i32) -> i32 {
    let mut count: i32 = 0;
    if flag & 1 != 0 && shield > 0 {
        count += w.checkskill(IG_SHIELD_MASTERY);
    }
    if flag & 2 != 0 && (weapon == W_1HSWORD || weapon == W_1HSPEAR || weapon == W_2HSPEAR) {
        count += w.checkskill(IG_SPEAR_SWORD_M);
    }
    count as u8 as i32
}

/// pc.cpp pc_maxaspd.
pub fn pc_maxaspd(class_: u64) -> i32 {
    if class_ & JOBL_THIRD != 0 {
        crate::conf::max_third_aspd()
    } else if (class_ & MAPID_SECONDMASK) == MAPID_KAGEROUOBORO || (class_ & MAPID_SECONDMASK) == MAPID_REBELLION {
        crate::conf::max_extended_aspd()
    } else if (class_ & MAPID_FIRSTMASK) == MAPID_SUMMONER {
        crate::conf::max_summoner_aspd()
    } else {
        crate::conf::max_aspd()
    }
}

/// pc.cpp pc_famerank: the character's place in the map server's fame list of `job`, 0 when not listed
/// (fame.rs keeps the lists).
pub fn pc_famerank(c: &Ctx, char_id: u32, job: u64) -> u8 {
    crate::fame::pc_famerank(c, char_id, job)
}

/// pc.hpp pc_is_taekwon_ranker (battle/player.conf taekwon_ranker_min_lv: 90).
fn pc_is_taekwon_ranker(c: &Ctx, sd: &PcStatus, ch: &Character) -> bool {
    (sd.class_ & MAPID_SECONDMASK) == MAPID_TAEKWON && ch.base_level >= 90 && pc_famerank(c, ch.id, MAPID_TAEKWON) != 0
}

/// skill.cpp skill_get_range2 for a player (battle/skill.conf skillrange_from_weapon: 0).
fn skill_get_range2_pc(w: &Who, range_status: u16, skill_id: u16, skill_lv: u16, is_server: bool) -> i32 {
    let mut range = skill_get_range(skill_id, skill_lv);
    if range < 0 {
        // battle_config.use_weapon_skill_range & BL_PC is 0
        let _ = range_status;
        range *= -1;
    }
    if is_server && range > 14 {
        range = 14; // Server-sided base range can't be above 14
    }
    let Some(skill) = skill_db().find(skill_id) else { return range };
    if skill.has_inf2(INF2_ALTERRANGEVULTURE) || skill.has_inf2(INF2_ALTERRANGESNAKEEYE) {
        if skill.has_inf2(INF2_ALTERRANGEVULTURE) {
            range += w.checkskill(AC_VULTURE);
        }
        if skill.has_inf2(INF2_ALTERRANGESNAKEEYE) {
            range += w.checkskill(GS_SNAKEEYE);
        }
    }
    if skill.has_inf2(INF2_ALTERRANGESHADOWJUMP) || skill.has_inf2(INF2_ALTERRANGERADIUS) || skill.has_inf2(INF2_ALTERRANGERESEARCHTRAP) {
        if skill.has_inf2(INF2_ALTERRANGESHADOWJUMP) {
            range = skill_get_range(NJ_SHADOWJUMP, w.checkskill(NJ_SHADOWJUMP) as u16);
        }
        if skill.has_inf2(INF2_ALTERRANGERADIUS) {
            range += w.checkskill(WL_RADIUS);
        }
        if skill.has_inf2(INF2_ALTERRANGERESEARCHTRAP) {
            let rt_range = [0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5];
            range += rt_range.get(w.checkskill(RA_RESEARCHTRAP) as usize).copied().unwrap_or(0);
        }
    }
    range
}

// ---------------------------------------------------------------------------------------------- base values

/// status.cpp status_base_atk (RENEWAL) for a player.
pub fn status_base_atk_pc(weapon: i32, status: &StatusData, level: i32) -> u16 {
    if crate::conf::enable_baseatk_renewal() & BL_PC == 0 {
        return 0;
    }
    let flag = matches!(weapon, W_BOW | W_MUSICAL | W_WHIP | W_REVOLVER | W_RIFLE | W_GATLING | W_SHOTGUN | W_GRENADE);
    let (dstr, dex) = if flag { (status.dex as i32, status.str as i32) } else { (status.str as i32, status.dex as i32) };
    let str = (dstr * 10 + dex * 10 / 5 + status.luk as i32 * 10 / 3 + level * 10 / 4) / 10 + 5 * status.pow as i32;
    cap_value(str, 0, u16::MAX as i32) as u16
}

/// status.cpp status_weapon_atk (RENEWAL).
pub fn status_weapon_atk(wa: &data::WeaponAtk) -> u32 {
    wa.atk as u32 + wa.atk2 as u32
}

/// status.cpp status_base_matk_min / status_base_matk_max (RENEWAL, BL_PC; both are the same formula).
pub fn status_base_matk_pc(status: &StatusData, level: i32) -> u16 {
    let v = status.int_ as i32 + (status.int_ as i32 / 2) + (status.dex as i32 / 5) + (status.luk as i32 / 3) + (level / 4) + 5 * status.spl as i32;
    v as u16
}

/// status.cpp status_calc_misc for a player.
fn status_calc_misc_pc(c: &Ctx, sd: &mut PcStatus, ch: &Character, level: i32, use_base: bool) {
    let weapon = ch.weapon as i32;
    let status = if use_base { &mut sd.base_status } else { &mut sd.battle_status };
    // (players stack with previous bonuses)
    let mut stat: i32;
    // Hit
    stat = status.hit as i32;
    stat += level + status.dex as i32 + status.luk as i32 / 3 + 175;
    stat += 2 * status.con as i32;
    status.hit = cap_value(stat, 1, i16::MAX as i32) as i16;
    // Flee
    stat = status.flee as i32;
    stat += level + status.agi as i32 + status.luk as i32 / 5 + 100;
    stat += 2 * status.con as i32;
    status.flee = cap_value(stat, 1, i16::MAX as i32) as i16;
    // Def2
    stat = status.def2 as i32;
    stat += ((level as f32 + status.vit as f32) / 2.0 + (status.agi as f32 / 5.0)) as i32;
    status.def2 = cap_value(stat, 0, i16::MAX as i32) as i16;
    // Mdef2
    stat = status.mdef2 as i32;
    stat += (status.int_ as f32 + (level as f32 / 4.0) + ((status.dex as i32 + status.vit as i32) as f32 / 5.0)) as i32;
    status.mdef2 = cap_value(stat, 0, i16::MAX as i32) as i16;
    // PAtk
    stat = status.patk as i32;
    stat += status.pow as i32 / 3 + status.con as i32 / 5;
    status.patk = cap_value(stat, 0, i16::MAX as i32) as i16;
    // SMatk
    stat = status.smatk as i32;
    stat += status.spl as i32 / 3 + status.con as i32 / 5;
    status.smatk = cap_value(stat, 0, i16::MAX as i32) as i16;
    // Res
    stat = status.res as i32;
    stat += status.sta as i32 + status.sta as i32 / 3 * 5;
    status.res = cap_value(stat, 0, i16::MAX as i32) as i16;
    // Mres
    stat = status.mres as i32;
    stat += status.wis as i32 + status.wis as i32 / 3 * 5;
    status.mres = cap_value(stat, 0, i16::MAX as i32) as i16;
    // HPlus
    stat = status.hplus as i32;
    stat += status.crt as i32;
    status.hplus = cap_value(stat, 0, i16::MAX as i32) as i16;
    // CRate
    stat = status.crate_ as i32;
    stat += status.crt as i32 / 3;
    status.crate_ = cap_value(stat, 0, i16::MAX as i32) as i16;
    // MAtk
    status.matk_min = status_base_matk_pc(status, level);
    status.matk_max = status_base_matk_pc(status, level);
    // Critical
    if crate::conf::enable_critical() & BL_PC != 0 {
        stat = status.cri as i32;
        stat += level / 10; // (every 10 BaseLevel = +0.1 critical)
        stat += 10 + status.luk as i32 * 3; // (every 1 luk = +0.3 critical)
        status.cri = cap_value(stat, 1, i16::MAX as i32) as i16;
    } else {
        status.cri = 0;
    }
    if crate::conf::enable_perfect_flee() & BL_PC != 0 {
        stat = status.flee2 as i32;
        stat += status.luk as i32 + 10; // (every 10 luk = +1 perfect flee)
        status.flee2 = cap_value(stat, 0, i16::MAX as i32) as i16;
    } else {
        status.flee2 = 0;
    }
    status.batk += status_base_atk_pc(weapon, status, level) as i32;
    // (players have no critical adjustment setting)
    let st = status.clone();
    status_calc_regen(c, sd, ch, &st);
}

/// status.cpp status_base_amotion_pc (RENEWAL_ASPD).
fn status_base_amotion_pc(w: &Who, sd: &PcStatus, ch: &Character, status: &StatusData, sc: &Sc) -> i32 {
    let Some(job) = jobs::job_db().find(ch.class) else { return 0 };
    let mut val: i16 = 0;
    let aspd_base = |i: i32| job.aspd_base.get(i as usize).copied().unwrap_or(0) as i32;
    // sd->weapontype1 / weapontype2 / equip_index (pc_items)
    let Some(it) = crate::inventory::st(w.c, w.id) else { return 0 };
    let mut aspd = aspd_base(it.weapontype1 as i32); // Single weapon
    if ch.shield != 0 {
        aspd += aspd_base(MAX_WEAPON_TYPE);
    } else if it.weapontype2 as i32 != W_FIST && it.equip_index[EQI_HAND_R] != it.equip_index[EQI_HAND_L] {
        aspd += aspd_base(it.weapontype2 as i32) / 4; // Dual-wield
    }
    let (dex, agi) = (status.dex as i32, status.agi as i32);
    let mut temp_aspd: f32 = match ch.weapon as i32 {
        W_BOW | W_MUSICAL | W_WHIP | W_REVOLVER | W_RIFLE | W_GATLING | W_SHOTGUN | W_GRENADE => (dex * dex) as f32 / 7.0f32 + (agi * agi) as f32 * 0.5f32,
        _ => (dex * dex) as f32 / 5.0f32 + (agi * agi) as f32 * 0.5f32,
    };
    temp_aspd = ((temp_aspd as f64).sqrt() * 0.25f64) as f32 + 196.0f32;
    let skill_lv = w.checkskill(SA_ADVANCEDBOOK) as i16;
    if skill_lv > 0 && ch.weapon as i32 == W_BOOK {
        val += (skill_lv - 1) / 2 + 1;
    }
    let skill_lv = w.checkskill(SG_DEVIL) as i16;
    if skill_lv > 0 && ((sd.class_ & MAPID_THIRDMASK) == MAPID_STAR_EMPEROR || jobs::pc_is_maxjoblv(ch.class, ch.job_level as u32)) {
        val += 1 + skill_lv;
    }
    let skill_lv = w.checkskill(GS_SINGLEACTION) as i16;
    if skill_lv > 0 && (ch.weapon as i32 >= W_REVOLVER && ch.weapon as i32 <= W_GRENADE) {
        val += (skill_lv + 1) / 2;
    }
    let option = w.pc.as_ref().map(|p| p.option).unwrap_or(0);
    if option & OPTION_RIDING != 0 {
        val -= 50 - 10 * w.checkskill(KN_CAVALIERMASTERY) as i16;
    } else if option & OPTION_DRAGON != 0 {
        val -= 25 - 5 * w.checkskill(RK_DRAGONTRAINING) as i16;
    }
    let fixed = scc::status_calc_aspd(w, sc, true) as i32;
    (temp_aspd + ((fixed + val as i32) as f32 * agi as f32 / 200.0f32)) as i32 - aspd.min(200)
}

/// status.cpp status_calc_maxhp_pc.
fn status_calc_maxhp_pc(w: &Who, sd: &PcStatus, ch: &Character, sc: &Sc, vit: u32) -> u32 {
    let Some(job) = jobs::job_db().find(ch.class) else { return 1 };
    let level = (ch.base_level as u32).max(1) - 1;
    let mut dmax: f64 = job.base_hp.get(level as usize).copied().unwrap_or(0) as f64;
    if vit > 0 {
        dmax *= 1.0 + vit as f64 * 0.01;
    }
    if (sd.class_ & JOBL_UPPER) != 0 || jobs::pc_is_primary_fourth(sd.class_) {
        dmax *= 1.25;
    } else if pc_is_taekwon_ranker(w.c, sd, ch) {
        dmax *= 3.0;
    }
    // Vit from equip gives +1 additional HP
    dmax += sd.indexed_bonus.param_equip[PARAM_VIT] as f64;
    let pin = PcBonusIn { bonus_hp: sd.bonus.hp, bonus_sp: sd.bonus.sp, bonus_ap: sd.bonus.ap, sex: ch.sex };
    dmax += scc::status_get_hpbonus(w, sc, Some(&pin), STATUS_BONUS_FIX) as f64;
    let equip_bonus = dmax * (sd.rates.hprate - 100) as f64 / 100.0;
    let item_bonus = dmax * scc::status_get_hpbonus_item(sc) as f64 / 100.0;
    dmax += equip_bonus + item_bonus;
    // Aegis accuracy
    dmax += (dmax * scc::status_get_hpbonus(w, sc, Some(&pin), STATUS_BONUS_RATE) as f64 / 100.0) as i64 as f64;
    if dmax < 1.0 {
        dmax = 1.0;
    }
    cap_value(dmax as u32, 1, u32::MAX)
}

/// status.cpp status_calc_maxsp_pc.
fn status_calc_maxsp_pc(w: &Who, sd: &PcStatus, ch: &Character, sc: &Sc, int_: u32) -> u32 {
    let Some(job) = jobs::job_db().find(ch.class) else { return 1 };
    let level = (ch.base_level as u32).max(1) - 1;
    let mut dmax: f64 = job.base_sp.get(level as usize).copied().unwrap_or(0) as f64;
    if int_ > 0 {
        dmax *= 1.0 + int_ as f64 * 0.01;
    }
    if (sd.class_ & JOBL_UPPER) != 0 || jobs::pc_is_primary_fourth(sd.class_) {
        dmax *= 1.25;
    } else if pc_is_taekwon_ranker(w.c, sd, ch) {
        dmax *= 3.0;
    }
    // Int from equip gives +1 additional SP
    dmax += sd.indexed_bonus.param_equip[PARAM_INT] as f64;
    let pin = PcBonusIn { bonus_hp: sd.bonus.hp, bonus_sp: sd.bonus.sp, bonus_ap: sd.bonus.ap, sex: ch.sex };
    dmax += scc::status_get_spbonus(w, sc, Some(&pin), STATUS_BONUS_FIX) as f64;
    let equip_bonus = dmax * (sd.rates.sprate - 100) as f64 / 100.0;
    let item_bonus = dmax * scc::status_get_spbonus_item(sc) as f64 / 100.0;
    dmax += equip_bonus + item_bonus;
    dmax += (dmax * scc::status_get_spbonus(w, sc, Some(&pin), STATUS_BONUS_RATE) as f64 / 100.0) as i64 as f64;
    if dmax < 1.0 {
        dmax = 1.0;
    }
    cap_value(dmax as u32, 1, u32::MAX)
}

/// status.cpp status_calc_maxap_pc.
fn status_calc_maxap_pc(w: &Who, sd: &PcStatus, ch: &Character, sc: &Sc) -> u32 {
    let Some(job) = jobs::job_db().find(ch.class) else { return 1 };
    let level = (ch.base_level as u32).max(1) - 1;
    let mut dmax: f64 = job.base_ap.get(level as usize).copied().unwrap_or(0) as f64;
    let pin = PcBonusIn { bonus_hp: sd.bonus.hp, bonus_sp: sd.bonus.sp, bonus_ap: sd.bonus.ap, sex: ch.sex };
    dmax += scc::status_get_apbonus(w, sc, Some(&pin), STATUS_BONUS_FIX) as f64;
    let equip_bonus = dmax * (sd.rates.aprate - 100) as f64 / 100.0;
    let item_bonus = dmax * scc::status_get_apbonus_item(sc) as f64 / 100.0;
    dmax += equip_bonus + item_bonus;
    dmax += (dmax * scc::status_get_apbonus(w, sc, Some(&pin), STATUS_BONUS_RATE) as f64 / 100.0) as i64 as f64;
    if dmax < 0.0 {
        dmax = 0.0;
    }
    cap_value(dmax as u32, 0, u32::MAX)
}

/// The pc-side view the per-stat functions need.
pub fn pc_view(c: &Ctx, sd: &PcStatus, ch: &Character, u: &Unit) -> PcView {
    let _ = c;
    PcView {
        class_: sd.class_,
        sex: ch.sex,
        weapon: ch.weapon as i32,
        speed_rate: sd.bonus.speed_rate,
        speed_add_rate: sd.bonus.speed_add_rate,
        permanent_speed: sd.permanent_speed,
        casting: u.ud.skilltimer != 0,
        skill_id: u.ud.skill_id,
        skill_lv: u.ud.skill_lv,
        option: u.sc.option,
    }
}

pub(crate) fn who<'a>(c: &'a Ctx<'a>, sd: &PcStatus, ch: &Character, u: &Unit) -> Who<'a> {
    Who { c, kind: BL_PC, id: u.id, map: u.map, pc: Some(pc_view(c, sd, ch, u)) }
}

/// The per-stat functions' view of an online player.
pub fn who_of<'a>(c: &'a Ctx<'a>, aid: u32) -> Option<Who<'a>> {
    let sd = data::sd_get(c, aid)?;
    let ch = c.db().character().id().find(sd.char_id)?;
    let u = unit::get(c, aid)?;
    Some(who(c, &sd, &ch, &u))
}

// ---------------------------------------------------------------------------------------------- weight

/// status.cpp status_calc_weight. sd->weight / sd->max_weight are the items system's `pc_items` row (pc_additem /
/// pc_delitem keep sd->weight up to date between recounts); sd->add_max_weight is the status row's bonus. Sends
/// SP_WEIGHT / SP_MAXWEIGHT when they change.
pub fn status_calc_weight(c: &Ctx, aid: u32, flag: u8) -> bool {
    let Some(sd) = data::sd_get(c, aid) else { return false };
    let Some(mut it) = crate::inventory::st(c, aid) else { return false };
    let Some(ch) = c.db().character().id().find(sd.char_id) else { return false };
    let w = match unit::get(c, aid) {
        Some(u) => who(c, &sd, &ch, &u),
        None => return false,
    };
    let b_max_weight = it.max_weight; // Store max weight for later comparison
    let b_weight = it.weight; // Store current weight for later comparison
    let job = pc_mapid2jobid(sd.class_, ch.sex as i32);
    it.max_weight = (jobs::job_db().get_max_weight(job as u16) + ch.str as i32 * 300) as u32; // Base weight + STR bonus
    if flag & CALCWT_ITEM != 0 {
        it.weight = 0; // Reset current weight
        for r in crate::inventory::inv_list(c, aid) {
            if r.item.nameid == 0 {
                continue;
            }
            it.weight = it.weight.wrapping_add(crate::inventory::data_of(&r.item).weight.wrapping_mul(r.item.amount as u32));
        }
    }
    if flag & CALCWT_MAXBONUS != 0 {
        // Skill/Status bonus weight increases
        it.max_weight = it.max_weight.wrapping_add(sd.add_max_weight);
        let skill = w.checkskill(MC_INCCARRY);
        if skill > 0 {
            it.max_weight = it.max_weight.wrapping_add((2000 * skill) as u32);
        }
        let option = w.pc.as_ref().map(|p| p.option).unwrap_or(0);
        if option & OPTION_RIDING != 0 && w.checkskill(KN_RIDING) > 0 {
            it.max_weight += 10000;
        } else if option & OPTION_DRAGON != 0 {
            it.max_weight = it.max_weight.wrapping_add((5000 + 2000 * w.checkskill(RK_DRAGONTRAINING)) as u32);
        }
        if let Some(e) = sc::get_sce(c, aid, SC_KNOWLEDGE) {
            it.max_weight = it.max_weight.wrapping_add((it.max_weight as i64 * e.val1 as i64 / 10) as u32);
        }
        let skill = w.checkskill(ALL_INCCARRY);
        if skill > 0 {
            it.max_weight = it.max_weight.wrapping_add((2000 * skill) as u32);
        }
        if option & OPTION_MADOGEAR != 0 {
            it.max_weight += 15000;
        }
    }
    let (weight, max_weight) = (it.weight, it.max_weight);
    crate::inventory::put_st(c, &it);
    // Update the client if the new weight calculations don't match
    if b_weight != weight {
        super::clif::updatestatus(c, aid, SP_WEIGHT);
    }
    if b_max_weight != max_weight {
        super::clif::updatestatus(c, aid, SP_MAXWEIGHT);
        pc_updateweightstatus(c, aid);
    }
    true
}

/// status.cpp status_calc_cart_weight (the cart's items are the items system's `cart_inventory`).
pub fn status_calc_cart_weight(c: &Ctx, aid: u32, flag: u8) -> bool {
    // NEW_CARTS: pc_iscarton is SC_PUSH_CART
    if !crate::inventory::pc_iscarton(c, aid) && flag & CALCWT_CARTSTATE == 0 {
        return false;
    }
    let Some(mut it) = crate::inventory::st(c, aid) else { return false };
    let b_cart_weight_max = it.cart_weight_max; // Store cart max weight for later comparison
    it.cart_weight_max = crate::conf::max_cart_weight() as u32; // Base cart weight
    if flag & CALCWT_ITEM != 0 {
        it.cart_weight = 0; // Reset current cart weight
        it.cart_num = 0; // Reset current cart item count
        for r in crate::cart::cart_list(c, aid) {
            if r.item.nameid == 0 {
                continue;
            }
            // Recalculate current cart weight
            it.cart_weight = it.cart_weight.wrapping_add(itemdb::itemdb_weight(r.item.nameid).wrapping_mul(r.item.amount as u32));
            it.cart_num += 1; // Recalculate current cart item count
        }
    }
    // Skill bonus max weight increases
    if flag & CALCWT_MAXBONUS != 0 {
        it.cart_weight_max += crate::skill::checkskill(c, aid, GN_REMODELING_CART) as u32 * 5000;
    }
    let changed = b_cart_weight_max != it.cart_weight_max;
    crate::inventory::put_st(c, &it);
    // Update the client if the new weight calculations don't match
    if changed {
        super::clif::updatestatus(c, aid, SP_CARTINFO);
    }
    true
}

/// pc.cpp pc_getpercentweight (weight 0 = the current weight).
pub fn pc_getpercentweight(it: &crate::inventory::PcItems, weight: u32) -> u16 {
    let weight = if weight == 0 { it.weight } else { weight };
    (weight as u64 * 100 / it.max_weight.max(1) as u64) as u16
}

/// pc.cpp pc_updateweightstatus: SC_WEIGHT50 / SC_WEIGHT90 follow the weight percentage.
pub fn pc_updateweightstatus(c: &Ctx, aid: u32) {
    let Some(it) = crate::inventory::st(c, aid) else { return };
    let old_overweight: u8 = if sc::has_sce(c, aid, SC_WEIGHT90) {
        2
    } else if sc::has_sce(c, aid, SC_WEIGHT50) {
        1
    } else {
        0
    };
    let overweight_percent = pc_getpercentweight(&it, 0) as u32;
    let new_overweight: u8 = if overweight_percent >= crate::conf::major_overweight_rate() {
        2
    } else if overweight_percent >= crate::conf::natural_heal_weight_rate() {
        1
    } else {
        0
    };
    // No change
    if old_overweight == new_overweight {
        return;
    }
    let Some(u) = unit::get(c, aid) else { return };
    match new_overweight {
        0 => {
            sc::status_change_end(c, aid, SC_WEIGHT50, 0);
            sc::status_change_end(c, aid, SC_WEIGHT90, 0);
        }
        1 => {
            sc::sc_start(c, Some(&u), &u, SC_WEIGHT50, 100, 0, 0);
        }
        _ => {
            sc::sc_start(c, Some(&u), &u, SC_WEIGHT90, 100, 0, 0);
        }
    }
    // Update overweight status
    data::with_sd(c, aid, |sd| sd.regen.overweight = new_overweight != 0);
}

// ---------------------------------------------------------------------------------------------- regen

/// status.cpp status_calc_regen for a player (`status` = the status the values come from).
fn status_calc_regen(c: &Ctx, sd: &mut PcStatus, ch: &Character, status: &StatusData) {
    let aid = sd.account_id;
    let sk = |id| crate::skill::checkskill(c, aid, id) as i32;
    let reg_flag = 0; // bl->type == BL_PC
    let mut val: i32 = (status.vit as i32 / 5) + 1.max(status.max_hp as i32 / 200);
    if sd.rates.hprecov_rate != 100 {
        val = val * sd.rates.hprecov_rate / 100;
    }
    sd.regen.hp = cap_value(val, reg_flag, i16::MAX as i32) as u16;
    val = 1 + (status.int_ as i32 / 6) + (status.max_sp as i32 / 100);
    if status.int_ >= 120 {
        val += ((status.int_ as i32 - 120) / 2) + 4;
    }
    if sd.rates.sprecov_rate != 100 {
        val = val * sd.rates.sprecov_rate / 100;
    }
    sd.regen.sp = cap_value(val, reg_flag, i16::MAX as i32) as u16;
    let skill = sk(HP_MEDITATIO);
    if skill > 0 {
        val = sd.regen.sp as i32 * (100 + 3 * skill) / 100;
        sd.regen.sp = cap_value(val, 1, i16::MAX as i32) as u16;
    }
    // Only players have skill/sitting skill regen for now.
    val = 0;
    let skill = sk(SM_RECOVERY);
    if skill > 0 {
        val += skill * 5 + skill * status.max_hp as i32 / 500;
    }
    sd.sregen.hp = cap_value(val, 0, i16::MAX as i32) as u16;
    val = 0;
    let skill = sk(MG_SRECOVERY);
    if skill > 0 {
        val += skill * 3 + skill * status.max_sp as i32 / 500;
    }
    let skill = sk(NJ_NINPOU);
    if skill > 0 {
        val += skill * 3 + skill * status.max_sp as i32 / 500;
    }
    let skill = sk(WM_LESSON);
    if skill > 0 {
        val += 3 + 3 * skill;
    }
    if let Some(e) = sc::get_sce(c, aid, SC_ANCILLA) {
        val += e.val2 / 100;
    }
    sd.sregen.sp = cap_value(val, 0, i16::MAX as i32) as u16;
    // Skill-related recovery (only when sit)
    val = 0;
    let skill = sk(MO_SPIRITSRECOVERY);
    if skill > 0 {
        val += skill * 4 + skill * status.max_hp as i32 / 500;
    }
    let skill = sk(TK_HPTIME);
    if skill > 0 && sd.rest {
        val += skill * 30 + skill * status.max_hp as i32 / 500;
    }
    sd.ssregen.hp = cap_value(val, 0, i16::MAX as i32) as u16;
    val = 0;
    let skill = sk(TK_SPTIME);
    if skill > 0 && sd.rest {
        val += skill * 3 + skill * status.max_sp as i32 / 500;
        let kaina = sk(SL_KAINA);
        if kaina > 0 {
            // Power up Enjoyable Rest
            val += (30 + 10 * kaina) * val / 100;
        }
    }
    let skill = sk(MO_SPIRITSRECOVERY);
    if skill > 0 {
        val += skill * 2 + skill * status.max_sp as i32 / 500;
    }
    sd.ssregen.sp = cap_value(val, 0, i16::MAX as i32) as u16;
    let _ = ch;
}

/// skill.cpp skill_sit_in / skill_sit_out: `status_calc_regen(bl, &sd->battle_status, &sd->regen);
/// status_calc_regen_rate(bl, &sd->regen, &sd->sc);` outside a full recalculation.
pub fn recalc_regen(c: &Ctx, aid: u32) {
    let Some(mut sd) = data::sd_get(c, aid) else { return };
    let Some(ch) = crate::pc::character_of(c, aid) else { return };
    let status = sd.battle_status.clone();
    status_calc_regen(c, &mut sd, &ch, &status);
    let scl = sc::load(c, aid);
    status_calc_regen_rate(c, &mut sd, &scl);
    data::sd_put(c, &sd);
}

/// status.cpp status_calc_regen_rate for a player.
fn status_calc_regen_rate(c: &Ctx, sd: &mut PcStatus, sc: &Sc) {
    let regen = &mut sd.regen;
    regen.flag = RGN_HP | RGN_SP;
    if regen.has_sub {
        if sd.sregen.hp != 0 {
            regen.flag |= RGN_SHP;
        }
        if sd.sregen.sp != 0 {
            regen.flag |= RGN_SSP;
        }
        sd.sregen.rate_hp = 100;
        sd.sregen.rate_sp = 100;
        if sd.ssregen.hp != 0 {
            regen.flag |= RGN_SHP;
        }
        if sd.ssregen.sp != 0 {
            regen.flag |= RGN_SSP;
        }
        sd.ssregen.rate_hp = 100;
        sd.ssregen.rate_sp = 100;
    }
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
    if sc.has(SC_DANCING)
        || sc.has(SC_MAXIMIZEPOWER)
        || ((sd.class_ & MAPID_SECONDMASK) == MAPID_MONK && sc.has(SC_EXTREMITYFIST) && (!sc.has(SC_SPIRIT) || sc.v2(SC_SPIRIT) != SL_MONK as i32))
        || (sc.has(SC_OBLIVIONCURSE) && sc.v3(SC_OBLIVIONCURSE) == 1)
    {
        regen.flag &= !RGN_SP;
    }
    if sc.has(SC_TENSIONRELAX) {
        if regen.overweight {
            regen.overweight = false; // 1x HP regen
        } else {
            regen.rate_hp += 200;
            if regen.has_sub {
                sd.sregen.rate_hp += 200;
            }
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
    // (Elemental recovery bonus: BL_ELEM only)
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
    let _ = c;
}

// ---------------------------------------------------------------------------------------------- status_calc_bl_

/// status.cpp status_calc_bl_: recalculates the parts of a unit's status in `flag` (SCB_* bits) and sends the
/// client updates. Players here; monsters, pets, homunculi, mercenaries and elementals hand over to their systems.
pub fn calc_bl(c: &Ctx, bl_id: u32, flag: u64, opt: u8) {
    let Some(u) = unit::get(c, bl_id) else { return };
    if u.kind == BL_MOB {
        // status_calc_mob_ and the monster path of status_calc_bl_main: the monster system
        crate::mob::status_bl::status_calc_bl_mob(c, bl_id, flag, opt);
        return;
    }
    if u.kind == crate::map::BL_PET {
        // status_calc_pet_, then "Pets are not affected by statuses" (pet/status.rs)
        if flag & (1u64 << SCB_BASE) != 0 {
            crate::pet::status::status_calc_pet(c, bl_id, opt);
        }
        return;
    }
    if u.kind == crate::map::BL_HOM {
        // status_calc_homunculus_, status_calc_bl_main's BL_HOM path and the homunculus window (homunculus/status.rs)
        crate::homunculus::status::status_calc_bl_hom(c, bl_id, flag, opt);
        return;
    }
    if crate::companion::is_kind(u.kind) {
        // status_calc_mercenary_ / status_calc_elemental_ and their path of status_calc_bl_main (companion/status.rs)
        crate::companion::status::status_calc_bl(c, bl_id, flag, opt);
        return;
    }
    if u.kind != BL_PC {
        // npc: with its system (a later wave)
        return;
    }
    let held = data::hold(c, bl_id);
    if !data::is_held(bl_id) {
        return; // no status row (not in the world)
    }
    calc_bl_pc(c, bl_id, flag, opt);
    if held {
        data::release(c, bl_id);
        sync_unit(c, bl_id);
    }
}

/// The player part of status_calc_bl_.
fn calc_bl_pc(c: &Ctx, aid: u32, mut flag: u64, opt: u8) {
    let Some(mut sd) = data::sd_get(c, aid) else { return };
    if sd.delayed_damage != 0 {
        if opt & SCO_FORCE != 0 {
            sd.hold_recalc = false; // Clear and move on
            data::sd_put(c, &sd);
        } else {
            sd.hold_recalc = true; // Flag and stop
            data::sd_put(c, &sd);
            return;
        }
    }
    // Remember previous values
    let b_status = sd.battle_status.clone();
    if flag & (1u64 << SCB_BASE) != 0 {
        status_calc_pc_(c, aid, opt);
    }
    status_calc_bl_main(c, aid, &mut flag);
    let Some(sd) = data::sd_get(c, aid) else { return };
    let status = &sd.battle_status;
    // Compare against new values and send client updates
    let up = |t| super::clif::updatestatus(c, aid, t);
    if b_status.str != status.str { up(SP_STR); }
    if b_status.agi != status.agi { up(SP_AGI); }
    if b_status.vit != status.vit { up(SP_VIT); }
    if b_status.int_ != status.int_ { up(SP_INT); }
    if b_status.dex != status.dex { up(SP_DEX); }
    if b_status.luk != status.luk { up(SP_LUK); }
    if b_status.hit != status.hit { up(SP_HIT); }
    if b_status.flee != status.flee { up(SP_FLEE1); }
    if b_status.amotion != status.amotion { up(SP_ASPD); }
    if b_status.speed != status.speed { up(SP_SPEED); }
    if b_status.batk != status.batk { up(SP_ATK1); }
    if b_status.def != status.def {
        up(SP_DEF1);
        up(SP_DEF2);
    }
    if b_status.watk != status.watk || b_status.watk2 != status.watk2 || b_status.eatk != status.eatk {
        up(SP_ATK2);
    }
    if b_status.def2 != status.def2 {
        up(SP_DEF2);
        up(SP_DEF1);
    }
    if b_status.flee2 != status.flee2 { up(SP_FLEE2); }
    if b_status.cri != status.cri { up(SP_CRITICAL); }
    if b_status.matk_max != status.matk_max || b_status.matk_min != status.matk_min {
        up(SP_MATK2);
        up(SP_MATK1);
    }
    if b_status.mdef != status.mdef {
        up(SP_MDEF1);
        up(SP_MDEF2);
    }
    if b_status.mdef2 != status.mdef2 {
        up(SP_MDEF2);
        up(SP_MDEF1);
    }
    if b_status.rhw.range != status.rhw.range { up(SP_ATTACKRANGE); }
    if b_status.max_hp != status.max_hp { up(SP_MAXHP); }
    if b_status.max_sp != status.max_sp { up(SP_MAXSP); }
    if b_status.hp != status.hp { up(SP_HP); }
    if b_status.sp != status.sp { up(SP_SP); }
    if b_status.pow != status.pow { up(SP_POW); }
    if b_status.sta != status.sta { up(SP_STA); }
    if b_status.wis != status.wis { up(SP_WIS); }
    if b_status.spl != status.spl { up(SP_SPL); }
    if b_status.con != status.con { up(SP_CON); }
    if b_status.crt != status.crt { up(SP_CRT); }
    if b_status.patk != status.patk { up(SP_PATK); }
    if b_status.smatk != status.smatk { up(SP_SMATK); }
    if b_status.res != status.res { up(SP_RES); }
    if b_status.mres != status.mres { up(SP_MRES); }
    if b_status.hplus != status.hplus { up(SP_HPLUS); }
    if b_status.crate_ != status.crate_ { up(SP_CRATE); }
    if b_status.max_ap != status.max_ap { up(SP_MAXAP); }
    if b_status.ap != status.ap { up(SP_AP); }
}

/// status.cpp status_calc_pc_: status_calc_pc_sub, then the script the player was attached to gets them back.
fn status_calc_pc_(c: &Ctx, aid: u32, opt: u8) -> i32 {
    // Save the old script the player was attached to
    let previous_st = crate::script::npc_run::sdx(c, aid).st;
    let ret = status_calc_pc_sub(c, aid, opt);
    // If an old script is present, reattach the player to it, so that the limitations of that script kick back in
    // (script_attach_state: the item scripts' own states detach back to it, so sd->st is that state again and only
    // the NPC id it runs as is restored)
    if previous_st != 0 && crate::script::npc_run::sdx(c, aid).st != previous_st {
        let mut x = crate::script::npc_run::sdx(c, aid);
        x.st = previous_st;
        crate::script::npc_run::put_sdx(c, &x);
    }
    ret
}

/// Runs an item / card / combo / status script on the player during the calculation (`run_script(script, 0,
/// sd->id, 0)`): the row is stored before and reloaded after, as the script's bonuses change it.
fn run_calc_script(c: &Ctx, sd: &mut PcStatus, kind: &str, key: &str) {
    data::sd_put(c, sd);
    super::scripts::ensure_providers();
    crate::script::run_ext(c, kind, key, sd.account_id, 0);
    if let Some(fresh) = data::sd_get(c, sd.account_id) {
        *sd = fresh;
    }
}

/// status.cpp status_calc_pc_sub. Returns -1 (too many recursive calls), 1 (a script recalculated the player) or 0.
fn status_calc_pc_sub(c: &Ctx, aid: u32, opt: u8) -> i32 {
    set_calculating(calculating() + 1);
    if calculating() > 10 {
        // Too many recursive calls!
        return -1;
    }
    let Some(mut sd) = data::sd_get(c, aid) else {
        set_calculating(0);
        return 0;
    };
    let Some(u) = unit::get(c, aid) else {
        set_calculating(0);
        return 0;
    };
    // Remember player-specific values that are currently being shown to the client (for refresh purposes)
    let b_skill = crate::skilltree::snapshot(c, aid);
    super::progress::pc_calc_skilltree(c, aid);
    let Some(ch) = c.db().character().id().find(sd.char_id) else {
        set_calculating(0);
        return 0;
    };
    if opt & SCO_FIRST != 0 {
        // Load Hp/SP from char-received data.
        sd.battle_status.hp = ch.hp;
        sd.battle_status.sp = ch.sp;
        if crate::conf::keep_ap_on_logout() {
            sd.battle_status.ap = ch.ap;
        }
        sd.regen.has_sub = true;
    }
    // These are not zeroed. [zzo]
    sd.rates = data::Rates {
        castrate: 100,
        hprate: 100,
        sprate: 100,
        aprate: 100,
        dsprate: 100,
        hprecov_rate: 100,
        sprecov_rate: 100,
        matk_rate: 100,
        critical_rate: 100,
        hit_rate: 100,
        flee_rate: 100,
        flee2_rate: 100,
        def_rate: 100,
        def2_rate: 100,
        mdef_rate: 100,
        mdef2_rate: 100,
        patk_rate: 100,
        smatk_rate: 100,
        res_rate: 100,
        mres_rate: 100,
        hplus_rate: 100,
        crate_rate: 100,
    };
    sd.regen.block = 0;
    sd.add_max_weight = 0;
    sd.indexed_bonus = data::IndexedBonus::default();
    sd.right_weapon.reset_keep_atkmods();
    sd.left_weapon.reset_keep_atkmods();
    if sd.special_state.intravision {
        super::clif::status_change(c, &u, EFST_CLAIRVOYANCE, 0, 0, 0, 0, 0);
    }
    if sd.special_state.no_walk_delay {
        super::clif::status_change(c, &u, EFST_ENDURE, 0, 0, 0, 0, 0);
    }
    sd.special_state = data::SpecialState::default();
    // (pc_isvip: VIP_ENABLE is off)
    if !sd.permanent_speed {
        sd.base_status.zero_keep_hpspap();
        sd.base_status.speed = DEFAULT_WALK_SPEED as u16;
    } else {
        let p_speed = sd.base_status.speed;
        sd.base_status.zero_keep_hpspap();
        sd.base_status.speed = p_speed;
    }
    // Give them all modes except these (useful for clones)
    sd.base_status.mode = MD_MASK & !(MD_STATUSIMMUNE | MD_IGNOREMELEE | MD_IGNOREMAGIC | MD_IGNORERANGED | MD_IGNOREMISC | MD_DETECTOR | MD_ANGRY | MD_TARGETWEAK);
    sd.base_status.size = if sd.class_ & JOBL_BABY != 0 {
        SZ_SMALL as u8
    } else if (sd.class_ & MAPID_FIRSTMASK) == MAPID_SUMMONER {
        crate::conf::summoner_size()
    } else {
        SZ_MEDIUM as u8
    };
    if crate::conf::character_size() != 0 && u.sc.option & OPTION_RIDING != 0 {
        if sd.class_ & JOBL_BABY != 0 {
            if crate::conf::character_size() & SZ_BIG != 0 {
                sd.base_status.size += 1;
            }
        } else if crate::conf::character_size() & SZ_MEDIUM != 0 {
            sd.base_status.size += 1;
        }
    }
    sd.base_status.aspd_rate = 1000;
    sd.base_status.ele_lv = 1;
    sd.base_status.race = if (sd.class_ & MAPID_FIRSTMASK) == MAPID_SUMMONER { crate::conf::summoner_race() } else { RC_PLAYER_HUMAN as u8 };
    sd.base_status.class_ = CLASS_NORMAL as u8;
    sd.lists = data::BonusLists::default();
    // Zero up structures...
    sd.races = data::RaceBonus::default();
    sd.bonus = data::Bonus::default();
    // Autobonus
    data::sd_put(c, &sd);
    super::bonus::pc_delautobonus(c, aid, 1, true);
    super::bonus::pc_delautobonus(c, aid, 2, true);
    super::bonus::pc_delautobonus(c, aid, 3, true);
    // (no `calculating` abort here: status.cpp:3893-3904 goes on to the equipment even when a restored autobonus
    // script re-entered status_calc_pc; its aborts come after the equipment scripts)
    // if (sd->pd != nullptr): pet_delautobonus(*sd, sd->pd->autobonus / autobonus2 / autobonus3, true) (pet/bonus.rs)
    for which in 1..=3 {
        crate::pet::bonus::pet_delautobonus(c, aid, which, true);
    }
    let Some(fresh) = data::sd_get(c, aid) else { return 0 };
    sd = fresh;
    let perm_all = crate::pc::has_permission(c, aid, crate::rules::PC_PERM_USE_ALL_EQUIPMENT);
    let m = u.map;
    let mut refinedef: i32 = 0;
    // Parse equipment
    for i in 0..EQI_MAX {
        let eqi = equip_index(c, aid);
        let index = eqi[i];
        set_item_index(index); // We send the item as argument to the script
        set_combo_pos(0);
        if index < 0 {
            continue;
        }
        if i == EQI_AMMO {
            continue;
        }
        if pc_is_same_equip_index(i, &eqi, index) {
            continue;
        }
        let Some(mut eq) = equip_at(c, aid, index) else { continue };
        sd.base_status.def = sd.base_status.def.wrapping_add(eq.data.def as i16);
        let scripts = eq.data.scripts();
        // Items may be equipped, their effects however are nullified.
        if opt & SCO_FIRST != 0 && scripts.map(|s| s.equip_script.is_some()).unwrap_or(false) && (perm_all || !itemdb_is_no_equip(c, eq.data, m)) {
            // Execute equip-script on login
            run_calc_script(c, &mut sd, "item", &format!("{}:equip", eq.data.nameid));
            if calculating() == 0 {
                return 1;
            }
        }
        // Sanitize the refine level in case someone decreased the value inbetween
        if eq.item.refine > MAX_REFINE {
            eq.item.refine = MAX_REFINE;
            if let Some(mut r) = crate::inventory::inv_get(c, aid, index as usize) {
                r.item.refine = MAX_REFINE;
                crate::inventory::inv_put(c, &r);
            }
        }
        let info = itemdb::refine::find_current_level_info(eq.data, eq.item.refine);
        let enchantgrade_info = if eq.item.enchantgrade > 0 { itemdb::enchantgrade::find_current_level_info(eq.data, eq.item.enchantgrade) } else { None };
        let refine = eq.item.refine as i32;
        if eq.data.r#type == IT_WEAPON {
            let mut wlv = eq.data.weapon_level as i32;
            if wlv >= MAX_WEAPON_LEVEL {
                wlv = MAX_WEAPON_LEVEL;
            }
            let left = i == EQI_HAND_L && eq.item.equip == EQP_HAND_L;
            {
                let wa = if left { &mut sd.base_status.lhw } else { &mut sd.base_status.rhw };
                wa.atk = wa.atk.wrapping_add(eq.data.atk as u16);
                if let Some(info) = info {
                    wa.atk2 = wa.atk2.wrapping_add((info.bonus / 100) as u16);
                    if let Some(eg) = enchantgrade_info {
                        wa.atk2 = wa.atk2.wrapping_add(((info.bonus / 100) * eg.bonus as u32 / 100) as u16);
                    }
                }
            }
            if info.is_some() && wlv == 5 {
                sd.base_status.patk = sd.base_status.patk.wrapping_add((refine * 2) as i16);
                sd.base_status.smatk = sd.base_status.smatk.wrapping_add((refine * 2) as i16);
            }
            let weapon_atk_rate = sd.bonus.weapon_atk_rate as i32;
            let weapontype1 = crate::inventory::st(c, aid).map(|s| s.weapontype1 as i32).unwrap_or(W_FIST);
            {
                let wa = if left { &mut sd.base_status.lhw } else { &mut sd.base_status.rhw };
                if weapon_atk_rate != 0 {
                    wa.atk = (wa.atk as i32 + wa.atk as i32 * weapon_atk_rate / 100) as u16;
                }
                wa.matk = wa.matk.wrapping_add(eq.data.matk as u16);
                wa.wlv = wlv as u8;
                // Renewal magic attack refine bonus
                if let Some(info) = info {
                    if weapontype1 != W_BOW {
                        wa.matk = wa.matk.wrapping_add((info.bonus / 100) as u16);
                        if let Some(eg) = enchantgrade_info {
                            wa.matk = wa.matk.wrapping_add(((info.bonus / 100) * eg.bonus as u32 / 100) as u16);
                        }
                    }
                }
            }
            // Overrefine bonus.
            if let Some(info) = info {
                let wd = if left { &mut sd.left_weapon } else { &mut sd.right_weapon };
                wd.overrefine = (info.randombonus_max / 100) as i32;
            }
            {
                let wa = if left { &mut sd.base_status.lhw } else { &mut sd.base_status.rhw };
                wa.range = wa.range.wrapping_add(eq.data.range);
            }
            if scripts.map(|s| s.script.is_some()).unwrap_or(false) && (perm_all || !itemdb_is_no_equip(c, eq.data, m)) {
                if left {
                    sd.lr_flag = LR_FLAG_WEAPON;
                    run_calc_script(c, &mut sd, "item", &eq.data.nameid.to_string());
                    sd.lr_flag = LR_FLAG_NONE;
                } else {
                    run_calc_script(c, &mut sd, "item", &eq.data.nameid.to_string());
                }
                if calculating() == 0 {
                    // Abort, run_script retriggered this. [Skotlex]
                    return 1;
                }
            }
            let weapon_matk_rate = sd.bonus.weapon_matk_rate as i32;
            {
                let wa = if left { &mut sd.base_status.lhw } else { &mut sd.base_status.rhw };
                if weapon_matk_rate != 0 {
                    wa.matk = (wa.matk as i32 + wa.matk as i32 * weapon_matk_rate / 100) as u16;
                }
            }
            let card = |k: usize| eq.item.card(k);
            if card(0) == itemdb::CARD0_FORGE {
                // Forged weapon
                let rank = pc_famerank(c, (card(2) & 0xffff) | ((card(3) & 0xffff) << 16), MAPID_BLACKSMITH);
                let wd = if left { &mut sd.left_weapon } else { &mut sd.right_weapon };
                wd.star += (card(1) >> 8) as i32;
                if wd.star >= 15 {
                    wd.star = 40; // 3 Star Crumbs now give +40 dmg
                }
                if rank != 0 {
                    wd.star += 10;
                }
                let wa = if left { &mut sd.base_status.lhw } else { &mut sd.base_status.rhw };
                if wa.ele == 0 {
                    // Do not overwrite element from previous bonuses.
                    wa.ele = (card(1) & 0x0f) as u8;
                }
            }
        } else if eq.data.r#type == IT_ARMOR {
            if let Some(info) = info {
                refinedef += info.bonus as i32;
                if eq.data.armor_level == 2 {
                    sd.base_status.res = sd.base_status.res.wrapping_add((refine * 2) as i16);
                    sd.base_status.mres = sd.base_status.mres.wrapping_add((refine * 2) as i16);
                }
            }
            if scripts.map(|s| s.script.is_some()).unwrap_or(false) && (perm_all || !itemdb_is_no_equip(c, eq.data, m)) {
                if i == EQI_HAND_L {
                    sd.lr_flag = LR_FLAG_SHIELD; // Shield
                }
                run_calc_script(c, &mut sd, "item", &eq.data.nameid.to_string());
                if i == EQI_HAND_L {
                    sd.lr_flag = LR_FLAG_NONE;
                }
                if calculating() == 0 {
                    return 1;
                }
            }
        } else if eq.data.r#type == IT_SHADOWGEAR {
            // Shadow System
            if scripts.map(|s| s.script.is_some()).unwrap_or(false) && (perm_all || !itemdb_is_no_equip(c, eq.data, m)) {
                run_calc_script(c, &mut sd, "item", &eq.data.nameid.to_string());
                if calculating() == 0 {
                    return 1;
                }
            }
        }
    }
    let ammo = equip_index(c, aid)[EQI_AMMO];
    if ammo >= 0 {
        if let Some(eq) = equip_at(c, aid, ammo) {
            // Arrows
            sd.bonus.arrow_atk += eq.data.atk as i32;
            sd.lr_flag = LR_FLAG_ARROW;
            if !itemdb::group::itemdb_group_item_exists(IG_THROWABLE, eq.data.nameid) {
                // Don't run scripts on throwable items
                if eq.data.scripts().map(|s| s.script.is_some()).unwrap_or(false) {
                    run_calc_script(c, &mut sd, "item", &eq.data.nameid.to_string());
                }
            }
            sd.lr_flag = LR_FLAG_NONE;
            if calculating() == 0 {
                return 1;
            }
        }
    }
    // Process and check item combos
    let combos = crate::inventory::st(c, aid).map(|s| s.combos).unwrap_or_default();
    for combo in combos {
        set_item_index(-1);
        set_combo_pos(combo.pos);
        let Some(item_combo) = itemdb::combo::combo(combo.id as u16) else { continue };
        if item_combo.script.is_none() {
            continue;
        }
        // Don't run the script if at least one of combo's pair has restriction
        let no_run = item_combo.nameid.iter().any(|&id| itemdb::itemdb_exists(id).map(|d| !perm_all && itemdb_is_no_equip(c, d, m)).unwrap_or(false));
        if no_run {
            continue;
        }
        run_calc_script(c, &mut sd, "item", &format!("combo:{}", combo.id));
        if calculating() == 0 {
            return 1;
        }
    }
    // Store equipment script bonuses
    sd.indexed_bonus.param_equip = sd.indexed_bonus.param_bonus.clone();
    sd.indexed_bonus.param_bonus = vec![0; PARAM_MAX];
    sd.base_status.def = (sd.base_status.def as i32 + (refinedef + 50) / 100) as i16;
    // Parse Cards
    for i in 0..EQI_MAX {
        let eqi = equip_index(c, aid);
        let index = eqi[i];
        set_item_index(index); // We send the item as argument to the script
        set_combo_pos(0);
        if index < 0 {
            continue;
        }
        if i == EQI_AMMO {
            continue;
        }
        if pc_is_same_equip_index(i, &eqi, index) {
            continue;
        }
        let Some(eq) = equip_at(c, aid, index) else { continue };
        // Card script execution.
        if itemdb::itemdb_isspecial(eq.item.card0) {
            continue;
        }
        for j in 0..MAX_SLOTS {
            let card = eq.item.card(j);
            set_card_id(card as i32);
            if card == 0 {
                continue;
            }
            let Some(cdata) = itemdb::itemdb_exists(card) else { continue };
            let cs = cdata.scripts();
            if opt & SCO_FIRST != 0 && cs.map(|s| s.equip_script.is_some()).unwrap_or(false) && (perm_all || !itemdb_is_no_equip(c, cdata, m)) {
                // Execute equip-script on login
                run_calc_script(c, &mut sd, "item", &format!("{card}:equip"));
                if calculating() == 0 {
                    return 1;
                }
            }
            if !cs.map(|s| s.script.is_some()).unwrap_or(false) {
                continue;
            }
            if !perm_all && itemdb_is_no_equip(c, cdata, m) {
                // Card restriction checks.
                continue;
            }
            if i == EQI_HAND_L && eq.item.equip == EQP_HAND_L {
                // Left hand status.
                sd.lr_flag = LR_FLAG_WEAPON;
                run_calc_script(c, &mut sd, "item", &card.to_string());
                sd.lr_flag = LR_FLAG_NONE;
            } else {
                run_calc_script(c, &mut sd, "item", &card.to_string());
            }
            if calculating() == 0 {
                return 1;
            }
        }
    }
    set_card_id(0); // Clear stored card ID [Secret]
    // Parse random options
    for i in 0..EQI_MAX {
        let eqi = equip_index(c, aid);
        let index = eqi[i];
        set_item_index(index);
        set_combo_pos(0);
        set_opt_index(-1);
        if index < 0 {
            continue;
        }
        if i == EQI_AMMO {
            continue;
        }
        if pc_is_same_equip_index(i, &eqi, index) {
            continue;
        }
        let Some(eq) = equip_at(c, aid, index) else { continue };
        for j in 0..crate::inventory::MAX_ITEM_RDM_OPT {
            let opt_id = eq.item.opt(j).id;
            if opt_id == 0 {
                continue;
            }
            set_opt_index(j as i16);
            let Some(data) = itemdb::randomopt::random_option(opt_id as u16) else { continue };
            if data.script.is_none() {
                continue;
            }
            if !perm_all && itemdb_is_no_equip(c, eq.data, m) {
                continue;
            }
            if i == EQI_HAND_L && eq.item.equip == EQP_HAND_L {
                // Left hand status.
                sd.lr_flag = LR_FLAG_WEAPON;
                run_calc_script(c, &mut sd, "randomopt", &opt_id.to_string());
                sd.lr_flag = LR_FLAG_NONE;
            } else {
                run_calc_script(c, &mut sd, "randomopt", &opt_id.to_string());
            }
            if calculating() == 0 {
                return 1;
            }
        }
        set_opt_index(-1);
    }
    let scl = sc::load(c, aid);
    if !scl.empty() {
        if let Some(e) = scl.get(SC_ITEMSCRIPT) {
            if itemdb::item_scripts(e.val1 as u32).map(|s| s.script.is_some()).unwrap_or(false) {
                run_calc_script(c, &mut sd, "item", &e.val1.to_string());
            }
        }
        // for (const auto& it : *sc): the unordered_map's order
        for t in sc::load_map_order(c, aid).iter().map(|e| e.sc_type) {
            if crate::db::misc::status::status_db().find(t).map(|s| s.script.is_some()).unwrap_or(false) {
                run_calc_script(c, &mut sd, "sc", &t.to_string());
            }
        }
    }
    data::sd_put(c, &sd);
    super::bonus::pc_bonus_script(c, aid);
    if let Some(fresh) = data::sd_get(c, aid) {
        sd = fresh;
    }
    // Pet Bonus: the pet database's bonus script, then the petskillbonus bonus (pet/bonus.rs)
    crate::pet::bonus::status_calc_pc_pet_bonus(c, &mut sd, &run_calc_script);
    // param_bonus now holds card bonuses.
    let st = &mut sd.base_status;
    if st.rhw.range < 1 {
        st.rhw.range = 1;
    }
    if st.lhw.range < 1 {
        st.lhw.range = 1;
    }
    if st.rhw.range < st.lhw.range {
        st.rhw.range = st.lhw.range;
    }
    sd.bonus.double_rate += sd.bonus.double_add_rate;
    sd.bonus.perfect_hit += sd.bonus.perfect_hit_add;
    sd.bonus.splash_range = sd.bonus.splash_range.wrapping_add(sd.bonus.splash_add_range);
    // Damage modifiers from weapon type
    let (weapontype1, weapontype2) = crate::inventory::st(c, aid).map(|s| (s.weapontype1 as i32, s.weapontype2 as i32)).unwrap_or((W_FIST, W_FIST));
    if let Some(r) = crate::db::misc::status::size_fix_db().find(weapontype1) {
        sd.right_weapon.atkmods[SZ_SMALL as usize] = r.small as i32;
        sd.right_weapon.atkmods[SZ_MEDIUM as usize] = r.medium as i32;
        sd.right_weapon.atkmods[SZ_BIG as usize] = r.large as i32;
    }
    if let Some(l) = crate::db::misc::status::size_fix_db().find(weapontype2) {
        sd.left_weapon.atkmods[SZ_SMALL as usize] = l.small as i32;
        sd.left_weapon.atkmods[SZ_MEDIUM as usize] = l.medium as i32;
        sd.left_weapon.atkmods[SZ_BIG as usize] = l.large as i32;
    }
    let option = unit::get(c, aid).map(|u| u.sc.option).unwrap_or(0);
    let weapon = crate::pc::character_of(c, aid).map(|ch| ch.weapon as i32).unwrap_or(W_FIST);
    if (option & OPTION_RIDING != 0 || option & OPTION_DRAGON != 0) && (weapon == W_1HSPEAR || weapon == W_2HSPEAR) {
        // When Riding with spear, damage modifier to mid-class becomes same as versus large size.
        sd.right_weapon.atkmods[SZ_MEDIUM as usize] = sd.right_weapon.atkmods[SZ_BIG as usize];
        sd.left_weapon.atkmods[SZ_MEDIUM as usize] = sd.left_weapon.atkmods[SZ_BIG as usize];
    }
    // ----- STATS CALCULATION -----
    let Some(ch) = c.db().character().id().find(sd.char_id) else { return 0 };
    let u = unit::get(c, aid).unwrap_or(u);
    let w = who(c, &sd, &ch, &u);
    // Job bonuses
    if let Some(job_info) = jobs::job_db().find(pc_mapid2jobid(sd.class_, ch.sex as i32) as u16) {
        if let Some(bonus) = job_info.job_bonus.get((ch.job_level as usize).wrapping_sub(1)) {
            let b = &mut sd.base_status;
            b.str = b.str.wrapping_add(bonus[PARAM_STR] as i16);
            b.agi = b.agi.wrapping_add(bonus[PARAM_AGI] as i16);
            b.vit = b.vit.wrapping_add(bonus[PARAM_VIT] as i16);
            b.int_ = b.int_.wrapping_add(bonus[PARAM_INT] as i16);
            b.dex = b.dex.wrapping_add(bonus[PARAM_DEX] as i16);
            b.luk = b.luk.wrapping_add(bonus[PARAM_LUK] as i16);
            b.pow = b.pow.wrapping_add(bonus[PARAM_POW] as i16);
            b.sta = b.sta.wrapping_add(bonus[PARAM_STA] as i16);
            b.wis = b.wis.wrapping_add(bonus[PARAM_WIS] as i16);
            b.spl = b.spl.wrapping_add(bonus[PARAM_SPL] as i16);
            b.con = b.con.wrapping_add(bonus[PARAM_CON] as i16);
            b.crt = b.crt.wrapping_add(bonus[PARAM_CRT] as i16);
        }
    }
    // If a Super Novice has never died and is at least joblv 70, he gets all stats +10
    if (sd.class_ & MAPID_SECONDMASK) == MAPID_SUPER_NOVICE && (ch.job_level >= 70 || sd.class_ & JOBL_THIRD != 0) && sd.die_counter == 0 {
        let b = &mut sd.base_status;
        b.str += 10;
        b.agi += 10;
        b.vit += 10;
        b.int_ += 10;
        b.dex += 10;
        b.luk += 10;
    }
    // Absolute modifiers from passive skills
    if w.checkskill(BS_HILTBINDING) > 0 {
        sd.base_status.str += 1;
    }
    let skill = w.checkskill(SA_DRAGONOLOGY);
    if skill > 0 {
        sd.base_status.int_ += ((skill + 1) / 2) as i16; // +1 INT / 2 lv
    }
    let skill = w.checkskill(AC_OWL);
    if skill > 0 {
        sd.base_status.dex += skill as i16;
    }
    let skill = w.checkskill(RA_RESEARCHTRAP);
    if skill > 0 {
        sd.base_status.int_ += skill as i16;
    }
    if w.checkskill(SU_POWEROFLAND) > 0 {
        sd.base_status.int_ += 20;
    }
    // Bonuses from cards and equipment as well as base stat, remember to avoid overflows.
    {
        let pb = sd.indexed_bonus.param_bonus.clone();
        let pe = sd.indexed_bonus.param_equip.clone();
        let b = &mut sd.base_status;
        let sum = |base: i16, stat: u16, p: usize| cap_value(base as i32 + stat as i32 + pb[p] + pe[p], 0, u16::MAX as i32) as u16 as i16;
        b.str = sum(b.str, ch.str, PARAM_STR);
        b.agi = sum(b.agi, ch.agi, PARAM_AGI);
        b.vit = sum(b.vit, ch.vit, PARAM_VIT);
        b.int_ = sum(b.int_, ch.int, PARAM_INT);
        b.dex = sum(b.dex, ch.dex, PARAM_DEX);
        b.luk = sum(b.luk, ch.luk, PARAM_LUK);
        b.pow = sum(b.pow, ch.pow, PARAM_POW);
        b.sta = sum(b.sta, ch.sta, PARAM_STA);
        b.wis = sum(b.wis, ch.wis, PARAM_WIS);
        b.spl = sum(b.spl, ch.spl, PARAM_SPL);
        b.con = sum(b.con, ch.con, PARAM_CON);
        b.crt = sum(b.crt, ch.crt, PARAM_CRT);
    }
    if sd.special_state.no_walk_delay {
        if let Some(mut e) = sc::get_sce(c, aid, SC_ENDURE) {
            if e.val4 != 0 {
                e.val4 = 0;
                sc::put_sce(c, &e);
            }
            data::sd_put(c, &sd);
            sc::status_change_end(c, aid, SC_ENDURE, 0);
            if let Some(fresh) = data::sd_get(c, aid) {
                sd = fresh;
            }
        }
        if !sc::is_disabled_on_map(c, SC_ENDURE, m) {
            super::clif::status_change(c, &u, EFST_ENDURE, 1, 0, 0, 0, 0);
            sd.base_status.mdef += 1;
        }
    }
    // ----- CONCENTRATION CALCULATION -----
    let skill = w.checkskill(NW_GRENADE_MASTERY);
    if skill > 0 {
        sd.base_status.con += skill as i16;
    }
    // ----- SPELL CALCULATION -----
    let skill = w.checkskill(SOA_SOUL_MASTERY);
    if skill > 0 {
        sd.base_status.spl += skill as i16;
    }
    // ------ ATTACK CALCULATION ------ (RENEWAL)
    sd.base_status.watk = status_weapon_atk(&sd.base_status.rhw) as u16;
    sd.base_status.watk2 = status_weapon_atk(&sd.base_status.lhw) as u16;
    sd.base_status.eatk = sd.bonus.eatk as i16;
    let scl = sc::load(c, aid);
    // ----- HP MAX CALCULATION -----
    let max_hp = status_calc_maxhp_pc(&w, &sd, &ch, &scl, sd.base_status.vit as u16 as u32);
    sd.base_status.max_hp = max_hp;
    let mut ch = ch;
    ch.max_hp = max_hp;
    if crate::conf::hp_rate() != 100 {
        sd.base_status.max_hp = (crate::conf::hp_rate() as f64 * (sd.base_status.max_hp as f64 / 100.0)) as u32;
    }
    sd.base_status.max_hp = if ch.base_level < 100 {
        cap_value(sd.base_status.max_hp, 1, crate::conf::max_hp_lv99())
    } else if ch.base_level < 151 {
        cap_value(sd.base_status.max_hp, 1, crate::conf::max_hp_lv150())
    } else {
        cap_value(sd.base_status.max_hp, 1, crate::conf::max_hp())
    };
    // ----- SP MAX CALCULATION -----
    let max_sp = status_calc_maxsp_pc(&w, &sd, &ch, &scl, sd.base_status.int_ as u16 as u32);
    sd.base_status.max_sp = max_sp;
    ch.max_sp = max_sp;
    if crate::conf::sp_rate() != 100 {
        sd.base_status.max_sp = (crate::conf::sp_rate() as f64 * (sd.base_status.max_sp as f64 / 100.0)) as u32;
    }
    sd.base_status.max_sp = cap_value(sd.base_status.max_sp, 1, crate::conf::max_sp());
    // ----- AP MAX CALCULATION -----
    let max_ap = status_calc_maxap_pc(&w, &sd, &ch, &scl);
    sd.base_status.max_ap = max_ap;
    ch.max_ap = max_ap;
    if crate::conf::ap_rate() != 100 {
        sd.base_status.max_ap = (crate::conf::ap_rate() as f64 * (sd.base_status.max_ap as f64 / 100.0)) as u32;
    }
    sd.base_status.max_ap = cap_value(sd.base_status.max_ap, 0, crate::conf::max_ap());
    super::put_char(c, &ch);
    // ----- RESPAWN HP/SP/AP -----
    // Calc respawn hp and store it on base_status
    if sd.special_state.restart_full_recover {
        sd.base_status.hp = sd.base_status.max_hp;
        sd.base_status.sp = sd.base_status.max_sp;
    } else {
        if (sd.class_ & MAPID_FIRSTMASK) == MAPID_NOVICE && sd.class_ & JOBL_2 == 0 && crate::conf::restart_hp_rate() < 50 {
            sd.base_status.hp = sd.base_status.max_hp / 2;
        } else {
            sd.base_status.hp = (sd.base_status.max_hp as i64 * crate::conf::restart_hp_rate() / 100) as u32;
        }
        if sd.base_status.hp == 0 {
            sd.base_status.hp = 1;
        }
        sd.base_status.sp = (sd.base_status.max_sp as i64 * crate::conf::restart_sp_rate() / 100) as u32;
        if sd.base_status.sp == 0 {
            // The minimum for the respawn setting is SP:1
            sd.base_status.sp = 1;
        }
        sd.base_status.ap = (sd.base_status.max_ap as i64 * crate::conf::restart_ap_rate() / 100) as u32;
    }
    // ----- MISC CALCULATION -----
    status_calc_misc_pc(c, &mut sd, &ch, ch.base_level as i32, true);
    // Equipment modifiers for misc settings
    let r = &mut sd.rates;
    let b = &mut sd.base_status;
    if r.matk_rate < 0 {
        r.matk_rate = 0;
    }
    if r.matk_rate != 100 {
        b.matk_max = (b.matk_max as i32 * r.matk_rate / 100) as u16;
        b.matk_min = (b.matk_min as i32 * r.matk_rate / 100) as u16;
    }
    if r.hit_rate < 0 {
        r.hit_rate = 0;
    }
    if r.hit_rate != 100 {
        b.hit = (b.hit as i32 * r.hit_rate / 100) as i16;
    }
    if r.flee_rate < 0 {
        r.flee_rate = 0;
    }
    if r.flee_rate != 100 {
        b.flee = (b.flee as i32 * r.flee_rate / 100) as i16;
    }
    if r.def2_rate < 0 {
        r.def2_rate = 0;
    }
    if r.def2_rate != 100 {
        b.def2 = (b.def2 as i32 * r.def2_rate / 100) as i16;
    }
    if r.mdef2_rate < 0 {
        r.mdef2_rate = 0;
    }
    if r.mdef2_rate != 100 {
        b.mdef2 = (b.mdef2 as i32 * r.mdef2_rate / 100) as i16;
    }
    if r.critical_rate < 0 {
        r.critical_rate = 0;
    }
    if r.critical_rate != 100 {
        b.cri = cap_value(b.cri as i32 * r.critical_rate / 100, i16::MIN as i32, i16::MAX as i32) as i16;
    }
    if w.checkskill(SU_POWEROFLIFE) > 0 {
        b.cri += 200;
    }
    if r.flee2_rate < 0 {
        r.flee2_rate = 0;
    }
    if r.flee2_rate != 100 {
        b.flee2 = (b.flee2 as i32 * r.flee2_rate / 100) as i16;
    }
    if r.patk_rate < 0 {
        r.patk_rate = 0;
    }
    if r.patk_rate != 100 {
        b.patk = (b.patk as i32 * r.patk_rate / 100) as i16;
    }
    if r.smatk_rate < 0 {
        r.smatk_rate = 0;
    }
    if r.smatk_rate != 100 {
        b.smatk = (b.smatk as i32 * r.smatk_rate / 100) as i16;
    }
    if r.res_rate < 0 {
        r.res_rate = 0;
    }
    if r.res_rate != 100 {
        b.res = (b.res as i32 * r.res_rate / 100) as i16;
    }
    if r.mres_rate < 0 {
        r.mres_rate = 0;
    }
    if r.mres_rate != 100 {
        b.mres = (b.mres as i32 * r.mres_rate / 100) as i16;
    }
    if r.hplus_rate < 0 {
        r.hplus_rate = 0;
    }
    if r.hplus_rate != 100 {
        b.hplus = (b.hplus as i32 * r.hplus_rate / 100) as i16;
    }
    if r.crate_rate < 0 {
        r.crate_rate = 0;
    }
    if r.crate_rate != 100 {
        b.crate_ = (b.crate_ as i32 * r.crate_rate / 100) as i16;
    }
    // ----- HIT CALCULATION -----
    let weapon = ch.weapon as i32;
    let skill = w.checkskill(AC_VULTURE);
    if skill > 0 && weapon == W_BOW {
        sd.base_status.rhw.range = sd.base_status.rhw.range.wrapping_add(skill as u16);
    }
    if (W_REVOLVER..=W_GRENADE).contains(&weapon) {
        let skill = w.checkskill(GS_SINGLEACTION);
        if skill > 0 {
            sd.base_status.hit += (2 * skill) as i16;
        }
        let skill = w.checkskill(GS_SNAKEEYE);
        if skill > 0 {
            sd.base_status.hit += skill as i16;
            sd.base_status.rhw.range = sd.base_status.rhw.range.wrapping_add(skill as u16);
        }
    }
    if weapon == W_1HAXE || weapon == W_2HAXE {
        let skill = w.checkskill(NC_TRAININGAXE);
        if skill > 0 {
            sd.base_status.hit += (skill * 3) as i16;
        }
    }
    if weapon == W_MACE || weapon == W_2HMACE {
        let skill = w.checkskill(NC_TRAININGAXE);
        if skill > 0 {
            sd.base_status.hit += (skill * 2) as i16;
        }
    }
    if w.checkskill(SU_POWEROFLIFE) > 0 {
        sd.base_status.hit += 20;
    }
    let skill = pc_checkskill_imperial_guard(&w, ch.shield, weapon, 2);
    if skill > 0 {
        sd.base_status.hit += (skill * 3) as i16;
    }
    if weapon == W_BOOK {
        let skill = w.checkskill(SKE_WAR_BOOK_MASTERY);
        if skill > 0 {
            sd.base_status.hit += (skill * 3) as i16;
        }
    }
    let skill = w.checkskill(SU_SOULATTACK);
    if skill > 0 {
        let r = skill_get_range2_pc(&w, sd.battle_status.rhw.range, SU_SOULATTACK, skill as u16, true);
        sd.base_status.rhw.range = (sd.base_status.rhw.range as i32 + r) as u16;
    }
    // ----- FLEE CALCULATION -----
    let skill = w.checkskill(TF_MISS);
    if skill > 0 {
        let per = if sd.class_ & JOBL_2 != 0 && (sd.class_ & MAPID_FIRSTMASK) == MAPID_THIEF { 4 } else { 3 };
        sd.base_status.flee += (skill * per) as i16;
    }
    let skill = w.checkskill(MO_DODGE);
    if skill > 0 {
        sd.base_status.flee += ((skill * 3) / 2) as i16;
    }
    if w.checkskill(SU_POWEROFLIFE) > 0 {
        sd.base_status.flee += 20;
    }
    let skill = w.checkskill(SHC_SHADOW_SENSE);
    if skill > 0 {
        sd.base_status.flee += (skill * 10) as i16;
    }
    // ----- CRITICAL CALCULATION -----
    let skill = w.checkskill(DC_DANCINGLESSON);
    if skill > 0 {
        sd.base_status.cri += (skill * 10) as i16;
    }
    let skill = w.checkskill(PR_MACEMASTERY);
    if skill > 0 && (weapon == W_MACE || weapon == W_2HMACE) {
        sd.base_status.cri += (skill * 10) as i16;
    }
    let skill = w.checkskill(SHC_SHADOW_SENSE);
    if skill > 0 {
        if weapon == W_DAGGER || weapon == W_DOUBLE_DD || weapon == W_DOUBLE_DS || weapon == W_DOUBLE_DA {
            sd.base_status.cri += (100 + skill * 40) as i16;
        } else if weapon == W_KATAR {
            sd.base_status.cri += (50 + skill * 20) as i16;
        }
    }
    // ----- P.Atk/S.Matk CALCULATION -----
    let skill = w.checkskill(TR_STAGE_MANNER);
    if skill > 0 && (weapon == W_BOW || weapon == W_MUSICAL || weapon == W_WHIP) {
        sd.base_status.patk += (skill * 3) as i16;
        sd.base_status.smatk += (skill * 3) as i16;
    }
    let skill = w.checkskill(HN_SELFSTUDY_TATICS);
    if skill > 0 {
        sd.base_status.patk += skill as i16;
    }
    let skill = w.checkskill(HN_SELFSTUDY_SOCERY);
    if skill > 0 {
        sd.base_status.smatk += skill as i16;
    }
    let skill = w.checkskill(NW_P_F_I);
    if skill > 0 && (W_REVOLVER..=W_GRENADE).contains(&weapon) {
        sd.base_status.patk += (skill + 2) as i16;
    }
    let skill = w.checkskill(SOA_TALISMAN_MASTERY);
    if skill > 0 {
        sd.base_status.smatk += skill as i16;
    }
    if weapon == W_BOOK {
        let skill = w.checkskill(SKE_WAR_BOOK_MASTERY);
        if skill > 0 {
            sd.base_status.patk += (skill + 2) as i16;
        }
    }
    // 2-Handed Staff Mastery
    if weapon == W_2HSTAFF {
        let skill = w.checkskill(AG_TWOHANDSTAFF);
        if skill > 0 {
            sd.base_status.smatk += (skill * 2) as i16;
        }
    }
    let skill = w.checkskill(SH_MYSTICAL_CREATURE_MASTERY);
    if skill > 0 {
        sd.base_status.smatk += (skill * 15 / 10) as i16;
        sd.base_status.patk += (skill * 15 / 10) as i16;
    }
    // ----- PHYSICAL RESISTANCE CALCULATION -----
    let skill = pc_checkskill_imperial_guard(&w, ch.shield, weapon, 1);
    if skill > 0 {
        sd.base_status.res += (skill * 3) as i16;
    }
    // ----- EQUIPMENT-DEF CALCULATION -----
    if sd.rates.def_rate < 0 {
        sd.rates.def_rate = 0;
    }
    if sd.rates.def_rate != 100 {
        let i = sd.base_status.def as i32 * sd.rates.def_rate / 100;
        sd.base_status.def = cap_value(i, DEFTYPE_MIN, DEFTYPE_MAX) as i16;
    }
    if option & OPTION_MADOGEAR != 0 && w.checkskill(NC_MAINFRAME) > 0 {
        sd.base_status.def += (20 + w.checkskill(NC_MAINFRAME) * 20) as i16;
    }
    // ----- EQUIPMENT-MDEF CALCULATION -----
    if sd.rates.mdef_rate < 0 {
        sd.rates.mdef_rate = 0;
    }
    if sd.rates.mdef_rate != 100 {
        let i = sd.base_status.mdef as i32 * sd.rates.mdef_rate / 100;
        sd.base_status.mdef = cap_value(i, DEFTYPE_MIN, DEFTYPE_MAX) as i16;
    }
    // ----- ASPD CALCULATION -----
    let base = sd.base_status.clone();
    let mut i = status_base_amotion_pc(&w, &sd, &ch, &base, &scl);
    // Renewal base value is actually ASPD and not amotion, so we need to convert it
    i = AMOTION_ZERO_ASPD - i * AMOTION_INTERVAL;
    sd.base_status.amotion = cap_value(i, pc_maxaspd(sd.class_) / AMOTION_DIVIDER_PC, MIN_ASPD / AMOTION_DIVIDER_PC) as u16;
    // (Renewal modifiers are handled in status_base_amotion_pc)
    sd.base_status.adelay = (AMOTION_DIVIDER_PC * sd.base_status.amotion as i32) as u16;
    // ----- DMOTION -----
    let i = 800 - sd.base_status.agi as i32 * 4;
    sd.base_status.dmotion = cap_value(i, 400, 800) as u16;
    if crate::conf::pc_damage_delay_rate() != 100 {
        sd.base_status.dmotion = (sd.base_status.dmotion as i32 * crate::conf::pc_damage_delay_rate() / 100) as u16;
    }
    // ----- MISC CALCULATIONS -----
    // Weight
    data::sd_put(c, &sd);
    status_calc_weight(c, aid, CALCWT_MAXBONUS);
    status_calc_cart_weight(c, aid, CALCWT_MAXBONUS);
    let Some(fresh) = data::sd_get(c, aid) else { return 0 };
    sd = fresh;
    sd.regen.walk = w.checkskill(SM_MOVINGRECOVERY) > 0 || option & OPTION_MADOGEAR != 0;
    // Skill SP cost
    let skill = w.checkskill(HP_MANARECHARGE);
    if skill > 0 {
        sd.rates.dsprate -= 4 * skill;
    }
    if let Some(e) = scl.get(SC_SERVICE4U) {
        sd.rates.dsprate -= e.val3;
    }
    // Underflow protections.
    if sd.rates.dsprate < 0 {
        sd.rates.dsprate = 0;
    }
    if sd.rates.castrate < 0 {
        sd.rates.castrate = 0;
    }
    if sd.rates.hprecov_rate < 0 {
        sd.rates.hprecov_rate = 0;
    }
    if sd.rates.sprecov_rate < 0 {
        sd.rates.sprecov_rate = 0;
    }
    // Anti-element and anti-race
    let lcf = crate::conf::left_cardfix_to_right();
    let ib = &mut sd.indexed_bonus;
    let skill = w.checkskill(CR_TRUST);
    if skill > 0 {
        ib.subele[ELE_HOLY as usize] += skill * 5;
    }
    let skill = w.checkskill(BS_SKINTEMPER);
    if skill > 0 {
        ib.subele[ELE_NEUTRAL as usize] += skill;
        ib.subele[ELE_FIRE as usize] += skill * 5;
    }
    let skill = w.checkskill(SA_DRAGONOLOGY);
    if skill > 0 {
        let dragon_matk = (skill * 2) as u8 as i32;
        let skill = skill * 4;
        sd.right_weapon.addrace[RC_DRAGON as usize] += skill;
        if !lcf {
            sd.left_weapon.addrace[RC_DRAGON as usize] += skill;
        }
        ib.magic_addrace[RC_DRAGON as usize] += dragon_matk;
        ib.subrace[RC_DRAGON as usize] += skill;
    }
    let skill = w.checkskill(AB_EUCHARISTICA);
    if skill > 0 {
        sd.right_weapon.addrace[RC_DEMON as usize] += skill;
        sd.right_weapon.addele[ELE_DARK as usize] += skill;
        if !lcf {
            sd.left_weapon.addrace[RC_DEMON as usize] += skill;
            sd.left_weapon.addele[ELE_DARK as usize] += skill;
        }
        ib.magic_addrace[RC_DEMON as usize] += skill;
        ib.magic_addele[ELE_DARK as usize] += skill;
        ib.subrace[RC_DEMON as usize] += skill;
        ib.subele[ELE_DARK as usize] += skill;
    }
    const DEF_BONUS_TABLE: [[u8; 10]; SZ_MAX] = [[1, 2, 3, 4, 5, 6, 7, 8, 9, 10], [2, 3, 5, 6, 8, 9, 11, 12, 14, 15], [3, 5, 7, 9, 10, 12, 13, 15, 16, 18], [0; 10]];
    let skill = w.checkskill(DK_TWOHANDDEF);
    if skill > 0 && (weapon == W_2HSWORD || weapon == W_2HSPEAR || weapon == W_2HAXE) {
        for size in 0..SZ_MAX {
            ib.weapon_subsize[size] += DEF_BONUS_TABLE[size][(skill - 1) as usize] as i32;
        }
    }
    let skill = w.checkskill(IQ_WILL_OF_FAITH);
    if skill > 0 && weapon == W_KNUCKLE {
        let race_atk: [u8; 10] = [3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
        let race_def: [u8; 10] = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        let k = (skill - 1) as usize;
        sd.right_weapon.addrace[RC_UNDEAD as usize] += race_atk[k] as i32;
        sd.right_weapon.addrace[RC_DEMON as usize] += race_atk[k] as i32;
        if !lcf {
            sd.left_weapon.addrace[RC_UNDEAD as usize] += race_atk[k] as i32;
            sd.left_weapon.addrace[RC_DEMON as usize] += race_atk[k] as i32;
        }
        ib.subrace[RC_UNDEAD as usize] += race_def[k] as i32;
        ib.subrace[RC_DEMON as usize] += race_def[k] as i32;
    }
    let skill = w.checkskill(CD_MACE_BOOK_M);
    if skill > 0 && (weapon == W_MACE || weapon == W_2HMACE || weapon == W_BOOK) {
        for size in 0..SZ_MAX {
            sd.right_weapon.addsize[size] += DEF_BONUS_TABLE[size][(skill - 1) as usize] as i32;
            if !lcf {
                sd.left_weapon.addsize[size] += DEF_BONUS_TABLE[size][(skill - 1) as usize] as i32;
            }
        }
    }
    let skill = w.checkskill(CD_FIDUS_ANIMUS);
    if skill > 0 {
        let holy_matk: [u8; 10] = [1, 3, 4, 6, 7, 9, 10, 12, 13, 15];
        ib.magic_atk_ele[ELE_HOLY as usize] += holy_matk[(skill - 1) as usize] as i32;
    }
    let skill = w.checkskill(MT_TWOAXEDEF);
    if skill > 0 && weapon == W_2HAXE {
        for size in 0..SZ_MAX {
            ib.weapon_subsize[size] += DEF_BONUS_TABLE[size][(skill - 1) as usize] as i32;
        }
    }
    let skill = w.checkskill(ABC_DAGGER_AND_BOW_M);
    if skill > 0 && (weapon == W_DAGGER || weapon == W_BOW || weapon == W_DOUBLE_DD || weapon == W_DOUBLE_DS || weapon == W_DOUBLE_DA) {
        const AB: [[u8; 10]; SZ_MAX] = [[1, 2, 3, 4, 5, 6, 7, 8, 9, 10], [2, 3, 5, 6, 8, 9, 11, 12, 14, 15], [2, 4, 6, 8, 10, 12, 14, 16, 18, 20], [0; 10]];
        for size in 0..SZ_MAX {
            sd.right_weapon.addsize[size] += AB[size][(skill - 1) as usize] as i32;
            if !lcf {
                sd.left_weapon.addsize[size] += AB[size][(skill - 1) as usize] as i32;
            }
        }
    }
    let skill = w.checkskill(ABC_MAGIC_SWORD_M);
    if skill > 0 && matches!(weapon, W_DAGGER | W_1HSWORD | W_DOUBLE_DD | W_DOUBLE_SS | W_DOUBLE_DS | W_DOUBLE_DA | W_DOUBLE_SA) {
        const MS: [u8; 10] = [2, 3, 5, 6, 8, 9, 11, 12, 14, 15];
        for size in 0..SZ_MAX {
            if size < SZ_ALL as usize {
                ib.magic_addsize[size] += MS[(skill - 1) as usize] as i32;
            }
        }
    }
    let skill = w.checkskill(EM_MAGIC_BOOK_M);
    if skill > 0 && weapon == W_BOOK {
        ib.magic_atk_ele[ELE_WATER as usize] += skill;
        ib.magic_atk_ele[ELE_EARTH as usize] += skill;
        ib.magic_atk_ele[ELE_FIRE as usize] += skill;
        ib.magic_atk_ele[ELE_WIND as usize] += skill;
        ib.magic_atk_ele[ELE_POISON as usize] += skill;
    }
    if !scl.empty() {
        sc_calc_pc_bonuses(c, &w, &mut sd, &scl);
    }
    let base = sd.base_status.clone();
    sd.battle_status.cpy_from(&base);
    data::sd_put(c, &sd);
    // ----- CLIENT-SIDE REFRESH -----
    if !unit::get(c, aid).map(|u| u.on_map).unwrap_or(false) {
        // Will update on LoadEndAck
        set_calculating(0);
        return 0;
    }
    // memcmp(b_skill, sd->status.skill): clif_deleteskill for the skills that left the tree, clif_skillinfoblock
    crate::skilltree::calc_refresh(c, aid, &b_skill);
    // If the skill is learned, the status is infinite.
    if let Some(u) = unit::get(c, aid) {
        if w.checkskill(SU_SPRITEMABLE) > 0 && !sc::has_sce(c, aid, SC_SPRITEMABLE) {
            sc::sc_start(c, Some(&u), &u, SC_SPRITEMABLE, 100, 1, sc::INFINITE_TICK);
        }
        if w.checkskill(SU_SOULATTACK) > 0 && !sc::has_sce(c, aid, SC_SOULATTACK) {
            sc::sc_start(c, Some(&u), &u, SC_SOULATTACK, 100, 1, sc::INFINITE_TICK);
        }
    }
    set_calculating(0);
    0
}

/// The status-change part at the end of status_calc_pc_sub (`if(!sc->empty())`).
fn sc_calc_pc_bonuses(c: &Ctx, w: &Who, sd: &mut PcStatus, sc: &Sc) {
    let lcf = crate::conf::left_cardfix_to_right();
    if let Some(mut e) = sc.get(SC_CONCENTRATE).cloned() {
        // Update the card-bonus data
        e.val3 = sd.indexed_bonus.param_bonus[1]; // Agi
        e.val4 = sd.indexed_bonus.param_bonus[4]; // Dex
        sc::put_sce(c, &e);
    }
    let ib = &mut sd.indexed_bonus;
    if sc.has(SC_SIEGFRIED) {
        let i = sc.v2(SC_SIEGFRIED);
        ib.subele[ELE_WATER as usize] += i;
        ib.subele[ELE_EARTH as usize] += i;
        ib.subele[ELE_FIRE as usize] += i;
        ib.subele[ELE_WIND as usize] += i;
    }
    if sc.has(SC_BASILICA) {
        let i = sc.v1(SC_BASILICA) * 5;
        sd.right_weapon.addele[ELE_DARK as usize] += i;
        sd.right_weapon.addele[ELE_UNDEAD as usize] += i;
        if !lcf {
            sd.left_weapon.addele[ELE_DARK as usize] += i;
            sd.left_weapon.addele[ELE_UNDEAD as usize] += i;
        }
        ib.magic_atk_ele[ELE_HOLY as usize] += sc.v1(SC_BASILICA) * 3;
    }
    if sc.has(SC_FIREWEAPON) { ib.magic_atk_ele[ELE_FIRE as usize] += sc.v1(SC_FIREWEAPON); }
    if sc.has(SC_WINDWEAPON) { ib.magic_atk_ele[ELE_WIND as usize] += sc.v1(SC_WINDWEAPON); }
    if sc.has(SC_WATERWEAPON) { ib.magic_atk_ele[ELE_WATER as usize] += sc.v1(SC_WATERWEAPON); }
    if sc.has(SC_EARTHWEAPON) { ib.magic_atk_ele[ELE_EARTH as usize] += sc.v1(SC_EARTHWEAPON); }
    if sc.has(SC_PROVIDENCE) {
        ib.subele[ELE_HOLY as usize] += sc.v2(SC_PROVIDENCE);
        ib.subrace[RC_DEMON as usize] += sc.v2(SC_PROVIDENCE);
    }
    if sc.has(SC_FIRE_CLOAK_OPTION) {
        let i = sc.v2(SC_FIRE_CLOAK_OPTION);
        ib.subele[ELE_FIRE as usize] += i;
        ib.subele[ELE_WATER as usize] -= i;
    }
    if sc.has(SC_WATER_DROP_OPTION) {
        let i = sc.v2(SC_WATER_DROP_OPTION);
        ib.subele[ELE_WATER as usize] += i;
        ib.subele[ELE_WIND as usize] -= i;
    }
    if sc.has(SC_WIND_CURTAIN_OPTION) {
        let i = sc.v2(SC_WIND_CURTAIN_OPTION);
        ib.subele[ELE_WIND as usize] += i;
        ib.subele[ELE_EARTH as usize] -= i;
    }
    if sc.has(SC_STONE_SHIELD_OPTION) {
        let i = sc.v2(SC_STONE_SHIELD_OPTION);
        ib.subele[ELE_EARTH as usize] += i;
        ib.subele[ELE_FIRE as usize] -= i;
    }
    if sc.has(SC_LAUDARAMUS) { sd.bonus.crit_atk_rate += 5 * sc.v1(SC_LAUDARAMUS); }
    if sc.has(SC_FORTUNE) { sd.bonus.crit_atk_rate += 2 * sc.v1(SC_FORTUNE); }
    if sc.has(SC_SYMPHONYOFLOVER) {
        ib.subele[ELE_GHOST as usize] += sc.v1(SC_SYMPHONYOFLOVER) * 3;
        ib.subele[ELE_HOLY as usize] += sc.v1(SC_SYMPHONYOFLOVER) * 3;
    }
    if sc.has(SC_PYREXIA) && sc.v3(SC_PYREXIA) == 0 { sd.bonus.crit_atk_rate += sc.v2(SC_PYREXIA); }
    if sc.has(SC_LUXANIMA) {
        super::bonus::pc_bonus2(c, sd, SP_ADDSIZE, SZ_ALL, sc.v3(SC_LUXANIMA));
        sd.bonus.crit_atk_rate += sc.v3(SC_LUXANIMA);
        sd.bonus.short_attack_atk_rate += sc.v3(SC_LUXANIMA);
        sd.bonus.long_attack_atk_rate += sc.v3(SC_LUXANIMA);
    }
    if sc.has(SC_STRIKING) { sd.bonus.perfect_hit += 20 + 10 * w.checkskill(SO_STRIKING); }
    if sc.has(SC_RUSH_QUAKE2) {
        sd.bonus.short_attack_atk_rate += 5 * sc.v1(SC_RUSH_QUAKE2);
        sd.bonus.long_attack_atk_rate += 5 * sc.v1(SC_RUSH_QUAKE2);
    }
    if sc.has(SC_HIDDEN_CARD) { sd.bonus.long_attack_atk_rate += sc.v3(SC_HIDDEN_CARD); }
    if sc.has(SC_DEADLY_DEFEASANCE) { sd.special_state.no_magic_damage = 0; }
    let ib = &mut sd.indexed_bonus;
    if sc.has(SC_CLIMAX_DES_HU) { ib.magic_atk_ele[ELE_WIND as usize] += 30; }
    if sc.has(SC_CLIMAX_CRYIMP) {
        ib.subele[ELE_WATER as usize] += 30;
        ib.magic_atk_ele[ELE_WATER as usize] += 30;
    }
    if sc.has(SC_SINCERE_FAITH) { sd.bonus.perfect_hit += sc.v3(SC_SINCERE_FAITH); }
    if sc.has(SC_HOLY_S) {
        ib.subele[ELE_DARK as usize] += sc.v2(SC_HOLY_S);
        ib.subele[ELE_UNDEAD as usize] += sc.v2(SC_HOLY_S);
        ib.magic_atk_ele[ELE_HOLY as usize] += sc.v2(SC_HOLY_S);
    }
    if sc.has(SC_SUMMON_ELEMENTAL_ARDOR) { ib.magic_atk_ele[ELE_FIRE as usize] += 10; }
    if sc.has(SC_SUMMON_ELEMENTAL_DILUVIO) { ib.magic_atk_ele[ELE_WATER as usize] += 10; }
    if sc.has(SC_SUMMON_ELEMENTAL_PROCELLA) { ib.magic_atk_ele[ELE_WIND as usize] += 10; }
    if sc.has(SC_SUMMON_ELEMENTAL_TERREMOTUS) { ib.magic_atk_ele[ELE_EARTH as usize] += 10; }
    if sc.has(SC_SUMMON_ELEMENTAL_SERPENS) { ib.magic_atk_ele[ELE_POISON as usize] += 10; }
    if sc.has(SC_FLAMEARMOR_OPTION) {
        ib.subele[ELE_FIRE as usize] += 100;
        ib.subele[ELE_WATER as usize] -= 30;
    }
    if sc.has(SC_CRYSTAL_ARMOR_OPTION) {
        ib.subele[ELE_WATER as usize] += 100;
        ib.subele[ELE_WIND as usize] -= 30;
    }
    if sc.has(SC_EYES_OF_STORM_OPTION) {
        ib.subele[ELE_WIND as usize] += 100;
        ib.subele[ELE_EARTH as usize] -= 30;
    }
    if sc.has(SC_STRONG_PROTECTION_OPTION) {
        ib.subele[ELE_EARTH as usize] += 100;
        ib.subele[ELE_FIRE as usize] -= 30;
    }
    if sc.has(SC_POISON_SHIELD_OPTION) {
        ib.subele[ELE_POISON as usize] += 100;
        ib.subele[ELE_HOLY as usize] -= 30;
    }
    if w.checkskill(SU_POWEROFLAND) > 0 && pc_checkskill_summoner(w, SUMMONER_POWER_LAND) >= 20 {
        super::bonus::pc_bonus(c, sd, SP_MATK_RATE, 20);
    }
    if sc.has(SC_SHRIMP) {
        super::bonus::pc_bonus(c, sd, SP_ATK_RATE, sc.v2(SC_SHRIMP));
        super::bonus::pc_bonus(c, sd, SP_MATK_RATE, sc.v2(SC_SHRIMP));
    }
    if sc.has(SC_INCMATKRATE) { super::bonus::pc_bonus(c, sd, SP_MATK_RATE, sc.v1(SC_INCMATKRATE)); }
    if sc.has(SC_MINDBREAKER) { super::bonus::pc_bonus(c, sd, SP_MATK_RATE, sc.v2(SC_MINDBREAKER)); }
    if sc.has(SC_CATNIPPOWDER) { super::bonus::pc_bonus(c, sd, SP_MATK_RATE, sc.v2(SC_CATNIPPOWDER)); }
    if sc.has(SC_NIBELUNGEN) && sc.v2(SC_NIBELUNGEN) == RINGNBL_MATKRATE { super::bonus::pc_bonus(c, sd, SP_MATK_RATE, 20); }
    if sc.has(SC_TALISMAN_OF_FIVE_ELEMENTS) {
        let bonus = sc.v2(SC_TALISMAN_OF_FIVE_ELEMENTS);
        for element in [ELE_FIRE, ELE_WATER, ELE_WIND, ELE_EARTH, ELE_NEUTRAL] {
            sd.indexed_bonus.magic_addele[element as usize] += bonus;
            sd.right_weapon.addele[element as usize] += bonus;
            if !lcf {
                sd.left_weapon.addele[element as usize] += bonus;
            }
        }
    }
    if sc.has(SC_HEAVEN_AND_EARTH) {
        let i = sc.v2(SC_HEAVEN_AND_EARTH);
        sd.indexed_bonus.magic_atk_ele[ELE_ALL as usize] += i;
        sd.bonus.short_attack_atk_rate += i;
        sd.bonus.long_attack_atk_rate += i;
    }
}

/// item group IG_THROWABLE (script constant).
const IG_THROWABLE: u16 = 477;

// ---------------------------------------------------------------------------------------------- status_calc_bl_main

fn has(flag: u64, scb: u32) -> bool {
    flag & (1u64 << scb) != 0
}
fn set(flag: &mut u64, scb: u32) {
    *flag |= 1u64 << scb;
}

/// status.cpp status_calc_bl_main for a player.
fn status_calc_bl_main(c: &Ctx, aid: u32, flag: &mut u64) {
    let Some(mut sd) = data::sd_get(c, aid) else { return };
    let Some(ch) = c.db().character().id().find(sd.char_id) else { return };
    let Some(mut u) = unit::get(c, aid) else { return };
    let sc = sc::load(c, aid);
    let w = who(c, &sd, &ch, &u);
    let b = sd.base_status.clone();
    let lv = ch.base_level as i32;
    if has(*flag, SCB_SPEED) {
        // Re-walk to adjust speed
        u.ud.change_walk_target = true;
        unit::save(c, &u);
    }
    let st = &mut sd.battle_status;
    if has(*flag, SCB_STR) {
        st.str = scc::status_calc_str(&w, &sc, b.str as i32) as i16;
        set(flag, SCB_BATK);
    }
    if has(*flag, SCB_AGI) {
        st.agi = scc::status_calc_agi(&w, &sc, b.agi as i32) as i16;
        set(flag, SCB_FLEE);
        set(flag, SCB_DEF2);
        set(flag, SCB_ASPD);
        set(flag, SCB_DSPD);
    }
    if has(*flag, SCB_VIT) {
        st.vit = scc::status_calc_vit(&w, &sc, b.vit as i32) as i16;
        set(flag, SCB_DEF2);
        set(flag, SCB_MDEF2);
        set(flag, SCB_MAXHP);
    }
    if has(*flag, SCB_INT) {
        st.int_ = scc::status_calc_int(&w, &sc, b.int_ as i32) as i16;
        set(flag, SCB_MATK);
        set(flag, SCB_MDEF2);
        set(flag, SCB_MAXSP);
    }
    if has(*flag, SCB_DEX) {
        st.dex = scc::status_calc_dex(&w, &sc, b.dex as i32) as i16;
        set(flag, SCB_BATK);
        set(flag, SCB_HIT);
        set(flag, SCB_MATK);
        set(flag, SCB_MDEF2);
        set(flag, SCB_ASPD);
    }
    if has(*flag, SCB_LUK) {
        st.luk = scc::status_calc_luk(&w, &sc, b.luk as i32) as i16;
        set(flag, SCB_BATK);
        set(flag, SCB_CRI);
        set(flag, SCB_FLEE2);
        set(flag, SCB_MATK);
        set(flag, SCB_HIT);
        set(flag, SCB_FLEE);
    }
    if has(*flag, SCB_POW) {
        st.pow = scc::status_calc_pow(&w, &sc, b.pow as i32) as i16;
        set(flag, SCB_BATK);
        set(flag, SCB_PATK);
    }
    if has(*flag, SCB_STA) {
        st.sta = scc::status_calc_sta(&w, &sc, b.sta as i32) as i16;
        set(flag, SCB_RES);
    }
    if has(*flag, SCB_WIS) {
        st.wis = scc::status_calc_wis(&w, &sc, b.wis as i32) as i16;
        set(flag, SCB_MRES);
    }
    if has(*flag, SCB_SPL) {
        st.spl = scc::status_calc_spl(&w, &sc, b.spl as i32) as i16;
        set(flag, SCB_MATK);
        set(flag, SCB_SMATK);
    }
    if has(*flag, SCB_CON) {
        st.con = scc::status_calc_con(&w, &sc, b.con as i32) as i16;
        set(flag, SCB_HIT);
        set(flag, SCB_FLEE);
        set(flag, SCB_PATK);
        set(flag, SCB_SMATK);
    }
    if has(*flag, SCB_CRT) {
        st.crt = scc::status_calc_crt(&w, &sc, b.crt as i32) as i16;
        set(flag, SCB_HPLUS);
        set(flag, SCB_CRATE);
    }
    let weapon = ch.weapon as i32;
    let st = &mut sd.battle_status;
    if has(*flag, SCB_BATK) && b.batk != 0 {
        st.batk = status_base_atk_pc(weapon, st, lv) as i32;
        // Then add the same difference to the base attack of battle status
        let temp = b.batk - status_base_atk_pc(weapon, &b, lv) as i32;
        st.batk += temp;
        st.batk = scc::status_calc_batk(&w, &sc, st.batk);
    }
    if has(*flag, SCB_WATK) {
        if b.watk == 0 {
            // We only have left-hand weapon
            st.watk = 0;
            st.watk2 = scc::status_calc_watk(&w, &sc, b.watk2 as i32);
        } else {
            st.watk = scc::status_calc_watk(&w, &sc, b.watk as i32);
        }
    }
    if has(*flag, SCB_HIT) {
        if st.dex == b.dex && st.luk == b.luk && st.con == b.con {
            st.hit = scc::status_calc_hit(&w, &sc, b.hit as i32);
        } else {
            st.hit = scc::status_calc_hit(
                &w,
                &sc,
                b.hit as i32 + (st.dex as i32 - b.dex as i32) + (st.luk as i32 / 3 - b.luk as i32 / 3) + 2 * (st.con as i32 - b.con as i32),
            );
        }
    }
    if has(*flag, SCB_FLEE) {
        if st.agi == b.agi && st.luk == b.luk && st.con == b.con {
            st.flee = scc::status_calc_flee(&w, &sc, b.flee as i32);
        } else {
            st.flee = scc::status_calc_flee(
                &w,
                &sc,
                b.flee as i32 + (st.agi as i32 - b.agi as i32) + (st.luk as i32 / 5 - b.luk as i32 / 5) + 2 * (st.con as i32 - b.con as i32),
            );
        }
    }
    if has(*flag, SCB_DEF) {
        st.def = scc::status_calc_def(&w, &sc, b.def as i32);
    }
    if has(*flag, SCB_DEF2) {
        let vit = st.vit as i32;
        if st.vit == b.vit && st.agi == b.agi {
            st.def2 = scc::status_calc_def2(&w, &sc, b.def2 as i32, vit);
        } else {
            let d = ((st.vit as f32 / 2.0 - b.vit as f32 / 2.0) + (st.agi as f32 / 5.0 - b.agi as f32 / 5.0)) as i32;
            st.def2 = scc::status_calc_def2(&w, &sc, b.def2 as i32 + d, vit);
        }
    }
    if has(*flag, SCB_MDEF) {
        st.mdef = scc::status_calc_mdef(&w, &sc, b.mdef as i32);
    }
    if has(*flag, SCB_MDEF2) {
        if st.int_ == b.int_ && st.vit == b.vit && st.dex == b.dex {
            st.mdef2 = scc::status_calc_mdef2(&w, &sc, b.mdef2 as i32);
        } else {
            let d = ((st.dex as f32 / 5.0 - b.dex as f32 / 5.0) + (st.vit as f32 / 5.0 - b.vit as f32 / 5.0)) as i32;
            st.mdef2 = scc::status_calc_mdef2(&w, &sc, b.mdef2 as i32 + (st.int_ as i32 - b.int_ as i32) + d);
        }
    }
    if has(*flag, SCB_SPEED) {
        st.speed = scc::status_calc_speed(&w, &sc, b.speed as i32);
        if !sd.permanent_speed && (sd.battle_status.speed as i32) < crate::conf::max_walk_speed_conf() {
            sd.battle_status.speed = crate::conf::max_walk_speed_conf() as u16;
        }
        // RENEWAL: Recalculate homunculus speed if the player receives a speed buff/debuff (status.cpp:6089-6097,
        // homunculus/status.rs)
        crate::homunculus::status::master_speed_changed(c, aid, sd.battle_status.speed);
    }
    let st = &mut sd.battle_status;
    if has(*flag, SCB_CRI) && b.cri != 0 {
        if st.luk == b.luk {
            st.cri = scc::status_calc_critical(&w, &sc, b.cri as i32);
        } else {
            st.cri = scc::status_calc_critical(&w, &sc, b.cri as i32 + 3 * (st.luk as i32 - b.luk as i32));
        }
        // After status_calc_critical so the bonus is applied despite if you have or not a sc bugreport:5240
        if weapon == W_KATAR {
            st.cri = st.cri.wrapping_mul(2);
        }
    }
    if has(*flag, SCB_FLEE2) && b.flee2 != 0 {
        if st.luk == b.luk {
            st.flee2 = scc::status_calc_flee2(&w, &sc, b.flee2 as i32);
        } else {
            st.flee2 = scc::status_calc_flee2(&w, &sc, b.flee2 as i32 + (st.luk as i32 - b.luk as i32));
        }
    }
    if has(*flag, SCB_ATK_ELE) {
        st.rhw.ele = scc::status_calc_attack_element(&sc, b.rhw.ele as i32);
        // (lr_flag = LR_FLAG_WEAPON around the left hand: no status reads it here)
        st.lhw.ele = scc::status_calc_attack_element(&sc, b.lhw.ele as i32);
    }
    if has(*flag, SCB_DEF_ELE) {
        st.def_ele = scc::status_calc_element(&w, &sc, b.def_ele as i32);
        st.ele_lv = scc::status_calc_element_lv(&w, &sc, b.ele_lv as i32);
    }
    if has(*flag, SCB_MODE) {
        st.mode = scc::status_calc_mode(&w, &sc, b.mode);
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
        let mode = st.mode;
        if mode & MD_CANATTACK != MD_CANATTACK || mode & MD_CANMOVE != MD_CANMOVE {
            if let Some(mut u) = unit::get(c, aid) {
                if mode & MD_CANATTACK != MD_CANATTACK {
                    unit::stop_attack(c, &mut u);
                }
                if mode & MD_CANMOVE != MD_CANMOVE {
                    unit::stop_walking(c, &mut u, unit::USW_FIXPOS, 0);
                }
                unit::save(c, &u);
            }
        }
    }
    if has(*flag, SCB_MAXHP) {
        let vit = sd.battle_status.vit as u16 as u32;
        let mut max_hp = status_calc_maxhp_pc(&w, &sd, &ch, &sc, vit);
        if crate::conf::hp_rate() != 100 {
            max_hp = (crate::conf::hp_rate() as f64 * (max_hp as f64 / 100.0)) as u32;
        }
        max_hp = if ch.base_level < 100 {
            max_hp.min(crate::conf::max_hp_lv99())
        } else if ch.base_level < 151 {
            max_hp.min(crate::conf::max_hp_lv150())
        } else {
            max_hp.min(crate::conf::max_hp())
        };
        sd.battle_status.max_hp = max_hp;
        if sd.battle_status.hp > sd.battle_status.max_hp {
            // !FIXME: Should perhaps a status_zap should be issued?
            sd.battle_status.hp = sd.battle_status.max_hp;
            data::sd_put(c, &sd);
            super::clif::updatestatus(c, aid, SP_HP);
        }
    }
    if has(*flag, SCB_MAXSP) {
        let int_ = sd.battle_status.int_ as u16 as u32;
        let mut max_sp = status_calc_maxsp_pc(&w, &sd, &ch, &sc, int_);
        if crate::conf::sp_rate() != 100 {
            max_sp = (crate::conf::sp_rate() as f64 * (max_sp as f64 / 100.0)) as u32;
        }
        sd.battle_status.max_sp = max_sp.min(crate::conf::max_sp());
        if sd.battle_status.sp > sd.battle_status.max_sp {
            sd.battle_status.sp = sd.battle_status.max_sp;
            data::sd_put(c, &sd);
            super::clif::updatestatus(c, aid, SP_SP);
        }
    }
    if has(*flag, SCB_MATK) {
        // MATK = StatusMATK + WeaponMATK + ExtraMATK
        let st = &mut sd.battle_status;
        let mut matk_min = status_base_matk_pc(st, lv) as i32;
        let mut matk_max = status_base_matk_pc(st, lv) as i32;
        // Soul energy spheres increase MATK (not displayed in status window).
        // sd->soulball (the skill system's PcSkill row)
        let soulball = crate::skill::state::get(c, aid).soulball as i32;
        matk_min += 3 * soulball;
        matk_max += 3 * soulball;
        if b.lhw.matk > 0 {
            st.lhw.matk = b.lhw.matk;
        }
        if b.rhw.matk > 0 {
            st.rhw.matk = b.rhw.matk;
        }
        let mut w_matk: i32 = 0;
        let mut variance: i32 = 0;
        if st.rhw.matk > 0 {
            w_matk = st.rhw.matk as i32;
            variance = st.rhw.matk as i32 * st.rhw.wlv as i32 / 10;
        }
        if st.lhw.matk > 0 {
            w_matk += st.lhw.matk as i32;
            variance += st.lhw.matk as i32 * st.lhw.wlv as i32 / 10;
        }
        matk_min += w_matk - variance;
        matk_max += w_matk + variance;
        // Apply Recognized Spell buff
        if sc.has(SC_RECOGNIZEDSPELL) {
            matk_min = matk_min.max(matk_max);
        }
        if sd.right_weapon.overrefine > 0 {
            matk_min += 1;
            matk_max += sd.right_weapon.overrefine - 1;
        }
        // Apply MATK % from skill Mystical Amplification
        if sc.has(SC_MAGICPOWER) {
            matk_min += matk_min * sc.v3(SC_MAGICPOWER) / 100;
            matk_max += matk_max * sc.v3(SC_MAGICPOWER) / 100;
        }
        // EquipMATK (flat MATK from equipments)
        if sd.bonus.ematk != 0 {
            matk_min += sd.bonus.ematk;
            matk_max += sd.bonus.ematk;
        }
        // Flat MATK not visible in status window
        if sd.bonus.ematk_hidden != 0 {
            matk_min += sd.bonus.ematk_hidden;
            matk_max += sd.bonus.ematk_hidden;
        }
        // PseudoBuffMATK (flat MATK from skills)
        matk_min = scc::status_calc_pseudobuff_matk(&w, &sc, matk_min) as i32;
        matk_max = scc::status_calc_pseudobuff_matk(&w, &sc, matk_max) as i32;
        // ConsumableMATK (flat MATK from consumables)
        matk_min = scc::status_calc_consumablematk(&sc, matk_min) as i32;
        matk_max = scc::status_calc_consumablematk(&sc, matk_max) as i32;
        // Apply MATK % (from equipments, usable items...)
        if sd.rates.matk_rate != 100 {
            matk_min = matk_min * sd.rates.matk_rate / 100;
            matk_max = matk_max * sd.rates.matk_rate / 100;
        }
        sd.battle_status.matk_min = cap_value(matk_min, 0, u16::MAX as i32) as u16;
        sd.battle_status.matk_max = cap_value(matk_max, 0, u16::MAX as i32) as u16;
    }
    if has(*flag, SCB_ASPD) {
        let st_now = sd.battle_status.clone();
        let mut amotion = status_base_amotion_pc(&w, &sd, &ch, &st_now, &sc);
        // Absolute ASPD % modifiers
        amotion = amotion * sd.battle_status.aspd_rate as i32 / 1000;
        let skill_lv = w.checkskill(SA_FREECAST) as u16;
        if u.ud.skilltimer != 0 && skill_lv > 0 {
            amotion = amotion * 5 * (skill_lv as i32 + 10) / 100;
        }
        // RE ASPD % modifier
        amotion += (195 - amotion).max(2) * (sd.battle_status.aspd_rate2 as i32 + scc::status_calc_aspd(&w, &sc, false) as i32) / 100;
        // Renewal base value is actually ASPD and not amotion, so we need to convert it
        amotion = AMOTION_ZERO_ASPD - amotion * AMOTION_INTERVAL;
        amotion += sd.bonus.aspd_add;
        amotion = scc::status_calc_fix_aspd(&w, &sc, amotion) as i32;
        sd.battle_status.amotion = cap_value(amotion, pc_maxaspd(sd.class_) / AMOTION_DIVIDER_PC, MIN_ASPD / AMOTION_DIVIDER_PC) as u16;
        sd.battle_status.adelay = (AMOTION_DIVIDER_PC * sd.battle_status.amotion as i32) as u16;
    }
    if has(*flag, SCB_DSPD) {
        if b.agi != sd.battle_status.agi {
            let dmotion = 800 - sd.battle_status.agi as i32 * 4;
            sd.battle_status.dmotion = cap_value(dmotion, 400, 800) as u16;
            if crate::conf::pc_damage_delay_rate() != 100 {
                sd.battle_status.dmotion = (sd.battle_status.dmotion as i32 * crate::conf::pc_damage_delay_rate() / 100) as u16;
            }
        }
    }
    let st = &mut sd.battle_status;
    if has(*flag, SCB_PATK) {
        if st.pow == b.pow && st.con == b.con {
            st.patk = scc::status_calc_patk(&w, &sc, b.patk as i32);
        } else {
            st.patk = scc::status_calc_patk(&w, &sc, b.patk as i32 + (st.pow as i32 - b.pow as i32) / 3 + (st.con as i32 - b.con as i32) / 5);
        }
    }
    if has(*flag, SCB_SMATK) {
        if st.spl == b.spl && st.con == b.con {
            st.smatk = scc::status_calc_smatk(&w, &sc, b.smatk as i32);
        } else {
            // (rAthena adds the stat difference outside of status_calc_smatk here)
            st.smatk = (scc::status_calc_smatk(&w, &sc, b.smatk as i32) as i32 + (st.spl as i32 - b.spl as i32) / 3 + (st.con as i32 - b.con as i32) / 5) as i16;
        }
    }
    if has(*flag, SCB_RES) {
        if st.sta == b.sta {
            st.res = scc::status_calc_res(&w, &sc, b.res as i32);
        } else {
            st.res = scc::status_calc_res(&w, &sc, b.res as i32 + (st.sta as i32 - b.sta as i32) + (st.sta as i32 - b.sta as i32) / 3 * 5);
        }
    }
    if has(*flag, SCB_MRES) {
        if st.wis == b.wis {
            st.mres = scc::status_calc_mres(&w, &sc, b.mres as i32);
        } else {
            st.mres = scc::status_calc_mres(&w, &sc, b.mres as i32 + (st.wis as i32 - b.wis as i32) + (st.wis as i32 - b.wis as i32) / 3 * 5);
        }
    }
    if has(*flag, SCB_HPLUS) {
        if st.crt == b.crt {
            st.hplus = scc::status_calc_hplus(&w, &sc, b.hplus as i32);
        } else {
            st.hplus = scc::status_calc_hplus(&w, &sc, b.hplus as i32 + (st.crt as i32 - b.crt as i32));
        }
    }
    if has(*flag, SCB_CRATE) {
        if st.crt == b.crt {
            st.crate_ = scc::status_calc_crate(&w, &sc, b.crate_ as i32);
        } else {
            st.crate_ = scc::status_calc_crate(&w, &sc, b.crate_ as i32 + (st.crt as i32 - b.crt as i32) / 3);
        }
    }
    if has(*flag, SCB_MAXAP) {
        let mut max_ap = status_calc_maxap_pc(&w, &sd, &ch, &sc);
        if crate::conf::ap_rate() != 100 {
            max_ap = (crate::conf::ap_rate() as f64 * (max_ap as f64 / 100.0)) as u32;
        }
        sd.battle_status.max_ap = max_ap.min(crate::conf::max_ap());
        if sd.battle_status.ap > sd.battle_status.max_ap {
            sd.battle_status.ap = sd.battle_status.max_ap;
            data::sd_put(c, &sd);
            super::clif::updatestatus(c, aid, SP_AP);
        }
    }
    if has(*flag, SCB_VIT) || has(*flag, SCB_MAXHP) || has(*flag, SCB_INT) || has(*flag, SCB_MAXSP) {
        let st = sd.battle_status.clone();
        status_calc_regen(c, &mut sd, &ch, &st);
    }
    if has(*flag, SCB_REGEN) {
        status_calc_regen_rate(c, &mut sd, &sc);
    }
    data::sd_put(c, &sd);
}

/// The unit row follows the status: status_get_speed, the HP bar values and death state (clif reads them there).
pub fn sync_unit(c: &Ctx, aid: u32) {
    let Some(sd) = data::sd_get(c, aid) else { return };
    let Some(mut u) = unit::get(c, aid) else { return };
    let (speed, hp, max_hp) = (unit::pc_effective_speed(sd.battle_status.speed, &u), sd.battle_status.hp as i64, sd.battle_status.max_hp as i64);
    if u.speed != speed || u.hp != hp || u.max_hp != max_hp {
        u.speed = speed;
        u.hp = hp;
        u.max_hp = max_hp;
        unit::save(c, &u);
    }
}
