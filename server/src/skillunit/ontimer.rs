//! skill.cpp skill_unit_onplace_timer (a unit's effect every interval on what stands on it) and the trap helpers it
//! and the unit timer use: skill_trap_splash, skill_detonator, skill_reveal_trap(_inarea), skill_graffitiremover.

use crate::battle::target::battle_check_target;
use crate::battle::{ATK_DEF, BCT_ENEMY, BCT_PARTY, BCT_SELF, BF_LONG, BF_MAGIC, BF_MISC, BF_SKILL, BF_WEAPON, DMG_SINGLE, DMG_SPLASH};
use crate::db::skills::{self as sk, *};
use crate::map::{BL_CHAR, BL_MOB, BL_PC, BL_SKILL};
use crate::map_cell::{self, CELL_CHKLANDPROTECTOR};
use crate::status::consts::*;
use crate::status::defs::{ELE_FIRE, RC_DEMON, RC_PLANT, SCSTART_NOAVOID, SCSTART_NONE, SCSTART_NORATEDEF, SCSTART_NOTICKDEF};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

use super::consts::*;
use super::data::{self};
use super::{conf, group, iface, util};

/// skill.cpp skill_area_temp-free inline of `map_foreachinallrange(skill_area_sub, center, range, BL_CHAR, src, skill_id,
/// skill_lv, tick, BCT_ENEMY, skill_area_sub_count)`: skill_area_sub with flag BCT_ENEMY (no SD_SPLASH / SD_PREAMBLE)
/// calls skill_area_sub_count, which returns 1, for each target battle_check_target(src, bl, BCT_ENEMY) accepts.
pub fn skill_area_sub_count_enemies(c: &Ctx, center: &Unit, range: i16, src: &Unit) -> i32 {
    let ids = util::ids_in_allrange(c, center, range, BL_CHAR);
    util::each(c, &ids, |bl| (battle_check_target(c, src, bl, BCT_ENEMY) > 0) as i32)
}

/// status.hpp status_get_class_ (CLASS_NORMAL / CLASS_BOSS …).
fn class_of(c: &Ctx, bl: &Unit) -> i32 {
    crate::battle::bl::class_type(c, bl)
}

