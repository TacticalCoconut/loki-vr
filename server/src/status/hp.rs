//! HP / SP / AP bookkeeping: status.cpp status_set_hp … status_set_maxap, status_charge, status_damage, status_heal,
//! status_percent_change (status_percent_heal / damage, status_kill), status_revive, the natural heal timer
//! (status_natural_heal_timer / status_natural_heal with the Project Loki sitting patch) and pc.cpp pc_damage,
//! pc_heal, pc_revive, pc_setrestartvalue, pc_itemheal, pc_percentheal, pc_bleeding, pc_regen.
//!
//! Players here; monsters hand over to mob/dead.rs, homunculi to homunculus/hp.rs, mercenaries and elementals to
//! companion/hp.rs (one early arm each).

use std::cell::Cell;

use super::consts::*;
use super::data;
use super::defs::*;
use super::sc;
use crate::timer::{self, Func};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

thread_local! {
    /// pc.cpp potion_flag: 0 normal, 1 Potion Pitcher / Slim Pitcher (the heal is only recorded), 2 ranked (Fame
    /// Top 10) potion, 3 Mild Wind-style heal (the items / skills systems set it around item scripts).
    static POTION_FLAG: Cell<i32> = const { Cell::new(0) };
    /// pc.cpp potion_hp, potion_sp, potion_per_hp, potion_per_sp (what itemheal / percentheal record while
    /// potion_flag is 1) and potion_target (the unit a pitched potion goes to, 0 = none).
    static POTION: Cell<(i32, i32, i32, i32, u32)> = const { Cell::new((0, 0, 0, 0, 0)) };
}

pub fn potion_flag() -> i32 {
    POTION_FLAG.with(|p| p.get())
}

pub fn set_potion_flag(v: i32) {
    POTION_FLAG.with(|p| p.set(v));
}

/// potion_hp / potion_sp (buildin itemheal while potion_flag is 1).
pub fn set_potion_hpsp(hp: i32, sp: i32) {
    POTION.with(|p| {
        let v = p.get();
        p.set((hp, sp, v.2, v.3, v.4));
    });
}

/// potion_per_hp / potion_per_sp (buildin percentheal while potion_flag is 1).
pub fn set_potion_per(hp: i32, sp: i32) {
    POTION.with(|p| {
        let v = p.get();
        p.set((v.0, v.1, hp, sp, v.4));
    });
}

/// (potion_hp, potion_sp, potion_per_hp, potion_per_sp).
pub fn potion_values() -> (i32, i32, i32, i32) {
    POTION.with(|p| {
        let v = p.get();
        (v.0, v.1, v.2, v.3)
    })
}

/// potion_target (None = 0).
pub fn potion_target() -> Option<u32> {
    POTION.with(|p| Some(p.get().4).filter(|&t| t != 0))
}

pub fn set_potion_target(target: Option<u32>) {
    POTION.with(|p| {
        let v = p.get();
        p.set((v.0, v.1, v.2, v.3, target.unwrap_or(0)));
    });
}

fn is_pc(c: &Ctx, bl: u32) -> bool {
    unit::get(c, bl).map(|u| u.kind == BL_PC).unwrap_or(false) && data::sd_get(c, bl).is_some()
}

// ---------------------------------------------------------------------------------------------- set

/// status.cpp status_set_hp: sets HP (can't kill); `flag` 2 shows the heal effect.
pub fn status_set_hp(c: &Ctx, bl: u32, mut hp: u32, flag: i32) -> i32 {
    if hp < 1 {
        return 0;
    }
    let Some(status) = super::status_data_of(c, bl) else { return 0 };
    if hp > status.max_hp {
        hp = status.max_hp;
    }
    if hp == status.hp {
        return 0;
    }
    if hp > status.hp {
        return status_heal(c, bl, (hp - status.hp) as i64, 0, 0, 1 | flag);
    }
    status_zap(c, bl, (status.hp - hp) as i64, 0, 0)
}

/// status.cpp status_set_maxhp.
pub fn status_set_maxhp(c: &Ctx, bl: u32, maxhp: u32, flag: i32) -> i32 {
    if maxhp < 1 {
        return 0;
    }
    let Some(status) = super::status_data_of(c, bl) else { return 0 };
    if maxhp == status.max_hp {
        return 0;
    }
    let heal = maxhp as i64 - status.max_hp as i64;
    data::with_sd(c, bl, |sd| sd.battle_status.max_hp = maxhp);
    // (status->max_hp of a homunculus: hd->battle_status)
    crate::homunculus::hp::set_max_hp(c, bl, maxhp);
    // (status->max_hp of a monster: md->status)
    if let Some(mut md) = crate::mob::get(c, bl) {
        md.status.max_hp = maxhp;
        crate::mob::put(c, &md);
    }
    // (a mercenary's / an elemental's battle status)
    if let Some(mut x) = crate::companion::get(c, bl) {
        x.battle_status_mut().max_hp = maxhp;
        crate::companion::put(c, &x);
    }
    if heal > 0 {
        status_heal(c, bl, heal, 0, 0, 1 | flag);
    } else {
        status_zap(c, bl, -heal, 0, 0);
    }
    maxhp as i32
}

/// status.cpp status_set_sp.
pub fn status_set_sp(c: &Ctx, bl: u32, mut sp: u32, flag: i32) -> i32 {
    let Some(status) = super::status_data_of(c, bl) else { return 0 };
    if sp > status.max_sp {
        sp = status.max_sp;
    }
    if sp == status.sp {
        return 0;
    }
    if sp > status.sp {
        return status_heal(c, bl, 0, (sp - status.sp) as i64, 0, 1 | flag);
    }
    status_zap(c, bl, 0, (status.sp - sp) as i64, 0)
}

/// status.cpp status_set_maxsp (rAthena heals / zaps HP here, not SP — kept).
pub fn status_set_maxsp(c: &Ctx, bl: u32, maxsp: u32, flag: i32) -> i32 {
    if maxsp < 1 {
        return 0;
    }
    let Some(status) = super::status_data_of(c, bl) else { return 0 };
    if maxsp == status.max_sp {
        return 0;
    }
    if maxsp > status.max_sp {
        status_heal(c, bl, (maxsp - status.max_sp) as i64, 0, 0, 1 | flag);
    } else {
        status_zap(c, bl, (status.max_sp - maxsp) as i64, 0, 0);
    }
    data::with_sd(c, bl, |sd| sd.battle_status.max_sp = maxsp);
    // (status->max_sp of a homunculus: hd->battle_status)
    crate::homunculus::hp::set_max_sp(c, bl, maxsp);
    // (a mercenary's / an elemental's battle status)
    if let Some(mut x) = crate::companion::get(c, bl) {
        x.battle_status_mut().max_sp = maxsp;
        crate::companion::put(c, &x);
    }
    maxsp as i32
}

