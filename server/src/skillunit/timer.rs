//! skill.cpp skill_unit_timer (every SKILLUNITTIMER_INTERVAL ms over skillunit_db), skill_unit_timer_sub (a unit's
//! expiry, its active-state checks and its interval effect) and skill_unit_timer_sub_onplace.
//!
//! rAthena arms skill_unit_timer once in do_init_skill (`add_timer_interval(gettick()+SKILLUNITTIMER_INTERVAL, ...)`)
//! and it runs for the server's whole life. The module has no server start: the interval timer is armed when a group
//! is made and stops when no unit is left (a run over an empty skillunit_db does nothing). It is armed on a fixed
//! 100 ms grid of the tick, never at the group's own tick + 100: as with rAthena's timer running since the boot, a new
//! group's first unit tick then comes 1-100 ms after it is made, so a group whose limit is 100 ms (Thunder Storm,
//! Earth Strain's waves, Fire Rain's waves …) gets its one tick and a limit that is a multiple of 100 ms its last one
//! (COORD 2026-09-28 13:11 mage, 16:08 ninjags, 16:34 thief).

use crate::battle::target::battle_check_target;
use crate::battle::BCT_ENEMY;
use crate::db::skills::{self as sk, *};
use crate::map::{BL_CHAR, BL_PC, BL_SKILL};
use crate::map_cell::{self, CELL_CHKLANDPROTECTOR};
use crate::status::consts::*;
use crate::status::sc;
use crate::timer::{self, Func};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

use super::consts::*;
use super::data::{self};
use super::{conf, group, iface, ontimer, setting, util};

/// Arms the Func::SkillUnitTimer interval timer if it is not running.
pub fn ensure_unit_timer(c: &Ctx) {
    let mut s = data::state(c);
    if s.timer != 0 && timer::get(c, s.timer).is_some() {
        return;
    }
    // the next tick of the fixed grid (module doc), as rAthena's boot-armed interval timer
    let due = c.tick() - c.tick().rem_euclid(SKILLUNITTIMER_INTERVAL) + SKILLUNITTIMER_INTERVAL;
    s.timer = timer::add_interval(c, due, Func::SkillUnitTimer, 0, 0, SKILLUNITTIMER_INTERVAL);
    data::put_state(c, &s);
}

/// skill.cpp skill_unit_timer (TIMER_FUNC): skill_unit_timer_sub for every unit, in skillunit_db order.
pub fn skill_unit_timer(c: &Ctx, tid: u64, tick: Tick) {
    // FreeBlockLock freeLock (map_freeblock_lock: block_lists freed meanwhile stay readable until the run ends; a
    // unit's rows here last until the end of skill_delunit). skillunit_db's own lock is db_obj_vforeach's: deleted
    // nodes are skipped and freed after the walk (unitdb::foreach)
    super::unitdb::foreach(c, |key| {
        skill_unit_timer_sub(c, key, tick);
    });
    if super::unitdb::len(c) == 0 {
        // nothing left to run over: the timer stops until the next group (see the module doc)
        timer::delete(c, tid);
        let mut s = data::state(c);
        if s.timer == tid {
            s.timer = 0;
            data::put_state(c, &s);
        }
    }
}

