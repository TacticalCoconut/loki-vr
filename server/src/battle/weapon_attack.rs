//! battle.cpp battle_weapon_attack (a normal attack, called by unit_attack_timer) and the damage delivery:
//! battle_delay_damage / battle_delay_damage_sub (the damage lands after the attack motion; the rAthena
//! `delay_damage` struct is a `battle_delay_damage` row named by the timer), battle_damage, battle_fix_damage,
//! battle_calc_walkdelay, battle_drain, battle_vanish_damage, battle_vellum_damage, battle_check_coma,
//! battle_consume_ammo, battle_damage_area (Reflect Damage), battle_autocast_aftercast,
//! battle_autocast_elembuff_skill and clif_bladestop.

use spacetimedb::{table, Table};

use super::bl::{self, Sd};
use super::calc::{self, Damage};
use super::clif::{clif_arrow_fail, clif_damage, ARROWFAIL_NO_AMMO, USESKILL_FAIL_NEED_MORE_BULLET};
use super::*;
use crate::db::skills::*;
use crate::inventory::EQI_AMMO;
use crate::map::{BL_MOB, BL_PC, BL_SKILL};
use crate::status::consts::*;
use crate::status::defs::*;
use crate::status::sc;
use crate::timer::{self, Func};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// battle.cpp struct delay_damage, one row per pending battle_delay_damage_sub timer (the timer's data is the id).
#[table(accessor = battle_delay_damage)]
#[derive(Clone, Debug)]
pub struct DelayDamage {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub src_id: u32,
    pub target_id: u32,
    pub damage: i64,
    pub div_: i16,
    pub distance: u16,
    pub skill_lv: u16,
    pub skill_id: u16,
    pub dmg_lv: u8,
    pub attack_type: u16,
    pub additional_effects: bool,
    pub src_type: u16,
    pub isspdamage: bool,
    pub is_norm_attacked: bool,
}

/// battle.cpp battle_calc_walkdelay: the walk delay a hit gives (0: none).
fn battle_calc_walkdelay(c: &Ctx, bl: &Unit, damage: i64, div_: i16) -> Tick {
    if damage < 1 {
        return 0;
    }
    // If hits are 0 that means that no walkdelay shall be set
    if div_ == 0 {
        return 0;
    }
    let mut delay = bl::status(c, bl).dmotion as Tick;
    let mut div_ = div_;
    // Multi-hit skills mean higher delays
    if div_ > 1 {
        // If there are more than 10 hits, it calculates as 2 hits
        if div_ > 10 {
            div_ = 2;
        }
        delay += conf::multihit_delay() * (div_ as Tick - 1);
    }
    // apply_rate: 64-bit value * rate / 100
    if bl.kind == BL_PC {
        delay = delay * conf::pc_walk_delay_rate() as Tick / 100;
    } else {
        delay = delay * conf::walk_delay_rate() as Tick / 100;
    }
    // Return 1 to specify there should be no noticeable delay, but you should stop walking.
    if delay > 0 {
        delay
    } else {
        1
    }
}

/// battle.cpp battle_damage: deals the damage now, then the additional effects and the monster events.
/// Returns HP + SP + AP taken (0 when nothing changed).
#[allow(clippy::too_many_arguments)]
pub fn battle_damage(c: &Ctx, src: Option<&Unit>, target: &Unit, damage: i64, div_: i16, skill_lv: u16, skill_id: u16, dmg_lv: u8, attack_type: u16, additional_effects: bool, tick: Tick, isspdamage: bool, is_norm_attacked: bool) -> i32 {
    // SP damage does not trigger anything, just substracts SP
    if isspdamage {
        return crate::status::hp::status_zap(c, target.id, 0, damage, 0);
    }
    let mut dmg_change = 0;
    let delay = battle_calc_walkdelay(c, target, damage, div_);
    let sd = src.and_then(|s| bl::sd(c, s));
    if sd.as_ref().map(|sd| battle_check_coma(c, sd, target, attack_type as i32)).unwrap_or(false) {
        // Coma attack
        dmg_change = crate::status::hp::status_damage(c, src, target.id, damage, 0, 0, delay, 16, skill_id);
    } else if dmg_lv > ATK_BLOCK {
        dmg_change = crate::status::hp::status_fix_damage(c, src, target.id, damage, delay, skill_id);
    }
    let Some(src) = src else { return dmg_change };
    let target_now = unit::get(c, target.id).unwrap_or_else(|| target.clone());
    if attack_type != 0 && !sc::status_isdead(c, &target_now) && additional_effects {
        super::addeff::skill_additional_effect(c, src, &target_now, skill_id, skill_lv, attack_type as i32, dmg_lv, tick);
    }
    if dmg_lv > ATK_BLOCK && attack_type != 0 && additional_effects {
        super::addeff::skill_counter_additional_effect(c, src, &target_now, skill_id, skill_lv, attack_type as i32, tick);
    }
    // This is the last place where we have access to the actual damage type, so any monster events depending on
    // type must be placed here. These events trigger when the attack connects
    if target.kind == BL_MOB && dmg_lv > ATK_BLOCK {
        // Trigger monster skill conditions
        let target_now = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        if src.id != target.id && !sc::status_isdead(c, &target_now) {
            if damage > 0 {
                crate::mob::skill::mobskill_event(c, target.id, src, tick, attack_type as i32, damage);
            }
            if skill_id > 0 {
                crate::mob::skill::mobskill_event(c, target.id, src, tick, crate::skill::castend::MSC_SKILLUSED | ((skill_id as i32) << 16), damage);
            }
        }
        // Prepare corresponding attacked event that occurs at the end of the calculated walk delay
        let f = if is_norm_attacked { crate::timer::Func::MobNormAttacked } else { crate::timer::Func::MobAttacked };
        crate::timer::add(c, tick + delay, f, target.id, src.id as i64);
    }
    dmg_change
}

/// battle.cpp battle_delay_damage_sub (Func::BattleDelayDamage; data = the `battle_delay_damage` row).
pub fn battle_delay_damage_sub(c: &Ctx, tick: Tick, row_id: u64) {
    let Some(dat) = c.db().battle_delay_damage().id().find(row_id) else { return };
    c.db().battle_delay_damage().id().delete(row_id);
    let src = unit::get(c, dat.src_id);
    let target = unit::get(c, dat.target_id);
    if let Some(target) = target.filter(|t| !sc::status_isdead(c, t)) {
        let invincible = target.kind == BL_PC && crate::pc::get(c, target.id).map(|p| p.invincible_timer != 0).unwrap_or(false);
        match &src {
            Some(src)
                if target.map == src.map
                    && !invincible
                    && crate::path::check_distance((src.x - target.x) as i32, (src.y - target.y) as i32, dat.distance as i32) =>
            {
                // Check to see if you haven't teleported. [Skotlex] Deal damage
                battle_damage(c, Some(src), &target, dat.damage, dat.div_, dat.skill_lv, dat.skill_id, dat.dmg_lv, dat.attack_type, dat.additional_effects, tick, dat.isspdamage, dat.is_norm_attacked);
            }
            None if dat.skill_id == CR_REFLECTSHIELD => {
                // it was monster reflected damage, and the monster died, we pass the damage to the character
                battle_fix_damage(c, Some(&target), &target, dat.damage, dat.div_, dat.skill_id);
            }
            _ => {}
        }
    }
    if let Some(src) = src.filter(|s| s.kind == BL_PC) {
        let recalc = crate::status::data::with_sd(c, src.id, |sd| {
            sd.delayed_damage -= 1;
            if sd.delayed_damage == 0 && sd.hold_recalc {
                sd.hold_recalc = false;
                return true;
            }
            false
        })
        .unwrap_or(false);
        if recalc {
            crate::status::calc_pc(c, src.id, crate::status::SCO_FORCE);
        }
    }
}

