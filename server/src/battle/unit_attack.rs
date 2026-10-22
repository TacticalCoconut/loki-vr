//! unit.cpp's attack parts: unit_attack, unit_attack_timer / unit_attack_timer_sub, unit_can_attack,
//! unit_set_attackdelay, unit_set_walkdelay, unit_stopattack, unit_step_timer (the attack remembered while walking)
//! and unit_is_walking. unit_stop_attack / unit_set_target / unit_unattackable / unit_stop_stepaction are in unit.rs.
//!
//! Timer ids are the module's (0 = INVALID_TIMER). A unit row read before a call that may change it is read again
//! after it (the functions below save what they change before calling out).

use super::mobif;
use super::target::{battle_check_range, battle_check_target};
use super::*;
use crate::clif;
use crate::map::{BL_HOM, BL_MER, BL_MOB, BL_NPC, BL_PC, BL_PET};
use crate::timer::{self, Func};
use crate::unit::{self, Unit, USW_FIXPOS, USW_MOVE_FULL_CELL, USW_NONE, USW_RELEASE_TARGET};
use crate::world::{Ctx, Tick};

/// unit.hpp enum e_delay_event.
pub const DELAY_EVENT_ATTACK: i32 = 1;
pub const DELAY_EVENT_CASTBEGIN_ID: i32 = 2;
pub const DELAY_EVENT_CASTBEGIN_POS: i32 = 3;
pub const DELAY_EVENT_CASTEND: i32 = 4;
pub const DELAY_EVENT_CASTCANCEL: i32 = 5;
pub const DELAY_EVENT_DAMAGED: i32 = 6;
pub const DELAY_EVENT_PARRY: i32 = 7;

/// unit.cpp unit_is_walking.
pub fn unit_is_walking(u: &Unit) -> bool {
    u.ud.walktimer != 0
}

/// unit.cpp unit_can_attack: status changes and blocks that forbid attacking (also for ground attacks).
pub fn unit_can_attack(c: &Ctx, bl: &Unit, target_id: u32) -> bool {
    use crate::status::consts::*;
    if bl.kind == BL_PC {
        let blocked = crate::pc::get(c, bl.id).map(|p| p.block_action & crate::pc::PCBLOCK_ATTACK != 0).unwrap_or(true);
        // pc_isridingwug: OPTION_WUGRIDER
        if blocked || bl.sc.option & crate::status::defs::OPTION_WUGRIDER != 0 {
            return false;
        }
    }
    if !mobif::has_sc(bl.kind) {
        return true;
    }
    if crate::status::sc::cant(c, bl.id, SCS_NOATTACK) {
        return false;
    }
    let scl = crate::status::sc::load(c, bl.id);
    if scl.has(SC_VOICEOFSIREN) && scl.v2(SC_VOICEOFSIREN) == target_id as i32 {
        return false;
    }
    if bl.kind != BL_PC && scl.has(SC_WINKCHARM) && scl.v2(SC_WINKCHARM) == target_id as i32 {
        return false;
    }
    true
}