/// skill.cpp skill_unit_onplace_timer: the unit's periodic effect on `bl` (checked by skill_unit_timer_sub_onplace).
pub fn skill_unit_onplace_timer(c: &Ctx, unit_id: u32, bl: &Unit, tick: Tick) -> i32 {
    let Some(unit) = data::get_unit(c, unit_id) else { return 0 };
    let Some(bl) = unit::get(c, bl.id) else { return 0 };
    if !bl.on_map || !unit.alive || sc::status_isdead(c, &bl) {
        return 0;
    }
    let Some(sg) = data::skill_id2group(c, unit.group_id) else { return 0 };
    let Some(ss) = unit::get(c, sg.src_id) else { return 0 };
    let tsd = bl.kind == BL_PC;
    let tsc = crate::battle::bl::sc(c, &bl);
    let ssc = crate::battle::bl::sc(c, &ss);
    let tstatus = crate::status::status_get_status_data(c, &bl);
    let t = skill_get_sc(sg.skill_id) as i32;
    let skill_id = sg.skill_id;
    let Some(skill) = skill_db().find(skill_id) else { return 0 };
    let has_trap = skill.has_inf2(INF2_ISTRAP);
    if ssc.as_ref().and_then(|s| s.get(SC_VOICEOFSIREN)).map(|e| e.val2 == bl.id as i32).unwrap_or(false) && has_trap {
        return 0; // Traps cannot be activated by the Maestro or Wanderer that enticed the trapper with this skill.
    }
    if tsc.as_ref().map(|s| s.has(SC_HOVERING)).unwrap_or(false) && skill.has_inf2(INF2_IGNOREHOVERING) {
        return 0; // Under Hovering characters are immune to trap and ground target skills.
    }
    if sg.interval == -1 {
        match sg.unit_id {
            //These happen when a trap is splash-triggered by multiple targets on the same cell.
            UNT_ANKLESNARE | UNT_FIREPILLAR_ACTIVE | UNT_ELECTRICSHOCKER | UNT_MANHOLE => return 0,
            _ => {
                // Dissonance effect might have killed another bard/dancer and skill returned to normal
                if sg.song_dance & 0x1 != 0 {
                    return 0;
                }
                log::error!("skill_unit_onplace_timer: interval error (unit id {:x})", sg.unit_id);
                return 0;
            }
        }
    }
    //Not all have it, eg: Traps don't have it even though they can be hit by Heaven's Drive [Skotlex]
    if let Some(diff) = group::tickset_check(c, &bl, &sg, tick) {
        if diff < 0 {
            return 0;
        }
    }
    // Wall of Thorn damaged by Fire element unit [Cydh]
    // TODO: This check doesn't matter the location, as long as one of units touched, this check will be executed.
    if bl.kind == BL_SKILL && skill_get_ele(sg.skill_id, sg.skill_lv) == ELE_FIRE {
        if let Some(sug) = data::group_of_unit(c, bl.id) {
            if sug.unit_id == UNT_WALLOFTHORN {
                if let Some(src2) = unit::get(c, sug.src_id) {
                    super::setting::skill_unitsetting(c, &src2, sug.skill_id, sug.skill_lv, (sug.val3 >> 16) as i16, (sug.val3 & 0xffff) as i16, 1);
                } else {
                    log::warn!("skill_unit_onplace_timer: Wall of Thorn's source {} is gone (map_id2bl(...) is nullptr in rAthena)", sug.src_id);
                }
                data::upd_group(c, sug.group_id, |g| {
                    g.limit = 0;
                    g.unit_id = UNT_USED_TRAPS;
                });
                data::upd_group(c, sg.group_id, |g| {
                    g.limit = 0;
                    g.unit_id = UNT_USED_TRAPS;
                });
                return skill_id as i32;
            }
        }
    }
    let attack = |atk_type: i32, src: &Unit, dsrc: &Unit, target: &Unit, id: u16, lv: u16, tk: Tick, flag: i32| iface::skill_attack(c, atk_type, src, dsrc, target, id, lv, tk, flag);
    let ubl = unit.bl.clone();
    match sg.unit_id {
        // Units that deals simple attack (UNT_GRAVITATION: RENEWAL attacks from the caster, below)
        UNT_EARTHSTRAIN | UNT_FIREWALK | UNT_ELECTRICWALK | UNT_PSYCHIC_WAVE | UNT_LAVA_SLIDE | UNT_MAKIBISHI | UNT_VENOMFOG
        | UNT_ICEMINE | UNT_FLAMECROSS | UNT_HELLBURNING | UNT_CANE_OF_EVIL_EYE | UNT_RAIN_OF_CRYSTAL | UNT_MYSTERY_ILLUSION
        | UNT_STRANTUM_TREMOR | UNT_TORNADO_STORM | UNT_FLORAL_FLARE_ROAD | UNT_CROSS_RAIN | UNT_PNEUMATICUS_PROCELLA
        | UNT_LIGHTNING_LAND | UNT_VENOM_SWAMP | UNT_CONFLAGRATION | UNT_DEEPBLINDTRAP | UNT_SOLIDTRAP | UNT_SWIFTTRAP
        | UNT_FLAMETRAP | UNT_STAR_BURST | UNT_GRENADES_DROPPING | UNT_MISSION_BOMBARD | UNT_FUUMASHOUAKU | UNT_KUNAIKAITEN => {
            attack(skill_get_type(sg.skill_id), &ss, &ubl, &bl, sg.skill_id, sg.skill_lv, tick, 0);
        }
        UNT_GRAVITATION | UNT_GROUND_GRAVITATION | UNT_JACK_FROST_NOVA => {
            // RENEWAL: UNT_GRAVITATION attacks from the caster
            attack(skill_get_type(sg.skill_id), &ss, &ss, &bl, sg.skill_id, sg.skill_lv, tick, 0);
        }
        UNT_DUMMYSKILL => match sg.skill_id {
            SG_SUN_WARM | SG_MOON_WARM | SG_STAR_WARM => {
                //SG skills [Komurka]
                let mut count: i64 = 0;
                let (x, y) = (bl.x, bl.y);
                //If target isn't knocked back it should hit every "interval" ms [Playtester]
                loop {
                    let mut stop = false;
                    if bl.kind == BL_PC {
                        crate::status::hp::status_zap(c, bl.id, 0, 10, 0); // sp damage to players
                    } else {
                        // mobs
                        // Bosses trigger the effect only 1 out of 5 times ("continue" re-tests the loop condition)
                        if class_of(c, &bl) == crate::status::defs::CLASS_BOSS && c.rnd_chance(4, 5) {
                        } else if crate::status::hp::status_charge(c, ss.id, 0, 2) == 0 {
                            // costs 2 SP per hit
                            //should end when out of sp.
                            data::upd_group(c, sg.group_id, |g| g.limit = tick - g.tick);
                            stop = true;
                        } else {
                            attack(BF_WEAPON, &ss, &ubl, &bl, sg.skill_id, sg.skill_lv, tick + count * sg.interval as i64, 0);
                        }
                    }
                    if stop {
                        break;
                    }
                    let b = unit::get(c, bl.id);
                    count += 1;
                    let still = b.as_ref().map(|b| b.x == x && b.y == y).unwrap_or(false);
                    if !(sg.interval > 0 && still && count < SKILLUNITTIMER_INTERVAL / sg.interval as i64 && !b.as_ref().map(|b| sc::status_isdead(c, b)).unwrap_or(true)) {
                        break;
                    }
                }
            }
            // (WZ_STORMGUST's counter: pre-renewal only)
            GS_DESPERADO => {
                if ((c.rnd() % 100) as i32) < unit.val1 {
                    attack(BF_WEAPON, &ss, &ubl, &bl, sg.skill_id, sg.skill_lv, tick, 0);
                }
            }
            NPC_COMET | WL_COMET => {
                if map_cell::getcell(c, Some(bl.map), bl.x, bl.y, CELL_CHKLANDPROTECTOR) == 0 {
                    // Nothing should happen if the target is on Land Protector
                    attack(skill_get_type(sg.skill_id), &ss, &ubl, &bl, sg.skill_id, sg.skill_lv, tick, 0);
                }
            }
            NPC_WIDESUCK => {
                let heal = attack(skill_get_type(sg.skill_id), &ss, &ubl, &bl, sg.skill_id, sg.skill_lv, tick, 0) as i32;
                if heal > 0 {
                    crate::battle::atcommand::clif_skill_nodamage(c, Some(&ss), &bl, sg.skill_id, sg.skill_lv as i32, true);
                    crate::battle::atcommand::clif_skill_nodamage(c, None, &ss, AL_HEAL, heal, true);
                    crate::status::hp::status_heal(c, ss.id, heal as i64, 0, 0, 0);
                }
            }
            CR_GRANDCROSS | NPC_GRANDDARKNESS => {
                if !conf::gx_allhit() {
                    data::upd_unit(c, unit_id, |u| u.val1 -= 1);
                }
                attack(skill_get_type(sg.skill_id), &ss, &ubl, &bl, sg.skill_id, sg.skill_lv, tick, 0);
            }
            HN_METEOR_STORM_BUSTER | SOA_TALISMAN_OF_BLACK_TORTOISE => {
                attack(skill_get_type(sg.skill_id), &ss, &ss, &bl, sg.skill_id, sg.skill_lv, tick, 0);
            }
            _ => {
                attack(skill_get_type(sg.skill_id), &ss, &ubl, &bl, sg.skill_id, sg.skill_lv, tick, 0);
            }
        },
        UNT_FIREWALL | UNT_KAEN => {
            if skill_id == GN_WALLOFTHORN && battle_check_target(c, &ss, &bl, sg.target_flag) <= 0 {
                // break
            } else {
                let mut count: i64 = 0;
                let (x, y) = (bl.x, bl.y);
                //Take into account these hit more times than the timer interval can handle.
                // do skill_attack(…) while(sg->interval > 0 && --unit->val2 && x == bl->x && y == bl->y &&
                //     ++count < SKILLUNITTIMER_INTERVAL/sg->interval && !status_isdead(*bl)): short-circuit order kept
                loop {
                    attack(BF_MAGIC, &ss, &ubl, &bl, sg.skill_id, sg.skill_lv, tick + count * sg.interval as i64, 0);
                    let interval = data::skill_id2group(c, sg.group_id).map(|g| g.interval).unwrap_or(sg.interval);
                    if interval <= 0 {
                        break;
                    }
                    let mut v2 = 0;
                    data::upd_unit(c, unit_id, |u| {
                        u.val2 -= 1;
                        v2 = u.val2;
                    });
                    if v2 == 0 {
                        break;
                    }
                    let b = unit::get(c, bl.id);
                    if !b.as_ref().map(|b| b.x == x && b.y == y).unwrap_or(false) {
                        break;
                    }
                    count += 1;
                    if count >= SKILLUNITTIMER_INTERVAL / interval as i64 {
                        break;
                    }
                    if b.as_ref().map(|b| sc::status_isdead(c, b)).unwrap_or(true) {
                        break;
                    }
                }
                if data::get_unit(c, unit_id).map(|u| u.val2 <= 0).unwrap_or(false) {
                    group::skill_delunit(c, unit_id);
                }
            }
        }
        UNT_SANCTUARY => {
            if sc::battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) || tstatus.race as i32 == RC_DEMON {
                //Only damage enemies with offensive Sanctuary. [Skotlex]
                if battle_check_target(c, &ubl, &bl, BCT_ENEMY) > 0 && attack(BF_MAGIC, &ss, &ubl, &bl, sg.skill_id, sg.skill_lv, tick, 0) != 0 {
                    data::upd_group(c, sg.group_id, |g| g.val1 -= 1); // Reduce the number of targets that can still be hit
                }
            } else {
                let mut heal = iface::skill_calc_heal(c, &ss, &bl, sg.skill_id, sg.skill_lv, true);
                let is_md = bl.kind == BL_MOB;
                // RENEWAL: the Emperium is not healed
                if is_md && crate::mob::get(c, bl.id).map(|m| m.mob_id as u32 == crate::db::mobs::MOBID_EMPERIUM).unwrap_or(false) {
                } else if is_md && class_of(c, &bl) == crate::status::defs::CLASS_BATTLEFIELD {
                } else if tstatus.hp >= tstatus.max_hp {
                } else {
                    if sc::status_isimmune(c, &bl) != 0 {
                        heal = 0;
                    }
                    crate::battle::atcommand::clif_skill_nodamage(c, Some(&ubl), &bl, AL_HEAL, heal, true);
                    if tsc.as_ref().map(|s| s.has(SC_AKAITSUKI)).unwrap_or(false) && heal != 0 {
                        heal = !heal + 1;
                    }
                    crate::status::hp::status_heal(c, bl.id, heal as i64, 0, 0, 0);
                }
            }
        }
        UNT_EVILLAND => {
            //Will heal demon and undead element monsters, but not players.
            if bl.kind == BL_PC || (!sc::battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) && tstatus.race as i32 != RC_DEMON) {
                //Damage enemies
                if battle_check_target(c, &ubl, &bl, BCT_ENEMY) > 0 {
                    attack(BF_MISC, &ss, &ubl, &bl, sg.skill_id, sg.skill_lv, tick, 0);
                }
            } else {
                let mut heal = iface::skill_calc_heal(c, &ss, &bl, sg.skill_id, sg.skill_lv, true);
                if tstatus.hp < tstatus.max_hp {
                    if sc::status_isimmune(c, &bl) != 0 {
                        heal = 0;
                    }
                    crate::battle::atcommand::clif_skill_nodamage(c, Some(&ubl), &bl, AL_HEAL, heal, true);
                    crate::status::hp::status_heal(c, bl.id, heal as i64, 0, 0, 0);
                }
            }
        }
        UNT_MAGNUS => {
            // (pre-renewal: only undead and demons)
            attack(BF_MAGIC, &ss, &ubl, &bl, sg.skill_id, sg.skill_lv, tick, 0);
        }
        UNT_FIREPILLAR_WAITING => {
            super::setting::skill_unitsetting(c, &ss, sg.skill_id, sg.skill_lv, ubl.x, ubl.y, 1);
            group::skill_delunit(c, unit_id);
        }
        UNT_SKIDTRAP => {
            //Knockback away from position of user during placement [Playtester]
            let dir = crate::map::calc_dir_xy(bl.x, bl.y, (sg.val1 >> 16) as i16, (sg.val1 & 0xFFFF) as i16, crate::path::DIR_EAST);
            iface::skill_blown(c, &ubl, &bl, skill_get_blewcount(sg.skill_id, sg.skill_lv), dir as i8, BLOWN_NONE);
            data::upd_group(c, sg.group_id, |g| g.unit_id = UNT_USED_TRAPS);
            super::clif::clif_changetraplook(c, &ubl, UNT_USED_TRAPS);
            data::upd_group(c, sg.group_id, |g| g.limit = tick - g.tick + 1500);
            // In renewal, target will be stopped for 3 seconds
            if let Some(b) = unit::get(c, bl.id) {
                sc::sc_start(c, Some(&ss), &b, SC_STOP, 100, 0, skill_get_time2(sg.skill_id, sg.skill_lv) as i64);
            }
        }
        UNT_ANKLESNARE | UNT_MANHOLE => {
            if sg.val2 == 0 && tsc.is_some() && ((sg.unit_id == UNT_ANKLESNARE && skill_id != sk::SC_ESCAPE) || bl.id != sg.src_id) {
                let mut sec = skill_get_time2(sg.skill_id, sg.skill_lv) as i64;
                if sg.unit_id == UNT_ANKLESNARE {
                    let mintime = 30 * (crate::status::status_get_lv(c, &ss) as i64 + 100);
                    // (pre-renewal: bosses divide by 5; bosses cannot activate Ankle Snare in renewal)
                    sec = ((sec * crate::battle::bl::status(c, &bl).agi as i64) / -200 + sec).max(mintime);
                }
                if sc::status_change_start(c, Some(&ss), &bl, t, 10000, sg.skill_lv as i32, sg.group_id, 0, 0, sec, SCSTART_NORATEDEF, 0) {
                    if let Some(e) = sc::get_sce(c, bl.id, t) {
                        if let Some((due, _)) = crate::timer::get(c, e.timer) {
                            sec = due - tick;
                        }
                    }
                    let b = unit::get(c, bl.id).unwrap_or(bl.clone());
                    if (sg.unit_id == UNT_MANHOLE && bl.kind == BL_PC) || crate::battle::knockback::unit_blown_immune(c, &b, 0x1) == 0 {
                        unit::movepos(c, bl.id, ubl.x, ubl.y, 0, false);
                        if let Some(b) = unit::get(c, bl.id) {
                            crate::clif::fixpos(c, &b);
                        }
                    }
                    data::upd_group(c, sg.group_id, |g| g.val2 = bl.id as i32);
                } else {
                    sec = 3000; //Couldn't trap it?
                }
                if sg.unit_id == UNT_ANKLESNARE {
                    super::clif::clif_skillunit_update(c, &ubl);
                    /*
                     * If you're snared from a trap that was invisible this makes the trap be
                     * visible again -- being you stepped on it (w/o this the trap remains invisible and you go "WTF WHY I CANT MOVE")
                     * bugreport:3961
                     */
                    super::clif::clif_changetraplook(c, &ubl, UNT_ANKLESNARE);
                }
                data::upd_group(c, sg.group_id, |g| {
                    g.limit = tick - g.tick + sec;
                    g.interval = -1;
                });
                data::upd_unit(c, unit_id, |u| u.range = 0);
            }
        }
        UNT_EARTHQUAKE => {
            let mut val1 = 0;
            data::upd_group(c, sg.group_id, |g| {
                g.val1 += 1; // Hit count
                val1 = g.val1;
            });
            let n = skill_area_sub_count_enemies(c, &ubl, skill_get_splash(sg.skill_id, sg.skill_lv) as i16, &ubl);
            attack(skill_get_type(sg.skill_id), &ss, &ubl, &bl, sg.skill_id, sg.skill_lv, tick, n | if val1 == 1 { NPC_EARTHQUAKE_FLAG } else { 0 });
        }
        UNT_ELECTRICSHOCKER => {
            if bl.id != ss.id {
                if sc::status_change_start(c, Some(&ss), &bl, t, 10000, sg.skill_lv as i32, sg.group_id, 0, 0, skill_get_time2(sg.skill_id, sg.skill_lv) as i64, SCSTART_NORATEDEF, 0) {
                    super::movement::map_moveblock(c, bl.id, ubl.x, ubl.y, tick);
                    if let Some(b) = unit::get(c, bl.id) {
                        crate::clif::fixpos(c, &b);
                    }
                }
                let ids = util::ids_in_allrange(c, &ubl, skill_get_splash(sg.skill_id, sg.skill_lv) as i16, sg.bl_flag as u16);
                util::each(c, &ids, |t| skill_trap_splash(c, t, unit_id, tick));
                data::upd_group(c, sg.group_id, |g| g.unit_id = UNT_USED_TRAPS); //Changed ID so it does not invoke a for each in area again.
            }
        }
        UNT_VENOMDUST => {
            if tsc.is_some() && sc::get_sce(c, bl.id, t).is_none() {
                sc::status_change_start(c, Some(&ss), &bl, t, 10000, sg.skill_lv as i32, sg.src_id as i32, 0, 0, skill_get_time2(sg.skill_id, sg.skill_lv) as i64, SCSTART_NONE, 0);
            }
        }
        UNT_MAGENTATRAP | UNT_COBALTTRAP | UNT_MAIZETRAP | UNT_VERDURETRAP | UNT_FIRINGTRAP | UNT_ICEBOUNDTRAP | UNT_CLUSTERBOMB
        | UNT_FLASHER | UNT_LANDMINE | UNT_BLASTMINE | UNT_SHOCKWAVE | UNT_SANDMAN | UNT_FREEZINGTRAP | UNT_FIREPILLAR_ACTIVE
        | UNT_CLAYMORETRAP => {
            let magenta_group = matches!(sg.unit_id, UNT_MAGENTATRAP | UNT_COBALTTRAP | UNT_MAIZETRAP | UNT_VERDURETRAP);
            let caster_group = magenta_group || matches!(sg.unit_id, UNT_FIRINGTRAP | UNT_ICEBOUNDTRAP | UNT_CLUSTERBOMB);
            let skip = (magenta_group && bl.kind == BL_PC) // it won't work on players
                || (caster_group && bl.id == ss.id) // it won't trigger on caster
                // Flasher doesn't activate on plant bosses
                || (sg.unit_id == UNT_FLASHER && bl.kind == BL_MOB && tstatus.class_ as i32 == crate::status::defs::CLASS_BOSS && tstatus.race as i32 == RC_PLANT);
            if !skip && !tsc.as_ref().map(|s| s.has(SC__MANHOLE)).unwrap_or(false) {
                let mut bl_flag = sg.bl_flag;
                let mut center = ubl.clone();
                match sg.unit_id {
                    UNT_CLAYMORETRAP | UNT_FIRINGTRAP | UNT_ICEBOUNDTRAP => {
                        // These can hit other traps
                        bl_flag = bl_flag | BL_SKILL as i32 | !BCT_SELF;
                    }
                    UNT_SANDMAN | UNT_FREEZINGTRAP => {
                        // These have their effect centered around the unit that triggered the trap
                        center = bl.clone();
                    }
                    _ => {}
                }
                let splash_range = skill_get_splash(sg.skill_id, sg.skill_lv);
                if splash_range == 0 {
                    // If no splash range, it only hits the unit that activated the trap
                    if skill_get_nk(skill_id, &[NK_NODAMAGE]) {
                        iface::skill_additional_effect(c, &ss, &bl, sg.skill_id, sg.skill_lv, BF_MISC, ATK_DEF, tick);
                    } else {
                        attack(skill_get_type(sg.skill_id), &ss, &ubl, &bl, sg.skill_id, sg.skill_lv, tick, 0);
                    }
                } else {
                    let ids = util::ids_in_range_wc(c, &center, splash_range as i16, bl_flag as u16);
                    util::each(c, &ids, |t| skill_trap_splash(c, t, unit_id, tick));
                }
                let range = data::get_unit(c, unit_id).map(|u| u.range).unwrap_or(0);
                if sg.unit_id != UNT_FIREPILLAR_ACTIVE && (conf::multi_trigger_trap() == 0 || range >= 0) {
                    super::clif::clif_changetraplook(c, &ubl, UNT_USED_TRAPS);
                }
                data::upd_group(c, sg.group_id, |g| g.limit = tick - g.tick + if sg.unit_id == UNT_FIRINGTRAP { 0 } else { 1500 });
                if conf::multi_trigger_trap() == 1 {
                    data::upd_unit(c, unit_id, |u| u.range = -1); // Trap will still process all units on it and will then be disabled in the calling function
                } else {
                    data::upd_group(c, sg.group_id, |g| g.unit_id = UNT_USED_TRAPS); // Change ID so it does not invoke for each in area again
                }
            }
        }
        UNT_TALKIEBOX => {
            if sg.src_id != bl.id && sg.val2 == 0 {
                super::clif::clif_talkiebox(c, &ubl, sg.valstr.as_deref().unwrap_or(""));
                data::upd_group(c, sg.group_id, |g| g.unit_id = UNT_USED_TRAPS);
                super::clif::clif_changetraplook(c, &ubl, UNT_USED_TRAPS);
                data::upd_group(c, sg.group_id, |g| {
                    g.limit = tick - g.tick + 5000;
                    g.val2 = -1;
                });
            }
        }
        UNT_LULLABY => {
            if ss.id != bl.id {
                iface::skill_additional_effect(c, &ss, &bl, sg.skill_id, sg.skill_lv, BF_LONG | BF_SKILL | BF_MISC, ATK_DEF, tick);
            }
        }
        UNT_UGLYDANCE => {
            //Ugly Dance [Skotlex]
            if ss.id != bl.id {
                iface::skill_additional_effect(c, &ss, &bl, sg.skill_id, sg.skill_lv, BF_LONG | BF_SKILL | BF_MISC, ATK_DEF, tick);
            }
        }
        UNT_DISSONANCE => {
            attack(BF_MISC, &ss, &ubl, &bl, sg.skill_id, sg.skill_lv, tick, 0);
        }
        UNT_APPLEIDUN => {
            //Apple of Idun [Skotlex]
            let emp = bl.kind == BL_MOB && crate::mob::get(c, bl.id).map(|m| m.mob_id as u32 == crate::db::mobs::MOBID_EMPERIUM).unwrap_or(false);
            let spirit_bd = tsc.as_ref().and_then(|s| s.get(SC_SPIRIT)).map(|e| e.val2 == SL_BARDDANCER as i32).unwrap_or(false);
            if emp {
                // RENEWAL: the Emperium is not healed
            } else if sg.src_id == bl.id && !spirit_bd {
                // affects self only when soullinked
            } else {
                let mut heal = iface::skill_calc_heal(c, &ss, &bl, sg.skill_id, sg.skill_lv, true);
                if tsc.as_ref().map(|s| s.has(SC_AKAITSUKI)).unwrap_or(false) && heal != 0 {
                    heal = !heal + 1;
                }
                crate::battle::atcommand::clif_skill_nodamage(c, Some(&ubl), &bl, AL_HEAL, heal, true);
                crate::status::hp::status_heal(c, bl.id, heal as i64, 0, 0, 0);
            }
        }
        UNT_TATAMIGAESHI | UNT_DEMONSTRATION => {
            attack(BF_WEAPON, &ss, &ubl, &bl, sg.skill_id, sg.skill_lv, tick, 0);
        }
        UNT_GOSPEL => {
            if (c.rnd() % 100) as i32 >= 50 + sg.skill_lv as i32 * 5 || ss.id == bl.id {
                // break
            } else if battle_check_target(c, &ss, &bl, BCT_PARTY) > 0 {
                // Support Effect only on party, not guild
                let i = c.rnd() % 13; // Positive buff count
                let time = skill_get_time2(sg.skill_id, sg.skill_lv) as i64; //Duration
                let sc_start = |t: i32, v1: i32, d: i64| sc::sc_start(c, Some(&ss), &bl, t, 100, v1, d);
                let info = |kind: i32| {
                    if tsd {
                        super::clif::clif_gospel_info(c, &bl, kind);
                    }
                };
                match i {
                    0 => {
                        // Heal 1000~9999 HP
                        let heal = (c.rnd() % 9000 + 1000) as i32;
                        crate::battle::atcommand::clif_skill_nodamage(c, Some(&ss), &bl, AL_HEAL, heal, true);
                        crate::status::hp::status_heal(c, bl.id, heal as i64, 0, 0, 0);
                    }
                    1 => {
                        // End all negative status
                        iface::status_change_clear_buffs(c, &bl, iface::SCCB_DEBUFFS | iface::SCCB_REFRESH);
                        info(0x15);
                    }
                    2 => {
                        // Immunity to all status
                        sc_start(SC_SCRESIST, 100, time);
                        info(0x16);
                    }
                    3 => {
                        // MaxHP +100%
                        sc_start(SC_INCMHPRATE, 100, time);
                        info(0x17);
                    }
                    4 => {
                        // MaxSP +100%
                        sc_start(SC_INCMSPRATE, 100, time);
                        info(0x18);
                    }
                    5 => {
                        // All stats +20
                        sc_start(SC_INCALLSTATUS, 20, time);
                        info(0x19);
                    }
                    6 => {
                        // Level 10 Blessing
                        sc_start(SC_BLESSING, 10, skill_get_time(AL_BLESSING, 10) as i64);
                    }
                    7 => {
                        // Level 10 Increase AGI
                        sc_start(SC_INCREASEAGI, 10, skill_get_time(AL_INCAGI, 10) as i64);
                    }
                    8 => {
                        // Enchant weapon with Holy element
                        sc_start(SC_ASPERSIO, 1, time);
                        info(0x1c);
                    }
                    9 => {
                        // Enchant armor with Holy element
                        sc_start(SC_BENEDICTIO, 1, time);
                        info(0x1d);
                    }
                    10 => {
                        // DEF +25%
                        sc_start(SC_INCDEFRATE, 25, 10000); //10 seconds
                        info(0x1e);
                    }
                    11 => {
                        // ATK +100%
                        sc_start(SC_INCATKRATE, 100, time);
                        info(0x1f);
                    }
                    12 => {
                        // HIT/Flee +50
                        sc_start(SC_INCHIT, 50, time);
                        sc_start(SC_INCFLEE, 50, time);
                        info(0x20);
                    }
                    _ => {}
                }
            } else if battle_check_target(c, &ubl, &bl, BCT_ENEMY) > 0 {
                // Offensive Effect
                let i = c.rnd() % 10; // Negative buff count
                let sc_start = |t: i32, v1: i32, d: i64| sc::sc_start(c, Some(&ss), &bl, t, 100, v1, d);
                match i {
                    0 | 1 => {
                        // Deal 3000~7999 damage reduced by DEF / Deal 1500~5499 damage unreducable
                        attack(BF_MISC, &ss, &ubl, &bl, sg.skill_id, sg.skill_lv, tick, i as i32);
                    }
                    2 => {
                        sc_start(SC_CURSE, 1, 1800000); //30 minutes
                    }
                    3 => {
                        sc_start(SC_BLIND, 1, 1800000); //30 minutes
                    }
                    4 => {
                        sc::sc_start2(c, Some(&ss), &bl, SC_POISON, 100, 1, ss.id as i32, 1800000); //30 minutes
                    }
                    5 => {
                        // Level 10 Provoke
                        let ok = sc_start(SC_PROVOKE, 10, sc::INFINITE_TICK); //Infinite
                        crate::battle::atcommand::clif_skill_nodamage(c, None, &bl, SM_PROVOKE, 10, ok);
                    }
                    6 => {
                        sc_start(SC_INCDEFRATE, -100, 20000); //20 seconds
                    }
                    7 => {
                        sc_start(SC_INCATKRATE, -100, 20000); //20 seconds
                    }
                    8 => {
                        sc_start(SC_INCFLEERATE, -100, 20000); //20 seconds
                    }
                    9 => {
                        // Speed/ASPD -25%
                        sc::sc_start4(c, Some(&ss), &bl, SC_GOSPEL, 100, 1, 0, 0, BCT_ENEMY, 20000); //20 seconds
                    }
                    _ => {}
                }
            }
        }
        // (UNT_BASILICA: pre-renewal only)
        UNT_GROUNDDRIFT_WIND | UNT_GROUNDDRIFT_DARK | UNT_GROUNDDRIFT_POISON | UNT_GROUNDDRIFT_WATER | UNT_GROUNDDRIFT_FIRE => {
            let ids = util::ids_in_range_wc(c, &ubl, skill_get_splash(sg.skill_id, sg.skill_lv) as i16, sg.bl_flag as u16);
            util::each(c, &ids, |t| skill_trap_splash(c, t, unit_id, tick));
            data::upd_group(c, sg.group_id, |g| {
                g.unit_id = UNT_USED_TRAPS;
                //clif_changetraplook(src, UNT_FIREPILLAR_ACTIVE);
                g.limit = tick - g.tick;
            });
        }
        UNT_POISONSMOKE => {
            if battle_check_target(c, &ss, &bl, BCT_ENEMY) > 0 && !tsc.as_ref().map(|s| s.has(sg.val2)).unwrap_or(false) && c.rnd() % 100 < 50 {
                sc::sc_start4(c, Some(&ss), &bl, sg.val2, 100, sg.val3, 0, 1, 0, skill_get_time2(GC_POISONINGWEAPON, 1) as i64);
            }
        }
        UNT_EPICLESIS => {
            let mut val1 = 0;
            data::upd_group(c, sg.group_id, |g| {
                g.val1 += 1; // Increment outside of the check to get the exact interval of the skill unit
                val1 = g.val1;
            });
            if bl.kind == BL_PC && !sc::battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) && tstatus.race as i32 != RC_DEMON {
                if val1 % 3 == 0 {
                    // Recover players every 3 seconds
                    let (hp, sp) = match sg.skill_lv {
                        1 | 2 => (3, 2),
                        3 | 4 => (4, 3),
                        _ => (5, 4),
                    };
                    let mut hp = (tstatus.max_hp as i64 * hp / 100) as i32;
                    let sp = (tstatus.max_sp as i64 * sp / 100) as i32;
                    if tstatus.hp < tstatus.max_hp {
                        crate::battle::atcommand::clif_skill_nodamage(c, Some(&ubl), &bl, AL_HEAL, hp, true);
                    }
                    if tstatus.sp < tstatus.max_sp {
                        crate::battle::atcommand::clif_skill_nodamage(c, Some(&ubl), &bl, MG_SRECOVERY, sp, true);
                    }
                    if tsc.as_ref().map(|s| s.has(SC_AKAITSUKI)).unwrap_or(false) && hp != 0 {
                        hp = !hp + 1;
                    }
                    crate::status::hp::status_heal(c, bl.id, hp as i64, sp as i64, 0, 3);
                }
                if val1 % 5 == 0 {
                    // Reveal hidden players every 5 seconds
                    // Doesn't remove Invisibility or Chase Walk.
                    for s in [SC_HIDING, SC_CLOAKING, SC_CLOAKINGEXCEED, SC_CAMOUFLAGE, SC_NEWMOON] {
                        sc::status_change_end(c, bl.id, s, 0);
                    }
                    if let Some(sf) = sc::get_sce(c, bl.id, SC__SHADOWFORM) {
                        if ((c.rnd() % 100) as i32) < 100 - sf.val1 * 10 {
                            // [100 - (Skill Level x 10)] %
                            sc::status_change_end(c, bl.id, SC__SHADOWFORM, 0);
                        }
                    }
                }
                if let Some(b) = unit::get(c, bl.id) {
                    sc::sc_start(c, Some(&ss), &b, t, 100, sg.skill_lv as i32, sg.interval as i64 + 100);
                }
            }
        }
        UNT_DIMENSIONDOOR => {
            if tsd && !crate::npc::mapflag(c, bl.map, crate::npc::MF_NOTELEPORT) {
                crate::script::buildins::pcinfo::pc_randomwarp(c, bl.id, crate::unit::CLR_TELEPORT, false);
            } else if bl.kind == BL_MOB && conf::mob_warp() & 8 != 0 {
                crate::mob::unit::unit_warp(c, bl.id, -1, -1, -1, crate::unit::CLR_TELEPORT);
            }
        }
        UNT_REVERBERATION => {
            super::clif::clif_changetraplook(c, &ubl, UNT_USED_TRAPS);
            let ids = util::ids_in_range_wc(c, &ubl, skill_get_splash(sg.skill_id, sg.skill_lv) as i16, sg.bl_flag as u16);
            util::each(c, &ids, |t| skill_trap_splash(c, t, unit_id, tick));
            data::upd_group(c, sg.group_id, |g| {
                g.limit = tick - g.tick + 1000;
                g.unit_id = UNT_USED_TRAPS;
            });
        }
        UNT_SEVERE_RAINSTORM => {
            if battle_check_target(c, &ubl, &bl, BCT_ENEMY) > 0 {
                attack(BF_WEAPON, &ss, &ubl, &bl, WM_SEVERE_RAINSTORM_MELEE, sg.skill_lv, tick, 0);
            }
        }
        UNT_NETHERWORLD => {
            if !crate::battle::bl::has_mode(c, &bl, crate::status::defs::MD_STATUSIMMUNE)
                || (!crate::npc::mapflag_gvg2(c, ss.map) && battle_check_target(c, &ubl, &bl, BCT_PARTY) < 0)
            {
                if !tsc.as_ref().map(|s| s.has(t)).unwrap_or(false) {
                    sc::sc_start(c, Some(&ss), &bl, t, 100, sg.skill_lv as i32, skill_get_time2(sg.skill_id, sg.skill_lv) as i64);
                    data::upd_group(c, sg.group_id, |g| {
                        g.limit = tick - g.tick;
                        g.unit_id = UNT_USED_TRAPS;
                    });
                }
            }
        }
        UNT_THORNS_TRAP => {
            if tsc.is_some() {
                if sg.val2 == 0 {
                    let mut sec = skill_get_time2(sg.skill_id, sg.skill_lv) as i64;
                    if sc::sc_start(c, Some(&ss), &bl, t, 100, sg.skill_lv as i32, sec) {
                        if let Some(e) = sc::get_sce(c, bl.id, t) {
                            if let Some((due, _)) = crate::timer::get(c, e.timer) {
                                sec = due - tick;
                            }
                        }
                        // map_moveblock(bl, src->x, src->y, tick); // in official server it doesn't behave like this. [malufett]
                        if let Some(b) = unit::get(c, bl.id) {
                            crate::clif::fixpos(c, &b);
                        }
                        data::upd_group(c, sg.group_id, |g| g.val2 = bl.id as i32);
                    } else {
                        sec = 3000; // Couldn't trap it?
                    }
                    data::upd_group(c, sg.group_id, |g| g.limit = tick - g.tick + sec);
                } else if sc::has_sce(c, bl.id, SC_THORNSTRAP) && bl.id as i32 == sg.val2 {
                    attack(skill_get_type(GN_THORNS_TRAP), &ss, &ss, &bl, sg.skill_id, sg.skill_lv, tick, SD_LEVEL | SD_ANIMATION);
                }
            }
        }
        UNT_WALLOFTHORN => {
            let mut before = 0;
            data::upd_unit(c, unit_id, |u| {
                before = u.val2;
                u.val2 -= 1;
            });
            if before <= 0 {
                // Max hit reached
            } else if crate::battle::bl::has_mode(c, &bl, crate::status::defs::MD_STATUSIMMUNE) {
                // This skill doesn't affect to Boss monsters. [iRO Wiki]
            } else {
                iface::skill_blown(c, &ubl, &bl, skill_get_blewcount(sg.skill_id, sg.skill_lv), crate::unit::getdir(&bl) as i8, BLOWN_IGNORE_NO_KNOCKBACK);
                iface::skill_addtimerskill(c, &ss, tick + 100, bl.id, ubl.x, ubl.y, sg.skill_id, sg.skill_lv, skill_get_type(sg.skill_id), 4 | SD_LEVEL);
            }
        }
        UNT_DEMONIC_FIRE => {
            // (case 1: and default: are the same)
            sc::sc_start4(c, Some(&ss), &bl, SC_BURNING, 4 + 4 * sg.skill_lv as i32, sg.skill_lv as i32, 1000, ss.id as i32, 0, skill_get_time2(sg.skill_id, sg.skill_lv) as i64);
            attack(skill_get_type(skill_id), &ss, &ubl, &bl, sg.skill_id, (sg.skill_lv as i32 + 10 * sg.val2) as u16, tick, 0);
        }
        UNT_ZEPHYR => {
            if ss.id != bl.id {
                // Doesn't affect the Elemental
                sc::sc_start(c, Some(&ss), &bl, t, 100, sg.skill_lv as i32, sg.interval as i64);
            }
        }
        UNT_CLOUD_KILL => {
            attack(skill_get_type(sg.skill_id), &ss, &ss, &bl, sg.skill_id, sg.skill_lv, tick, SD_ANIMATION | SD_LEVEL);
        }
        UNT_VACUUM_EXTREME => {
            if let Some(s) = &tsc {
                let post = s.get(SC_VACUUM_EXTREME_POSTDELAY).map(|e| e.val2 == sg.group_id).unwrap_or(false);
                if s.has(SC_HALLUCINATIONWALK) || s.has(SC_NPC_HALLUCINATIONWALK) || s.has(SC_HOVERING) || s.has(SC_VACUUM_EXTREME) || post {
                    // Ignore post delay from other vacuum (this will make stack effect enabled)
                    return 0;
                }
            }
            // Apply effect and suck targets one-by-one each n seconds
            let mut val3 = 0;
            data::upd_group(c, sg.group_id, |g| {
                g.val3 += 1;
                val3 = g.val3;
            });
            let g = data::skill_id2group(c, sg.group_id).unwrap_or(sg.clone());
            sc::sc_start4(c, Some(&ss), &bl, t, 100, g.skill_lv as i32, g.group_id, (g.val1 << 16) | g.val2, val3 * 500, g.limit - (tick - g.tick));
        }
        UNT_BANDING => {
            if battle_check_target(c, &ubl, &bl, BCT_ENEMY) > 0 && !tsc.as_ref().map(|s| s.has(SC_BANDING_DEFENCE)).unwrap_or(false) {
                let rate = (crate::status::status_get_lv(c, &ubl) / 5) + (sg.skill_lv as i32 * 5) - (crate::battle::bl::status(c, &bl).agi as i32 / 10);
                sc::sc_start(c, Some(&ss), &bl, SC_BANDING_DEFENCE, rate, 90, skill_get_time2(sg.skill_id, sg.skill_lv) as i64);
            }
        }
        UNT_FIRE_MANTLE => {
            if battle_check_target(c, &ubl, &bl, BCT_ENEMY) > 0 {
                attack(BF_MAGIC, &ss, &ubl, &bl, sg.skill_id, sg.skill_lv, tick, 0);
            }
        }
        UNT_ZENKAI_WATER | UNT_ZENKAI_LAND | UNT_ZENKAI_FIRE | UNT_ZENKAI_WIND => {
            let t2 = skill_get_time2(sg.skill_id, sg.skill_lv) as i64;
            if battle_check_target(c, &ubl, &bl, BCT_ENEMY) > 0 {
                let rate = sg.val1 * 5;
                match sg.unit_id {
                    UNT_ZENKAI_WATER => match c.rnd() % 2 + 1 {
                        1 => {
                            sc::sc_start(c, Some(&ss), &bl, SC_FREEZE, rate, sg.skill_lv as i32, t2);
                        }
                        _ => {
                            sc::sc_start(c, Some(&ss), &bl, SC_FREEZING, rate, sg.skill_lv as i32, t2);
                        }
                    },
                    UNT_ZENKAI_LAND => match c.rnd() % 2 + 1 {
                        1 => {
                            // sc_start2(ss, bl, SC_STONEWAIT, rate, lv, ss->id, time2, time): the delay is the 8th argument
                            sc::status_change_start(c, Some(&ss), &bl, SC_STONEWAIT, rate * 100, sg.skill_lv as i32, ss.id as i32, 0, 0, t2, SCSTART_NONE, skill_get_time(sg.skill_id, sg.skill_lv));
                        }
                        _ => {
                            sc::sc_start2(c, Some(&ss), &bl, SC_POISON, rate, sg.skill_lv as i32, ss.id as i32, t2);
                        }
                    },
                    UNT_ZENKAI_FIRE => {
                        sc::sc_start4(c, Some(&ss), &bl, SC_BURNING, rate, sg.skill_lv as i32, 1000, ss.id as i32, 0, t2);
                    }
                    _ => match c.rnd() % 3 + 1 {
                        1 => {
                            sc::sc_start(c, Some(&ss), &bl, SC_SLEEP, rate, sg.skill_lv as i32, t2);
                        }
                        2 => {
                            sc::sc_start(c, Some(&ss), &bl, SC_SILENCE, rate, sg.skill_lv as i32, t2);
                        }
                        _ => {
                            sc::sc_start(c, Some(&ss), &bl, SC_DEEPSLEEP, rate, sg.skill_lv as i32, t2);
                        }
                    },
                }
            } else {
                sc::sc_start2(c, Some(&ss), &bl, t, 100, sg.val1, sg.val2, t2);
            }
        }
        UNT_POISON_MIST => {
            attack(BF_MAGIC, &ss, &ubl, &bl, sg.skill_id, sg.skill_lv, tick, 0);
            if let Some(b) = unit::get(c, bl.id) {
                sc::status_change_start(c, Some(&ss), &b, SC_POISON_MIST, (10 + 10 * sg.skill_lv as i32) * 100, sg.skill_lv as i32, sg.skill_id as i32, 0, 0, skill_get_time2(sg.skill_id, sg.skill_lv) as i64, SCSTART_NOTICKDEF | SCSTART_NORATEDEF, 0);
            }
        }
        UNT_CHAOSPANIC => {
            if !tsc.as_ref().map(|s| s.has(t)).unwrap_or(false) {
                sc::status_change_start(c, Some(&ss), &bl, t, 3500 + (sg.skill_lv as i32 * 1500), sg.skill_lv as i32, 0, 0, 1, skill_get_time2(sg.skill_id, sg.skill_lv) as i64, SCSTART_NOAVOID | SCSTART_NORATEDEF | SCSTART_NOTICKDEF, 0);
            }
        }
        UNT_B_TRAP => {
            if !tsc.as_ref().map(|s| s.has(t)).unwrap_or(false) {
                sc::sc_start(c, Some(&ss), &bl, t, 100, sg.skill_lv as i32, skill_get_time2(sg.skill_id, sg.skill_lv) as i64);
                data::upd_unit(c, unit_id, |u| u.val2 += 1); // Mark as ever been used
            }
        }
        UNT_FIRE_RAIN => {
            let dmg = attack(skill_get_type(sg.skill_id), &ss, &ubl, &bl, sg.skill_id, sg.skill_lv, tick, SD_ANIMATION | SD_SPLASH);
            let amotion = crate::battle::bl::status(c, &ss).amotion as i32;
            iface::clif_skill_damage(c, &ss, &bl, tick, amotion, 0, dmg, 1, sg.skill_id, sg.skill_lv, DMG_SINGLE);
        }
        UNT_MAGMA_ERUPTION => {
            attack(skill_get_type(NC_MAGMA_ERUPTION_DOTDAMAGE), &ss, &ubl, &bl, NC_MAGMA_ERUPTION_DOTDAMAGE, sg.skill_lv, tick, 0);
            if sg.skill_id == NC_MAGMA_ERUPTION {
                attack(skill_get_type(NC_MAGMA_ERUPTION_DOTDAMAGE), &ss, &ubl, &bl, NC_MAGMA_ERUPTION_DOTDAMAGE, sg.skill_lv, tick, 0);
            } else {
                attack(skill_get_type(NPC_MAGMA_ERUPTION_DOTDAMAGE), &ss, &ubl, &bl, NPC_MAGMA_ERUPTION_DOTDAMAGE, sg.skill_lv, tick, 0);
            }
        }
        UNT_ACIDIFIED_ZONE_WATER => {
            attack(skill_get_type(BO_ACIDIFIED_ZONE_WATER_ATK), &ss, &ubl, &bl, BO_ACIDIFIED_ZONE_WATER_ATK, sg.skill_lv, tick, 0);
        }
        UNT_ACIDIFIED_ZONE_GROUND => {
            attack(skill_get_type(BO_ACIDIFIED_ZONE_GROUND_ATK), &ss, &ubl, &bl, BO_ACIDIFIED_ZONE_GROUND_ATK, sg.skill_lv, tick, 0);
        }
        UNT_ACIDIFIED_ZONE_WIND => {
            attack(skill_get_type(BO_ACIDIFIED_ZONE_WIND_ATK), &ss, &ubl, &bl, BO_ACIDIFIED_ZONE_WIND_ATK, sg.skill_lv, tick, 0);
        }
        UNT_ACIDIFIED_ZONE_FIRE => {
            attack(skill_get_type(BO_ACIDIFIED_ZONE_FIRE_ATK), &ss, &ubl, &bl, BO_ACIDIFIED_ZONE_FIRE_ATK, sg.skill_lv, tick, 0);
        }
        UNT_ASTRAL_STRIKE => {
            attack(skill_get_type(AG_ASTRAL_STRIKE_ATK), &ss, &ubl, &bl, AG_ASTRAL_STRIKE_ATK, sg.skill_lv, tick, 0);
        }
        UNT_ABYSS_SQUARE => {
            let mut flag = 0;
            // Check to see if the caster is in the AoE.
            if crate::path::distance((ss.x - ubl.x) as i32, (ss.y - ubl.y) as i32) <= unit.range as i32 {
                flag |= 2; // If yes, skill hits twice.
            }
            attack(skill_get_type(sg.skill_id), &ss, &ubl, &bl, sg.skill_id, sg.skill_lv, tick, flag);
        }
        UNT_TOTEM_OF_TUTELARY => {
            if bl.kind == BL_PC {
                let crt = crate::battle::bl::status(c, &ss).crt as i32;
                let mastery = if ss.kind == BL_PC { crate::skill::checkskill(c, ss.id, SOA_TALISMAN_MASTERY) as i32 } else { 0 };
                let lv = sg.skill_lv as i32;
                let mut hp = 500;
                hp += 500 * lv;
                hp += 50 * mastery * lv;
                hp += 5 * crt * lv;
                hp *= crate::status::status_get_lv(c, &ss) / 100;
                let mut sp = 0;
                sp += 50 * lv;
                sp += 5 * mastery * lv;
                sp += 5 * crt * lv;
                sp *= crate::status::status_get_lv(c, &ss) / 100;
                crate::status::hp::status_heal(c, bl.id, hp as i64, sp as i64, 0, 2);
                if let Some(b) = unit::get(c, bl.id) {
                    sc::sc_start(c, Some(&ss), &b, skill_get_sc(sg.skill_id) as i32, 100, sg.skill_lv as i32, sg.interval as i64 + 100);
                }
            }
        }
        UNT_HYUN_ROKS_BREEZE => {
            attack(skill_get_type(sg.skill_id), &ss, &ss, &bl, sg.skill_id, sg.skill_lv, tick, SD_ANIMATION);
        }
        _ => {}
    }
    if bl.kind == BL_MOB && ss.id != bl.id {
        iface::mobskill_event(c, &bl, &ss, tick, MSC_SKILLUSED | ((skill_id as i32) << 16));
    }
    skill_id as i32
}

