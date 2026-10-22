//! status.cpp status_change_start_post_delay: the value setup `switch (type)`, third part (status.cpp:12521-13019,
//! SC_EPICLESIS .. SC_OVERCOMING_CRISIS): each status's val1..val4, duration and first interval.
//!
//! Several cases read the source unit (`src->id`, `status_get_lv(src)`, …). rAthena's callers always pass one for
//! them (skills, thrown potions, scripts pass the player); a missing source reads as 0 here instead of crashing.

use super::consts::*;
use super::defs::*;
use super::sc::{sc_start, status_change_start, status_get_sc_interval, INFINITE_TICK};
use super::sc_ext::pc_checkskill;
use super::sc_st::{Flow, StartSt};
use crate::db::skills::ids::*;
use crate::db::skills::{skill_get_time, skill_get_time2};
use crate::unit::Unit;
use crate::world::Ctx;

/// Every status this part has a case for (a unit test checks the three parts against rAthena's case list).
pub const CASES: &[i32] = &[
    SC_EPICLESIS, SC_ILLUSIONDOPING, SC_OVERHEAT, SC_OVERHEAT_LIMITPOINT, SC_STEALTHFIELD, SC_STEALTHFIELD_MASTER, SC_VACUUM_EXTREME,
    SC_FIRE_INSIGNIA, SC_WATER_INSIGNIA, SC_WIND_INSIGNIA, SC_EARTH_INSIGNIA, SC_NEUTRALBARRIER, SC_MAGIC_POISON, SC_B_TRAP, SC_C_MARKER,
    SC_BURNT, SC_H_MINE, SC_HEAT_BARREL, SC_P_ALTER, SC_E_CHAIN, SC_ANTI_M_BLAST, SC_CATNIPPOWDER, SC_BITESCAR, SC_ARCLOUSEDASH, SC_SHRIMP,
    SC_FRESHSHRIMP, SC_TUNAPARTY, SC_HISS, SC_GROOMING, SC_CHATTERING, SC_SWORDCLAN, SC_ARCWANDCLAN, SC_GOLDENMACECLAN, SC_CROSSBOWCLAN,
    SC_JUMPINGCLAN, SC_ANCILLA, SC_HELPANGEL, SC_EMERGENCY_MOVE, SC_SUNSTANCE, SC_LUNARSTANCE, SC_STARSTANCE, SC_DIMENSION1,
    SC_DIMENSION2, SC_UNIVERSESTANCE, SC_NEWMOON, SC_FALLINGSTAR, SC_CREATINGSTAR, SC_LIGHTOFSUN, SC_LIGHTOFMOON, SC_LIGHTOFSTAR,
    SC_SOULGOLEM, SC_SOULSHADOW, SC_SOULFALCON, SC_SOULFAIRY, SC_SOULUNITY, SC_SOULDIVISION, SC_SOULREAPER, SC_SOULCOLLECT, SC_SP_SHA,
    SC_SERVANTWEAPON, SC_RELIEVE_ON, SC_VIGOR, SC_POWERFUL_FAITH, SC_FIRM_FAITH, SC_SINCERE_FAITH, SC_GUARD_STANCE, SC_GUARDIAN_S,
    SC_REBOUND_S, SC_ATTACK_STANCE, SC_HOLY_S, SC_MEDIALE, SC_A_VITA, SC_A_TELUM, SC_PRE_ACIES, SC_COMPETENTIA, SC_RELIGIO,
    SC_BENEDICTUM, SC_DANCING_KNIFE, SC_POTENT_VENOM, SC_D_MACHINE, SC_SHADOW_STRIP, SC_ABYSSFORCEWEAPON, SC_ABYSS_SLAYER, SC_WINDSIGN,
    SC_CALAMITYGALE, SC_GEF_NOCTURN, SC_AIN_RHAPSODY, SC_MUSICAL_INTERLUDE, SC_JAWAII_SERENADE, SC_PRON_MARCH, SC_SPELL_ENCHANTING,
    SC_FLAMETECHNIC, SC_FLAMEARMOR, SC_COLD_FORCE, SC_CRYSTAL_ARMOR, SC_GRACE_BREEZE, SC_EYES_OF_STORM, SC_EARTH_CARE,
    SC_STRONG_PROTECTION, SC_DEEP_POISONING, SC_POISON_SHIELD, SC_FLAMETECHNIC_OPTION, SC_COLD_FORCE_OPTION, SC_GRACE_BREEZE_OPTION,
    SC_EARTH_CARE_OPTION, SC_DEEP_POISONING_OPTION, SC_SUB_WEAPONPROPERTY, SC_WEAPONBREAKER, SC_INTENSIVE_AIM, SC_HIDDEN_CARD,
    SC_TALISMAN_OF_PROTECTION, SC_TALISMAN_OF_WARRIOR, SC_TALISMAN_OF_MAGICIAN, SC_T_FIFTH_GOD, SC_TALISMAN_OF_FIVE_ELEMENTS,
    SC_HEAVEN_AND_EARTH, SC_TEMPORARY_COMMUNION, SC_MARINE_FESTIVAL, SC_SANDY_FESTIVAL, SC_KI_SUL_RAMPAGE, SC_BLESSING_OF_M_CREATURES,
    SC_COLORS_OF_HYUN_ROK_BUFF, SC_STAR_BURST, SC_WILD_WALK, SC_OVERCOMING_CRISIS,
];

