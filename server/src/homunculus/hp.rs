//! status.cpp's HP / SP bookkeeping for a homunculus (BL_HOM): status_damage, status_heal and status_revive from the
//! point where they read the target's status (status/hp.rs runs the shared prefix and hands a homunculus over), the
//! max HP / SP / AP setters' status write, and status_natural_heal for a homunculus (map_foreachregen walks regen_db,
//! which holds homunculi from hom_alloc's map_addiddb to unit_free's map_deliddb).
//!
//! The homunculus' status is hd->battle_status (`hom_data` row), its regen hd->regen with the tick part in
//! `regen_tick_hp` / `regen_tick_sp`, its status changes hd->sc (the status-change rows of its block id).

use super::data;
use crate::status::consts::*;
use crate::status::defs::*;
use crate::status::sc;
use crate::timer::{self, Func};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

// ---------------------------------------------------------------------------------------------- damage

/// status.cpp status_damage (status.cpp:1528-1790) from `status = status_get_status_data(*target)` on, for a
/// homunculus. `dhp` is the caller's original HP damage (for the monster damage log), `hp` / `sp` / `ap` the values
/// after the shared prefix's absorbed-damage and non-SP-target adjustments. Returns hp + sp + ap.
#[allow(clippy::too_many_arguments)]
pub fn status_damage_hom(c: &Ctx, src: Option<&Unit>, target: u32, dhp: i64, hp: i32, sp: i32, ap: i32, walkdelay: Tick, flag: i32, skill_id: u16) -> i32 {
    let (mut hp, mut sp, mut ap, mut flag) = (hp, sp, ap, flag);
    let Some(hd) = data::get(c, target) else { return 0 };
    let Some(tu) = unit::get(c, target) else { return 0 };
    let status = hd.battle_status.clone();
    if hp as u32 >= status.hp {
        if flag & 2 != 0 {
            return 0;
        }
        hp = status.hp as i32;
    }
    if sp as u32 > status.sp {
        if flag & 2 != 0 {
            return 0;
        }
        sp = status.sp as i32;
    }
    if ap as u32 > status.ap {
        if flag & 2 != 0 {
            return 0;
        }
        ap = status.ap as i32;
    }
    // If no damage is dealt and the damage was passive
    if hp == 0 && sp == 0 && ap == 0 && flag & 1 != 0 {
        return 0;
    }
    if status.hp == 0 {
        flag |= 8;
    }
    let scl = sc::load(c, target);
    if hp != 0 && crate::conf::invincible_nodamage() && src.is_some() && scl.has(SC_INVINCIBLE) {
        hp = 1;
    }
    // If the damage is not passive
    if flag & 1 == 0 {
        // (a homunculus always has its status_change: an empty one ends nothing below)
        if !scl.empty() {
            for s in crate::db::misc::status::status_db().iter() {
                let t = s.type_;
                // For non-players, Wink Charm, Voice of Siren and Deep Sleep end only when damage was dealt
                if (t == SC_WINKCHARM || t == SC_VOICEOFSIREN || t == SC_DEEPSLEEP) && hp == 0 {
                    continue;
                }
                if sc::has_sce(c, target, t) && s.has_flag(SCF_REMOVEONDAMAGED) {
                    // A status change that gets broken by damage should still be considered when calculating if a
                    // status change can be applied or not (for the same attack).
                    let mut us = sc::unit_sc(c, target);
                    if unit::get(c, target).map(|u| u.sc.opt1 > OPT1_NONE).unwrap_or(false) && us.last_effect_timer == 0 {
                        us.last_effect_timer = timer::add(c, c.tick() + 10, Func::StatusClearLastEffect, target, 0);
                        us.last_effect = t;
                        sc::put_unit_sc(c, &us);
                    }
                    sc::status_change_end(c, target, t, 0);
                }
            }
            if let Some(mut sce) = sc::get_sce(c, target, SC_ENDURE) {
                if sce.val4 == 0 {
                    // Endure count is only reduced by non-players on non-gvg maps. val4 signals infinite endure.
                    if let Some(s) = src {
                        if s.kind != BL_PC && !crate::status::map_flag_gvg2(c, tu.map) && !crate::npc::mapflag(c, tu.map, crate::npc::MF_BATTLEGROUND) {
                            sce.val2 -= 1;
                            sc::put_sce(c, &sce);
                            if sce.val2 <= 0 {
                                sc::status_change_end(c, target, SC_ENDURE, 0);
                            }
                        }
                    }
                }
            }
            if sc::has_sce(c, target, SC_DANCING) && hp as u32 > status.max_hp / 4 {
                sc::status_change_end(c, target, SC_DANCING, 0);
            }
            if let Some(mut sce) = sc::get_sce(c, target, SC_CLOAKINGEXCEED) {
                sce.val2 -= 1;
                sc::put_sce(c, &sce);
                if sce.val2 <= 0 {
                    sc::status_change_end(c, target, SC_CLOAKINGEXCEED, 0);
                }
            }
            if let Some(mut sce) = sc::get_sce(c, target, SC_KAGEMUSYA) {
                sce.val3 -= 1;
                sc::put_sce(c, &sce);
                if sce.val3 <= 0 {
                    sc::status_change_end(c, target, SC_KAGEMUSYA, 0);
                }
            }
        }
        // (pc_bonus_script_clear: players only)
        crate::skill::unit::unit_skillcastcancel(c, target, 2);
        // Remember last time the unit was hit by a source
        if src.is_some() {
            if let Some(mut u) = unit::get(c, target) {
                u.ud.dmg_tick = c.tick();
                unit::save(c, &u);
            }
        }
    }
    // (exp_calc_type 1 logs the real damage: exp_calc_type is 0)
    let Some(mut hd) = data::get(c, target) else { return 0 };
    hd.battle_status.hp = hd.battle_status.hp.wrapping_sub(hp as u32);
    hd.battle_status.sp = hd.battle_status.sp.wrapping_sub(sp as u32);
    hd.battle_status.ap = hd.battle_status.ap.wrapping_sub(ap as u32);
    // Coma
    if flag & 16 != 0 {
        hd.battle_status.hp = 1;
        hd.battle_status.sp = 1;
        if sp == 0 {
            sp = 1; // To make sure the status bar is updated
        }
    }
    data::put(c, &hd);
    let new_hp = hd.battle_status.hp;
    if hp != 0 && new_hp != 0 {
        if sc::has_sce(c, target, SC_BERSERK) && new_hp <= 100 {
            sc::status_change_end(c, target, SC_BERSERK, 0);
        }
        if sc::has_sce(c, target, SC_RAISINGDRAGON) && new_hp <= 1000 {
            sc::status_change_end(c, target, SC_RAISINGDRAGON, 0);
        }
        if sc::has_sce(c, target, SC_SATURDAYNIGHTFEVER) && new_hp <= 100 {
            sc::status_change_end(c, target, SC_SATURDAYNIGHTFEVER, 0);
        }
    }
    // Need to pass original HP damage for the mob damage log
    let dhp = dhp.clamp(i32::MIN as i64, i32::MAX as i64);
    // case BL_HOM: hom_heal(*hd, hp != 0, sp != 0)
    super::hom_heal(c, target, hp != 0, sp != 0);
    // Normal attack damage is logged in the monster's dmglog as attack damage
    // This counts as exp tap and is used for determining the MVP
    if let Some(s) = src {
        if s.kind == BL_MOB && skill_id == 0 {
            if let Some(tu) = unit::get(c, target) {
                crate::mob::dead::mob_log_damage(c, s.id, &tu, 0, dhp);
            }
        }
    }
    // (the disguise rule: players only)
    let status_hp = data::get(c, target).map(|h| h.battle_status.hp).unwrap_or(0);
    if status_hp != 0 || flag & 8 != 0 {
        // Still lives or has been dead before this damage.
        if walkdelay > 0 && !crate::status::status_isendure(c, target, c.tick(), false) {
            crate::battle::unit_attack::unit_set_walkdelay(c, target, c.tick(), walkdelay, 0, skill_id);
        }
        return hp + sp + ap;
    }
    data::with(c, target, |hd| hd.battle_status.hp = 0);
    // NOTE: These dead functions should return: 0: Death cancelled, auto-revived. Non-zero: Standard death. Clear
    // status, cancel move/attack, etc. &2: Remove object from map. &4: Delete object from memory.
    let dflag = super::hom_dead(c, target);
    if dflag == 0 {
        return hp + sp + ap; // Death cancelled.
    }
    // Normal death
    if crate::skillunit::conf::clear_unit_ondeath() != 0 && crate::skillunit::conf::clear_unit_ondeath() & tu.kind != 0 {
        crate::skillunit::skill_clear_unitgroup(c, target);
    }
    // target->type & BL_REGEN: reset regen ticks (a homunculus has no sregen / ssregen)
    data::with(c, target, |hd| {
        hd.regen_tick_hp = 0;
        hd.regen_tick_sp = 0;
    });
    let scl = sc::load(c, target);
    let tmap = unit::get(c, target).map(|u| u.map).unwrap_or(tu.map);
    if let Some(kaizel) = scl.get(SC_KAIZEL).filter(|_| !crate::status::map_flag_gvg2(c, tmap)) {
        // flag&8 = disable Kaizel
        let time = crate::db::skills::skill_get_time2(crate::db::skills::ids::SL_KAIZEL, kaizel.val1 as u16) as i64;
        // (Osiris Card's full recovery: players only)
        crate::status::hp::status_revive(c, target, kaizel.val2 as u8, 0, 0);
        sc::status_change_clear(c, target, 0);
        if let Some(t) = unit::get(c, target) {
            crate::skill::clif::clif_skill_nodamage(c, Some(&t), &t, crate::db::skills::ids::ALL_RESURRECTION, 1, true);
        }
        if let Some(t) = unit::get(c, target) {
            sc::sc_start(c, src, &t, SC_KYRIE, 100, 10, time);
        }
        return hp + sp + ap;
    }
    // Disable Ultimate Sacrifice on GVG maps
    if scl.has(SC_ULTIMATE_S) && !crate::status::map_flag_gvg2(c, tmap) {
        crate::status::hp::status_revive(c, target, 100, 100, 0);
        sc::status_change_clear(c, target, 0);
        if let Some(t) = unit::get(c, target) {
            crate::skill::clif::clif_skill_nodamage(c, Some(&t), &t, crate::db::skills::ids::ALL_RESURRECTION, 1, true);
        }
        return hp + sp + ap;
    }
    // (SC_REBIRTH: monsters only)
    sc::status_change_clear(c, target, 0);
    if dflag & 4 != 0 {
        // Delete from memory. (also invokes map removal code)
        super::unit::unit_free(c, target, unit::CLR_DEAD);
    } else if dflag & 2 != 0 {
        // remove from map (hom_dead returns 3: this branch)
        super::unit::unit_remove_map(c, target, unit::CLR_DEAD);
    } else if let Some(mut u) = unit::get(c, target) {
        // Some death states that would normally be handled by unit_remove_map
        unit::stop_attack(c, &mut u);
        unit::stop_walking(c, &mut u, unit::USW_FIXPOS | unit::USW_RELEASE_TARGET, 0);
        unit::save(c, &u);
        crate::skill::unit::unit_skillcastcancel(c, target, 0);
        if let Some(u) = unit::get(c, target) {
            crate::clif::clearunit_area(c, &u, unit::CLR_DEAD);
        }
        crate::skillunit::skill_unit_move_id(c, target, c.tick(), 4);
        if let Some(u) = unit::get(c, target) {
            crate::skill::timerskill::skill_cleartimerskill(c, &u);
        }
    }
    // (NPC die scripts: players only)
    hp + sp + ap
}