/// skill.cpp skill_trap_splash: a trap's splash on `bl` (the trap went off, timed out or was damaged).
pub fn skill_trap_splash(c: &Ctx, bl: &Unit, src_id: u32, tick: Tick) -> i32 {
    let Some(unit) = data::get_unit(c, src_id) else { return 0 };
    if !unit.alive || !bl.on_map {
        return 0;
    }
    let Some(sg) = data::skill_id2group(c, unit.group_id) else { return 0 };
    let Some(ss) = unit::get(c, sg.src_id) else { return 0 };
    let src = unit.bl.clone();
    if battle_check_target(c, &src, bl, sg.target_flag) <= 0 {
        return 0;
    }
    let attack = |t: i32, id: u16, lv: u16, flag: i32| iface::skill_attack(c, t, &ss, &src, bl, id, lv, tick, flag);
    match sg.unit_id {
        UNT_B_TRAP => {
            if battle_check_target(c, &ss, bl, sg.target_flag & !BCT_SELF) > 0 {
                iface::skill_castend_damage_id(c, &ss, bl, sg.skill_id, sg.skill_lv, tick, SD_ANIMATION | SD_LEVEL | SD_SPLASH | 1);
            }
        }
        UNT_SANDMAN => {
            iface::skill_additional_effect(c, &ss, bl, sg.skill_id, sg.skill_lv, BF_MISC, ATK_DEF, tick);
        }
        UNT_GROUNDDRIFT_WIND => {
            if attack(skill_get_type(sg.skill_id), sg.skill_id, sg.skill_lv, sg.val1) != 0 {
                sc::sc_start(c, Some(&ss), bl, SC_STUN, 50, sg.skill_lv as i32, skill_get_time2(sg.skill_id, 1) as i64);
            }
        }
        UNT_GROUNDDRIFT_DARK => {
            if attack(skill_get_type(sg.skill_id), sg.skill_id, sg.skill_lv, sg.val1) != 0 {
                sc::sc_start(c, Some(&ss), bl, SC_BLIND, 50, sg.skill_lv as i32, skill_get_time2(sg.skill_id, 2) as i64);
            }
        }
        UNT_GROUNDDRIFT_POISON => {
            if attack(skill_get_type(sg.skill_id), sg.skill_id, sg.skill_lv, sg.val1) != 0 {
                sc::sc_start2(c, Some(&ss), bl, SC_POISON, 50, sg.skill_lv as i32, ss.id as i32, skill_get_time2(sg.skill_id, 3) as i64);
            }
        }
        UNT_GROUNDDRIFT_WATER => {
            if attack(skill_get_type(sg.skill_id), sg.skill_id, sg.skill_lv, sg.val1) != 0 {
                sc::sc_start(c, Some(&ss), bl, SC_FREEZE, 50, sg.skill_lv as i32, skill_get_time2(sg.skill_id, 4) as i64);
            }
        }
        UNT_GROUNDDRIFT_FIRE => {
            if attack(skill_get_type(sg.skill_id), sg.skill_id, sg.skill_lv, sg.val1) != 0 {
                iface::skill_blown(c, &src, bl, skill_get_blewcount(sg.skill_id, sg.skill_lv), -1, BLOWN_NONE);
            }
        }
        UNT_ELECTRICSHOCKER => {
            if bl.id != ss.id && !crate::battle::bl::has_mode(c, bl, crate::status::defs::MD_STATUSIMMUNE)
                && sc::status_change_start(c, Some(&ss), bl, SC_ELECTRICSHOCKER, 10000, sg.skill_lv as i32, sg.group_id, 0, 0, skill_get_time2(sg.skill_id, sg.skill_lv) as i64, SCSTART_NORATEDEF, 0)
            {
                super::movement::map_moveblock(c, bl.id, unit.bl.x, unit.bl.y, tick);
                if let Some(b) = unit::get(c, bl.id) {
                    crate::clif::fixpos(c, &b);
                    iface::clif_skill_damage(c, &src, &b, tick, 0, 0, DMGVAL_IGNORE, 1, sg.skill_id, sg.skill_lv, DMG_SPLASH);
                }
            }
        }
        UNT_MAGENTATRAP | UNT_COBALTTRAP | UNT_MAIZETRAP | UNT_VERDURETRAP => {
            if bl.kind == BL_MOB && class_of(c, bl) != crate::status::defs::CLASS_BOSS {
                // status->def_ele / ele_lv of the monster (mob_data::status)
                if let Some(mut md) = crate::mob::get(c, bl.id) {
                    md.status.def_ele = skill_get_ele(sg.skill_id, sg.skill_lv) as u8;
                    md.status.ele_lv = sg.skill_lv as u8;
                    crate::mob::put(c, &md);
                }
            }
        }
        UNT_REVERBERATION => {
            // For proper skill delay animation when used with Dominion Impulse
            iface::skill_addtimerskill(c, &ss, tick + 50, bl.id, 0, 0, NPC_REVERBERATION_ATK, sg.skill_lv, BF_WEAPON, 0);
        }
        _ => {
            // UNT_FIRINGTRAP / UNT_ICEBOUNDTRAP fall through to UNT_CLUSTERBOMB, UNT_CLAYMORETRAP to default
            if matches!(sg.unit_id, UNT_FIRINGTRAP | UNT_ICEBOUNDTRAP) {
                if src.id == bl.id {
                    return 1;
                }
                if bl.kind == BL_SKILL && data::group_of_unit(c, bl.id).map(|g| g.unit_id == UNT_USED_TRAPS).unwrap_or(false) {
                    return 1;
                }
                // fallthrough to UNT_CLUSTERBOMB
            }
            if matches!(sg.unit_id, UNT_FIRINGTRAP | UNT_ICEBOUNDTRAP | UNT_CLUSTERBOMB) {
                if ss.id != bl.id {
                    attack(BF_MISC, sg.skill_id, sg.skill_lv, sg.val1 | SD_LEVEL);
                }
                return 1;
            }
            if sg.unit_id == UNT_CLAYMORETRAP {
                if src.id == bl.id {
                    return 1;
                }
                if bl.kind == BL_SKILL {
                    let Some(sug) = data::group_of_unit(c, bl.id) else { return 0 };
                    match sug.unit_id {
                        UNT_CLAYMORETRAP | UNT_LANDMINE | UNT_BLASTMINE | UNT_SHOCKWAVE | UNT_SANDMAN | UNT_FLASHER | UNT_FREEZINGTRAP
                        | UNT_FIRINGTRAP | UNT_ICEBOUNDTRAP => {
                            super::clif::clif_changetraplook(c, bl, UNT_USED_TRAPS);
                            data::upd_group(c, sug.group_id, |g| {
                                g.limit = c.tick() - g.tick + 1500;
                                g.unit_id = UNT_USED_TRAPS;
                            });
                        }
                        _ => {}
                    }
                }
                // fallthrough to default
            }
            // default:
            let mut split_count = 0;
            if skill_get_nk(sg.skill_id, &[NK_SPLASHSPLIT]) {
                split_count = skill_area_sub_count_enemies(c, &src, skill_get_splash(sg.skill_id, sg.skill_lv) as i16, &src).max(1);
            }
            attack(skill_get_type(sg.skill_id), sg.skill_id, sg.skill_lv, split_count);
        }
    }
    1
}

