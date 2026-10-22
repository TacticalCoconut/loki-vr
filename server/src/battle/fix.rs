//! battle.cpp's damage fixes: battle_attr_fix (element table + element statuses), battle_calc_cardfix (card / item
//! race, size, element, class bonuses of both sides), battle_absorb_damage, battle_status_block_damage,
//! battle_calc_damage (status reductions / blocks on the final damage), battle_can_hit_gvg_target / bg_target,
//! battle_calc_gvg_damage / bg_damage / pk_damage, battle_calc_drain and battle_calc_return_damage.
//!
//! The skill reactions of these functions (Weapon Blocking, Auto Guard, Parrying, Dodge, Utsusemi, Millennium
//! Shield, Lightning Walk, Magma Flow, Stone Hard Skin, Force of Vanguard, Gentle Touch Energy Gain, Soul Reaper,
//! Deathbound, Devotion's reflect inheritance, the Mado Gear's overheat) call the skill pipeline as rAthena does.
//! Skill unit groups (Safety Wall / Steinwand, Maelstrom cells, Wall of Thorn) are the skillunit agent's,
//! status_change_spread the status2 agent's, homunculi homunculus/'s (SC_STYLE_CHANGE's spheres are wired),
//! mercenaries / elementals mercenary/'s, elemental/'s and companion/'s.

use super::bl;
use super::calc::{self, Damage};
use super::*;
use crate::map::{BL_ELEM, BL_MOB, BL_PC};
use crate::status::consts::*;
use crate::status::defs::*;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::Ctx;

/// A skill-side effect that is not in this build (wave C's placeholder; no caller is left since skillcore ported
/// the skill sides: pc_dead's sphere removal now calls crate::skill::state).
pub fn phase4(what: &str) {
    log::warn!("battle: {what} is not in this build");
}

/// map.hpp CHK_ELEMENT / CHK_ELEMENT_LEVEL / CHK_RACE / CHK_CLASS.
pub fn chk_element(e: i32) -> bool {
    e > ELE_NONE && e < ELE_MAX as i32
}
pub fn chk_element_level(lv: i32) -> bool {
    (1..=4).contains(&lv)
}
pub fn chk_race(r: i32) -> bool {
    r > RC_NONE_ as i32 && r < RC_MAX as i32
}
pub fn chk_class(cl: i32) -> bool {
    cl > CLASS_NONE && cl < CLASS_MAX as i32
}

/// e_battle_flag masks: does a bonus's flag cover this attack (weapon/magic/misc, short/long, skill/normal)?
fn flag_covers(bonus_flag: i32, flag: i32) -> bool {
    let f = bonus_flag & flag;
    f & BF_WEAPONMASK != 0 && f & BF_RANGEMASK != 0 && f & BF_SKILLMASK != 0
}

// ---------------------------------------------------------------------------------------------- battle_attr_fix

/// battle.cpp battle_attr_fix (RENEWAL): the element table and the element statuses of both sides. flag&1 allows
/// a negative result even when attribute_recover is off.
pub fn battle_attr_fix(c: &Ctx, src: Option<&Unit>, target: Option<&Unit>, damage: i64, atk_elem: i32, def_type: i32, def_lv: i32, flag: i32) -> i64 {
    let mut damage = damage;
    let mut atk_elem = atk_elem;
    let scl = src.and_then(|s| bl::sc(c, s));
    let tsc = target.and_then(|t| bl::sc(c, t));
    if !chk_element(atk_elem) {
        atk_elem = (c.rnd() % ELE_ALL as u32) as i32;
    }
    if !chk_element(def_type) || !chk_element_level(def_lv) {
        log::error!("battle_attr_fix: unknown attribute type: atk={atk_elem} def_type={def_type} def_lv={def_lv}");
        return damage;
    }
    let mut ratio = crate::db::misc::status::elemental_attribute_db().get_attribute(def_lv as u16, atk_elem as u16, def_type as u16) as i32;
    if let Some(s) = scl.as_ref().filter(|s| !s.empty()) {
        // increase dmg by src status
        match atk_elem {
            ELE_FIRE => {
                if s.has(SC_VOLCANO) {
                    ratio += s.v3(SC_VOLCANO);
                }
            }
            ELE_WIND => {
                if s.has(SC_VIOLENTGALE) {
                    ratio += s.v3(SC_VIOLENTGALE);
                }
            }
            ELE_WATER => {
                if s.has(SC_DELUGE) {
                    ratio += s.v3(SC_DELUGE);
                }
            }
            ELE_GHOST => {
                if s.has(SC_TELEKINESIS_INTENSE) {
                    // damage after elemental_attribute_db should be calculated first to avoid rounding issues
                    damage -= damage * (100 - ratio) as i64 / 100;
                    ratio = 100;
                    ratio += ratio * s.v3(SC_TELEKINESIS_INTENSE) / 100;
                }
            }
            _ => {}
        }
    }
    if let Some(t) = target.filter(|t| t.kind == crate::map::BL_SKILL) {
        // a Wall of Thorn set on fire (skillunit)
        if atk_elem == ELE_FIRE && super::target::battle_getcurrentskill(c, t) == crate::db::skills::GN_WALLOFTHORN && !crate::skillunit::attr_fix_wall_of_thorn(c, t) {
            return 0;
        }
    }
    if let (Some(t), Some(ts)) = (target, tsc.as_ref().filter(|s| !s.empty())) {
        // increase dmg by target status
        match atk_elem {
            ELE_FIRE => {
                if ts.has(SC_SPIDERWEB) {
                    ratio += 100; // Double damage
                    // Remove a unit group or end whole status change
                    sc::status_change_end(c, t.id, SC_SPIDERWEB, 0);
                }
                let cur_skill = src.map(|s| super::target::battle_getcurrentskill(c, s)).unwrap_or(0);
                if ts.has(SC_THORNSTRAP) && cur_skill != crate::db::skills::GN_CARTCANNON {
                    sc::status_change_end(c, t.id, SC_THORNSTRAP, 0);
                }
                if ts.has(SC_CRYSTALIZE) {
                    sc::status_change_end(c, t.id, SC_CRYSTALIZE, 0);
                }
                if ts.has(SC_EARTH_INSIGNIA) {
                    ratio += 50;
                }
                if ts.has(SC_WIDEWEB) {
                    ratio += 100;
                    sc::status_change_end(c, t.id, SC_WIDEWEB, 0);
                }
                if ts.has(SC_BURNT) {
                    ratio += 400;
                }
            }
            ELE_HOLY => {
                if ts.has(SC_ORATIO) {
                    ratio += ts.v1(SC_ORATIO) * 2;
                }
            }
            ELE_POISON => {
                if ts.has(SC_VENOMIMPRESS) {
                    ratio += ts.v2(SC_VENOMIMPRESS);
                }
            }
            ELE_WIND => {
                if ts.has(SC_WATER_INSIGNIA) {
                    ratio += 50;
                }
                if ts.has(SC_CRYSTALIZE) {
                    let skill_id = src.map(|s| super::target::battle_getcurrentskill(c, s)).unwrap_or(0);
                    if crate::db::skills::skill_get_type(skill_id) & BF_MAGIC != 0 {
                        ratio += 50;
                    }
                }
            }
            ELE_WATER => {
                if ts.has(SC_FIRE_INSIGNIA) {
                    ratio += 50;
                }
            }
            ELE_EARTH => {
                if ts.has(SC_WIND_INSIGNIA) {
                    ratio += 50;
                }
                sc::status_change_end(c, t.id, SC_MAGNETICFIELD, 0); // freed if received earth dmg
            }
            ELE_NEUTRAL => {
                if ts.has(SC_ANTI_M_BLAST) {
                    ratio += ts.v2(SC_ANTI_M_BLAST);
                }
            }
            ELE_DARK => {
                if ts.has(SC_SOULCURSE) {
                    if bl::class_type(c, t) == CLASS_BOSS {
                        ratio += 20;
                    } else {
                        ratio += 100;
                    }
                }
            }
            _ => {}
        }
    }
    if !conf::attr_recover() && flag & 1 == 0 && ratio < 0 {
        ratio = 0;
    }
    // In renewal, reductions are always rounded down so damage can never reach 0 unless ratio is 0
    damage - damage * (100 - ratio) as i64 / 100
}

// ---------------------------------------------------------------------------------------------- battle_calc_cardfix

/// battle.cpp battle_calc_cardfix_debuff: statuses that affect the target's element.
fn battle_calc_cardfix_debuff(tsc: &sc::Sc, rh_ele: i32) -> i32 {
    let mut ele_fix = 0;
    if tsc.has(SC_MAGIC_POISON) {
        ele_fix += 50;
    }
    match rh_ele {
        ELE_FIRE => {
            if tsc.has(SC_CLIMAX_BLOOM) {
                ele_fix += 100;
            }
        }
        ELE_EARTH => {
            if tsc.has(SC_CLIMAX_EARTH) {
                ele_fix += 100;
            }
        }
        ELE_WATER => {
            if tsc.has(SC_MISTYFROST) {
                ele_fix += 15;
            }
        }
        ELE_POISON => {
            if tsc.has(SC_CLOUD_POISON) {
                ele_fix += 5 * tsc.v1(SC_CLOUD_POISON);
            }
        }
        _ => {}
    }
    ele_fix
}

/// APPLY_CARDFIX: official servers apply the cardfix on a base of 1000 and round the change down.
fn apply_cardfix(damage: &mut i64, fix: i32) {
    *damage -= *damage * (1000 - fix.max(0)) as i64 / 1000;
}

/// APPLY_CARDFIX_RE (BF_MAGIC in renewal).
fn apply_cardfix_re(damage: &mut i64, fix: i32) {
    *damage -= *damage * (100 - (100 + fix).max(0)) as i64 / 100;
}

/// NK_* flags of an attack (a skill's, or the normal attack's): bit n = NK_n.
pub type Nk = u64;
pub fn nk_has(nk: Nk, bit: u8) -> bool {
    nk & (1u64 << bit) != 0
}