// ---------------------------------------------------------------------------------------------- heal

/// status.cpp status_heal (status.cpp:1803-1889) for a homunculus: the heal is capped at the missing HP / SP / AP,
/// Berserk blocks unforced HP heals, negative values are passive damage, then hom_heal tells the master. Returns
/// hp + sp + ap.
pub fn status_heal_hom(c: &Ctx, bl: u32, hhp: i64, hsp: i64, hap: i64, flag: i32) -> i32 {
    let mut hp = hhp.clamp(i32::MIN as i64, i32::MAX as i64) as i32;
    let mut sp = hsp.clamp(i32::MIN as i64, i32::MAX as i64) as i32;
    let mut ap = hap.clamp(i32::MIN as i64, i32::MAX as i64) as i32;
    let mut flag = flag;
    let Some(hd) = data::get(c, bl) else { return 0 };
    if hd.battle_status.hp == 0 {
        return 0;
    }
    let scl = sc::load(c, bl);
    let has_sc = !scl.empty();
    if hp < 0 {
        if hp == i32::MIN {
            hp += 1; // -INT_MIN == INT_MIN in some architectures!
        }
        // status_damage(nullptr, bl, -hp, 0, 0, 1, 0): the 7-argument overload (walkdelay 0, flag 1: passive)
        crate::status::hp::status_damage(c, None, bl, -(hp as i64), 0, 0, 0, 1, 0);
        hp = 0;
    }
    // (status is a pointer in rAthena: the values after the damage above)
    let status = data::get(c, bl).map(|h| h.battle_status).unwrap_or(hd.battle_status.clone());
    if hp != 0 {
        if has_sc && scl.has(SC_BERSERK) {
            if flag & 1 != 0 {
                flag &= !2;
            } else {
                hp = 0;
            }
        }
        if hp as u32 > status.max_hp.wrapping_sub(status.hp) {
            hp = status.max_hp.wrapping_sub(status.hp) as i32;
        }
    }
    if sp < 0 {
        if sp == i32::MIN {
            sp += 1;
        }
        crate::status::hp::status_damage(c, None, bl, 0, -(sp as i64), 0, 0, 1, 0);
        sp = 0;
    }
    let status = data::get(c, bl).map(|h| h.battle_status).unwrap_or(status);
    if sp != 0 && sp as u32 > status.max_sp.wrapping_sub(status.sp) {
        sp = status.max_sp.wrapping_sub(status.sp) as i32;
    }
    if ap < 0 {
        if ap == i32::MIN {
            ap += 1;
        }
        crate::status::hp::status_damage(c, None, bl, 0, 0, -(ap as i64), 0, 1, 0);
        ap = 0;
    }
    let status = data::get(c, bl).map(|h| h.battle_status).unwrap_or(status);
    if ap != 0 && ap as u32 > status.max_ap.wrapping_sub(status.ap) {
        ap = status.max_ap.wrapping_sub(status.ap) as i32;
    }
    if ap == 0 && sp == 0 && hp == 0 && flag & 4 == 0 {
        return 0;
    }
    let Some(mut hd) = data::get(c, bl) else { return 0 };
    hd.battle_status.hp = hd.battle_status.hp.wrapping_add(hp as u32);
    hd.battle_status.sp = hd.battle_status.sp.wrapping_add(sp as u32);
    hd.battle_status.ap = hd.battle_status.ap.wrapping_add(ap as u32);
    data::put(c, &hd);
    // Send HP update to client: case BL_HOM: hom_heal(*hd, hp != 0, sp != 0)
    super::hom_heal(c, bl, hp != 0, sp != 0);
    hp + sp + ap
}

