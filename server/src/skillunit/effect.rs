//! skill.cpp skill_unit_effect, skill_unit_onplace (+ ext_skill_unit_onplace), skill_unit_onout, skill_unit_onleft
//! and skill_unit_ondamaged: what a unit does to the characters that step on it, off it, or out of its group.

use crate::battle::target::battle_check_target;
use crate::battle::{BCT_ENEMY, BCT_GUILD, BCT_PARTY, BCT_SELF};
use crate::db::skills::{self as sk, *};
use crate::map::{BL_MOB, BL_PC};
use crate::map_cell::{self, CELL_CHKBASILICA, CELL_CHKLANDPROTECTOR, CELL_CHKMAELSTROM};
use crate::status::consts::*;
use crate::status::defs::{RC_DEMON, SCSTART_NOAVOID, SCSTART_NOICON, SCSTART_NORATEDEF, SCSTART_NOTICKDEF};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

use super::consts::*;
use super::data::{self, SkillUnit};
use super::{conf, group, iface, setting, util};

/// status.hpp OPTION_HIDE.
const OPTION_HIDE: i32 = 0x2;

/// map_foreachincell(skill_unit_effect, m, x, y, bl_flag, unit, tick, flag).
#[allow(clippy::too_many_arguments)]
pub fn foreachincell_unit_effect(c: &Ctx, unit: &SkillUnit, m: u16, x: i16, y: i16, bl_flag: i32, tick: Tick, flag: u32) -> i32 {
    let ids = util::ids_in_cell(c, m, x, y, bl_flag as u16);
    util::each(c, &ids, |bl| skill_unit_effect(c, bl, unit.id(), tick, flag))
}

/// skill.cpp skill_unit_effect: a unit cell was placed / removed / deleted over `bl`.
/// flag &1: onplace (otherwise onout); &4: an onleft call (the unit may be about to go); &8: no dissonance check.
pub fn skill_unit_effect(c: &Ctx, bl: &Unit, unit_id: u32, tick: Tick, flag: u32) -> i32 {
    let Some(unit) = data::get_unit(c, unit_id) else { return 0 };
    if (!unit.alive && flag & 4 == 0) || !bl.on_map {
        return 0;
    }
    if data::skill_id2group(c, unit.group_id).is_none() {
        return 0;
    }
    let mut dissonance = false;
    if flag & 8 == 0 {
        dissonance = setting::skill_dance_switch(c, unit_id, false);
    }
    let Some(group) = data::skill_id2group(c, unit.group_id) else { return 0 };
    //Target-type check.
    let is_target = group.bl_flag & bl.kind as i32 != 0 && battle_check_target(c, &unit.bl, bl, group.target_flag) > 0;
    //Necessary in case the group is deleted after calling on_place/on_out [Skotlex]
    let skill_id = group.skill_id;
    if is_target {
        if flag & 1 != 0 {
            skill_unit_onplace(c, unit_id, bl, tick);
        } else if skill_unit_onout(c, unit_id, bl, tick) == -1 {
            return 0; // Don't let a Bard/Dancer update their own song timer
        }
        if flag & 4 != 0 {
            skill_unit_onleft(c, skill_id, bl, tick);
        }
    } else if !is_target && flag & 4 != 0 && (group.song_dance & 0x1 != 0 || (group.src_id == bl.id && group.song_dance & 0x2 != 0)) {
        skill_unit_onleft(c, skill_id, bl, tick); //Ensemble check to terminate it.
    }
    if dissonance {
        setting::skill_dance_switch(c, unit_id, true);
    }
    0
}

/// skill.cpp ext_skill_unit_onplace.
pub fn ext_skill_unit_onplace(c: &Ctx, unit_id: u32, bl: &Unit, tick: Tick) {
    skill_unit_onplace(c, unit_id, bl, tick);
}