/// status.cpp status_set_ap.
pub fn status_set_ap(c: &Ctx, bl: u32, mut ap: u32, flag: i32) -> i32 {
    let Some(status) = super::status_data_of(c, bl) else { return 0 };
    if ap > status.max_ap {
        ap = status.max_ap;
    }
    if ap == status.ap {
        return 0;
    }
    if ap > status.ap {
        return status_heal(c, bl, 0, 0, (ap - status.ap) as i64, 1 | flag);
    }
    status_zap(c, bl, 0, 0, (status.ap - ap) as i64)
}

/// status.cpp status_set_maxap.
pub fn status_set_maxap(c: &Ctx, bl: u32, maxap: u32, flag: i32) -> i32 {
    if maxap < 1 {
        return 0;
    }
    let Some(status) = super::status_data_of(c, bl) else { return 0 };
    if maxap == status.max_ap {
        return 0;
    }
    if maxap > status.max_ap {
        status_heal(c, bl, 0, 0, (maxap - status.max_ap) as i64, 1 | flag);
    } else {
        status_zap(c, bl, 0, 0, (status.max_ap - maxap) as i64);
    }
    data::with_sd(c, bl, |sd| sd.battle_status.max_ap = maxap);
    // (status->max_ap of a homunculus: hd->battle_status)
    crate::homunculus::hp::set_max_ap(c, bl, maxap);
    maxap as i32
}

// ---------------------------------------------------------------------------------------------- damage

/// status.cpp status_charge: takes HP / SP for a skill (flag 3: passive, fail when not enough).
pub fn status_charge(c: &Ctx, bl: u32, hp: i64, sp: i64) -> i64 {
    let kind = unit::get(c, bl).map(|u| u.kind).unwrap_or(0);
    if kind & BL_CONSUME == 0 {
        return (hp as i32 as i64) + sp; // Assume all was charged so there are no 'not enough' fails.
    }
    status_damage(c, None, bl, hp, sp, 0, 0, 3, 0) as i64
}

/// status.hpp status_zap: passive HP / SP / AP loss.
pub fn status_zap(c: &Ctx, bl: u32, hp: i64, sp: i64, ap: i64) -> i32 {
    status_damage(c, None, bl, hp, sp, ap, 0, 1, 0)
}

/// status.hpp status_fix_damage.
pub fn status_fix_damage(c: &Ctx, src: Option<&Unit>, target: u32, hp: i64, walkdelay: Tick, skill_id: u16) -> i32 {
    status_damage(c, src, target, hp, 0, 0, walkdelay, 0, skill_id)
}

