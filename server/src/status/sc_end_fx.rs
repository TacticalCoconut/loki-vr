//! status.cpp status_change_end: the per-status `switch (type)` after the entry is removed (status.cpp:13519-14031),
//! the effects a status has when it ends.

use super::consts::*;
use super::defs::*;
use super::hp::{status_damage, status_fix_damage, status_percent_change, status_percent_heal, status_set_hp, status_set_sp, status_zap};
use super::sc::{get_sce, put_sce, sc_start, sc_start2, sc_start4, status_change_end, status_isdead};
use super::sc_ext::*;
use super::sc_st::{EndFlow, EndSt};
use crate::db::skills::ids::*;
use crate::db::skills::{skill_get_time, skill_get_time2, DMG_NORMAL};
use crate::skill::state::{self as pcskill, MAX_DEVOTION, MAX_SKILL_CRIMSON_MARKER};
use crate::skill::{SD_ANIMATION, SD_LEVEL};
use crate::skillunit::{skill_delunitgroup, skill_id2group};
use crate::unit::{self, Unit};

/// map.cpp map_id2sd: the player with that id.
fn map_id2sd(c: &crate::world::Ctx, id: u32) -> Option<Unit> {
    unit::get(c, id).filter(|u| u.kind == BL_PC)
}

/// `std::shared_ptr<s_skill_unit_group> group = skill_id2group(id); if (group) skill_delunitgroup(group);`
fn delunitgroup_id(c: &crate::world::Ctx, id: i32) {
    if let Some(g) = skill_id2group(c, id) {
        skill_delunitgroup(c, g.group_id);
    }
}

/// Every status this switch has a case for (a unit test checks it against rAthena's case list).
pub const CASES: &[i32] = &[
    SC_KEEPING, SC_BARRIER, SC_GRANITIC_ARMOR, SC_RUN, SC_AUTOBERSERK, SC_ENDURE, SC_DEFENDER, SC_REFLECTSHIELD, SC_AUTOGUARD, SC_DEVOTION,
    SC_FLASHKICK, SC_SOULUNITY, SC_BLADESTOP, SC_DANCING, SC_NOCHAT, SC_SPLASHER, SC_ROSEBLOSSOM, SC_CLOSECONFINE2, SC_CLOSECONFINE,
    SC_COMBO, SC_MARIONETTE, SC_MARIONETTE2, SC_CONCENTRATION, SC_BERSERK, SC_GOSPEL, SC_TRICKDEAD, SC_WARM, SC__MANHOLE, SC_BANDING,
    SC_LEADERSHIP, SC_GLORYWOUNDS, SC_SOULCOLD, SC_HAWKEYES, SC_JAILED, SC_CHANGE, SC_AUTOTRADE, SC_STOP, SC_WINKCHARM,
    SC_TENSIONRELAX, SC_MONSTER_TRANSFORM, SC_ACTIVE_MONSTER_TRANSFORM, SC_MILLENNIUMSHIELD, SC_HALLUCINATIONWALK, SC_WHITEIMPRISON,
    SC_WUGDASH, SC__SHADOWFORM, SC_SATURDAYNIGHTFEVER, SC_NEUTRALBARRIER_MASTER, SC_STEALTHFIELD_MASTER, SC_CURSEDCIRCLE_ATKER,
    SC_RAISINGDRAGON, SC_CURSEDCIRCLE_TARGET, SC_SITDOWN_FORCE, SC_BANANA_BOMB_SITDOWN, SC_KYOUGAKU, SC_INTRAVISION, SC_GRAVITYCONTROL,
    SC_OVERED_BOOST, SC_FULL_THROTTLE, SC_REBOUND, SC_ITEMSCRIPT, SC_C_MARKER, SC_H_MINE, SC_VACUUM_EXTREME, SC_DIMENSION1,
    SC_DIMENSION2, SC_SOULENERGY, SC_MADOGEAR, SC_HOMUN_TIME, SC_SERVANT_SIGN, SC_SOUNDBLEND, SC_SERVANTWEAPON, SC_ABYSSFORCEWEAPON,
    SC_BLESSING_OF_M_CREATURES,
];