/// sc of a unit kind that has a status_change (status_get_sc).
fn sc_of(c: &Ctx, bl: &Unit) -> Option<sc::Sc> {
    crate::battle::bl::sc(c, bl)
}

/// skill.cpp skill_unit_onplace: `bl` stands on the unit when it is placed (or moved), or steps on it. Returns the
/// skill id (0 when nothing applies).
pub fn skill_unit_onplace(c: &Ctx, unit_id: u32, bl: &Unit, tick: Tick) -> i32 {
    let Some(unit) = data::get_unit(c, unit_id) else { return 0 };
    let Some(bl) = unit::get(c, bl.id) else { return 0 };
    if !bl.on_map || !unit.alive || sc::status_isdead(c, &bl) {
        return 0;
    }
    let Some(mut sg) = data::skill_id2group(c, unit.group_id) else { return 0 };
    let Some(ss) = unit::get(c, sg.src_id) else { return 0 };
    let tstatus = crate::status::status_get_status_data(c, &bl);
    let lp = |m, x, y| map_cell::getcell(c, Some(m), x, y, CELL_CHKLANDPROTECTOR) != 0;
    if (skill_get_type(sg.skill_id) == crate::battle::BF_MAGIC
        && (if conf::land_protector_behavior() { lp(bl.map, bl.x, bl.y) } else { lp(unit.bl.map, unit.bl.x, unit.bl.y) })
        && sg.skill_id != SA_LANDPROTECTOR)
        || map_cell::getcell(c, Some(bl.map), bl.x, bl.y, CELL_CHKMAELSTROM) != 0
    {
        return 0; //AoE skills are ineffective. [Skotlex]
    }
    let Some(skill) = skill_db().find(sg.skill_id) else { return 0 };
    if (skill.has_inf2(INF2_ISSONG) || skill.has_inf2(INF2_ISENSEMBLE)) && map_cell::getcell(c, Some(bl.map), bl.x, bl.y, CELL_CHKBASILICA) != 0 {
        return 0; //Songs don't work in Basilica
    }
    let sc = sc_of(c, &bl);
    if sc.is_some() && bl.sc.option & OPTION_HIDE != 0 && !skill.has_inf2(INF2_TARGETHIDDEN) {
        return 0; //Hidden characters are immune to AoE skills except to these. [Skotlex]
    }
    if sc.as_ref().map(|s| s.has(SC_VACUUM_EXTREME)).unwrap_or(false) && lp(bl.map, bl.x, bl.y) {
        sc::status_change_end(c, bl.id, SC_VACUUM_EXTREME, 0);
    }
    if sc.as_ref().map(|s| s.has(SC_HOVERING)).unwrap_or(false) && skill.has_inf2(INF2_IGNOREHOVERING) {
        return 0; // Under Hovering characters are immune to select trap and ground target skills.
    }
    let t = skill_get_sc(sg.skill_id) as i32;
    let sce = if t != crate::db::skills::SC_NONE as i32 { sc::get_sce(c, bl.id, t) } else { None };
    let has_sce = sce.is_some() && sc.is_some();
    let skill_id = sg.skill_id as i32; //In case the group is deleted, we need to return the correct skill id, still.
    let sc_start = |t: i32, rate: i32, v1: i32, dur: i64| sc::sc_start(c, Some(&ss), &bl, t, rate, v1, dur);
    let sc_start4 = |t: i32, rate: i32, v1: i32, v2: i32, v3: i32, v4: i32, dur: i64| sc::sc_start4(c, Some(&ss), &bl, t, rate, v1, v2, v3, v4, dur);
    match sg.unit_id {
        UNT_SPIDERWEB => {
            if sc.is_some() {
                //Duration in PVM is: 1st - 8s, 2nd - 16s, 3rd - 8s
                //Duration in PVP is: 1st - 4s, 2nd - 8s, 3rd - 12s
                let mut sec = skill_get_time2(sg.skill_id, sg.skill_lv) as i64;
                let vs = crate::npc::mapflag_vs(c, bl.map);
                if vs {
                    sec /= 2;
                }
                if let Some(mut e) = sc::get_sce(c, bl.id, t) {
                    if e.val2 != 0 && e.val3 != 0 && e.val4 != 0 {
                        //Already triple affected, immune
                        sg.limit = tick - sg.tick;
                        data::put_group(c, &sg);
                    } else {
                        //Don't increase val1 here, we need a higher val in status_change_start so it overwrites the old one
                        if vs && e.val1 < 3 {
                            sec *= (e.val1 + 1) as i64;
                        } else if !vs && e.val1 < 2 {
                            sec *= (e.val1 + 1) as i64;
                        }
                        //Add group id to status change
                        if e.val2 == 0 {
                            e.val2 = sg.group_id;
                        } else if e.val3 == 0 {
                            e.val3 = sg.group_id;
                        } else if e.val4 == 0 {
                            e.val4 = sg.group_id;
                        }
                        sc::put_sce(c, &e);
                        //Overwrite status change with new duration
                        if let Some((due, _)) = crate::timer::get(c, e.timer) {
                            sc::status_change_start(c, Some(&ss), &bl, t, 10000, e.val1 + 1, e.val2, e.val3, e.val4, (due - tick).max(sec), SCSTART_NORATEDEF, 0);
                        }
                        data::upd_group(c, sg.group_id, |g| {
                            g.val2 = bl.id as i32;
                            g.limit = tick - g.tick + sec;
                        });
                    }
                } else {
                    if sc::status_change_start(c, Some(&ss), &bl, t, 10000, 1, sg.group_id, 0, 0, sec, SCSTART_NORATEDEF, 0) {
                        if let Some(e) = sc::get_sce(c, bl.id, t) {
                            if let Some((due, _)) = crate::timer::get(c, e.timer) {
                                sec = due - tick;
                            }
                        }
                        super::movement::map_moveblock(c, bl.id, unit.bl.x, unit.bl.y, tick);
                        if let Some(b) = unit::get(c, bl.id) {
                            crate::clif::fixpos(c, &b);
                        }
                    } else {
                        sec = 3000; //Couldn't trap it?
                    }
                    data::upd_group(c, sg.group_id, |g| {
                        g.val2 = bl.id as i32;
                        g.limit = tick - g.tick + sec;
                    });
                }
            }
        }
        UNT_SAFETYWALL => {
            if !has_sce {
                sc_start4(t, 100, sg.skill_lv as i32, sg.skill_id as i32, sg.group_id, 0, sg.limit);
            }
        }
        UNT_BLOODYLUST => {
            if sg.src_id != bl.id {
                //Does not affect the caster.
                if !has_sce && sc_start4(t, 100, sg.skill_lv as i32, 0, SC__BLOODYLUST, 0, sg.limit) {
                    // Dirty fix to add extra time to Bloody Lust so it doesn't end before
                    // Berserk, causing HP to drop to 100 when we don't want it to [Akinari]
                    sc_start(SC__BLOODYLUST, 100, sg.skill_lv as i32, sg.limit + 100);
                }
            }
        }
        UNT_PNEUMA => {
            if !has_sce {
                sc_start4(t, 100, sg.skill_lv as i32, sg.group_id, 0, 0, sg.limit);
            }
        }
        UNT_CHAOSPANIC => {
            sc::status_change_start(
                c,
                Some(&ss),
                &bl,
                t,
                3500 + (sg.skill_lv as i32 * 1500),
                sg.skill_lv as i32,
                0,
                0,
                1,
                skill_get_time2(sg.skill_id, sg.skill_lv) as i64,
                SCSTART_NOAVOID | SCSTART_NORATEDEF | SCSTART_NOTICKDEF,
                0,
            );
        }
        UNT_WARP_WAITING => {
            let mut working = sg.val1 & 0xffff;
            if bl.kind == BL_PC && working == 0 {
                let chat = crate::pc::get(c, bl.id).map(|p| p.chat_id).unwrap_or(0);
                if (chat == 0 || conf::chat_warpportal()) && bl.ud.to_x == unit.bl.x && bl.ud.to_y == unit.bl.y {
                    let x = (sg.val2 >> 16) as i16;
                    let y = (sg.val2 & 0xffff) as i16;
                    let mut count = sg.val1 >> 16;
                    let m = sg.val3 as u16;
                    count -= 1;
                    if count <= 0 {
                        group::skill_delunitgroup(c, sg.group_id);
                    }
                    if m == bl.map && x == bl.x && y == bl.y {
                        working = 1; /* we break it because officials break it, lovely stuff. */
                    }
                    sg.val1 = (count << 16) | working;
                    data::put_group(c, &sg);
                    let class = crate::pc::character_of(c, bl.id).map(|ch| ch.class as i32).unwrap_or(0);
                    if crate::pc::job_can_entermap(c, class, m, crate::pc::group_level(c, bl.id)) {
                        crate::pc::setpos(c, bl.id, m, x, y, crate::unit::CLR_TELEPORT);
                    }
                }
            } else if bl.kind == BL_MOB && conf::mob_warp() & 2 != 0 {
                // map_mapindex2mapid(sg->val3) (every map is on this map-server)
                let m = sg.val3;
                crate::mob::unit::unit_warp(c, bl.id, m, (sg.val2 >> 16) as i16, (sg.val2 & 0xffff) as i16, crate::unit::CLR_TELEPORT);
            }
        }
        UNT_QUAGMIRE => {
            if !has_sce && battle_check_target(c, &unit.bl, &bl, sg.target_flag) > 0 {
                sc_start4(t, 100, sg.skill_lv as i32, sg.group_id, 0, 0, sg.limit);
            }
        }
        UNT_VOLCANO | UNT_DELUGE | UNT_VIOLENTGALE | UNT_FIRE_INSIGNIA | UNT_WATER_INSIGNIA | UNT_WIND_INSIGNIA | UNT_EARTH_INSIGNIA => {
            if !has_sce {
                sc_start(t, 100, sg.skill_lv as i32, sg.limit);
            }
        }
        UNT_WATER_BARRIER | UNT_ZEPHYR | UNT_POWER_OF_GAIA => {
            if bl.id != ss.id && !has_sce {
                // Doesn't affect the Elemental
                sc_start(t, 100, sg.skill_lv as i32, sg.limit);
            }
        }
        UNT_SUITON => {
            if !has_sce {
                let v2 = if crate::npc::mapflag_vs(c, bl.map) || battle_check_target(c, &unit.bl, &bl, BCT_ENEMY) > 0 { 1 } else { 0 }; //Send val3 =1 to reduce agi.
                sc_start4(t, 100, sg.skill_lv as i32, v2, 0, 0, sg.limit);
            }
        }
        UNT_HERMODE | UNT_RICHMANKIM | UNT_ETERNALCHAOS | UNT_DRUMBATTLEFIELD | UNT_RINGNIBELUNGEN | UNT_ROKISWEIL | UNT_INTOABYSS
        | UNT_SIEGFRIED => {
            if sg.unit_id == UNT_HERMODE && sg.src_id != bl.id && battle_check_target(c, &unit.bl, &bl, BCT_PARTY | BCT_GUILD) > 0 {
                iface::status_change_clear_buffs(c, &bl, iface::SCCB_HERMODE); //Should dispell only allies.
            }
            //Needed to check when a dancer/bard leaves their ensemble area.
            let spirit_bd = sc.as_ref().and_then(|s| s.get(SC_SPIRIT)).map(|e| e.val2 == SL_BARDDANCER as i32).unwrap_or(false);
            if sg.src_id == bl.id && !spirit_bd {
                return skill_id;
            }
            if !has_sce {
                sc_start4(t, 100, sg.skill_lv as i32, sg.val1, sg.val2, 0, sg.limit);
            }
        }
        UNT_WHISTLE | UNT_ASSASSINCROSS | UNT_POEMBRAGI | UNT_APPLEIDUN | UNT_HUMMING | UNT_DONTFORGETME | UNT_FORTUNEKISS
        | UNT_SERVICEFORYOU => {
            let spirit_bd = sc.as_ref().and_then(|s| s.get(SC_SPIRIT)).map(|e| e.val2 == SL_BARDDANCER as i32).unwrap_or(false);
            if sg.src_id == bl.id && !spirit_bd {
                return 0;
            }
            if sc.is_none() {
                return 0;
            }
            // Pseudo-infinite duration
            // In case it is just started, it needs to last one interval longer than the skill unit lasts
            // This is to ensure the skill unit is removed before the status change ends
            if !has_sce {
                sc_start4(t, 100, sg.skill_lv as i32, sg.val1, sg.val2, 0, sg.limit + SKILLUNITTIMER_INTERVAL);
            } else if conf::refresh_song() == 1 && sce.as_ref().map(|e| e.val4 == 1).unwrap_or(false) {
                //Readjust timers since the effect will not last long.
                if let Some(mut e) = sce.clone() {
                    e.val4 = 0; //remove the mark that we stepped out
                    crate::timer::delete(c, e.timer);
                    e.timer = crate::timer::add(c, tick + sg.limit + SKILLUNITTIMER_INTERVAL, crate::timer::Func::StatusChangeTimer, bl.id, t as i64);
                    sc::put_sce(c, &e);
                    // Update icon duration (refresh_song_icon: no)
                }
            }
        }
        UNT_FOGWALL => {
            if !has_sce {
                sc_start4(t, 100, sg.skill_lv as i32, sg.val1, sg.val2, sg.group_id, sg.limit);
                if battle_check_target(c, &unit.bl, &bl, BCT_ENEMY) > 0 {
                    iface::skill_additional_effect(c, &ss, &bl, sg.skill_id, sg.skill_lv, crate::battle::BF_MISC, crate::battle::ATK_DEF, tick);
                }
            }
        }
        // (UNT_GRAVITATION, UNT_BASILICA: pre-renewal only)
        UNT_MOONLIT => {
            //Knockback out of area if affected char isn't in Moonlit effect
            let dancing_moonlit = sc.as_ref().and_then(|s| s.get(SC_DANCING)).map(|e| (e.val1 & 0xFFFF) == CG_MOONLIT as i32).unwrap_or(false);
            if !dancing_moonlit && ss.id != bl.id {
                //Also needed to prevent infinite loop crash.
                iface::skill_blown(c, &ss, &bl, skill_get_blewcount(sg.skill_id, sg.skill_lv), crate::unit::getdir(&bl) as i8, BLOWN_NONE);
            }
        }
        UNT_REVERBERATION => {
            if sg.src_id != bl.id {
                //Does not affect the caster.
                super::clif::clif_changetraplook(c, &unit.bl, UNT_USED_TRAPS);
                let ids = util::ids_in_range_wc(c, &unit.bl, skill_get_splash(sg.skill_id, sg.skill_lv) as i16, sg.bl_flag as u16);
                util::each(c, &ids, |t| super::ontimer::skill_trap_splash(c, t, unit_id, tick));
                data::upd_group(c, sg.group_id, |g| {
                    g.limit = tick - g.tick + 1500;
                    g.unit_id = UNT_USED_TRAPS;
                });
            }
        }
        UNT_FIRE_EXPANSION_SMOKE_POWDER => {
            if !has_sce && battle_check_target(c, &unit.bl, &bl, sg.target_flag) > 0 {
                sc_start(t, 100, sg.skill_lv as i32, sg.limit);
            }
        }
        UNT_FIRE_EXPANSION_TEAR_GAS => {
            if !has_sce && battle_check_target(c, &unit.bl, &bl, sg.target_flag) > 0 && crate::npc::mapflag_vs(c, bl.map) && sc_start4(t, 100, sg.skill_lv as i32, 0, ss.id as i32, 0, sg.limit) {
                sc_start(SC_TEARGAS_SOB, 100, sg.skill_lv as i32, sg.limit);
            }
        }
        UNT_VOLCANIC_ASH => {
            if !has_sce {
                sc_start(SC_ASH, 100, sg.skill_lv as i32, skill_get_time(MH_VOLCANIC_ASH, sg.skill_lv) as i64);
            }
        }
        UNT_KINGS_GRACE => {
            if !has_sce {
                let mut state = 0;
                if !crate::npc::mapflag_vs(c, ss.map) && !crate::npc::mapflag_gvg2(c, ss.map) {
                    state |= BCT_GUILD;
                }
                if battle_check_target(c, &unit.bl, &bl, BCT_SELF | BCT_PARTY | state) > 0 {
                    sc_start4(t, 100, sg.skill_lv as i32, 0, ss.id as i32, 0, sg.limit);
                }
            }
        }
        UNT_STEALTHFIELD => {
            if bl.id != sg.src_id && !has_sce {
                // Doesn't work on self (video shows that)
                sc_start(t, 100, sg.skill_lv as i32, sg.limit);
            }
        }
        UNT_NEUTRALBARRIER => {
            if !has_sce {
                sc::status_change_start(c, Some(&ss), &bl, t, 10000, sg.skill_lv as i32, 0, 0, 0, sg.limit, SCSTART_NOICON, 0);
            }
        }
        UNT_WARMER => {
            if !has_sce && bl.kind == BL_PC && !sc::battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) && tstatus.race as i32 != RC_DEMON {
                sc::sc_start2(c, Some(&ss), &bl, t, 100, sg.skill_lv as i32, ss.id as i32, skill_get_time(sg.skill_id, sg.skill_lv) as i64);
            }
        }
        UNT_CATNIPPOWDER => {
            if sg.src_id != bl.id && !has_sce && battle_check_target(c, &unit.bl, &bl, BCT_ENEMY) > 0 {
                // Does not affect the caster or Boss.
                sc_start(t, 100, sg.skill_lv as i32, skill_get_time(sg.skill_id, sg.skill_lv) as i64);
            }
        }
        UNT_NYANGGRASS => {
            if !has_sce {
                sc_start(t, 100, sg.skill_lv as i32, skill_get_time(sg.skill_id, sg.skill_lv) as i64);
            }
        }
        UNT_CREATINGSTAR => {
            if !has_sce {
                sc_start4(t, 100, sg.skill_lv as i32, ss.id as i32, unit.id() as i32, 0, sg.limit);
            }
        }
        UNT_GD_LEADERSHIP | UNT_GD_GLORYWOUNDS | UNT_GD_SOULCOLD | UNT_GD_HAWKEYES => {
            if !has_sce && battle_check_target(c, &unit.bl, &bl, sg.target_flag) > 0 {
                sc_start4(t, 100, sg.skill_lv as i32, 0, 0, 0, 1000);
            }
        }
        _ => {}
    }
    skill_id
}

