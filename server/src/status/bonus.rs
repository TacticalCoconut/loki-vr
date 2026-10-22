//! pc.cpp item bonuses: pc_bonus, pc_bonus2 … pc_bonus5 and their helpers (pc_bonus_autospell*, pc_bonus_addeff*,
//! pc_bonus_item_drop, pc_bonus_addele / subele / subrace, pc_bonus_regen, pc_bonus_itembonus, pc_bonus_addvanish),
//! autobonus (pc_addautobonus / pc_delautobonus / pc_exeautobonus / pc_endautobonus, script.cpp
//! script_run_autobonus) and bonus_script (pc_bonus_script, _add, _clear, _timer; chrif bsdata load / save).
//!
//! The bonus functions work on the character's status row as rAthena's work on map_session_data: they are called
//! from item / card / combo / status scripts while status_calc_pc runs (the row is then the in-calculation copy, see
//! data.rs) and from status_calc_pc_sub itself.

use spacetimedb::Table;

use super::calc;
use super::consts::*;
use super::consts::SC_NONE;
use super::data::{self, bonus_script, AddDrop, AddEffect, AddEffectOnSkill, AddEle2, AddRace2, Autobonus, Autospell, BonusScript, ItemBonus, PcStatus, SRegen, VanishBonus};
use super::defs::*;
use crate::db::items as itemdb;
use crate::db::skills::*;
use crate::timer::{self, Func};
use crate::world::{Ctx, Tick};

// ---------------------------------------------------------------------------------------------- helpers

/// util::safe_addition_cap for the int16 fields (rate + value capped at `cap`, with the same overflow rules).
fn safe_add_i16(a: i16, b: i16, cap: i16) -> i16 {
    safe_addition_cap(a as i64, b as i64, cap as i64) as i16
}

fn safe_add_i32(a: i32, b: i32, cap: i32) -> i32 {
    safe_addition_cap(a as i64, b as i64, cap as i64) as i32
}

/// The item a "pc_bonus: unknown bonus type" warning names (pc.cpp default cases).
fn warn_unknown(c: &Ctx, sd: &PcStatus, func: &str, args: &str) {
    let combo_pos = calc::current_equip_combo_pos();
    let card = calc::current_equip_card_id();
    let index = calc::current_equip_item_index();
    if combo_pos > 0 {
        let nameid = pc_checkequip_nameid(c, sd, combo_pos).unwrap_or(0);
        log::warn!("{func}: unknown bonus type {args} in a combo with item #{nameid}");
    } else if card > 0 || index > 0 {
        let nameid = if card != 0 { card as u32 } else { crate::inventory::inv_get(c, sd.account_id, index as usize).map(|r| r.item.nameid).unwrap_or(0) };
        log::warn!("{func}: unknown bonus type {args} in item #{nameid}");
    } else {
        log::warn!("{func}: unknown bonus type {args} in unknown usage. Report this!");
    }
}

/// sd->status.weapon (the character row; pc_calcweapontype keeps it).
fn sd_weapon(c: &Ctx, aid: u32) -> i32 {
    crate::pc::character_of(c, aid).map(|ch| ch.weapon as i32).unwrap_or(W_FIST)
}

/// `sd->inventory_data[pc_checkequip(sd, pos)]->nameid` (pc.cpp pc_bonus*'s combo warning).
fn pc_checkequip_nameid(c: &Ctx, sd: &PcStatus, pos: u32) -> Option<u32> {
    let i = crate::pc_equip::pc_checkequip(c, sd.account_id, pos, false);
    if i < 0 {
        return None;
    }
    crate::inventory::inv_get(c, sd.account_id, i as usize).map(|r| r.item.nameid)
}

/// PC_BONUS_CHK_ELEMENT and friends: the bonus is skipped with an error when the index is invalid.
fn chk(ok: bool, func: &str, bonus: i32, what: &str, v: i32) -> bool {
    if !ok {
        log::error!("{func}: {bonus}: Invalid {what} {v}.");
    }
    ok
}

fn chk_sc(v: i32) -> bool {
    v > SC_NONE && v < SC_MAX
}

fn chk_race2(v: i32) -> bool {
    v > RC2_NONE && (v as usize) < RC2_MAX
}

/// Adds `val` to element `i` of an indexed bonus table (rAthena indexes unchecked after the CHK macros; indexes the
/// macros do not check — weapon types — are skipped when out of range instead of overrunning).
fn add_at<T: Copy + std::ops::Add<Output = T>>(v: &mut [T], i: i32, val: T) {
    if i >= 0 {
        if let Some(x) = v.get_mut(i as usize) {
            *x = *x + val;
        }
    }
}

/// pc.cpp pc_bonus_autospell.
fn pc_bonus_autospell(spell: &mut Vec<Autospell>, id: u16, lv: u16, rate: i16, mut battle_flag: i16, card_id: u32, flag: u8) {
    if spell.len() == MAX_PC_BONUS {
        log::warn!("pc_bonus_autospell: Reached max ({MAX_PC_BONUS}) number of autospells per character!");
        return;
    }
    if rate == 0 {
        return;
    }
    let bf = battle_flag as i32;
    let mut bf2 = bf;
    if bf2 & BF_RANGEMASK == 0 {
        bf2 |= BF_SHORT | BF_LONG; // No range defined? Use both.
    }
    if bf2 & BF_WEAPONMASK == 0 {
        bf2 |= BF_WEAPON; // No attack type defined? Use weapon.
    }
    if bf2 & BF_SKILLMASK == 0 {
        if bf2 & (BF_MAGIC | BF_MISC) != 0 {
            bf2 |= BF_SKILL; // These two would never trigger without BF_SKILL
        }
        if bf2 & BF_WEAPON != 0 {
            bf2 |= BF_NORMAL; // By default autospells should only trigger on normal weapon attacks.
        }
    }
    battle_flag = bf2 as i16;
    for it in spell.iter_mut() {
        if (it.card_id == card_id || it.rate < 0 || rate < 0) && it.id == id && it.lv == lv && it.battle_flag == battle_flag && it.flag == flag {
            if !crate::conf::autospell_stacking() && it.rate > 0 && rate > 0 {
                // Stacking disabled
                return;
            }
            it.rate = safe_add_i16(it.rate, rate, 1000);
            return;
        }
    }
    spell.push(Autospell { id, lv, trigger_skill: 0, rate: cap_value(rate, -1000, 1000), battle_flag, card_id, flag, lock: false });
}

/// pc.cpp pc_bonus_autospell_onskill.
fn pc_bonus_autospell_onskill(spell: &mut Vec<Autospell>, src_skill: u16, id: u16, lv: u16, rate: i16, card_id: u32, flag: u8) {
    if spell.len() == MAX_PC_BONUS {
        log::warn!("pc_bonus_autospell_onskill: Reached max ({MAX_PC_BONUS}) number of autospells per character!");
        return;
    }
    if rate == 0 {
        return;
    }
    spell.push(Autospell { id, lv, trigger_skill: src_skill, rate: cap_value(rate, -1000, 1000), battle_flag: 0, card_id, flag, lock: false });
}

/// pc.cpp pc_bonus_addeff.
fn pc_bonus_addeff(effect: &mut Vec<AddEffect>, sc: i32, rate: i32, arrow_rate: i16, mut flag: u8, mut duration: u32) {
    if effect.len() == MAX_PC_BONUS {
        log::warn!("pc_bonus_addeff: Reached max ({MAX_PC_BONUS}) number of add effects per character!");
        return;
    }
    let f = flag as i32;
    let mut f2 = f;
    if f2 & (ATF_SHORT | ATF_LONG) == 0 {
        f2 |= ATF_SHORT | ATF_LONG; // Default range: both
    }
    if f2 & (ATF_TARGET | ATF_SELF) == 0 {
        f2 |= ATF_TARGET; // Default target: enemy.
    }
    if f2 & (ATF_WEAPON | ATF_MAGIC | ATF_MISC) == 0 {
        f2 |= ATF_WEAPON; // Default type: weapon.
    }
    flag = f2 as u8;
    if duration == 0 {
        duration = skill_get_time2(crate::db::misc::status::status_db().get_skill(sc), 7) as u32;
    }
    for it in effect.iter_mut() {
        if it.sc == sc && it.flag == flag {
            it.rate = safe_add_i32(it.rate, rate, i32::MAX);
            it.arrow_rate = safe_add_i16(it.arrow_rate, arrow_rate, i16::MAX);
            it.duration = it.duration.max(duration);
            return;
        }
    }
    effect.push(AddEffect { sc, rate, arrow_rate, flag, duration });
}

/// pc.cpp pc_bonus_addeff_onskill.
fn pc_bonus_addeff_onskill(effect: &mut Vec<AddEffectOnSkill>, sc: i32, rate: i32, skill_id: i16, target: u8, mut duration: u32) {
    if effect.len() == MAX_PC_BONUS {
        log::warn!("pc_bonus_addeff_onskill: Reached max ({MAX_PC_BONUS}) number of add effects per character!");
        return;
    }
    if duration == 0 {
        duration = skill_get_time2(crate::db::misc::status::status_db().get_skill(sc), 7) as u32;
    }
    for it in effect.iter_mut() {
        if it.sc == sc && it.skill_id == skill_id && it.target == target {
            it.rate = safe_add_i32(it.rate, rate, i32::MAX);
            it.duration = it.duration.max(duration);
            return;
        }
    }
    effect.push(AddEffectOnSkill { sc, rate, skill_id, target, duration });
}

/// pc.cpp pc_bonus_item_drop.
fn pc_bonus_item_drop(drop: &mut Vec<AddDrop>, nameid: u32, group: u16, class_: i32, race: i16, mut rate: i32) {
    if nameid == 0 && group == 0 {
        log::warn!("pc_bonus_item_drop: No Item ID nor Item Group ID specified.");
        return;
    }
    if nameid != 0 && itemdb::itemdb_exists(nameid).is_none() {
        log::warn!("pc_bonus_item_drop: Invalid item id {nameid}");
        return;
    }
    if group != 0 && itemdb::group::item_group(group).is_none() {
        log::warn!("pc_bonus_item_drop: Invalid Item Group {group}");
        return;
    }
    if drop.len() == MAX_PC_BONUS {
        log::warn!("pc_bonus_item_drop: Reached max ({MAX_PC_BONUS}) number of added drops per character! (nameid: {nameid} group: {group} class_: {class_} race: {race} rate: {rate})");
        return;
    }
    // Apply config rate adjustment settings.
    if rate >= 0 {
        // Absolute drop.
        if crate::conf::item_rate_adddrop() != 100 {
            rate = rate * crate::conf::item_rate_adddrop() / 100;
        }
        if rate < crate::conf::item_drop_adddrop_min() {
            rate = crate::conf::item_drop_adddrop_min();
        } else if rate > crate::conf::item_drop_adddrop_max() {
            rate = crate::conf::item_drop_adddrop_max();
        }
    } else {
        // Relative drop, max/min limits are applied at drop time.
        if crate::conf::item_rate_adddrop() != 100 {
            rate = rate * crate::conf::item_rate_adddrop() / 100;
        }
        if rate > -1 {
            rate = -1;
        }
    }
    // (s_add_drop::class_ is uint16 in pc.hpp: CLASS_NONE is stored truncated as 65535). pc.cpp:3331 compares that
    // uint16 with the int32 parameter, so an entry of CLASS_NONE (-1: bAddMonsterDropItem, bAddMonsterIdDropItem,
    // bAddMonsterDropItemGroup) never matches and every such bonus keeps its own entry (its own roll).
    let class_u = class_ as u16;
    for it in drop.iter_mut() {
        if it.nameid == nameid && it.group == group && it.race == race && it.class_ as i32 == class_ {
            if (rate < 0 && it.rate < 0) || (rate > 0 && it.rate > 0) {
                // Adjust the rate if it has same classification
                it.rate = safe_add_i32(it.rate, rate, 10000);
            }
            return;
        }
    }
    drop.push(AddDrop { nameid, group, rate: cap_value(rate, -10000, 10000), race, class_: class_u });
}

/// The BF_* defaults pc_bonus_addele / subele / subrace / addvanish apply to a battle flag.
fn default_battle_flag(mut flag: i32) -> i32 {
    if flag & BF_RANGEMASK == 0 {
        flag |= BF_SHORT | BF_LONG;
    }
    if flag & BF_WEAPONMASK == 0 {
        flag |= BF_WEAPON;
    }
    if flag & BF_SKILLMASK == 0 {
        if flag & (BF_MAGIC | BF_MISC) != 0 {
            flag |= BF_SKILL;
        }
        if flag & BF_WEAPON != 0 {
            flag |= BF_NORMAL | BF_SKILL;
        }
    }
    flag
}