/// battle.cpp battle_calc_cardfix: the change the card / item bonuses make to `damage`. left: 3 attacker bonuses
/// of both hands, 2 attacker right hand only, 0 / 1 target bonuses only.
#[allow(clippy::too_many_arguments)]
pub fn battle_calc_cardfix(c: &Ctx, attack_type: i32, src: &Unit, target: &Unit, nk: Nk, rh_ele: i32, lh_ele: i32, damage: i64, left: i32, flag: i32) -> i32 {
    use crate::db::skills::{NK_IGNOREATKCARD, NK_IGNOREDEFCARD, NK_IGNOREELEMENT, NK_IGNORELONGCARD};
    if damage == 0 {
        return 0;
    }
    let mut damage = damage;
    let original_damage = damage;
    let mut flag = flag;
    let mut cardfix: i32 = 1000;
    let sd = bl::sd(c, src);
    let tsd = bl::sd(c, target);
    let t_class = bl::class_of(c, target);
    let s_class = bl::class_of(c, src);
    let sstatus = bl::status(c, src);
    let tstatus = bl::status(c, target);
    let tsc = bl::sc(c, target);
    let s_race2 = bl::race2_of(c, src);
    let t_race2 = bl::race2_of(c, target);
    let s_defele = if tsd.is_some() { sstatus.def_ele as i32 } else { ELE_NONE };
    // When the attacker is a monster, then all bonuses on BF_WEAPON will work and no bonuses on BF_MAGIC
    if src.kind == BL_MOB && conf::cardfix_monster_physical() {
        flag |= BF_WEAPON;
        flag &= !BF_MAGIC;
    }
    let t_size = tstatus.size as usize;
    let t_race = tstatus.race as usize;
    let t_class_ = tstatus.class_ as usize;
    let t_ele = tstatus.def_ele as usize;
    let s_size = sstatus.size as usize;
    let s_race = sstatus.race as usize;
    let s_class_ = sstatus.class_ as usize;
    let (rall, eall, sall, call) = (RC_ALL as usize, ELE_ALL as usize, SZ_ALL as usize, CLASS_ALL as usize);
    let at = |v: &Vec<i32>, i: usize| v.get(i).copied().unwrap_or(0);
    match attack_type {
        BF_MAGIC => {
            // Affected by attacker ATK bonuses
            if let Some(sd) = sd.as_ref().filter(|_| !nk_has(nk, NK_IGNOREATKCARD)) {
                let ib = &sd.st.indexed_bonus;
                let mut race2_val = 0;
                // On (at least) BF_MAGIC, damages are calculated consecutively and rounded down in the following
                // order to match official damage: size, race2, ele, atk_ele, race, class
                apply_cardfix_re(&mut damage, at(&ib.magic_addsize, t_size) + at(&ib.magic_addsize, sall));
                // race2 is the same as the bonus per class ID
                for &r in &t_race2 {
                    race2_val += at(&ib.magic_addrace2, r as usize);
                }
                for it in &sd.st.lists.add_mdmg {
                    if it.id as i32 == t_class {
                        race2_val += it.val;
                        break;
                    }
                }
                apply_cardfix_re(&mut damage, race2_val);
                if !nk_has(nk, NK_IGNOREELEMENT) {
                    apply_cardfix_re(&mut damage, at(&ib.magic_addele, t_ele) + at(&ib.magic_addele, eall) + at(&ib.magic_addele_script, t_ele) + at(&ib.magic_addele_script, eall));
                }
            }
            // Statuses that affect the target's element and should be calculated right after magic_addele
            if let Some(ts) = &tsc {
                if !nk_has(nk, NK_IGNOREDEFCARD) && !nk_has(nk, NK_IGNOREELEMENT) {
                    apply_cardfix_re(&mut damage, battle_calc_cardfix_debuff(ts, rh_ele));
                }
            }
            if let Some(sd) = sd.as_ref().filter(|_| !nk_has(nk, NK_IGNOREATKCARD)) {
                let ib = &sd.st.indexed_bonus;
                if !nk_has(nk, NK_IGNOREELEMENT) {
                    apply_cardfix_re(&mut damage, at(&ib.magic_atk_ele, rh_ele.max(0) as usize) + at(&ib.magic_atk_ele, eall));
                }
                apply_cardfix_re(&mut damage, at(&ib.magic_addrace, t_race) + at(&ib.magic_addrace, rall));
                apply_cardfix_re(&mut damage, at(&ib.magic_addclass, t_class_) + at(&ib.magic_addclass, call));
            }
            // Affected by target DEF bonuses
            if let Some(tsd) = tsd.as_ref().filter(|_| !nk_has(nk, NK_IGNOREDEFCARD)) {
                let ib = &tsd.st.indexed_bonus;
                cardfix = 1000; // reset var for target
                if !nk_has(nk, NK_IGNOREELEMENT) {
                    let re = rh_ele.max(0) as usize;
                    let mut ele_fix = at(&ib.subele, re) + at(&ib.subele, eall) + at(&ib.subele_script, re) + at(&ib.subele_script, eall);
                    for it in &tsd.st.lists.subele2 {
                        if it.ele as i32 != ELE_ALL && it.ele as i32 != rh_ele {
                            continue;
                        }
                        if !flag_covers(it.flag as i32, flag) {
                            continue;
                        }
                        ele_fix += it.rate as i32;
                    }
                    if s_defele != ELE_NONE {
                        ele_fix += at(&ib.magic_subdefele, s_defele as usize) + at(&ib.magic_subdefele, eall);
                    }
                    cardfix = cardfix * (100 - ele_fix) / 100;
                }
                cardfix = cardfix * (100 - at(&ib.subsize, s_size) - at(&ib.subsize, sall)) / 100;
                cardfix = cardfix * (100 - at(&ib.magic_subsize, s_size) - at(&ib.magic_subsize, sall)) / 100;
                let mut race_fix = 0;
                for &r in &s_race2 {
                    race_fix += at(&ib.subrace2, r as usize);
                }
                cardfix = cardfix * (100 - race_fix) / 100;
                race_fix = at(&ib.subrace, s_race) + at(&ib.subrace, rall);
                for it in &tsd.st.lists.subrace3 {
                    if it.race as i32 != RC_ALL as i32 && it.race as usize != s_race {
                        continue;
                    }
                    if !flag_covers(it.flag as i32, flag) {
                        continue;
                    }
                    race_fix += it.rate as i32;
                }
                cardfix = cardfix * (100 - race_fix) / 100;
                cardfix = cardfix * (100 - at(&ib.subclass, s_class_) - at(&ib.subclass, call)) / 100;
                for it in &tsd.st.lists.add_mdef {
                    if it.id as i32 == s_class {
                        cardfix = cardfix * (100 - it.val) / 100;
                        break;
                    }
                }
                cardfix = cardfix * (100 - tsd.st.bonus.magic_def_rate) / 100;
                if let Some(ts) = &tsc {
                    if ts.has(SC_MDEF_RATE) {
                        cardfix = cardfix * (100 - ts.v1(SC_MDEF_RATE)) / 100;
                    }
                }
                apply_cardfix(&mut damage, cardfix);
            }
        }
        BF_WEAPON => {
            // Affected by attacker ATK bonuses
            if let Some(sd) = sd.as_ref().filter(|_| !nk_has(nk, NK_IGNOREATKCARD) && left & 2 != 0) {
                let rw = &sd.st.right_weapon;
                let lw = &sd.st.left_weapon;
                let ib = &sd.st.indexed_bonus;
                // int16 cardfix_ (the left hand's): the products are stored back into 16 bits
                let mut cardfix_: i16 = 1000;
                let arrow_atk = state::get(c, sd.aid).arrow_atk;
                if arrow_atk {
                    // Ranged attack
                    cardfix = cardfix * (100 + at(&rw.addrace, t_race) + at(&ib.arrow_addrace, t_race) + at(&rw.addrace, rall) + at(&ib.arrow_addrace, rall)) / 100;
                    if !nk_has(nk, NK_IGNOREELEMENT) {
                        let mut ele_fix = at(&rw.addele, t_ele) + at(&ib.arrow_addele, t_ele) + at(&rw.addele, eall) + at(&ib.arrow_addele, eall);
                        for it in &rw.addele2 {
                            if it.ele as i32 != ELE_ALL && it.ele as usize != t_ele {
                                continue;
                            }
                            if !flag_covers(it.flag as i32, flag) {
                                continue;
                            }
                            ele_fix += it.rate as i32;
                        }
                        cardfix = cardfix * (100 + ele_fix) / 100;
                    }
                    cardfix = cardfix * (100 + at(&rw.addsize, t_size) + at(&ib.arrow_addsize, t_size) + at(&rw.addsize, sall) + at(&ib.arrow_addsize, sall)) / 100;
                    let mut race_fix = 0;
                    for &r in &t_race2 {
                        race_fix += at(&rw.addrace2, r as usize);
                    }
                    cardfix = cardfix * (100 + race_fix) / 100;
                    cardfix = cardfix * (100 + at(&rw.addclass, t_class_) + at(&ib.arrow_addclass, t_class_) + at(&rw.addclass, call) + at(&ib.arrow_addclass, call)) / 100;
                } else if !crate::conf::left_cardfix_to_right() {
                    // Melee attack: each right & left hand weapon bonuses separatedly
                    cardfix = cardfix * (100 + at(&rw.addrace, t_race) + at(&rw.addrace, rall)) / 100;
                    if !nk_has(nk, NK_IGNOREELEMENT) {
                        let mut ele_fix = at(&rw.addele, t_ele) + at(&rw.addele, eall);
                        for it in &rw.addele2 {
                            if it.ele as i32 != ELE_ALL && it.ele as usize != t_ele {
                                continue;
                            }
                            if !flag_covers(it.flag as i32, flag) {
                                continue;
                            }
                            ele_fix += it.rate as i32;
                        }
                        cardfix = cardfix * (100 + ele_fix) / 100;
                    }
                    cardfix = cardfix * (100 + at(&rw.addsize, t_size) + at(&rw.addsize, sall)) / 100;
                    for &r in &t_race2 {
                        cardfix = cardfix * (100 + at(&rw.addrace2, r as usize)) / 100;
                    }
                    cardfix = cardfix * (100 + at(&rw.addclass, t_class_) + at(&rw.addclass, call)) / 100;
                    if left & 1 != 0 {
                        // Left-handed weapon
                        cardfix_ = (cardfix_ as i32 * (100 + at(&lw.addrace, t_race) + at(&lw.addrace, rall)) / 100) as i16;
                        if !nk_has(nk, NK_IGNOREELEMENT) {
                            let mut ele_fix_lh = at(&lw.addele, t_ele) + at(&lw.addele, eall);
                            for it in &lw.addele2 {
                                if it.ele as i32 != ELE_ALL && it.ele as usize != t_ele {
                                    continue;
                                }
                                if !flag_covers(it.flag as i32, flag) {
                                    continue;
                                }
                                ele_fix_lh += it.rate as i32;
                            }
                            cardfix_ = (cardfix_ as i32 * (100 + ele_fix_lh) / 100) as i16;
                        }
                        cardfix_ = (cardfix_ as i32 * (100 + at(&lw.addsize, t_size) + at(&lw.addsize, sall)) / 100) as i16;
                        for &r in &t_race2 {
                            cardfix_ = (cardfix_ as i32 * (100 + at(&lw.addrace2, r as usize)) / 100) as i16;
                        }
                        cardfix_ = (cardfix_ as i32 * (100 + at(&lw.addclass, t_class_) + at(&lw.addclass, call)) / 100) as i16;
                    }
                } else {
                    // Calculates right & left hand weapon as unity (the element bonuses without the
                    // NK_IGNOREELEMENT check, as rAthena leaves it commented out)
                    let mut ele_fix = at(&rw.addele, t_ele) + at(&lw.addele, t_ele) + at(&rw.addele, eall) + at(&lw.addele, eall);
                    for it in rw.addele2.iter().chain(lw.addele2.iter()) {
                        if it.ele as i32 != ELE_ALL && it.ele as usize != t_ele {
                            continue;
                        }
                        if !flag_covers(it.flag as i32, flag) {
                            continue;
                        }
                        ele_fix += it.rate as i32;
                    }
                    cardfix = cardfix * (100 + ele_fix) / 100;
                    cardfix = cardfix * (100 + at(&rw.addrace, t_race) + at(&lw.addrace, t_race) + at(&rw.addrace, rall) + at(&lw.addrace, rall)) / 100;
                    cardfix = cardfix * (100 + at(&rw.addsize, t_size) + at(&lw.addsize, t_size) + at(&rw.addsize, sall) + at(&lw.addsize, sall)) / 100;
                    for &r in &t_race2 {
                        cardfix = cardfix * (100 + at(&rw.addrace2, r as usize) + at(&lw.addrace2, r as usize)) / 100;
                    }
                    cardfix = cardfix * (100 + at(&rw.addclass, t_class_) + at(&lw.addclass, t_class_) + at(&rw.addclass, call) + at(&lw.addclass, call)) / 100;
                }
                for it in &rw.add_dmg {
                    if it.id as i32 == t_class {
                        cardfix = cardfix * (100 + it.val) / 100;
                        break;
                    }
                }
                if left & 1 != 0 {
                    for it in &lw.add_dmg {
                        if it.id as i32 == t_class {
                            cardfix_ = (cardfix_ as i32 * (100 + it.val) / 100) as i16;
                            break;
                        }
                    }
                }
                if left & 1 != 0 {
                    apply_cardfix(&mut damage, cardfix_ as i32);
                } else {
                    apply_cardfix(&mut damage, cardfix);
                }
            } else if let Some(tsd) = tsd.as_ref().filter(|_| !nk_has(nk, NK_IGNOREDEFCARD) && left & 2 == 0) {
                // Affected by target DEF bonuses
                let ib = &tsd.st.indexed_bonus;
                if !nk_has(nk, NK_IGNOREELEMENT) {
                    let re = rh_ele.max(0) as usize;
                    let mut ele_fix = at(&ib.subele, re) + at(&ib.subele, eall) + at(&ib.subele_script, re) + at(&ib.subele_script, eall);
                    for it in &tsd.st.lists.subele2 {
                        if it.ele as i32 != ELE_ALL && it.ele as i32 != rh_ele {
                            continue;
                        }
                        if !flag_covers(it.flag as i32, flag) {
                            continue;
                        }
                        ele_fix += it.rate as i32;
                    }
                    cardfix = cardfix * (100 - ele_fix) / 100;
                    if left & 1 != 0 && lh_ele != rh_ele {
                        let le = lh_ele.max(0) as usize;
                        let mut ele_fix_lh = at(&ib.subele, le) + at(&ib.subele, eall) + at(&ib.subele_script, le) + at(&ib.subele_script, eall);
                        for it in &tsd.st.lists.subele2 {
                            if it.ele as i32 != ELE_ALL && it.ele as i32 != lh_ele {
                                continue;
                            }
                            if !flag_covers(it.flag as i32, flag) {
                                continue;
                            }
                            ele_fix_lh += it.rate as i32;
                        }
                        cardfix = cardfix * (100 - ele_fix_lh) / 100;
                    }
                    // subdefele[s_defele]: s_defele is ELE_NONE (-1) only without a target player, never here
                    cardfix = cardfix * (100 - at(&ib.subdefele, s_defele.max(0) as usize) - at(&ib.subdefele, eall)) / 100;
                }
                cardfix = cardfix * (100 - at(&ib.subsize, s_size) - at(&ib.subsize, sall)) / 100;
                cardfix = cardfix * (100 - at(&ib.weapon_subsize, s_size) - at(&ib.weapon_subsize, sall)) / 100;
                let mut race_fix = 0;
                for &r in &s_race2 {
                    race_fix += at(&ib.subrace2, r as usize);
                }
                cardfix = cardfix * (100 - race_fix) / 100;
                race_fix = at(&ib.subrace, s_race) + at(&ib.subrace, rall);
                for it in &tsd.st.lists.subrace3 {
                    if it.race as i32 != RC_ALL as i32 && it.race as usize != s_race {
                        continue;
                    }
                    if !flag_covers(it.flag as i32, flag) {
                        continue;
                    }
                    race_fix += it.rate as i32;
                }
                cardfix = cardfix * (100 - race_fix) / 100;
                cardfix = cardfix * (100 - at(&ib.subclass, s_class_) - at(&ib.subclass, call)) / 100;
                for it in &tsd.st.lists.add_def {
                    if it.id as i32 == s_class {
                        cardfix = cardfix * (100 - it.val) / 100;
                        break;
                    }
                }
                if flag & BF_SHORT != 0 {
                    cardfix = cardfix * (100 - tsd.st.bonus.near_attack_def_rate) / 100;
                } else if !nk_has(nk, NK_IGNORELONGCARD) {
                    // BF_LONG (there's no other choice)
                    cardfix = cardfix * (100 - tsd.st.bonus.long_attack_def_rate) / 100;
                }
                if let Some(ts) = &tsc {
                    if ts.has(SC_DEF_RATE) {
                        cardfix = cardfix * (100 - ts.v1(SC_DEF_RATE)) / 100;
                    }
                }
                apply_cardfix(&mut damage, cardfix);
            }
            // Custom on BF_WEAPON to follow SC_ debuff BF_MAGIC renewal behavior
            if let Some(ts) = &tsc {
                if !nk_has(nk, NK_IGNOREDEFCARD) && !nk_has(nk, NK_IGNOREELEMENT) {
                    cardfix = 1000;
                    cardfix = cardfix * (100 + battle_calc_cardfix_debuff(ts, rh_ele)) / 100;
                    apply_cardfix(&mut damage, cardfix);
                }
            }
        }
        BF_MISC => {
            // Affected by target DEF bonuses
            if let Some(tsd) = tsd.as_ref().filter(|_| !nk_has(nk, NK_IGNOREDEFCARD)) {
                let ib = &tsd.st.indexed_bonus;
                if !nk_has(nk, NK_IGNOREELEMENT) {
                    let re = rh_ele.max(0) as usize;
                    let mut ele_fix = at(&ib.subele, re) + at(&ib.subele, eall) + at(&ib.subele_script, re) + at(&ib.subele_script, eall);
                    for it in &tsd.st.lists.subele2 {
                        if it.ele as i32 != rh_ele {
                            continue;
                        }
                        if !flag_covers(it.flag as i32, flag) {
                            continue;
                        }
                        ele_fix += it.rate as i32;
                    }
                    if s_defele != ELE_NONE {
                        ele_fix += at(&ib.subdefele, s_defele as usize) + at(&ib.subdefele, eall);
                    }
                    cardfix = cardfix * (100 - ele_fix) / 100;
                }
                let mut race_fix = at(&ib.subrace, s_race) + at(&ib.subrace, rall);
                for it in &tsd.st.lists.subrace3 {
                    if it.race as i32 != RC_ALL as i32 && it.race as usize != s_race {
                        continue;
                    }
                    if !flag_covers(it.flag as i32, flag) {
                        continue;
                    }
                    race_fix += it.rate as i32;
                }
                cardfix = cardfix * (100 - race_fix) / 100;
                cardfix = cardfix * (100 - at(&ib.subsize, s_size) - at(&ib.subsize, sall)) / 100;
                race_fix = 0;
                for &r in &s_race2 {
                    race_fix += at(&ib.subrace2, r as usize);
                }
                cardfix = cardfix * (100 - race_fix) / 100;
                cardfix = cardfix * (100 - at(&ib.subclass, s_class_) - at(&ib.subclass, call)) / 100;
                cardfix = cardfix * (100 - tsd.st.bonus.misc_def_rate) / 100;
                if flag & BF_SHORT != 0 {
                    cardfix = cardfix * (100 - tsd.st.bonus.near_attack_def_rate) / 100;
                } else if !nk_has(nk, NK_IGNORELONGCARD) {
                    cardfix = cardfix * (100 - tsd.st.bonus.long_attack_def_rate) / 100;
                }
                apply_cardfix(&mut damage, cardfix);
            }
            if let Some(ts) = &tsc {
                if !nk_has(nk, NK_IGNOREDEFCARD) && !nk_has(nk, NK_IGNOREELEMENT) {
                    cardfix = 1000;
                    cardfix = cardfix * (100 + battle_calc_cardfix_debuff(ts, rh_ele)) / 100;
                    apply_cardfix(&mut damage, cardfix);
                }
            }
        }
        _ => {}
    }
    (damage - original_damage).clamp(i32::MIN as i64, i32::MAX as i64) as i32
}