/// `unit_stop_walking(bl, USW_FIXPOS)` when the unit's walktimer runs, after `ud->state.running = 0`.
fn stop_running(c: &crate::world::Ctx, bl_id: u32) -> bool {
    let Some(mut u) = unit::get(c, bl_id) else { return false };
    let was_running = u.ud.running;
    u.ud.running = false;
    unit::save(c, &u);
    if u.ud.walktimer != 0 {
        unit::stop_walking(c, &mut u, unit::USW_FIXPOS, 0);
        unit::save(c, &u);
    }
    was_running
}

/// The end-effect switch. `EndFlow::Continue` = `break`, `EndFlow::Return(r)` = `return r`.
pub fn end_effect(st: &mut EndSt) -> EndFlow {
    let c = st.c;
    let bl_id = st.bl.id;
    let t = st.t;
    let is_pc = st.is_pc();
    let (val1, val2, val3, val4) = (st.val1, st.val2, st.val3, st.val4);
    let tid = st.tid;
    match t {
        SC_KEEPING | SC_BARRIER => {
            if let Some(mut u) = unit::get(c, bl_id) {
                let endtick = c.tick();
                u.ud.attackabletime = endtick;
                u.ud.canact_tick = endtick;
                u.ud.canmove_tick = endtick;
                u.ud.endure_tick = endtick;
                unit::save(c, &u);
            }
        }
        SC_GRANITIC_ARMOR => {
            let status = st.status();
            let mut damage = (status.max_hp.wrapping_mul(val3 as u32) / 100) as i32;
            if status.hp < damage as u32 {
                // to not kill him (uint32 hp against the int32 damage: an unsigned comparison)
                damage = status.hp.wrapping_sub(1) as i32;
            }
            // status_damage(nullptr, bl, damage, 0, 0, 1, 0): the 7-argument overload (walkdelay 0, flag 1)
            status_damage(c, None, bl_id, damage as i64, 0, 0, 0, 1, 0);
        }
        SC_RUN => {
            // Note: this int64 value is stored in two separate int32 variables (FIXME)
            let mut starttick = (val3 as i64) & 0x0000_0000_ffff_ffff;
            starttick |= ((val4 as i64) << 32) & (0xffff_ffff_0000_0000u64 as i64);
            // (ud: every unit that can have SC_RUN has unit data)
            let begin_spurt = stop_running(c, bl_id);
            let fists = !is_pc || crate::inventory::st(c, bl_id).map(|s| s.weapontype1 == crate::inventory::W_FIST && s.weapontype2 == crate::inventory::W_FIST).unwrap_or(false);
            if begin_spurt && val1 >= 7 && c.tick() - starttick <= 1000 && fists {
                let bl = st.bl_now();
                sc_start(c, Some(&bl), &bl, SC_SPURT, 100, val1, skill_get_time2(st.scdb.skill_id, val1 as u16) as i64);
            }
        }
        SC_AUTOBERSERK => {
            if st.sce(SC_PROVOKE).map(|e| e.val4 == 1).unwrap_or(false) {
                status_change_end(c, bl_id, SC_PROVOKE, 0);
            }
        }
        SC_ENDURE | SC_DEFENDER | SC_REFLECTSHIELD | SC_AUTOGUARD => {
            if is_pc {
                // Clear Status from others
                for i in 0..MAX_DEVOTION {
                    let d = pcskill::get(c, bl_id).devotion.get(i).copied().unwrap_or(0);
                    if d != 0 {
                        if let Some(tsd) = map_id2sd(c, d) {
                            if super::sc::has_sce(c, tsd.id, t) {
                                status_change_end(c, tsd.id, t, 0);
                            }
                        }
                    }
                }
            } else if st.bl.kind == BL_MER {
                // Clear Status from Master: `((TBL_MER*)bl)->devotion_flag` (mercenary.rs)
                if let Some(tsd) = crate::mercenary::devotion_master(c, bl_id) {
                    if super::sc::has_sce(c, tsd.id, t) {
                        status_change_end(c, tsd.id, t, 0);
                    }
                }
            }
        }
        SC_DEVOTION => {
            if let Some(d_bl) = unit::get(c, val1 as u32) {
                if d_bl.kind == BL_PC {
                    pcskill::with(c, d_bl.id, |s| {
                        if let Some(d) = s.devotion.get_mut(val2 as usize) {
                            *d = 0;
                        }
                    });
                } else if d_bl.kind == BL_MER {
                    // ((TBL_MER*)d_bl)->devotion_flag = 0 (mercenary/data.rs)
                    if let Some(mut md) = crate::mercenary::data::get(c, d_bl.id) {
                        md.devotion_flag = false;
                        crate::mercenary::data::put(c, &md);
                    }
                }
                clif_devotion(c, &d_bl, None);
            }
        }
        SC_FLASHKICK => {
            if let Some(tsd) = map_id2sd(c, val1 as u32) {
                pcskill::with(c, tsd.id, |s| {
                    if let Some(m) = s.stellar_mark.get_mut(val2 as usize) {
                        *m = 0;
                    }
                });
            }
        }
        SC_SOULUNITY => {
            if let Some(tsd) = map_id2sd(c, val2 as u32) {
                pcskill::with(c, tsd.id, |s| {
                    if let Some(m) = s.united_soul.get_mut(val3 as usize) {
                        *m = 0;
                    }
                });
            }
        }
        SC_BLADESTOP => {
            if val4 != 0 {
                // Stop the status for the other guy of bladestop as well
                if let Some(tbl) = unit::get(c, val4 as u32).filter(|u| has_status_change(u.kind)) {
                    if let Some(mut e) = get_sce(c, tbl.id, SC_BLADESTOP) {
                        e.val4 = 0;
                        put_sce(c, &e);
                        status_change_end(c, tbl.id, SC_BLADESTOP, 0);
                    }
                }
                clif_bladestop(c, &st.bl_now(), val4 as u32, false);
            }
        }
        SC_DANCING => {
            if val4 != 0 && val4 != BCT_SELF {
                // End status on partner as well
                if let Some(dsd) = unit::get(c, val4 as u32).filter(|u| u.kind == BL_PC) {
                    if let Some(mut dsc) = get_sce(c, dsd.id, SC_DANCING) {
                        // This will prevent recursive loops.
                        dsc.val2 = 0;
                        dsc.val4 = BCT_SELF;
                        put_sce(c, &dsc);
                        status_change_end(c, dsd.id, SC_DANCING, 0);
                    }
                }
            }
            if val2 != 0 {
                // Erase associated land skill
                let group = skill_id2group(c, val2);
                st.val2 = 0;
                if let Some(g) = group {
                    skill_delunitgroup(c, g.group_id);
                }
            }
            if (val1 & 0xFFFF) == CG_MOONLIT as i32 {
                super::clif::status_change(c, &st.bl_now(), EFST_MOON, 0, 0, 0, 0, 0);
            }
        }
        SC_NOCHAT => {
            if is_pc {
                if let Some(mut ch) = crate::pc::character_of(c, bl_id) {
                    if ch.manner < 0 && tid != 0 {
                        ch.manner = 0;
                        super::put_char(c, &ch);
                    }
                }
                if tid == 0 {
                    super::clif::changemanner(c, bl_id);
                    super::clif::updatestatus(c, bl_id, SP_MANNER);
                }
            }
        }
        SC_SPLASHER | SC_ROSEBLOSSOM => {
            if let Some(src) = unit::get(c, val3 as u32) {
                if tid != 0 {
                    crate::skill::castend::skill_castend_damage_id(c, &src, &st.bl_now(), val2 as u16, val1 as u16, c.tick(), SD_LEVEL);
                }
            }
        }
        SC_CLOSECONFINE2 | SC_CLOSECONFINE => {
            if t == SC_CLOSECONFINE2 {
                if let Some(src) = if val2 != 0 { unit::get(c, val2 as u32) } else { None } {
                    if has_status_change(src.kind) {
                        if let Some(mut e) = get_sce(c, src.id, SC_CLOSECONFINE) {
                            // If status was already ended, do nothing.
                            // Decrease count
                            e.val1 -= 1;
                            put_sce(c, &e);
                            if e.val1 <= 0 {
                                // No more holds, free him up.
                                status_change_end(c, src.id, SC_CLOSECONFINE, 0);
                            }
                        }
                    }
                }
                // [[fallthrough]]
            }
            if val2 > 0 {
                // Caster has been unlocked... nearby chars need to be unlocked.
                let bl = st.bl_now();
                let range = 1 + crate::skill::misc::skill_get_range2(c, &bl, st.scdb.skill_id, val1 as u16, true) + crate::skill::misc::skill_get_range2(c, &bl, TF_BACKSLIDING, 1, true); // Since most people use this to escape the hold....
                let r = range as i16;
                for u in crate::map::units_in_area(c, bl.map, bl.x - r, bl.y - r, bl.x + r, bl.y + r, crate::map::BL_CHAR) {
                    if let Some(u) = unit::get(c, u.id) {
                        super::sc_clear::status_change_timer_sub(c, &u, &bl, None, t, c.tick());
                    }
                }
            }
        }
        SC_COMBO => {
            crate::skill::combo::skill_combo_toggle_inf(c, &st.bl_now(), val1 as u16, crate::db::skills::INF_PASSIVE_SKILL as i32);
        }
        SC_MARIONETTE | SC_MARIONETTE2 => {
            // Marionette target
            if val1 != 0 {
                // Check for partner and end their marionette status as well
                let type2 = if t == SC_MARIONETTE { SC_MARIONETTE2 } else { SC_MARIONETTE };
                if let Some(pbl) = unit::get(c, val1 as u32).filter(|u| has_status_change(u.kind)) {
                    if let Some(mut e) = get_sce(c, pbl.id, type2) {
                        e.val1 = 0;
                        put_sce(c, &e);
                        status_change_end(c, pbl.id, type2, 0);
                    }
                }
            }
        }
        SC_CONCENTRATION => {
            if st.sce(SC_ENDURE).map(|e| e.val4 == 0).unwrap_or(false) {
                status_change_end(c, bl_id, SC_ENDURE, 0);
            }
        }
        SC_BERSERK => {
            let status = st.status();
            if status.hp > 200 && st.has(SC__BLOODYLUST) {
                status_percent_heal(c, bl_id, 100, 0, 0);
                status_change_end(c, bl_id, SC__BLOODYLUST, 0);
            } else if status.hp > 100 && val2 != 0 {
                // If val2 is removed, no HP penalty (dispelled?) [Skotlex]
                status_set_hp(c, bl_id, 100, 0);
            }
            if let Some(mut e) = st.sce(SC_ENDURE).filter(|e| e.val4 != 0) {
                e.val4 = 0;
                put_sce(c, &e);
                status_change_end(c, bl_id, SC_ENDURE, 0);
            }
            let bl = st.bl_now();
            sc_start4(c, Some(&bl), &bl, SC_REGENERATION, 100, 10, 0, 0, (RGN_HP | RGN_SP) as i32, skill_get_time(LK_BERSERK, val1 as u16) as i64);
        }
        SC_GOSPEL => {
            if val3 != 0 {
                // Clear the group.
                delunitgroup_id(c, val3);
            }
        }
        SC_TRICKDEAD => {
            if let Some(mut u) = unit::get(c, bl_id) {
                u.vd.dead_sit = 0;
                unit::save(c, &u);
            }
        }
        SC_WARM | SC__MANHOLE | SC_BANDING | SC_LEADERSHIP | SC_GLORYWOUNDS | SC_SOULCOLD | SC_HAWKEYES => {
            if val4 != 0 {
                // Clear the group. Might have been cleared before status ended, e.g. land protector
                delunitgroup_id(c, val4);
            }
        }
        SC_JAILED => {
            // (map indexes: the module's map id stands for rAthena's mapindex, see the value setup)
            if is_pc && crate::pc::get(c, bl_id).and_then(|p| p.mapindex).map(|m| m as i32) == Some(val2) {
                // (pc_setpos takes int32 coordinates)
                crate::script::buildins::script2::mapctl::setpos32(c, bl_id, val3 as u16, val4 & 0xFFFF, val4 >> 16, unit::CLR_TELEPORT);
            }
            // Guess hes not in jail :P
        }
        SC_CHANGE => {
            if tid != 0 {
                // "lose almost all their HP and SP" on natural expiration.
                status_set_hp(c, bl_id, 10, 0);
                status_set_sp(c, bl_id, 10, 0);
            }
        }
        SC_AUTOTRADE => {
            if tid != 0 {
                // Vending is not automatically closed for autovenders
                crate::vending::vending_closevending(c, bl_id);
                crate::pc::quit(c, bl_id);
                // Because map_quit calls status_change_end with tid -1
                // from here it's not necessary to continue
                return EndFlow::Return(1);
            }
        }
        SC_STOP => {
            if val2 != 0 {
                if let Some(tbl) = unit::get(c, val2 as u32) {
                    if has_status_change(tbl.kind) && get_sce(c, tbl.id, SC_STOP).map(|e| e.val2 == bl_id as i32).unwrap_or(false) {
                        status_change_end(c, tbl.id, SC_STOP, 0);
                    }
                }
            }
        }
        SC_WINKCHARM => {
            if st.bl.kind == BL_MOB {
                crate::battle::mobif::unlocktarget(c, &st.bl_now(), c.tick());
            }
        }
        SC_TENSIONRELAX => {
            if st.has(SC_WEIGHT50) || st.has(SC_WEIGHT90) {
                // Add the overweight flag back (status_get_regen_data: players keep theirs in pc_status)
                super::data::with_sd(c, bl_id, |sd| sd.regen.overweight = true);
            }
        }
        SC_MONSTER_TRANSFORM | SC_ACTIVE_MONSTER_TRANSFORM => {
            if val2 != 0 {
                status_change_end(c, bl_id, val2, 0);
            }
        }
        /* 3rd Stuff */
        SC_MILLENNIUMSHIELD => {
            pcskill::clif_millenniumshield(c, &st.bl_now(), 0);
        }
        SC_HALLUCINATIONWALK => {
            let bl = st.bl_now();
            sc_start(c, Some(&bl), &bl, SC_HALLUCINATIONWALK_POSTDELAY, 100, val1, skill_get_time2(GC_HALLUCINATIONWALK, val1 as u16) as i64);
        }
        SC_WHITEIMPRISON => {
            let src = unit::get(c, val2 as u32);
            if tid != 0 {
                if let Some(src) = src {
                    // (not terminated by damage)
                    let bl = st.bl_now();
                    crate::battle::clif::clif_damage(c, &bl, &bl, c.tick(), 0, 0, (400 * val1) as i64, 0, DMG_NORMAL, 0, false);
                    status_fix_damage(c, Some(&src), bl_id, (400 * val1) as i64, 1, WL_WHITEIMPRISON);
                }
            }
        }
        SC_WUGDASH => {
            stop_running(c, bl_id);
        }
        SC__SHADOWFORM => {
            if let Some(s_sd) = map_id2sd(c, val2 as u32) {
                pcskill::with(c, s_sd.id, |s| s.shadowform_id = 0);
            }
        }
        SC_SATURDAYNIGHTFEVER => {
            // Sit down force of Saturday Night Fever has the duration of only 3 seconds.
            let bl = st.bl_now();
            sc_start(c, Some(&bl), &bl, SC_SITDOWN_FORCE, 100, val1, skill_get_time2(WM_SATURDAY_NIGHT_FEVER, val1 as u16) as i64);
        }
        SC_NEUTRALBARRIER_MASTER | SC_STEALTHFIELD_MASTER => {
            if val2 != 0 {
                // Might have been cleared before status ended, e.g. land protector
                delunitgroup_id(c, val2);
            }
        }
        SC_CURSEDCIRCLE_ATKER => {
            if val2 != 0 {
                // Used the default area size cause there is a chance the caster could knock back and can't clear the target.
                let bl = st.bl_now();
                for u in crate::map::units_in_range(c, bl.map, bl.x, bl.y, crate::conf::area_size() + 3, crate::map::BL_CHAR) {
                    if let Some(u) = unit::get(c, u.id) {
                        super::sc_clear::status_change_timer_sub(c, &u, &bl, None, SC_CURSEDCIRCLE_TARGET, c.tick());
                    }
                }
            }
        }
        SC_RAISINGDRAGON => {
            if is_pc && !crate::pc::isdead(c, bl_id) {
                let balls = pcskill::get(c, bl_id).spiritball as i32;
                let mut i = balls.min(5);
                pcskill::pc_delspiritball(c, bl_id, balls, 0);
                status_change_end(c, bl_id, SC_EXPLOSIONSPIRITS, 0);
                while i > 0 {
                    let interval = skill_get_time(MO_CALLSPIRITS, pc_checkskill(c, bl_id, MO_CALLSPIRITS) as u16);
                    pcskill::pc_addspiritball(c, bl_id, interval, 5);
                    i -= 1;
                }
            }
        }
        SC_CURSEDCIRCLE_TARGET => {
            if let Some(src) = unit::get(c, val2 as u32).filter(|u| has_status_change(u.kind)) {
                if let Some(mut e) = get_sce(c, src.id, SC_CURSEDCIRCLE_ATKER) {
                    e.val2 -= 1;
                    put_sce(c, &e);
                    if e.val2 == 0 {
                        clif_bladestop(c, &st.bl_now(), val2 as u32, false);
                        status_change_end(c, src.id, SC_CURSEDCIRCLE_ATKER, 0);
                    }
                }
            }
        }
        SC_SITDOWN_FORCE | SC_BANANA_BOMB_SITDOWN => {
            if is_pc && pc_issit(c, bl_id) && crate::pc::setstand(c, bl_id, false) {
                skill_sit(c, bl_id, false);
            }
        }
        SC_KYOUGAKU => {
            let bl = st.bl_now();
            clif_status_load(c, &bl, EFST_KYOUGAKU, 0); // Avoid client crash
            clif_status_load(c, &bl, EFST_ACTIVE_MONSTER_TRANSFORM, 0);
        }
        SC_INTRAVISION => {
            st.calc_flag = crate::db::misc::status::status_db().get_scb_all(); // Required for overlapping
        }
        SC_GRAVITYCONTROL => {
            let bl = st.bl_now();
            crate::battle::clif::clif_damage(c, &bl, &bl, c.tick(), 0, 0, val2 as i64, 0, DMG_NORMAL, 0, false);
            status_fix_damage(c, Some(&bl), bl_id, val2 as i64, 1, 0);
            // the fall may kill: rAthena's bl is still the (freed) block_list, the effects go to its area
            if let Some(bl) = unit::get_or_freed(c, bl_id) {
                crate::clif::specialeffect(c, &bl, 223, crate::clif::Target::Area);
                crate::clif::specialeffect(c, &bl, 330, crate::clif::Target::Area);
            }
        }
        SC_OVERED_BOOST => match st.bl.kind {
            BL_HOM => {
                // hd->homunculus.hunger = max(1, hd->homunculus.hunger - 50) (homunculus/data.rs)
                crate::homunculus::data::with(c, bl_id, |hd| hd.homunculus.hunger = (hd.homunculus.hunger - 50).max(1));
            }
            BL_PC => {
                status_zap(c, bl_id, 0, (st.status().max_sp / 2) as i64, 0);
            }
            _ => {}
        },
        SC_FULL_THROTTLE => {
            let sec = skill_get_time2(st.scdb.skill_id, val1 as u16);
            let bl = st.bl_now();
            super::clif::status_change(c, &bl, EFST_DEC_AGI, 1, sec as i64, 0, 0, 0);
            sc_start(c, Some(&bl), &bl, SC_REBOUND, 100, val1, sec as i64);
        }
        SC_REBOUND => {
            clif_status_load(c, &st.bl_now(), EFST_DEC_AGI, 0);
        }
        SC_ITEMSCRIPT => {
            // Removes Buff Icons
            if is_pc {
                clif_status_load(c, &st.bl_now(), val2, 0);
            }
        }
        SC_C_MARKER => {
            // Remove mark data from caster
            if let Some(caster) = map_id2sd(c, val2 as u32) {
                let marks = pcskill::get(c, caster.id).c_marker;
                if let Some(i) = (0..MAX_SKILL_CRIMSON_MARKER).find(|&i| marks.get(i).copied() == Some(bl_id)) {
                    pcskill::with(c, caster.id, |s| s.c_marker[i] = 0);
                    clif_crimson_marker(c, &caster, &st.bl_now(), true);
                }
            }
        }
        SC_H_MINE => {
            // Only drop the material from target if expired naturally
            if tid != 0 {
                let bl = st.bl_now();
                let caster = unit::get(c, val2 as u32).filter(|u| u.kind == BL_PC);
                if status_isdead(c, &bl) {
                    return EndFlow::Continue;
                }
                let Some(caster) = caster else { return EndFlow::Continue };
                let Some(skill) = crate::db::skills::skill_db().find(RL_H_MINE) else { return EndFlow::Continue };
                if crate::db::items::itemdb_exists(skill.require.itemid[0]).is_none() {
                    return EndFlow::Continue;
                }
                let it = crate::inventory::Item { nameid: skill.require.itemid[0], amount: skill.require.amount[0].max(1) as i16, identify: 1, ..Default::default() };
                let char_id = crate::pc::character_of(c, caster.id).map(|ch| ch.id).unwrap_or(0);
                crate::flooritem::map_addflooritem(c, &it, it.amount as i32, bl.map, bl.x, bl.y, char_id, 0, 0, 4, 0, false, None, crate::map::BL_NUL);
            }
        }
        SC_VACUUM_EXTREME => {
            // !CHECKME: Seems on official, there's delay before same target can be vacuumed in same area again [Cydh]
            let bl = st.bl_now();
            sc_start2(c, Some(&bl), &bl, SC_VACUUM_EXTREME_POSTDELAY, 100, val1, val2, skill_get_time2(SO_VACUUM_EXTREME, val1 as u16) as i64);
        }
        SC_DIMENSION1 | SC_DIMENSION2 => {
            if is_pc {
                pcskill::pc_delspiritball(c, bl_id, 1, 0);
            }
        }
        SC_SOULENERGY => {
            let soulball = if is_pc { pcskill::get(c, bl_id).soulball as i32 } else { 0 };
            if is_pc && soulball > 0 {
                // When SC_SOULENERGY ends, removes all soulball if SC_SOULCOLLECT is active otherwise 1
                if st.has(SC_SOULCOLLECT) {
                    pcskill::pc_delsoulball(c, bl_id, soulball, false);
                } else {
                    pcskill::pc_delsoulball(c, bl_id, 1, false);
                }
            }
        }
        SC_MADOGEAR => {
            let has = |x| super::sc::has_sce(c, bl_id, x);
            for rem in crate::db::misc::status::status_db().remove_by_status_flag(&has, &[SCF_MADOENDCANCEL]) {
                status_change_end(c, bl_id, rem, 0);
            }
            if is_pc {
                super::bonus::pc_bonus_script_clear(c, bl_id, BSF_REM_ON_MADOGEAR);
            }
        }
        SC_HOMUN_TIME => {
            // if (sd && hom_is_active(sd->hd)) hom_vaporize(sd, HOM_ST_REST) (homunculus.rs)
            if is_pc && crate::homunculus::data::is_active_of(c, bl_id) {
                crate::homunculus::hom_vaporize(c, bl_id, crate::homunculus::data::HOM_ST_REST);
            }
        }
        SC_SERVANT_SIGN => {
            if let Some(tsd) = map_id2sd(c, val1 as u32) {
                pcskill::with(c, tsd.id, |s| {
                    if let Some(m) = s.servant_sign.get_mut(val2 as usize) {
                        *m = 0;
                    }
                });
            }
        }
        SC_SOUNDBLEND => {
            if let Some(src) = unit::get(c, val2 as u32) {
                if tid != 0 {
                    crate::skill::castend::skill_castend_damage_id(c, &src, &st.bl_now(), TR_SOUNDBLEND, val1 as u16, c.tick(), SD_LEVEL | SD_ANIMATION);
                }
            }
        }
        SC_SERVANTWEAPON => {
            if is_pc {
                let n = pcskill::get(c, bl_id).servantball as i32;
                pcskill::pc_delservantball(c, bl_id, n);
            }
        }
        SC_ABYSSFORCEWEAPON => {
            if is_pc {
                let n = pcskill::get(c, bl_id).abyssball as i32;
                pcskill::pc_delabyssball(c, bl_id, n);
            }
        }
        SC_BLESSING_OF_M_CREATURES => {
            let bl = st.bl_now();
            sc_start(c, Some(&bl), &bl, SC_BLESSING_OF_M_C_DEBUFF, 100, 1, skill_get_time2(SH_BLESSING_OF_MYSTICAL_CREATURES, 1) as i64);
            status_percent_change(c, Some(&bl), bl_id, 0, 0, -100, 1);
        }
        _ => {}
    }
    EndFlow::Continue
}
