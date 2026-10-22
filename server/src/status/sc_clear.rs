//! status.cpp status_change_timer_sub, status_change_clear_buffs and status_change_spread (status.cpp:15234-15434).

use super::consts::*;
use super::defs::*;
use super::data::ScEntry;
use super::sc::{get_sce, has_sce, load, put_sce, put_unit_sc, status_change_end, status_change_start, status_isdead, unit_sc, INFINITE_TICK};
use crate::db::misc::status::status_db;
use crate::db::skills::ids::{AL_RUWACH, WZ_SIGHTBLASTER};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// status.hpp e_status_change_clear_buffs_flags.
pub const SCCB_BUFFS: u8 = 0x01;
pub const SCCB_DEBUFFS: u8 = 0x02;
pub const SCCB_REFRESH: u8 = 0x04;
pub const SCCB_CHEM_PROTECT: u8 = 0x08;
pub const SCCB_LUXANIMA: u8 = 0x10;
pub const SCCB_HERMODE: u8 = 0x20;

/// The statuses status_change_timer_sub has a case for.
pub const CASES_TIMER_SUB: &[i32] = &[SC_SIGHT, SC_CONCENTRATE, SC_RUWACH, SC_SIGHTBLASTER, SC_CLOSECONFINE, SC_CURSEDCIRCLE_TARGET];

/// `tsc->getSCE(SC__SHADOWFORM) && (sce && sce->val4 > 0 && sce->val4%2000 == 0) && rnd()%100 < 100 - val1 * 10`
/// (the roll only happens when the first parts hold).
fn shadowform_revealed(c: &Ctx, bl: &Unit, sce: &Option<&mut ScEntry>) -> bool {
    let Some(sf) = get_sce(c, bl.id, SC__SHADOWFORM) else { return false };
    let every_2s = sce.as_ref().map(|e| e.val4 > 0 && e.val4 % 2000 == 0).unwrap_or(false);
    every_2s && ((c.rnd() % 100) as i32) < 100 - sf.val1 * 10
}