// ---------------------------------------------------------------------------------------------- absorb / block

/// battle.cpp battle_absorb_damage: bAbsorbDmgMaxHP / bAbsorbDmgMaxHP2 of a target player.
pub fn battle_absorb_damage(c: &Ctx, bl: &Unit, d: &mut Damage) {
    if d.damage == 0 && d.damage2 == 0 {
        return;
    }
    let (dmg_ori, mut dmg_new);
    match bl.kind {
        BL_PC => {
            let Some(sd) = bl::sd(c, bl) else { return };
            dmg_ori = d.damage + d.damage2;
            dmg_new = dmg_ori;
            let max_hp = sd.st.battle_status.max_hp as i64;
            if sd.st.bonus.absorb_dmg_maxhp != 0 {
                let hp = (sd.st.bonus.absorb_dmg_maxhp as i64 * max_hp / 100) as i32 as i64;
                if dmg_ori > hp {
                    dmg_new = dmg_ori - hp;
                }
            }
            if sd.st.bonus.absorb_dmg_maxhp2 != 0 {
                let hp = (sd.st.bonus.absorb_dmg_maxhp2 as i64 * max_hp / 100) as i32 as i64;
                if dmg_ori > hp {
                    dmg_new = hp;
                }
            }
        }
        _ => return,
    }
    if dmg_ori == dmg_new {
        return;
    }
    if d.damage2 == 0 {
        d.damage = dmg_new;
    } else if d.damage == 0 {
        d.damage2 = dmg_new;
    } else {
        d.damage = dmg_new;
        d.damage2 = dmg_new * d.damage2 / dmg_ori / 100;
        if d.damage2 < 1 {
            d.damage2 = 1;
        }
        d.damage -= d.damage2;
    }
}

/// Writes an SC entry's changed vals back (the entry may have ended since).
fn put(c: &Ctx, e: &crate::status::data::ScEntry) {
    sc::put_sce(c, e);
}

/// The Devotion link test rAthena repeats (battle_status_block_damage, battle_calc_return_damage,
/// battle_weapon_attack): `d_bl` devotes `target` — a mercenary whose master is the target, or a player whose
/// devotion[idx] is the target.
pub fn devotes(c: &Ctx, d_bl: &Unit, idx: i32, target: &Unit) -> bool {
    match d_bl.kind {
        crate::map::BL_MER => crate::skill::iface::mercenary_master_id(c, d_bl) == Some(target.id),
        BL_PC => usize::try_from(idx).ok().and_then(|i| crate::skill::state::get(c, d_bl.id).devotion.get(i).copied()) == Some(target.id),
        _ => false,
    }
}

