//! status.cpp status_damage / status_heal / status_natural_heal for mercenaries (BL_MER) and elementals (BL_ELEM): the
//! generic code with their arms (mercenary_heal / elemental_heal send the master the new values, mercenary_dead /
//! elemental_dead delete the companion and cancel the "death").

use super::Comp;
use crate::map::{BL_MER, BL_MOB, BL_PC};
use crate::status::consts::*;
use crate::status::defs::*;
use crate::status::sc;
use crate::timer::{self, Func};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// status.cpp status_damage for a mercenary / an elemental (hp / sp / ap are already capped to int and made
/// non-negative by the caller's first part, status/hp.rs). flag&1: passive; &2: fail if not enough; &8: damage of a
/// dead unit; &16: coma. Returns hp + sp + ap.
#[allow(clippy::too_many_arguments)]
pub fn status_damage_comp(c: &Ctx, src: Option<&Unit>, target: u32, dhp: i64, mut hp: i32, mut sp: i32, mut ap: i32, walkdelay: Tick, mut flag: i32, skill_id: u16) -> i32 {
    let Some(tu) = unit::get(c, target) else { return 0 };
    let Some(x) = super::get(c, target) else { return 0 };
    let status = x.battle_status().clone();
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
    let Some(mut x) = super::get(c, target) else { return 0 };
    {
        let st = x.battle_status_mut();
        st.hp = st.hp.wrapping_sub(hp as u32);
        st.sp = st.sp.wrapping_sub(sp as u32);
        st.ap = st.ap.wrapping_sub(ap as u32);
        // Coma
        if flag & 16 != 0 {
            st.hp = 1;
            st.sp = 1;
            if sp == 0 {
                sp = 1; // To make sure the status bar is updated
            }
        }
    }
    super::put(c, &x);
    let new_hp = x.battle_status().hp;
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
    comp_heal_notify(c, &x, hp, sp);
    // Normal attack damage is logged in the monster's dmglog as attack damage (counts for the exp tap and the MVP)
    if let Some(s) = src {
        if s.kind == BL_MOB && skill_id == 0 {
            let dhp = dhp.clamp(i32::MIN as i64, i32::MAX as i64);
            crate::mob::dead::mob_log_damage(c, s.id, &tu, 0, dhp);
        }
    }
    let status_hp = super::get(c, target).map(|x| x.battle_status().hp).unwrap_or(0);
    if status_hp != 0 || flag & 8 != 0 {
        // Still lives or has been dead before this damage.
        if walkdelay > 0 && !crate::status::status_isendure(c, target, c.tick(), false) {
            crate::battle::unit_attack::unit_set_walkdelay(c, target, c.tick(), walkdelay, 0, skill_id);
        }
        return hp + sp + ap;
    }
    if let Some(mut x) = super::get(c, target) {
        x.battle_status_mut().hp = 0;
        super::put(c, &x);
    }
    // NOTE: These dead functions should return: 0: Death cancelled, auto-revived. Non-zero: Standard death.
    // mercenary_dead / elemental_dead delete the companion and answer 0 ("death cancelled").
    match x {
        Comp::Mer(md) => {
            crate::mercenary::mercenary_dead(c, md.id);
        }
        Comp::Elem(ed) => {
            crate::elemental::elemental_dead(c, ed.id);
        }
    }
    hp + sp + ap
}

/// mercenary_heal / elemental_heal (status_damage's and status_heal's type switch): the master sees the new HP / SP.
fn comp_heal_notify(c: &Ctx, x: &Comp, hp: i32, sp: i32) {
    match x {
        Comp::Mer(md) => crate::mercenary::mercenary_heal(c, md.master, hp, sp),
        Comp::Elem(ed) => crate::elemental::elemental_heal(c, ed.master, hp, sp),
    }
}

/// status.cpp status_heal for a mercenary / an elemental. flag&1: forced (overrides Berserk); &4: heal even with 0.
/// Returns hp + sp + ap.
pub fn status_heal_comp(c: &Ctx, bl: u32, hhp: i64, hsp: i64, hap: i64, mut flag: i32) -> i32 {
    let mut hp = hhp.clamp(i32::MIN as i64, i32::MAX as i64) as i32;
    let mut sp = hsp.clamp(i32::MIN as i64, i32::MAX as i64) as i32;
    let mut ap = hap.clamp(i32::MIN as i64, i32::MAX as i64) as i32;
    let Some(x) = super::get(c, bl) else { return 0 };
    if x.battle_status().hp == 0 {
        return 0;
    }
    let scl = sc::load(c, bl);
    let has_sc = !scl.empty();
    if hp < 0 {
        if hp == i32::MIN {
            hp += 1; // -INT_MIN == INT_MIN in some architectures!
        }
        crate::status::hp::status_damage(c, None, bl, -(hp as i64), 0, 0, 0, 1, 0);
        hp = 0;
    }
    let Some(x) = super::get(c, bl) else { return 0 };
    let status = x.battle_status().clone();
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
    let Some(x) = super::get(c, bl) else { return 0 };
    let status = x.battle_status().clone();
    if sp != 0 && sp as u32 > status.max_sp.wrapping_sub(status.sp) {
        sp = status.max_sp.wrapping_sub(status.sp) as i32;
    }
    if ap < 0 {
        if ap == i32::MIN {
            ap += 1;
        }
        // (rAthena passes the flag 1 as walkdelay here: status_damage(nullptr, bl, 0, 0, -ap, 0, 1, 0))
        crate::status::hp::status_damage(c, None, bl, 0, 0, -(ap as i64), 0, 1, 0);
        ap = 0;
    }
    let Some(mut x) = super::get(c, bl) else { return 0 };
    let status = x.battle_status().clone();
    if ap != 0 && ap as u32 > status.max_ap.wrapping_sub(status.ap) {
        ap = status.max_ap.wrapping_sub(status.ap) as i32;
    }
    if ap == 0 && sp == 0 && hp == 0 && flag & 4 == 0 {
        return 0;
    }
    {
        let st = x.battle_status_mut();
        st.hp = st.hp.wrapping_add(hp as u32);
        st.sp = st.sp.wrapping_add(sp as u32);
        st.ap = st.ap.wrapping_add(ap as u32);
    }
    super::put(c, &x);
    // Send HP update to client
    comp_heal_notify(c, &x, hp, sp);
    hp + sp + ap
}