/// pc.cpp pc_bonus_addele.
fn pc_bonus_addele(sd: &mut PcStatus, ele: u8, rate: i16, flag: i16) {
    let wd = if sd.lr_flag != LR_FLAG_NONE { &mut sd.left_weapon } else { &mut sd.right_weapon };
    if wd.addele2.len() == MAX_PC_BONUS {
        log::warn!("pc_bonus_addele: Reached max ({MAX_PC_BONUS}) number of add element damage bonuses per character!");
        return;
    }
    let flag = default_battle_flag(flag as i32) as i16;
    for it in wd.addele2.iter_mut() {
        if it.ele == ele && it.flag == flag {
            it.rate = safe_add_i16(it.rate, rate, 10000);
            return;
        }
    }
    wd.addele2.push(AddEle2 { flag, rate: cap_value(rate, -10000, 10000), ele });
}

/// pc.cpp pc_bonus_regen.
fn pc_bonus_regen(bonus: &mut Vec<SRegen>, value: i16, rate: i32) {
    if rate < 1 {
        return;
    }
    bonus.push(SRegen { value, rate, tick: 0 });
}

/// pc.cpp pc_bonus_subele.
fn pc_bonus_subele(sd: &mut PcStatus, ele: u8, rate: i16, flag: i16) {
    if sd.lists.subele2.len() == MAX_PC_BONUS {
        log::warn!("pc_bonus_subele: Reached max ({MAX_PC_BONUS}) number of resist element damage bonuses per character!");
        return;
    }
    let flag = default_battle_flag(flag as i32) as i16;
    for it in sd.lists.subele2.iter_mut() {
        if it.ele == ele && it.flag == flag {
            it.rate = safe_add_i16(it.rate, rate, 10000);
            return;
        }
    }
    sd.lists.subele2.push(AddEle2 { flag, rate: cap_value(rate, -10000, 10000), ele });
}

/// pc.cpp pc_bonus_subrace.
fn pc_bonus_subrace(sd: &mut PcStatus, race: u8, rate: i16, flag: i16) {
    if sd.lists.subrace3.len() == MAX_PC_BONUS {
        log::warn!("pc_bonus_subrace: Reached max ({MAX_PC_BONUS}) number of resist race damage bonuses per character!");
        return;
    }
    let flag = default_battle_flag(flag as i32) as i16;
    for it in sd.lists.subrace3.iter_mut() {
        if it.race == race && it.flag == flag {
            it.rate = safe_add_i16(it.rate, rate, 10000);
            return;
        }
    }
    sd.lists.subrace3.push(AddRace2 { flag, rate: cap_value(rate, -10000, 10000), race });
}

/// pc.cpp pc_bonus_itembonus.
fn pc_bonus_itembonus(bonus: &mut Vec<ItemBonus>, id: u16, val: i32, cap_rate: bool) {
    for it in bonus.iter_mut() {
        if it.id == id {
            if cap_rate {
                it.val = safe_add_i32(it.val, val, 10000);
            } else {
                it.val = it.val.wrapping_add(val);
            }
            return;
        }
    }
    bonus.push(ItemBonus { id, val: if cap_rate { cap_value(val, -10000, 10000) } else { val } });
}

/// pc.cpp pc_bonus_addvanish.
fn pc_bonus_addvanish(bonus: &mut Vec<VanishBonus>, rate: i16, per: i16, flag: i32) {
    if bonus.len() == MAX_PC_BONUS {
        log::warn!("pc_bonus_addvanish: Reached max ({MAX_PC_BONUS}) number of vanish damage bonuses per character!");
        return;
    }
    let flag = default_battle_flag(flag);
    for it in bonus.iter_mut() {
        if it.flag == flag {
            it.rate = safe_add_i16(it.rate, rate, 10000);
            it.per = it.per.wrapping_add(per);
            return;
        }
    }
    bonus.push(VanishBonus { rate: cap_value(rate, -10000, 10000), per, flag });
}

fn is_ranged_weapon(weapon: i32) -> bool {
    matches!(weapon, W_BOW | W_REVOLVER | W_RIFLE | W_GATLING | W_SHOTGUN | W_GRENADE)
}

fn cap16(v: i32) -> i16 {
    cap_value(v, i16::MIN as i32, i16::MAX as i32) as i16
}

// ---------------------------------------------------------------------------------------------- pc_bonus