/// status.cpp status_change_timer_sub: what a status (SC_SIGHT, SC_RUWACH, SC_CLOSECONFINE …) does to one unit around
/// its owner `src`. `sce` is the owner's entry (the caller writes it back).
pub fn status_change_timer_sub(c: &Ctx, bl: &Unit, src: &Unit, mut sce: Option<&mut ScEntry>, t: i32, tick: Tick) -> i32 {
    if status_isdead(c, bl) {
        return 0;
    }
    let tsc = super::sc_ext::has_status_change(bl.kind);
    match t {
        SC_SIGHT | SC_CONCENTRATE => {
            // Reveal hidden ennemy on 3*3 range
            status_change_end(c, bl.id, SC_HIDING, 0);
            status_change_end(c, bl.id, SC_CLOAKING, 0);
            status_change_end(c, bl.id, SC_CLOAKINGEXCEED, 0);
            status_change_end(c, bl.id, SC_CAMOUFLAGE, 0);
            status_change_end(c, bl.id, SC_NEWMOON, 0);
            // For every 2 seconds do the checking; [100 - (Skill Level x 10)] %
            if tsc && shadowform_revealed(c, bl, &sce) {
                status_change_end(c, bl.id, SC__SHADOWFORM, 0);
            }
        }
        SC_RUWACH => {
            // Reveal hidden target and deal little dammages if enemy
            if tsc && [SC_HIDING, SC_CLOAKING, SC_CAMOUFLAGE, SC_NEWMOON, SC_CLOAKINGEXCEED].iter().any(|&x| has_sce(c, bl.id, x)) {
                status_change_end(c, bl.id, SC_HIDING, 0);
                status_change_end(c, bl.id, SC_CLOAKING, 0);
                status_change_end(c, bl.id, SC_CAMOUFLAGE, 0);
                status_change_end(c, bl.id, SC_CLOAKINGEXCEED, 0);
                status_change_end(c, bl.id, SC_NEWMOON, 0);
                if crate::battle::target::battle_check_target(c, src, bl, BCT_ENEMY) > 0 {
                    let bl = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
                    crate::skill::attack::skill_attack(c, BF_MAGIC, src, src, &bl, AL_RUWACH, 1, tick, 0);
                }
            }
            if tsc && shadowform_revealed(c, bl, &sce) {
                status_change_end(c, bl.id, SC__SHADOWFORM, 0);
                if crate::battle::target::battle_check_target(c, src, bl, BCT_ENEMY) > 0 {
                    let bl = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
                    let skill_id = crate::db::misc::status::status_db().get_skill(t);
                    crate::skill::attack::skill_attack(c, BF_MAGIC, src, src, &bl, skill_id, 1, tick, 0);
                }
            }
        }
        SC_SIGHTBLASTER => {
            if crate::battle::target::battle_check_target(c, src, bl, BCT_ENEMY) > 0 && crate::status::check::status_check_skilluse(c, Some(src), Some(bl), WZ_SIGHTBLASTER, 2) {
                if let Some(sce) = sce.as_deref_mut() {
                    let hit = crate::skill::attack::skill_attack(c, BF_MAGIC, src, src, bl, WZ_SIGHTBLASTER, sce.val1 as u16, tick, 0x1000000) != 0;
                    // su: the skill unit when bl is one (its group read after the attack, as the && evaluates)
                    let su_group = if hit && bl.kind == BL_SKILL { crate::skillunit::group_of_unit(c, bl.id) } else { None };
                    // The hit is not counted if it's against a trap
                    let trap = su_group.map(|g| crate::db::skills::skill_get_inf2(g.skill_id, &[crate::db::skills::INF2_ISTRAP])).unwrap_or(false);
                    if hit && !trap {
                        sce.val2 = 0; // This signals it to end.
                    } else if bl.kind & BL_SKILL != 0 && sce.val4 % 2 == 0 {
                        //Remove trap immunity temporarily so it triggers if you still stand on it
                        sce.val4 += 1;
                    }
                }
            }
        }
        SC_CLOSECONFINE => {
            // Lock char has released the hold on everyone...
            if tsc {
                if let Some(mut e) = get_sce(c, bl.id, SC_CLOSECONFINE2).filter(|e| e.val2 == src.id as i32) {
                    e.val2 = 0;
                    put_sce(c, &e);
                    status_change_end(c, bl.id, SC_CLOSECONFINE2, 0);
                }
            }
        }
        SC_CURSEDCIRCLE_TARGET => {
            if tsc {
                if let Some(e) = get_sce(c, bl.id, SC_CURSEDCIRCLE_TARGET).filter(|e| e.val2 == src.id as i32) {
                    super::sc_ext::clif_bladestop(c, bl, e.val2 as u32, false);
                    status_change_end(c, bl.id, t, 0);
                }
            }
        }
        _ => {}
    }
    0
}