/// status.cpp status_base_matk_min / status_base_matk_max (RENEWAL) for any unit: `max` picks the maximum's monster
/// formula. Homunculi use the status_get_hom* formulas (homunculus/status.rs).
fn status_base_matk(c: &Ctx, u: &Unit, status: &super::data::StatusData, level: i32, max: bool) -> i32 {
    use crate::map::{BL_ELEM, BL_HOM, BL_MER, BL_MOB, BL_PET};
    match u.kind {
        BL_PET | BL_MOB | BL_MER | BL_ELEM => {
            let pct = if max { 130 } else { 70 };
            (status.int_ as i32 + level + status.rhw.matk as i32 * pct / 100) as u16 as i32
        }
        BL_HOM if max => crate::homunculus::status::status_base_matk_max_of(c, u.id, status, level) as i32,
        BL_HOM => crate::homunculus::status::status_base_matk_min_of(c, u.id, status, level) as i32,
        _ => super::calc::status_base_matk_pc(status, level) as i32,
    }
}

/// `sd ? sd->spiritball_old : dflt` (int8): the Rebellion coins used by the skill (the callers cast as rAthena does).
fn spiritball_old_or(st: &StartSt, dflt: i32) -> i32 {
    if st.is_pc() {
        crate::skill::state::get(st.c, st.bl.id).spiritball_old as i32
    } else {
        dflt
    }
}

/// The source's status_get_lv (0 without a source).
fn src_lv(c: &Ctx, st: &StartSt) -> i32 {
    st.src.as_ref().map(|s| super::status_get_lv(c, s)).unwrap_or(0)
}