/// pc.cpp pc_bonus: `bonus bBonusName,val;`.
pub fn pc_bonus(c: &Ctx, sd: &mut PcStatus, type_: i32, mut val: i32) {
    let lr = sd.lr_flag;
    let not_arrow = lr != LR_FLAG_ARROW;
    let status = &mut sd.base_status;
    match type_ {
        SP_STR | SP_AGI | SP_VIT | SP_INT | SP_DEX | SP_LUK => {
            if not_arrow {
                add_at(&mut sd.indexed_bonus.param_bonus, type_ - SP_STR, val);
            }
        }
        SP_POW | SP_STA | SP_WIS | SP_SPL | SP_CON | SP_CRT => {
            if not_arrow {
                add_at(&mut sd.indexed_bonus.param_bonus, type_ - SP_POW + PARAM_POW as i32, val);
            }
        }
        SP_ATK1 => {
            if lr == LR_FLAG_NONE {
                status.rhw.atk = cap_value(status.rhw.atk as i32 + val, 0, u16::MAX as i32) as u16;
            } else if lr == LR_FLAG_WEAPON {
                status.lhw.atk = cap_value(status.lhw.atk as i32 + val, 0, u16::MAX as i32) as u16;
            }
        }
        SP_ATK2 => {
            if lr == LR_FLAG_NONE {
                status.rhw.atk2 = cap_value(status.rhw.atk2 as i32 + val, 0, u16::MAX as i32) as u16;
            } else if lr == LR_FLAG_WEAPON {
                status.lhw.atk2 = cap_value(status.lhw.atk2 as i32 + val, 0, u16::MAX as i32) as u16;
            }
        }
        SP_BASE_ATK => {
            if not_arrow {
                // RENEWAL
                sd.bonus.eatk = cap16(sd.bonus.eatk + val) as i32;
            }
        }
        SP_DEF1 => {
            if not_arrow {
                status.def = cap16(status.def as i32 + val);
            }
        }
        SP_DEF2 => {
            if not_arrow {
                status.def2 = cap16(status.def2 as i32 + val);
            }
        }
        SP_MDEF1 => {
            if not_arrow {
                let bonus = status.mdef as i32 + val;
                status.mdef = cap16(bonus);
                if lr == LR_FLAG_SHIELD {
                    // Shield, used for royal guard
                    sd.bonus.shieldmdef += bonus;
                }
            }
        }
        SP_MDEF2 => {
            if not_arrow {
                status.mdef2 = cap16(status.mdef2 as i32 + val);
            }
        }
        SP_HIT => {
            if not_arrow {
                status.hit = cap16(status.hit as i32 + val);
            } else {
                sd.bonus.arrow_hit += val;
            }
        }
        SP_FLEE1 => {
            if not_arrow {
                status.flee = cap16(status.flee as i32 + val);
            }
        }
        SP_FLEE2 => {
            if not_arrow {
                status.flee2 = cap16(status.flee2 as i32 + val * 10);
            }
        }
        SP_CRITICAL => {
            if not_arrow {
                status.cri = cap16(status.cri as i32 + val * 10);
            } else {
                sd.bonus.arrow_cri += val * 10;
            }
        }
        SP_PATK => {
            if not_arrow {
                status.patk = cap16(status.patk as i32 + val);
            }
        }
        SP_SMATK => {
            if not_arrow {
                status.smatk = cap16(status.smatk as i32 + val);
            }
        }
        SP_RES => {
            if not_arrow {
                status.res = cap16(status.res as i32 + val);
            }
        }
        SP_MRES => {
            if not_arrow {
                status.mres = cap16(status.mres as i32 + val);
            }
        }
        SP_HPLUS => {
            if not_arrow {
                status.hplus = cap16(status.hplus as i32 + val);
            }
        }
        SP_CRATE => {
            if not_arrow {
                status.crate_ = cap16(status.crate_ as i32 + val);
            }
        }
        SP_ATKELE => {
            if !chk(chk_element(val), "pc_bonus", SP_ATKELE, "Element", val) {
                return;
            }
            match lr {
                LR_FLAG_ARROW => {
                    if is_ranged_weapon(sd_weapon(c, sd.account_id)) {
                        // Become weapon element.
                        status.rhw.ele = val as u8;
                    } else {
                        // Become arrow element.
                        sd.bonus.arrow_ele = val;
                    }
                }
                LR_FLAG_WEAPON => status.lhw.ele = val as u8,
                _ => status.rhw.ele = val as u8,
            }
        }
        SP_DEFELE => {
            if !chk(chk_element(val), "pc_bonus", SP_DEFELE, "Element", val) {
                return;
            }
            if not_arrow {
                status.def_ele = val as u8;
            }
        }
        SP_MAXHP => {
            if not_arrow {
                sd.bonus.hp += val;
            }
        }
        SP_MAXSP => {
            if not_arrow {
                sd.bonus.sp += val;
            }
        }
        SP_MAXAP => {
            if not_arrow {
                sd.bonus.ap += val;
            }
        }
        SP_MAXHPRATE => {
            if not_arrow {
                sd.rates.hprate += val;
            }
        }
        SP_MAXSPRATE => {
            if not_arrow {
                sd.rates.sprate += val;
            }
        }
        SP_MAXAPRATE => {
            if not_arrow {
                sd.rates.aprate += val;
            }
        }
        SP_SPRATE => {
            if not_arrow {
                sd.rates.dsprate += val;
            }
        }
        SP_ATTACKRANGE => match lr {
            LR_FLAG_ARROW => {
                if is_ranged_weapon(sd_weapon(c, sd.account_id)) {
                    status.rhw.range = (status.rhw.range as i32 + val) as u16;
                }
            }
            LR_FLAG_WEAPON => status.lhw.range = (status.lhw.range as i32 + val) as u16,
            _ => status.rhw.range = (status.rhw.range as i32 + val) as u16,
        },
        SP_SPEED_RATE => {
            // Non stackable increase
            if not_arrow {
                sd.bonus.speed_rate = sd.bonus.speed_rate.min(-val);
            }
        }
        SP_SPEED_ADDRATE => {
            // Stackable increase
            if not_arrow {
                sd.bonus.speed_add_rate -= val;
            }
        }
        SP_ASPD => {
            // Raw increase
            if not_arrow {
                sd.bonus.aspd_add -= 10 * val;
            }
        }
        SP_ASPD_RATE => {
            // Stackable increase (RENEWAL_ASPD)
            if not_arrow {
                status.aspd_rate2 = (status.aspd_rate2 as i32 + val) as i16;
            }
        }
        SP_HP_RECOV_RATE => {
            if not_arrow {
                sd.rates.hprecov_rate += val;
            }
        }
        SP_SP_RECOV_RATE => {
            if not_arrow {
                sd.rates.sprecov_rate += val;
            }
        }
        SP_CRITICAL_DEF => {
            if not_arrow {
                sd.bonus.critical_def += val;
            }
        }
        SP_NEAR_ATK_DEF => {
            if not_arrow {
                sd.bonus.near_attack_def_rate += val;
            }
        }
        SP_LONG_ATK_DEF => {
            if not_arrow {
                sd.bonus.long_attack_def_rate += val;
            }
        }
        SP_DOUBLE_RATE => {
            if lr == LR_FLAG_NONE && sd.bonus.double_rate < val {
                sd.bonus.double_rate = val;
            }
        }
        SP_DOUBLE_ADD_RATE => {
            if lr == LR_FLAG_NONE {
                sd.bonus.double_add_rate += val;
            }
        }
        SP_MATK_RATE => {
            if not_arrow {
                sd.rates.matk_rate += val;
            }
        }
        SP_IGNORE_DEF_ELE => {
            if !chk(chk_element(val), "pc_bonus", SP_IGNORE_DEF_ELE, "Element", val) {
                return;
            }
            if lr == LR_FLAG_NONE {
                sd.right_weapon.ignore_def_ele |= 1 << val;
            } else if lr == LR_FLAG_WEAPON {
                sd.left_weapon.ignore_def_ele |= 1 << val;
            }
        }
        SP_IGNORE_DEF_RACE => {
            if !chk(chk_race(val), "pc_bonus", SP_IGNORE_DEF_RACE, "Race", val) {
                return;
            }
            if lr == LR_FLAG_NONE {
                sd.right_weapon.ignore_def_race |= 1 << val;
            } else if lr == LR_FLAG_WEAPON {
                sd.left_weapon.ignore_def_race |= 1 << val;
            }
        }
        SP_IGNORE_DEF_CLASS => {
            if !chk(chk_class(val), "pc_bonus", SP_IGNORE_DEF_CLASS, "Class", val) {
                return;
            }
            if lr == LR_FLAG_NONE {
                sd.right_weapon.ignore_def_class |= 1 << val;
            } else if lr == LR_FLAG_WEAPON {
                sd.left_weapon.ignore_def_class |= 1 << val;
            }
        }
        SP_ATK_RATE => {
            // RENEWAL
            if not_arrow {
                sd.bonus.atk_rate += val;
            }
        }
        SP_MAGIC_ATK_DEF => {
            if not_arrow {
                sd.bonus.magic_def_rate += val;
            }
        }
        SP_MISC_ATK_DEF => {
            if not_arrow {
                sd.bonus.misc_def_rate += val;
            }
        }
        SP_IGNORE_MDEF_ELE => {
            if !chk(chk_element(val), "pc_bonus", SP_IGNORE_MDEF_ELE, "Element", val) {
                return;
            }
            if not_arrow {
                sd.bonus.ignore_mdef_ele |= 1 << val;
            }
        }
        SP_IGNORE_MDEF_RACE => {
            if !chk(chk_race(val), "pc_bonus", SP_IGNORE_MDEF_RACE, "Race", val) {
                return;
            }
            if not_arrow {
                sd.bonus.ignore_mdef_race |= 1 << val;
            }
        }
        SP_PERFECT_HIT_RATE => {
            if not_arrow && sd.bonus.perfect_hit < val {
                sd.bonus.perfect_hit = val;
            }
        }
        SP_PERFECT_HIT_ADD_RATE => {
            if not_arrow {
                sd.bonus.perfect_hit_add += val;
            }
        }
        SP_CRITICAL_RATE => {
            if not_arrow {
                sd.rates.critical_rate += val;
            }
        }
        SP_DEF_RATIO_ATK_ELE => {
            if !chk(chk_element(val), "pc_bonus", SP_DEF_RATIO_ATK_ELE, "Element", val) {
                return;
            }
            if lr == LR_FLAG_NONE {
                sd.right_weapon.def_ratio_atk_ele |= 1 << val;
            } else if lr == LR_FLAG_WEAPON {
                sd.left_weapon.def_ratio_atk_ele |= 1 << val;
            }
        }
        SP_DEF_RATIO_ATK_RACE => {
            if !chk(chk_race(val), "pc_bonus", SP_DEF_RATIO_ATK_RACE, "Race", val) {
                return;
            }
            if lr == LR_FLAG_NONE {
                sd.right_weapon.def_ratio_atk_race |= 1 << val;
            } else if lr == LR_FLAG_WEAPON {
                sd.left_weapon.def_ratio_atk_race |= 1 << val;
            }
        }
        SP_DEF_RATIO_ATK_CLASS => {
            if !chk(chk_class(val), "pc_bonus", SP_DEF_RATIO_ATK_CLASS, "Class", val) {
                return;
            }
            if lr == LR_FLAG_NONE {
                sd.right_weapon.def_ratio_atk_class |= 1 << val;
            } else if lr == LR_FLAG_WEAPON {
                sd.left_weapon.def_ratio_atk_class |= 1 << val;
            }
        }
        SP_HIT_RATE => {
            if not_arrow {
                sd.rates.hit_rate += val;
            }
        }
        SP_FLEE_RATE => {
            if not_arrow {
                sd.rates.flee_rate += val;
            }
        }
        SP_FLEE2_RATE => {
            if not_arrow {
                sd.rates.flee2_rate += val;
            }
        }
        SP_DEF_RATE => {
            if not_arrow {
                sd.rates.def_rate += val;
            }
        }
        SP_DEF2_RATE => {
            if not_arrow {
                sd.rates.def2_rate += val;
            }
        }
        SP_MDEF_RATE => {
            if not_arrow {
                sd.rates.mdef_rate += val;
            }
        }
        SP_MDEF2_RATE => {
            if not_arrow {
                sd.rates.mdef2_rate += val;
            }
        }
        SP_PATK_RATE => {
            if not_arrow {
                sd.rates.patk_rate += val;
            }
        }
        SP_SMATK_RATE => {
            if not_arrow {
                sd.rates.smatk_rate += val;
            }
        }
        SP_RES_RATE => {
            if not_arrow {
                sd.rates.res_rate += val;
            }
        }
        SP_MRES_RATE => {
            if not_arrow {
                sd.rates.mres_rate += val;
            }
        }
        SP_HPLUS_RATE => {
            if not_arrow {
                sd.rates.hplus_rate += val;
            }
        }
        SP_CRATE_RATE => {
            if not_arrow {
                sd.rates.crate_rate += val;
            }
        }
        SP_RESTART_FULL_RECOVER => {
            if not_arrow {
                sd.special_state.restart_full_recover = true;
            }
        }
        SP_NO_CASTCANCEL => {
            if not_arrow {
                sd.special_state.no_castcancel = true;
            }
        }
        SP_NO_CASTCANCEL2 => {
            if not_arrow {
                sd.special_state.no_castcancel2 = true;
            }
        }
        SP_NO_SIZEFIX => {
            if not_arrow {
                sd.special_state.no_sizefix = true;
            }
        }
        SP_NO_MAGIC_DAMAGE => {
            if lr == LR_FLAG_ARROW {
                return;
            }
            val += sd.special_state.no_magic_damage as i32;
            sd.special_state.no_magic_damage = cap_value(val, 0, 100) as u8;
        }
        SP_NO_WEAPON_DAMAGE => {
            if lr == LR_FLAG_ARROW {
                return;
            }
            val += sd.special_state.no_weapon_damage as i32;
            sd.special_state.no_weapon_damage = cap_value(val, 0, 100) as u8;
        }
        SP_NO_MISC_DAMAGE => {
            if lr == LR_FLAG_ARROW {
                return;
            }
            val += sd.special_state.no_misc_damage as i32;
            sd.special_state.no_misc_damage = cap_value(val, 0, 100) as u8;
        }
        SP_NO_GEMSTONE => {
            if not_arrow && sd.special_state.no_gemstone != 2 {
                sd.special_state.no_gemstone = 1;
            }
        }
        SP_INTRAVISION => {
            // Maya Purple Card effect allowing to see Hiding/Cloaking people [DracoRPG]
            if not_arrow {
                sd.special_state.intravision = true;
                if let Some(u) = crate::unit::get(c, sd.account_id) {
                    super::clif::status_change(c, &u, EFST_CLAIRVOYANCE, 1, 0, 0, 0, 0);
                }
            }
        }
        SP_NO_KNOCKBACK => {
            if not_arrow {
                sd.special_state.no_knockback = true;
            }
        }
        SP_SPLASH_RANGE => {
            if (sd.bonus.splash_range as i32) < val {
                sd.bonus.splash_range = val as i16;
            }
        }
        SP_SPLASH_ADD_RANGE => {
            sd.bonus.splash_add_range = (sd.bonus.splash_add_range as i32 + val) as i16;
        }
        SP_SHORT_WEAPON_DAMAGE_RETURN => {
            if not_arrow {
                sd.bonus.short_weapon_damage_return += val;
            }
        }
        SP_LONG_WEAPON_DAMAGE_RETURN => {
            if not_arrow {
                sd.bonus.long_weapon_damage_return += val;
            }
        }
        SP_MAGIC_DAMAGE_RETURN => {
            // AppleGirl Was Here
            if not_arrow {
                sd.bonus.magic_damage_return += val;
            }
        }
        SP_REDUCE_DAMAGE_RETURN => {
            if not_arrow {
                sd.bonus.reduce_damage_return += val;
            }
        }
        SP_ALL_STATS => {
            // [Valaris]
            if not_arrow {
                for p in PARAM_STR..=PARAM_LUK {
                    sd.indexed_bonus.param_bonus[p] += val;
                }
            }
        }
        SP_ALL_TRAIT_STATS => {
            if not_arrow {
                for p in PARAM_POW..=PARAM_CRT {
                    sd.indexed_bonus.param_bonus[p] += val;
                }
            }
        }
        SP_AGI_VIT => {
            // [Valaris]
            if not_arrow {
                sd.indexed_bonus.param_bonus[PARAM_AGI] += val;
                sd.indexed_bonus.param_bonus[PARAM_VIT] += val;
            }
        }
        SP_AGI_DEX_STR => {
            // [Valaris]
            if not_arrow {
                sd.indexed_bonus.param_bonus[PARAM_AGI] += val;
                sd.indexed_bonus.param_bonus[PARAM_DEX] += val;
                sd.indexed_bonus.param_bonus[PARAM_STR] += val;
            }
        }
        SP_PERFECT_HIDE => {
            // [Valaris]
            if not_arrow {
                sd.special_state.perfect_hiding = true;
            }
        }
        SP_UNBREAKABLE => {
            if not_arrow {
                sd.bonus.unbreakable = (sd.bonus.unbreakable as i32 + val) as u16;
            }
        }
        SP_UNBREAKABLE_WEAPON => {
            if not_arrow {
                sd.bonus.unbreakable_equip |= EQP_WEAPON as u16;
            }
        }
        SP_UNBREAKABLE_ARMOR => {
            if not_arrow {
                sd.bonus.unbreakable_equip |= EQP_ARMOR as u16;
            }
        }
        SP_UNBREAKABLE_HELM => {
            if not_arrow {
                sd.bonus.unbreakable_equip |= EQP_HELM as u16;
            }
        }
        SP_UNBREAKABLE_SHIELD => {
            if not_arrow {
                sd.bonus.unbreakable_equip |= EQP_SHIELD as u16;
            }
        }
        SP_UNBREAKABLE_GARMENT => {
            if not_arrow {
                sd.bonus.unbreakable_equip |= EQP_GARMENT as u16;
            }
        }
        SP_UNBREAKABLE_SHOES => {
            if not_arrow {
                sd.bonus.unbreakable_equip |= EQP_SHOES as u16;
            }
        }
        SP_CLASSCHANGE => {
            // [Valaris]
            if not_arrow {
                sd.bonus.classchange = val;
            }
        }
        SP_SKILL_RATIO => {
            if not_arrow {
                sd.bonus.skill_ratio += val;
            }
        }
        SP_SHORT_ATK_RATE => {
            if not_arrow {
                sd.bonus.short_attack_atk_rate += val;
            }
        }
        SP_LONG_ATK_RATE => {
            if not_arrow {
                sd.bonus.long_attack_atk_rate += val;
            }
        }
        SP_NON_CRIT_ATK_RATE => {
            if not_arrow {
                sd.bonus.non_crit_atk_rate += val;
            }
        }
        SP_BREAK_WEAPON_RATE => {
            if not_arrow {
                sd.bonus.break_weapon_rate += val;
            }
        }
        SP_BREAK_ARMOR_RATE => {
            if not_arrow {
                sd.bonus.break_armor_rate += val;
            }
        }
        SP_ADD_STEAL_RATE => {
            if not_arrow {
                sd.bonus.add_steal_rate = (sd.bonus.add_steal_rate as i32 + val) as i16;
            }
        }
        SP_DELAYRATE => {
            if not_arrow {
                sd.bonus.delayrate -= val;
            }
        }
        SP_CRIT_ATK_RATE => {
            if not_arrow {
                sd.bonus.crit_atk_rate += val;
            }
        }
        SP_CRIT_DEF_RATE => {
            if not_arrow {
                sd.bonus.crit_def_rate += val;
            }
        }
        SP_NO_REGEN => {
            // regen.state.block : 2
            if not_arrow {
                sd.regen.block = (sd.regen.block | val as u8) & 3;
            }
        }
        SP_UNSTRIPABLE_WEAPON => {
            if not_arrow {
                sd.bonus.unstripable_equip |= EQP_WEAPON as u16;
            }
        }
        SP_UNSTRIPABLE | SP_UNSTRIPABLE_ARMOR => {
            if not_arrow {
                sd.bonus.unstripable_equip |= EQP_ARMOR as u16;
            }
        }
        SP_UNSTRIPABLE_HELM => {
            if not_arrow {
                sd.bonus.unstripable_equip |= EQP_HELM as u16;
            }
        }
        SP_UNSTRIPABLE_SHIELD => {
            if not_arrow {
                sd.bonus.unstripable_equip |= EQP_SHIELD as u16;
            }
        }
        SP_HP_DRAIN_VALUE => {
            // bonus bHPDrainValue,n;
            let wd = if lr == LR_FLAG_NONE { Some(&mut sd.right_weapon) } else if lr == LR_FLAG_WEAPON { Some(&mut sd.left_weapon) } else { None };
            if let Some(wd) = wd {
                wd.hp_drain_class[CLASS_NORMAL as usize] = (wd.hp_drain_class[CLASS_NORMAL as usize] as i32 + val) as i16;
                wd.hp_drain_class[CLASS_BOSS as usize] = (wd.hp_drain_class[CLASS_BOSS as usize] as i32 + val) as i16;
            }
        }
        SP_SP_DRAIN_VALUE => {
            // bonus bSPDrainValue,n;
            let wd = if lr == LR_FLAG_NONE { Some(&mut sd.right_weapon) } else if lr == LR_FLAG_WEAPON { Some(&mut sd.left_weapon) } else { None };
            if let Some(wd) = wd {
                wd.sp_drain_class[CLASS_NORMAL as usize] = (wd.sp_drain_class[CLASS_NORMAL as usize] as i32 + val) as i16;
                wd.sp_drain_class[CLASS_BOSS as usize] = (wd.sp_drain_class[CLASS_BOSS as usize] as i32 + val) as i16;
            }
        }
        SP_SP_GAIN_VALUE => {
            if lr == LR_FLAG_NONE {
                sd.bonus.sp_gain_value += val;
            }
        }
        SP_HP_GAIN_VALUE => {
            if lr == LR_FLAG_NONE {
                sd.bonus.hp_gain_value += val;
            }
        }
        SP_LONG_SP_GAIN_VALUE => {
            if lr == LR_FLAG_NONE {
                sd.bonus.long_sp_gain_value += val;
            }
        }
        SP_LONG_HP_GAIN_VALUE => {
            if lr == LR_FLAG_NONE {
                sd.bonus.long_hp_gain_value += val;
            }
        }
        SP_MAGIC_SP_GAIN_VALUE => {
            if lr == LR_FLAG_NONE {
                sd.bonus.magic_sp_gain_value += val;
            }
        }
        SP_MAGIC_HP_GAIN_VALUE => {
            if lr == LR_FLAG_NONE {
                sd.bonus.magic_hp_gain_value += val;
            }
        }
        SP_ADD_HEAL_RATE => {
            if not_arrow {
                sd.bonus.add_heal_rate += val;
            }
        }
        SP_ADD_HEAL2_RATE => {
            if not_arrow {
                sd.bonus.add_heal2_rate += val;
            }
        }
        SP_ADD_ITEM_HEAL_RATE => {
            if not_arrow {
                sd.bonus.itemhealrate2 += val;
            }
        }
        SP_EMATK => {
            if not_arrow {
                sd.bonus.ematk += val;
            }
        }
        SP_EMATK_HIDDEN => {
            if not_arrow {
                sd.bonus.ematk_hidden += val;
            }
        }
        SP_ADD_VARIABLECAST => {
            if not_arrow {
                sd.bonus.add_varcast += val;
            }
        }
        // RENEWAL_CAST
        SP_FIXCASTRATE => {
            if not_arrow {
                sd.bonus.fixcastrate = sd.bonus.fixcastrate.min(val);
            }
        }
        SP_ADD_FIXEDCAST => {
            if not_arrow {
                sd.bonus.add_fixcast += val;
            }
        }
        SP_CASTRATE | SP_VARCASTRATE => {
            if not_arrow {
                sd.bonus.varcastrate -= val;
            }
        }
        SP_ADDMAXWEIGHT => {
            if not_arrow {
                sd.add_max_weight = (sd.add_max_weight as i64 + val as i64) as u32;
            }
        }
        SP_ABSORB_DMG_MAXHP => {
            // bonus bAbsorbDmgMaxHP,n;
            sd.bonus.absorb_dmg_maxhp = (sd.bonus.absorb_dmg_maxhp as i32).max(val) as u8;
        }
        SP_ABSORB_DMG_MAXHP2 => {
            sd.bonus.absorb_dmg_maxhp2 = (sd.bonus.absorb_dmg_maxhp2 as i32).max(val) as u8;
        }
        SP_CRITICAL_RANGEATK => {
            // bonus bCriticalLong,n;
            if not_arrow {
                sd.bonus.critical_rangeatk = (sd.bonus.critical_rangeatk as i32 + val * 10) as i16;
            } else {
                sd.bonus.arrow_cri += val * 10;
            }
        }
        SP_WEAPON_ATK_RATE => {
            if not_arrow {
                sd.bonus.weapon_atk_rate = (sd.bonus.weapon_atk_rate as i32 + val) as i16;
            }
        }
        SP_WEAPON_MATK_RATE => {
            if not_arrow {
                sd.bonus.weapon_matk_rate = (sd.bonus.weapon_matk_rate as i32 + val) as i16;
            }
        }
        SP_NO_MADO_FUEL => {
            if not_arrow {
                sd.special_state.no_mado_fuel = true;
            }
        }
        SP_NO_WALK_DELAY => {
            if not_arrow {
                sd.special_state.no_walk_delay = true;
            }
        }
        SP_ADD_ITEM_SPHEAL_RATE => {
            if not_arrow {
                sd.bonus.itemsphealrate2 += val;
            }
        }
        _ => warn_unknown(c, sd, "pc_bonus", &format!("{type_} {val}")),
    }
}