/// battle.cpp battle_delay_damage: the damage lands `amotion` ms later (Devotion / Water Screen take it over).
#[allow(clippy::too_many_arguments)]
pub fn battle_delay_damage(c: &Ctx, tick: Tick, amotion: i32, src: &Unit, target: &Unit, attack_type: i32, skill_id: u16, skill_lv: u16, damage: i64, dmg_lv: u8, div_: i16, additional_effects: bool, isspdamage: bool, is_norm_attacked: bool) -> i32 {
    let mut damage = damage;
    let mut dmg_lv = dmg_lv;
    let mut amotion = amotion as i64;
    let tsc = bl::sc(c, target);
    let mut devoted = false;
    if let Some(ts) = &tsc {
        if let Some(e) = ts.get(SC_DEVOTION).filter(|e| e.val1 != 0) {
            if let Some(d) = unit::get(c, e.val1 as u32) {
                if crate::path::check_distance((target.x - d.x) as i32, (target.y - d.y) as i32, e.val3) {
                    devoted = true;
                }
            }
        }
        if ts.get(SC_WATER_SCREEN_OPTION).map(|e| e.val1 != 0 && unit::get(c, e.val1 as u32).is_some()).unwrap_or(false) {
            devoted = true;
        }
    }
    if devoted && damage > 0 && skill_id != CR_REFLECTSHIELD {
        if target.kind == BL_PC && crate::inventory::pc_issit(c, target.id) && conf::devotion_standup_fix() {
            crate::pc::setstand(c, target.id, true);
            crate::status::sc_ext::skill_sit(c, target.id, false);
        }
        damage = 0;
        // This is a quick fix to make devotion protect from cast cancel and autocasts
        if dmg_lv == ATK_DEF {
            dmg_lv = ATK_BLOCK;
        }
    }
    // The client refuses to display animations slower than 1x speed
    if conf::synchronize_damage() && skill_id == 0 && src.kind == BL_MOB && amotion > bl::status(c, src).clientamotion as i64 {
        amotion = bl::status(c, src).clientamotion as i64;
    } else if !conf::delay_battle_damage() {
        amotion = 1;
    } else if src.kind != BL_PC && amotion > 1000 {
        // Aegis places a damage-delay cap of 1 sec to non player attacks
        amotion = 1000;
    }
    // Skip creation of timer
    if amotion <= 1 {
        battle_damage(c, Some(src), target, damage, div_, skill_lv, skill_id, dmg_lv, attack_type as u16, additional_effects, c.tick(), isspdamage, false);
        return 0;
    }
    let dist = crate::path::distance((src.x - target.x) as i32, (src.y - target.y) as i32);
    let row = c.db().battle_delay_damage().insert(DelayDamage {
        id: 0,
        src_id: src.id,
        target_id: target.id,
        damage,
        div_,
        distance: (dist + if conf::snap_dodge() { 10 } else { crate::conf::area_size() as i32 }) as u16,
        skill_lv,
        skill_id,
        dmg_lv,
        attack_type: attack_type as u16,
        additional_effects,
        src_type: src.kind,
        isspdamage,
        is_norm_attacked,
    });
    if src.kind == BL_PC {
        crate::status::data::with_sd(c, src.id, |sd| sd.delayed_damage += 1);
    }
    timer::add(c, tick + amotion, Func::BattleDelayDamage, 0, row.id as i64);
    0
}

/// battle.cpp battle_fix_damage: an immediate BF_MISC damage without additional effects.
pub fn battle_fix_damage(c: &Ctx, src: Option<&Unit>, target: &Unit, damage: i64, div_: i16, skill_id: u16) -> i32 {
    battle_damage(c, src, target, damage, div_, 0, skill_id, ATK_DEF, BF_MISC as u16, false, c.tick(), false, false)
}

/// battle.cpp battle_check_coma: bComaClass / bComaRace / bWeaponComa* chance (never against GvG monsters or
/// battlefield classes).
pub fn battle_check_coma(c: &Ctx, sd: &Sd, target: &Unit, attack_type: i32) -> bool {
    let tstatus = bl::status(c, target);
    if !sd.st.special_state.bonus_coma {
        return false;
    }
    if target.kind == BL_MOB {
        const RC2_GVG: u8 = 12;
        if bl::race2_of(c, target).contains(&RC2_GVG) || bl::class_of(c, target) == CLASS_BATTLEFIELD {
            return false;
        }
    }
    let ib = &sd.st.indexed_bonus;
    let at = |v: &Vec<i16>, i: usize| v.get(i).copied().unwrap_or(0) as i32;
    let status_immune = bl::has_mode(c, target, MD_STATUSIMMUNE);
    let (cl, race, ele) = (tstatus.class_ as usize, tstatus.race as usize, tstatus.def_ele as usize);
    let mut rate = 0;
    rate += at(&ib.coma_class, cl) + at(&ib.coma_class, CLASS_ALL as usize);
    if !status_immune {
        rate += at(&ib.coma_race, race) + at(&ib.coma_race, RC_ALL as usize);
    }
    if attack_type & BF_WEAPON != 0 {
        rate += at(&ib.weapon_coma_class, cl) + at(&ib.weapon_coma_class, CLASS_ALL as usize);
        if !status_immune {
            rate += at(&ib.weapon_coma_ele, ele) + at(&ib.weapon_coma_ele, ELE_ALL as usize);
            rate += at(&ib.weapon_coma_race, race) + at(&ib.weapon_coma_race, RC_ALL as usize);
        }
    }
    rate > 0 && c.rnd_chance(rate, 10000)
}

/// battle.cpp battle_vellum_damage: bHPVanishRaceRate / bSPVanishRaceRate replace the damage.
pub fn battle_vellum_damage(c: &Ctx, sd: &Sd, target: &Unit, wd: &mut Damage) -> bool {
    let tstatus = bl::status(c, target);
    let r = &sd.st.races;
    let race = tstatus.race as usize;
    let all = RC_ALL as usize;
    let g = |v: &Vec<crate::status::data::VanishGain>, i: usize| v.get(i).cloned().unwrap_or_default();
    let (hr, ha) = (g(&r.hp_vanish_race, race), g(&r.hp_vanish_race, all));
    let (sr, sa) = (g(&r.sp_vanish_race, race), g(&r.sp_vanish_race, all));
    let vellum_rate_hp = (hr.rate as i32 + ha.rate as i32).clamp(0, i16::MAX as i32) as i16;
    let vellum_hp = (hr.per as i32 + ha.per as i32).clamp(i8::MIN as i32, i8::MAX as i32) as i8;
    let vellum_rate_sp = (sr.rate as i32 + sa.rate as i32).clamp(0, i16::MAX as i32) as i16;
    let vellum_sp = (sr.per as i32 + sa.per as i32).clamp(i8::MIN as i32, i8::MAX as i32) as i8;
    // The HP and SP damage bonus from these items don't stack because of the special damage display for SP.
    if vellum_hp != 0 && vellum_rate_hp != 0 && (vellum_rate_hp >= 1000 || ((c.rnd() % 1000) as i32) < vellum_rate_hp as i32) {
        wd.damage = apply_rate(tstatus.max_hp, vellum_hp as i32) as i64;
        wd.damage2 = 0;
    } else if vellum_sp != 0 && vellum_rate_sp != 0 && (vellum_rate_sp >= 1000 || ((c.rnd() % 1000) as i32) < vellum_rate_sp as i32) {
        wd.damage = apply_rate(tstatus.max_sp, vellum_sp as i32) as i64;
        wd.damage2 = 0;
        wd.isspdamage = true;
    } else {
        return false;
    }
    true
}