/// status.cpp status_damage. flag&1: passive; &2: fail if not enough; &4: no EXP / loot; &8: damage SP of a dead
/// character; &16: coma. Returns hp + sp + ap.
#[allow(clippy::too_many_arguments)]
pub fn status_damage(c: &Ctx, src: Option<&Unit>, target: u32, dhp: i64, dsp: i64, dap: i64, walkdelay: Tick, mut flag: i32, _skill_id: u16) -> i32 {
    let mut hp = cap_value(dhp, i32::MIN as i64, i32::MAX as i64) as i32;
    let mut sp = cap_value(dsp, i32::MIN as i64, i32::MAX as i64) as i32;
    let mut ap = cap_value(dap, i32::MIN as i64, i32::MAX as i64) as i32;
    let Some(tu) = unit::get(c, target) else { return 0 };
    if sp != 0 && tu.kind & BL_CONSUME == 0 {
        sp = 0; // Not a valid SP target.
    }
    if ap != 0 && tu.kind & BL_CONSUME == 0 {
        ap = 0; // Not a valid AP target.
    }
    if hp < 0 {
        // Assume absorbed damage.
        status_heal(c, target, -(hp as i64), 0, 0, 1);
        hp = 0;
    }
    if sp < 0 {
        status_heal(c, target, 0, -(sp as i64), 0, 1);
        sp = 0;
    }
    if ap < 0 {
        status_heal(c, target, 0, 0, -(ap as i64), 1);
        ap = 0;
    }
    if tu.kind == crate::map::BL_SKILL {
        if src.map(|s| s.kind & crate::skillunit::conf::can_damage_skill() != 0).unwrap_or(true) {
            return crate::skillunit::skill_unit_ondamaged(c, target, hp as i64) as i32;
        }
        return 0;
    }
    if tu.kind == crate::map::BL_MOB {
        // the monster side of status_damage (mob_damage, mob_dead): the monster system
        return crate::mob::dead::status_damage_mob(c, src, target, dhp, hp, sp, ap, walkdelay, flag, _skill_id);
    }
    if tu.kind == crate::map::BL_HOM {
        // the homunculus side of status_damage (hom_heal, hom_dead: status.cpp:1658-1659, 1695; homunculus/hp.rs)
        return crate::homunculus::hp::status_damage_hom(c, src, target, dhp, hp, sp, ap, walkdelay, flag, _skill_id);
    }
    if crate::companion::is_kind(tu.kind) {
        // the mercenary / elemental side of status_damage (companion/hp.rs)
        return crate::companion::hp::status_damage_comp(c, src, target, dhp, hp, sp, ap, walkdelay, flag, _skill_id);
    }
    if !is_pc(c, target) {
        // (npcs / skill units: no status here)
        return 0;
    }
    let Some(status) = super::status_data_of(c, target) else { return 0 };
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
                if (t == SC_WINKCHARM || t == SC_VOICEOFSIREN || t == SC_DEEPSLEEP) && tu.kind != BL_PC && hp == 0 {
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
                        if s.kind != BL_PC && !super::map_flag_gvg2(c, tu.map) && !crate::npc::mapflag(c, tu.map, crate::npc::MF_BATTLEGROUND) {
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
        if tu.kind == BL_PC {
            super::bonus::pc_bonus_script_clear(c, target, BSF_REM_ON_DAMAGED);
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
    let Some(mut sd) = data::sd_get(c, target) else { return 0 };
    sd.battle_status.hp = sd.battle_status.hp.wrapping_sub(hp as u32);
    sd.battle_status.sp = sd.battle_status.sp.wrapping_sub(sp as u32);
    sd.battle_status.ap = sd.battle_status.ap.wrapping_sub(ap as u32);
    // Coma
    if flag & 16 != 0 {
        sd.battle_status.hp = 1;
        sd.battle_status.sp = 1;
        if sp == 0 {
            sp = 1; // To make sure the status bar is updated
        }
    }
    data::sd_put(c, &sd);
    super::calc::sync_unit(c, target);
    let new_hp = sd.battle_status.hp;
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
    pc_damage(c, target, src, hp as u32, sp as u32, ap as u32);
    // Normal attack damage is logged in the monster's dmglog as attack damage (counts for the exp tap and the MVP)
    if let Some(s) = src {
        if s.kind == crate::map::BL_MOB && _skill_id == 0 {
            let dhp = cap_value(dhp, i32::MIN as i64, i32::MAX as i64);
            crate::mob::dead::mob_log_damage(c, s.id, &tu, 0, dhp);
        }
    }
    // Stop walking when attacked in disguise to prevent walk-delay bug
    if src.is_some() && crate::atcmd_disguise::disguise_of(c, target) != 0 {
        if let Some(mut u) = crate::unit::get(c, target) {
            crate::unit::stop_walking(c, &mut u, crate::unit::USW_FIXPOS, 0);
            crate::unit::save(c, &u);
        }
    }
    let status_hp = super::status_data_of(c, target).map(|s| s.hp).unwrap_or(0);
    if status_hp != 0 || flag & 8 != 0 {
        // Still lives or has been dead before this damage.
        if walkdelay > 0 && !super::status_isendure(c, target, c.tick(), false) {
            crate::battle::unit_attack::unit_set_walkdelay(c, target, c.tick(), walkdelay, 0, _skill_id);
        }
        return hp + sp + ap;
    }
    data::with_sd(c, target, |sd| sd.battle_status.hp = 0);
    // NOTE: These dead functions should return: 0: Death cancelled, auto-revived. Non-zero: Standard death.
    let dflag = pc_dead(c, target, src);
    if dflag == 0 {
        // Death cancelled.
        return hp + sp + ap;
    }
    // Normal death
    if crate::skillunit::conf::clear_unit_ondeath() & tu.kind != 0 {
        crate::skillunit::skill_clear_unitgroup(c, target);
    }
    // reset regen ticks.
    let mut t = data::regen_tick(c, target);
    t.hp = 0;
    t.sp = 0;
    t.shp = 0;
    t.ssp = 0;
    t.s_hp = 0;
    t.s_sp = 0;
    t.ss_hp = 0;
    t.ss_sp = 0;
    data::put_regen_tick(c, &t);
    let scl = sc::load(c, target);
    if let Some(kaizel) = scl.get(SC_KAIZEL).filter(|_| !super::map_flag_gvg2(c, tu.map)) {
        // flag&8 = disable Kaizel
        let time = crate::db::skills::skill_get_time2(crate::db::skills::ids::SL_KAIZEL, kaizel.val1 as u16) as i64;
        // Look for Osiris Card's bonus effect on the character and revive 100% or revive normally
        let osiris = tu.kind == BL_PC && data::sd_get(c, target).map(|sd| sd.special_state.restart_full_recover).unwrap_or(false);
        if osiris {
            status_revive(c, target, 100, 100, 0);
        } else {
            status_revive(c, target, kaizel.val2 as u8, 0, 0);
        }
        sc::status_change_clear(c, target, 0);
        if let Some(t) = unit::get(c, target) {
            crate::skill::clif::clif_skill_nodamage(c, Some(&t), &t, crate::db::skills::ids::ALL_RESURRECTION, 1, true);
        }
        if let Some(t) = unit::get(c, target) {
            sc::sc_start(c, src, &t, SC_KYRIE, 100, 10, time);
        }
        if tu.kind == BL_MOB {
            // ((TBL_MOB*)target)->state.rebirth = 1
            if let Some(mut md) = crate::mob::get(c, target) {
                md.rebirth = true;
                crate::mob::put(c, &md);
            }
        }
        return hp + sp + ap;
    }
    if scl.has(SC_ULTIMATE_S) && !super::map_flag_gvg2(c, tu.map) {
        status_revive(c, target, 100, 100, 0);
        sc::status_change_clear(c, target, 0);
        if let Some(t) = unit::get(c, target) {
            crate::skill::clif::clif_skill_nodamage(c, Some(&t), &t, crate::db::skills::ids::ALL_RESURRECTION, 1, true);
        }
        return hp + sp + ap;
    }
    sc::status_change_clear(c, target, 0);
    // Some death states that would normally be handled by unit_remove_map
    if let Some(mut u) = unit::get(c, target) {
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
    // Always run NPC scripts for players last: the battleground team's die event (status.cpp:1779-1784), then
    // OnPCDieEvent
    crate::battleground::status_damage_die_event(c, target);
    crate::script::npc_run::npc_script_event(c, target, crate::script::npc_run::NPCE_DIE);
    hp + sp + ap
}

/// pc.cpp pc_dead: the player part of dying (battle agent: src/battle/pc_death.rs; it includes the fix agent's W22,
/// the "can log out" tick reset of pc.cpp:10143-10145).
pub fn pc_dead(c: &Ctx, aid: u32, src: Option<&Unit>) -> i32 {
    crate::battle::pc_death::pc_dead(c, aid, src)
}

/// pc.cpp pc_damage: the client updates of a damaged player (standing up, NPC progress bars, pets, elementals: with
/// their systems).
pub fn pc_damage(c: &Ctx, aid: u32, src: Option<&Unit>, hp: u32, sp: u32, ap: u32) {
    if ap != 0 {
        super::clif::updatestatus(c, aid, SP_AP);
    }
    if sp != 0 {
        super::clif::updatestatus(c, aid, SP_SP);
    }
    if hp != 0 {
        super::clif::updatestatus(c, aid, SP_HP);
    } else {
        return;
    }
    if src.is_none() {
        return;
    }
    if unit::get(c, aid).map(|u| u.vd.dead_sit == 2).unwrap_or(false) {
        crate::pc::setstand(c, aid, true);
        super::sc_ext::skill_sit(c, aid, false);
    }
    // if (sd->progressbar.npc_id) clif_progressbar_abort(sd)
    crate::pc_misc::progressbar_abort_if_running(c, aid);
    // pet_target_check with pet_damage_support (pc.cpp:9700-9701, pet.rs)
    crate::pet::pc_damage(c, aid, src);
    // if( sd->status.ele_id > 0 ) elemental_set_target(sd,src) (pc.cpp:9703-9704)
    if let Some(s) = src {
        if crate::elemental::data::ele_id_of(c, aid) > 0 {
            crate::elemental::elemental_set_target(c, aid, s);
        }
    }
    if crate::conf::prevent_logout_trigger() & crate::conf::PLT_DAMAGE != 0 {
        if let Some(mut p) = crate::pc::get(c, aid) {
            p.canlog_tick = c.tick();
            crate::pc::put(c, &p);
        }
    }
}

// ---------------------------------------------------------------------------------------------- heal

/// status.cpp status_heal. flag&1: forced (overrides Berserk); &2: heal effect (clif_heal); &4: HP heal effect even
/// with 0 heal. Returns hp + sp + ap.
pub fn status_heal(c: &Ctx, bl: u32, hhp: i64, hsp: i64, hap: i64, mut flag: i32) -> i32 {
    let mut hp = cap_value(hhp, i32::MIN as i64, i32::MAX as i64) as i32;
    let mut sp = cap_value(hsp, i32::MIN as i64, i32::MAX as i64) as i32;
    let mut ap = cap_value(hap, i32::MIN as i64, i32::MAX as i64) as i32;
    if crate::mob::is_mob(c, bl) {
        // mob_heal: the monster system
        return crate::mob::dead::status_heal_mob(c, bl, hhp, hsp, hap, flag);
    }
    if unit::get(c, bl).map(|u| u.kind == crate::map::BL_HOM).unwrap_or(false) {
        // hom_heal (status.cpp:1877-1878): homunculus/hp.rs
        return crate::homunculus::hp::status_heal_hom(c, bl, hhp, hsp, hap, flag);
    }
    if unit::get(c, bl).map(|u| crate::companion::is_kind(u.kind)).unwrap_or(false) {
        // mercenary_heal / elemental_heal (companion/hp.rs)
        return crate::companion::hp::status_heal_comp(c, bl, hhp, hsp, hap, flag);
    }
    if !is_pc(c, bl) {
        // (other units: no heal here)
        return 0;
    }
    let Some(status) = super::status_data_of(c, bl) else { return 0 };
    if status.hp == 0 {
        return 0;
    }
    let scl = sc::load(c, bl);
    let has_sc = !scl.empty();
    if hp < 0 {
        if hp == i32::MIN {
            hp += 1; // -INT_MIN == INT_MIN in some architectures!
        }
        status_damage(c, None, bl, -(hp as i64), 0, 0, 0, 1, 0);
        hp = 0;
    }
    let status = super::status_data_of(c, bl).unwrap_or(status);
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
        status_damage(c, None, bl, 0, -(sp as i64), 0, 0, 1, 0);
        sp = 0;
    }
    let status = super::status_data_of(c, bl).unwrap_or(status);
    if sp != 0 && sp as u32 > status.max_sp.wrapping_sub(status.sp) {
        sp = status.max_sp.wrapping_sub(status.sp) as i32;
    }
    if ap < 0 {
        if ap == i32::MIN {
            ap += 1;
        }
        status_damage(c, None, bl, 0, 0, -(ap as i64), 0, 1, 0);
        ap = 0;
    }
    let status = super::status_data_of(c, bl).unwrap_or(status);
    if ap != 0 && ap as u32 > status.max_ap.wrapping_sub(status.ap) {
        ap = status.max_ap.wrapping_sub(status.ap) as i32;
    }
    if ap == 0 && sp == 0 && hp == 0 && flag & 4 == 0 {
        return 0;
    }
    data::with_sd(c, bl, |sd| {
        sd.battle_status.hp = sd.battle_status.hp.wrapping_add(hp as u32);
        sd.battle_status.sp = sd.battle_status.sp.wrapping_add(sp as u32);
        sd.battle_status.ap = sd.battle_status.ap.wrapping_add(ap as u32);
    });
    super::calc::sync_unit(c, bl);
    // Send HP update to client
    pc_heal(c, bl, hp as u32, sp as u32, ap as u32, flag);
    hp + sp + ap
}

/// pc.cpp pc_heal: type&2 shows the heal (ZC_RECOVERY), otherwise plain status updates.
pub fn pc_heal(c: &Ctx, aid: u32, hp: u32, sp: u32, ap: u32, type_: i32) {
    if type_ & 2 != 0 {
        if hp != 0 || type_ & 4 != 0 {
            super::clif::heal(c, aid, SP_HP, hp);
            super::clif::update_hp(c, aid);
        }
        if sp != 0 {
            super::clif::heal(c, aid, SP_SP, sp);
        }
        if ap != 0 {
            super::clif::heal(c, aid, SP_AP, ap);
        }
    } else {
        if hp != 0 {
            super::clif::updatestatus(c, aid, SP_HP);
        }
        if sp != 0 {
            super::clif::updatestatus(c, aid, SP_SP);
        }
        if ap != 0 {
            super::clif::updatestatus(c, aid, SP_AP);
        }
    }
}

/// status.cpp apply_rate for the percent functions (uint32 * int32 rate / 100 in 64 bits).
fn rate_of(v: u32, rate: i32) -> u32 {
    apply_rate(v, rate)
}

/// status.cpp status_percent_change. Rates > 0: percent of the current value; < 0: percent of the maximum.
/// flag 0: heal; 1: damage; 2: damage that must not kill.
pub fn status_percent_change(c: &Ctx, src: Option<&Unit>, target: u32, hp_rate: i8, sp_rate: i8, ap_rate: i8, flag: u8) -> i32 {
    let Some(status) = super::status_data_of(c, target) else { return 0 };
    let (mut hp, mut sp, mut ap): (u32, u32, u32) = (0, 0, 0);
    // It's safe now [MarkZD]
    if hp_rate > 99 {
        hp = status.hp;
    } else if hp_rate > 0 {
        hp = rate_of(status.hp, hp_rate as i32);
    } else if hp_rate < -99 {
        hp = status.max_hp;
    } else if hp_rate < 0 {
        hp = rate_of(status.max_hp, -(hp_rate as i32));
    }
    if hp_rate != 0 && hp == 0 {
        hp = 1;
    }
    if flag == 2 && hp >= status.hp {
        hp = status.hp.wrapping_sub(1); // Must not kill target.
    }
    if sp_rate > 99 {
        sp = status.sp;
    } else if sp_rate > 0 {
        sp = rate_of(status.sp, sp_rate as i32);
    } else if sp_rate < -99 {
        sp = status.max_sp;
    } else if sp_rate < 0 {
        sp = rate_of(status.max_sp, -(sp_rate as i32));
    }
    if sp_rate != 0 && sp == 0 {
        sp = 1;
    }
    if ap_rate > 99 {
        ap = status.ap;
    } else if ap_rate > 0 {
        ap = rate_of(status.ap, ap_rate as i32);
    } else if ap_rate < -99 {
        ap = status.max_ap;
    } else if ap_rate < 0 {
        ap = rate_of(status.max_ap, -(ap_rate as i32));
    }
    if ap_rate != 0 && ap == 0 {
        ap = 1;
    }
    let dflag = if src.is_none() || src.map(|s| s.id) == Some(target) { 5 } else { 1 };
    // Ugly check in case damage dealt is too much for the received args of status_heal / status_damage. [Skotlex]
    if hp > i32::MAX as u32 {
        hp -= i32::MAX as u32;
        if flag != 0 {
            status_damage(c, src, target, i32::MAX as i64, 0, 0, 0, dflag, 0);
        } else {
            status_heal(c, target, i32::MAX as i64, 0, 0, 0);
        }
    }
    if sp > i32::MAX as u32 {
        sp -= i32::MAX as u32;
        if flag != 0 {
            status_damage(c, src, target, 0, i32::MAX as i64, 0, 0, dflag, 0);
        } else {
            status_heal(c, target, 0, i32::MAX as i64, 0, 0);
        }
    }
    if ap > i32::MAX as u32 {
        ap -= i32::MAX as u32;
        if flag != 0 {
            status_damage(c, src, target, 0, 0, i32::MAX as i64, 0, dflag, 0);
        } else {
            status_heal(c, target, 0, 0, i32::MAX as i64, 0);
        }
    }
    if flag != 0 {
        return status_damage(c, src, target, hp as i64, sp as i64, ap as i64, 0, dflag, 0);
    }
    status_heal(c, target, hp as i64, sp as i64, ap as i64, 0)
}

/// status.hpp status_percent_heal.
pub fn status_percent_heal(c: &Ctx, bl: u32, hp_rate: i32, sp_rate: i32, ap_rate: i32) -> i32 {
    status_percent_change(c, None, bl, (hp_rate as i8).wrapping_neg(), (sp_rate as i8).wrapping_neg(), (ap_rate as i8).wrapping_neg(), 0)
}

/// status.hpp status_percent_damage.
pub fn status_percent_damage(c: &Ctx, src: Option<&Unit>, target: u32, hp_rate: i32, sp_rate: i32, ap_rate: i32, kill: bool) -> i32 {
    status_percent_change(c, src, target, hp_rate as i8, sp_rate as i8, ap_rate as i8, if kill { 1 } else { 2 })
}

/// status.hpp status_kill: instant kill with no drops / exp.
pub fn status_kill(c: &Ctx, bl: u32) -> i32 {
    status_percent_damage(c, None, bl, 100, 0, 0, true)
}

/// status.cpp status_revive.
pub fn status_revive(c: &Ctx, bl: u32, per_hp: u8, per_sp: u8, per_ap: u8) -> i32 {
    let Some(u) = unit::get(c, bl) else { return 0 };
    if !sc::status_isdead(c, &u) {
        return 0;
    }
    if u.kind == crate::map::BL_MOB {
        // mob_revive: the monster system
        return crate::mob::dead::status_revive_mob(c, bl, per_hp, per_sp, per_ap);
    }
    if u.kind == crate::map::BL_HOM {
        // hom_revive (status.cpp:2021): homunculus/hp.rs
        return crate::homunculus::hp::status_revive_hom(c, bl, per_hp, per_sp, per_ap);
    }
    if !is_pc(c, bl) {
        // mercenaries / elementals: not ported (rAthena adds hp / sp to their status and sends clif_resurrection,
        // status.cpp:1993-2017, then its switch has no arm for them; mercenary_dead / elemental_dead delete one at its
        // death, so none is ever dead here)
        return 0;
    }
    let Some(status) = super::status_data_of(c, bl) else { return 0 };
    let mut hp = (status.max_hp as i64 * per_hp as i64 / 100) as u32;
    let mut sp = (status.max_sp as i64 * per_sp as i64 / 100) as u32;
    let mut ap = (status.max_ap as i64 * per_ap as i64 / 100) as u32;
    if hp > status.max_hp.wrapping_sub(status.hp) {
        hp = status.max_hp.wrapping_sub(status.hp);
    } else if per_hp != 0 && hp == 0 {
        hp = 1;
    }
    if sp > status.max_sp.wrapping_sub(status.sp) {
        sp = status.max_sp.wrapping_sub(status.sp);
    } else if per_sp != 0 && sp == 0 {
        sp = 1;
    }
    if ap > status.max_ap.wrapping_sub(status.ap) {
        ap = status.max_ap.wrapping_sub(status.ap);
    } else if per_ap != 0 && ap == 0 {
        ap = 1;
    }
    data::with_sd(c, bl, |sd| {
        sd.battle_status.hp = sd.battle_status.hp.wrapping_add(hp);
        sd.battle_status.sp = sd.battle_status.sp.wrapping_add(sp);
        sd.battle_status.ap = sd.battle_status.ap.wrapping_add(ap);
    });
    super::calc::sync_unit(c, bl);
    if u.on_map {
        // Animation only if character is already on a map.
        super::clif::resurrection(c, &u);
    }
    pc_revive(c, bl, hp, sp, ap);
    1
}

/// pc.cpp pc_revive.
pub fn pc_revive(c: &Ctx, aid: u32, hp: u32, sp: u32, ap: u32) {
    if hp != 0 {
        super::clif::updatestatus(c, aid, SP_HP);
    }
    if sp != 0 {
        super::clif::updatestatus(c, aid, SP_SP);
    }
    if ap != 0 {
        super::clif::updatestatus(c, aid, SP_AP);
    }
    crate::pc::setstand(c, aid, true);
    if let Some(mut u) = unit::get(c, aid) {
        u.dead = false;
        unit::save(c, &u);
    }
    crate::pc::setinvincibletimer(c, aid);
    // pc.cpp:10157-10162: a guild master's auras are placed again
    crate::guild::guildaura_init(c, aid);
}

/// pc.cpp pc_setrestartvalue: type&1 normal resurrection; otherwise only the values saved on the char-server (as if
/// respawned).
pub fn pc_setrestartvalue(c: &Ctx, aid: u32, type_: i32) {
    let Some(sd) = data::sd_get(c, aid) else { return };
    let b = sd.base_status.clone();
    if type_ & 1 != 0 {
        // Normal resurrection
        data::with_sd(c, aid, |sd| sd.battle_status.hp = 1); // Otherwise status_heal may fail if dead.
        status_heal(c, aid, b.hp as i64, 0, 0, 1);
        let cur_sp = data::sd_get(c, aid).map(|s| s.battle_status.sp).unwrap_or(0);
        if cur_sp < b.sp {
            status_set_sp(c, aid, b.sp, 1);
        }
    } else {
        // Just for saving on the char-server (with values as if respawned)
        let Some(mut ch) = crate::pc::character_of(c, aid) else { return };
        let st = &sd.battle_status;
        ch.hp = b.hp;
        ch.sp = if st.sp < b.sp { b.sp } else { st.sp };
        ch.ap = if st.ap < b.ap { b.ap } else { st.ap };
        super::put_char(c, &ch);
    }
}

/// pc.cpp pc_itemheal: an item's HP / SP recovery with the character's bonuses.
pub fn pc_itemheal(c: &Ctx, aid: u32, itemid: u32, mut hp: i32, mut sp: i32) -> i32 {
    let Some(sd) = data::sd_get(c, aid) else { return 0 };
    let scl = sc::load(c, aid);
    let chk = |s| crate::skill::checkskill(c, aid, s) as i32;
    use crate::db::skills::*;
    let pf = potion_flag();
    if hp != 0 {
        let mut bonus = 100 + (sd.battle_status.vit as i32 * 2) + chk(SM_RECOVERY) * 10 + chk(AM_LEARNINGPOTION) * 5;
        // A potion produced by an Alchemist in the Fame Top 10 gets +50% effect [DracoRPG]
        if pf == 2 {
            bonus += bonus * 50 / 100;
            if scl.has(SC_SPIRIT) && scl.v2(SC_SPIRIT) == SL_ROGUE as i32 {
                bonus += bonus; // Receive an additional +100% effect from ranked potions to HP only
            }
        }
        // All item bonuses.
        bonus += sd.bonus.itemhealrate2;
        // Item Group bonuses
        bonus += bonus * pc_get_itemgroup_bonus(itemid, &sd.lists.itemgrouphealrate) / 100;
        // Individual item bonuses.
        if let Some(it) = sd.lists.itemhealrate.iter().find(|it| it.id as u32 == itemid) {
            bonus += bonus * it.val / 100;
        }
        // Recovery Potion
        if scl.has(SC_INCHEALRATE) {
            bonus += bonus * scl.v1(SC_INCHEALRATE) / 100;
        }
        let tmp = hp.wrapping_mul(bonus) / 100; // Overflow check
        if bonus != 100 && tmp > hp {
            hp = tmp;
        }
    }
    if sp != 0 {
        let mut bonus = 100 + (sd.battle_status.int_ as i32 * 2) + chk(MG_SRECOVERY) * 10 + chk(AM_LEARNINGPOTION) * 5;
        if pf == 2 {
            bonus += bonus * 50 / 100;
        }
        bonus += sd.bonus.itemsphealrate2;
        bonus += bonus * pc_get_itemgroup_bonus(itemid, &sd.lists.itemgroupsphealrate) / 100;
        if let Some(it) = sd.lists.itemsphealrate.iter().find(|it| it.id as u32 == itemid) {
            bonus += bonus * it.val / 100;
        }
        let tmp = sp.wrapping_mul(bonus) / 100; // Overflow check
        if bonus != 100 && tmp > sp {
            sp = tmp;
        }
    }
    if !scl.empty() {
        let mut penalty = 0;
        // Critical Wound and Death Hurt stack
        if scl.has(SC_CRITICALWOUND) {
            penalty += scl.v2(SC_CRITICALWOUND);
        }
        if scl.has(SC_DEATHHURT) && scl.v3(SC_DEATHHURT) == 1 {
            penalty += 20;
        }
        if scl.has(SC_NORECOVER_STATE) {
            penalty = 100;
        }
        if scl.has(SC_VITALITYACTIVATION) {
            hp += hp / 2; // 1.5 times
        }
        if scl.has(SC_WATER_INSIGNIA) && scl.v1(SC_WATER_INSIGNIA) == 2 {
            hp += hp / 10;
            sp += sp / 10;
        }
        // RENEWAL
        if scl.has(SC_APPLEIDUN) {
            hp += scl.v3(SC_APPLEIDUN) / 100;
        }
        if penalty > 0 {
            hp -= hp * penalty / 100;
            sp -= sp * penalty / 100;
        }
        if scl.has(SC_EXTREMITYFIST) {
            sp = 0;
        }
        if scl.has(SC_BITESCAR) {
            hp = 0;
        }
    }
    status_heal(c, aid, hp as i64, sp as i64, 0, 1)
}

/// pc.cpp pc_get_itemgroup_bonus: the sum of the bonuses of every group the item belongs to, in rAthena's int16
/// (it wraps), group id 0 skipped.
fn pc_get_itemgroup_bonus(nameid: u32, bonuses: &[data::ItemBonus]) -> i32 {
    let mut bonus: i16 = 0;
    for it in bonuses {
        if it.id == 0 {
            continue;
        }
        if crate::db::items::group::itemdb_group_item_exists(it.id, nameid) {
            bonus = bonus.wrapping_add(it.val as i16);
        }
    }
    bonus as i32
}

/// pc.cpp pc_percentheal: heal / damage by rate (negative rates damage by percent of the maximum).
pub fn pc_percentheal(c: &Ctx, aid: u32, mut hp: i32, mut sp: i32) -> i32 {
    hp = hp.clamp(-100, 100);
    sp = sp.clamp(-100, 100);
    if hp >= 0 && sp >= 0 {
        // Heal
        return status_percent_heal(c, aid, hp, sp, 0);
    }
    if hp <= 0 && sp <= 0 {
        // Damage (negative rates indicate % of max rather than current), and only kill target IF the specified amount is 100%
        return status_percent_damage(c, None, aid, hp, sp, 0, hp == -100);
    }
    // Crossed signs
    if hp != 0 {
        if hp > 0 {
            status_percent_heal(c, aid, hp, 0, 0);
        } else {
            status_percent_damage(c, None, aid, hp, 0, 0, hp == -100);
        }
    }
    if sp != 0 {
        if sp > 0 {
            status_percent_heal(c, aid, 0, sp, 0);
        } else {
            status_percent_damage(c, None, aid, 0, sp, 0, false);
        }
    }
    0
}

// ---------------------------------------------------------------------------------------------- natural heal

/// status.cpp do_init_status: `add_timer_interval(natural_heal_prev_tick = gettick() + NATURAL_HEAL_INTERVAL,
/// status_natural_heal_timer, 0, 0, NATURAL_HEAL_INTERVAL)`. The module has no server start; the timer is created
/// the first time a character enters the world (and again if it is ever gone).
pub fn ensure_natural_heal_timer(c: &Ctx) {
    let mut w = data::world(c);
    if w.natural_heal_timer != 0 && timer::get(c, w.natural_heal_timer).is_some() {
        return;
    }
    w.natural_heal_prev_tick = c.tick() + NATURAL_HEAL_INTERVAL;
    w.natural_heal_timer = timer::add_interval(c, w.natural_heal_prev_tick, Func::StatusNaturalHeal, 0, 0, NATURAL_HEAL_INTERVAL);
    data::put_world(c, w);
}

/// status.cpp status_natural_heal_timer: every NATURAL_HEAL_INTERVAL, the regen of every unit in regen_db.
pub fn status_natural_heal_timer(c: &Ctx, tick: Tick) {
    let mut w = data::world(c);
    w.natural_heal_diff_tick = tick - w.natural_heal_prev_tick;
    w.natural_heal_prev_tick = tick;
    data::put_world(c, w.clone());
    // map_foreachregen (map.cpp:2568-2586): a DBIterator over regen_db (an int DBMap: common/db.cpp bucket id % 283,
    // then ascending id), which holds every unit of a BL_REGEN type (BL_PC|BL_HOM|BL_MER|BL_ELEM, status.hpp:3354)
    // from map_addiddb to map_deliddb (map.cpp:2187-2188, 2212-2213): the players, homunculi, mercenaries and
    // elementals, walked together in that one order.
    #[derive(Clone, Copy, PartialEq)]
    enum Regen {
        Pc,
        Hom,
        MerElem,
    }
    let mut ids: Vec<(u32, Regen)> = c.db().pc_status().iter().map(|s| (s.account_id, Regen::Pc)).collect();
    ids.extend(crate::homunculus::hp::regen_ids(c).into_iter().map(|id| (id, Regen::Hom)));
    ids.extend(crate::companion::regen_ids(c).into_iter().map(|id| (id, Regen::MerElem)));
    ids.sort_unstable_by_key(|&(id, _)| crate::party::idb_order(id));
    for (id, kind) in ids {
        if kind == Regen::Hom {
            // (a homunculus freed by an earlier heal in this loop is gone from regen_db: status_natural_heal_hom
            // finds no row)
            crate::homunculus::hp::status_natural_heal_hom(c, id, w.natural_heal_prev_tick, w.natural_heal_diff_tick);
            continue;
        }
        if kind == Regen::MerElem {
            // (likewise a mercenary / elemental freed earlier in this loop: no row)
            crate::companion::hp::status_natural_heal_comp(c, id, w.natural_heal_prev_tick, w.natural_heal_diff_tick);
            continue;
        }
        let active = crate::pc::get(c, id).map(|p| p.active).unwrap_or(false);
        if active {
            status_natural_heal(c, id, w.natural_heal_prev_tick, w.natural_heal_diff_tick);
        }
    }
}

use spacetimedb::Table;
use super::data::pc_status;

/// status.cpp status_natural_heal for a player (with the Project Loki patch: sitting players recover
/// loki_sit_regen_rate times the normal amount per tick). Returns the regen flags applied.
fn status_natural_heal(c: &Ctx, aid: u32, natural_heal_prev_tick: Tick, natural_heal_diff_tick: Tick) -> u8 {
    let Some(sd) = data::sd_get(c, aid) else { return 0 };
    let Some(u) = unit::get(c, aid) else { return 0 };
    let status = sd.battle_status.clone();
    let regen = sd.regen.clone();
    let scl = sc::load(c, aid);
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
    if flag != 0 && (sc::status_isdead(c, &u) || (!scl.empty() && (u.sc.option & (OPTION_HIDE | OPTION_CLOAK | OPTION_CHASEWALK) != 0 || scl.has(SC__INVISIBILITY)))) {
        flag = RGN_NONE;
    }
    if !sd.lists.hp_loss.is_empty() || !sd.lists.sp_loss.is_empty() {
        pc_bleeding(c, aid, natural_heal_diff_tick);
    }
    if !sd.lists.hp_regen.is_empty() || !sd.lists.sp_regen.is_empty() || !sd.lists.percent_hp_regen.is_empty() || !sd.lists.percent_sp_regen.is_empty() {
        pc_regen(c, aid, natural_heal_diff_tick);
    }
    let mut ticks = data::regen_tick(c, aid);
    let sitting = unit::get(c, aid).map(|u| u.vd.dead_sit == 2).unwrap_or(false);
    if flag & (RGN_SHP | RGN_SSP) != 0 && regen.has_sub && sitting {
        // Apply sitting regen bonus.
        let sregen = &sd.ssregen;
        if flag & RGN_SHP != 0 {
            // Sitting HP regen
            let mut rate = (natural_heal_diff_tick as f64 * (sregen.rate_hp as f64 / 100.)) as i32;
            if regen.overweight {
                rate /= 2; // Half as fast when overweight.
            }
            ticks.ss_hp = ticks.ss_hp.wrapping_add(rate as u32);
            while ticks.ss_hp >= crate::conf::natural_heal_skill_interval() as u32 {
                ticks.ss_hp -= crate::conf::natural_heal_skill_interval() as u32;
                data::put_regen_tick(c, &ticks);
                if status_heal(c, aid, sregen.hp as i64, 0, 0, 3) < sregen.hp as i32 {
                    // Full
                    flag &= !RGN_SHP;
                    break;
                }
            }
        }
        if flag & RGN_SSP != 0 {
            // Sitting SP regen
            let mut rate = (natural_heal_diff_tick as f64 * (sregen.rate_sp as f64 / 100.)) as i32;
            if regen.overweight {
                rate /= 2; // Half as fast when overweight.
            }
            ticks.ss_sp = ticks.ss_sp.wrapping_add(rate as u32);
            while ticks.ss_sp >= crate::conf::natural_heal_skill_interval() as u32 {
                ticks.ss_sp -= crate::conf::natural_heal_skill_interval() as u32;
                data::put_regen_tick(c, &ticks);
                if status_heal(c, aid, 0, sregen.sp as i64, 0, 3) < sregen.sp as i32 {
                    // Full
                    flag &= !RGN_SSP;
                    break;
                }
            }
        }
        data::put_regen_tick(c, &ticks);
    }
    if flag != 0 && regen.overweight {
        flag = RGN_NONE;
    }
    let walking = unit::get(c, aid).map(|u| u.ud.walktimer != 0).unwrap_or(false);
    if walking {
        flag &= !(RGN_SHP | RGN_SSP);
        if !regen.walk {
            flag &= !RGN_HP;
        }
    }
    // Project Loki: sitting players recover loki_sit_regen_rate times the normal amount per tick
    let sitting = unit::get(c, aid).map(|u| u.vd.dead_sit == 2).unwrap_or(false);
    let loki_sitmul: i64 = if sitting { crate::conf::loki_sit_regen_rate() } else { 1 };
    let mut multi = 1;
    if flag & (RGN_HP | RGN_SP) != 0 {
        if sitting {
            multi += 1; // This causes the interval to be halved
        }
        if regen.gc {
            multi += 1; // This causes the interval to be halved
        }
    }
    let mut ticks = data::regen_tick(c, aid);
    // Natural Hp regen
    if flag & RGN_HP != 0 {
        // Interval to next recovery tick
        let mut rate = (crate::conf::natural_healhp_interval() as f64 / (regen.rate_hp as f64 / 100. * multi as f64)) as i32;
        // Half recovery while moving only applies to players with certain traits
        if walking {
            rate *= 2;
        }
        // Our timer system isn't 100% accurate so make sure we use the closest interval
        rate -= (NATURAL_HEAL_INTERVAL / 2) as i32;
        if ticks.hp + rate as i64 <= natural_heal_prev_tick {
            ticks.hp = natural_heal_prev_tick;
            data::put_regen_tick(c, &ticks);
            let cur = super::status_data_of(c, aid).unwrap_or(status.clone());
            if cur.hp >= cur.max_hp {
                flag &= !(RGN_HP | RGN_SHP);
            } else if (status_heal(c, aid, regen.hp as i64 * loki_sitmul, 0, 0, 1) as i64) < regen.hp as i64 * loki_sitmul {
                flag &= !RGN_SHP; // Full
            }
        }
    } else {
        ticks.hp = natural_heal_prev_tick;
        data::put_regen_tick(c, &ticks);
    }
    let mut ticks = data::regen_tick(c, aid);
    // Natural SP regen
    if flag & RGN_SP != 0 {
        // Interval to next recovery tick
        let mut rate = (crate::conf::natural_healsp_interval() as f64 / (regen.rate_sp as f64 / 100. * multi as f64)) as i32;
        // RENEWAL: Tick is doubled in Fury state
        if (sd.class_ & crate::db::jobs::MAPID_SECONDMASK) == crate::db::jobs::MAPID_MONK && scl.has(SC_EXPLOSIONSPIRITS) && (!scl.has(SC_SPIRIT) || scl.v2(SC_SPIRIT) != crate::db::skills::SL_MONK as i32) {
            rate *= 2;
        }
        // Our timer system isn't 100% accurate so make sure we use the closest interval
        rate -= (NATURAL_HEAL_INTERVAL / 2) as i32;
        if ticks.sp + rate as i64 <= natural_heal_prev_tick {
            ticks.sp = natural_heal_prev_tick;
            data::put_regen_tick(c, &ticks);
            let cur = super::status_data_of(c, aid).unwrap_or(status.clone());
            if cur.sp >= cur.max_sp {
                flag &= !(RGN_SP | RGN_SSP);
            } else if (status_heal(c, aid, 0, regen.sp as i64 * loki_sitmul, 0, 1) as i64) < regen.sp as i64 * loki_sitmul {
                flag &= !RGN_SSP; // Full
            }
        }
    } else {
        ticks.sp = natural_heal_prev_tick;
        data::put_regen_tick(c, &ticks);
    }
    if !regen.has_sub {
        return flag;
    }
    // Skill regen
    let sregen = &sd.sregen;
    let mut ticks = data::regen_tick(c, aid);
    if flag & RGN_SHP != 0 {
        // Skill HP regen
        ticks.s_hp = ticks.s_hp.wrapping_add((natural_heal_diff_tick as f64 * (sregen.rate_hp as f64 / 100.)) as i32 as u32);
        while ticks.s_hp >= crate::conf::natural_heal_skill_interval() as u32 {
            ticks.s_hp -= crate::conf::natural_heal_skill_interval() as u32;
            data::put_regen_tick(c, &ticks);
            if status_heal(c, aid, sregen.hp as i64, 0, 0, 3) < sregen.hp as i32 {
                break; // Full
            }
        }
        data::put_regen_tick(c, &ticks);
    }
    if flag & RGN_SSP != 0 {
        // Skill SP regen
        ticks.s_sp = ticks.s_sp.wrapping_add((natural_heal_diff_tick as f64 * (sregen.rate_sp as f64 / 100.)) as i32 as u32);
        while ticks.s_sp >= crate::conf::natural_heal_skill_interval() as u32 {
            let mut val = sregen.sp as i32;
            if data::sd_get(c, aid).map(|s| s.doridori).unwrap_or(false) {
                val *= 2;
                data::with_sd(c, aid, |s| s.doridori = false);
                let rate = crate::skill::checkskill(c, aid, crate::db::skills::TK_SPTIME) as i32;
                if rate != 0 {
                    if let Some(u) = unit::get(c, aid) {
                        let t = crate::db::skills::skill_get_sc(crate::db::skills::TK_SPTIME) as i32;
                        sc::sc_start(c, Some(&u), &u, t, 100, rate, crate::db::skills::skill_get_time(crate::db::skills::TK_SPTIME, rate as u16) as i64);
                    }
                }
                // battle_config.sg_angel_skill_ratio (skill.conf: 10)
                if (sd.class_ & crate::db::jobs::MAPID_SECONDMASK) == crate::db::jobs::MAPID_STAR_GLADIATOR && (c.rnd() % 10000) < 10 {
                    // Angel of the Sun/Moon/Star
                    super::sc_ext::clif_feel_hate_reset(c, aid);
                    crate::skilltree::pc_resethate(c, aid);
                    crate::skilltree::pc_resetfeel(c, aid);
                }
            }
            ticks.s_sp -= crate::conf::natural_heal_skill_interval() as u32;
            data::put_regen_tick(c, &ticks);
            if status_heal(c, aid, 0, val as i64, 0, 3) < val {
                break; // Full
            }
        }
        data::put_regen_tick(c, &ticks);
    }
    flag
}

/// pc.cpp pc_bleeding: bHPLossRate / bSPLossRate drains.
pub fn pc_bleeding(c: &Ctx, aid: u32, diff_tick: Tick) {
    if crate::pc::isdead(c, aid) {
        return;
    }
    let Some(mut sd) = data::sd_get(c, aid) else { return };
    let (mut hp, mut sp) = (0i32, 0i32);
    let cur_hp = sd.battle_status.hp as i32;
    for it in sd.lists.hp_loss.iter_mut() {
        it.tick += diff_tick;
        while it.tick >= it.rate as i64 {
            hp += it.value as i32;
            it.tick -= it.rate as i64;
        }
        if hp >= cur_hp {
            hp = cur_hp - 1; // Script drains cannot kill you.
        }
    }
    for it in sd.lists.sp_loss.iter_mut() {
        it.tick += diff_tick;
        while it.tick >= it.rate as i64 {
            sp += it.value as i32;
            it.tick -= it.rate as i64;
        }
    }
    data::sd_put(c, &sd);
    if hp > 0 || sp > 0 {
        status_zap(c, aid, hp as i64, sp as i64, 0);
    }
}

/// pc.cpp pc_regen: bHPRegenRate / bSPRegenRate / bRegenPercentHP / bRegenPercentSP (the percent ones read the
/// char-server's max_hp / max_sp, as rAthena does).
pub fn pc_regen(c: &Ctx, aid: u32, diff_tick: Tick) {
    let Some(mut sd) = data::sd_get(c, aid) else { return };
    let Some(ch) = crate::pc::character_of(c, aid) else { return };
    let (mut hp, mut sp) = (0i32, 0i32);
    for it in sd.lists.hp_regen.iter_mut() {
        it.tick += diff_tick;
        while it.tick >= it.rate as i64 {
            hp += it.value as i32;
            it.tick -= it.rate as i64;
        }
    }
    for it in sd.lists.sp_regen.iter_mut() {
        it.tick += diff_tick;
        while it.tick >= it.rate as i64 {
            sp += it.value as i32;
            it.tick -= it.rate as i64;
        }
    }
    for it in sd.lists.percent_hp_regen.iter_mut() {
        it.tick += diff_tick;
        while it.tick >= it.rate as i64 {
            hp = hp.wrapping_add((ch.max_hp as i64 * it.value as i64 / 100) as i32);
            it.tick -= it.rate as i64;
        }
    }
    for it in sd.lists.percent_sp_regen.iter_mut() {
        it.tick += diff_tick;
        while it.tick >= it.rate as i64 {
            sp = sp.wrapping_add((ch.max_sp as i64 * it.value as i64 / 100) as i32);
            it.tick -= it.rate as i64;
        }
    }
    data::sd_put(c, &sd);
    if hp > 0 || sp > 0 {
        status_heal(c, aid, hp as i64, sp as i64, 0, 0);
    }
}