/// battle.cpp battle_status_block_damage: statuses that block or absorb the damage. true: damage is inflicted.
pub fn battle_status_block_damage(c: &Ctx, src: &Unit, target: &Unit, d: &mut Damage, damage: &mut i64, skill_id: u16, skill_lv: u16) -> bool {
    use crate::db::skills::*;
    let flag = d.flag;
    let capi = |v: i64| v.clamp(i32::MIN as i64, i32::MAX as i64) as i32;
    // SC Types that must be first because they may or may not block damage
    if let Some(mut sce) = sc::get_sce(c, target.id, SC_KYRIE).filter(|_| *damage > 0) {
        sce.val2 -= capi(*damage);
        put(c, &sce);
        if flag & BF_WEAPON != 0 || skill_id == TF_THROWSTONE {
            *damage = if sce.val2 >= 0 { 0 } else { -(sce.val2 as i64) };
        }
        sce.val3 -= 1;
        put(c, &sce);
        if sce.val3 <= 0 || sce.val2 <= 0 || skill_id == AL_HOLYLIGHT || skill_id == PA_PRESSURE {
            sc::status_change_end(c, target.id, SC_KYRIE, 0);
        }
    }
    let element = if flag & BF_WEAPON != 0 {
        let sstatus = bl::status(c, src);
        if sstatus.rhw.ele as i32 == ELE_NEUTRAL && sstatus.lhw.ele > sstatus.rhw.ele {
            calc::battle_get_weapon_element(c, d, src, target, skill_id, skill_lv, crate::inventory::EQI_HAND_L, false)
        } else {
            calc::battle_get_weapon_element(c, d, src, target, skill_id, skill_lv, crate::inventory::EQI_HAND_R, false)
        }
    } else if flag & BF_MAGIC != 0 {
        calc::battle_get_magic_element(c, d, src, target, skill_id, skill_lv)
    } else {
        calc::battle_get_misc_element(c, d, src, target, skill_id, skill_lv)
    };
    let scl = sc::load(c, target.id);
    let immune = match element {
        ELE_NEUTRAL => SC_IMMUNE_PROPERTY_NOTHING,
        ELE_WATER => SC_IMMUNE_PROPERTY_WATER,
        ELE_EARTH => SC_IMMUNE_PROPERTY_GROUND,
        ELE_FIRE => SC_IMMUNE_PROPERTY_FIRE,
        ELE_WIND => SC_IMMUNE_PROPERTY_WIND,
        ELE_DARK => SC_IMMUNE_PROPERTY_DARKNESS,
        ELE_HOLY => SC_IMMUNE_PROPERTY_SAINT,
        ELE_POISON => SC_IMMUNE_PROPERTY_POISON,
        ELE_GHOST => SC_IMMUNE_PROPERTY_TELEKINESIS,
        ELE_UNDEAD => SC_IMMUNE_PROPERTY_UNDEAD,
        _ => -1,
    };
    if immune >= 0 && scl.has(immune) {
        *damage = 0;
        return false;
    }
    if let Some(mut sce) = sc::get_sce(c, target.id, SC_P_ALTER).filter(|_| *damage > 0) {
        crate::clif::specialeffect(c, target, EF_GUARD, crate::clif::Target::Area);
        sce.val3 -= capi(*damage);
        put(c, &sce);
        *damage = if sce.val3 >= 0 { 0 } else { -(sce.val3 as i64) };
        if sce.val3 <= 0 {
            sc::status_change_end(c, target.id, SC_P_ALTER, 0);
        }
    }
    if let Some(mut sce) = sc::get_sce(c, target.id, SC_TUNAPARTY).filter(|_| *damage > 0) {
        sce.val2 -= capi(*damage);
        put(c, &sce);
        *damage = if sce.val2 >= 0 { 0 } else { -(sce.val2 as i64) };
        if sce.val2 <= 0 {
            sc::status_change_end(c, target.id, SC_TUNAPARTY, 0);
        }
    }
    for t in [SC_DIMENSION1, SC_DIMENSION2] {
        if let Some(mut sce) = sc::get_sce(c, target.id, t).filter(|_| *damage > 0) {
            sce.val2 -= capi(*damage);
            put(c, &sce);
            if sce.val2 <= 0 {
                sc::status_change_end(c, target.id, t, 0);
            }
            return false;
        }
    }
    if let Some(mut sce) = sc::get_sce(c, target.id, SC_GUARDIAN_S).filter(|_| *damage > 0) {
        crate::clif::specialeffect(c, target, EF_GUARD3, crate::clif::Target::Area);
        sce.val2 -= capi(*damage);
        put(c, &sce);
        if flag & BF_WEAPON != 0 {
            *damage = if sce.val2 >= 0 { 0 } else { -(sce.val2 as i64) };
        }
        if sce.val2 <= 0 {
            sc::status_change_end(c, target.id, SC_GUARDIAN_S, 0);
        }
    }
    // Weapon Blocking can be triggered while the above statuses are active.
    if let Some(sce) = sc::get_sce(c, target.id, SC_WEAPONBLOCKING) {
        if flag & (BF_SHORT | BF_WEAPON) != 0 && ((c.rnd() % 100) as i32) < sce.val2 {
            crate::skill::clif::clif_skill_nodamage(c, Some(target), src, GC_WEAPONBLOCKING, sce.val1, true);
            sc::sc_start(c, Some(src), target, SC_WEAPONBLOCK_ON, 100, src.id as i32, skill_get_time2(GC_WEAPONBLOCKING, sce.val1 as u16) as i64);
            d.dmg_lv = ATK_BLOCK;
            return false;
        }
    }
    if *damage == 0 {
        return false;
    }
    let scl = sc::load(c, target.id);
    // ATK_BLOCK Type
    if let Some(sce) = scl.get(SC_SAFETYWALL).filter(|_| flag & (BF_SHORT | BF_MAGIC) == BF_SHORT) {
        // the Safety Wall / Steinwand unit group (skillunit) takes the hit; without it the status ends
        if crate::skillunit::safetywall_block(c, sce.val2, sce.val3, *damage) {
            d.dmg_lv = ATK_BLOCK;
            return false;
        }
        sc::status_change_end(c, target.id, SC_SAFETYWALL, 0);
    }
    let scl = sc::load(c, target.id);
    if (scl.has(SC_PNEUMA) && flag & (BF_MAGIC | BF_LONG) == BF_LONG)
        || (scl.has(SC_ZEPHYR) && !(flag & BF_MAGIC != 0 && skill_id != 0) && crate::db::skills::skill_get_inf(skill_id) & (INF_GROUND_SKILL | INF_SELF_SKILL) as i32 == 0)
        || scl.has(SC__MANHOLE)
        || scl.has(SC_KINGS_GRACE)
        || scl.has(SC_GRAVITYCONTROL)
    {
        d.dmg_lv = ATK_BLOCK;
        return false;
    }
    if scl.has(SC_WHITEIMPRISON) {
        // Gravitation and Pressure do damage without removing the effect
        let sstatus = bl::status(c, src);
        if skill_id == MG_NAPALMBEAT
            || skill_id == MG_SOULSTRIKE
            || skill_id == WL_SOULEXPANSION
            || (skill_id != 0 && crate::db::skills::skill_get_ele(skill_id, skill_lv) == ELE_GHOST)
            || (skill_id == 0 && sstatus.rhw.ele as i32 == ELE_GHOST)
        {
            if skill_id == WL_SOULEXPANSION {
                *damage *= 2;
            }
            sc::status_change_end(c, target.id, SC_WHITEIMPRISON, 0);
        } else {
            d.dmg_lv = ATK_BLOCK;
            return false;
        }
    }
    if let Some(mut sce) = sc::get_sce(c, target.id, SC_MILLENNIUMSHIELD).filter(|e| e.val2 > 0 && *damage > 0) {
        sce.val3 -= capi(*damage); // absorb damage
        put(c, &sce);
        d.dmg_lv = ATK_BLOCK;
        if sce.val3 <= 0 {
            // Shield Down
            sce.val2 -= 1;
            if sce.val2 > 0 {
                crate::skill::state::clif_millenniumshield(c, target, sce.val2 as i16);
                sce.val3 = 1000; // Next shield
                put(c, &sce);
            } else {
                put(c, &sce);
                sc::status_change_end(c, target.id, SC_MILLENNIUMSHIELD, 0); // All shields down
            }
            sc::status_change_start(c, Some(src), target, SC_STUN, 10000, 0, 0, 0, 0, 1000, SCSTART_NOTICKDEF, 0);
        }
        return false;
    }
    // ATK_MISS Type
    if let Some(sce) = sc::get_sce(c, target.id, SC_AUTOGUARD) {
        if flag & BF_WEAPON != 0 && ((c.rnd() % 100) as i32) < sce.val2 && !crate::db::skills::skill_get_inf2(skill_id, &[INF2_IGNOREAUTOGUARD]) {
            let sce_d = sc::get_sce(c, target.id, SC_DEVOTION);
            // different delay depending on skill level [celest]
            let delay: i64 = if sce.val1 <= 5 {
                300
            } else if sce.val1 > 5 && sce.val1 <= 9 {
                200
            } else {
                100
            };
            if target.kind == BL_PC && crate::inventory::pc_issit(c, target.id) {
                crate::pc::setstand(c, target.id, true);
            }
            let tick = c.tick();
            let d_bl = sce_d.as_ref().and_then(|e| crate::unit::get(c, e.val1 as u32));
            let devoter = match (&sce_d, &d_bl) {
                (Some(e), Some(dbl)) => devotes(c, dbl, e.val2, target) && crate::path::check_distance((target.x - dbl.x) as i32, (target.y - dbl.y) as i32, e.val3),
                _ => false,
            };
            match d_bl.filter(|_| devoter) {
                Some(dbl) => {
                    // If player is target of devotion, show guard effect on the devotion caster rather than the target
                    crate::skill::clif::clif_skill_nodamage(c, Some(&dbl), &dbl, CR_AUTOGUARD, sce.val1, true);
                    super::unit_attack::unit_set_walkdelay(c, dbl.id, tick, delay, 1, 0);
                }
                None => {
                    let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
                    crate::skill::clif::clif_skill_nodamage(c, Some(&t), &t, CR_AUTOGUARD, sce.val1, true);
                    super::unit_attack::unit_set_walkdelay(c, target.id, tick, delay, 1, 0);
                    if sc::has_sce(c, target.id, SC_SHRINK) {
                        let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
                        sc::sc_start(c, Some(&t), src, SC_STUN, 50, skill_lv as i32, skill_get_time2(skill_id, skill_lv) as i64);
                    }
                }
            }
            d.dmg_lv = ATK_MISS;
            return false;
        }
    }
    if scl.has(SC_NEUTRALBARRIER) && flag & (BF_LONG | BF_MAGIC) == BF_LONG {
        d.dmg_lv = ATK_MISS;
        return false;
    }
    // ATK_DEF Type
    if let Some(sce) = sc::get_sce(c, target.id, SC_LIGHTNINGWALK) {
        if flag & BF_MAGIC == 0 && flag & BF_LONG != 0 && ((c.rnd() % 100) as i32) < sce.val1 {
            let dir = crate::map::calc_dir(target, src.x, src.y);
            let (dx, dy) = (crate::path::DIRX[dir as usize & 7], crate::path::DIRY[dir as usize & 7]);
            if crate::unit::movepos(c, target.id, src.x - dx, src.y - dy, 1, true) {
                if let Some(mut t) = crate::unit::get(c, target.id) {
                    super::clif::clif_blown(c, &t);
                    crate::unit::setdir(c, &mut t, dir, true);
                    crate::unit::save(c, &t);
                }
            }
            d.dmg_lv = ATK_DEF;
            sc::status_change_end(c, target.id, SC_LIGHTNINGWALK, 0);
            return false;
        }
    }
    // Other
    if (scl.has(SC_HERMODE) && flag & BF_MAGIC != 0)
        || (scl.has(SC_TATAMIGAESHI) && flag & (BF_MAGIC | BF_LONG) == BF_LONG)
        || (scl.has(SC_MEIKYOUSISUI) && c.rnd() % 100 < 40)
    {
        return false;
    }
    if let Some(sce) = sc::get_sce(c, target.id, SC_PARRYING) {
        if flag & BF_WEAPON != 0 && skill_id != WS_CARTTERMINATION && ((c.rnd() % 100) as i32) < sce.val2 {
            crate::skill::clif::clif_skill_nodamage(c, Some(target), target, LK_PARRYING, sce.val1, true);
            if let Some(mut t) = crate::unit::get(c, target.id) {
                super::unit_attack::unit_set_attackdelay(c, &mut t, c.tick(), super::unit_attack::DELAY_EVENT_PARRY);
                crate::unit::save(c, &t);
            }
            return false;
        }
    }
    let not_quake = skill_id != NPC_EARTHQUAKE || flag & crate::skill::NPC_EARTHQUAKE_FLAG != 0;
    if scl.has(SC_DODGE) && (flag & BF_LONG != 0 || scl.has(SC_SPURT)) && not_quake && c.rnd() % 100 < 20 {
        if target.kind == BL_PC && crate::inventory::pc_issit(c, target.id) {
            crate::pc::setstand(c, target.id, true); // Stand it to dodge.
        }
        let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
        crate::skill::clif::clif_skill_nodamage(c, Some(&t), &t, TK_DODGE, 1, true);
        sc::sc_start4(c, Some(src), &t, SC_COMBO, 100, TK_JUMPKICK as i32, src.id as i32, 1, 0, 2000);
        return false;
    }
    if let Some(mut sce) = sc::get_sce(c, target.id, SC_KAUPE) {
        if not_quake && ((c.rnd() % 100) as i32) < sce.val2 {
            // Kaupe blocks damage (skill or otherwise) from players, mobs, homuns, mercenaries.
            crate::clif::specialeffect(c, target, EF_STORMKICK4, crate::clif::Target::Area);
            sce.val3 -= 1;
            put(c, &sce);
            if sce.val3 <= 0 {
                sc::status_change_end(c, target.id, SC_KAUPE, 0);
            }
            return false;
        }
    }
    if flag & BF_MAGIC != 0 {
        if let Some(sce) = sc::get_sce(c, target.id, SC_PRESTIGE) {
            if ((c.rnd() % 100) as i32) < sce.val2 {
                crate::clif::specialeffect(c, target, EF_STORMKICK4, crate::clif::Target::Area);
                return false;
            }
        }
    }
    if (scl.has(SC_UTSUSEMI) || scl.has(SC_BUNSINJYUTSU)) && flag & BF_WEAPON != 0 && !crate::db::skills::skill_get_inf2(skill_id, &[INF2_IGNORECICADA]) {
        let utsusemi = scl.get(SC_UTSUSEMI).cloned();
        let tick = c.tick();
        let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
        crate::skill::effect::skill_additional_effect(c, src, &t, skill_id, skill_lv, flag, ATK_BLOCK, tick);
        let src_now = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        if !sc::status_isdead(c, &src_now) {
            let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
            crate::skill::effect::skill_counter_additional_effect(c, &src_now, &t, skill_id, skill_lv, flag, tick);
        }
        if let Some(e) = &utsusemi {
            let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
            crate::clif::specialeffect(c, &t, EF_STORMKICK4, crate::clif::Target::Area);
            let src_now = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
            crate::skill::attack::skill_blown(c, &src_now, &t, e.val3, -1, crate::skill::attack::BLOWN_NONE);
        }
        // Both need to be consumed if they are active.
        if utsusemi.is_some() {
            if let Some(mut e) = sc::get_sce(c, target.id, SC_UTSUSEMI) {
                e.val2 -= 1;
                put(c, &e);
                if e.val2 <= 0 {
                    sc::status_change_end(c, target.id, SC_UTSUSEMI, 0);
                }
            }
        }
        if let Some(mut e) = sc::get_sce(c, target.id, SC_BUNSINJYUTSU) {
            e.val2 -= 1;
            put(c, &e);
            if e.val2 <= 0 {
                sc::status_change_end(c, target.id, SC_BUNSINJYUTSU, 0);
            }
        }
        return false;
    }
    if let Some(sce) = sc::get_sce(c, target.id, SC_DAMAGE_HEAL) {
        if *damage > 0 && flag & sce.val2 != 0 {
            let heal = (*damage).min(i32::MAX as i64) as i32;
            if flag & BF_WEAPON != 0 {
                clif_specialeffect_value(c, target, EF_HEAL, heal);
            } else {
                clif_specialeffect_value(c, target, 1143, heal);
            }
            clif_specialeffect_value(c, target, EF_GREEN_NUMBER, heal);
            crate::status::hp::status_heal(c, target.id, *damage, 0, 0, 0);
            *damage = 0;
            return false;
        }
    }
    true
}