/// map.hpp apply_rate (uint32 value * rate / 100 in 64 bits).
fn apply_rate(v: u32, rate: i32) -> u32 {
    (v as i64 * rate as i64 / 100) as u32
}

/// battle.cpp battle_vanish_damage: bHPVanishRate / bSPVanishRate (percent of the target's HP / SP).
pub fn battle_vanish_damage(c: &Ctx, sd: &Sd, target: &Unit, flag: i32) {
    let covers = |f: i32| {
        let f = f & flag;
        f & BF_WEAPONMASK != 0 && f & BF_RANGEMASK != 0 && f & BF_SKILLMASK != 0
    };
    // int16 accumulators
    let mut vanish_hp: i16 = 0;
    for it in &sd.st.lists.hp_vanish {
        if !covers(it.flag) {
            continue;
        }
        if it.rate != 0 && (it.rate >= 1000 || ((c.rnd() % 1000) as i32) < it.rate as i32) {
            vanish_hp = vanish_hp.wrapping_add(it.per);
        }
    }
    let mut vanish_sp: i16 = 0;
    for it in &sd.st.lists.sp_vanish {
        if !covers(it.flag) {
            continue;
        }
        if it.rate != 0 && (it.rate >= 1000 || ((c.rnd() % 1000) as i32) < it.rate as i32) {
            vanish_sp = vanish_sp.wrapping_add(it.per);
        }
    }
    if vanish_hp > 0 || vanish_sp > 0 {
        // Damage HP/SP applied once
        let src = unit::get(c, sd.aid);
        crate::status::hp::status_percent_damage(c, src.as_ref(), target.id, -(vanish_hp as i32), -(vanish_sp as i32), 0, false);
    }
}

/// battle.cpp battle_drain: bHPDrainRate / bSPDrainRate and the race / class drains of both hands.
pub fn battle_drain(c: &Ctx, sd: &Sd, _tbl: &Unit, rdamage: i64, ldamage: i64, race: i32, class_: i32) {
    if !super::fix::chk_race(race) && !super::fix::chk_class(class_) {
        return;
    }
    let (mut thp, mut tsp) = (0i32, 0i32);
    for i in 0..4 {
        // First two iterations: Right hand
        let (wd, damage) = if i < 2 { (&sd.st.right_weapon, rdamage) } else { (&sd.st.left_weapon, ldamage) };
        if damage <= 0 {
            continue;
        }
        let at = |v: &Vec<i16>, i: i32| v.get(i.max(0) as usize).copied().unwrap_or(0) as i32;
        if i == 1 || i == 3 {
            let mut hp = at(&wd.hp_drain_class, class_) + at(&wd.hp_drain_class, CLASS_ALL);
            hp += super::fix::battle_calc_drain(c, damage, wd.hp_drain_rate.rate as i32, wd.hp_drain_rate.per as i32);
            let mut sp = at(&wd.sp_drain_class, class_) + at(&wd.sp_drain_class, CLASS_ALL);
            sp += super::fix::battle_calc_drain(c, damage, wd.sp_drain_rate.rate as i32, wd.sp_drain_rate.per as i32);
            thp += hp;
            tsp += sp;
        } else {
            thp += at(&wd.hp_drain_race, race) + at(&wd.hp_drain_race, RC_ALL);
            tsp += at(&wd.sp_drain_race, race) + at(&wd.sp_drain_race, RC_ALL);
        }
    }
    if thp == 0 && tsp == 0 {
        return;
    }
    crate::status::hp::status_heal(c, sd.aid, thp as i64, tsp as i64, 0, if conf::show_hp_sp_drain() { 3 } else { 1 });
}

/// battle.cpp battle_consume_ammo: one ammunition (skills: their amount) is used.
pub fn battle_consume_ammo(c: &Ctx, aid: u32, skill: u16, lv: u16) {
    if conf::arrow_decrement() == 0 {
        return;
    }
    let mut qty = 1;
    if skill != 0 {
        qty = skill_get_ammo_qty(skill, lv);
        if qty == 0 {
            qty = 1;
        }
        if skill == NW_MAGAZINE_FOR_ONE && crate::inventory::st(c, aid).map(|s| s.weapontype1 as i32 == W_GATLING).unwrap_or(false) {
            qty += 4;
        }
    }
    let idx = crate::inventory::st(c, aid).and_then(|s| s.equip_index.get(EQI_AMMO).copied()).unwrap_or(-1);
    if idx >= 0 {
        // Qty check should have been done in skill_check_condition (LOG_TYPE_CONSUME)
        crate::inventory::pc_delitem(c, aid, idx as i32, qty, 0, 1, crate::map_log::LOG_TYPE_CONSUME);
    }
    state::with(c, aid, |b| b.arrow_atk = false);
}