/// unit.cpp unit_attack: a unit is asked to attack `target_id` (continuous &1: keep attacking, &2: from
/// unit_step_timer). Returns how the caller should stop walking (USW_*).
pub fn unit_attack(c: &Ctx, src_id: u32, target_id: u32, continuous: i32) -> i32 {
    let Some(src) = unit::get(c, src_id) else { return USW_NONE };
    let mut target_id = target_id;
    // Check for special monster random target mode, function might overwrite the original target
    if src.kind == BL_MOB && !mobif::randomtarget(c, &src, &mut target_id) {
        return USW_NONE; // Continue walking
    }
    let mut stop_flag = USW_FIXPOS | USW_RELEASE_TARGET;
    let target = unit::get(c, target_id);
    let Some(target) = target.filter(|t| !crate::status::sc::status_isdead(c, t)) else {
        let mut s = src;
        unit::unattackable(c, &mut s);
        unit::save(c, &s);
        return stop_flag;
    };
    if src.kind == BL_PC && target.kind == BL_NPC {
        // Monster npcs [Valaris]
        crate::script::npc_run::npc_click(c, src.id, target.id);
        return stop_flag;
    }
    if !unit_can_attack(c, &src, target_id) {
        let mut s = src;
        unit::stop_attack(c, &mut s);
        unit::save(c, &s);
        return USW_NONE; // Continue walking
    }
    if battle_check_target(c, &src, &target, BCT_ENEMY) <= 0 || !mobif::status_check_skilluse(c, Some(&src), Some(&target), 0, 0) {
        let mut s = src;
        unit::unattackable(c, &mut s);
        unit::save(c, &s);
        return stop_flag;
    }
    let mut s = unit::get(c, src_id).unwrap_or(src);
    s.ud.attack_continue = continuous & 1 != 0;
    s.ud.step_attack = continuous & 2 != 0;
    unit::set_target(c, &mut s, target_id);
    // status_get_range
    let sstatus = crate::status::status_get_status_data(c, &s);
    let range = sstatus.rhw.range as i32;
    if continuous != 0 {
        // If you're to attack continously, set to auto-chase character
        s.ud.chaserange = range;
    }
    let extra_range = if (unit_is_walking(&target) || s.ud.step_attack)
        && (target.kind == crate::map::BL_PC || crate::map_cell::getcell(c, Some(target.map), target.x, target.y, crate::map_cell::CELL_CHKICEWALL) == 0)
    {
        1
    } else {
        0
    };
    if s.kind == BL_PC
        && s.ud.walktimer == 0
        && !crate::path::check_distance_client((s.x - target.x) as i32, (s.y - target.y) as i32, range + extra_range)
        && crate::path::path_search_long(s.map, s.x, s.y, target.x, target.y, crate::map::CellChk::ChkWall)
    {
        if s.ud.attacktimer != 0 {
            let _ = timer::delete(c, s.ud.attacktimer);
            s.ud.attacktimer = 0;
        }
        if s.ud.stepaction || s.ud.steptimer != 0 {
            unit::stop_stepaction(c, &mut s);
        }
        unit::save(c, &s);
        clif_movetoattack(c, &s, &target, sstatus.rhw.range);
        return USW_NONE;
    }
    // Just change target/type. [Skotlex]
    if s.ud.attacktimer != 0 {
        unit::save(c, &s);
        return stop_flag;
    }
    // New action request received, delete previous action request if not executed yet
    if s.ud.stepaction || s.ud.steptimer != 0 {
        unit::stop_stepaction(c, &mut s);
    }
    // Remember the attack request from the client while walking to the next cell
    if s.kind == BL_PC && s.ud.walktimer != 0 && !battle_check_range(c, &s, &target, range - 1) {
        s.ud.stepaction = true;
        s.ud.target_to = s.ud.target;
        s.ud.stepskill_id = 0;
        s.ud.stepskill_lv = 0;
        unit::save(c, &s);
        // Attacking will be handled by unit_walktoxy_timer in this case
        return USW_NONE;
    }
    let tick = c.tick();
    if s.ud.attackabletime - tick > 0 {
        // Do attack next time it is possible. [Skotlex]
        s.ud.attacktimer = timer::add(c, s.ud.attackabletime, Func::UnitAttack, s.id, 0);
        unit::save(c, &s);
    } else {
        // Attack NOW. We need to send fixpos before the attack so that we don't cancel the attack animation
        unit::save(c, &s);
        clif::fixpos(c, &s);
        unit_attack_timer(c, 0, tick, s.id);
        stop_flag &= !USW_FIXPOS;
    }
    // Monster state is set regardless of whether the attack is executed now or later
    if s.kind == BL_MOB {
        if let Some(md) = unit::get(c, src_id) {
            mobif::setstate(c, &md, mobif::MSS_BERSERK);
        }
    }
    stop_flag
}

/// unit.cpp unit_attack_timer (Func::UnitAttack; also called directly with tid 0 = INVALID_TIMER).
pub fn unit_attack_timer(c: &Ctx, tid: u64, tick: Tick, id: u32) {
    if unit::get(c, id).is_some() && unit_attack_timer_sub(c, id, tid, tick) == 0 {
        if let Some(mut u) = unit::get(c, id) {
            unit::unattackable(c, &mut u);
            unit::save(c, &u);
            if u.kind == BL_MOB {
                mobif::unlocktarget(c, &u, c.tick());
            }
            // unit_unattackable's BL_PET part: pet_unlocktarget (pet.rs)
            if u.kind == BL_PET {
                crate::pet::pet_unlocktarget(c, u.id);
            }
        }
    }
}