// ---------------------------------------------------------------------------------------------- revive

/// status.cpp status_revive (status.cpp:1983-2024) for a homunculus (the caller checked it is dead): HP / SP / AP by
/// percentage, the resurrection animation when on a map, then hom_revive.
pub fn status_revive_hom(c: &Ctx, bl: u32, per_hp: u8, per_sp: u8, per_ap: u8) -> i32 {
    let Some(mut hd) = data::get(c, bl) else { return 0 };
    let st = hd.battle_status.clone();
    let mut hp = (st.max_hp as i64 * per_hp as i64 / 100) as u32;
    let mut sp = (st.max_sp as i64 * per_sp as i64 / 100) as u32;
    let mut ap = (st.max_ap as i64 * per_ap as i64 / 100) as u32;
    if hp > st.max_hp.wrapping_sub(st.hp) {
        hp = st.max_hp.wrapping_sub(st.hp);
    } else if per_hp != 0 && hp == 0 {
        hp = 1;
    }
    if sp > st.max_sp.wrapping_sub(st.sp) {
        sp = st.max_sp.wrapping_sub(st.sp);
    } else if per_sp != 0 && sp == 0 {
        sp = 1;
    }
    if ap > st.max_ap.wrapping_sub(st.ap) {
        ap = st.max_ap.wrapping_sub(st.ap);
    } else if per_ap != 0 && ap == 0 {
        ap = 1;
    }
    hd.battle_status.hp = hd.battle_status.hp.wrapping_add(hp);
    hd.battle_status.sp = hd.battle_status.sp.wrapping_add(sp);
    hd.battle_status.ap = hd.battle_status.ap.wrapping_add(ap);
    data::put(c, &hd);
    if let Some(u) = unit::get(c, bl) {
        if u.on_map {
            // Animation only if character is already on a map.
            crate::status::clif::resurrection(c, &u);
        }
    }
    super::hom_revive(c, bl, hp, sp);
    1
}