// ---------------------------------------------------------------------------------------------- pc_bonus2

/// pc.cpp pc_bonus2: `bonus2 bBonusName,type2,val;`.
pub fn pc_bonus2(c: &Ctx, sd: &mut PcStatus, type_: i32, type2: i32, val: i32) {
    let lr = sd.lr_flag;
    let not_arrow = lr != LR_FLAG_ARROW;
    const F: &str = "pc_bonus2";
    match type_ {
        SP_ADDELE => {
            // bonus2 bAddEle,e,x;
            if !chk(chk_element(type2), F, SP_ADDELE, "Element", type2) {
                return;
            }
            if lr == LR_FLAG_NONE || lr == LR_FLAG_SHIELD {
                add_at(&mut sd.right_weapon.addele, type2, val);
            } else if lr == LR_FLAG_WEAPON {
                add_at(&mut sd.left_weapon.addele, type2, val);
            } else if lr == LR_FLAG_ARROW {
                add_at(&mut sd.indexed_bonus.arrow_addele, type2, val);
            }
        }
        SP_ADDRACE => {
            // bonus2 bAddRace,r,x;
            if !chk(chk_race(type2), F, SP_ADDRACE, "Race", type2) {
                return;
            }
            if lr == LR_FLAG_NONE || lr == LR_FLAG_SHIELD {
                add_at(&mut sd.right_weapon.addrace, type2, val);
            } else if lr == LR_FLAG_WEAPON {
                add_at(&mut sd.left_weapon.addrace, type2, val);
            } else if lr == LR_FLAG_ARROW {
                add_at(&mut sd.indexed_bonus.arrow_addrace, type2, val);
            }
        }
        SP_ADDCLASS => {
            // bonus2 bAddClass,c,x;
            if !chk(chk_class(type2), F, SP_ADDCLASS, "Class", type2) {
                return;
            }
            if lr == LR_FLAG_NONE || lr == LR_FLAG_SHIELD {
                add_at(&mut sd.right_weapon.addclass, type2, val);
            } else if lr == LR_FLAG_WEAPON {
                add_at(&mut sd.left_weapon.addclass, type2, val);
            } else if lr == LR_FLAG_ARROW {
                add_at(&mut sd.indexed_bonus.arrow_addclass, type2, val);
            }
        }
        SP_ADDSIZE => {
            // bonus2 bAddSize,s,x;
            if !chk(chk_mobsize(type2), F, SP_ADDSIZE, "Size", type2) {
                return;
            }
            if lr == LR_FLAG_NONE || lr == LR_FLAG_SHIELD {
                add_at(&mut sd.right_weapon.addsize, type2, val);
            } else if lr == LR_FLAG_WEAPON {
                add_at(&mut sd.left_weapon.addsize, type2, val);
            } else if lr == LR_FLAG_ARROW {
                add_at(&mut sd.indexed_bonus.arrow_addsize, type2, val);
            }
        }
        SP_SUBELE => {
            // bonus2 bSubEle,e,x;
            if !chk(chk_element(type2), F, SP_SUBELE, "Element", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.subele_script, type2, val);
            }
        }
        SP_SUBRACE => {
            // bonus2 bSubRace,r,x;
            if !chk(chk_race(type2), F, SP_SUBRACE, "Race", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.subrace, type2, val);
            }
        }
        SP_SUBCLASS => {
            // bonus2 bSubClass,c,x;
            if !chk(chk_class(type2), F, SP_SUBCLASS, "Class", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.subclass, type2, val);
            }
        }
        SP_ADDEFF => {
            // bonus2 bAddEff,eff,n;
            if !chk(chk_sc(type2), F, SP_ADDEFF, "Effect", type2) {
                return;
            }
            pc_bonus_addeff(&mut sd.lists.addeff, type2, if not_arrow { val } else { 0 }, if !not_arrow { val as i16 } else { 0 }, 0, 0);
        }
        SP_ADDEFF2 => {
            // bonus2 bAddEff2,eff,n;
            if !chk(chk_sc(type2), F, SP_ADDEFF2, "Effect", type2) {
                return;
            }
            pc_bonus_addeff(&mut sd.lists.addeff, type2, if not_arrow { val } else { 0 }, if !not_arrow { val as i16 } else { 0 }, ATF_SELF as u8, 0);
        }
        SP_RESEFF => {
            // bonus2 bResEff,eff,n;
            if lr == LR_FLAG_ARROW {
                return;
            }
            if sd.lists.reseff.len() == MAX_PC_BONUS {
                log::warn!("pc_bonus2: SP_RESEFF: Reached max ({MAX_PC_BONUS}) number of resistance bonuses per character!");
                return;
            }
            pc_bonus_itembonus(&mut sd.lists.reseff, type2 as u16, val, true);
        }
        SP_MAGIC_ADDELE => {
            // bonus2 bMagicAddEle,e,x;
            if !chk(chk_element(type2), F, SP_MAGIC_ADDELE, "Element", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.magic_addele_script, type2, val);
            }
        }
        SP_MAGIC_ADDRACE => {
            // bonus2 bMagicAddRace,r,x;
            if !chk(chk_race(type2), F, SP_MAGIC_ADDRACE, "Race", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.magic_addrace, type2, val);
            }
        }
        SP_MAGIC_ADDCLASS => {
            // bonus2 bMagicAddClass,c,x;
            if !chk(chk_class(type2), F, SP_MAGIC_ADDCLASS, "Class", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.magic_addclass, type2, val);
            }
        }
        SP_MAGIC_ADDSIZE => {
            // bonus2 bMagicAddSize,s,x;
            if !chk(chk_mobsize(type2), F, SP_MAGIC_ADDSIZE, "Size", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.magic_addsize, type2, val);
            }
        }
        SP_MAGIC_ATK_ELE => {
            // bonus2 bMagicAtkEle,e,x;
            if !chk(chk_element(type2), F, SP_MAGIC_ATK_ELE, "Element", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.magic_atk_ele, type2, val);
            }
        }
        SP_ADD_DAMAGE_CLASS => {
            // bonus2 bAddDamageClass,mid,x;
            let wd = if lr != LR_FLAG_NONE { &mut sd.left_weapon } else { &mut sd.right_weapon };
            if wd.add_dmg.len() == MAX_PC_BONUS {
                log::warn!("pc_bonus2: SP_ADD_DAMAGE_CLASS: Reached max ({MAX_PC_BONUS}) number of add class damage bonuses per character!");
                return;
            }
            pc_bonus_itembonus(&mut wd.add_dmg, type2 as u16, val, false);
        }
        SP_ADD_MAGIC_DAMAGE_CLASS => {
            // bonus2 bAddMagicDamageClass,mid,x;
            if lr == LR_FLAG_ARROW {
                return;
            }
            if sd.lists.add_mdmg.len() == MAX_PC_BONUS {
                log::warn!("pc_bonus2: SP_ADD_MAGIC_DAMAGE_CLASS: Reached max ({MAX_PC_BONUS}) number of add class magic dmg bonuses per character!");
                return;
            }
            pc_bonus_itembonus(&mut sd.lists.add_mdmg, type2 as u16, val, false);
        }
        SP_ADD_DEF_MONSTER => {
            // bonus2 bAddDefMonster,mid,x;
            if lr == LR_FLAG_ARROW {
                return;
            }
            if sd.lists.add_def.len() == MAX_PC_BONUS {
                log::warn!("pc_bonus2: SP_ADD_DEF_MONSTER: Reached max ({MAX_PC_BONUS}) number of add class def bonuses per character!");
                return;
            }
            pc_bonus_itembonus(&mut sd.lists.add_def, type2 as u16, val, false);
        }
        SP_ADD_MDEF_MONSTER => {
            // bonus2 bAddMDefMonster,mid,x;
            if lr == LR_FLAG_ARROW {
                return;
            }
            if sd.lists.add_mdef.len() == MAX_PC_BONUS {
                log::warn!("pc_bonus2: SP_ADD_MDEF_MONSTER: Reached max ({MAX_PC_BONUS}) number of add class mdef bonuses per character!");
                return;
            }
            pc_bonus_itembonus(&mut sd.lists.add_mdef, type2 as u16, val, false);
        }
        SP_HP_DRAIN_RATE => {
            // bonus2 bHPDrainRate,x,n;
            let wd = if lr == LR_FLAG_NONE { Some(&mut sd.right_weapon) } else if lr == LR_FLAG_WEAPON { Some(&mut sd.left_weapon) } else { None };
            if let Some(wd) = wd {
                wd.hp_drain_rate.rate = (wd.hp_drain_rate.rate as i32 + type2) as i16;
                wd.hp_drain_rate.per = (wd.hp_drain_rate.per as i32 + val) as i16;
            }
        }
        SP_SP_DRAIN_RATE => {
            // bonus2 bSPDrainRate,x,n;
            let wd = if lr == LR_FLAG_NONE { Some(&mut sd.right_weapon) } else if lr == LR_FLAG_WEAPON { Some(&mut sd.left_weapon) } else { None };
            if let Some(wd) = wd {
                wd.sp_drain_rate.rate = (wd.sp_drain_rate.rate as i32 + type2) as i16;
                wd.sp_drain_rate.per = (wd.sp_drain_rate.per as i32 + val) as i16;
            }
        }
        SP_SP_VANISH_RATE => {
            // bonus2 bSPVanishRate,x,n;
            if not_arrow {
                pc_bonus_addvanish(&mut sd.lists.sp_vanish, type2 as i16, val as i16, BF_NORMAL);
            }
        }
        SP_HP_VANISH_RATE => {
            // bonus2 bHPVanishRate,x,n;
            if not_arrow {
                pc_bonus_addvanish(&mut sd.lists.hp_vanish, type2 as i16, val as i16, BF_NORMAL);
            }
        }
        SP_GET_ZENY_NUM => {
            // bonus2 bGetZenyNum,x,n;
            if not_arrow && sd.bonus.get_zeny_rate < val {
                sd.bonus.get_zeny_rate = val;
                sd.bonus.get_zeny_num = type2;
            }
        }
        SP_ADD_GET_ZENY_NUM => {
            // bonus2 bAddGetZenyNum,x,n;
            if not_arrow {
                sd.bonus.get_zeny_rate += val;
                sd.bonus.get_zeny_num += type2;
            }
        }
        SP_WEAPON_COMA_ELE => {
            // bonus2 bWeaponComaEle,e,n;
            if !chk(chk_element(type2), F, SP_WEAPON_COMA_ELE, "Element", type2) {
                return;
            }
            if lr == LR_FLAG_ARROW {
                return;
            }
            add_at(&mut sd.indexed_bonus.weapon_coma_ele, type2, val as i16);
            sd.special_state.bonus_coma = true;
        }
        SP_WEAPON_COMA_RACE => {
            // bonus2 bWeaponComaRace,r,n;
            if !chk(chk_race(type2), F, SP_WEAPON_COMA_RACE, "Race", type2) {
                return;
            }
            if lr == LR_FLAG_ARROW {
                return;
            }
            add_at(&mut sd.indexed_bonus.weapon_coma_race, type2, val as i16);
            sd.special_state.bonus_coma = true;
        }
        SP_WEAPON_COMA_CLASS => {
            // bonus2 bWeaponComaClass,c,n;
            if !chk(chk_class(type2), F, SP_WEAPON_COMA_CLASS, "Class", type2) {
                return;
            }
            if lr == LR_FLAG_ARROW {
                return;
            }
            add_at(&mut sd.indexed_bonus.weapon_coma_class, type2, val as i16);
            sd.special_state.bonus_coma = true;
        }
        SP_WEAPON_ATK => {
            // bonus2 bWeaponAtk,w,n;
            if not_arrow {
                add_at(&mut sd.indexed_bonus.weapon_atk, type2, val);
            }
        }
        SP_WEAPON_DAMAGE_RATE => {
            // bonus2 bWeaponDamageRate,w,n;
            if not_arrow {
                add_at(&mut sd.indexed_bonus.weapon_damage_rate, type2, val);
            }
        }
        SP_CRITICAL_ADDRACE => {
            // bonus2 bCriticalAddRace,r,n;
            if !chk(chk_race(type2), F, SP_CRITICAL_ADDRACE, "Race", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.critaddrace, type2, val * 10);
            }
        }
        SP_ADDEFF_WHENHIT => {
            // bonus2 bAddEffWhenHit,eff,n;
            if !chk(chk_sc(type2), F, SP_ADDEFF_WHENHIT, "Effect", type2) {
                return;
            }
            if not_arrow {
                pc_bonus_addeff(&mut sd.lists.addeff_atked, type2, val, 0, 0, 0);
            }
        }
        SP_SKILL_ATK => itembonus_list(sd, lr, |l| &mut l.skillatk, "SP_SKILL_ATK", type2, val, false, "(+{v}%)"),
        SP_SKILL_HEAL => itembonus_list(sd, lr, |l| &mut l.skillheal, "SP_SKILL_HEAL", type2, val, false, "(+{v}%)"),
        SP_SKILL_HEAL2 => itembonus_list(sd, lr, |l| &mut l.skillheal2, "SP_SKILL_HEAL2", type2, val, false, "(+{v}%)"),
        SP_ADD_SKILL_BLOW => itembonus_list(sd, lr, |l| &mut l.skillblown, "SP_ADD_SKILL_BLOW", type2, val, false, "({v})"),
        SP_HP_LOSS_RATE => regen_list(sd, lr, |l| &mut l.hp_loss, "SP_HP_LOSS_RATE", type2, val),
        SP_HP_REGEN_RATE => regen_list(sd, lr, |l| &mut l.hp_regen, "SP_HP_REGEN_RATE", type2, val),
        SP_REGEN_PERCENT_HP => regen_list(sd, lr, |l| &mut l.percent_hp_regen, "SP_REGEN_PERCENT_HP", type2, val),
        SP_REGEN_PERCENT_SP => regen_list(sd, lr, |l| &mut l.percent_sp_regen, "SP_REGEN_PERCENT_SP", type2, val),
        SP_ADDRACE2 => {
            // bonus2 bAddRace2,mr,x;
            if !chk(chk_race2(type2), F, SP_ADDRACE2, "Race2", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.right_weapon.addrace2, type2, val);
            } else {
                add_at(&mut sd.left_weapon.addrace2, type2, val);
            }
        }
        SP_SUBSIZE => {
            // bonus2 bSubSize,s,x;
            if !chk(chk_mobsize(type2), F, SP_SUBSIZE, "Size", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.subsize, type2, val);
            }
        }
        SP_WEAPON_SUBSIZE => {
            // bonus2 bWeaponSubSize,s,x;
            if !chk(chk_mobsize(type2), F, SP_WEAPON_SUBSIZE, "Size", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.weapon_subsize, type2, val);
            }
        }
        SP_MAGIC_SUBSIZE => {
            // bonus2 bMagicSubSize,s,x;
            if !chk(chk_mobsize(type2), F, SP_MAGIC_SUBSIZE, "Size", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.magic_subsize, type2, val);
            }
        }
        SP_SUBRACE2 => {
            // bonus2 bSubRace2,mr,x;
            if !chk(chk_race2(type2), F, SP_SUBRACE2, "Race2", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.subrace2, type2, val);
            }
        }
        SP_ADD_ITEM_HEAL_RATE => {
            // bonus2 bAddItemHealRate,iid,n;
            if lr == LR_FLAG_ARROW {
                return;
            }
            if itemdb::itemdb_exists(type2 as u32).is_none() {
                log::warn!("pc_bonus2: SP_ADD_ITEM_HEAL_RATE Invalid item with id {type2}");
                return;
            }
            if sd.lists.itemhealrate.len() == MAX_PC_BONUS {
                log::warn!("pc_bonus2: SP_ADD_ITEM_HEAL_RATE: Reached max ({MAX_PC_BONUS}) number of item heal bonuses per character!");
                return;
            }
            pc_bonus_itembonus(&mut sd.lists.itemhealrate, type2 as u16, val, false);
        }
        SP_ADD_ITEMGROUP_HEAL_RATE => {
            // bonus2 bAddItemGroupHealRate,ig,n;
            if lr == LR_FLAG_ARROW {
                return;
            }
            if type2 == 0 || itemdb::group::item_group(type2 as u16).is_none() {
                log::warn!("pc_bonus2: SP_ADD_ITEMGROUP_HEAL_RATE: Invalid item group with id {type2}");
                return;
            }
            if sd.lists.itemgrouphealrate.len() == MAX_PC_BONUS {
                log::warn!("pc_bonus2: SP_ADD_ITEMGROUP_HEAL_RATE: Reached max ({MAX_PC_BONUS}) number of item heal bonuses per character!");
                return;
            }
            pc_bonus_itembonus(&mut sd.lists.itemgrouphealrate, type2 as u16, val, false);
        }
        SP_EXP_ADDRACE => {
            // bonus2 bExpAddRace,r,x;
            if !chk(chk_race(type2), F, SP_EXP_ADDRACE, "Race", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.expaddrace, type2, val);
            }
        }
        SP_EXP_ADDCLASS => {
            // bonus2 bExpAddClass,c,x;
            if !chk(chk_class(type2), F, SP_EXP_ADDCLASS, "Class", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.expaddclass, type2, val);
            }
        }
        SP_SP_GAIN_RACE => {
            // bonus2 bSPGainRace,r,n;
            if !chk(chk_race(type2), F, SP_SP_GAIN_RACE, "Race", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.sp_gain_race, type2, val as i16);
            }
        }
        SP_ADD_MONSTER_DROP_ITEM => {
            // bonus2 bAddMonsterDropItem,iid,n;
            if not_arrow {
                pc_bonus_item_drop(&mut sd.lists.add_drop, type2 as u32, 0, CLASS_ALL, RC_NONE_ as i16, val);
            }
        }
        SP_ADD_MONSTER_DROP_ITEMGROUP => {
            // bonus2 bAddMonsterDropItemGroup,ig,n;
            if not_arrow {
                pc_bonus_item_drop(&mut sd.lists.add_drop, 0, type2 as u16, CLASS_ALL, RC_NONE_ as i16, val);
            }
        }
        SP_SP_LOSS_RATE => regen_list(sd, lr, |l| &mut l.sp_loss, "SP_SP_LOSS_RATE", type2, val),
        SP_SP_REGEN_RATE => regen_list(sd, lr, |l| &mut l.sp_regen, "SP_SP_REGEN_RATE", type2, val),
        SP_HP_DRAIN_VALUE_RACE | SP_SP_DRAIN_VALUE_RACE => {
            // bonus2 bHPDrainValueRace,r,n; / bSPDrainValueRace,r,n;
            if !chk(chk_race(type2), F, type_, "Race", type2) {
                return;
            }
            let wd = if lr == LR_FLAG_NONE { Some(&mut sd.right_weapon) } else if lr == LR_FLAG_WEAPON { Some(&mut sd.left_weapon) } else { None };
            if let Some(wd) = wd {
                let t = if type_ == SP_HP_DRAIN_VALUE_RACE { &mut wd.hp_drain_race } else { &mut wd.sp_drain_race };
                add_at(t, type2, val as i16);
            }
        }
        SP_HP_DRAIN_VALUE_CLASS | SP_SP_DRAIN_VALUE_CLASS => {
            // bonus2 bHPDrainValueClass,c,n; / bSPDrainValueClass,c,n;
            if !chk(chk_class(type2), F, type_, "Class", type2) {
                return;
            }
            let wd = if lr == LR_FLAG_NONE { Some(&mut sd.right_weapon) } else if lr == LR_FLAG_WEAPON { Some(&mut sd.left_weapon) } else { None };
            if let Some(wd) = wd {
                let t = if type_ == SP_HP_DRAIN_VALUE_CLASS { &mut wd.hp_drain_class } else { &mut wd.sp_drain_class };
                add_at(t, type2, val as i16);
            }
        }
        SP_IGNORE_MDEF_RACE_RATE => {
            // bonus2 bIgnoreMdefRaceRate,r,n;
            if !chk(chk_race(type2), F, SP_IGNORE_MDEF_RACE_RATE, "Race", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.ignore_mdef_by_race, type2, val);
            }
        }
        SP_IGNORE_MDEF_CLASS_RATE => {
            // bonus2 bIgnoreMdefClassRate,c,n;
            if !chk(chk_class(type2), F, SP_IGNORE_MDEF_CLASS_RATE, "Class", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.ignore_mdef_by_class, type2, val);
            }
        }
        SP_IGNORE_DEF_RACE_RATE => {
            // bonus2 bIgnoreDefRaceRate,r,n;
            if !chk(chk_race(type2), F, SP_IGNORE_DEF_RACE_RATE, "Race", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.ignore_def_by_race, type2, val);
            }
        }
        SP_SP_IGNORE_RES_RACE_RATE => {
            // bonus2 bIgnoreResRaceRate,r,n;
            if !chk(chk_race(type2), F, SP_SP_IGNORE_RES_RACE_RATE, "Race", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.ignore_res_by_race, type2, val);
            }
        }
        SP_SP_IGNORE_MRES_RACE_RATE => {
            // bonus2 bIgnoreMResRaceRate,r,n;
            if !chk(chk_race(type2), F, SP_SP_IGNORE_MRES_RACE_RATE, "Race", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.ignore_mres_by_race, type2, val);
            }
        }
        SP_IGNORE_DEF_CLASS_RATE => {
            // bonus2 bIgnoreDefClassRate,r,n;
            if !chk(chk_class(type2), F, SP_IGNORE_DEF_CLASS_RATE, "Class", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.ignore_def_by_class, type2, val);
            }
        }
        SP_SKILL_USE_SP_RATE => itembonus_list(sd, lr, |l| &mut l.skillusesprate, "SP_SKILL_USE_SP_RATE", type2, val, true, "(+{v}%)"),
        SP_SKILL_DELAY => itembonus_list(sd, lr, |l| &mut l.skilldelay, "SP_SKILL_DELAY", type2, val, false, "({v})"),
        SP_SKILL_COOLDOWN => itembonus_list(sd, lr, |l| &mut l.skillcooldown, "SP_SKILL_COOLDOWN", type2, val, false, "({v})"),
        SP_SKILL_VARIABLECAST => itembonus_list(sd, lr, |l| &mut l.skillvarcast, "SP_SKILL_VARIABLECAST", type2, val, false, "({v})"),
        // RENEWAL_CAST
        SP_SKILL_FIXEDCAST => itembonus_list(sd, lr, |l| &mut l.skillfixcast, "SP_SKILL_FIXEDCAST", type2, val, false, "({v})"),
        SP_CASTRATE | SP_VARCASTRATE => {
            // bonus2 bCastrate,sk,n; / bVariableCastrate,sk,n; (send inversed value here)
            if lr == LR_FLAG_ARROW {
                return;
            }
            if sd.lists.skillcastrate.len() == MAX_PC_BONUS {
                log::warn!("pc_bonus2: SP_VARCASTRATE: Reached max ({MAX_PC_BONUS}) number of skills per character, bonus skill {type2} ({val}%) lost.");
                return;
            }
            pc_bonus_itembonus(&mut sd.lists.skillcastrate, type2 as u16, val.wrapping_neg(), true);
        }
        SP_FIXCASTRATE => {
            // bonus2 bFixedCastrate,sk,n; (send inversed value here)
            if lr == LR_FLAG_ARROW {
                return;
            }
            if sd.lists.skillfixcastrate.len() == MAX_PC_BONUS {
                log::warn!("pc_bonus2: SP_FIXCASTRATE: Reached max ({MAX_PC_BONUS}) number of skills per character, bonus skill {type2} ({val}%) lost.");
                return;
            }
            pc_bonus_itembonus(&mut sd.lists.skillfixcastrate, type2 as u16, val.wrapping_neg(), true);
        }
        SP_SKILL_USE_SP => itembonus_list(sd, lr, |l| &mut l.skillusesp, "SP_SKILL_USE_SP", type2, val, false, "({v})"),
        SP_SUB_SKILL => {
            // bonus2 bSubSkill,sk,n; (no arrow check)
            if sd.lists.subskill.len() == MAX_PC_BONUS {
                log::warn!("pc_bonus2: SP_SUB_SKILL: Reached max ({MAX_PC_BONUS}) number of skills per character, bonus skill {type2} ({val}) lost.");
                return;
            }
            pc_bonus_itembonus(&mut sd.lists.subskill, type2 as u16, val, false);
        }
        SP_SUBDEF_ELE => {
            // bonus2 bSubDefEle,e,x;
            if !chk(chk_element(type2), F, SP_SUBDEF_ELE, "Element", type2) {
                return;
            }
            add_at(&mut sd.indexed_bonus.subdefele, type2, val);
        }
        SP_COMA_CLASS => {
            // bonus2 bComaClass,c,n;
            if !chk(chk_class(type2), F, SP_COMA_CLASS, "Class", type2) {
                return;
            }
            add_at(&mut sd.indexed_bonus.coma_class, type2, val as i16);
            sd.special_state.bonus_coma = true;
        }
        SP_COMA_RACE => {
            // bonus2 bComaRace,r,n;
            if !chk(chk_race(type2), F, SP_COMA_RACE, "Race", type2) {
                return;
            }
            add_at(&mut sd.indexed_bonus.coma_race, type2, val as i16);
            sd.special_state.bonus_coma = true;
        }
        SP_MAGIC_ADDRACE2 => {
            // bonus2 bMagicAddRace2,mr,n;
            if !chk(chk_race2(type2), F, SP_MAGIC_ADDRACE2, "Race2", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.magic_addrace2, type2, val);
            }
        }
        SP_IGNORE_MDEF_RACE2_RATE => {
            // bonus2 bIgnoreMdefRace2Rate,mr,n;
            if !chk(chk_race2(type2), F, SP_IGNORE_MDEF_RACE2_RATE, "Race2", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.ignore_mdef_by_race2, type2, val);
            }
        }
        SP_DROP_ADDRACE => {
            // bonus2 bDropAddRace,r,x;
            if !chk(chk_race(type2), F, SP_DROP_ADDRACE, "Race", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.dropaddrace, type2, val);
            }
        }
        SP_DROP_ADDCLASS => {
            // bonus2 bDropAddClass,c,x;
            if !chk(chk_class(type2), F, SP_DROP_ADDCLASS, "Class", type2) {
                return;
            }
            if not_arrow {
                add_at(&mut sd.indexed_bonus.dropaddclass, type2, val);
            }
        }
        SP_MAGIC_SUBDEF_ELE => {
            // bonus2 bMagicSubDefEle,e,x;
            if !chk(chk_element(type2), F, SP_MAGIC_SUBDEF_ELE, "Element", type2) {
                return;
            }
            add_at(&mut sd.indexed_bonus.magic_subdefele, type2, val);
        }
        SP_ADD_ITEM_SPHEAL_RATE => {
            // bonus2 bAddItemSPHealRate,iid,n;
            if lr == LR_FLAG_ARROW {
                return;
            }
            if itemdb::itemdb_exists(type2 as u32).is_none() {
                log::warn!("pc_bonus2: SP_ADD_ITEM_SPHEAL_RATE Invalid item with id {type2}");
                return;
            }
            if sd.lists.itemsphealrate.len() == MAX_PC_BONUS {
                log::warn!("pc_bonus2: SP_ADD_ITEM_SPHEAL_RATE: Reached max ({MAX_PC_BONUS}) number of item SP heal bonuses per character!");
                return;
            }
            pc_bonus_itembonus(&mut sd.lists.itemsphealrate, type2 as u16, val, false);
        }
        SP_ADD_ITEMGROUP_SPHEAL_RATE => {
            // bonus2 bAddItemGroupSPHealRate,ig,n;
            if lr == LR_FLAG_ARROW {
                return;
            }
            if type2 == 0 || itemdb::group::item_group(type2 as u16).is_none() {
                log::warn!("pc_bonus2: SP_ADD_ITEMGROUP_SPHEAL_RATE: Invalid item group with id {type2}");
                return;
            }
            if sd.lists.itemgroupsphealrate.len() == MAX_PC_BONUS {
                log::warn!("pc_bonus2: SP_ADD_ITEMGROUP_SPHEAL_RATE: Reached max ({MAX_PC_BONUS}) number of item SP heal bonuses per character!");
                return;
            }
            pc_bonus_itembonus(&mut sd.lists.itemgroupsphealrate, type2 as u16, val, false);
        }
        _ => warn_unknown(c, sd, "pc_bonus2", &format!("{type_} {type2} {val}")),
    }
}