/// unit.cpp unit_attack_timer_sub: one attack of a unit on its target. 1: attackable, 0: not.
fn unit_attack_timer_sub(c: &Ctx, src_id: u32, tid: u64, tick: Tick) -> i32 {
    let Some(mut src) = unit::get(c, src_id) else { return 0 };
    if src.ud.attacktimer != tid {
        log::error!("unit_attack_timer {} != {}", src.ud.attacktimer, tid);
        return 0;
    }
    let is_pc = src.kind == BL_PC;
    let is_mob = src.kind == BL_MOB;
    // Make sure attacktimer is removed before doing anything else
    src.ud.attacktimer = 0;
    unit::save(c, &src);
    // Officially there is no attack timer: monsters' timed attacks go through their AI.
    if is_mob && tid != 0 {
        return mobif::ai_sub_hard_attacktimer(c, &src, tick);
    }
    let Some(target) = unit::get(c, src.ud.target) else { return 0 };
    if !src.on_map || !target.on_map {
        return 0;
    }
    if crate::status::sc::status_isdead(c, &src)
        || crate::status::sc::status_isdead(c, &target)
        || battle_check_target(c, &src, &target, BCT_ENEMY) <= 0
        || !mobif::status_check_skilluse(c, Some(&src), Some(&target), 0, 0)
        // OFFICIAL_WALKPATH
        || !crate::path::path_search_long(src.map, src.x, src.y, target.x, target.y, crate::map::CellChk::ChkWall)
        || !unit_can_attack(c, &src, target.id)
    {
        return 0; // Can't attack under these conditions
    }
    let freecast = is_pc && crate::skill::checkskill(c, src.id, crate::db::skills::SA_FREECAST) > 0;
    if src.ud.skilltimer != 0 && !freecast {
        return 0; // Can't attack while casting
    }
    if !conf::sdelay_attack_enable() && src.ud.canact_tick - tick > 0 && !freecast {
        // Attacking when under cast delay has restrictions:
        if tid == 0 {
            // Requested attack.
            if is_pc {
                crate::skill::skill_fail_ex(c, &src, 1, crate::skill::USESKILL_FAIL_SKILLINTERVAL, 0, 0);
            }
            return 0;
        }
        // Otherwise, we are in a combo-attack, delay this until your canact time is over. [Skotlex]
        if src.ud.attack_continue {
            if src.ud.canact_tick - src.ud.attackabletime > 0 {
                src.ud.attackabletime = src.ud.canact_tick;
            }
            src.ud.attacktimer = timer::add(c, src.ud.attackabletime, Func::UnitAttack, src.id, 0);
            unit::save(c, &src);
        }
        return 1;
    }
    let sstatus = crate::status::status_get_status_data(c, &src);
    let mut range = sstatus.rhw.range as i32;
    // Extra range when chasing (does not apply to mobs locked in an icewall)
    if (unit_is_walking(&target) || src.ud.step_attack)
        && (target.kind == crate::map::BL_PC || crate::map_cell::getcell(c, Some(target.map), target.x, target.y, crate::map_cell::CELL_CHKICEWALL) == 0)
    {
        range += 1;
    }
    let (dx, dy) = ((src.x - target.x) as i32, (src.y - target.y) as i32);
    if is_pc && !crate::path::check_distance_client(dx, dy, range) {
        // Player tries to attack but target is too far, notify client
        clif_movetoattack(c, &src, &target, sstatus.rhw.range);
        return 1;
    } else if is_mob && !crate::path::check_distance(dx, dy, range) {
        // Monster: Chase if required
        let flag = (src.ud.walk_easy as u8) | 2;
        mobif::unit_walktobl(c, &src, &target, src.ud.chaserange, flag);
        return 1;
    }
    if !battle_check_range(c, &src, &target, range) {
        // Within range, but no direct line of attack
        if src.ud.attack_continue && src.ud.chaserange > 1 {
            src.ud.chaserange = (src.ud.chaserange - 2).max(1);
            unit::save(c, &src);
            // Walk closer / around the obstacle and start attacking once you are in range
            let flag = (src.ud.walk_easy as u8) | 2;
            return mobif::unit_walktobl(c, &src, &target, src.ud.chaserange, flag);
        }
        // Can't attack even though next to the target? Giving up here.
        return 0;
    }
    // Sync packet only for players. Non-players use the sync packet on the walk timer. [Skotlex]
    if tid == 0 && is_pc {
        clif::fixpos(c, &src);
    }
    if src.ud.attackabletime - tick <= 0 {
        if conf::attack_direction_change() != 0 && src.kind & conf::attack_direction_change() != 0 {
            let d = crate::map::calc_dir(&src, target.x, target.y);
            unit::setdir(c, &mut src, d, false);
            unit::save(c, &src);
        }
        if src.ud.walktimer != 0 {
            unit::stop_walking(c, &mut src, USW_FIXPOS, 0);
            unit::save(c, &src);
        }
        if is_mob && mobif::has_mode(sstatus.mode, mobif::MD_ASSIST) {
            // Link monsters nearby [Skotlex] (MIN_MOBLINKTIME and md->last_linktime: mob agent)
            mobif::link_assist(c, &src, target.id, tick);
        }
        if src.kind == BL_PET && crate::pet::pet_attackskill(c, src.id, target.id) != 0 {
            return 1;
        }
        // ud->attacktarget_lv (only read right below)
        let lv = super::weapon_attack::battle_weapon_attack(c, src.id, target.id, tick, 0);
        // pet_target_check with pet_attack_support (unit.cpp:3311-3312, pet.rs)
        if src.kind == BL_PC {
            crate::pet::unit_attack_support(c, src.id, &target);
        }
        // Applied when you're unable to attack (e.g. out of ammo)
        if lv == ATK_NONE {
            return 1;
        }
        let Some(mut s) = unit::get(c, src_id) else { return 1 };
        unit_set_attackdelay(c, &mut s, tick, DELAY_EVENT_ATTACK);
        // Only reset skill_id here if no skilltimer is currently ongoing
        if s.ud.skilltimer == 0 {
            s.ud.skill_id = 0;
        }
        unit::save(c, &s);
        // You can't move during your attack motion
        if s.kind & conf::attack_walk_delay() != 0 {
            let amotion = crate::status::status_get_status_data(c, &s).amotion as Tick;
            unit_set_walkdelay(c, s.id, tick, amotion, 1, 0);
        }
    }
    let Some(mut s) = unit::get(c, src_id) else { return 1 };
    if s.ud.attack_continue && !crate::status::sc::status_isdead(c, &s) {
        if s.kind == BL_PC && crate::conf::idletime_option() & IDLE_ATTACK != 0 {
            if let Some(mut p) = crate::pc::get(c, s.id) {
                p.idletime = crate::pc::last_tick(c);
                crate::pc::put(c, &p);
            }
        }
        s.ud.attacktimer = timer::add(c, s.ud.attackabletime, Func::UnitAttack, s.id, 0);
        unit::save(c, &s);
    }
    if is_pc && crate::conf::prevent_logout_trigger() & PLT_ATTACK != 0 {
        if let Some(mut p) = crate::pc::get(c, src_id) {
            p.canlog_tick = c.tick();
            crate::pc::put(c, &p);
        }
    }
    1
}