/// The cases of this part. None: the status has no case here.
pub fn vals(st: &mut StartSt) -> Option<Flow> {
    let c = st.c;
    let bl_id = st.bl.id;
    let is_pc = st.is_pc();
    let t = st.t;
    match t {
        SC_EPICLESIS => {
            st.val2 = 5 * st.val1; //HP rate bonus
        }
        SC_ILLUSIONDOPING => {
            st.val2 = 50; // -Hit
        }
        SC_OVERHEAT | SC_OVERHEAT_LIMITPOINT | SC_STEALTHFIELD => {
            st.tick_time = st.tick;
            st.tick = INFINITE_TICK as i32;
        }
        SC_STEALTHFIELD_MASTER => {
            st.val3 = 2000 + 1000 * st.val1;
            st.tick_time = st.val3;
            st.val4 = st.tick / st.tick_time;
        }
        SC_VACUUM_EXTREME => {
            // Suck target at n second, only if the n second is lower than the duration
            // Does not suck targets on no-knockback maps
            if st.val4 < st.tick && crate::battle::knockback::unit_blown_immune(c, &st.bl_now(), 0x9) == crate::battle::knockback::UB_KNOCKABLE {
                st.tick_time = st.val4;
                st.val4 = st.tick - st.tick_time;
            } else {
                st.val4 = 0;
            }
        }
        SC_FIRE_INSIGNIA | SC_WATER_INSIGNIA | SC_WIND_INSIGNIA | SC_EARTH_INSIGNIA => {
            st.tick_time = 5000;
            st.val4 = st.tick / st.tick_time;
        }
        SC_NEUTRALBARRIER => {
            st.val2 = 10 + st.val1 * 5; // Def/Mdef
            st.tick = INFINITE_TICK as i32;
        }
        SC_MAGIC_POISON => {
            st.val2 = 50; // Attribute Reduction
        }
        /* Rebellion */
        SC_B_TRAP => {
            st.val2 = st.src_id() as i32;
            st.val3 = st.val1 * 25; // -movespeed TODO: Figure out movespeed rate
        }
        SC_C_MARKER | SC_BURNT => {
            // val1 = skill_lv
            // val2 = src_id
            st.val3 = 10; // -10 flee
            //Start timer to send mark on mini map
            st.val4 = st.tick / 1000;
            st.tick_time = 1000; // Sends every 1 seconds
        }
        SC_H_MINE => {
            st.val2 = st.src_id() as i32;
        }
        SC_HEAT_BARREL => {
            let n = spiritball_old_or(st, 10) as u8 as i32;
            //kRO Update 2016-05-25
            st.val2 = n * 5; // -fixed casttime
            st.val3 = (6 + st.val1 * 2) * n; // ATK
            st.val4 = 25 + st.val1 * 5; // -hit
        }
        SC_P_ALTER => {
            let n = spiritball_old_or(st, 10) as u8 as i32;
            st.val2 = 10 * n; // +atk
            st.val3 = (st.status().max_hp.wrapping_mul((st.val1 * 5) as u32) / 100) as i32; // Barrier HP
        }
        SC_E_CHAIN => {
            st.val2 = 10;
            if is_pc {
                st.val2 = spiritball_old_or(st, 10);
            }
        }
        SC_ANTI_M_BLAST => {
            st.val2 = st.val1 * 10;
        }
        SC_CATNIPPOWDER => {
            st.val2 = -50; // WATK%, MATK%
            st.val3 = 25 * st.val1; // Move speed reduction
            if is_pc && pc_checkskill(c, bl_id, SU_SPIRITOFLAND) != 0 {
                st.val4 = src_lv(c, st) / 12;
            }
        }
        SC_BITESCAR => {
            // Base Status of the source
            let b_status = st.src.as_ref().map(|s| super::status_get_base_status(c, s)).unwrap_or_default();
            let max_hp = st.status().max_hp;
            // (status_get_max_hp(bl) * (val1 + (dex / 25))) / status_get_max_hp(bl): MHP% damage (uint32)
            st.val2 = if max_hp != 0 { (max_hp.wrapping_mul((st.val1 + (b_status.dex as i32 / 25)) as u32) / max_hp) as i32 } else { 0 };
            st.tick_time = 1000;
            st.val4 = st.tick / st.tick_time;
        }
        SC_ARCLOUSEDASH => {
            st.val2 = 15 + 5 * st.val1; // AGI
            st.val3 = 25; // Move speed increase
            if is_pc && super::data::sd_get(c, bl_id).map(|sd| (sd.class_ & crate::db::jobs::ids::MAPID_FIRSTMASK) == crate::db::jobs::ids::MAPID_SUMMONER).unwrap_or(false) {
                st.val4 = 10; // Ranged ATK increase if the target is a Doram
            }
        }
        SC_SHRIMP => {
            st.val2 = 10; // BATK%, MATK%
        }
        SC_FRESHSHRIMP => {
            let mut min: i32 = 0;
            let mut max: i32 = 0;
            let ssd = st.src.as_ref().filter(|s| s.kind == BL_PC).map(|s| s.id);
            if let Some(src) = st.src.as_ref() {
                let sstatus = super::status_get_status_data(c, src);
                let lv = super::status_get_lv(c, src);
                min = status_base_matk(c, src, &sstatus, lv, false);
                max = status_base_matk(c, src, &sstatus, lv, true);
                if sstatus.rhw.matk > 0 {
                    let w_matk = sstatus.rhw.matk as i32;
                    let variance = w_matk * sstatus.rhw.wlv as i32 / 10;
                    min += w_matk - variance;
                    max += w_matk + variance;
                }
            }
            if let Some(sid) = ssd {
                let overrefine = super::data::sd_get(c, sid).map(|sd| sd.right_weapon.overrefine).unwrap_or(0);
                if overrefine > 0 {
                    min += 1;
                    max += overrefine as i32 - 1;
                }
            }
            st.val2 += min + 178; // Heal
            if max > min {
                st.val2 += (c.rnd() % (max - min) as u32) as i32; // Heal
            }
            if let Some(sid) = ssd {
                if pc_checkskill(c, sid, SU_POWEROFSEA) > 0 {
                    st.val2 += st.val2 * 10 / 100;
                    let src = st.src.clone().unwrap_or_else(|| st.bl.clone());
                    let w = super::calc_sc::Who { c, kind: BL_PC, id: sid, map: src.map, pc: None };
                    if super::calc::pc_checkskill_summoner(&w, SUMMONER_POWER_SEA) >= 20 {
                        st.val2 += st.val2 * 20 / 100;
                    }
                }
                if pc_checkskill(c, sid, SU_SPIRITOFSEA) > 0 {
                    st.val2 *= 2; // Doubles HP
                }
            }
            st.tick_time = 10000 - ((st.val1 - 1) * 1000);
            st.val4 = if st.tick_time != 0 { st.tick / st.tick_time } else { 0 };
        }
        SC_TUNAPARTY => {
            let src_max_hp = st.src.as_ref().map(|s| super::status_get_status_data(c, s).max_hp).unwrap_or(0);
            st.val2 = (src_max_hp.wrapping_mul((st.val1 * 10) as u32) / 100) as i32; // Shield according to the Caster's MaxHP%
            // map_session_data* ssd = BL_CAST(BL_PC, src); pc_checkskill(ssd, …) is 0 for a null ssd
            let ssd = st.src.as_ref().filter(|s| s.kind == BL_PC).map(|s| s.id);
            if ssd.map(|sid| pc_checkskill(c, sid, SU_SPIRITOFSEA) > 0).unwrap_or(false) {
                st.val2 *= 2; // Double the shield life if the caster has learned Spirit of Sea
            }
        }
        SC_HISS => {
            st.val2 = 50; // Perfect Dodge
            sc_start(c, st.src.as_ref(), &st.bl, SC_DORAM_WALKSPEED, 100, 50, skill_get_time2(SU_HISS, st.val1 as u16) as i64);
        }
        SC_GROOMING => {
            st.val2 = 100; // Flee
        }
        SC_CHATTERING => {
            st.val2 = 100; // eATK, eMATK
            sc_start(c, st.src.as_ref(), &st.bl, SC_DORAM_WALKSPEED, 100, 50, skill_get_time2(SU_CHATTERING, st.val1 as u16) as i64);
        }
        SC_SWORDCLAN | SC_ARCWANDCLAN | SC_GOLDENMACECLAN | SC_CROSSBOWCLAN | SC_JUMPINGCLAN => {
            st.tick = INFINITE_TICK as i32;
            status_change_start(c, st.src.as_ref(), &st.bl, SC_CLAN_INFO, 10000, 0, st.val2, 0, 0, INFINITE_TICK, st.flag, 0);
        }
        SC_ANCILLA => {
            st.val1 = 15; // Heal Power rate bonus
            st.val2 = 30; // SP Recovery rate bonus
        }
        SC_HELPANGEL => {
            st.tick_time = 1000;
            st.val4 = st.tick / st.tick_time;
        }
        SC_EMERGENCY_MOVE => {
            st.val2 = 25; // Movement speed increase
        }
        SC_SUNSTANCE => {
            st.val2 = 2 + st.val1; // ATK Increase
            st.tick = INFINITE_TICK as i32;
        }
        SC_LUNARSTANCE => {
            st.val2 = 2 + st.val1; // MaxHP Increase
            st.tick = INFINITE_TICK as i32;
        }
        SC_STARSTANCE => {
            st.val2 = 4 + 2 * st.val1; // ASPD Increase
            st.tick = INFINITE_TICK as i32;
        }
        SC_DIMENSION1 | SC_DIMENSION2 => {
            if is_pc {
                crate::skill::state::pc_addspiritball(c, bl_id, skill_get_time2(SJ_BOOKOFDIMENSION, 1), 2);
            }
        }
        SC_UNIVERSESTANCE => {
            st.val2 = 2 + st.val1; // All Stats Increase
            st.tick = INFINITE_TICK as i32;
        }
        SC_NEWMOON => {
            st.val2 = 7; // Number of Regular Attacks Until Reveal
            st.tick_time = 1000;
            st.val4 = st.tick / st.tick_time;
        }
        SC_FALLINGSTAR => {
            st.val2 = 8 + 2 * (1 + st.val1) / 2; // Autocast Chance
            if st.val1 >= 7 {
                st.val2 += 1; // Make it 15% at level 7.
            }
        }
        SC_CREATINGSTAR => {
            st.tick_time = 500;
            st.val4 = st.tick / st.tick_time;
        }
        SC_LIGHTOFSUN | SC_LIGHTOFMOON | SC_LIGHTOFSTAR => {
            st.val2 = 5 * st.val1; // Skill Damage Increase.
        }
        SC_SOULGOLEM => {
            st.val2 = 60 * st.val1; // DEF Increase
            st.val3 = 15 + 5 * st.val1; // MDEF Increase
        }
        SC_SOULSHADOW => {
            st.val2 = (1 + st.val1) / 2; // ASPD Increase
            st.val3 = 10 + 2 * st.val1; // CRIT Increase
        }
        SC_SOULFALCON => {
            st.val2 = 10 * st.val1; // WATK Increase
            st.val3 = 10; // HIT Increase
            if st.val1 >= 3 {
                st.val3 += 3;
            } else if st.val1 >= 5 {
                st.val3 += 5;
            }
        }
        SC_SOULFAIRY => {
            st.val2 = 10 * st.val1; // MATK Increase
            st.val3 = 5; // Variable Cast Time Reduction
            if st.val1 >= 3 {
                st.val3 += 2;
            } else if st.val1 >= 5 {
                st.val3 += 5;
            }
        }
        SC_SOULUNITY => {
            st.tick_time = 3000;
            st.val4 = st.tick / st.tick_time;
        }
        SC_SOULDIVISION => {
            st.val2 = 10 * st.val1; // Skill Aftercast Increase
        }
        SC_SOULREAPER => {
            st.val2 = 10 + 5 * st.val1; // Chance of Getting A Soul Sphere.
        }
        SC_SOULCOLLECT => {
            st.val2 = 5 + 3 * st.val2; // Max Soul Sphere's.
            st.val3 = if st.tick > 0 { st.tick } else { 60000 };
            st.tick_time = st.tick;
            st.tick = INFINITE_TICK as i32; // Duration sent to the client should be infinite
        }
        SC_SP_SHA => {
            st.val2 = 50; // Move speed reduction
        }
        SC_SERVANTWEAPON => {
            if is_pc {
                // Generate 5 servants on start
                crate::skill::state::pc_addservantball(c, bl_id, crate::skill::state::MAX_SERVANTBALL);
            }
            st.tick_time = skill_get_time2(DK_SERVANTWEAPON, st.val1 as u16); // Servant Regen Interval
            if st.tick_time < 500 {
                st.tick_time = 500; // Avoid being brought down to 0.
            }
            st.val4 = st.tick - st.tick_time; // Remaining Time
        }
        SC_RELIEVE_ON => {
            st.val2 = (10 * st.val1).min(99); // % damage received reduced from 10 * skill lvl up to 99%
        }
        SC_VIGOR => {
            st.val2 = 100 - 10 * (st.val1 - 1); // HP consumption with each attack is reduced by skill lvl
            st.val2 = st.val2.max(0);
        }
        SC_POWERFUL_FAITH => {
            st.val2 = 5 + 5 * st.val1; // ATK Increase
            st.val3 = 5 + 2 * st.val1; // PAtk Increase
        }
        SC_FIRM_FAITH => {
            st.val2 = 2 * st.val1; // MaxHP Increase
            st.val3 = 8 * st.val1; // Res Increase
        }
        SC_SINCERE_FAITH => {
            st.val2 = (1 + st.val1) / 2; // ASPD Increase
            st.val3 = 4 * st.val1; // Perfect Hit Increase
        }
        SC_GUARD_STANCE => {
            st.val2 = 50 + 50 * st.val1; // DEF Increase
            st.val3 = 50 * st.val1; // ATK Decrease
            st.tick = INFINITE_TICK as i32;
        }
        SC_GUARDIAN_S => {
            let s = st.status();
            // ( status->max_hp * 30 / 100 ) * ( 25 * val1 ) / 100 + 15 * status->sta: Barrier HP (uint32)
            st.val2 = ((s.max_hp.wrapping_mul(30) / 100).wrapping_mul((25 * st.val1) as u32) / 100).wrapping_add((15 * s.sta as i32) as u32) as i32;
        }
        SC_REBOUND_S => {
            st.val2 = 10 * st.val1; // Reduced Damage From Devotion
            if st.val2 > 99 {
                st.val2 = 99; // Lets not let it reduce above 99.
            }
        }
        SC_ATTACK_STANCE => {
            st.val2 = 40 * st.val1; // DEF Decrease
            st.val3 = 3 * st.val1; // P.ATK/S.MATK Increase
            st.tick = INFINITE_TICK as i32;
        }
        SC_HOLY_S => {
            st.val2 = 5 + 2 * st.val1; // Damage Reduction / Holy Damage Increase
        }
        SC_MEDIALE => {
            st.val2 = 2 * st.val1; // Heal Rate
            st.val4 = st.tick / 2000;
            st.tick_time = 2000;
        }
        SC_A_VITA | SC_A_TELUM => {
            st.val2 = 5 * st.val1; // Res/MRes Pierce Percentage
        }
        SC_PRE_ACIES => {
            st.val2 = 2 * st.val1; // CRate Increase
        }
        SC_COMPETENTIA => {
            st.val2 = 10 * st.val1; // PAtk/SMatk Increase - Unconfirmed if this is official formula but its 50 at Lv 5. [Rytech]
        }
        SC_RELIGIO | SC_BENEDICTUM => {
            st.val2 = 2 * st.val1; // Trait Stats Increase
        }
        SC_DANCING_KNIFE => {
            st.val4 = st.tick / 300;
            st.tick_time = 300;
        }
        SC_POTENT_VENOM => {
            st.val2 = 2 * st.val1; // Res Pierce Percentage
        }
        SC_D_MACHINE => {
            st.val2 = 200 + 50 * st.val1; // DEF Increase
            st.val3 = 20 * st.val1; // Res Increase
        }
        SC_SHADOW_STRIP => {
            if !is_pc {
                // Res/MRes on mobs only.
                st.val2 = 25; // Need official reduction amount.
            }
        }
        SC_ABYSSFORCEWEAPON => {
            if is_pc {
                // Generate 5 abyss spheres on start.
                crate::skill::state::pc_addabyssball(c, bl_id, crate::skill::state::MAX_ABYSSBALL);
            }
            st.tick_time = skill_get_time2(ABC_FROM_THE_ABYSS, st.val1 as u16); // Abyss Regen Interval
            if st.tick_time < 500 {
                st.tick_time = 500; // Avoid being brought down to 0.
            }
            st.val4 = st.tick - st.tick_time; // Remaining Time
        }
        SC_ABYSS_SLAYER => {
            st.val2 = 10 + 2 * st.val1; // PAtk/SMatk Increase
            st.val3 = 100 + 20 * st.val1; // Hit Increase
        }
        SC_WINDSIGN => {
            st.val2 = 8 + 6 * st.val1; // Chance to gain AP on attack.
            if st.val1 == 5 {
                st.val2 += 2; // Its 40% on level 5.
            }
        }
        SC_CALAMITYGALE => {
            // Unlimit runs along with this.
            sc_start(c, Some(&st.bl), &st.bl, SC_UNLIMIT, 100, 5, skill_get_time(RA_UNLIMIT, 5) as i64);
        }
        SC_GEF_NOCTURN | SC_AIN_RHAPSODY => {
            // MRes Reduction. Official formula unknown. / Res Reduction. Official formula unknown.
            st.val2 = 10 * st.val1; // Res/MRes Decrease
            if st.val3 & 2 != 0 {
                st.val2 *= 2; // Bonus if partner is found in party.
            }
        }
        SC_MUSICAL_INTERLUDE => {
            st.val2 = 5 + 5 * st.val1; // Res Increase
            if st.val3 & 2 != 0 {
                st.val2 *= 2; // Bonus if partner is found in party.
            }
        }
        SC_JAWAII_SERENADE => {
            st.val2 = 3 * st.val1; // SMatk Increase
            if st.val3 & 2 != 0 {
                st.val2 *= 2; // Bonus if partner is found in party.
            }
        }
        SC_PRON_MARCH => {
            st.val2 = 3 * st.val1; // PAtk Increase
            if st.val3 & 2 != 0 {
                st.val2 *= 2; // Bonus if partner is found in party.
            }
        }
        SC_SPELL_ENCHANTING => {
            st.val2 = 4 * st.val1; // SMatk Increase
        }
        SC_FLAMETECHNIC | SC_FLAMEARMOR | SC_COLD_FORCE | SC_CRYSTAL_ARMOR | SC_GRACE_BREEZE | SC_EYES_OF_STORM | SC_EARTH_CARE
        | SC_STRONG_PROTECTION | SC_DEEP_POISONING | SC_POISON_SHIELD => {
            st.val2 += 10;
            st.val3 += 10000;
            st.tick_time = st.val3;
        }
        SC_FLAMETECHNIC_OPTION => {
            st.val3 = ELE_FIRE;
        }
        SC_COLD_FORCE_OPTION => {
            st.val3 = ELE_WATER;
        }
        SC_GRACE_BREEZE_OPTION => {
            st.val3 = ELE_WIND;
        }
        SC_EARTH_CARE_OPTION => {
            st.val3 = ELE_EARTH;
        }
        SC_DEEP_POISONING_OPTION => {
            st.val3 = ELE_POISON;
        }
        SC_SUB_WEAPONPROPERTY => {
            if is_pc && st.val3 == ASC_EDP as i32 {
                let poison_level = pc_checkskill(c, bl_id, GC_RESEARCHNEWPOISON);
                if poison_level > 0 {
                    st.tick += 30000; // Base of 30 seconds
                    st.tick += poison_level * 15 * 1000; // Additional 15 seconds per level
                }
            }
        }
        SC_WEAPONBREAKER => {
            st.val2 = st.val1 * 2 * 100; // Chance to break weapon
        }
        SC_INTENSIVE_AIM => {
            st.tick = 500;
        }
        SC_HIDDEN_CARD => {
            st.val2 = 3 * st.val1;
            st.val3 = 10 * st.val1;
        }
        SC_TALISMAN_OF_PROTECTION => {
            // Heal value is static per cast of skill
            let bl = st.bl_now();
            let src = st.src.clone().unwrap_or_else(|| bl.clone());
            st.val3 = crate::skill::misc::skill_calc_heal(c, &src, Some(&bl), SOA_TALISMAN_OF_PROTECTION, st.val1 as u16, true);
            st.val4 = st.tick / 3000;
            // First heal tick applies on cast
            st.tick_time = 100;
        }
        SC_TALISMAN_OF_WARRIOR | SC_TALISMAN_OF_MAGICIAN => {
            st.val2 = 2 * st.val1;
        }
        SC_T_FIFTH_GOD => {
            st.val2 = 5 * st.val1;
        }
        SC_TALISMAN_OF_FIVE_ELEMENTS => {
            st.val2 = 4 * st.val1;
        }
        SC_HEAVEN_AND_EARTH => {
            st.val2 = 5 + 2 * st.val1;
        }
        SC_TEMPORARY_COMMUNION => {
            st.val2 = st.val1 * 3;
        }
        SC_MARINE_FESTIVAL | SC_SANDY_FESTIVAL => {
            st.val2 = 2 * st.val1;
        }
        SC_KI_SUL_RAMPAGE => {
            st.tick_time = 100;
            st.val4 = st.tick / 1000;
        }
        SC_BLESSING_OF_M_CREATURES => {
            st.val2 = st.val1 * 10;
        }
        SC_COLORS_OF_HYUN_ROK_BUFF => {
            st.val2 = 50;
        }
        SC_STAR_BURST => {
            st.tick_time = status_get_sc_interval(t);
            st.val4 = st.tick - st.tick_time; // Remaining time
        }
        SC_WILD_WALK => {
            st.val2 = (1 + st.val1 / 2) * 25;
            st.val3 = 50 + 50 * st.val1;
        }
        SC_OVERCOMING_CRISIS => {
            st.val2 = 3 * st.val1;
            st.val3 = 15000 * st.val1;
        }
        _ => return None,
    }
    Some(Flow::Continue)
}