/// skill.cpp skill_unit_onout: `bl` steps off a unit (or the unit goes from under it). Returns the skill id, or -1
/// for a bard / dancer leaving their own song (their song timer must not be touched).
pub fn skill_unit_onout(c: &Ctx, unit_id: u32, bl: &Unit, _tick: Tick) -> i32 {
    let Some(src) = data::get_unit(c, unit_id) else { return 0 };
    let Some(sg) = data::skill_id2group(c, src.group_id) else { return 0 };
    let Some(bl) = unit::get(c, bl.id) else { return 0 };
    let sc = sc_of(c, &bl);
    let t = skill_get_sc(sg.skill_id) as i32;
    let sce = if sc.is_some() && t != crate::db::skills::SC_NONE as i32 { sc::get_sce(c, bl.id, t) } else { None };
    if !bl.on_map || (sc::status_isdead(c, &bl) && sg.unit_id != UNT_ANKLESNARE) {
        //Need to delete the trap if the source died.
        return 0;
    }
    match sg.unit_id {
        UNT_SAFETYWALL | UNT_PNEUMA | UNT_EPICLESIS | UNT_TOTEM_OF_TUTELARY => {
            if sce.is_some() {
                sc::status_change_end(c, bl.id, t, 0);
            }
        }
        // (UNT_BASILICA: pre-renewal only)
        UNT_HERMODE => {
            //Clear Hermode if the owner moved.
            if let Some(e) = &sce {
                if e.val3 == BCT_SELF && e.val4 == sg.group_id {
                    sc::status_change_end(c, bl.id, t, 0);
                }
            }
        }
        UNT_DISSONANCE | UNT_UGLYDANCE | UNT_WHISTLE | UNT_ASSASSINCROSS | UNT_POEMBRAGI | UNT_APPLEIDUN | UNT_HUMMING
        | UNT_DONTFORGETME | UNT_FORTUNEKISS | UNT_SERVICEFORYOU => {
            let spirit_bd = sc.as_ref().and_then(|s| s.get(SC_SPIRIT)).map(|e| e.val2 == SL_BARDDANCER as i32).unwrap_or(false);
            if sg.src_id == bl.id && !spirit_bd {
                return -1;
            }
        }
        _ => {}
    }
    sg.skill_id as i32
}