/// pc.hpp PLT_ATTACK (prevent_logout_trigger).
pub const PLT_ATTACK: i32 = 0x2;
/// status.hpp MAX_ASPD_NOPC, unit.cpp MIN_DELAY_SLAVE; battle.conf `amotion_min_skill_delay: 0` (homunc.conf).
const MAX_ASPD_NOPC: Tick = 100;
const MIN_DELAY_SLAVE: Tick = MAX_ASPD_NOPC * 2;
fn amotion_min_skill_delay() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::AMOTION_MIN_SKILL_DELAY)
}

/// clif.cpp clif_movetoattack: ZC_ATTACK_FAILURE_FOR_DISTANCE (the target is too far).
pub fn clif_movetoattack(c: &Ctx, sd: &Unit, bl: &Unit, range: u16) {
    let p = crate::net::packets::PACKET_ZC_ATTACK_FAILURE_FOR_DISTANCE {
        PacketType: crate::net::packets::PACKET_ZC_ATTACK_FAILURE_FOR_DISTANCE::ID as i16,
        targetAID: bl.id,
        targetXPos: bl.x,
        targetYPos: bl.y,
        xPos: sd.x,
        yPos: sd.y,
        currentAttRange: range as i16,
    };
    clif::send(c, &p.to_vec(), sd, clif::Target::SelfOnly);
}