/// homunculus.hpp enum e_hom_mode (SC_STYLE_CHANGE's val1).
pub const MH_MD_FIGHTING: i32 = 1;
pub const MH_MD_GRAPPLING: i32 = 2;

/// clif.hpp e_effect ids used here.
pub const EF_AUTOCOUNTER: i32 = 131;
pub const EF_HEAL: i32 = 312;
pub const EF_GUARD: i32 = 336;
pub const EF_GUARD3: i32 = 675;
pub const EF_STORMKICK4: i32 = 462;
pub const EF_GREEN_NUMBER: i32 = 657;

/// clif.cpp clif_specialeffect_value: ZC_NOTIFY_EFFECT3 (effect with a number) to the area.
pub fn clif_specialeffect_value(c: &Ctx, bl: &Unit, effect_id: i32, num: i32) {
    let mut p = crate::net::packets::PACKET_ZC_NOTIFY_EFFECT3 {
        packetType: crate::net::packets::PACKET_ZC_NOTIFY_EFFECT3::ID as i16,
        aid: bl.id,
        effectId: effect_id as u32,
        num: num as u64,
    };
    crate::clif::send(c, &p.to_vec(), bl, crate::clif::Target::Area);
    if crate::atcmd_disguise::disguised(c, bl) {
        p.aid = crate::atcmd_disguise::disguised_bl_id(bl.id);
        crate::clif::send(c, &p.to_vec(), bl, crate::clif::Target::SelfOnly);
    }
}

// ---------------------------------------------------------------------------------------------- battle_calc_damage

/// pc.cpp pc_overheat: the Mado Gear's heat (SC_OVERHEAT_LIMITPOINT; a cooling device's negative heat ends it).
pub fn pc_overheat(c: &Ctx, aid: u32, heat: i16) {
    if let Some(mut sce) = sc::get_sce(c, aid, SC_OVERHEAT_LIMITPOINT) {
        sce.val1 += heat as i32;
        sce.val1 = sce.val1.clamp(0, 1000);
        let v = sce.val1;
        put(c, &sce);
        if heat < 0 && v == 0 {
            // Cooling device used.
            sc::status_change_end(c, aid, SC_OVERHEAT_LIMITPOINT, 0);
            sc::status_change_end(c, aid, SC_OVERHEAT, 0);
        }
    } else if heat > 0 {
        if let Some(u) = crate::unit::get(c, aid) {
            sc::sc_start(c, Some(&u), &u, SC_OVERHEAT_LIMITPOINT, 100, heat as i32, 1000);
        }
    }
}