/// The "skill list" cases of pc_bonus2 (arrow check, MAX_PC_BONUS warning, pc_bonus_itembonus).
fn itembonus_list(sd: &mut PcStatus, lr: u8, list: impl FnOnce(&mut data::BonusLists) -> &mut Vec<ItemBonus>, name: &str, type2: i32, val: i32, cap_rate: bool, fmt: &str) {
    if lr == LR_FLAG_ARROW {
        return;
    }
    let l = list(&mut sd.lists);
    if l.len() == MAX_PC_BONUS {
        let v = fmt.replace("{v}", &val.to_string());
        log::warn!("pc_bonus2: {name}: Reached max ({MAX_PC_BONUS}) number of skills per character, bonus skill {type2} {v} lost.");
        return;
    }
    pc_bonus_itembonus(l, type2 as u16, val, cap_rate);
}

/// The regen-list cases of pc_bonus2 (bHPLossRate, bHPRegenRate, bRegenPercentHP … ).
fn regen_list(sd: &mut PcStatus, lr: u8, list: impl FnOnce(&mut data::BonusLists) -> &mut Vec<SRegen>, name: &str, type2: i32, val: i32) {
    if lr == LR_FLAG_ARROW {
        return;
    }
    let l = list(&mut sd.lists);
    if l.len() == MAX_PC_BONUS {
        log::warn!("pc_bonus2: {name}: Reached max ({MAX_PC_BONUS}) number of bonus per character!");
        return;
    }
    pc_bonus_regen(l, type2 as i16, val);
}