/// unit.cpp unit_set_attackdelay: the attack / act delays after an event. The unit is updated in place.
pub fn unit_set_attackdelay(c: &Ctx, bl: &mut Unit, tick: Tick, event: i32) {
    let st = crate::status::status_get_status_data(c, bl);
    let mut attack_delay: Tick = 0;
    let mut act_delay: Tick = 0;
    match bl.kind {
        BL_PC => match event {
            DELAY_EVENT_CASTBEGIN_ID | DELAY_EVENT_CASTBEGIN_POS | DELAY_EVENT_ATTACK | DELAY_EVENT_PARRY => {
                unit::mark_pc_combat(c, bl, tick);
                if (event == DELAY_EVENT_CASTBEGIN_ID || event == DELAY_EVENT_CASTBEGIN_POS) && crate::skill::state::get(c, bl.id).skillitem == bl.ud.skill_id as i16 {
                    // Skills used from items don't seem to give any attack or act delay
                    return;
                }
                attack_delay = st.adelay as Tick;
                // A fixed delay equal to the minimum attack motion you can get
                let class_ = crate::status::data::sd_get(c, bl.id).map(|s| s.class_).unwrap_or(0);
                act_delay = st.amotion as Tick + (crate::status::calc::pc_maxaspd(class_) / crate::status::defs::AMOTION_DIVIDER_PC) as Tick;
            }
            _ => {}
        },
        BL_MOB => {
            match event {
                DELAY_EVENT_ATTACK | DELAY_EVENT_CASTEND | DELAY_EVENT_CASTCANCEL => {
                    attack_delay = st.adelay as Tick;
                }
                _ => {}
            }
            // Set monster-specific delays (inactive AI time, monster skill delays)
            mobif::set_delay(c, bl, tick, event);
        }
        BL_HOM => match event {
            // This represents setting of attack delay (recharge time) that happens for non-PCs
            DELAY_EVENT_ATTACK => attack_delay = st.adelay as Tick,
            // Non-PCs controlled from the client: a security delay (or amotion with amotion_min_skill_delay)
            DELAY_EVENT_CASTBEGIN_ID | DELAY_EVENT_CASTBEGIN_POS => {
                act_delay = if amotion_min_skill_delay() == 1 { st.amotion as Tick + MAX_ASPD_NOPC } else { MIN_DELAY_SLAVE };
            }
            _ => {}
        },
        BL_MER => match event {
            DELAY_EVENT_ATTACK => attack_delay = st.adelay as Tick,
            DELAY_EVENT_CASTBEGIN_ID => {
                act_delay = if amotion_min_skill_delay() == 1 { st.amotion as Tick + MAX_ASPD_NOPC } else { MIN_DELAY_SLAVE };
            }
            // For ground skills, mercenaries work similar to players
            DELAY_EVENT_CASTBEGIN_POS => {
                attack_delay = st.adelay as Tick;
                act_delay = st.amotion as Tick + MAX_ASPD_NOPC;
            }
            _ => {}
        },
        _ => match event {
            DELAY_EVENT_ATTACK => attack_delay = st.adelay as Tick,
            DELAY_EVENT_CASTBEGIN_ID | DELAY_EVENT_CASTBEGIN_POS => act_delay = st.amotion as Tick,
            _ => {}
        },
    }
    // Never decrease delays set by another source already
    if attack_delay > 0 {
        bl.ud.attackabletime = (tick + attack_delay).max(bl.ud.attackabletime);
    }
    if act_delay > 0 {
        bl.ud.canact_tick = (tick + act_delay).max(bl.ud.canact_tick);
    }
}