/// battle.cpp battle_calc_damage: statuses and map rules on the final damage (ATK may miss, be blocked, reduced,
/// increased, end statuses). GvG / battleground reductions come after it.
pub fn battle_calc_damage(c: &Ctx, src: &Unit, bl: &Unit, d: &mut Damage, damage: i64, skill_id: u16, skill_lv: u16) -> i64 {
    use crate::db::skills::*;
    let mut damage = damage;
    let div_ = d.div_ as i64;
    let flag = d.flag;
    // (note: rAthena names the attacker `tsd` and the target `sd` here)
    let tsd = bl::sd(c, src);
    if damage == 0 {
        return 0;
    }
    if conf::ksprotection() != 0 {
        // mob_ksprotected(src, bl) (monster.conf ksprotection: 0 — never consulted)
    }
    // a ground skill's damage on a Maelstrom cell (the cells skillunit sets)
    if crate::map_cell::getcell(c, Some(bl.map), bl.x, bl.y, crate::map_cell::CELL_CHKMAELSTROM) != 0
        && skill_id != 0
        && skill_get_type(skill_id) != BF_MISC
        && skill_get_casttype(skill_id) == CastType::Ground
    {
        return 0;
    }
    let sd = bl::sd(c, bl);
    if let Some(sd) = &sd {
        // Special no damage states
        let ss = &sd.st.special_state;
        if flag & BF_WEAPON != 0 && ss.no_weapon_damage != 0 {
            damage -= damage * ss.no_weapon_damage as i64 / 100;
        }
        if flag & BF_MAGIC != 0 && ss.no_magic_damage != 0 {
            damage -= damage * ss.no_magic_damage as i64 / 100;
        }
        if flag & BF_MISC != 0 && ss.no_misc_damage != 0 {
            damage -= damage * ss.no_misc_damage as i64 / 100;
        }
        if damage == 0 {
            return 0;
        }
    }
    if skill_id == SP_SOULEXPLOSION {
        // These skills bypass everything else.
        if crate::conf::pk_mode() == 1 && crate::npc::mapflag(c, bl.map, crate::npc::MF_PVP) {
            damage = battle_calc_pk_damage(c, src, bl, damage, skill_id, flag);
        }
        return damage;
    }
    let tsc = bl::sc(c, bl);
    // Nothing can reduce the damage, but Safety Wall and Millennium Shield can block it completely.
    if skill_id == SJ_NOVAEXPLOSING && !tsc.as_ref().map(|s| s.has(SC_SAFETYWALL) || s.has(SC_MILLENNIUMSHIELD)).unwrap_or(false) {
        if crate::conf::pk_mode() == 1 && crate::npc::mapflag(c, bl.map, crate::npc::MF_PVP) {
            damage = battle_calc_pk_damage(c, src, bl, damage, skill_id, flag);
        }
        return damage;
    }
    if let Some(ts) = tsc.as_ref().filter(|s| !s.empty()) {
        // Barrier just sets damage to 1 per hit and prevents the rest of the code from being processed
        if damage > 0 && ts.has(SC_BARRIER) {
            return div_;
        }
        // Damage increasing effects: flat +400% damage from melee
        if ts.has(SC_KAITE) && flag & (BF_SHORT | BF_MAGIC) == BF_SHORT {
            damage *= 4;
        }
        if ts.has(SC_GROUNDGRAVITY) && flag & (BF_MAGIC | BF_WEAPON) != 0 {
            damage += damage * 10 / 100;
        }
        if ts.has(SC_SHIELDCHAINRUSH) {
            damage += damage / 10;
        }
        if ts.has(SC_AETERNA) && skill_id != PF_SOULBURN {
            if src.kind != crate::map::BL_MER || skill_id == 0 {
                damage *= 2; // Lex Aeterna only doubles damage of regular attacks from mercenaries
            }
            sc::status_change_end(c, bl.id, SC_AETERNA, 0);
        }
        if ts.has(SC_RAID) {
            if bl::class_type(c, bl) == CLASS_BOSS {
                damage += damage * 15 / 100;
            } else {
                damage += damage * 30 / 100;
            }
        }
        if damage != 0 {
            if ts.has(SC_DEEPSLEEP) {
                damage += damage / 2; // 1.5 times more damage while in Deep Sleep.
                sc::status_change_end(c, bl.id, SC_DEEPSLEEP, 0);
            }
            if let (Some(tsd), Some(_)) = (&tsd, &sd) {
                if ts.has(SC_CRYSTALIZE) && flag & BF_WEAPON != 0 {
                    let w = tsd.ch.weapon as i32;
                    let arrow_atk = state::get(c, tsd.aid).arrow_atk;
                    match w {
                        W_MACE | W_2HMACE | W_1HAXE | W_2HAXE => damage += damage / 2,
                        W_MUSICAL | W_WHIP if !arrow_atk => {}
                        W_MUSICAL | W_WHIP | W_BOW | W_REVOLVER | W_RIFLE | W_GATLING | W_SHOTGUN | W_GRENADE | W_DAGGER | W_1HSWORD | W_2HSWORD => {
                            damage -= damage / 2
                        }
                        _ => {}
                    }
                }
            }
        }
        if ts.has(SC_SOUNDOFDESTRUCTION) {
            damage *= 2;
        }
        if ts.has(SC_DARKCROW) && flag & (BF_SHORT | BF_MAGIC) == BF_SHORT {
            let mut bonus = ts.v2(SC_DARKCROW) as i64;
            if ts.has(SC_BURNT) && bl::status(c, src).def_ele as i32 == ELE_FIRE {
                damage += damage * 666 / 100; // Custom value
            }
            if bl::class_type(c, bl) == CLASS_BOSS {
                bonus /= 2;
            }
            damage += damage * bonus / 100;
        }
        if ts.has(SC_HOLY_OIL) && flag & (BF_LONG | BF_WEAPON) == (BF_LONG | BF_WEAPON) {
            damage += damage * (3 * ts.v1(SC_HOLY_OIL)) as i64 / 100;
        }
        if ts.has(SC_RUSH_QUAKE1) && flag & BF_WEAPON == BF_WEAPON {
            damage += damage * 50 / 100;
        }
        if ts.has(SC_SHADOW_SCAR) {
            damage += damage * (3 * ts.v1(SC_SHADOW_SCAR)) as i64 / 100;
        }
        // Damage reductions
        if ts.has(SC_SHADOW_CLOCK) && flag & (BF_WEAPON | BF_MAGIC) != 0 {
            damage = damage * 85 / 100;
        }
        if ts.has(SC_DEFENDER) && skill_id != NJ_ZENYNAGE && skill_id != KO_MUCHANAGE && (flag & (BF_LONG | BF_WEAPON) == (BF_LONG | BF_WEAPON) || skill_id == GN_FIRE_EXPANSION_ACID) {
            damage -= damage * ts.v2(SC_DEFENDER) as i64 / 100;
        }
        if ts.has(SC_ADJUSTMENT) && flag & (BF_LONG | BF_WEAPON) == (BF_LONG | BF_WEAPON) {
            damage -= damage * 20 / 100;
        }
        if ts.has(SC_FOGWALL) && skill_id != RK_DRAGONBREATH && skill_id != RK_DRAGONBREATH_WATER && skill_id != NPC_DRAGONBREATH {
            if flag & BF_SKILL != 0 {
                damage -= damage * 25 / 100; // 25% reduction
            } else if flag & (BF_LONG | BF_WEAPON) == (BF_LONG | BF_WEAPON) {
                damage /= 4; // 75% reduction
            }
        }
        if ts.has(SC_SPORE_EXPLOSION) && flag & BF_LONG == BF_LONG {
            // (status_get_class(bl) == CLASS_BOSS: rAthena compares the class id, never CLASS_BOSS for real ids)
            damage += damage * (if bl::class_of(c, bl) == CLASS_BOSS { 5 } else { 10 }) / 100;
        }
        if ts.has(SC_ARMORCHANGE) {
            // On official servers, SC_ARMORCHANGE does not change DEF/MDEF but rather increases/decreases the damage
            if flag & BF_WEAPON != 0 {
                damage -= damage * ts.v2(SC_ARMORCHANGE) as i64 / 100;
            } else if flag & BF_MAGIC != 0 {
                damage -= damage * ts.v3(SC_ARMORCHANGE) as i64 / 100;
            }
        }
        if ts.has(SC_SMOKEPOWDER) {
            if flag & (BF_SHORT | BF_WEAPON) == (BF_SHORT | BF_WEAPON) {
                damage -= damage * 15 / 100; // 15% reduction to physical melee attacks
            } else if flag & (BF_LONG | BF_WEAPON) == (BF_LONG | BF_WEAPON) {
                damage -= damage * 50 / 100; // 50% reduction to physical ranged attacks
            }
        }
        if ts.has(SC_WATER_BARRIER) {
            damage = damage * 80 / 100; // 20% reduction to all type attacks
        }
        if ts.has(SC_SU_STOOP) {
            damage -= damage * 90 / 100;
        }
        if ts.has(SC_ARMOR) && ts.v3(SC_ARMOR) & flag != 0 && ts.v4(SC_ARMOR) & flag != 0 {
            // NPC_DEFENDER
            damage /= ts.v2(SC_ARMOR) as i64;
        }
        if ts.has(SC_ENERGYCOAT) && (skill_id == GN_HELLS_PLANT_ATK || ((flag & BF_WEAPON != 0 || flag & BF_MAGIC != 0) && skill_id != WS_CARTTERMINATION)) {
            let status = bl::status(c, bl);
            let mut per = (100 * status.sp as i64 / status.max_sp.max(1) as i64 - 1) as i32; // 100% counts as 80~99%
            per /= 20; // Uses 20% SP intervals.
            // SP Cost: 1% + 0.5% per every 20% SP
            if crate::status::hp::status_charge(c, bl.id, 0, ((10 + 5 * per) as i64 * status.max_sp as i64 / 1000) as i64) == 0 {
                sc::status_change_end(c, bl.id, SC_ENERGYCOAT, 0);
            }
            damage -= damage * 6 * (1 + per) as i64 / 100; // Reduction: 6% + 6% every 20%
        }
        if ts.has(SC_GRANITIC_ARMOR) {
            damage -= damage * ts.v2(SC_GRANITIC_ARMOR) as i64 / 100;
        }
        if ts.has(SC_PAIN_KILLER) {
            damage -= ts.v2(SC_PAIN_KILLER) as i64;
            damage = damage.max(1);
        }
        if ts.has(SC_MAGMA_FLOW) && (c.rnd() % 100) as i32 <= ts.v2(SC_MAGMA_FLOW) {
            let bl_now = crate::unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
            let src_now = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
            crate::skill::castend::skill_castend_damage_id(c, &bl_now, &src_now, MH_MAGMA_FLOW, ts.v1(SC_MAGMA_FLOW) as u16, c.tick(), 0);
        }
        if damage > 0 && ts.has(SC_STONEHARDSKIN) {
            if src.kind == BL_MOB {
                // using explicit call instead break_equip for duration
                sc::sc_start(c, Some(src), src, SC_STRIPWEAPON, 30, 0, crate::db::skills::skill_get_time2(RK_STONEHARDSKIN, ts.v1(SC_STONEHARDSKIN) as u16) as i64);
            } else if flag & (BF_WEAPON | BF_SHORT) != 0 {
                let src_now = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
                crate::skill::effect::skill_break_equip(c, &src_now, &src_now, crate::inventory::EQP_WEAPON as u16, 3000, BCT_SELF);
            }
        }
        if src.kind == BL_PC && ts.has(SC_GVG_GOLEM) {
            if flag & BF_WEAPON != 0 {
                damage -= damage * ts.v3(SC_GVG_GOLEM) as i64 / 100;
            }
            if flag & BF_MAGIC != 0 {
                damage -= damage * ts.v4(SC_GVG_GOLEM) as i64 / 100;
            }
        }
        // Renewal: steel body reduces all incoming damage to 1/10 [helvetica]
        if ts.has(SC_STEELBODY) {
            damage = if damage > 10 { damage / 10 } else { 1 };
        }
        // Finally added to remove the status of immobile when Aimed Bolt is used. [Jobbie]
        if skill_id == RA_AIMEDBOLT && (ts.has(SC_BITE) || ts.has(SC_ANKLE) || ts.has(SC_ELECTRICSHOCKER)) {
            sc::status_change_end(c, bl.id, SC_BITE, 0);
            sc::status_change_end(c, bl.id, SC_ANKLE, 0);
            sc::status_change_end(c, bl.id, SC_ELECTRICSHOCKER, 0);
        }
        if damage == 0 {
            return 0;
        }
        if sd.is_some() && ts.has(SC_FORCEOFVANGUARD) && flag & BF_WEAPON != 0 && ((c.rnd() % 100) as i32) < ts.v2(SC_FORCEOFVANGUARD) {
            if let Some(e) = ts.get(SC_FORCEOFVANGUARD) {
                crate::skill::state::pc_addspiritball(c, bl.id, skill_get_time(LG_FORCEOFVANGUARD, e.val1 as u16), e.val3);
            }
        }
        if sd.is_some() && ts.has(SC_GT_ENERGYGAIN) && flag & BF_WEAPON != 0 && ((c.rnd() % 100) as i32) < ts.v2(SC_GT_ENERGYGAIN) {
            let mut spheres = 5;
            if let Some(e) = sc::get_sce(c, bl.id, SC_RAISINGDRAGON) {
                spheres += e.val1;
            }
            crate::skill::state::pc_addspiritball(c, bl.id, skill_get_time2(SR_GENTLETOUCH_ENERGYGAIN, ts.v1(SC_GT_ENERGYGAIN) as u16), spheres);
        }
        if ts.get(SC_STYLE_CHANGE).map(|e| e.val1 == MH_MD_GRAPPLING).unwrap_or(false) {
            // (battle.cpp:1949-1954) We add a sphere for when the Homunculus is being hit
            // hd = BL_CAST(BL_HOM, bl); `hd && rnd()%100 < 50`: the roll only for a homunculus
            let is_hd = bl.kind == crate::map::BL_HOM && crate::homunculus::data::get(c, bl.id).is_some();
            if is_hd && c.rnd() % 100 < 50 {
                // According to WarpPortal, this is a flat 50% chance
                crate::homunculus::hom_addspiritball(c, bl.id, 10);
            }
        }
        if ts.has(SC__DEADLYINFECT) && flag & (BF_SHORT | BF_MAGIC) == BF_SHORT && damage > 0 && ((c.rnd() % 100) as i32) < (30 + 10 * ts.v1(SC__DEADLYINFECT)) {
            // status_change_spread(bl, src) (status.cpp: the status2 agent's port)
            log::warn!("battle_calc_damage: SC__DEADLYINFECT needs status_change_spread (status2 agent)");
        }
    }
    // SC effects from caster side.
    if let Some(s) = bl::sc(c, src).filter(|s| !s.empty()) {
        if s.has(SC_BREAKINGLIMIT) {
            match skill_id {
                HN_SHIELD_CHAIN_RUSH | HN_DOUBLEBOWLINGBASH => damage += damage * 120 / 100,
                HN_MEGA_SONIC_BLOW => damage *= 2,
                HN_SPIRAL_PIERCE_MAX => damage += damage * 130 / 100,
                _ => {}
            }
        }
        if s.has(SC_BLOODLUST) && flag & BF_WEAPON != 0 && damage > 0 && c.rnd_chance(s.v3(SC_BLOODLUST), 100) {
            crate::status::hp::status_heal(c, src.id, damage * s.v4(SC_BLOODLUST) as i64 / 100, 0, 0, 1);
        }
        if s.has(SC_BLOODSUCKER) && flag & BF_WEAPON != 0 && damage > 0 && ((c.rnd() % 100) as i32) < (2 * s.v1(SC_BLOODSUCKER) - 1) {
            crate::status::hp::status_heal(c, src.id, damage * s.v1(SC_BLOODSUCKER) as i64 / 100, 0, 0, 3);
        }
        if flag & BF_MAGIC != 0 && bl.kind == BL_PC && s.has(SC_GVG_GIANT) && s.v4(SC_GVG_GIANT) != 0 {
            damage += damage * s.v4(SC_GVG_GIANT) as i64 / 100;
        }
        if s.has(SC_POISONINGWEAPON) && flag & BF_SHORT != 0 && damage > 0 {
            damage += damage * 10 / 100;
            if ((c.rnd() % 100) as i32) < s.v3(SC_POISONINGWEAPON) {
                let t = s.v2(SC_POISONINGWEAPON);
                let dur = if t == SC_VENOMBLEED { crate::db::skills::skill_get_time2(GC_POISONINGWEAPON, 1) } else { crate::db::skills::skill_get_time2(GC_POISONINGWEAPON, 2) };
                sc::sc_start4(c, Some(src), bl, t, 100, s.v1(SC_POISONINGWEAPON), 0, 1, 0, dur as i64);
            }
        }
        if s.has(SC__DEADLYINFECT) && flag & (BF_SHORT | BF_MAGIC) == BF_SHORT && damage > 0 && ((c.rnd() % 100) as i32) < (30 + 10 * s.v1(SC__DEADLYINFECT)) {
            // status_change_spread(src, bl) (status.cpp: the status2 agent's port)
            log::warn!("battle_calc_damage: SC__DEADLYINFECT needs status_change_spread (status2 agent)");
        }
        if s.get(SC_STYLE_CHANGE).map(|e| e.val1 == MH_MD_FIGHTING).unwrap_or(false) {
            // (battle.cpp:1998-2002) when attacking: hd = BL_CAST(BL_HOM, src); the roll only for a homunculus
            let is_hd = src.kind == crate::map::BL_HOM && crate::homunculus::data::get(c, src.id).is_some();
            if is_hd && c.rnd() % 100 < 50 {
                crate::homunculus::hom_addspiritball(c, src.id, 10); // According to WarpPortal, this is a flat 50% chance
            }
        }
        if flag & BF_WEAPON != 0 && s.has(SC_ADD_ATK_DAMAGE) {
            damage += damage * s.v1(SC_ADD_ATK_DAMAGE) as i64 / 100;
        }
        if flag & BF_MAGIC != 0 && s.has(SC_ADD_MATK_DAMAGE) {
            damage += damage * s.v1(SC_ADD_MATK_DAMAGE) as i64 / 100;
        }
        if s.has(SC_DANCEWITHWUG) && flag & (BF_LONG | BF_WEAPON) == (BF_LONG | BF_WEAPON) {
            damage += damage * s.v1(SC_DANCEWITHWUG) as i64 / 100;
        }
        if s.has(SC_UNLIMITEDHUMMINGVOICE) && flag & BF_MAGIC != 0 {
            damage += damage * s.v3(SC_UNLIMITEDHUMMINGVOICE) as i64 / 100;
        }
        if let (Some(tsd), Some(e)) = (&tsd, s.get(SC_SOULREAPER)) {
            let soulball = crate::skill::state::get(c, tsd.aid).soulball as i32;
            if ((c.rnd() % 100) as i32) < e.val2 && soulball < crate::skill::state::MAX_SOUL_BALL {
                crate::clif::specialeffect(c, src, 1208, crate::clif::Target::Area);
                let max = 5 + 3 * bl::checkskill(c, Some(tsd), SP_SOULENERGY);
                crate::skill::state::pc_addsoulball(c, tsd.aid, 1, max);
            }
        }
    }
    // PK damage rates
    if crate::conf::pk_mode() == 1 && crate::npc::mapflag(c, bl.map, crate::npc::MF_PVP) {
        damage = battle_calc_pk_damage(c, src, bl, damage, skill_id, flag);
    }
    if conf::skill_min_damage() != 0 && damage > 0 && damage < div_ {
        if (flag & BF_WEAPON != 0 && conf::skill_min_damage() & 1 != 0) || (flag & BF_MAGIC != 0 && conf::skill_min_damage() & 2 != 0) || (flag & BF_MISC != 0 && conf::skill_min_damage() & 4 != 0) {
            damage = div_;
        }
    }
    if sd.is_some() && bl.sc.option & OPTION_MADOGEAR != 0 {
        // pc_ismadogear: pc_overheat(*sd, weapon element == ELE_FIRE ? 3 : 1)
        let ele = calc::battle_get_weapon_element(c, d, src, bl, skill_id, skill_lv, crate::inventory::EQI_HAND_R, false);
        pc_overheat(c, bl.id, if ele == ELE_FIRE { 3 } else { 1 });
    }
    // Target status (again), required for RELIEVE
    if let Some(ts) = bl::sc(c, bl).filter(|s| !s.empty()) {
        if ts.has(SC_RELIEVE_ON) && !ts.has(SC_RELIEVE_OFF) {
            damage = (damage - damage * ts.v2(SC_RELIEVE_ON) as i64 / 100).max(1);
        }
    }
    if bl.kind == BL_MOB {
        // Reduces damage received for Green Aura MVP
        let taken = super::mobif::damagetaken(c, bl);
        if taken != 100 {
            damage = (damage * taken / 100).max(1);
        }
    } else if bl.kind == BL_ELEM {
        // s_elemental_data* ed = BL_CAST(BL_ELEM, bl): ed->db->damagetaken (elemental_db `DamageTaken`)
        if let Some(db) = crate::elemental::data::get(c, bl.id).as_ref().and_then(crate::elemental::data::db_of) {
            if db.damagetaken != 100 {
                damage = (damage * db.damagetaken as i64 / 100).max(1);
            }
        }
    }
    if let Some(ts) = bl::sc(c, bl).filter(|s| !s.empty()) {
        let _ = ts;
        if !battle_status_block_damage(c, src, bl, d, &mut damage, skill_id, skill_lv) {
            // Statuses that reduce damage to 0.
            return 0;
        }
    }
    damage
}