/// skill_get_inf / inf2 target rule of bAutoSpell: support or self (non-auto-target) skills pick self.
fn autospell_targets_self(skill_id: u16) -> bool {
    let target = skill_get_inf(skill_id);
    target & INF_SUPPORT_SKILL as i32 != 0 || (target & INF_SELF_SKILL as i32 != 0 && !skill_get_inf2(skill_id, &[INF2_NOTARGETSELF]))
}

// ---------------------------------------------------------------------------------------------- pc_bonus3..5

/// pc.cpp pc_bonus3: `bonus3 bBonusName,type2,type3,val;`.
pub fn pc_bonus3(c: &Ctx, sd: &mut PcStatus, type_: i32, type2: i32, type3: i32, val: i32) {
    let lr = sd.lr_flag;
    let not_arrow = lr != LR_FLAG_ARROW;
    const F: &str = "pc_bonus3";
    let card_id = calc::current_equip_card_id() as u32;
    match type_ {
        SP_ADD_MONSTER_DROP_ITEM => {
            // bonus3 bAddMonsterDropItem,iid,r,n;
            if not_arrow {
                pc_bonus_item_drop(&mut sd.lists.add_drop, type2 as u32, 0, CLASS_NONE, type3 as i16, val);
            }
        }
        SP_ADD_MONSTER_ID_DROP_ITEM => {
            // bonus3 bAddMonsterIdDropItem,iid,mid,n;
            if not_arrow {
                pc_bonus_item_drop(&mut sd.lists.add_drop, type2 as u32, 0, CLASS_NONE, type3.wrapping_neg() as i16, val);
            }
        }
        SP_ADD_CLASS_DROP_ITEM => {
            // bonus3 bAddClassDropItem,iid,c,n;
            if not_arrow {
                pc_bonus_item_drop(&mut sd.lists.add_drop, type2 as u32, 0, type3, RC_NONE_ as i16, val);
            }
        }
        SP_AUTOSPELL => {
            // bonus3 bAutoSpell,sk,y,n;
            if not_arrow {
                let target = autospell_targets_self(type2 as u16);
                pc_bonus_autospell(&mut sd.lists.autospell, type2 as u16, type3 as u16, val as i16, 0, card_id, if target { AUTOSPELL_FORCE_SELF } else { AUTOSPELL_FORCE_TARGET });
            }
        }
        SP_AUTOSPELL_WHENHIT => {
            // bonus3 bAutoSpellWhenHit,sk,y,n;
            if not_arrow {
                let target = autospell_targets_self(type2 as u16);
                pc_bonus_autospell(&mut sd.lists.autospell2, type2 as u16, type3 as u16, val as i16, (BF_NORMAL | BF_SKILL) as i16, card_id, if target { AUTOSPELL_FORCE_SELF } else { AUTOSPELL_FORCE_TARGET });
            }
        }
        SP_ADD_MONSTER_DROP_ITEMGROUP => {
            // bonus3 bAddMonsterDropItemGroup,ig,r,n;
            if not_arrow {
                pc_bonus_item_drop(&mut sd.lists.add_drop, 0, type2 as u16, CLASS_NONE, type3 as i16, val);
            }
        }
        SP_ADD_CLASS_DROP_ITEMGROUP => {
            // bonus3 bAddClassDropItemGroup,ig,c,n;
            if not_arrow {
                pc_bonus_item_drop(&mut sd.lists.add_drop, 0, type2 as u16, type3, RC_NONE_ as i16, val);
            }
        }
        SP_ADDEFF => {
            // bonus3 bAddEff,eff,n,y;
            if !chk(chk_sc(type2), F, SP_ADDEFF, "Effect", type2) {
                return;
            }
            pc_bonus_addeff(&mut sd.lists.addeff, type2, if not_arrow { type3 } else { 0 }, if !not_arrow { type3 as i16 } else { 0 }, val as u8, 0);
        }
        SP_ADDEFF_WHENHIT => {
            // bonus3 bAddEffWhenHit,eff,n,y;
            if !chk(chk_sc(type2), F, SP_ADDEFF_WHENHIT, "Effect", type2) {
                return;
            }
            if not_arrow {
                pc_bonus_addeff(&mut sd.lists.addeff_atked, type2, type3, 0, val as u8, 0);
            }
        }
        SP_ADDEFF_ONSKILL => {
            // bonus3 bAddEffOnSkill,sk,eff,n;
            if !chk(chk_sc(type3), F, SP_ADDEFF_ONSKILL, "Effect", type3) {
                return;
            }
            if not_arrow {
                pc_bonus_addeff_onskill(&mut sd.lists.addeff_onskill, type3, val, type2 as i16, ATF_TARGET as u8, 0);
            }
        }
        SP_ADDELE => {
            // bonus3 bAddEle,e,x,bf;
            if !chk(chk_element(type2), F, SP_ADDELE, "Element", type2) {
                return;
            }
            if not_arrow {
                pc_bonus_addele(sd, type2 as u8, type3 as i16, val as i16);
            }
        }
        SP_SUBELE => {
            // bonus3 bSubEle,e,x,bf;
            if !chk(chk_element(type2), F, SP_SUBELE, "Element", type2) {
                return;
            }
            if not_arrow {
                pc_bonus_subele(sd, type2 as u8, type3 as i16, val as i16);
            }
        }
        SP_SUBRACE => {
            // bonus3 bSubRace,r,x,bf;
            if !chk(chk_race(type2), F, SP_SUBRACE, "Race", type2) {
                return;
            }
            if not_arrow {
                pc_bonus_subrace(sd, type2 as u8, type3 as i16, val as i16);
            }
        }
        SP_SP_VANISH_RACE_RATE | SP_HP_VANISH_RACE_RATE => {
            // bonus3 bSPVanishRaceRate,r,x,n; / bHPVanishRaceRate,r,x,n;
            if !chk(chk_race(type2), F, type_, "Race", type2) {
                return;
            }
            if not_arrow {
                let t = if type_ == SP_SP_VANISH_RACE_RATE { &mut sd.races.sp_vanish_race } else { &mut sd.races.hp_vanish_race };
                if let Some(e) = t.get_mut(type2 as usize) {
                    e.rate = (e.rate as i32 + type3) as i16;
                    e.per = (e.per as i32 + val) as i16;
                }
            }
        }
        SP_SP_VANISH_RATE => {
            // bonus3 bSPVanishRate,x,n,bf;
            if not_arrow {
                pc_bonus_addvanish(&mut sd.lists.sp_vanish, type2 as i16, type3 as i16, val);
            }
        }
        SP_HP_VANISH_RATE => {
            // bonus3 bHPVanishRate,x,n,bf;
            if not_arrow {
                pc_bonus_addvanish(&mut sd.lists.hp_vanish, type2 as i16, type3 as i16, val);
            }
        }
        SP_STATE_NORECOVER_RACE => {
            // bonus3 bStateNoRecoverRace,r,x,t;
            if !chk(chk_race(type2), F, SP_STATE_NORECOVER_RACE, "Race", type2) {
                return;
            }
            if lr == LR_FLAG_ARROW {
                return;
            }
            if let Some(e) = sd.races.norecover_state_race.get_mut(type2 as usize) {
                e.rate = type3;
                e.tick = val;
            }
        }
        _ => warn_unknown(c, sd, "pc_bonus3", &format!("{type_} {type2} {type3} {val}")),
    }
}