/// unit.cpp unit_set_walkdelay. type_ 0: damage-induced (never shortens, stops at the next cell centre when the
/// legacy walk delay is off); 1: skill / attack-induced (can only be increased). The unit is saved. 1 = set.
pub fn unit_set_walkdelay(c: &Ctx, id: u32, tick: Tick, delay: Tick, type_: i32, skill_id: u16) -> i32 {
    let mut delay = delay;
    let Some(mut u) = unit::get(c, id) else { return 0 };
    if delay <= 0 {
        return 0;
    }
    if type_ != 0 {
        // Bosses can ignore skill induced walkdelay (but not damage induced)
        if u.kind == BL_MOB && mobif::has_mode(crate::status::status_get_status_data(c, &u).mode, crate::status::defs::MD_STATUSIMMUNE) {
            return 0;
        }
        // Make sure walk delay is not decreased
        if u.ud.canmove_tick - (tick + delay) > 0 {
            return 0;
        }
    } else {
        // Mob needs to escape, don't stop it (md->state.can_escape, set by monster skills)
        if u.kind == BL_MOB && crate::mob::get(c, u.id).map(|m| m.can_escape).unwrap_or(false) {
            return 0;
        }
        // Trapped or legacy walk delay system disabled
        if !unit::can_move(c, &u) || u.kind & conf::damage_walk_delay() == 0 {
            // Stop on the closest cell center
            unit::stop_walking(c, &mut u, USW_MOVE_FULL_CELL, 0);
            unit::save(c, &u);
            return 0;
        }
        const MG_FIREWALL: u16 = 18;
        const PR_SANCTUARY: u16 = 70;
        const NJ_KAENSIN: u16 = 537;
        if matches!(skill_id, MG_FIREWALL | PR_SANCTUARY | NJ_KAENSIN) {
            // When using legacy walk delay, these skills should just stop the target
            delay = 1;
        }
        // Immune to being stopped for double the flinch time
        if u.ud.canmove_tick - (tick - delay) > 0 {
            return 0;
        }
    }
    u.ud.canmove_tick = tick + delay;
    if u.ud.walktimer != 0 {
        // Stop walking, if chasing, readjust timers.
        if delay == 1 {
            // Minimal delay (walk-delay) disabled. Just stop walking.
            unit::stop_walking(c, &mut u, USW_NONE, 0);
        } else if u.ud.running {
            // Resume running after can move again [Kevin]
            crate::timer::add(c, u.ud.canmove_tick, crate::timer::Func::UnitResumeRunning, id, 0);
        } else {
            unit::stop_walking(c, &mut u, USW_MOVE_FULL_CELL, 0);
            if u.ud.target_to != 0 {
                // add_timer(canmove_tick + 1, unit_walktobl_sub, id, target_to) (mob agent's unit_walktobl_sub)
                crate::timer::add(c, u.ud.canmove_tick + 1, crate::timer::Func::UnitWalktoblSub, id, u.ud.target_to as i64);
            }
        }
    }
    unit::save(c, &u);
    1
}

/// unit.cpp TIMER_FUNC(unit_resume_running) (Func::UnitResumeRunning, target = the unit): resumes running (RA_WUGDASH
/// or TK_RUN) after a walk delay: a player riding its warg starts SC_WUGDASH again, anyone else SC_RUN, in the unit's
/// facing direction with the level of its last skill (`ud->skill_lv`), with the no-damage packet; a player gets
/// clif_walkok. (rAthena's timer data is the unit_data pointer; a unit gone before the timer is not touched here.)
pub fn unit_resume_running(c: &Ctx, _tid: u64, id: u32) {
    use crate::db::skills::{RA_WUGDASH, TK_RUN};
    use crate::status::consts::{SC_RUN, SC_WUGDASH};
    let Some(u) = unit::get(c, id) else { return };
    let is_pc = u.kind == BL_PC;
    // sd && pc_isridingwug(sd)
    let (skill_id, t) = if is_pc && u.sc.option & crate::status::defs::OPTION_WUGRIDER != 0 { (RA_WUGDASH, SC_WUGDASH) } else { (TK_RUN, SC_RUN) };
    let ok = crate::status::sc::sc_start4(c, Some(&u), &u, t, 100, u.ud.skill_lv as i32, unit::getdir(&u) as i32, 0, 0, 0);
    let u2 = unit::get(c, id).unwrap_or(u);
    crate::skill::clif::clif_skill_nodamage(c, Some(&u2), &u2, skill_id, u2.ud.skill_lv as i32, ok);
    if is_pc {
        crate::clif::walkok(c, &u2);
    }
}