// ---------------------------------------------------------------------------------------------- gvg / bg / pk

/// battle.cpp battle_can_hit_bg_target: a battleground team's monster (md->bg_id, battleground.rs) is immune to skills
/// with MD_SKILLIMMUNE and to its own team's players.
pub fn battle_can_hit_bg_target(c: &Ctx, src: &Unit, bl: &Unit, _skill_id: u16, flag: i32) -> bool {
    if bl.ud.immune_attack {
        return false;
    }
    let md_bg_id = if bl.kind == BL_MOB { crate::mob::get(c, bl.id).map(|md| md.bg_id as i32).unwrap_or(0) } else { 0 };
    if md_bg_id != 0 {
        if bl::has_mode(c, bl, MD_SKILLIMMUNE) && flag & BF_SKILL != 0 {
            return false; // Skill immunity.
        }
        if src.kind == BL_PC && crate::battleground::bg_id_of(c, src.id) == md_bg_id {
            return false;
        }
    }
    true
}

/// battle.cpp battle_calc_bg_damage.
pub fn battle_calc_bg_damage(c: &Ctx, src: &Unit, bl: &Unit, damage: i64, skill_id: u16, flag: i32) -> i64 {
    use crate::db::skills::INF2_IGNOREBGREDUCTION;
    let mut damage = damage;
    if damage == 0 {
        return 0;
    }
    if !battle_can_hit_bg_target(c, src, bl, skill_id, flag) {
        return 0;
    }
    if crate::db::skills::skill_get_inf2(skill_id, &[INF2_IGNOREBGREDUCTION]) {
        return damage; // skill that ignore bg map reduction
    }
    if flag & BF_SKILL != 0 {
        // Skills get a different reduction than non-skills. [Skotlex]
        if flag & BF_WEAPON != 0 {
            damage = damage * conf::bg_weapon_damage_rate() / 100;
        }
        if flag & BF_MAGIC != 0 {
            damage = damage * conf::bg_magic_damage_rate() / 100;
        }
        if flag & BF_MISC != 0 {
            damage = damage * conf::bg_misc_damage_rate() / 100;
        }
    } else {
        // Normal attacks get reductions based on range.
        if flag & BF_SHORT != 0 {
            damage = damage * conf::bg_short_damage_rate() / 100;
        }
        if flag & BF_LONG != 0 {
            damage = damage * conf::bg_long_damage_rate() / 100;
        }
    }
    damage.max(1)
}

/// battle.cpp battle_can_hit_gvg_target (guardians / the Emperium: mob agent and guild agent data).
pub fn battle_can_hit_gvg_target(c: &Ctx, src: &Unit, bl: &Unit, skill_id: u16, flag: i32) -> bool {
    if bl.ud.immune_attack {
        return false;
    }
    if bl.kind == BL_MOB && (super::mobif::guardian_guild_id(c, bl).is_some() || super::mobif::special_ai(c, bl) == super::mobif::AI_GUILD) {
        let class_ = bl::class_of(c, bl);
        let emperium = class_ == super::mobif::MOBID_EMPERIUM as i32;
        if (bl::has_mode(c, bl, MD_SKILLIMMUNE) || (emperium && !crate::db::skills::skill_get_inf2(skill_id, &[crate::db::skills::INF2_TARGETEMPERIUM]))) && flag & BF_SKILL != 0 {
            return false; // Skill immunity.
        }
        if src.kind != BL_MOB || crate::mob::rtdb::mob_is_clone(c, super::mobif::mob_id(c, src) as i32) != 0 {
            // `auto g = src->type == BL_PC ? ((TBL_PC *)src)->guild : guild_search(status_get_guild_id(src));`
            let g = if src.kind == BL_PC {
                crate::guild::sd_guild(c, src.id)
            } else {
                crate::guild::guild_search(c, super::target::status_get_guild_id(c, src))
            };
            let approval = |g: &crate::guild::MapGuild| crate::guild::guild_checkskill(&g.guild, crate::db::skills::ids::GD_APPROVAL as i32);
            if emperium && g.as_ref().map(|g| approval(g) <= 0).unwrap_or(true) {
                return false;
            }
            if let Some(g) = g {
                // battle_config.guild_max_castles: 0 (conf/battle/guild.conf)
                let guild_max_castles: i32 = crate::battle_config::get(crate::battle_config::Bc::GUILD_MAX_CASTLES);
                if guild_max_castles != 0 && crate::guild::guild_checkcastles(c, &g.guild) >= guild_max_castles {
                    return false; // [MouseJstr]
                }
                if super::mobif::special_ai(c, bl) == super::mobif::AI_GUILD && g.guild.guild_id == super::mobif::master_id(c, bl) as i32 {
                    return false;
                }
            }
        }
    }
    true
}

/// battle.cpp battle_calc_gvg_damage.
pub fn battle_calc_gvg_damage(c: &Ctx, src: &Unit, bl: &Unit, damage: i64, skill_id: u16, flag: i32) -> i64 {
    use crate::db::skills::INF2_IGNOREGVGREDUCTION;
    let mut damage = damage;
    if damage == 0 {
        return 0; // No reductions to make.
    }
    if !battle_can_hit_gvg_target(c, src, bl, skill_id, flag) {
        return 0;
    }
    if crate::db::skills::skill_get_inf2(skill_id, &[INF2_IGNOREGVGREDUCTION]) {
        return damage; // Skills with no gvg damage reduction.
    }
    if flag & BF_SKILL != 0 {
        if flag & BF_WEAPON != 0 {
            damage = damage * conf::gvg_weapon_damage_rate() / 100;
        }
        if flag & BF_MAGIC != 0 {
            damage = damage * conf::gvg_magic_damage_rate() / 100;
        }
        if flag & BF_MISC != 0 {
            damage = damage * conf::gvg_misc_damage_rate() / 100;
        }
    } else {
        if flag & BF_SHORT != 0 {
            damage = damage * conf::gvg_short_damage_rate() / 100;
        }
        if flag & BF_LONG != 0 {
            damage = damage * conf::gvg_long_damage_rate() / 100;
        }
    }
    damage.max(1)
}