/// battle.cpp battle_weapon_attack: a basic physical attack (through unit_attack_timer). Returns the damage_lv.
pub fn battle_weapon_attack(c: &Ctx, src_id: u32, target_id: u32, tick: Tick, flag: i32) -> u8 {
    let (Some(src), Some(target)) = (unit::get(c, src_id), unit::get(c, target_id)) else { return ATK_NONE };
    if !src.on_map || !target.on_map {
        return ATK_NONE;
    }
    let sd = bl::sd(c, &src);
    let sstatus = bl::status(c, &src);
    let tstatus = bl::status(c, &target);
    // Avoid sc checks when there's none to check for. [Skotlex] (`sc` / `tsc` become nullptr when empty now; the
    // later reads through them are live)
    let sc_on = bl::sc_nonempty(c, &src).is_some();
    let tsc_on = bl::sc_nonempty(c, &target).is_some();
    let (src0, target0) = (src.clone(), target.clone());
    let scl = |c: &Ctx| if sc_on { bl::sc(c, &src0) } else { None };
    let tscl = |c: &Ctx| if tsc_on { bl::sc(c, &target0) } else { None };
    let tsd = bl::sd(c, &target);
    if let Some(sd) = &sd {
        let w = sd.ch.weapon as i32;
        let arrow_atk = w == W_BOW || (W_REVOLVER..=W_GRENADE).contains(&w);
        state::with(c, sd.aid, |b| b.arrow_atk = arrow_atk);
        if arrow_atk {
            match bl::equip(c, sd, EQI_AMMO) {
                None if sd.it.equip_index.get(EQI_AMMO).copied().unwrap_or(-1) < 0 => {
                    let wt1 = sd.it.weapontype1 as i32;
                    if wt1 > W_KATAR && wt1 < W_HUUMA {
                        crate::skill::skill_fail_ex(c, &src, 0, USESKILL_FAIL_NEED_MORE_BULLET, 0, 0);
                    } else {
                        clif_arrow_fail(c, &src, ARROWFAIL_NO_AMMO);
                    }
                    return ATK_NONE;
                }
                Some((_, data)) => {
                    // Ammo check by Ishizu-chan
                    use crate::db::items::{AMMO_ARROW, AMMO_BULLET};
                    match w {
                        W_BOW => {
                            if data.subtype != AMMO_ARROW {
                                clif_arrow_fail(c, &src, ARROWFAIL_NO_AMMO);
                                return ATK_NONE;
                            }
                        }
                        W_REVOLVER | W_RIFLE | W_GATLING | W_SHOTGUN | W_GRENADE => {
                            // (RENEWAL: grenades use bullets too)
                            if data.subtype != AMMO_BULLET {
                                crate::skill::skill_fail_ex(c, &src, 0, USESKILL_FAIL_NEED_MORE_BULLET, 0, 0);
                                return ATK_NONE;
                            }
                        }
                        _ => {}
                    }
                }
                None => {}
            }
        }
    }
    if let Some(s) = scl(c).filter(|s| !s.empty()) {
        if s.has(SC_CLOAKING) && s.v4(SC_CLOAKING) & 2 == 0 {
            sc::status_change_end(c, src.id, SC_CLOAKING, 0);
        } else if s.has(SC_CLOAKINGEXCEED) && s.v4(SC_CLOAKINGEXCEED) & 2 == 0 {
            sc::status_change_end(c, src.id, SC_CLOAKINGEXCEED, 0);
        } else if let Some(mut e) = s.get(SC_NEWMOON).cloned() {
            e.val2 -= 1;
            sc::put_sce(c, &e);
            if e.val2 <= 0 {
                sc::status_change_end(c, src.id, SC_NEWMOON, 0);
            }
        }
    }
    if let Some(ts) = tscl(c) {
        if ts.has(SC_AUTOCOUNTER) && super::mobif::status_check_skilluse(c, Some(&target), Some(&src), KN_AUTOCOUNTER, 1) {
            let dir = crate::map::calc_dir(&target, src.x, src.y);
            let t_dir = crate::unit::getdir(&target);
            let dist = crate::path::distance((src.x - target.x) as i32, (src.y - target.y) as i32);
            if dist <= 0 || (bl::map_check_dir(dir as i32, t_dir as i32) == 0 && dist <= tstatus.rhw.range as i32 + 1) {
                let skill_lv = ts.v1(SC_AUTOCOUNTER) as u16;
                crate::skill::clif::clif_skillcastcancel(c, &target); // Remove the casting bar. [Skotlex]
                clif_damage(c, &src, &target, tick, sstatus.amotion as i32, 1, 0, 1, DMG_NORMAL, 0, false); // Display MISS.
                sc::status_change_end(c, target.id, SC_AUTOCOUNTER, 0);
                let (t, s) = (unit::get(c, target.id).unwrap_or(target.clone()), unit::get(c, src.id).unwrap_or(src.clone()));
                crate::skill::attack::skill_attack(c, BF_WEAPON, &t, &t, &s, KN_AUTOCOUNTER, skill_lv, tick, 0);
                return ATK_BLOCK;
            }
        }
    }
    // Poison React counter activates on attacks from poison-element enemies as well as normal poison attacks
    if tsc_on && ((src.kind != BL_PC && sstatus.def_ele as i32 == ELE_POISON) || sstatus.rhw.ele as i32 == ELE_POISON) {
        if let Some(mut sce) = sc::get_sce(c, target.id, SC_POISONREACT).filter(|e| e.val4 == 0) {
            // Next normal attack will receive a damage boost
            sce.val4 = 1;
            sc::put_sce(c, &sce);
            // The target will start attacking instead of the source
            super::unit_attack::unit_attack(c, target.id, src.id, 0);
            return ATK_BLOCK;
        }
    }
    if let Some(e) = tscl(c).and_then(|ts| ts.get(SC_BLADESTOP_WAIT).cloned()) {
        if src.kind == BL_PC || crate::path::check_distance((src.x - target.x) as i32, (src.y - target.y) as i32, 2) {
            let skill_lv = e.val1;
            let mut duration = skill_get_time2(MO_BLADESTOP, skill_lv as u16) as i64;
            if bl::class_type(c, &src) == CLASS_BOSS {
                duration = 2000; // Only lasts 2 seconds for Boss monsters
            }
            sc::status_change_end(c, target.id, SC_BLADESTOP_WAIT, 0);
            let lv = if sd.is_some() { bl::checkskill(c, sd.as_ref(), MO_BLADESTOP) } else { 5 };
            if sc::sc_start4(c, Some(&src), &src, SC_BLADESTOP, 100, lv, 0, 0, target.id as i32, duration) {
                // Target locked.
                clif_damage(c, &src, &target, tick, sstatus.amotion as i32, 1, 0, 1, DMG_NORMAL, 0, false); // Display MISS.
                let t = unit::get(c, target.id).unwrap_or(target.clone());
                clif_bladestop(c, &t, src.id, true);
                let s = unit::get(c, src.id).unwrap_or(src.clone());
                sc::sc_start4(c, Some(&s), &t, SC_BLADESTOP, 100, skill_lv, 0, 0, src.id as i32, duration);
                return ATK_BLOCK;
            }
        }
    }
    if let Some(sd) = &sd {
        let skillv = bl::checkskill(c, Some(sd), MO_TRIPLEATTACK);
        if skillv > 0 {
            let mut triple_rate = 30; // Base Rate (RENEWAL)
            if let Some(e) = scl(c).and_then(|s| s.get(SC_SKILLRATE_UP).cloned()) {
                if e.val1 == MO_TRIPLEATTACK as i32 {
                    triple_rate += triple_rate * e.val2 / 100;
                    sc::status_change_end(c, src.id, SC_SKILLRATE_UP, 0);
                }
            }
            if ((c.rnd() % 100) as i32) < triple_rate {
                // Need to apply canact_tick here because it doesn't go through skill_castend_id
                if let Some(mut u) = unit::get(c, src.id) {
                    let delay = crate::skill::castfix::skill_delayfix(c, &u, MO_TRIPLEATTACK, skillv as u16) as Tick;
                    u.ud.canact_tick = (tick + delay).max(u.ud.canact_tick);
                    unit::save(c, &u);
                }
                let s = unit::get(c, src.id).unwrap_or(src.clone());
                if crate::skill::attack::skill_attack(c, BF_WEAPON, &s, &s, &target, MO_TRIPLEATTACK, skillv as u16, tick, 0) != 0 {
                    return ATK_DEF;
                }
                return ATK_MISS;
            }
        }
    }
    if let Some(s) = scl(c) {
        if let Some(mut e) = s.get(SC_SACRIFICE).cloned() {
            let skill_lv = e.val1 as u16;
            e.val2 -= 1;
            sc::put_sce(c, &e);
            if e.val2 <= 0 {
                sc::status_change_end(c, src.id, SC_SACRIFICE, 0);
            }
            // We need to calculate the DMG before the hp reduction, because it can kill the source.
            // For further information: bugreport:4950
            let s = unit::get(c, src.id).unwrap_or(src.clone());
            // (damage_lv)skill_attack(…): the int64 damage is truncated to the uint8 enum
            let ret_val = crate::skill::attack::skill_attack(c, BF_WEAPON, &s, &s, &target, PA_SACRIFICE, skill_lv, tick, 0) as u8;
            let max_hp = bl::status(c, &src).max_hp;
            crate::status::hp::status_zap(c, src.id, (max_hp.wrapping_mul(9) / 100) as i64, 0, 0); // Damage to self is always 9%
            if ret_val == ATK_NONE {
                return ATK_MISS;
            }
            return ret_val;
        }
        if let Some(e) = s.get(SC_MAGICALATTACK) {
            let s = unit::get(c, src.id).unwrap_or(src.clone());
            if crate::skill::attack::skill_attack(c, BF_MAGIC, &s, &s, &target, NPC_MAGICALATTACK, e.val1 as u16, tick, 0) != 0 {
                return ATK_DEF;
            }
            return ATK_MISS;
        }
        if let Some(e) = s.get(SC_GT_ENERGYGAIN) {
            let mut spheres = 5;
            if let Some(r) = s.get(SC_RAISINGDRAGON) {
                spheres += r.val1;
            }
            if sd.is_some() && ((c.rnd() % 100) as i32) < e.val2 {
                crate::skill::state::pc_addspiritball(c, src.id, skill_get_time2(SR_GENTLETOUCH_ENERGYGAIN, e.val1 as u16), spheres);
            }
        }
    }
    if let Some(ts) = tscl(c) {
        if let Some(e) = ts.get(SC_GT_ENERGYGAIN) {
            let mut spheres = 5;
            if let Some(r) = ts.get(SC_RAISINGDRAGON) {
                spheres += r.val1;
            }
            if tsd.is_some() && ((c.rnd() % 100) as i32) < e.val2 {
                crate::skill::state::pc_addspiritball(c, target.id, skill_get_time2(SR_GENTLETOUCH_ENERGYGAIN, e.val1 as u16), spheres);
            }
        }
    }
    if let Some(e) = tscl(c).and_then(|ts| ts.get(SC_KAAHI).cloned()) {
        let tstatus = bl::status(c, &target);
        if tstatus.hp < tstatus.max_hp && crate::status::hp::status_charge(c, target.id, 0, e.val3 as i64) != 0 {
            let mut hp_heal = tstatus.max_hp.wrapping_sub(tstatus.hp) as i32;
            if hp_heal > e.val2 {
                hp_heal = e.val2;
            }
            if hp_heal != 0 {
                crate::status::hp::status_heal(c, target.id, hp_heal as i64, 0, 0, 2);
            }
        }
    }
    let src = unit::get(c, src.id).unwrap_or(src);
    let target = unit::get(c, target.id).unwrap_or(target);
    let mut wd = calc::battle_calc_attack(c, BF_WEAPON, &src, &target, 0, 0, flag);
    let mut vellum_damage = false;
    if let Some(sd) = bl::sd(c, &src) {
        if wd.damage + wd.damage2 > 0 {
            let target_now = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            if battle_vellum_damage(c, &sd, &target_now, &mut wd) {
                vellum_damage = true;
            }
        }
    }
    if let Some(s) = scl(c).filter(|s| !s.empty()) {
        if s.has(SC_EXEEDBREAK) {
            sc::status_change_end(c, src.id, SC_EXEEDBREAK, 0);
        }
        if let Some(e) = s.get(SC_SPELLFIST).filter(|_| !vellum_damage) {
            if crate::status::hp::status_charge(c, src.id, 0, 20) != 0 {
                if !calc::is_infinite_defense(c, &target, wd.flag) {
                    let s_now = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                    let t_now = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                    let ad = calc::battle_calc_attack(c, BF_MAGIC, &s_now, &t_now, e.val2 as u16, e.val3 as u16, flag | BF_SHORT);
                    wd.damage = ad.damage;
                } else {
                    wd.damage = 1;
                }
                // Double the damage for multiple hits (DAMAGE_DIV_FIX)
                if wd.div_ < 0 {
                    wd.div_ = -wd.div_;
                    wd.damage /= wd.div_ as i64;
                }
                wd.damage *= wd.div_ as i64;
            } else {
                sc::status_change_end(c, src.id, SC_SPELLFIST, 0);
            }
        }
        if s.has(SC_GIANTGROWTH) && wd.flag & BF_SHORT != 0 && ((c.rnd() % 100) as i32) < s.v2(SC_GIANTGROWTH) && !calc::is_infinite_defense(c, &target, wd.flag) && !vellum_damage {
            wd.damage += wd.damage * 150 / 100; // 2.5 times damage
        }
        if s.has(SC_VIGOR) && wd.flag & BF_SHORT != 0 && !calc::is_infinite_defense(c, &target, wd.flag) && !vellum_damage {
            let mut m = 100 + s.v1(SC_VIGOR) * 15;
            let race = tstatus.race as i32;
            if race == RC_DEMIHUMAN || race == RC_ANGEL {
                m += s.v1(SC_VIGOR) * 10;
            }
            wd.damage += wd.damage * m as i64 / 100;
        }
        if conf::arrow_decrement() != 0 && s.has(SC_FEARBREEZE) && s.v4(SC_FEARBREEZE) > 0 {
            if let Some(sd) = &sd {
                let idx = sd.it.equip_index.get(EQI_AMMO).copied().unwrap_or(-1);
                if idx >= 0 {
                    if let Some(row) = crate::inventory::inv_get(c, sd.aid, idx as usize) {
                        if row.item.amount as i32 >= s.v4(SC_FEARBREEZE) {
                            crate::inventory::pc_delitem(c, sd.aid, idx as i32, s.v4(SC_FEARBREEZE), 0, 1, crate::map_log::LOG_TYPE_CONSUME);
                            if let Some(mut e) = sc::get_sce(c, src.id, SC_FEARBREEZE) {
                                e.val4 = 0;
                                sc::put_sce(c, &e);
                            }
                        }
                    }
                }
            }
        }
    }
    if let Some(sd) = &sd {
        if state::get(c, sd.aid).arrow_atk {
            // Consume arrow.
            battle_consume_ammo(c, sd.aid, 0, 0);
        }
    }
    let damage = wd.damage + wd.damage2;
    if damage > 0 && src.id != target.id {
        if let Some(e) = scl(c).and_then(|s| s.get(SC_DUPLELIGHT).cloned()) {
            if wd.flag & BF_SHORT != 0 {
                // Activates only from regular melee damage. Success chance is separate for both duple light attacks.
                // (uint16 duple_rate; rAthena rolls libc rand() here: the context RNG in the module)
                let duple_rate = (10 + 2 * e.val1) as u16 as i32;
                if ((c.rnd() % 100) as i32) < duple_rate {
                    let (s, t) = (unit::get(c, src.id).unwrap_or(src.clone()), unit::get(c, target.id).unwrap_or(target.clone()));
                    crate::skill::castend::skill_castend_damage_id(c, &s, &t, AB_DUPLELIGHT_MELEE, e.val1 as u16, tick, flag | crate::skill::SD_LEVEL);
                }
                if ((c.rnd() % 100) as i32) < duple_rate {
                    let (s, t) = (unit::get(c, src.id).unwrap_or(src.clone()), unit::get(c, target.id).unwrap_or(target.clone()));
                    crate::skill::castend::skill_castend_damage_id(c, &s, &t, AB_DUPLELIGHT_MAGIC, e.val1 as u16, tick, flag | crate::skill::SD_LEVEL);
                }
            }
        }
    }
    let src = unit::get(c, src.id).unwrap_or(src);
    let target = unit::get(c, target.id).unwrap_or(target);
    clif_damage(c, &src, &target, tick, wd.amotion, wd.dmotion, wd.damage, wd.div_, wd.type_, wd.damage2, wd.isspdamage);
    if let Some(sd) = &sd {
        if sd.st.bonus.splash_range > 0 && damage > 0 {
            crate::skill::castend::skill_castend_damage_id(c, &src, &target, 0, 1, tick, 0);
        }
    }
    let mut is_norm_attacked = false;
    if target.kind == BL_SKILL && damage > 0 {
        // Blast Mine knockback, Wall of Thorn (battle.cpp:7406-7417, skillunit)
        crate::skillunit::battle_damage_unit(c, &src, &target);
    } else if target.kind == BL_MOB {
        // Monsters trigger a special event when they are hit in Berserk state by a normal attack within
        // [attack range+1]; certain AI types only switch target on this trigger
        if super::mobif::skillstate(c, &target) == super::mobif::MSS_BERSERK && super::mobif::target_id(c, &target) != src.id {
            is_norm_attacked = conf::mob_ai() & 0x4 != 0 || crate::path::check_distance((src.x - target.x) as i32, (src.y - target.y) as i32, tstatus.rhw.range as i32 + 1);
        }
    }
    let target_devoted = tscl(c).map(|s| s.has(SC_DEVOTION)).unwrap_or(false);
    if !target_devoted && !vellum_damage && crate::skill::attack::skill_check_shadowform(c, &target, damage, wd.div_) {
        let target_now = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        if !sc::status_isdead(c, &target_now) {
            crate::skill::effect::skill_additional_effect(c, &src, &target_now, 0, 0, wd.flag, wd.dmg_lv, tick);
        }
        if wd.dmg_lv > ATK_BLOCK {
            let target_now = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            crate::skill::effect::skill_counter_additional_effect(c, &src, &target_now, 0, 0, wd.flag, tick);
        }
    } else {
        battle_delay_damage(c, tick, wd.amotion, &src, &target, wd.flag, 0, 0, damage, wd.dmg_lv, wd.div_, true, wd.isspdamage, is_norm_attacked);
    }
    if let Some(ts) = tscl(c) {
        if let Some(sce) = ts.get(SC_DEVOTION) {
            let d_bl = unit::get(c, sce.val1 as u32);
            let valid = d_bl
                .as_ref()
                .map(|d| super::fix::devotes(c, d, sce.val2, &target) && crate::path::check_distance((target.x - d.x) as i32, (target.y - d.y) as i32, sce.val3))
                .unwrap_or(false);
            match d_bl.filter(|_| valid) {
                Some(d_bl) => {
                    // Only trigger if the devoted player was hit
                    if damage > 0 {
                        let mut devotion_damage = damage;
                        // The devoting player needs to stand up
                        if d_bl.kind == BL_PC && crate::inventory::pc_issit(c, d_bl.id) {
                            crate::pc::setstand(c, d_bl.id, true);
                            crate::skill::iface::skill_sit(c, d_bl.id, false);
                        }
                        // Needed to check the devotion master for Rebound Shield status.
                        if let Some(e) = bl::sc(c, &d_bl).and_then(|s| s.get(SC_REBOUND_S).cloned()) {
                            devotion_damage -= devotion_damage * e.val2 as i64 / 100;
                        }
                        let d_now = unit::get(c, d_bl.id).unwrap_or(d_bl);
                        clif_damage(c, &d_now, &d_now, c.tick(), wd.amotion, wd.dmotion, devotion_damage, 1, DMG_NORMAL, 0, false);
                        battle_fix_damage(c, Some(&src), &d_now, devotion_damage, 0, CR_DEVOTION);
                    }
                }
                None => {
                    sc::status_change_end(c, target.id, SC_DEVOTION, 0);
                }
            }
        }
        if target.kind == BL_PC && wd.flag & BF_SHORT != 0 && ts.has(SC_CIRCLE_OF_FIRE_OPTION) {
            // s_elemental_data *ed = ((TBL_PC*)target)->ed: the elemental strikes back at the attacker at the
            // option's level (tsc->getSCE(SC_CIRCLE_OF_FIRE_OPTION)->val1, read as it is now)
            let ed = crate::elemental::data::of_master(c, target.id).and_then(|ed| unit::get(c, ed.id));
            if let Some(ed) = ed {
                let lv = sc::get_sce(c, target.id, SC_CIRCLE_OF_FIRE_OPTION).map(|e| e.val1).unwrap_or(0);
                let target_now = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                let amotion = crate::skills::novice::status_get_amotion(c, &src);
                crate::skill::clif::clif_skill_damage(c, &ed, &target_now, tick, amotion, 0, crate::skill::clif::DMGVAL_IGNORE, 1, EL_CIRCLE_OF_FIRE, lv, DMG_SINGLE);
                let lv = sc::get_sce(c, target.id, SC_CIRCLE_OF_FIRE_OPTION).map(|e| e.val1).unwrap_or(0);
                let src_now = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                crate::skill::attack::skill_attack(c, BF_WEAPON, &ed, &ed, &src_now, EL_CIRCLE_OF_FIRE, lv as u16, tick, wd.flag);
            }
        }
        if let Some(e) = ts.get(SC_WATER_SCREEN_OPTION) {
            if let Some(e_bl) = unit::get(c, e.val1 as u32) {
                if !sc::status_isdead(c, &e_bl) {
                    clif_damage(c, &e_bl, &e_bl, tick, 0, 0, damage, wd.div_, DMG_NORMAL, 0, false);
                    battle_fix_damage(c, Some(&src), &e_bl, damage, 0, EL_WATER_SCREEN);
                }
            }
        }
    }
    if let Some(e) = scl(c).and_then(|s| s.get(SC_AUTOSPELL).cloned()) {
        if ((c.rnd() % 100) as i32) < e.val4 {
            let skill_id = e.val2 as u16;
            let mut skill_lv = e.val3 as u16;
            let mut i = (c.rnd() % 100) as i32;
            if scl(c).and_then(|s| s.get(SC_SPIRIT).map(|sp| sp.val2 == SL_SAGE as i32)).unwrap_or(false) {
                i = 0; // Max chance, no skill_lv reduction. [Skotlex]
            }
            // reduction only for skill_lv > 1
            if skill_lv > 1 {
                if i >= 50 {
                    skill_lv /= 2;
                } else if i >= 15 {
                    skill_lv -= 1;
                }
            }
            let sp = skill_get_sp(skill_id, skill_lv) * 2 / 3;
            if crate::status::hp::status_charge(c, src.id, 0, sp as i64) != 0 {
                let (s, t) = (unit::get(c, src.id).unwrap_or(src.clone()), unit::get(c, target.id).unwrap_or(target.clone()));
                match skill_get_casttype(skill_id) {
                    CastType::Ground => {
                        crate::skill::castend::skill_castend_pos2(c, &s, t.x, t.y, skill_id, skill_lv, tick, flag);
                    }
                    CastType::NoDamage => {
                        crate::skill::castend::skill_castend_nodamage_id(c, &s, &t, skill_id, skill_lv, tick, flag);
                    }
                    CastType::Damage => {
                        crate::skill::castend::skill_castend_damage_id(c, &s, &t, skill_id, skill_lv, tick, flag);
                    }
                }
                if let Some(mut u) = unit::get(c, src.id) {
                    let autospell_tick = crate::skill::castfix::skill_delayfix(c, &u, skill_id, skill_lv) as Tick;
                    if u.ud.canact_tick - (tick + autospell_tick) < 0 {
                        u.ud.canact_tick = (tick + autospell_tick).max(u.ud.canact_tick);
                        unit::save(c, &u);
                        if crate::conf::display_status_timers() && sd.is_some() {
                            crate::status::clif::status_change(c, &u, EFST_POSTDELAY, 1, autospell_tick, 0, 0, 0);
                        }
                    }
                }
            }
        }
    }
    if let Some(sd) = &sd {
        if let Some(e) = scl(c).and_then(|s| s.get(SC__AUTOSHADOWSPELL).cloned()) {
            if wd.flag & BF_WEAPON != 0 && ((c.rnd() % 100) as i32) < e.val3 {
                let r_skill = e.val1 as u16;
                let sk_idx = if r_skill != 0 { skill_get_index(r_skill) } else { 0 };
                let slot = crate::skilltree::Skills::load(c, sd.aid).map(|sk| sk.at(sk_idx)).unwrap_or_default();
                if r_skill != 0 && sk_idx != 0 && slot.id != 0 && slot.flag == SKILL_FLAG_PLAGIARIZED as u8 && r_skill != AL_HOLYLIGHT && r_skill != PR_MAGNUS {
                    let r_lv = e.val2 as u16;
                    let mut type_ = Some(skill_get_casttype(r_skill));
                    if type_ == Some(CastType::Ground) {
                        let unit_flag = |uf: u8| skill_get_unit_flag(r_skill, &[uf]);
                        if crate::map::BL_PC & crate::skill::conf::skill_reiteration() == 0 && unit_flag(UF_NOREITERATION) {
                            type_ = None;
                        }
                        if crate::map::BL_PC & crate::skill::conf::skill_nofootset() != 0 && unit_flag(UF_NOFOOTSET) {
                            type_ = None;
                        }
                        let maxcount = skill_get_maxcount(r_skill, r_lv);
                        if crate::map::BL_PC & crate::skill::conf::land_skill_limit() != 0 && maxcount > 0 {
                            // unit_skillunit_maxcount(sd->ud, r_skill, maxcount): one less for each of the caster's
                            // units of r_skill (skill units: skillunit agent; none exist yet, maxcount stays)
                            if maxcount == 0 {
                                type_ = None;
                            }
                        }
                        if type_ != Some(CastType::Ground) {
                            let s = unit::get(c, src.id).unwrap_or(src.clone());
                            crate::skill::clif::skill_fail(c, &s, r_skill);
                            return wd.dmg_lv;
                        }
                    }
                    if !crate::skill::state::get(c, sd.aid).autocast {
                        crate::skill::state::with(c, sd.aid, |st| st.autocast = true);
                        let s = unit::get(c, src.id).unwrap_or(src.clone());
                        crate::skill::condition::skill_consume_requirement(c, &s, r_skill, r_lv, 3);
                        let (s, t) = (unit::get(c, src.id).unwrap_or(src.clone()), unit::get(c, target.id).unwrap_or(target.clone()));
                        match type_ {
                            Some(CastType::Ground) => {
                                crate::skill::castend::skill_castend_pos2(c, &s, t.x, t.y, r_skill, r_lv, tick, flag);
                            }
                            Some(CastType::NoDamage) => {
                                crate::skill::castend::skill_castend_nodamage_id(c, &s, &t, r_skill, r_lv, tick, flag);
                            }
                            Some(CastType::Damage) => {
                                crate::skill::castend::skill_castend_damage_id(c, &s, &t, r_skill, r_lv, tick, flag);
                            }
                            None => {}
                        }
                    }
                    crate::skill::state::with(c, sd.aid, |st| st.autocast = false);
                    if let Some(mut u) = unit::get(c, src.id) {
                        let delay = crate::skill::castfix::skill_delayfix(c, &u, r_skill, r_lv) as Tick;
                        u.ud.canact_tick = (tick + delay).max(u.ud.canact_tick);
                        unit::save(c, &u);
                        let delay = crate::skill::castfix::skill_delayfix(c, &u, r_skill, r_lv) as Tick;
                        crate::status::clif::status_change(c, &u, EFST_POSTDELAY, 1, delay, 0, 0, 1);
                    }
                }
            }
        }
        if let Some(e) = scl(c).and_then(|s| s.get(SC_FALLINGSTAR).cloned()) {
            // (rAthena rolls libc rand() here: the context RNG in the module)
            if wd.flag & BF_WEAPON != 0 && ((c.rnd() % 100) as i32) < e.val2 {
                crate::skill::state::with(c, sd.aid, |st| st.autocast = true);
                if crate::status::hp::status_charge(c, src.id, 0, skill_get_sp(SJ_FALLINGSTAR_ATK, e.val1 as u16) as i64) != 0 {
                    let s = unit::get(c, src.id).unwrap_or(src.clone());
                    crate::skill::castend::skill_castend_nodamage_id(c, &s, &s, SJ_FALLINGSTAR_ATK, e.val1 as u16, tick, flag);
                }
                crate::skill::state::with(c, sd.aid, |st| st.autocast = false);
            }
        }
        if scl(c).is_some() {
            weapon_attack_autocasts(c, sd, &src, &target, tick, flag, &scl);
        }
        if wd.flag & BF_WEAPON != 0 && src.id != target.id && damage > 0 {
            let tstatus = bl::status(c, &target);
            let target_now = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            if crate::conf::left_cardfix_to_right() {
                battle_drain(c, sd, &target_now, wd.damage, wd.damage, tstatus.race as i32, tstatus.class_ as i32);
            } else {
                battle_drain(c, sd, &target_now, wd.damage, wd.damage2, tstatus.race as i32, tstatus.class_ as i32);
            }
        }
    }
    if sd.is_some() && wd.flag & BF_LONG != 0 {
        if let Some(e) = tscl(c).and_then(|ts| ts.get(SC_WINDSIGN).cloned()) {
            // (rAthena rolls libc rand() here: the context RNG in the module)
            if ((c.rnd() % 100) as i32) < e.val2 {
                crate::status::hp::status_heal(c, src.id, 0, 0, 1, 0);
            }
        }
    }
    wd.dmg_lv
}