/// status.cpp status_change_clear_buffs: ends the statuses of the SCCB_* kinds (buffs, debuffs, Refresh, Chemical
/// Protection, Lux Anima, Hermode), skipping SCF_NOCLEARBUFF ones, then the matching bonus_script entries.
pub fn status_change_clear_buffs(c: &Ctx, bl_id: u32, type_: u8) {
    if load(c, bl_id).empty() {
        return;
    }
    //Clears buffs with specified flag and type
    for s in status_db().iter() {
        let status = s.type_;
        let Some(mut sce) = get_sce(c, bl_id, status) else { continue };
        // Skip status with SCF_NOCLEARBUFF, no matter what
        if s.has_flag(SCF_NOCLEARBUFF) {
            continue;
        }
        let end = if type_ & SCCB_LUXANIMA != 0 && s.has_flag(SCF_REMOVEONLUXANIMA) {
            // &SCCB_LUXANIMA : Cleared by RK_LUXANIMA and has the SCF_REMOVEONLUXANIMA flag
            true
        } else if type_ & SCCB_CHEM_PROTECT != 0 && s.has_flag(SCF_REMOVECHEMICALPROTECT) {
            // &SCCB_CHEM_PROTECT : Clears AM_CP_ARMOR/HELP/SHIELD/WEAPON
            true
        } else if type_ & SCCB_REFRESH != 0 && s.has_flag(SCF_REMOVEONREFRESH) {
            // &SCCB_REFRESH : Cleared by RK_REFRESH and has the SCF_REMOVEONREFRESH flag
            true
        } else if type_ & SCCB_DEBUFFS != 0 && s.has_flag(SCF_DEBUFF) {
            // &SCCB_DEBUFFS : Clears debuffs
            true
        } else if type_ & SCCB_BUFFS != 0 && !s.has_flag(SCF_DEBUFF) {
            // &SCCB_BUFFS : Clears buffs - skip if it is a debuff
            true
        } else {
            // &SCCB_HERMODE : Cleared by CG_HERMODE and has the SCF_REMOVEONHERMODE flag
            type_ & SCCB_HERMODE != 0 && s.has_flag(SCF_REMOVEONHERMODE)
        };
        if status == SC_SATURDAYNIGHTFEVER || status == SC_BERSERK {
            // Mark to not lose HP
            sce.val2 = 0;
            put_sce(c, &sce);
        }
        if end {
            status_change_end(c, bl_id, status, 0);
        }
    }
    //Removes bonus_script
    if crate::unit::get(c, bl_id).map(|u| u.kind == BL_PC).unwrap_or(false) {
        let mut i: u32 = 0;
        if type_ & SCCB_BUFFS != 0 {
            i |= BSF_REM_BUFF;
        }
        if type_ & SCCB_DEBUFFS != 0 {
            i |= BSF_REM_DEBUFF;
        }
        if type_ & SCCB_REFRESH != 0 {
            i |= BSF_REM_ON_REFRESH;
        }
        if type_ & SCCB_LUXANIMA != 0 {
            i |= BSF_REM_ON_LUXANIMA;
        }
        super::bonus::pc_bonus_script_clear(c, bl_id, i);
    }
    // Cleaning all extras vars
    let mut us = unit_sc(c, bl_id);
    us.comet_x = 0;
    us.comet_y = 0;
    put_unit_sc(c, &us);
}

/// status.cpp status_change_spread (SC_DEADLYINFECT): the source's SCF_SPREADEFFECT statuses go to `bl`, with the
/// time they have left.
pub fn status_change_spread(c: &Ctx, src: &Unit, bl: &Unit) -> bool {
    // Status Immunity resistance
    let immune = |u: &Unit| super::status_get_status_data(c, u).mode & MD_STATUSIMMUNE == MD_STATUSIMMUNE;
    if immune(src) || immune(bl) {
        return false;
    }
    if !super::sc_ext::has_status_change(src.kind) || load(c, src.id).empty() {
        return false;
    }
    let mut has_spread = false;
    let tick = c.tick();
    for s in status_db().iter() {
        let t = s.type_;
        let Some(e) = get_sce(c, src.id, t) else { continue };
        if !s.has_flag(SCF_SPREADEFFECT) {
            continue;
        }
        let sc_tick = if e.timer != 0 {
            // get_timer: the timer must exist, run status_change_timer and not be overdue
            use crate::timer::timer;
            let Some(timer) = c.db().timer().id().find(e.timer) else { continue };
            if timer.func != crate::timer::Func::StatusChangeTimer as u16 || timer.due - tick < 0 {
                continue;
            }
            timer.due - tick + if e.val4 > 0 { e.val4 as i64 } else { 0 }
        } else {
            INFINITE_TICK
        };
        status_change_start(c, Some(src), bl, t, 10000, e.val1, e.val2, e.val3, e.val4, sc_tick, SCSTART_NOAVOID | SCSTART_NOTICKDEF | SCSTART_NORATEDEF, 0);
        has_spread = true;
    }
    has_spread
}