// ---------------------------------------------------------------------------------------------- max setters

/// status.cpp status_set_maxhp's `status->max_hp = maxhp` for a homunculus (before the heal / zap).
pub fn set_max_hp(c: &Ctx, bl: u32, maxhp: u32) {
    data::with(c, bl, |hd| hd.battle_status.max_hp = maxhp);
}

/// status.cpp status_set_maxsp's `status->max_sp = maxsp` for a homunculus (after the heal / zap).
pub fn set_max_sp(c: &Ctx, bl: u32, maxsp: u32) {
    data::with(c, bl, |hd| hd.battle_status.max_sp = maxsp);
}

/// status.cpp status_set_maxap's `status->max_ap = maxap` for a homunculus (after the heal / zap).
pub fn set_max_ap(c: &Ctx, bl: u32, maxap: u32) {
    data::with(c, bl, |hd| hd.battle_status.max_ap = maxap);
}

// ---------------------------------------------------------------------------------------------- natural heal

/// The homunculi in regen_db (every loaded homunculus: map_addiddb in hom_alloc, map_deliddb in unit_free).
pub fn regen_ids(c: &Ctx) -> Vec<u32> {
    use spacetimedb::Table;
    c.db().hom_data().iter().map(|hd| hd.id).collect()
}

use data::hom_data;