/// One autocast of battle_weapon_attack's 4th-job block: `sd->state.autocast = 1`, the cast, battle_autocast_aftercast,
/// `sd->state.autocast = 0`.
fn autocast(c: &Ctx, aid: u32, target: &Unit, skill_id: u16, skill_lv: u16, tick: Tick, flag: i32, ground: bool) {
    crate::skill::state::with(c, aid, |st| st.autocast = true);
    let (s, t) = (unit::get(c, aid), unit::get(c, target.id).unwrap_or_else(|| target.clone()));
    if let Some(s) = s {
        if ground {
            crate::skill::castend::skill_castend_pos2(c, &s, t.x, t.y, skill_id, skill_lv, tick, flag);
        } else {
            crate::skill::castend::skill_castend_damage_id(c, &s, &t, skill_id, skill_lv, tick, flag);
        }
        let s = unit::get(c, aid).unwrap_or(s);
        battle_autocast_aftercast(c, &s, skill_id, skill_lv, tick);
    }
    crate::skill::state::with(c, aid, |st| st.autocast = false);
}

/// battle_weapon_attack's `if (sc)` block of a player: Servant Weapon, Duple Light's Petitio, Abyss Force Weapon,
/// Auto Firing Launcher and the super elementals' supportive autocasts.
fn weapon_attack_autocasts(c: &Ctx, sd: &Sd, src: &Unit, target: &Unit, tick: Tick, flag: i32, scl: &dyn Fn(&Ctx) -> Option<sc::Sc>) {
    let aid = sd.aid;
    let chk = |id: u16| bl::checkskill(c, Some(sd), id) as u16;
    if let Some(e) = scl(c).and_then(|s| s.get(SC_SERVANTWEAPON).cloned()) {
        if crate::skill::state::get(c, aid).servantball > 0 && c.rnd_chance(5 * e.val1, 100) {
            let skill_lv = e.val1 as u16;
            crate::skill::state::with(c, aid, |st| st.autocast = true);
            crate::skill::state::pc_delservantball(c, aid, 1);
            if let Some(s) = unit::get(c, aid) {
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                crate::skill::castend::skill_castend_damage_id(c, &s, &t, DK_SERVANTWEAPON_ATK, skill_lv, tick, flag);
                let s = unit::get(c, aid).unwrap_or(s);
                battle_autocast_aftercast(c, &s, DK_SERVANTWEAPON_ATK, skill_lv, tick);
            }
            crate::skill::state::with(c, aid, |st| st.autocast = false);
        }
    }
    // TODO: Whats the official success chance? Is SP consumed for every autocast? [Rytech]
    if scl(c).map(|s| s.has(SC_DUPLELIGHT)).unwrap_or(false) && chk(CD_PETITIO) > 0 && c.rnd() % 100 < 20 {
        autocast(c, aid, target, CD_PETITIO, chk(CD_PETITIO), tick, flag, false);
    }
    if let Some(e) = scl(c).and_then(|s| s.get(SC_ABYSSFORCEWEAPON).cloned()) {
        if crate::skill::state::get(c, aid).abyssball > 0 && c.rnd_chance(25, 100) {
            let skill_lv = e.val1 as u16;
            crate::skill::state::with(c, aid, |st| st.autocast = true);
            crate::skill::state::pc_delabyssball(c, aid, 1);
            if let Some(s) = unit::get(c, aid) {
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                crate::skill::castend::skill_castend_damage_id(c, &s, &t, ABC_FROM_THE_ABYSS_ATK, skill_lv, tick, flag);
                let s = unit::get(c, aid).unwrap_or(s);
                battle_autocast_aftercast(c, &s, ABC_FROM_THE_ABYSS_ATK, skill_lv, tick);
            }
            crate::skill::state::with(c, aid, |st| st.autocast = false);
        }
    }
    // It has a success chance of triggering even tho the description says nothing about it.
    // TODO: Need to find out what the official success chance is. [Rytech]
    if scl(c).map(|s| s.has(SC_ABYSSFORCEWEAPON)).unwrap_or(false) && c.rnd() % 100 < 20 {
        autocast(c, aid, target, ABC_ABYSS_SQUARE, chk(ABC_ABYSS_SQUARE), tick, flag, true);
    }
    if let Some(e) = scl(c).and_then(|s| s.get(SC_AUTO_FIRING_LAUNCHER).cloned()) {
        // (skill, chance %) in rAthena's order for each level of the launcher
        let rolls: &[(u16, i32)] = match e.val1 {
            1 => &[(NW_BASIC_GRENADE, 6)],
            2 => &[(NW_BASIC_GRENADE, 7)],
            3 => &[(NW_HASTY_FIRE_IN_THE_HOLE, 3), (NW_BASIC_GRENADE, 8)],
            4 => &[(NW_HASTY_FIRE_IN_THE_HOLE, 5), (NW_BASIC_GRENADE, 9)],
            5 => &[(NW_GRENADES_DROPPING, 3), (NW_HASTY_FIRE_IN_THE_HOLE, 7), (NW_BASIC_GRENADE, 10)],
            _ => &[],
        };
        for &(skill_id, chance) in rolls {
            let skill_lv = chk(skill_id);
            if skill_lv > 0 && c.rnd_chance(chance, 100) {
                autocast(c, aid, target, skill_id, skill_lv, tick, flag, true);
            }
        }
    }
    // Autocasted skills from super elemental supportive buffs.
    for (t, skill_id) in [
        (SC_FLAMETECHNIC_OPTION, MG_FIREBOLT),
        (SC_COLD_FORCE_OPTION, MG_COLDBOLT),
        (SC_GRACE_BREEZE_OPTION, MG_LIGHTNINGBOLT),
        (SC_EARTH_CARE_OPTION, WZ_EARTHSPIKE),
        (SC_DEEP_POISONING_OPTION, SO_POISON_BUSTER),
    ] {
        if scl(c).map(|s| s.has(t)).unwrap_or(false) && c.rnd() % 100 < 7 {
            let t_now = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            battle_autocast_elembuff_skill(c, aid, &t_now, skill_id, tick, flag);
        }
    }
    let _ = src;
}