/// skill.cpp skill_detonator: Ranger's Detonator sets off `src`'s traps in range (`flag`: the level for a Kunai
/// Distortion that turns into Kunai Refraction).
pub fn skill_detonator(c: &Ctx, bl: &Unit, src: &Unit, flag: i32) -> i32 {
    if bl.kind != BL_SKILL {
        return 0;
    }
    let Some(unit) = data::get_unit(c, bl.id) else { return 0 };
    let Some(group) = data::skill_id2group(c, unit.group_id) else { return 0 };
    if group.src_id != src.id {
        return 0;
    }
    let unit_id = group.unit_id;
    match unit_id {
        //List of Hunter and Ranger Traps that can be detonate.
        UNT_BLASTMINE | UNT_SANDMAN | UNT_CLAYMORETRAP | UNT_TALKIEBOX | UNT_CLUSTERBOMB | UNT_FIRINGTRAP | UNT_ICEBOUNDTRAP => {
            match unit_id {
                UNT_TALKIEBOX => {
                    super::clif::clif_talkiebox(c, bl, group.valstr.as_deref().unwrap_or(""));
                    data::upd_group(c, group.group_id, |g| g.val2 = -1);
                }
                UNT_CLAYMORETRAP | UNT_FIRINGTRAP | UNT_ICEBOUNDTRAP => {
                    let kinds = (group.bl_flag | BL_SKILL as i32 | !BCT_SELF) as u16;
                    let ids = util::ids_in_range_wc(c, bl, skill_get_splash(group.skill_id, group.skill_lv) as i16, kinds);
                    util::each(c, &ids, |t| skill_trap_splash(c, t, bl.id, group.tick));
                }
                _ => {
                    let ids = util::ids_in_range_wc(c, bl, skill_get_splash(group.skill_id, group.skill_lv) as i16, group.bl_flag as u16);
                    util::each(c, &ids, |t| skill_trap_splash(c, t, bl.id, group.tick));
                }
            }
            if data::group_of_unit(c, bl.id).is_none() {
                return 0;
            }
            super::clif::clif_changetraplook(c, bl, UNT_USED_TRAPS);
            data::upd_group(c, group.group_id, |g| {
                g.unit_id = UNT_USED_TRAPS;
                g.limit = c.tick() - g.tick
                    + if unit_id == UNT_TALKIEBOX {
                        5000
                    } else if unit_id == UNT_CLUSTERBOMB || unit_id == UNT_ICEBOUNDTRAP {
                        2500
                    } else if unit_id == UNT_FIRINGTRAP {
                        0
                    } else {
                        1500
                    };
            });
        }
        UNT_KUNAIWAIKYOKU => {
            let (x, y) = (bl.x, bl.y);
            group::skill_delunit(c, bl.id);
            super::setting::skill_unitsetting(c, src, SS_KUNAIKUSSETSU, flag as u16, x, y, UNIT_NOCONSUME_AMMO);
        }
        _ => {}
    }
    0
}

