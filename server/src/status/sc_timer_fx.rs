//! status.cpp status_change_timer: the per-status `switch (type)` (status.cpp:14170-15216), the periodic effects.
//!
//! `FreeBlockLock freeLock` / `freeLock.lock()` (map_freeblock_lock: the unit is not freed while the case runs) has no
//! counterpart: rows are only deleted by explicit calls here. `sce` is a copy (see `TimerSt`): a case that changes it
//! and then calls something that reads this unit's statuses writes it back first.

use super::consts::*;
use super::defs::*;
use super::hp::{status_charge, status_damage, status_fix_damage, status_heal, status_percent_damage, status_percent_heal, status_zap};
use super::sc::{sc_start, sc_start4, status_change_end, status_isdead, INFINITE_TICK};
use super::sc_ext::*;
use super::sc_st::{TimerFlow, TimerSt};
use crate::db::skills::ids::*;
use crate::db::skills::{skill_get_time2, skill_get_unit_range, DMG_NORMAL};
use crate::unit;

/// Every status this switch has a case for (a unit test checks it against rAthena's case list).
pub const CASES: &[i32] = &[
    SC_MAXIMIZEPOWER, SC_CLOAKING, SC_CHASEWALK, SC_HIDING, SC_SIGHT, SC_RUWACH, SC_SIGHTBLASTER, SC_AUTOBERSERK, SC_STONE, SC_POISON,
    SC_DPOISON, SC_BLEEDING, SC_BURNING, SC_TOXIN, SC_MAGICMUSHROOM, SC_PYREXIA, SC_LEECHESEND, SC_DEATHHURT, SC_TENSIONRELAX,
    SC_KNOWLEDGE, SC_GRADUAL_GRAVITY, SC_BOSSMAPINFO, SC_DANCING, SC_BERSERK, SC_NOCHAT, SC_SPLASHER, SC_MARIONETTE, SC_MARIONETTE2,
    SC_GOSPEL, SC_JAILED, SC_BLIND, SC_ABUNDANCE, SC_OBLIVIONCURSE, SC_WEAPONBLOCKING, SC_CLOAKINGEXCEED, SC_RENOVATIO, SC_SPHERE_1,
    SC_SPHERE_2, SC_SPHERE_3, SC_SPHERE_4, SC_SPHERE_5, SC_FREEZE_SP, SC_ELECTRICSHOCKER, SC_CAMOUFLAGE, SC__REPRODUCE, SC__SHADOWFORM,
    SC__INVISIBILITY, SC_STRIKING, SC_WARMER, SC_HELLS_PLANT, SC_WINKCHARM, SC_VOICEOFSIREN, SC_DEEPSLEEP, SC_SATURDAYNIGHTFEVER,
    SC_CRYSTALIZE, SC_FORCEOFVANGUARD, SC_BANDING, SC_REFLECTDAMAGE, SC_OVERHEAT_LIMITPOINT, SC_OVERHEAT, SC_MAGNETICFIELD,
    SC_INSPIRATION, SC_SHIELDSPELL_HP, SC_SHIELDSPELL_SP, SC_TROPIC, SC_CHILLY_AIR, SC_WILD_STORM, SC_UPHEAVAL, SC_HEATER, SC_COOLER,
    SC_BLAST, SC_CURSED_SOIL, SC_PYROTECHNIC, SC_AQUAPLAY, SC_GUST, SC_PETROLOGY, SC_CIRCLE_OF_FIRE, SC_FIRE_CLOAK, SC_WATER_DROP,
    SC_WATER_SCREEN, SC_WIND_CURTAIN, SC_WIND_STEP, SC_STONE_SHIELD, SC_SOLID_SKIN, SC_FLAMETECHNIC, SC_FLAMEARMOR, SC_COLD_FORCE,
    SC_CRYSTAL_ARMOR, SC_GRACE_BREEZE, SC_EYES_OF_STORM, SC_EARTH_CARE, SC_STRONG_PROTECTION, SC_DEEP_POISONING, SC_POISON_SHIELD,
    SC_WATER_SCREEN_OPTION, SC_TEARGAS, SC_TEARGAS_SOB, SC_STOMACHACHE, SC_LEADERSHIP, SC_GLORYWOUNDS, SC_SOULCOLD, SC_HAWKEYES,
    SC_MEIKYOUSISUI, SC_KAGEMUSYA, SC_ANGRIFFS_MODUS, SC_CBC, SC_FULL_THROTTLE, SC_REBOUND, SC_KINGS_GRACE, SC_FRIGG_SONG, SC_C_MARKER,
    SC_STEALTHFIELD_MASTER, SC_VACUUM_EXTREME, SC_FIRE_INSIGNIA, SC_WATER_INSIGNIA, SC_WIND_INSIGNIA, SC_EARTH_INSIGNIA, SC_BITESCAR,
    SC_FRESHSHRIMP, SC_NEWMOON, SC_CREATINGSTAR, SC_SOULUNITY, SC_SOULCOLLECT, SC_HELPANGEL, SC_BURNT, SC_MEDIALE, SC_DANCING_KNIFE,
    SC_SERVANTWEAPON, SC_ABYSSFORCEWEAPON, SC_KILLING_AURA, SC_INTENSIVE_AIM, SC_TALISMAN_OF_PROTECTION, SC_KI_SUL_RAMPAGE,
    SC_STAR_BURST,
];

/// status_charge(...) != 0.
fn charge(st: &TimerSt, hp: i64, sp: i64) -> bool {
    status_charge(st.c, st.bl.id, hp, sp) != 0
}