// ---------------------------------------------------------------------------------------------- natural heal

/// status.cpp status_natural_heal for a mercenary / an elemental (map_foreachregen: status/hp.rs calls it in regen_db's
/// order). Returns the regen flags applied.
pub fn status_natural_heal_comp(c: &Ctx, id: u32, natural_heal_prev_tick: Tick, _natural_heal_diff_tick: Tick) -> u8 {
    let Some(x) = super::get(c, id) else { return 0 };
    let Some(u) = unit::get(c, id) else { return 0 };
    let status = x.battle_status().clone();
    let regen = x.regen().clone();
    let scl = sc::load(c, id);
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
    if flag != 0 && (status.hp == 0 || (!scl.empty() && (u.sc.option & (OPTION_HIDE | OPTION_CLOAK | OPTION_CHASEWALK) != 0 || scl.has(SC__INVISIBILITY)))) {
        flag = RGN_NONE;
    }
    // (no ssregen: the sitting regen is the players')
    if flag != 0 && regen.overweight {
        flag = RGN_NONE;
    }
    if u.ud.walktimer != 0 {
        flag &= !(RGN_SHP | RGN_SSP);
        //Mercenaries recover HP even while walking
        if u.kind != BL_MER && !regen.walk {
            flag &= !RGN_HP;
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
    let (mut tick_hp, mut tick_sp) = x.regen_ticks();
    // Natural Hp regen
    if flag & RGN_HP != 0 {
        // Interval to next recovery tick
        let mut rate = (crate::conf::natural_healhp_interval() as f64 / (regen.rate_hp as f64 / 100. * multi as f64)) as i32;
        // (half recovery while moving: players only)
        // Mercenary HP regen fix (8 seconds instead of 6 seconds)
        if u.kind == BL_MER {
            rate = (rate * 4) / 3;
        }
        // Our timer system isn't 100% accurate so make sure we use the closest interval
        rate -= (NATURAL_HEAL_INTERVAL / 2) as i32;
        if tick_hp + rate as i64 <= natural_heal_prev_tick {
            tick_hp = natural_heal_prev_tick;
            set_ticks(c, id, Some(tick_hp), None);
            let cur = super::status_of(c, id).unwrap_or(status.clone());
            if cur.hp >= cur.max_hp {
                flag &= !(RGN_HP | RGN_SHP);
            } else if crate::status::hp::status_heal(c, id, regen.hp as i64, 0, 0, 1) < regen.hp as i32 {
                flag &= !RGN_SHP; // Full
            }
        }
    } else {
        tick_hp = natural_heal_prev_tick;
        set_ticks(c, id, Some(tick_hp), None);
    }
    // Natural SP regen
    if flag & RGN_SP != 0 {
        // Interval to next recovery tick
        let mut rate = (crate::conf::natural_healsp_interval() as f64 / (regen.rate_sp as f64 / 100. * multi as f64)) as i32;
        // Mercenary SP regen fix (6 seconds instead of 8 seconds)
        if u.kind == BL_MER {
            rate = (rate * 3) / 4;
        }
        // Our timer system isn't 100% accurate so make sure we use the closest interval
        rate -= (NATURAL_HEAL_INTERVAL / 2) as i32;
        if tick_sp + rate as i64 <= natural_heal_prev_tick {
            tick_sp = natural_heal_prev_tick;
            set_ticks(c, id, None, Some(tick_sp));
            let cur = super::status_of(c, id).unwrap_or(status.clone());
            if cur.sp >= cur.max_sp {
                flag &= !(RGN_SP | RGN_SSP);
            } else if crate::status::hp::status_heal(c, id, 0, regen.sp as i64, 0, 1) < regen.sp as i32 {
                flag &= !RGN_SSP; // Full
            }
        }
    } else {
        tick_sp = natural_heal_prev_tick;
        set_ticks(c, id, None, Some(tick_sp));
    }
    // (!regen->sregen: companions have no skill regen)
    flag
}

fn set_ticks(c: &Ctx, id: u32, hp: Option<Tick>, sp: Option<Tick>) {
    if let Some(mut x) = super::get(c, id) {
        let (h, s) = x.regen_ticks();
        x.set_regen_ticks(hp.unwrap_or(h), sp.unwrap_or(s));
        match &x {
            Comp::Mer(m) => {
                use crate::mercenary::data::mercenary_data;
                c.db().mercenary_data().id().update(m.clone());
            }
            Comp::Elem(e) => {
                use crate::elemental::data::elemental_data;
                c.db().elemental_data().id().update(e.clone());
            }
        }
    }
}

/// status_damage's "reset regen ticks" (target->type & BL_REGEN) for a companion (memset(&regen->tick, 0, …)).
pub fn reset_regen_ticks(c: &Ctx, id: u32) {
    set_ticks(c, id, Some(0), Some(0));
}