/// pc.cpp pc_bonus4: `bonus4 bBonusName,type2,type3,type4,val;`.
pub fn pc_bonus4(c: &Ctx, sd: &mut PcStatus, type_: i32, type2: i32, type3: i32, type4: i32, val: i32) {
    let lr = sd.lr_flag;
    let not_arrow = lr != LR_FLAG_ARROW;
    const F: &str = "pc_bonus4";
    let card_id = calc::current_equip_card_id() as u32;
    match type_ {
        SP_AUTOSPELL => {
            // bonus4 bAutoSpell,sk,y,n,i;
            if not_arrow {
                pc_bonus_autospell(&mut sd.lists.autospell, type2 as u16, type3 as u16, type4 as i16, 0, card_id, (val as u8) & AUTOSPELL_FORCE_ALL);
            }
        }
        SP_AUTOSPELL_WHENHIT => {
            // bonus4 bAutoSpellWhenHit,sk,y,n,i;
            if not_arrow {
                pc_bonus_autospell(&mut sd.lists.autospell2, type2 as u16, type3 as u16, type4 as i16, (BF_NORMAL | BF_SKILL) as i16, card_id, (val as u8) & AUTOSPELL_FORCE_ALL);
            }
        }
        SP_AUTOSPELL_ONSKILL => {
            // bonus4 bAutoSpellOnSkill,sk,x,y,n;
            if not_arrow {
                let target = autospell_targets_self(type3 as u16);
                pc_bonus_autospell_onskill(&mut sd.lists.autospell3, type2 as u16, type3 as u16, type4 as u16, val as i16, card_id, if target { AUTOSPELL_FORCE_TARGET } else { AUTOSPELL_FORCE_SELF });
            }
        }
        SP_ADDEFF => {
            // bonus4 bAddEff,eff,n,y,t;
            if !chk(chk_sc(type2), F, SP_ADDEFF, "Effect", type2) {
                return;
            }
            pc_bonus_addeff(&mut sd.lists.addeff, type2, if not_arrow { type3 } else { 0 }, if !not_arrow { type3 as i16 } else { 0 }, type4 as u8, val as u32);
        }
        SP_ADDEFF_WHENHIT => {
            // bonus4 bAddEffWhenHit,eff,n,y,t;
            if !chk(chk_sc(type2), F, SP_ADDEFF_WHENHIT, "Effect", type2) {
                return;
            }
            if not_arrow {
                pc_bonus_addeff(&mut sd.lists.addeff_atked, type2, type3, 0, type4 as u8, val as u32);
            }
        }
        SP_ADDEFF_ONSKILL => {
            // bonus4 bAddEffOnSkill,sk,eff,n,y;
            if !chk(chk_sc(type3), F, SP_ADDEFF_ONSKILL, "Effect", type3) {
                return;
            }
            if not_arrow {
                pc_bonus_addeff_onskill(&mut sd.lists.addeff_onskill, type3, type4, type2 as i16, val as u8, 0);
            }
        }
        SP_SET_DEF_RACE | SP_SET_MDEF_RACE => {
            // bonus4 bSetDefRace,r,n,t,y; / bSetMDefRace,r,n,t,y;
            if !chk(chk_race(type2), F, type_, "Race", type2) {
                return;
            }
            if lr == LR_FLAG_ARROW {
                return;
            }
            let t = if type_ == SP_SET_DEF_RACE { &mut sd.races.def_set_race } else { &mut sd.races.mdef_set_race };
            if let Some(e) = t.get_mut(type2 as usize) {
                e.rate = type3;
                e.tick = type4;
                e.value = val as i16;
            }
        }
        _ => warn_unknown(c, sd, "pc_bonus4", &format!("{type_} {type2} {type3} {type4} {val}")),
    }
}

/// pc.cpp pc_bonus5: `bonus5 bBonusName,type2,type3,type4,type5,val;`.
pub fn pc_bonus5(c: &Ctx, sd: &mut PcStatus, type_: i32, type2: i32, type3: i32, type4: i32, type5: i32, val: i32) {
    let lr = sd.lr_flag;
    let not_arrow = lr != LR_FLAG_ARROW;
    let card_id = calc::current_equip_card_id() as u32;
    match type_ {
        SP_AUTOSPELL => {
            // bonus5 bAutoSpell,sk,y,n,bf,i;
            if not_arrow {
                pc_bonus_autospell(&mut sd.lists.autospell, type2 as u16, type3 as u16, type4 as i16, type5 as i16, card_id, (val as u8) & AUTOSPELL_FORCE_ALL);
            }
        }
        SP_AUTOSPELL_WHENHIT => {
            // bonus5 bAutoSpellWhenHit,sk,y,n,bf,i;
            if not_arrow {
                pc_bonus_autospell(&mut sd.lists.autospell2, type2 as u16, type3 as u16, type4 as i16, type5 as i16, card_id, (val as u8) & AUTOSPELL_FORCE_ALL);
            }
        }
        SP_AUTOSPELL_ONSKILL => {
            // bonus5 bAutoSpellOnSkill,sk,x,y,n,i;
            if not_arrow {
                pc_bonus_autospell_onskill(&mut sd.lists.autospell3, type2 as u16, type3 as u16, type4 as u16, type5 as i16, card_id, (val as u8) & AUTOSPELL_FORCE_ALL);
            }
        }
        SP_ADDEFF_ONSKILL => {
            // bonus5 bAddEffOnSkill,sk,eff,n,y,t;
            if !chk(chk_sc(type3), "pc_bonus5", SP_ADDEFF_ONSKILL, "Effect", type3) {
                return;
            }
            if not_arrow {
                pc_bonus_addeff_onskill(&mut sd.lists.addeff_onskill, type3, type4, type2 as i16, type5 as u8, val as u32);
            }
        }
        _ => warn_unknown(c, sd, "pc_bonus5", &format!("{type_} {type2} {type3} {type4} {type5} {val}")),
    }
}

// ---------------------------------------------------------------------------------------------- autobonus

fn autobonus_list(sd: &mut PcStatus, which: u8) -> &mut Vec<Autobonus> {
    match which {
        2 => &mut sd.autobonus2,
        3 => &mut sd.autobonus3,
        _ => &mut sd.autobonus,
    }
}

/// pc.cpp pc_deleteautobonus (static, pc_unequipitem_sub): the autobonuses whose positions are all within
/// `position` go (~s_autobonus deletes an active end timer).
pub fn pc_deleteautobonus(c: &Ctx, aid: u32, which: u8, position: u32) {
    let Some(mut sd) = data::sd_get(c, aid) else { return };
    let list = autobonus_list(&mut sd, which);
    let mut keep: Vec<Autobonus> = Vec::new();
    for b in list.drain(..) {
        if (b.pos & position) != b.pos {
            keep.push(b);
            continue;
        }
        if b.active != 0 {
            timer::delete(c, b.active);
        }
    }
    *list = keep;
    data::sd_put(c, &sd);
}

/// pc.cpp pc_addautobonus (which: 1 autobonus, 2 autobonus2, 3 autobonus3).
pub fn pc_addautobonus(sd: &mut PcStatus, which: u8, script: &str, rate: i16, dur: u32, mut flag: u16, other_script: Option<&str>, pos: u32, onskill: bool) -> bool {
    let bonus = autobonus_list(sd, which);
    // Check if the same bonus already exists (compare based on position and bonus script)
    if bonus.iter().any(|b| b.pos == pos && b.bonus_script == script) {
        return false;
    }
    if bonus.len() == MAX_PC_BONUS {
        log::warn!("pc_addautobonus: Reached max ({MAX_PC_BONUS}) number of autobonus per character!");
        return false;
    }
    if !onskill {
        let mut f = flag as i32;
        if f & BF_RANGEMASK == 0 {
            f |= BF_SHORT | BF_LONG; // No range defined? Use both.
        }
        if f & BF_WEAPONMASK == 0 {
            f |= BF_WEAPON; // No attack type defined? Use weapon.
        }
        if f & BF_SKILLMASK == 0 {
            if f & (BF_MAGIC | BF_MISC) != 0 {
                f |= BF_SKILL; // These two would never trigger without BF_SKILL
            }
            if f & BF_WEAPON != 0 {
                f |= BF_NORMAL | BF_SKILL;
            }
        }
        flag = f as u16;
    }
    if !(-10000..=10000).contains(&rate) {
        log::warn!("pc_addautobonus: Item bonus rate {rate} exceeds -10000~10000 range, capping.");
    }
    bonus.push(Autobonus {
        rate: cap_value(rate, -10000, 10000),
        atk_type: flag,
        duration: dur,
        bonus_script: script.to_string(),
        other_script: other_script.map(|s| s.to_string()),
        active: 0,
        pos,
    });
    true
}

/// Every equipped position of the character (pc_delautobonus: `equip_pos_idx |= sd->inventory.u.items_inventory[
/// sd->equip_index[j]].equip` over the worn slots).
fn equip_pos_idx(c: &Ctx, sd: &PcStatus) -> u32 {
    let mut pos = 0;
    for idx in calc::equip_index(c, sd.account_id) {
        if idx >= 0 {
            if let Some(r) = crate::inventory::inv_get(c, sd.account_id, idx as usize) {
                pos |= r.item.equip;
            }
        }
    }
    pos
}