/// unit.cpp unit_stopattack: map_foreach helper that stops a unit's auto-attack (all of them with id 0, or only
/// those attacking `id`). 1 when it stopped one.
pub fn unit_stopattack(c: &Ctx, bl: &Unit, id: u32) -> i32 {
    if bl.ud.attacktimer != 0 && (id == 0 || id == bl.ud.target) {
        let mut u = bl.clone();
        unit::stop_attack(c, &mut u);
        unit::save(c, &u);
        return 1;
    }
    0
}

/// unit.cpp unit_walktoxy_timer's stepaction part (called by unit.rs after a step): the request remembered while
/// walking runs half a step later, at the full cell.
pub fn walktoxy_arm_steptimer(c: &Ctx, u: &mut Unit, tick: Tick) {
    if u.ud.stepaction && u.ud.target_to != 0 {
        // Delete old stepaction even if not executed yet, the latest command is what counts
        if u.ud.steptimer != 0 {
            timer::delete(c, u.ud.steptimer);
            u.ud.steptimer = 0;
        }
        // Delay stepactions by half a step (so they are executed at full step)
        let dir = u.ud.path.get(u.ud.path_pos as usize).copied().unwrap_or(0);
        let speed = if crate::path::direction_diagonal(dir) {
            unit::speed(u) * crate::path::MOVE_DIAGONAL_COST as i64 / crate::path::MOVE_COST as i64 / 2
        } else {
            unit::speed(u) / 2
        };
        u.ud.steptimer = timer::add(c, tick + speed, Func::UnitStep, u.id, 0);
    }
}

/// unit.cpp unit_step_timer (Func::UnitStep): runs the remembered request (normal attack or skill).
pub fn unit_step_timer(c: &Ctx, tid: u64, _tick: Tick, id: u32) -> i32 {
    let Some(mut u) = unit::get(c, id) else { return 0 };
    if !u.on_map {
        return 0;
    }
    if u.ud.steptimer != tid {
        log::error!("unit_step_timer mismatch {} != {}", u.ud.steptimer, tid);
        return 0;
    }
    u.ud.steptimer = 0;
    if !u.ud.stepaction {
        unit::save(c, &u);
        return 0;
    }
    // Set to false here because if an error occurs, it should not be executed again
    u.ud.stepaction = false;
    if u.ud.target_to == 0 {
        unit::save(c, &u);
        return 0;
    }
    // Flush target_to as it might contain map coordinates which should not be used by other functions
    let target_id = u.ud.target_to;
    u.ud.target_to = 0;
    unit::save(c, &u);
    // If stepaction is set then we remembered a client request that should be executed on the next step
    // Execute request now if target is in attack range
    if u.ud.stepskill_id != 0 && crate::db::skills::skill_get_inf(u.ud.stepskill_id) & crate::db::skills::INF_GROUND_SKILL as i32 != 0 {
        // Execute ground skill
        let xs = crate::data::maps().get(u.map).map(|m| m.xs as u32).unwrap_or(1).max(1);
        crate::skill::unit::unit_skilluse_pos(c, id, (target_id % xs) as i16, (target_id / xs) as i16, u.ud.stepskill_id, u.ud.stepskill_lv);
        return 1;
    }
    // If a player has target_id set and target is in range, attempt attack
    let Some(tbl) = unit::get(c, target_id) else { return 0 };
    if !mobif::status_check_visibility(c, &u, &tbl, false) {
        return 0;
    }
    if u.ud.stepskill_id == 0 {
        // Execute normal attack
        unit_attack(c, id, tbl.id, (u.ud.attack_continue as i32) + 2);
    } else {
        // Execute non-ground skill
        crate::skill::unit::unit_skilluse_id(c, id, tbl.id, u.ud.stepskill_id, u.ud.stepskill_lv);
    }
    1
}