/// skill.cpp skill_unit_timer_sub_onplace: `bl` in range of `unit` gets the unit's interval effect if it is a valid
/// target.
pub fn skill_unit_timer_sub_onplace(c: &Ctx, bl: &Unit, unit_id: u32, tick: Tick) -> i32 {
    let Some(unit) = data::get_unit(c, unit_id) else { return 0 };
    if !unit.alive || !bl.on_map {
        return 0;
    }
    let Some(group) = data::skill_id2group(c, unit.group_id) else { return 0 };
    let Some(skill) = skill_db().find(group.skill_id) else { return 0 };
    let lp = if conf::land_protector_behavior() {
        map_cell::getcell(c, Some(bl.map), bl.x, bl.y, CELL_CHKLANDPROTECTOR) != 0
    } else {
        map_cell::getcell(c, Some(unit.bl.map), unit.bl.x, unit.bl.y, CELL_CHKLANDPROTECTOR) != 0
    };
    if !(skill.has_inf2(INF2_ISSONG) || skill.has_inf2(INF2_ISTRAP)) && !skill.has_inf2(INF2_IGNORELANDPROTECTOR) && group.skill_id != NC_NEUTRALBARRIER && lp {
        return 0; //AoE skills are ineffective. [Skotlex]
    }
    // RENEWAL: Ankle Snare and Skid Trap can no longer trap bosses in renewal
    if (group.unit_id == UNT_ANKLESNARE || group.unit_id == UNT_SKIDTRAP) && crate::battle::bl::has_mode(c, bl, crate::status::defs::MD_STATUSIMMUNE) {
        return 0;
    }
    if battle_check_target(c, &unit.bl, bl, group.target_flag) <= 0 {
        return 0;
    }
    ontimer::skill_unit_onplace_timer(c, unit_id, bl, tick);
    1
}

/// The meteor-type skills whose units deal their damage when they expire.
fn is_meteor(skill_id: u16) -> bool {
    matches!(
        skill_id,
        WZ_METEOR | SU_CN_METEOR | SU_CN_METEOR2 | AG_VIOLENT_QUAKE_ATK | AG_ALL_BLOOM_ATK | AG_ALL_BLOOM_ATK2 | NPC_RAINOFMETEOR | HN_METEOR_STORM_BUSTER
    )
}