/// skill.cpp skill_reveal_trap: a hidden trap becomes visible.
fn skill_reveal_trap(c: &Ctx, bl: &Unit) -> i32 {
    let Some(su) = data::get_unit(c, bl.id) else { return 0 };
    let Some(g) = data::skill_id2group(c, su.group_id) else { return 0 };
    if su.alive && su.hidden && skill_get_inf2(g.skill_id, &[INF2_ISTRAP]) {
        //Change look is not good enough, the client ignores it as an actual trap still. [Skotlex]
        //clif_changetraplook(bl, su->group->unit_id);
        data::upd_unit(c, bl.id, |u| u.hidden = false);
        if let Some(su) = data::get_unit(c, bl.id) {
            super::clif::skill_getareachar_skillunit_visibilty(c, &su, crate::clif::Target::Area);
        }
        return 1;
    }
    0
}

/// skill.cpp skill_reveal_trap_inarea.
pub fn skill_reveal_trap_inarea(c: &Ctx, src: &Unit, range: i16, x: i16, y: i16) {
    if conf::traps_setting() == 0 {
        return;
    }
    let ids = util::ids_in_area(c, src.map, x - range, y - range, x + range, y + range, BL_SKILL, false);
    util::each(c, &ids, |bl| skill_reveal_trap(c, bl));
}

/// skill.cpp skill_graffitiremover: a Graffiti unit (removed when `remove` is 1). Returns 1 for a Graffiti.
pub fn skill_graffitiremover(c: &Ctx, bl: &Unit, remove: i32) -> i32 {
    if bl.kind != BL_SKILL {
        return 0;
    }
    let Some(g) = data::group_of_unit(c, bl.id) else { return 0 };
    if g.unit_id == UNT_GRAFFITI {
        if remove == 1 {
            group::skill_delunit(c, bl.id);
        }
        return 1;
    }
    0
}