/// skill.cpp skill_unit_onleft: `bl` left a unit group entirely (or it went). [Skotlex]
pub fn skill_unit_onleft(c: &Ctx, skill_id: u16, bl: &Unit, tick: Tick) -> i32 {
    let mut sc = sc_of(c, bl);
    if sc.as_ref().map(|s| s.empty()).unwrap_or(false) {
        sc = None;
    }
    let t = skill_get_sc(skill_id) as i32;
    let sce = if sc.is_some() && t != crate::db::skills::SC_NONE as i32 { sc::get_sce(c, bl.id, t) } else { None };
    match skill_id {
        WZ_QUAGMIRE => {
            if bl.kind != BL_MOB && sce.is_some() {
                sc::status_change_end(c, bl.id, t, 0);
            }
        }
        BD_LULLABY | BD_RICHMANKIM | BD_ETERNALCHAOS | BD_DRUMBATTLEFIELD | BD_RINGNIBELUNGEN | BD_ROKISWEIL | BD_INTOABYSS
        | BD_SIEGFRIED => {
            let dancing_it = sc.as_ref().and_then(|s| s.get(SC_DANCING)).map(|e| (e.val1 & 0xFFFF) == skill_id as i32).unwrap_or(false);
            if dancing_it {
                //Check if you just stepped out of your ensemble skill to cancel dancing. [Skotlex]
                //We don't check for SC_LONGING because someone could always have knocked you back and out of the song/dance.
                sc::status_change_end(c, bl.id, SC_DANCING, 0);
            }
            // Sleep effect of Lullaby doesn't end even if song ends
            if skill_id != BD_LULLABY && sce.is_some() {
                sc::status_change_end(c, bl.id, t, 0);
            }
        }
        MH_STEINWAND | MG_SAFETYWALL | AL_PNEUMA | SA_VOLCANO | SA_DELUGE | SA_VIOLENTGALE | CG_HERMODE | NJ_SUITON
        | sk::SC_MAELSTROM | EL_WATER_BARRIER | EL_ZEPHYR | EL_POWER_OF_GAIA | SO_WARMER | SO_FIRE_INSIGNIA | SO_WATER_INSIGNIA
        | SO_WIND_INSIGNIA | SO_EARTH_INSIGNIA | SJ_BOOKOFCREATINGSTAR | sk::SC_BLOODYLUST | GN_FIRE_EXPANSION_SMOKE_POWDER
        | GN_FIRE_EXPANSION_TEAR_GAS | LG_KINGS_GRACE | NC_STEALTHFIELD | NC_NEUTRALBARRIER | SU_NYANGGRASS => {
            // (HW_GRAVITATION, HP_BASILICA: pre-renewal only)
            if sce.is_some() {
                sc::status_change_end(c, bl.id, t, 0);
            }
        }
        BA_POEMBRAGI | BA_WHISTLE | BA_ASSASSINCROSS | BA_APPLEIDUN | DC_HUMMING | DC_DONTFORGETME | DC_FORTUNEKISS
        | DC_SERVICEFORYOU => {
            if bl.kind == BL_PC && sce.as_ref().map(|e| e.val4 == 0).unwrap_or(false) {
                if let Some(mut e) = sce.clone() {
                    crate::timer::delete(c, e.timer);
                    //NOTE: It'd be nice if we could get the skill_lv for a more accurate extra time, but alas...
                    //not possible on our current implementation.
                    let duration = skill_get_time2(skill_id, 1) as i64;
                    e.val4 = 1; //Store the fact that this is a "reduced" duration effect.
                    e.timer = crate::timer::add(c, tick + duration, crate::timer::Func::StatusChangeTimer, bl.id, t as i64);
                    sc::put_sce(c, &e);
                    // Update icon duration (refresh_song_icon: no)
                }
            } else if bl.kind != BL_PC {
                // Non-players don't have lingering effects
                sc::status_change_end(c, bl.id, t, 0);
            }
        }
        PF_FOGWALL => {
            if sce.is_some() {
                sc::status_change_end(c, bl.id, t, 0);
                if let Some(mut blind) = sc::get_sce(c, bl.id, SC_BLIND) {
                    if bl.kind == BL_PC {
                        //Players get blind ended inmediately, others have it still for 30 secs. [Skotlex]
                        sc::status_change_end(c, bl.id, SC_BLIND, 0);
                    } else {
                        crate::timer::delete(c, blind.timer);
                        blind.timer = crate::timer::add(c, 30000 + tick, crate::timer::Func::StatusChangeTimer, bl.id, SC_BLIND as i64);
                        sc::put_sce(c, &blind);
                    }
                }
            }
        }
        GD_LEADERSHIP | GD_GLORYWOUNDS | GD_SOULCOLD | GD_HAWKEYES => {
            if !sce.as_ref().map(|e| e.val4 != 0).unwrap_or(false) {
                sc::status_change_end(c, bl.id, t, 0);
            }
        }
        _ => {}
    }
    skill_id as i32
}

/// skill.cpp skill_unit_ondamaged: damage to a trap, Ice Wall, Wall of Thorn … takes its HP (val1); other units take
/// no damage.
pub fn skill_unit_ondamaged(c: &Ctx, unit_id: u32, damage: i64) -> i64 {
    let Some(unit) = data::get_unit(c, unit_id) else { return 0 };
    let Some(sg) = data::skill_id2group(c, unit.group_id) else { return 0 };
    match sg.unit_id {
        UNT_BLASTMINE | UNT_SKIDTRAP | UNT_LANDMINE | UNT_SHOCKWAVE | UNT_SANDMAN | UNT_FLASHER | UNT_CLAYMORETRAP
        | UNT_FREEZINGTRAP | UNT_ANKLESNARE | UNT_ICEWALL | UNT_WALLOFTHORN | UNT_REVERBERATION | UNT_NETHERWORLD => {
            data::upd_unit(c, unit_id, |u| u.val1 = u.val1.wrapping_sub(damage.clamp(i32::MIN as i64, i32::MAX as i64) as i32));
            damage
        }
        _ => 0,
    }
}