/// skill.cpp skill_unit_timer_sub: one unit's turn of skill_unit_timer.
pub fn skill_unit_timer_sub(c: &Ctx, unit_id: u32, tick: Tick) -> i32 {
    let Some(unit) = data::get_unit(c, unit_id) else { return 0 };
    if !unit.alive {
        return 0;
    }
    let Some(group) = data::skill_id2group(c, unit.group_id) else { return 0 };
    let gid = group.group_id;
    let bl = unit.bl.clone();
    // Check for expiration
    if !group.guildaura && (tick - group.tick >= group.limit || tick - group.tick >= unit.limit) {
        // skill unit expired (inlined from skill_unit_onlimit())
        match group.unit_id {
            UNT_ICEWALL => {
                let mut hp = 0;
                data::upd_unit(c, unit_id, |u| {
                    u.val1 -= 50; // icewall loses 50 hp every second
                    u.limit = tick + group.interval as i64 - group.tick;
                    hp = u.val1;
                });
                data::upd_group(c, gid, |g| g.limit = tick + g.interval as i64 - g.tick);
                if hp <= 0 {
                    group::skill_delunit(c, unit_id);
                }
            }
            // RENEWAL: UNT_CLAYMORETRAP turns into used traps like Blast Mine
            UNT_BLASTMINE | UNT_CLAYMORETRAP | UNT_GROUNDDRIFT_WIND | UNT_GROUNDDRIFT_DARK | UNT_GROUNDDRIFT_POISON
            | UNT_GROUNDDRIFT_WATER | UNT_GROUNDDRIFT_FIRE => {
                data::upd_group(c, gid, |g| {
                    g.unit_id = UNT_USED_TRAPS;
                    //clif_changetraplook(bl, UNT_FIREPILLAR_ACTIVE);
                    g.limit = tick + 1500 - g.tick;
                });
                let gtick = group.tick;
                data::upd_unit(c, unit_id, |u| u.limit = tick + 1500 - gtick);
            }
            UNT_ANKLESNARE | UNT_ELECTRICSHOCKER | UNT_SKIDTRAP | UNT_LANDMINE | UNT_SHOCKWAVE | UNT_SANDMAN | UNT_FLASHER
            | UNT_FREEZINGTRAP | UNT_TALKIEBOX | UNT_CLUSTERBOMB | UNT_MAGENTATRAP | UNT_COBALTTRAP | UNT_MAIZETRAP
            | UNT_VERDURETRAP | UNT_FIRINGTRAP | UNT_ICEBOUNDTRAP => {
                if matches!(group.unit_id, UNT_ANKLESNARE | UNT_ELECTRICSHOCKER) && group.val2 > 0 {
                    //Used Trap doesn't return back to item
                    group::skill_delunit(c, unit_id);
                } else {
                    // (UNT_CLAYMORETRAP returns to an item only in pre-renewal)
                    let src = unit::get(c, group.src_id);
                    if unit.val1 > 0 && src.as_ref().map(|s| s.kind == BL_PC).unwrap_or(false) {
                        // revert unit back into a trap
                        let mut item = crate::inventory::Item::default();
                        item.nameid = if group.item_id != 0 { group.item_id } else { setting::ITEMID_TRAP };
                        item.identify = 1;
                        crate::flooritem::map_addflooritem(c, &item, 1, bl.map, bl.x, bl.y, 0, 0, 0, 4, 0, false, None, crate::map::BL_ITEM);
                    }
                    group::skill_delunit(c, unit_id);
                }
            }
            UNT_WARP_ACTIVE => {
                // warp portal opens (morph to a UNT_WARP_WAITING cell)
                let new_id = skill_get_unit_id2(group.skill_id); // UNT_WARP_WAITING
                data::upd_group(c, gid, |g| g.unit_id = new_id);
                super::clif::clif_changelook_unit(c, &bl, new_id);
                // restart timers
                let t = skill_get_time(group.skill_id, group.skill_lv) as i64;
                data::upd_group(c, gid, |g| g.limit = t);
                data::upd_unit(c, unit_id, |u| u.limit = t);
                // apply effect to all units standing on it
                if let Some(u) = data::get_unit(c, unit_id) {
                    let bl_flag = data::skill_id2group(c, gid).map(|g| g.bl_flag).unwrap_or(0);
                    super::effect::foreachincell_unit_effect(c, &u, u.bl.map, u.bl.x, u.bl.y, bl_flag, c.tick(), 1);
                }
            }
            UNT_CALLFAMILY => {
                if group.val1 != 0 {
                    let sd = crate::pc::charid2sd(c, group.val1 as u32);
                    data::upd_group(c, gid, |g| g.val1 = 0);
                    if let Some(sd) = sd {
                        call_family_warp(c, &sd, &bl);
                    }
                }
                let val2 = data::skill_id2group(c, gid).map(|g| g.val2).unwrap_or(group.val2);
                if val2 != 0 {
                    let sd = crate::pc::charid2sd(c, val2 as u32);
                    data::upd_group(c, gid, |g| g.val2 = 0);
                    if let Some(sd) = sd {
                        call_family_warp(c, &sd, &bl);
                    }
                }
                group::skill_delunit(c, unit_id);
            }
            UNT_REVERBERATION | UNT_NETHERWORLD => {
                if unit.val1 <= 0 {
                    // If it was deactivated.
                    group::skill_delunit(c, unit_id);
                } else {
                    super::clif::clif_changetraplook(c, &bl, UNT_USED_TRAPS);
                    if group.unit_id == UNT_REVERBERATION {
                        let ids = util::ids_in_range_wc(c, &bl, skill_get_splash(group.skill_id, group.skill_lv) as i16, group.bl_flag as u16);
                        util::each(c, &ids, |t| ontimer::skill_trap_splash(c, t, unit_id, tick));
                    }
                    data::upd_group(c, gid, |g| {
                        g.limit = tick - g.tick + 1000;
                        g.unit_id = UNT_USED_TRAPS;
                    });
                    data::upd_unit(c, unit_id, |u| u.limit = tick - group.tick + 1000);
                }
            }
            UNT_FEINTBOMB => {
                if let Some(src) = unit::get(c, group.src_id) {
                    iface::skill_area_sub_castend_damage_id(c, &bl, unit.range, BL_CHAR | BL_SKILL, &src, group.skill_id, group.skill_lv, tick, BCT_ENEMY | SD_ANIMATION | 5);
                }
                group::skill_delunit(c, unit_id);
            }
            UNT_BANDING => {
                let banding = unit::get(c, group.src_id).map(|s| crate::battle::bl::sc(c, &s).map(|sc| sc.has(SC_BANDING)).unwrap_or(false)).unwrap_or(false);
                if !banding {
                    group::skill_delunit(c, unit_id);
                } else {
                    // This unit isn't removed while SC_BANDING is active.
                    data::upd_group(c, gid, |g| g.limit = tick + g.interval as i64 - g.tick);
                    data::upd_unit(c, unit_id, |u| u.limit = tick + group.interval as i64 - group.tick);
                }
            }
            UNT_B_TRAP => {
                let src = unit::get(c, group.src_id);
                if group.item_id != 0 && unit.val2 <= 0 && src.as_ref().map(|s| s.kind == BL_PC).unwrap_or(false) {
                    let mut item = crate::inventory::Item::default();
                    item.nameid = group.item_id;
                    item.identify = 1;
                    crate::flooritem::map_addflooritem(c, &item, 1, bl.map, bl.x, bl.y, 0, 0, 0, 4, 0, false, None, crate::map::BL_ITEM);
                }
                group::skill_delunit(c, unit_id);
            }
            _ => {
                if group.val2 == 1 && is_meteor(group.skill_id) {
                    // Deal damage before expiration
                } else {
                    group::skill_delunit(c, unit_id);
                }
            }
        }
    } else {
        // skill unit is still active
        match group.unit_id {
            UNT_BLASTMINE | UNT_SKIDTRAP | UNT_LANDMINE | UNT_SHOCKWAVE | UNT_SANDMAN | UNT_FLASHER | UNT_CLAYMORETRAP
            | UNT_FREEZINGTRAP | UNT_TALKIEBOX | UNT_ANKLESNARE | UNT_B_TRAP => {
                if unit.val1 <= 0 {
                    if group.unit_id == UNT_ANKLESNARE && group.val2 > 0 {
                        group::skill_delunit(c, unit_id);
                    } else {
                        super::clif::clif_changetraplook(c, &bl, if group.unit_id == UNT_LANDMINE { UNT_FIREPILLAR_ACTIVE } else { UNT_USED_TRAPS });
                        data::upd_group(c, gid, |g| {
                            g.limit = tick - g.tick + 1500;
                            g.unit_id = UNT_USED_TRAPS;
                        });
                    }
                }
            }
            UNT_REVERBERATION | UNT_NETHERWORLD => {
                if unit.val1 <= 0 {
                    super::clif::clif_changetraplook(c, &bl, UNT_USED_TRAPS);
                    if group.unit_id == UNT_REVERBERATION {
                        let ids = util::ids_in_range_wc(c, &bl, skill_get_splash(group.skill_id, group.skill_lv) as i16, group.bl_flag as u16);
                        util::each(c, &ids, |t| ontimer::skill_trap_splash(c, t, unit_id, tick));
                    }
                    data::upd_group(c, gid, |g| {
                        g.limit = tick - g.tick + 1000;
                        g.unit_id = UNT_USED_TRAPS;
                    });
                    data::upd_unit(c, unit_id, |u| u.limit = tick - group.tick + 1000);
                }
            }
            UNT_WALLOFTHORN => {
                if group.val3 < 0 {
                    // Remove if attacked by fire element, turned to Fire Wall
                    group::skill_delunitgroup(c, gid);
                } else if unit.val1 <= 0 || unit.val2 <= 0 {
                    // Remove the unit only if no HP or hit limit is reached
                    group::skill_delunit(c, unit_id);
                }
            }
            UNT_SANCTUARY => {
                if group.val1 <= 0 {
                    group::skill_delunitgroup(c, gid);
                }
            }
            _ => {
                if is_meteor(group.skill_id) {
                    if group.val2 == 0 && (tick - group.tick >= group.limit - group.interval as i64 || tick - group.tick >= unit.limit - group.interval as i64) {
                        // Unit will expire the next interval, start dropping Meteor
                        if let Some(src) = unit::get(c, group.src_id) {
                            if matches!(group.skill_id, AG_VIOLENT_QUAKE_ATK | AG_ALL_BLOOM_ATK | AG_ALL_BLOOM_ATK2) {
                                // Don't yell a blank skill name.
                                crate::mob::clif::skill_poseffect(c, &src, group.skill_id, u16::MAX, bl.x, bl.y, tick);
                            } else {
                                crate::mob::clif::skill_poseffect(c, &src, group.skill_id, group.skill_lv, bl.x, bl.y, tick);
                            }
                            data::upd_group(c, gid, |g| {
                                g.val2 = 1;
                                // All meteors hit 700ms after the effect starts, regardless of their interval
                                if matches!(g.skill_id, WZ_METEOR | SU_CN_METEOR | SU_CN_METEOR2) {
                                    g.limit = g.limit - g.interval as i64 + 700;
                                }
                            });
                        }
                    }
                    // No damage until expiration
                    return 0;
                }
            }
        }
    }
    //Don't continue if unit or even group is expired and has been deleted.
    let Some(unit) = data::get_unit(c, unit_id) else { return 0 };
    if !unit.alive {
        return 0;
    }
    if data::skill_id2group(c, unit.group_id).is_none() {
        return 0;
    }
    let dissonance = setting::skill_dance_switch(c, unit_id, false);
    let Some(group) = data::skill_id2group(c, unit.group_id) else { return 0 };
    if unit.range >= 0 && group.interval != -1 {
        let ids = if skill_get_unit_flag(group.skill_id, &[UF_PATHCHECK]) {
            util::ids_in_range_wc(c, &unit.bl, unit.range, group.bl_flag as u16)
        } else {
            util::ids_in_allrange(c, &unit.bl, unit.range, group.bl_flag as u16)
        };
        util::each(c, &ids, |t| skill_unit_timer_sub_onplace(c, t, unit_id, tick));
        let Some(unit) = data::get_unit(c, unit_id) else { return 0 };
        let Some(group) = data::skill_id2group(c, unit.group_id) else { return 0 };
        if unit.range == -1 {
            //Unit disabled, but it should not be deleted yet.
            data::upd_group(c, group.group_id, |g| g.unit_id = UNT_USED_TRAPS);
        } else if group.unit_id == UNT_TATAMIGAESHI {
            data::upd_unit(c, unit_id, |u| u.range = -1); //Disable processed cell.
            let mut left = 0;
            data::upd_group(c, group.group_id, |g| {
                g.val1 -= 1;
                left = g.val1;
            });
            if left <= 0 {
                // number of live cells
                //All tiles were processed, disable skill.
                data::upd_group(c, group.group_id, |g| {
                    g.target_flag = crate::battle::BCT_NOONE;
                    g.bl_flag = crate::map::BL_NUL as i32;
                });
            }
        } else if is_meteor(group.skill_id) || (matches!(group.skill_id, CR_GRANDCROSS | NPC_GRANDDARKNESS) && unit.val1 <= 0) {
            group::skill_delunit(c, unit_id);
            return 0;
        }
    }
    if dissonance {
        setting::skill_dance_switch(c, unit_id, true);
    }
    0
}

/// UNT_CALLFAMILY's warp of a family member to the unit (skill_unit_timer_sub).
fn call_family_warp(c: &Ctx, sd: &Unit, unit_bl: &Unit) {
    if crate::npc::mapflag(c, sd.map, crate::npc::MF_NOWARP) {
        return;
    }
    let class = crate::pc::character_of(c, sd.id).map(|ch| ch.class as i32).unwrap_or(0);
    if crate::pc::job_can_entermap(c, class, unit_bl.map, crate::pc::group_level(c, sd.id)) {
        crate::pc::setpos(c, sd.id, unit_bl.map, unit_bl.x, unit_bl.y, crate::unit::CLR_TELEPORT);
    }
}

#[allow(unused_imports)]
use {sc as _, sk as _};