/// pc.cpp pc_delautobonus (which: 1 autobonus, 2 autobonus2, 3 autobonus3). Works on the stored row (the caller has
/// put its copy): active bonuses are re-applied when `restore` and their items are still equipped.
pub fn pc_delautobonus(c: &Ctx, aid: u32, which: u8, restore: bool) {
    let Some(sd) = data::sd_get(c, aid) else { return };
    let list = match which {
        2 => sd.autobonus2.clone(),
        3 => sd.autobonus3.clone(),
        _ => sd.autobonus.clone(),
    };
    let mut keep: Vec<Autobonus> = Vec::new();
    let mut restore = restore;
    for b in list {
        if b.active != 0 && restore && !b.bonus_script.is_empty() {
            // Create a list of all equipped positions to see if all items needed for the autobonus are still present
            let cur = data::sd_get(c, aid).unwrap_or_else(|| sd.clone());
            if equip_pos_idx(c, &cur) & b.pos == b.pos {
                script_run_autobonus(c, aid, &b.bonus_script, b.pos);
            } else {
                // Not all required items equipped anymore
                restore = false;
            }
        }
        if restore {
            keep.push(b);
            continue;
        }
        // ~s_autobonus: the end timer goes with the entry
        if b.active != 0 {
            timer::delete(c, b.active);
        }
    }
    data::with_sd(c, aid, |sd| *autobonus_list(sd, which) = keep);
}

/// script.cpp script_run_autobonus: runs an autobonus script as the item (or combo) at `pos` would.
pub fn script_run_autobonus(c: &Ctx, aid: u32, autobonus: &str, pos: u32) {
    super::scripts::ensure_providers();
    if data::sd_get(c, aid).is_none() {
        return;
    }
    let mut found: Option<i16> = None;
    for idx in calc::equip_index(c, aid) {
        if idx >= 0 && crate::inventory::inv_get(c, aid, idx as usize).map(|r| r.item.equip == pos).unwrap_or(false) {
            found = Some(idx);
            break;
        }
    }
    match found {
        Some(idx) => {
            // Single item autobonus
            calc::set_item_index(idx);
            calc::set_combo_pos(0);
        }
        None => {
            // Combo autobonus
            calc::set_item_index(-1);
            calc::set_combo_pos(pos);
        }
    }
    crate::script::run_ext(c, "autobonus", autobonus, aid, 0);
}

/// pc.cpp pc_exeautobonus: an autobonus triggers (battle, phase 3): its other script runs, its bonus stays for the
/// duration.
pub fn pc_exeautobonus(c: &Ctx, aid: u32, which: u8, index: usize) {
    let Some(sd) = data::sd_get(c, aid) else { return };
    let list = match which {
        2 => &sd.autobonus2,
        3 => &sd.autobonus3,
        _ => &sd.autobonus,
    };
    let Some(b) = list.get(index).cloned() else { return };
    if b.active != 0 {
        timer::delete(c, b.active);
    }
    if let Some(other) = &b.other_script {
        if equip_pos_idx(c, &sd) & b.pos == b.pos {
            script_run_autobonus(c, aid, other, b.pos);
        }
    }
    let tid = timer::add(c, c.tick() + b.duration as Tick, Func::StatusEndAutobonus, aid, which as i64);
    data::with_sd(c, aid, |sd| {
        if let Some(e) = autobonus_list(sd, which).get_mut(index) {
            e.active = tid;
        }
    });
    super::calc_pc(c, aid, SCO_FORCE);
}

/// pc.cpp pc_endautobonus (timer; data = which list).
pub fn pc_endautobonus(c: &Ctx, tid: u64, aid: u32, which: i64) {
    if data::sd_get(c, aid).is_none() {
        return;
    }
    data::with_sd(c, aid, |sd| {
        if let Some(b) = autobonus_list(sd, which as u8).iter_mut().find(|b| b.active == tid) {
            b.active = 0;
        }
    });
    super::calc_pc(c, aid, SCO_FORCE);
}

// ---------------------------------------------------------------------------------------------- bonus_script

/// sd->bonus_script.head in list order: linkdb_insert prepends, so the newest entry comes first.
fn bonus_scripts(c: &Ctx, char_id: u32, online: bool) -> Vec<BonusScript> {
    let mut v: Vec<BonusScript> = c.db().bonus_script().by_char().filter(&char_id).filter(|b| b.online == online).collect();
    v.sort_by(|a, b| b.id.cmp(&a.id));
    v
}

/// pc.cpp pc_bonus_script: starts the timers of new entries and runs every entry's script.
pub fn pc_bonus_script(c: &Ctx, aid: u32) {
    let Some(sd) = data::sd_get(c, aid) else { return };
    let now = c.tick();
    super::scripts::ensure_providers();
    for mut entry in bonus_scripts(c, sd.char_id, true) {
        // Only start timer for new bonus_script
        if entry.tid == 0 {
            if entry.icon != EFST_BLANK {
                // Gives status icon if exist
                if let Some(u) = crate::unit::get(c, aid) {
                    super::clif::status_change(c, &u, entry.icon, 1, entry.tick, 1, 0, 0);
                }
            }
            entry.tick += now;
            entry.tid = timer::add(c, entry.tick, Func::StatusBonusScript, aid, entry.id as i64);
            c.db().bonus_script().id().update(entry.clone());
        }
        crate::script::run_ext(c, "bonus_script", &entry.script, aid, 0);
    }
}

/// Whether a bonus_script text compiles (parse_script(…, "bonus_script", 0, SCRIPT_IGNORE_EXTERNAL_BRACKETS)).
fn bonus_script_parses(c: &Ctx, script_str: &str) -> bool {
    super::scripts::ensure_providers();
    let k = crate::script::vm::CodeKey::Ext(crate::script::vm::ExtCode { kind: "bonus_script".into(), key: script_str.to_string() });
    crate::script::code::get(Some(c), &k).is_some()
}

/// pc.cpp pc_bonus_script_add + the linkdb_insert of its callers (buildin bonus_script, chrif_bsdata_received):
/// true when a new entry was inserted.
pub fn pc_bonus_script_add(c: &Ctx, aid: u32, script_str: &str, dur: Tick, icon: i32, flag: u16, kind: u8) -> bool {
    let Some(sd) = data::sd_get(c, aid) else { return false };
    if !bonus_script_parses(c, script_str) {
        log::error!("pc_bonus_script_add: Failed to parse script '{script_str}' (CID:{}).", sd.char_id);
        return false;
    }
    // Duplication checks
    for entry in bonus_scripts(c, sd.char_id, true) {
        if entry.script.eq_ignore_ascii_case(script_str) {
            let newdur = c.tick() + dur;
            if flag as u32 & BSF_FORCE_REPLACE != 0 && entry.tick < newdur {
                // Change duration (settick_timer)
                if entry.tid != 0 {
                    timer::settick(c, entry.tid, newdur);
                }
                return false;
            } else if flag as u32 & BSF_FORCE_DUPLICATE != 0 {
                // Allow duplicate
                break;
            } else {
                // No duplicate bonus
                return false;
            }
        }
    }
    c.db().bonus_script().insert(BonusScript { id: 0, char_id: sd.char_id, script: script_str.to_string(), tick: dur, flag, icon, kind, tid: 0, online: true });
    true
}

/// pc.cpp pc_bonus_script_free_entry.
fn pc_bonus_script_free_entry(c: &Ctx, aid: u32, entry: &BonusScript) {
    if entry.tid != 0 {
        timer::delete(c, entry.tid);
    }
    c.db().bonus_script().id().delete(entry.id);
    if entry.icon != EFST_BLANK {
        if let Some(u) = crate::unit::get(c, aid) {
            super::clif::status_change(c, &u, entry.icon, 0, 0, 0, 0, 0);
        }
    }
}

/// pc.cpp pc_bonus_script_timer.
pub fn pc_bonus_script_timer(c: &Ctx, tid: u64, aid: u32, entry_id: i64) {
    if data::sd_get(c, aid).is_none() {
        log::error!("pc_bonus_script_timer: Null pointer id: {aid} tid: {tid}");
        return;
    }
    let Some(entry) = c.db().bonus_script().id().find(entry_id as u64) else {
        log::error!("pc_bonus_script_timer: Invalid entry pointer {entry_id}!");
        return;
    };
    pc_bonus_script_free_entry(c, aid, &entry);
    super::calc_pc(c, aid, SCO_NONE);
}

/// pc.cpp pc_bonus_script_clear: removes the entries matching `flag` (BSF_*).
pub fn pc_bonus_script_clear(c: &Ctx, aid: u32, flag: u32) {
    let Some(sd) = data::sd_get(c, aid) else { return };
    let mut count = 0;
    for entry in bonus_scripts(c, sd.char_id, true) {
        let ef = entry.flag as u32;
        if flag == BSF_PERMANENT // Remove all with permanent bonus
            || (flag == 0 && ef & BSF_PERMANENT == 0) // Remove all WITHOUT permanent bonus
            || flag & ef != 0 // Matched flag
            || (flag & BSF_REM_BUFF != 0 && entry.kind == 1) // Remove buff
            || (flag & BSF_REM_DEBUFF != 0 && entry.kind == 2)
        // Remove debuff
        {
            pc_bonus_script_free_entry(c, aid, &entry);
            count += 1;
        }
    }
    if count != 0 && flag & BSF_REM_ON_LOGOUT == 0 {
        // Don't need to do this if log out
        super::calc_pc(c, aid, SCO_NONE);
    }
}

/// chrif.cpp chrif_bsdata_save at logout: entries removed on logout go, the rest are kept with their remaining time
/// (the char-server's `bonus_script` rows; at most MAX_PC_BONUS_SCRIPT).
pub fn bonus_script_save_on_quit(c: &Ctx, aid: u32) {
    let Some(sd) = data::sd_get(c, aid) else { return };
    let entries = bonus_scripts(c, sd.char_id, true);
    if !entries.is_empty() {
        let mut flag = BSF_REM_ON_LOGOUT; // Remove bonus when logout
        if crate::conf::debuff_on_logout() & 1 != 0 {
            flag |= BSF_REM_DEBUFF; // Remove negative buffs
        }
        if crate::conf::debuff_on_logout() & 2 != 0 {
            flag |= BSF_REM_BUFF; // Remove positive buffs
        }
        pc_bonus_script_clear(c, aid, flag);
    }
    let tick = c.tick();
    let mut saved = 0usize;
    for mut entry in bonus_scripts(c, sd.char_id, true) {
        let due = if entry.tid != 0 { timer::due(c, entry.tid) } else { None };
        if entry.tid != 0 {
            timer::delete(c, entry.tid);
        }
        match due {
            Some(due) if due - tick >= 0 && saved < MAX_PC_BONUS_SCRIPT => {
                entry.tick = due - tick;
                entry.tid = 0;
                entry.online = false;
                c.db().bonus_script().id().update(entry);
                saved += 1;
            }
            _ => {
                c.db().bonus_script().id().delete(entry.id);
            }
        }
    }
}

/// chrif.cpp chrif_bsdata_received at login: the saved entries come back (pc_bonus_script_add each) and the
/// character is recalculated when any is there.
pub fn bonus_script_load(c: &Ctx, aid: u32) {
    let Some(sd) = data::sd_get(c, aid) else { return };
    let saved = bonus_scripts(c, sd.char_id, false);
    if saved.is_empty() {
        return;
    }
    // (the char-server sends them in its SELECT order: oldest first)
    let mut rows = saved;
    rows.sort_by_key(|b| b.id);
    for bs in &rows {
        c.db().bonus_script().id().delete(bs.id);
    }
    for bs in rows {
        if bs.script.is_empty() || bs.tick == 0 {
            continue;
        }
        pc_bonus_script_add(c, aid, &bs.script, bs.tick, bs.icon, bs.flag, bs.kind);
    }
    if !bonus_scripts(c, sd.char_id, true).is_empty() {
        super::calc_pc(c, aid, SCO_NONE);
    }
}

#[cfg(test)]
mod tests {
    /// pc.cpp:3331: a CLASS_NONE (-1) entry is stored as uint16 65535 and never equals the int32 -1 again, so two such
    /// bonuses keep two entries; a real class merges the rates.
    #[test]
    fn item_drop_class_none_never_merges() {
        let mut drop = Vec::new();
        super::pc_bonus_item_drop(&mut drop, 501, 0, -1, 1002, 100);
        super::pc_bonus_item_drop(&mut drop, 501, 0, -1, 1002, 100);
        assert_eq!(drop.len(), 2);
        let mut drop = Vec::new();
        super::pc_bonus_item_drop(&mut drop, 501, 0, 0, 1002, 100);
        super::pc_bonus_item_drop(&mut drop, 501, 0, 0, 1002, 100);
        assert_eq!(drop.len(), 1);
        assert_eq!(drop[0].rate, 2 * drop_rate(100));
    }

    /// The rate pc_bonus_item_drop stores for an absolute rate (item_rate_adddrop, min / max).
    fn drop_rate(rate: i32) -> i32 {
        let mut d = Vec::new();
        super::pc_bonus_item_drop(&mut d, 501, 0, 0, 1002, rate);
        d[0].rate
    }
}