/// status.cpp status_natural_heal (status.cpp:15444-15645) for a homunculus: its HP every natural_healhp_interval / 3
/// ("Homun HP regen fix (2 seconds instead of 6 seconds)") and SP every natural_healsp_interval / 2, no SP while walking
/// (unless regen->state.walk), nothing while dead, hidden, overweight or blocked (hom_vaporize sets state.block 3).
/// A homunculus has no sregen / ssregen: the skill and sitting parts never run. Returns the regen flags applied.
pub fn status_natural_heal_hom(c: &Ctx, id: u32, natural_heal_prev_tick: Tick, _natural_heal_diff_tick: Tick) -> u8 {
    let Some(hd) = data::get(c, id) else { return 0 };
    let Some(u) = unit::get(c, id) else { return 0 };
    let status = hd.battle_status.clone();
    let regen = hd.regen.clone();
    let scl = sc::load(c, id);
    let has_sc = !scl.empty();
    let mut flag = regen.flag;
    if flag & RGN_HP != 0 && regen.block & 1 != 0 {
        flag &= !(RGN_HP | RGN_SHP);
    }
    if flag & RGN_SP != 0 && regen.block & 2 != 0 {
        flag &= !(RGN_SP | RGN_SSP);
    }
    // Only skill-based regen is disabled at max HP/SP
    if flag & RGN_SHP != 0 && status.hp >= status.max_hp {
        flag &= !RGN_SHP;
    }
    if flag & RGN_SSP != 0 && status.sp >= status.max_sp {
        flag &= !RGN_SSP;
    }
    if flag != 0 && (status.hp == 0 || (has_sc && (u.sc.option & (OPTION_HIDE | OPTION_CLOAK | OPTION_CHASEWALK) != 0 || scl.has(SC__INVISIBILITY)))) {
        flag = RGN_NONE;
    }
    // (pc_bleeding / pc_regen: players only; the sitting regen needs regen->ssregen: players only)
    if flag != 0 && regen.overweight {
        flag = RGN_NONE;
    }
    let walking = u.ud.walktimer != 0;
    if walking {
        flag &= !(RGN_SHP | RGN_SSP);
        //Mercenaries recover HP even while walking
        if !regen.walk {
            flag &= !RGN_HP;
        }
        //Homunculus don't recover SP while walking
        if !regen.walk {
            flag &= !RGN_SP;
        }
    }
    let mut multi = 1;
    if flag & (RGN_HP | RGN_SP) != 0 {
        if u.vd.dead_sit == 2 {
            multi += 1; //This causes the interval to be halved
        }
        if regen.gc {
            multi += 1; //This causes the interval to be halved
        }
    }
    // Natural Hp regen
    if flag & RGN_HP != 0 {
        // Interval to next recovery tick
        let mut rate = (crate::conf::natural_healhp_interval() as f64 / (regen.rate_hp as f64 / 100. * multi as f64)) as i32;
        // (Half recovery while moving: players only)
        // Homun HP regen fix (2 seconds instead of 6 seconds)
        rate /= 3;
        // Our timer system isn't 100% accurate so make sure we use the closest interval
        rate -= (NATURAL_HEAL_INTERVAL / 2) as i32;
        let tick_hp = data::get(c, id).map(|h| h.regen_tick_hp).unwrap_or(hd.regen_tick_hp);
        if tick_hp + rate as i64 <= natural_heal_prev_tick {
            data::with(c, id, |h| h.regen_tick_hp = natural_heal_prev_tick);
            let cur = data::get(c, id).map(|h| h.battle_status).unwrap_or(status.clone());
            if cur.hp >= cur.max_hp {
                flag &= !(RGN_HP | RGN_SHP);
            } else if status_heal_hom(c, id, regen.hp as i64, 0, 0, 1) < regen.hp as i32 {
                flag &= !RGN_SHP; // Full
            }
        }
    } else {
        data::with(c, id, |h| h.regen_tick_hp = natural_heal_prev_tick);
    }
    // Natural SP regen
    if flag & RGN_SP != 0 {
        // Interval to next recovery tick
        let mut rate = (crate::conf::natural_healsp_interval() as f64 / (regen.rate_sp as f64 / 100. * multi as f64)) as i32;
        // Homun SP regen fix (4 seconds instead of 8 seconds)
        rate /= 2;
        // (RENEWAL Fury rule: Monks only)
        // Our timer system isn't 100% accurate so make sure we use the closest interval
        rate -= (NATURAL_HEAL_INTERVAL / 2) as i32;
        let tick_sp = data::get(c, id).map(|h| h.regen_tick_sp).unwrap_or(hd.regen_tick_sp);
        if tick_sp + rate as i64 <= natural_heal_prev_tick {
            data::with(c, id, |h| h.regen_tick_sp = natural_heal_prev_tick);
            let cur = data::get(c, id).map(|h| h.battle_status).unwrap_or(status.clone());
            if cur.sp >= cur.max_sp {
                flag &= !(RGN_SP | RGN_SSP);
            } else if status_heal_hom(c, id, 0, regen.sp as i64, 0, 1) < regen.sp as i32 {
                flag &= !RGN_SSP; // Full
            }
        }
    } else {
        data::with(c, id, |h| h.regen_tick_sp = natural_heal_prev_tick);
    }
    // if (!regen->sregen) return flag;
    flag
}

#[cfg(test)]
mod tests {
    use super::super::status::status_calc_regen_rate_hom;
    use crate::status::data::RegenData;
    use crate::status::defs::*;

    #[test]
    fn regen_rate_without_status_changes() {
        let mut r = RegenData { flag: 0, rate_hp: 7, rate_sp: 9, ..Default::default() };
        status_calc_regen_rate_hom(&mut r, &crate::status::sc::Sc::default());
        assert_eq!(r.flag, RGN_HP | RGN_SP);
        assert_eq!((r.rate_hp, r.rate_sp), (100, 100));
    }
}