/// battle.cpp battle_calc_pk_damage (between players only).
pub fn battle_calc_pk_damage(_c: &Ctx, src: &Unit, bl: &Unit, damage: i64, _skill_id: u16, flag: i32) -> i64 {
    let mut damage = damage;
    if damage == 0 {
        return 0;
    }
    if crate::conf::pk_mode() == 0 {
        return damage; // PK mode is disabled.
    }
    if src.kind == BL_PC && bl.kind == BL_PC {
        if flag & BF_SKILL != 0 {
            if flag & BF_WEAPON != 0 {
                damage = damage * conf::pk_weapon_damage_rate() / 100;
            }
            if flag & BF_MAGIC != 0 {
                damage = damage * conf::pk_magic_damage_rate() / 100;
            }
            if flag & BF_MISC != 0 {
                damage = damage * conf::pk_misc_damage_rate() / 100;
            }
        } else {
            if flag & BF_SHORT != 0 {
                damage = damage * conf::pk_short_damage_rate() / 100;
            }
            if flag & BF_LONG != 0 {
                damage = damage * conf::pk_long_damage_rate() / 100;
            }
        }
    }
    damage.max(1)
}

/// battle.cpp battle_calc_drain: HP / SP drained (rate 1000 = 100%).
pub fn battle_calc_drain(c: &Ctx, damage: i64, rate: i32, per: i32) -> i32 {
    let mut diff: i64 = 0;
    if per != 0 && (rate > 1000 || ((c.rnd() % 1000) as i32) < rate) {
        diff = damage * per as i64 / 100;
        if diff == 0 {
            diff = if per > 0 { 1 } else { -1 };
        }
    }
    diff.clamp(i32::MIN as i64, i32::MAX as i64) as i32
}

/// battle.cpp battle_calc_return_damage: the damage bounced back to the attacker (item reflect; status reflect with
/// status_reflect).
pub fn battle_calc_return_damage(c: &Ctx, tbl: &Unit, src: &Unit, dmg: &mut i64, flag: i32, skill_id: u16, status_reflect: bool) -> i64 {
    use crate::db::skills::*;
    let tsc = bl::sc(c, tbl);
    if let Some(ts) = &tsc {
        // These statuses do not reflect any damage (off the target)
        if ts.has(SC_WHITEIMPRISON) || ts.has(SC_DARKCROW) || ts.has(SC_KYOMU) {
            return 0;
        }
    }
    let scl = bl::sc(c, src);
    if let Some(s) = &scl {
        if skill_id == GN_HELLS_PLANT_ATK && s.has(SC_HELLS_PLANT) {
            return 0;
        }
    }
    let tsd = bl::sd(c, tbl);
    let mut rdamage: i64 = 0;
    let damage = *dmg;
    if flag & BF_SHORT != 0 {
        // Bounces back part of the damage.
        let trap = crate::db::skills::skill_get_inf2(skill_id, &[INF2_ISTRAP]);
        let swr = tsd.as_ref().map(|s| s.st.bonus.short_weapon_damage_return).unwrap_or(0);
        if (trap || !status_reflect) && swr != 0 {
            rdamage += damage * swr as i64 / 100;
        } else if let Some(ts) = tsc.as_ref().filter(|s| status_reflect && !s.empty()) {
            if ts.has(SC_REFLECTSHIELD) {
                if let Some(sce_d) = ts.get(SC_DEVOTION) {
                    if let Some(d_bl) = crate::unit::get(c, sce_d.val1 as u32) {
                        if devotes(c, &d_bl, sce_d.val2, tbl) {
                            // Don't reflect non-skill attack if has SC_REFLECTSHIELD from Devotion bonus inheritance
                            if (skill_id == 0 && conf::devotion_rdamage_skill_only() && ts.v4(SC_REFLECTSHIELD) != 0)
                                || !crate::path::check_distance((tbl.x - d_bl.x) as i32, (tbl.y - d_bl.y) as i32, sce_d.val3)
                            {
                                return 0;
                            }
                        }
                    }
                }
            }
            if ts.has(SC_REFLECTSHIELD) && skill_id != WS_CARTTERMINATION && skill_id != NPC_MAXPAIN_ATK {
                if skill_id == 0 && conf::devotion_rdamage_skill_only() && ts.v4(SC_REFLECTSHIELD) != 0 {
                    rdamage = 0;
                } else {
                    rdamage += damage * ts.v2(SC_REFLECTSHIELD) as i64 / 100;
                }
            }
            if ts.has(SC_DEATHBOUND) && skill_id != WS_CARTTERMINATION && skill_id != GN_HELLS_PLANT_ATK && !bl::has_mode(c, src, MD_STATUSIMMUNE) {
                let dist = crate::path::distance((src.x - tbl.x) as i32, (src.y - tbl.y) as i32);
                let dir = crate::map::calc_dir(tbl, src.x, src.y);
                if let Some(e) = ts.get(SC_DEATHBOUND).filter(|_| dist <= 0 || bl::map_check_dir(dir as i32, crate::unit::getdir(tbl) as i32) == 0) {
                    // Amplify damage.
                    let rd1 = damage.min(bl::status(c, tbl).max_hp as i64) * e.val2 as i64 / 100;
                    *dmg = rd1 * 30 / 100; // Received damage = 30% of amplified damage.
                    let amotion = bl::status(c, src).amotion as i32;
                    crate::skill::clif::clif_skill_damage(c, src, tbl, c.tick(), amotion, 0, crate::skill::clif::DMGVAL_IGNORE, 1, RK_DEATHBOUND, e.val1, DMG_SINGLE);
                    crate::skill::attack::skill_blown(c, tbl, src, skill_get_blewcount(RK_DEATHBOUND, e.val1 as u16), crate::unit::getdir(src) as i8, crate::skill::attack::BLOWN_NONE);
                    sc::status_change_end(c, tbl.id, SC_DEATHBOUND, 0);
                    rdamage += rd1 * 70 / 100; // Target receives 70% of the amplified damage. [Rytech]
                }
            }
        }
    } else {
        let lwr = tsd.as_ref().map(|s| s.st.bonus.long_weapon_damage_return).unwrap_or(0);
        if !status_reflect && lwr != 0 {
            rdamage += damage * lwr as i64 / 100;
        }
    }
    // Config damage adjustment
    if crate::npc::mapflag_gvg2(c, src.map) {
        rdamage = battle_calc_gvg_damage(c, src, tbl, rdamage, skill_id, flag);
    } else if crate::npc::mapflag(c, src.map, crate::npc::MF_BATTLEGROUND) {
        rdamage = battle_calc_bg_damage(c, src, tbl, rdamage, skill_id, flag);
    } else if crate::npc::mapflag(c, src.map, crate::npc::MF_PVP) {
        rdamage = battle_calc_pk_damage(c, src, tbl, rdamage, skill_id, flag);
    }
    // Skill damage adjustment
    let skill_damage = calc::battle_skill_damage(c, src, Some(tbl), skill_id);
    if skill_damage != 0 {
        rdamage += rdamage * skill_damage as i64 / 100;
    }
    let mut reduce: i64 = 0;
    if let Some(sd) = bl::sd(c, src) {
        if sd.st.bonus.reduce_damage_return != 0 {
            reduce += sd.st.bonus.reduce_damage_return as i64;
        }
    }
    if let Some(s) = &scl {
        if status_reflect && s.has(SC_REFLECTDAMAGE) {
            reduce += s.v2(SC_REFLECTDAMAGE) as i64;
        }
        if s.has(SC_VENOMBLEED) && s.v3(SC_VENOMBLEED) == 0 {
            reduce += s.v2(SC_VENOMBLEED) as i64;
        }
        if s.has(SC_REF_T_POTION) {
            reduce += 100;
        }
    }
    if rdamage > 0 {
        rdamage -= rdamage * reduce.min(100) / 100;
        rdamage = rdamage.max(1);
    }
    if rdamage == 0 {
        return 0; // No reflecting damage calculated.
    }
    rdamage.clamp(1, bl::status(c, tbl).max_hp as i64)
}


#[cfg(test)]
mod tests {
    use super::*;

    /// The effect, map flag and constant values battle names equal the script constants of rAthena's build.
    #[test]
    fn constants_match_script_constants() {
        let k = |n: &str| crate::data::constant(n).unwrap_or_else(|| panic!("constant {n}"));
        assert_eq!(k("EF_AUTOCOUNTER"), EF_AUTOCOUNTER as i64);
        assert_eq!(k("EF_HEAL"), EF_HEAL as i64);
        assert_eq!(k("EF_GUARD"), EF_GUARD as i64);
        assert_eq!(k("EF_GUARD3"), EF_GUARD3 as i64);
        assert_eq!(k("EF_STORMKICK4"), EF_STORMKICK4 as i64);
        assert_eq!(k("EF_GREEN_NUMBER"), EF_GREEN_NUMBER as i64);
        assert_eq!(k("EF_GRANDCROSS2"), 450);
        assert_eq!(k("mf_pvp_noparty"), super::super::target::MF_PVP_NOPARTY as i64);
        assert_eq!(k("mf_pvp_noguild"), super::super::target::MF_PVP_NOGUILD as i64);
        assert_eq!(k("mf_gvg_noparty"), super::super::target::MF_GVG_NOPARTY as i64);
        assert_eq!(k("mf_pvp_nocalcrank"), super::super::target::MF_PVP_NOCALCRANK as i64);
        assert_eq!(k("MD_LOOTER"), super::super::mobif::MD_LOOTER as i64);
        assert_eq!(k("MD_ASSIST"), super::super::mobif::MD_ASSIST as i64);
        assert_eq!(k("BF_WEAPON"), BF_WEAPON as i64);
        assert_eq!(k("BF_NORMAL"), BF_NORMAL as i64);
        assert_eq!(k("BCT_ENEMY"), BCT_ENEMY as i64);
        assert_eq!(k("BCT_ALL"), BCT_ALL as i64);
        assert_eq!(k("UNT_USED_TRAPS"), super::super::target::UNT_USED_TRAPS as i64);
        assert_eq!(k("BF_MAGIC"), BF_MAGIC as i64);
        assert_eq!(k("BF_MISC"), BF_MISC as i64);
        assert_eq!(k("BF_SKILL"), BF_SKILL as i64);
        assert_eq!(k("EFST_POSTDELAY"), EFST_POSTDELAY as i64);
    }

    /// The e_battle_flag trigger rule of bonuses (bSubEle2, bAddEle2, bSubRace3, vanish, autobonus).
    #[test]
    fn battle_flag_trigger_rule() {
        let normal_melee = BF_WEAPON | BF_SHORT | BF_NORMAL;
        assert!(flag_covers(BF_WEAPON | BF_SHORT | BF_NORMAL, normal_melee));
        assert!(!flag_covers(BF_WEAPON | BF_LONG | BF_NORMAL, normal_melee));
        assert!(!flag_covers(BF_MAGIC | BF_SHORT | BF_NORMAL, normal_melee));
        assert!(!flag_covers(BF_WEAPON | BF_SHORT | BF_SKILL, normal_melee));
    }
}