/// battle.cpp battle_damage_area (map_foreachinshootrange callback of Reflect Damage): deals the same damage to the
/// targets in the area.
pub fn battle_damage_area(c: &Ctx, bl: &Unit, tick: Tick, src: &Unit, amotion: i32, dmotion: i32, damage: i32) -> i32 {
    if bl::has_mode(c, bl, MD_SKILLIMMUNE) || bl::class_of(c, bl) == super::mobif::MOBID_EMPERIUM as i32 {
        return 0;
    }
    let src = unit::get(c, src.id).unwrap_or_else(|| src.clone());
    if bl.id != src.id && super::target::battle_check_target(c, &src, bl, BCT_ENEMY) > 0 {
        let damage = damage as i64;
        if src.kind == BL_PC {
            if let Some(ssd) = bl::sd(c, &src) {
                let st = bl::status(c, bl);
                battle_drain(c, &ssd, bl, damage, damage, st.race as i32, st.class_ as i32);
            }
        }
        if amotion != 0 {
            battle_delay_damage(c, tick, amotion, &src, bl, 0, CR_REFLECTSHIELD, 0, damage, ATK_DEF, 0, true, false, false);
        } else {
            battle_fix_damage(c, Some(&src), bl, damage, 0, LG_REFLECTDAMAGE);
        }
        let bl_now = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
        clif_damage(c, &bl_now, &bl_now, tick, amotion, dmotion, damage, 1, DMG_ENDURE, 0, false);
        crate::skill::effect::skill_additional_effect(c, &src, &bl_now, CR_REFLECTSHIELD, 1, BF_WEAPON | BF_SHORT | BF_NORMAL, ATK_DEF, tick);
    }
    0
}