/// `map_foreachinallrange(status_change_timer_sub, bl, range, kinds, bl, sce, type, tick)`.
fn foreach_timer_sub(st: &mut TimerSt, range: i32, kinds: u16) {
    let c = st.c;
    // (the entry is written first: the attacks the pass makes read the owner's statuses)
    st.sync();
    let bl = st.bl_now();
    for u in crate::map::units_in_range(c, bl.map, bl.x, bl.y, range as i16, kinds) {
        if let Some(u) = unit::get(c, u.id) {
            super::sc_clear::status_change_timer_sub(c, &u, &bl, Some(&mut st.sce), st.t, st.tick);
        }
    }
}

/// The periodic-effect switch. `TimerFlow::Break` = `break` (then the interval check, else the status ends),
/// `TimerFlow::Return` = `return 0`.
pub fn timer_effect(st: &mut TimerSt) -> TimerFlow {
    let c = st.c;
    let bl_id = st.bl.id;
    let tick = st.tick;
    let t = st.t;
    let is_pc = st.is_pc();
    match t {
        SC_MAXIMIZEPOWER | SC_CLOAKING => {
            // status_damage(nullptr, bl, 0, 1, 0, 3, 0): the 7-argument overload (dhp 0, dsp 1, walkdelay 0, flag 3)
            if status_damage(c, None, bl_id, 0, 1, 0, 0, 3, 0) == 0 {
                return TimerFlow::Break; // Not enough SP to continue.
            }
            st.next(st.sce.val2 as i64 + tick);
            TimerFlow::Return
        }
        SC_CHASEWALK => {
            if !charge(st, 0, st.sce.val4 as i64) {
                return TimerFlow::Break; // Not enough SP to continue.
            }
            if !st.has(SC_CHASEWALK2) {
                let rogue = st.get(SC_SPIRIT).map(|e| e.val2 == SL_ROGUE as i32).unwrap_or(false);
                // SL bonus -> x10 duration
                let dur = (if rogue { 10 } else { 1 }) as i64 * skill_get_time2(crate::db::misc::status::status_db().get_skill(t), st.sce.val1 as u16) as i64;
                let bl = st.bl_now();
                sc_start(c, Some(&bl), &bl, SC_CHASEWALK2, 100, 1i32.wrapping_shl((st.sce.val1 - 1) as u32), dur);
            }
            st.next(st.sce.val2 as i64 + tick);
            TimerFlow::Return
        }
        SC_HIDING => {
            st.sce.val2 -= 1;
            if st.sce.val2 > 0 {
                // (a val4 of 0 would divide by zero in rAthena; it is val1 + 3 from the value setup)
                if st.sce.val2.checked_rem(st.sce.val4).unwrap_or(1) == 0 && !charge(st, 0, 1) {
                    return TimerFlow::Break; // Fail if it's time to substract SP and there isn't.
                }
                st.next(1000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_SIGHT | SC_RUWACH | SC_SIGHTBLASTER => {
            if t == SC_SIGHTBLASTER {
                //Restore trap immunity
                if st.sce.val4 % 2 != 0 {
                    st.sce.val4 -= 1;
                }
                let range = st.sce.val3;
                foreach_timer_sub(st, range, crate::map::BL_CHAR | crate::map::BL_SKILL);
            } else {
                let range = st.sce.val3;
                foreach_timer_sub(st, range, crate::map::BL_CHAR);
                st.sync();
                let bl = st.bl_now();
                crate::skillunit::ontimer::skill_reveal_trap_inarea(c, &bl, st.sce.val3 as i16, bl.x, bl.y);
                st.reload();
            }
            st.sce.val2 -= 1;
            if st.sce.val2 > 0 {
                st.sce.val4 += 20; // Use for Shadow Form 2 seconds checking.
                st.next(20 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_AUTOBERSERK => {
            let status = st.status();
            if st.get(SC_PROVOKE).map(|e| e.val4 == 1).unwrap_or(false) {
                // End infinite provoke granted by auto berserk
                if status.hp > status.max_hp / 4 {
                    status_change_end(c, bl_id, SC_PROVOKE, 0);
                }
            } else {
                // Start infinite provoke granted by auto berserk
                if status.hp <= status.max_hp / 4 {
                    let bl = st.bl_now();
                    sc_start4(c, Some(&bl), &bl, SC_PROVOKE, 100, 10, 0, 0, 1, INFINITE_TICK);
                }
            }
            // Repeat check every interval
            st.next(100 + tick);
            TimerFlow::Return
        }
        SC_STONE => {
            let status = st.status();
            if st.sce.val4 >= 0 && status.hp > status.max_hp / 4 {
                status_percent_damage(c, None, bl_id, -1, 0, 0, false);
            }
            TimerFlow::Break
        }
        SC_POISON | SC_DPOISON => {
            if st.sce.val4 >= 0 && !st.has(SC_SLOWPOISON) {
                let status = st.status();
                let damage: u32 = if is_pc {
                    if t == SC_DPOISON { 2 + status.max_hp / 50 } else { 2 + status.max_hp.wrapping_mul(3) / 200 }
                } else if t == SC_DPOISON {
                    2 + status.max_hp / 100
                } else {
                    2 + status.max_hp / 200
                };
                if status.hp > (status.max_hp / 4).max(damage) {
                    // Stop damaging after 25% HP left.
                    status_zap(c, bl_id, damage as i64, 0, 0);
                }
            }
            TimerFlow::Break
        }
        SC_BLEEDING => {
            if st.sce.val4 >= 0 {
                let mut damage: i64 = (c.rnd() % 600 + 200) as i64;
                let hp = st.status().hp;
                if !is_pc && damage >= hp as i64 {
                    damage = hp.wrapping_sub(1) as i64; // No deadly damage for monsters
                }
                status_zap(c, bl_id, damage, 0, 0);
            }
            TimerFlow::Break
        }
        SC_BURNING => {
            if st.sce.val4 >= 0 {
                let status = st.status();
                let damage: i64 = 1000 + (status.max_hp.wrapping_mul(3) / 100) as i64; // Deals fixed (1000 + 3%*MaxHP)
                let bl = st.bl_now();
                crate::battle::clif::clif_damage(c, &bl, &bl, tick, 0, 1, damage, 1, DMG_NORMAL, 0, false);
                status_fix_damage(c, Some(&bl), bl_id, damage, 1, 0);
            }
            TimerFlow::Break
        }
        SC_TOXIN => {
            if st.sce.val4 >= 0 {
                // Damage is every 10 seconds including 3%sp drain.
                let status = st.status();
                if st.sce.val3 == 1 {
                    // Target
                    let bl = st.bl_now();
                    crate::battle::clif::clif_damage(c, &bl, &bl, tick, status.amotion as i32, status.dmotion as i32 + 500, 1, 1, DMG_NORMAL, 0, false);
                    // status_damage(bl, bl, 1, max_sp * 3 / 100, dmotion + 500, 0, 0): the 7-argument overload
                    status_damage(c, Some(&bl), bl_id, 1, (status.max_sp.wrapping_mul(3) / 100) as i64, 0, status.dmotion as i64 + 500, 0, 0);
                } else {
                    // Caster
                    st.interval = 1000; // Assign here since status_get_sc_internval() contains the target interval.
                    if status.sp < status.max_sp {
                        status_heal(c, bl_id, 0, (status.max_sp as i32 / 100) as i64, 0, 1);
                    }
                }
            }
            TimerFlow::Break
        }
        SC_MAGICMUSHROOM => {
            if st.sce.val4 >= 0 {
                let mut flag = false;
                let status = st.status();
                let mut damage: i64 = (status.max_hp.wrapping_mul(3) / 100) as i64;
                if status.hp as i64 <= damage {
                    damage = status.hp.wrapping_sub(1) as i64; // Cannot Kill
                }
                if damage > 0 {
                    // 3% Damage each 4 seconds
                    st.sync();
                    status_zap(c, bl_id, damage, 0, 0);
                    flag = !st.has(t); // Killed? Should not
                }
                if !flag {
                    // Random Skill Cast
                    let db = &crate::db::skills::skill_misc().magic_mushroom;
                    if !db.is_empty() && is_pc && !pc_issit(c, bl_id) {
                        // Can't cast if sit
                        let mushroom_skill_id = db[(c.rnd() as usize) % db.len()];
                        if crate::db::skills::skill_get_index(mushroom_skill_id) == 0 {
                            return TimerFlow::Break;
                        }
                        if let Some(mut u) = unit::get(c, bl_id) {
                            unit::stop_attack(c, &mut u);
                            unit::save(c, &u);
                        }
                        st.sync();
                        crate::skill::unit::unit_skillcastcancel(c, bl_id, 0);
                        // Magic Mushroom skills are buffs or area damage
                        if let Some(bl) = unit::get(c, bl_id) {
                            use crate::db::skills::CastType;
                            match crate::db::skills::skill_get_casttype(mushroom_skill_id) {
                                CastType::Ground => {
                                    crate::skill::castend::skill_castend_pos2(c, &bl, bl.x, bl.y, mushroom_skill_id, 1, tick, 0);
                                }
                                CastType::NoDamage => {
                                    crate::skill::castend::skill_castend_nodamage_id(c, &bl, &bl, mushroom_skill_id, 1, tick, 0);
                                }
                                CastType::Damage => {
                                    crate::skill::castend::skill_castend_damage_id(c, &bl, &bl, mushroom_skill_id, 1, tick, 0);
                                }
                            }
                        }
                        st.reload();
                    }
                    crate::clif::emotion(c, &st.bl_now(), ET_SMILE);
                }
            }
            TimerFlow::Break
        }
        SC_PYREXIA => {
            if st.sce.val4 >= 0 {
                let status = st.status();
                let bl = st.bl_now();
                crate::battle::clif::clif_damage(c, &bl, &bl, tick, status.amotion as i32, status.dmotion as i32 + 500, 100, 1, DMG_NORMAL, 0, false);
                status_fix_damage(c, Some(&bl), bl_id, 100, status.dmotion as i64 + 500, 0);
                crate::skill::unit::unit_skillcastcancel(c, bl_id, 2);
            }
            TimerFlow::Break
        }
        SC_LEECHESEND => {
            if st.sce.val4 >= 0 {
                let status = st.status();
                // {Target VIT x (New Poison Research Skill Level - 3)} + (Target HP/100)
                let damage: i64 = (status.vit as i32 * (st.sce.val1 - 3) + status.max_hp as i32 / 100) as i64;
                let bl = st.bl_now();
                crate::battle::clif::clif_damage(c, &bl, &bl, tick, status.amotion as i32, status.dmotion as i32 + 500, damage, 1, DMG_NORMAL, 0, false);
                status_fix_damage(c, Some(&bl), bl_id, damage, status.dmotion as i64 + 500, 0);
                crate::skill::unit::unit_skillcastcancel(c, bl_id, 2);
            }
            TimerFlow::Break
        }
        SC_DEATHHURT => {
            if st.sce.val4 >= 0 {
                let status = st.status();
                if status.hp < status.max_hp {
                    status_heal(c, bl_id, (status.max_hp as i32 / 100) as i64, 0, 0, 1);
                }
            }
            TimerFlow::Break
        }
        SC_TENSIONRELAX => {
            let status = st.status();
            if status.max_hp > status.hp {
                st.sce.val3 -= 1;
                if st.sce.val3 >= 0 {
                    st.next(10000 + tick);
                    return TimerFlow::Return;
                }
            }
            TimerFlow::Break
        }
        SC_KNOWLEDGE => {
            if !is_pc {
                return TimerFlow::Break;
            }
            if super::sc_ext::on_feel_map(c, bl_id, st.bl_now().map as i32) {
                // Timeout will be handled by pc_setpos
                st.sce.timer = 0;
                st.sync();
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_GRADUAL_GRAVITY => {
            if st.sce.val4 >= 0 {
                let status = st.status();
                status_zap(c, bl_id, (status.max_hp.wrapping_mul(st.sce.val2 as u32) / 100) as i64, 0, 0);
            }
            TimerFlow::Break
        }
        SC_BOSSMAPINFO => {
            if is_pc && st.sce.val4 >= 0 {
                let Some(boss_md) = map_id2boss(c, st.sce.val1) else {
                    st.sce.val4 = 0;
                    return TimerFlow::Break;
                };
                let sd = st.bl_now();
                let boss_u = unit::get(c, boss_md.id);
                let boss_m = boss_u.as_ref().map(|u| u.map);
                if Some(sd.map) != boss_m {
                    // Not on same map anymore
                    st.sce.val4 = 0;
                    return TimerFlow::Break;
                } else if boss_u.as_ref().map(|u| u.on_map).unwrap_or(false) {
                    // Boss is alive - Update X, Y on minimap
                    st.sce.val2 = 0;
                    clif_bossmapinfo(c, &sd, Some(&boss_md), BOSS_INFO_ALIVE);
                } else if boss_md.spawn_timer != 0 && st.sce.val2 == 0 {
                    // Boss is dead
                    st.sce.val2 = 1;
                    clif_bossmapinfo(c, &sd, Some(&boss_md), BOSS_INFO_DEAD);
                }
            }
            TimerFlow::Break
        }
        SC_DANCING => {
            // SP consumption by time of dancing skills
            let mut s = 0;
            let mut sp = 1;
            st.sce.val3 -= 1;
            if st.sce.val3 <= 0 {
                return TimerFlow::Break;
            }
            if (st.sce.val1 & 0xFFFF) == CG_MOONLIT as i32 {
                // Moonlit's cost is 4sp*skill_lv [Skotlex]
                sp = 4 * (st.sce.val1 >> 16);
                // Upkeep is also every 10 secs.
                s = 10;
            }
            if s != 0 && st.sce.val3 % s == 0 {
                st.sync();
                if !charge(st, 0, sp as i64) {
                    return TimerFlow::Break;
                }
                // If no more SP left, the effect ends immediately
                if st.status().sp == 0 {
                    return TimerFlow::Break;
                }
            }
            st.next(1000 + tick);
            TimerFlow::Return
        }
        SC_BERSERK => {
            // 5% every 10 seconds [DracoRPG]
            st.sce.val3 -= 1;
            if st.sce.val3 > 0 {
                st.sync();
                if charge(st, st.sce.val2 as i64, 0) && st.status().hp > 100 {
                    st.next(st.sce.val4 as i64 + tick);
                    return TimerFlow::Return;
                }
            }
            TimerFlow::Break
        }
        SC_NOCHAT => {
            if is_pc {
                if let Some(mut ch) = crate::pc::character_of(c, bl_id) {
                    ch.manner = ch.manner.wrapping_add(1);
                    super::put_char(c, &ch);
                    super::clif::changemanner(c, bl_id);
                    super::clif::updatestatus(c, bl_id, SP_MANNER);
                    if ch.manner < 0 {
                        // Every 60 seconds your manner goes up by 1 until it gets back to 0.
                        st.next(60000 + tick);
                        return TimerFlow::Return;
                    }
                }
            }
            TimerFlow::Break
        }
        SC_SPLASHER => {
            // Custom Venom Splasher countdown timer
            st.sce.val4 -= 500;
            if st.sce.val4 > 0 {
                st.next(500 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_MARIONETTE | SC_MARIONETTE2 => {
            if let Some(pbl) = unit::get(c, st.sce.val1 as u32) {
                if crate::mob::unit::check_distance_bl(&st.bl_now(), &pbl, 7) {
                    st.next(1000 + tick);
                    return TimerFlow::Return;
                }
            }
            TimerFlow::Break
        }
        SC_GOSPEL => {
            if st.sce.val4 == BCT_SELF {
                st.sce.val2 -= 1;
                if st.sce.val2 > 0 {
                    let hp = if st.sce.val1 > 5 { 45 } else { 30 };
                    let sp = if st.sce.val1 > 5 { 35 } else { 20 };
                    st.sync();
                    if !charge(st, hp, sp) {
                        return TimerFlow::Break;
                    }
                    st.next(10000 + tick);
                    return TimerFlow::Return;
                }
            }
            TimerFlow::Break
        }
        SC_JAILED => {
            if st.sce.val1 == i32::MAX || {
                st.sce.val1 -= 1;
                st.sce.val1 > 0
            } {
                st.next(60000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_BLIND => {
            if st.has(SC_FOGWALL) {
                // Blind lasts forever while you are standing on the fog.
                st.next(5000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_ABUNDANCE => {
            st.sce.val4 -= 1;
            if st.sce.val4 > 0 {
                st.sync();
                status_heal(c, bl_id, 0, 60, 0, 0);
                // (no `return 0`: the switch breaks with the new timer set, so the status_change_end below sees another
                // timer and keeps the status)
                st.next(10000 + tick);
            }
            TimerFlow::Break
        }
        SC_OBLIVIONCURSE => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                crate::clif::emotion(c, &st.bl_now(), ET_QUESTION);
                st.next(3000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_WEAPONBLOCKING => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                st.sync();
                if !charge(st, 0, 3) {
                    return TimerFlow::Break;
                }
                st.next(5000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_CLOAKINGEXCEED => {
            if !charge(st, 0, (10 - st.sce.val1) as i64) {
                return TimerFlow::Break;
            }
            st.next(1000 + tick);
            TimerFlow::Return
        }
        SC_RENOVATIO => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                let mut heal = (st.status().max_hp.wrapping_mul((st.sce.val1 + 4) as u32) / 100) as i32;
                if st.has(SC_AKAITSUKI) && heal != 0 {
                    heal = (!heal).wrapping_add(1);
                }
                st.sync();
                status_heal(c, bl_id, heal as i64, 0, 0, 3);
                st.next(5000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_SPHERE_1 | SC_SPHERE_2 | SC_SPHERE_3 | SC_SPHERE_4 | SC_SPHERE_5 => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                st.sync();
                if !charge(st, 0, 1) {
                    return TimerFlow::Break;
                }
                st.next(1000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_FREEZE_SP => {
            if !charge(st, 0, st.sce.val2 as i64) {
                for i in SC_SPELLBOOK1..=SC_MAXSPELLBOOK {
                    // Also remove stored spell as well.
                    status_change_end(c, bl_id, i, 0);
                }
                return TimerFlow::Break;
            }
            st.next(10000 + tick);
            TimerFlow::Return
        }
        SC_ELECTRICSHOCKER => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                let max_sp = st.status().max_sp;
                st.sync();
                status_charge(c, bl_id, 0, ((5 * st.sce.val1) as u32).wrapping_mul(max_sp) as i64 / 100);
                st.next(1000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_CAMOUFLAGE => {
            if !charge(st, 0, (7 - st.sce.val1) as i64) {
                return TimerFlow::Break;
            }
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                st.sce.val3 += 1;
            }
            st.next(1000 + tick);
            TimerFlow::Return
        }
        SC__REPRODUCE => {
            if !charge(st, 0, 1) {
                return TimerFlow::Break;
            }
            st.next(1000 + tick);
            TimerFlow::Return
        }
        SC__SHADOWFORM => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                st.sync();
                if !charge(st, 0, (11 - st.sce.val1) as i64) {
                    return TimerFlow::Break;
                }
                st.next(1000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC__INVISIBILITY => {
            // 6% - skill_lv. ((12 - 2 * val1) * max_sp is computed in uint32)
            let sp = (((12 - 2 * st.sce.val1) as u32).wrapping_mul(st.status().max_sp) / 100) as i64;
            if !charge(st, 0, sp) {
                return TimerFlow::Break;
            }
            st.next(1000 + tick);
            TimerFlow::Return
        }
        SC_STRIKING => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                st.sync();
                if !charge(st, 0, st.sce.val3 as i64) {
                    return TimerFlow::Break;
                }
                st.next(1000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_WARMER => {
            let status = st.status();
            let heater = unit::get(c, st.sce.val2 as u32).map(|u| has_status_change(u.kind) && super::sc::has_sce(c, u.id, SC_HEATER_OPTION)).unwrap_or(false);
            let mut hp: i32 = if heater {
                (status.max_hp.wrapping_mul(3).wrapping_mul(st.sce.val1 as u32) / 100) as i32
            } else {
                (status.max_hp.wrapping_mul(st.sce.val1 as u32) / 100) as i32
            };
            if st.has(SC_AKAITSUKI) && hp != 0 {
                hp = (!hp).wrapping_add(1);
            }
            if status.hp != status.max_hp {
                status_heal(c, bl_id, hp as i64, 0, 0, 0);
            }
            st.next(3000 + tick);
            TimerFlow::Return
        }
        SC_HELLS_PLANT => {
            if st.sce.val4 >= 0 {
                st.sync();
                let bl = st.bl_now();
                crate::skill::castend::skill_castend_damage_id(c, &bl, &bl, GN_HELLS_PLANT_ATK, st.sce.val1 as u16, tick, 0);
                st.reload();
            }
            TimerFlow::Break
        }
        SC_WINKCHARM | SC_VOICEOFSIREN => {
            if st.sce.val4 >= 0 {
                crate::clif::emotion(c, &st.bl_now(), ET_THROB);
            }
            TimerFlow::Break
        }
        SC_DEEPSLEEP => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                // Recovers 3% HP/SP every 2 seconds.
                let status = st.status();
                st.sync();
                status_heal(c, bl_id, (status.max_hp.wrapping_mul(3) / 100) as i64, (status.max_sp.wrapping_mul(3) / 100) as i64, 0, 2);
                st.next(2000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_SATURDAYNIGHTFEVER => {
            // 1% HP/SP drain every val4 seconds [Jobbie]
            st.sce.val3 -= 1;
            if st.sce.val3 >= 0 {
                let status = st.status();
                st.sync();
                if !charge(st, (status.hp / 100) as i64, (status.sp / 100) as i64) {
                    return TimerFlow::Break;
                }
                st.next(st.sce.val4 as i64 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_CRYSTALIZE => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                // Drains 2% of HP and 1% of SP every seconds.
                let status = st.status();
                st.sync();
                if !charge(st, (status.max_hp.wrapping_mul(2) / 100) as i64, (status.max_sp / 100) as i64) {
                    return TimerFlow::Break;
                }
                st.next(1000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_FORCEOFVANGUARD => {
            if !charge(st, 0, (24 - 4 * st.sce.val1) as i64) {
                return TimerFlow::Break;
            }
            st.next(10000 + tick);
            TimerFlow::Return
        }
        SC_BANDING => {
            if charge(st, 0, (7 - st.sce.val1) as i64) {
                st.sce.val2 = if is_pc { skill_banding_count(c, bl_id) } else { 1 };
                st.next(5000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_REFLECTDAMAGE => {
            st.sce.val4 -= 1;
            if st.sce.val4 > 0 {
                st.sync();
                if !charge(st, 0, 10) {
                    return TimerFlow::Break;
                }
                st.next(1000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_OVERHEAT_LIMITPOINT => {
            st.sce.val1 -= 1;
            if st.sce.val1 >= 0 {
                // Cooling
                if st.sce.val2 == 0 {
                    // Flag the overheat limit once it has been met.
                    const LIMIT: [i32; 5] = [150, 200, 280, 360, 450];
                    let skill_lv = if is_pc { cap_value(pc_checkskill(c, bl_id, NC_MAINFRAME), 0, (LIMIT.len() - 1) as i32) } else { 0 };
                    if st.sce.val1 > LIMIT[skill_lv as usize] {
                        st.sce.val2 = 1;
                    }
                } else {
                    st.sync();
                    status_change_end(c, bl_id, SC_OVERHEAT, 0);
                    if st.sce.val2 > 0 {
                        let bl = st.bl_now();
                        sc_start(c, Some(&bl), &bl, SC_OVERHEAT, 100, st.sce.val1, 975);
                    }
                }
                st.next(1000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_OVERHEAT => {
            let status = st.status();
            let mut damage: u32 = status.max_hp / 100; // Suggestion 1% each second
            if damage >= status.hp {
                damage = status.hp.wrapping_sub(1); // Do not kill, just keep you with 1 hp minimum
            }
            status_zap(c, bl_id, damage as i64, 0, 0);
            st.next(975 + tick); // Tick is not 1000 to avoid desync with SC_OVERHEAT_LIMITPOINT.
            TimerFlow::Return
        }
        SC_MAGNETICFIELD => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                let src = unit::get(c, st.sce.val2 as u32);
                match src {
                    Some(src) if !status_isdead(c, &src) && src.map == st.bl_now().map => {}
                    _ => return TimerFlow::Break,
                }
                st.sync();
                if !charge(st, 0, 50) {
                    let sp = st.status().sp;
                    status_zap(c, bl_id, 0, sp as i64, 0);
                }
                if st.has(t) {
                    st.next(1000 + tick);
                }
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_INSPIRATION => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                let status = st.status();
                let hp = (status.max_hp.wrapping_mul((35 - 5 * st.sce.val1) as u32) / 1000) as i64;
                let sp = (status.max_sp.wrapping_mul((45 - 5 * st.sce.val1) as u32) / 1000) as i64;
                st.sync();
                if !charge(st, hp, sp) {
                    return TimerFlow::Break;
                }
                st.next(5000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_SHIELDSPELL_HP => {
            let status = st.status();
            if st.sce.val4 >= 0 && status.hp < status.max_hp {
                status_heal(c, bl_id, (status.max_hp.wrapping_mul(st.sce.val2 as u32) / 100) as i64, 0, 0, 1);
            }
            TimerFlow::Break
        }
        SC_SHIELDSPELL_SP => {
            let status = st.status();
            if st.sce.val4 >= 0 && status.sp < status.max_sp {
                status_heal(c, bl_id, 0, (status.max_sp.wrapping_mul(st.sce.val2 as u32) / 100) as i64, 0, 1);
            }
            TimerFlow::Break
        }
        SC_TROPIC | SC_CHILLY_AIR | SC_WILD_STORM | SC_UPHEAVAL | SC_HEATER | SC_COOLER | SC_BLAST | SC_CURSED_SOIL | SC_PYROTECHNIC | SC_AQUAPLAY
        | SC_GUST | SC_PETROLOGY | SC_CIRCLE_OF_FIRE | SC_FIRE_CLOAK | SC_WATER_DROP | SC_WATER_SCREEN | SC_WIND_CURTAIN | SC_WIND_STEP
        | SC_STONE_SHIELD | SC_SOLID_SKIN | SC_FLAMETECHNIC | SC_FLAMEARMOR | SC_COLD_FORCE | SC_CRYSTAL_ARMOR | SC_GRACE_BREEZE
        | SC_EYES_OF_STORM | SC_EARTH_CARE | SC_STRONG_PROTECTION | SC_DEEP_POISONING | SC_POISON_SHIELD => {
            if !charge(st, 0, st.sce.val2 as i64) {
                let bl = st.bl_now();
                let s_bl = crate::battle::target::battle_get_master(c, &bl);
                if bl.kind == BL_ELEM {
                    crate::elemental::elemental_change_mode(c, bl.id, crate::elemental::data::EL_MODE_PASSIVE);
                }
                status_change_end(c, s_bl.id, t + 1, 0);
                status_change_end(c, bl_id, t, 0);
                return TimerFlow::Break;
            }
            st.next(st.sce.val3 as i64 + tick);
            TimerFlow::Return
        }
        SC_WATER_SCREEN_OPTION => {
            status_heal(c, bl_id, 1000, 0, 0, 2);
            st.next(10000 + tick);
            TimerFlow::Return
        }
        SC_TEARGAS => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                let src = unit::get(c, st.sce.val3 as u32);
                let damage = st.sce.val2;
                let bl = st.bl_now();
                crate::battle::clif::clif_damage(c, &bl, &bl, tick, 0, 0, damage as i64, 1, crate::battle::DMG_MULTI_HIT_ENDURE, 0, false);
                st.sync();
                // status_damage(src, bl, damage, 0, 0, 1, 0): the 7-argument overload (walkdelay 0, flag 1)
                status_damage(c, src.as_ref(), bl_id, damage as i64, 0, 0, 0, 1, 0);
                if st.has(t) {
                    st.next(2000 + tick);
                }
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_TEARGAS_SOB => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                crate::clif::emotion(c, &st.bl_now(), ET_CRY);
                st.next(3000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_STOMACHACHE => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                st.sync();
                status_charge(c, bl_id, 0, st.sce.val2 as i64); // Reduce 8 every 10 seconds.
                if is_pc && !pc_issit(c, bl_id) {
                    // Force to sit every 10 seconds.
                    sit_down(c, bl_id);
                }
                st.next(10000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_LEADERSHIP | SC_GLORYWOUNDS | SC_SOULCOLD | SC_HAWKEYES => {
            // They only end by status_change_end
            st.next(600000 + tick);
            TimerFlow::Return
        }
        SC_MEIKYOUSISUI => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                let status = st.status();
                st.sync();
                status_heal(
                    c,
                    bl_id,
                    (status.max_hp.wrapping_mul(st.sce.val2 as u32) / 100) as i64,
                    (status.max_sp.wrapping_mul(st.sce.val3 as u32) / 100) as i64,
                    0,
                    0,
                );
                st.next(1000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_KAGEMUSYA => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                st.sync();
                if !charge(st, 0, 1) {
                    return TimerFlow::Break;
                }
                st.next(1000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_ANGRIFFS_MODUS => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                // Drain hp/sp
                st.sync();
                if !charge(st, 100, 20) {
                    return TimerFlow::Break;
                }
                st.next(1000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_CBC => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                // Drain hp/sp
                let mut hp: i32 = 0;
                let sp: i32 = (st.status().max_sp.wrapping_mul(st.sce.val3 as u32) / 100) as i32;
                if st.bl.kind == BL_MOB {
                    hp = sp * 10;
                }
                st.sync();
                if !charge(st, hp as i64, sp as i64) {
                    return TimerFlow::Break;
                }
                st.next(1000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_FULL_THROTTLE => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                st.sync();
                let bl = st.bl_now();
                status_percent_damage(c, Some(&bl), bl_id, 0, st.sce.val2, 0, false);
                st.next(1000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_REBOUND => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                crate::clif::emotion(c, &st.bl_now(), ET_SWEAT);
                st.next(2000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_KINGS_GRACE => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                st.sync();
                status_percent_heal(c, bl_id, st.sce.val2, 0, 0);
                st.next(1000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_FRIGG_SONG => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                st.sync();
                status_heal(c, bl_id, st.sce.val3 as i64, 0, 0, 0);
                st.next(1000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_C_MARKER => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                let bl = st.bl_now();
                let caster = unit::get(c, st.sce.val2 as u32).filter(|u| u.kind == BL_PC);
                let Some(caster) = caster.filter(|u| u.map == bl.map) else {
                    //End the SC if caster isn't in same map
                    return TimerFlow::Break;
                };
                st.next(1000 + tick);
                clif_crimson_marker(c, &caster, &bl, false);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_STEALTHFIELD_MASTER => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                let max_sp = st.status().max_sp;
                st.sync();
                if !charge(st, 0, (max_sp.wrapping_mul(3) / 100) as i64) {
                    return TimerFlow::Break;
                }
                st.next(st.sce.val3 as i64 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_VACUUM_EXTREME => {
            if st.sce.val4 > 0 {
                // Only slide targets to center if they are standing still
                let bl = st.bl_now();
                if bl.ud.walktimer == 0 {
                    let x = (st.sce.val3 >> 16) as u16 as i16;
                    let y = (st.sce.val3 & 0xFFFF) as u16 as i16;
                    if distance_xy(x, y, bl.x, bl.y) <= skill_get_unit_range(SO_VACUUM_EXTREME, st.sce.val1 as u16) && unit::movepos(c, bl_id, x, y, 0, false) {
                        if let Some(u) = unit::get(c, bl_id) {
                            crate::battle::clif::clif_slide(c, &u, x, y);
                            crate::clif::fixpos(c, &u);
                        }
                    }
                }
                st.next(tick + st.sce.val4 as i64);
                st.sce.val4 = 0;
                st.sync();
            }
            TimerFlow::Break
        }
        SC_FIRE_INSIGNIA | SC_WATER_INSIGNIA | SC_WIND_INSIGNIA | SC_EARTH_INSIGNIA => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                let (heal_ele, hurt_ele) = match t {
                    SC_FIRE_INSIGNIA => (ELE_FIRE, ELE_EARTH),
                    SC_WATER_INSIGNIA => (ELE_WATER, ELE_FIRE),
                    SC_WIND_INSIGNIA => (ELE_WIND, ELE_WATER),
                    _ => (ELE_EARTH, ELE_WIND),
                };
                let status = st.status();
                st.sync();
                if status.def_ele as i32 == heal_ele {
                    status_heal(c, bl_id, (status.max_hp / 100) as i64, 0, 0, 1);
                } else if status.def_ele as i32 == hurt_ele {
                    status_zap(c, bl_id, (status.max_hp / 100) as i64, 0, 0);
                }
                st.next(5000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_BITESCAR => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                st.sync();
                let bl = st.bl_now();
                // status_percent_damage(bl, bl, -(sce->val2), 0, 0): the 5-argument overload (kill = false)
                status_percent_damage(c, Some(&bl), bl_id, -st.sce.val2, 0, 0, false);
                st.next(1000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_FRESHSHRIMP => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                st.sync();
                status_heal(c, bl_id, st.sce.val2 as i64, 0, 0, 2);
                st.next((10000 - ((st.sce.val1 - 1) * 1000)) as i64 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_NEWMOON => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                st.sync();
                if !charge(st, 0, 1) {
                    return TimerFlow::Break;
                }
                st.next(1000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_CREATINGSTAR => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                // Needed to check who the caster is and what AoE is giving the status.
                let star_caster = unit::get(c, st.sce.val2 as u32);
                let star_aoe = unit::get(c, st.sce.val3 as u32);
                let m = st.bl_now().map;
                let (star_caster, star_aoe) = match (star_caster, star_aoe) {
                    (Some(sc_), Some(aoe)) if !status_isdead(c, &sc_) && sc_.map == m => (sc_, aoe),
                    _ => return TimerFlow::Break,
                };
                st.next(500 + tick);
                // Attack after timer to prevent errors
                let bl = st.bl_now();
                crate::skill::attack::skill_attack(c, BF_WEAPON, &star_caster, &star_aoe, &bl, SJ_BOOKOFCREATINGSTAR, st.sce.val1 as u16, tick, 0);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_SOULUNITY => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                // Needed to check the caster's location for the range check.
                let bl = st.bl_now();
                match unit::get(c, st.sce.val2 as u32) {
                    Some(u) if !status_isdead(c, &u) && u.map == bl.map && crate::mob::unit::check_distance_bl(&bl, &u, 11) => {}
                    _ => return TimerFlow::Break,
                }
                st.sync();
                status_heal(c, bl_id, (150 * st.sce.val1) as i64, 0, 0, 2);
                st.next(3000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_SOULCOLLECT => {
            // (sd: SC_SOULCOLLECT is a player's status; rAthena dereferences sd unchecked)
            if is_pc {
                st.sync();
                crate::skill::state::pc_addsoulball(c, bl_id, 1, st.sce.val2);
                st.reload();
            }
            st.next(st.sce.val3 as i64 + tick);
            TimerFlow::Return
        }
        SC_HELPANGEL => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                st.sync();
                status_heal(c, bl_id, 1000, 350, 0, 0); // Heal amount not displayed
                st.next(1000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_BURNT => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                let mut damage: i32 = 2000;
                let hp = st.status().hp;
                if damage as u32 >= hp {
                    damage = hp.wrapping_sub(1) as i32;
                }
                st.sync();
                status_zap(c, bl_id, damage as i64, 0, 0);
                if st.has(t) {
                    st.next(1000 + tick);
                }
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_MEDIALE => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                crate::clif::specialeffect(c, &st.bl_now(), 1808, crate::clif::Target::Area);
                st.sync();
                let bl = st.bl_now();
                crate::skill::castend::skill_castend_nodamage_id(c, &bl, &bl, CD_MEDIALE_VOTUM, st.sce.val1 as u16, tick, 1);
                st.reload();
                st.next(2000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_DANCING_KNIFE => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                st.sync();
                let bl = st.bl_now();
                crate::skill::castend::skill_castend_nodamage_id(c, &bl, &bl, SHC_DANCING_KNIFE, st.sce.val1 as u16, tick, 1);
                st.reload();
                st.next(300 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_SERVANTWEAPON => {
            if st.sce.val4 >= 0 {
                if is_pc && (crate::skill::state::get(c, bl_id).servantball as i32) < crate::skill::state::MAX_SERVANTBALL {
                    crate::skill::state::pc_addservantball(c, bl_id, crate::skill::state::MAX_SERVANTBALL);
                }
                st.interval = 500.max(skill_get_time2(DK_SERVANTWEAPON, st.sce.val1 as u16));
            }
            TimerFlow::Break
        }
        SC_ABYSSFORCEWEAPON => {
            if st.sce.val4 >= 0 {
                if is_pc && (crate::skill::state::get(c, bl_id).abyssball as i32) < crate::skill::state::MAX_ABYSSBALL {
                    crate::skill::state::pc_addabyssball(c, bl_id, 1);
                }
                st.interval = 500.max(skill_get_time2(ABC_FROM_THE_ABYSS, st.sce.val1 as u16));
            }
            TimerFlow::Break
        }
        SC_KILLING_AURA => {
            if st.sce.val4 >= 0 {
                st.sync();
                let bl = st.bl_now();
                crate::skill::castend::skill_castend_damage_id(c, &bl, &bl, NPC_KILLING_AURA, st.sce.val1 as u16, tick, 0);
                st.reload();
            }
            TimerFlow::Break
        }
        SC_INTENSIVE_AIM => {
            if !st.has(SC_INTENSIVE_AIM_COUNT) {
                st.sce.val4 = 0;
            }
            if st.sce.val4 < 10 {
                st.sce.val4 += 1;
                st.sync();
                let bl = st.bl_now();
                sc_start(c, Some(&bl), &bl, SC_INTENSIVE_AIM_COUNT, 100, st.sce.val4, INFINITE_TICK);
            }
            st.next(500 + tick);
            TimerFlow::Return
        }
        SC_TALISMAN_OF_PROTECTION => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                // Get the original caster
                let bl = st.bl_now();
                let ssd = unit::get(c, st.sce.val2 as u32).filter(|u| u.kind == BL_PC);
                let party_of = |id: u32| crate::pc::character_of(c, id).map(|ch| ch.party_id).unwrap_or(0);
                // If the caster is offline, dead, on another map or
                // if the target is not a player or is in another party
                let ok = match &ssd {
                    Some(ssd) => !status_isdead(c, ssd) && ssd.map == bl.map && is_pc && party_of(ssd.id) == party_of(bl_id),
                    None => false,
                };
                if !ok {
                    // End the status change
                    st.sce.val4 = 0;
                    return TimerFlow::Break;
                }
                let hp = st.sce.val3; // sc->getSCE(SC_TALISMAN_OF_PROTECTION)->val3
                st.sync();
                status_heal(c, bl_id, hp as i64, 0, 0, 0);
                crate::skill::clif::clif_skill_nodamage(c, None, &st.bl_now(), AL_HEAL, hp, true);
                st.reload();
                st.next(3000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_KI_SUL_RAMPAGE => {
            st.sce.val4 -= 1;
            if st.sce.val4 >= 0 {
                st.sync();
                let bl = st.bl_now();
                crate::skill::castend::skill_castend_nodamage_id(c, &bl, &bl, SH_KI_SUL_RAMPAGE, st.sce.val1 as u16, tick, 1);
                st.reload();
                st.next(1000 + tick);
                return TimerFlow::Return;
            }
            TimerFlow::Break
        }
        SC_STAR_BURST => {
            if st.sce.val4 >= 0 {
                if let Some(src) = unit::get(c, st.sce.val2 as u32) {
                    if st.tid != 0 {
                        st.sync();
                        let bl = st.bl_now();
                        crate::skillunit::skill_unitsetting(c, &src, SKE_STAR_BURST, st.sce.val1 as u16, bl.x, bl.y, 0);
                        st.reload();
                    }
                }
            }
            TimerFlow::Break
        }
        _ => TimerFlow::Break,
    }
}