/// battle.cpp battle_autocast_aftercast: the after-cast delay of an autocasted skill.
pub fn battle_autocast_aftercast(c: &Ctx, src: &Unit, skill_id: u16, skill_lv: u16, tick: Tick) {
    let Some(mut u) = unit::get(c, src.id) else { return };
    let autocast_tick = crate::skill::castfix::skill_delayfix(c, &u, skill_id, skill_lv) as Tick;
    if u.ud.canact_tick - (tick + autocast_tick) < 0 {
        u.ud.canact_tick = (tick + autocast_tick).max(u.ud.canact_tick);
        unit::save(c, &u);
        if crate::conf::display_status_timers() && src.kind == BL_PC {
            crate::status::clif::status_change(c, &u, EFST_POSTDELAY, 1, autocast_tick, 0, 0, 0);
        }
    }
}

/// battle.cpp battle_autocast_elembuff_skill: a super elemental's supportive buff autocasts its bolt.
pub fn battle_autocast_elembuff_skill(c: &Ctx, aid: u32, target: &Unit, skill_id: u16, tick: Tick, flag: i32) {
    let skill_lv = (crate::skill::checkskill(c, aid, skill_id) as u16).max(1);
    crate::skill::state::with(c, aid, |st| st.autocast = true);
    if crate::status::hp::status_charge(c, aid, 0, skill_get_sp(skill_id, skill_lv) as i64) != 0 {
        if let Some(s) = unit::get(c, aid) {
            crate::skill::castend::skill_castend_damage_id(c, &s, target, skill_id, skill_lv, tick, flag);
            let s = unit::get(c, aid).unwrap_or(s);
            battle_autocast_aftercast(c, &s, skill_id, skill_lv, tick);
        }
    }
    crate::skill::state::with(c, aid, |st| st.autocast = false);
}

/// clif.cpp clif_bladestop: ZC_BLADESTOP (0x01d1) to the area of `src`.
pub fn clif_bladestop(c: &Ctx, src: &Unit, target_id: u32, active: bool) {
    // one copy: status2's (status/sc_ext.rs)
    crate::status::sc_ext::clif_bladestop(c, src, target_id, active);
}
