//! status.cpp status_change_start_post_delay: the value setup `switch (type)`, second part (status.cpp:11827-12520,
//! "General" SC_FEAR .. SC_ACTIVE_MONSTER_TRANSFORM): each status's val1..val4, duration and first interval.

use super::consts::*;
use super::defs::*;
use super::sc::{sc_start, status_change_start, status_get_sc_interval, INFINITE_TICK};
use super::sc_clear::{status_change_clear_buffs, SCCB_DEBUFFS, SCCB_REFRESH};
use super::sc_ext::*;
use super::sc_st::{Flow, StartSt};
use crate::db::skills::ids::*;
use crate::db::skills::{skill_get_max, skill_get_time2};

/// Every status this part has a case for (a unit test checks the three parts against rAthena's case list).
pub const CASES: &[i32] = &[
    SC_FEAR, SC_DEATHBOUND, SC_STONEHARDSKIN, SC_REFRESH, SC_MILLENNIUMSHIELD, SC_ABUNDANCE, SC_GIANTGROWTH, SC_LUXANIMA, SC_RENOVATIO,
    SC_SECRAMENT, SC_VENOMIMPRESS, SC_WEAPONBLOCKING, SC_OBLIVIONCURSE, SC_CLOAKINGEXCEED, SC_HALLUCINATIONWALK, SC_NPC_HALLUCINATIONWALK,
    SC_MARSHOFABYSS, SC_FREEZE_SP, SC_SPHERE_1, SC_SPHERE_2, SC_SPHERE_3, SC_SPHERE_4, SC_SPHERE_5, SC_SHAPESHIFT, SC_ELECTRICSHOCKER,
    SC_CRYSTALIZE, SC_MEIKYOUSISUI, SC_CAMOUFLAGE, SC_WUGDASH, SC__SHADOWFORM, SC__STRIPACCESSORY, SC__INVISIBILITY, SC__ENERVATION,
    SC__GROOMY, SC__LAZINESS, SC__UNLUCKY, SC__WEAKNESS, SC_GN_CARTBOOST, SC_PROPERTYWALK, SC_STRIKING, SC_WARMER, SC_HELLS_PLANT,
    SC_SWINGDANCE, SC_SYMPHONYOFLOVER, SC_MOONLITSERENADE, SC_RUSHWINDMILL, SC_ECHOSONG, SC_HARMONIZE, SC_DEEPSLEEP, SC_SIRCLEOFNATURE,
    SC_SONGOFMANA, SC_SATURDAYNIGHTFEVER, SC_GLOOMYDAY, SC_GLOOMYDAY_SK, SC_SITDOWN_FORCE, SC_BANANA_BOMB_SITDOWN, SC_DANCEWITHWUG,
    SC_LERADSDEW, SC_MELODYOFSINK, SC_BEYONDOFWARCRY, SC_UNLIMITEDHUMMINGVOICE, SC_REFLECTDAMAGE, SC_FORCEOFVANGUARD, SC_EXEEDBREAK,
    SC_PRESTIGE, SC_SHIELDSPELL_HP, SC_SHIELDSPELL_SP, SC_SHIELDSPELL_ATK, SC_BANDING, SC_MAGNETICFIELD, SC_INSPIRATION, SC_CRESCENTELBOW,
    SC_LIGHTNINGWALK, SC_GT_ENERGYGAIN, SC_GT_CHANGE, SC_GT_REVITALIZE, SC_PYROTECHNIC_OPTION, SC_HEATER_OPTION, SC_TROPIC_OPTION,
    SC_AQUAPLAY_OPTION, SC_COOLER_OPTION, SC_CHILLY_AIR_OPTION, SC_WIND_STEP_OPTION, SC_BLAST_OPTION, SC_WILD_STORM_OPTION,
    SC_PETROLOGY_OPTION, SC_SOLID_SKIN_OPTION, SC_CURSED_SOIL_OPTION, SC_UPHEAVAL_OPTION, SC_CIRCLE_OF_FIRE_OPTION, SC_WATER_SCREEN_OPTION,
    SC_FIRE_CLOAK_OPTION, SC_WATER_DROP_OPTION, SC_WIND_CURTAIN_OPTION, SC_STONE_SHIELD_OPTION, SC_TROPIC, SC_CHILLY_AIR, SC_WILD_STORM,
    SC_UPHEAVAL, SC_HEATER, SC_COOLER, SC_BLAST, SC_CURSED_SOIL, SC_PYROTECHNIC, SC_AQUAPLAY, SC_GUST, SC_PETROLOGY, SC_CIRCLE_OF_FIRE,
    SC_FIRE_CLOAK, SC_WATER_DROP, SC_WATER_SCREEN, SC_WIND_CURTAIN, SC_WIND_STEP, SC_STONE_SHIELD, SC_SOLID_SKIN, SC_WATER_BARRIER,
    SC_ZEPHYR, SC_TIDAL_WEAPON, SC_ROCK_CRUSHER, SC_ROCK_CRUSHER_ATK, SC_POWER_OF_GAIA, SC_TEARGAS, SC_TEARGAS_SOB, SC_STOMACHACHE,
    SC_PROMOTE_HEALTH_RESERCH, SC_ENERGY_DRINK_RESERCH, SC_KYOUGAKU, SC_KAGEMUSYA, SC_ZANGETSU, SC_GENSOU, SC_ANGRIFFS_MODUS,
    SC_GOLDENE_FERSE, SC_STONE_WALL, SC_OVERED_BOOST, SC_GRANITIC_ARMOR, SC_MAGMA_FLOW, SC_PYROCLASTIC, SC_TEMPERING, SC_GOLDENE_TONE,
    SC_PARALYSIS, SC_TOXIN_OF_MANDARA, SC_LIGHT_OF_REGENE, SC_PAIN_KILLER, SC_STYLE_CHANGE, SC_CBC, SC_EQC, SC_ASH, SC_FULL_THROTTLE,
    SC_REBOUND, SC_KINGS_GRACE, SC_TELEKINESIS_INTENSE, SC_OFFERTORIUM, SC_FRIGG_SONG, SC_FLASHCOMBO, SC_DARKCROW, SC_UNLIMIT,
    SC_MONSTER_TRANSFORM, SC_ACTIVE_MONSTER_TRANSFORM,
];

/// `sd ? sd->status.job_level : dflt`.
fn job_level_or(st: &StartSt, dflt: i32) -> i32 {
    if st.is_pc() {
        crate::pc::character_of(st.c, st.bl.id).map(|ch| ch.job_level as i32).unwrap_or(dflt)
    } else {
        dflt
    }
}

/// `val4 = tick / n; if (val4 < 1) val4 = 1; tick_time = n` style intervals: `val4 = tick / every`.
fn per(tick: i32, every: i32) -> i32 {
    tick / every
}

/// The GROOMY / GLOOMYDAY dismounts: `if (pc_isriding(sd)) pc_setriding(sd, 0); if (pc_isridingdragon(sd))
/// pc_setoption(sd, sd->sc.option&~OPTION_DRAGON);`.
fn dismount_riding_and_dragon(c: &crate::world::Ctx, aid: u32) {
    if option_of(c, aid) & OPTION_RIDING != 0 {
        pc_setriding(c, aid, 0);
    }
    if option_of(c, aid) & OPTION_DRAGON != 0 {
        crate::skilltree::pc_setoption(c, aid, option_of(c, aid) & !OPTION_DRAGON, 0);
    }
}

/// The cases of this part. None: the status has no case here.
pub fn vals(st: &mut StartSt) -> Option<Flow> {
    let c = st.c;
    let bl_id = st.bl.id;
    let is_pc = st.is_pc();
    let t = st.t;
    match t {
        /* General */
        SC_FEAR => {
            sc_start(c, st.src.as_ref(), &st.bl, SC_ANKLE, 100, 0, 2000);
        }
        /* Rune Knight */
        SC_DEATHBOUND => {
            st.val2 = 500 + 100 * st.val1;
        }
        SC_STONEHARDSKIN => {
            if super::hp::status_charge(c, bl_id, (st.status().hp / 5) as i64, 0) == 0 {
                // 20% of HP
                return Some(Flow::Return(false));
            }
            if is_pc {
                st.val1 = job_level_or(st, 0) * pc_checkskill(c, bl_id, RK_RUNEMASTERY) / 4; // DEF/MDEF Increase
            }
        }
        SC_REFRESH => {
            let max_hp = st.status().max_hp;
            super::hp::status_heal(c, bl_id, (max_hp.wrapping_mul(25) / 100) as i64, 0, 0, 1);
            status_change_clear_buffs(c, bl_id, SCCB_REFRESH);
        }
        SC_MILLENNIUMSHIELD => {
            let chance = (c.rnd() % 100) as i8;
            st.val2 = if chance < 20 {
                4
            } else if chance < 50 {
                3
            } else {
                2
            }; // Shield count
            st.val3 = 1000; // Shield HP
            crate::skill::state::clif_millenniumshield(c, &st.bl_now(), st.val2 as i16);
        }
        SC_ABUNDANCE => {
            st.val4 = per(st.tick, 10000);
            st.tick_time = 10000; // [GodLesZ] tick time
        }
        SC_GIANTGROWTH => {
            st.val2 = 30; // Damage success rate and STR increase
        }
        SC_LUXANIMA => {
            st.val2 = 15; // Storm Blast success %
            st.val3 = 30; // Damage/HP/SP % increase
        }
        /* Arch Bishop */
        SC_RENOVATIO => {
            st.val4 = per(st.tick, 5000);
            st.tick_time = 5000;
        }
        SC_SECRAMENT => {
            st.val2 = 10 * st.val1;
        }
        SC_VENOMIMPRESS => {
            st.val2 = 10 * st.val1;
        }
        SC_WEAPONBLOCKING => {
            st.val2 = 10 + 2 * st.val1; // Chance
            st.val4 = per(st.tick, 5000);
            st.tick_time = 5000; // [GodLesZ] tick time
        }
        SC_OBLIVIONCURSE => {
            if st.val3 != 0 {
                st.val4 = per(st.tick, 3000);
                st.tick_time = 3000; // [GodLesZ] tick time
            }
        }
        SC_CLOAKINGEXCEED => {
            st.val2 = (st.val1 + 1) / 2; // Hits
            st.val3 = (st.val1 - 1) * 10; // Walk speed
            if is_pc {
                st.val4 |= super::sc_conf::pc_cloak_check_type() & 7;
            } else {
                st.val4 |= super::sc_conf::monster_cloak_check_type() & 7;
            }
            st.tick_time = 1000; // [GodLesZ] tick time
        }
        SC_HALLUCINATIONWALK | SC_NPC_HALLUCINATIONWALK => {
            st.val2 = 50 * st.val1; // Evasion rate of physical attacks. Flee
            st.val3 = 10 * st.val1; // Evasion rate of magical attacks.
        }
        SC_MARSHOFABYSS => {
            if is_pc {
                st.val2 = 3 * st.val1; // AGI and DEX Reduction
            } else {
                // BL_MOB
                st.val2 = 6 * st.val1; // AGI and DEX Reduction
            }
            st.val3 = 10 * st.val1; // Movement Speed Reduction
        }
        SC_FREEZE_SP => {
            // val2 = sp drain per 10 seconds
            st.tick_time = 10000; // [GodLesZ] tick time
        }
        SC_SPHERE_1 | SC_SPHERE_2 | SC_SPHERE_3 | SC_SPHERE_4 | SC_SPHERE_5 => {
            if !is_pc {
                return Some(Flow::Return(false)); // Should only work on players.
            }
            st.val4 = per(st.tick, 1000).max(1);
            st.tick_time = 1000; // [GodLesZ] tick time
        }
        SC_SHAPESHIFT => match st.val1 {
            1 => st.val2 = ELE_FIRE,
            2 => st.val2 = ELE_EARTH,
            3 => st.val2 = ELE_WIND,
            4 => st.val2 = ELE_WATER,
            _ => {}
        },
        SC_ELECTRICSHOCKER | SC_CRYSTALIZE => {
            st.val4 = per(st.tick, 1000).max(1);
            st.tick_time = 1000; // [GodLesZ] tick time
        }
        SC_MEIKYOUSISUI => {
            st.val2 = st.val1 * 2; // % HP each sec
            st.val3 = st.val1; // % SP each sec
            st.val4 = per(st.tick, 1000).max(1);
            st.tick_time = 1000;
        }
        SC_CAMOUFLAGE => {
            st.val4 = per(st.tick, 1000);
            st.tick_time = 1000; // [GodLesZ] tick time
        }
        SC_WUGDASH => {
            //Store time at which you started running.
            let currenttick = c.tick();
            // Note: this int64 value is stored in two separate int32 variables (FIXME)
            st.val3 = (currenttick & 0x0000_0000_ffff_ffff) as i32;
            st.val4 = ((currenttick as u64 & 0xffff_ffff_0000_0000) >> 32) as i32;
            st.tick = INFINITE_TICK as i32;
        }
        SC__SHADOWFORM => {
            if crate::unit::get(c, st.val2 as u32).map(|u| u.kind == BL_PC).unwrap_or(false) {
                crate::skill::state::with(c, st.val2 as u32, |s| s.shadowform_id = bl_id);
            }
            st.val4 = per(st.tick, 1000);
            st.tick_time = 1000; // [GodLesZ] tick time
        }
        SC__STRIPACCESSORY => {
            if !is_pc {
                st.val2 = 20;
            }
        }
        SC__INVISIBILITY => {
            st.val2 = 50 - 10 * st.val1; // ASPD
            st.val3 = 20 * st.val1; // CRITICAL
            st.val4 = per(st.tick, 1000);
            st.tick = INFINITE_TICK as i32; // Duration sent to the client should be infinite
            st.tick_time = 1000; // [GodLesZ] tick time
        }
        SC__ENERVATION => {
            st.val2 = 20 + 10 * st.val1; // ATK Reduction
            if is_pc {
                let n = crate::skill::state::get(c, bl_id).spiritball as i32;
                crate::skill::state::pc_delspiritball(c, bl_id, n, 0);
                let s = crate::skill::state::get(c, bl_id);
                crate::skill::state::pc_delspiritcharm(c, bl_id, s.spiritcharm as i32, s.spiritcharm_type);
            }
        }
        SC__GROOMY => {
            st.val2 = 20 + 10 * st.val1; // ASPD
            st.val3 = 20 * st.val1; // HIT
            if is_pc {
                // Removes Animals
                dismount_riding_and_dragon(c, bl_id);
                if option_of(c, bl_id) & OPTION_WUG != 0 {
                    crate::skilltree::pc_setoption(c, bl_id, option_of(c, bl_id) & !OPTION_WUG, 0);
                }
                if option_of(c, bl_id) & OPTION_WUGRIDER != 0 {
                    crate::skilltree::pc_setoption(c, bl_id, option_of(c, bl_id) & !OPTION_WUGRIDER, 0);
                }
                if option_of(c, bl_id) & OPTION_FALCON != 0 {
                    crate::skilltree::pc_setoption(c, bl_id, option_of(c, bl_id) & !OPTION_FALCON, 0);
                }
                if crate::pc::character_of(c, bl_id).map(|ch| ch.pet_id > 0).unwrap_or(false) {
                    // pet_return_egg(sd, sd->pd) (sd->pd null: rAthena would dereference it; the port skips)
                    if let Some(pd) = crate::pet::data::of_master(c, bl_id) {
                        crate::pet::pet_return_egg(c, bl_id, pd.id);
                    }
                }
                // if( hom_is_active(sd->hd) ) hom_vaporize(sd, HOM_ST_ACTIVE) (homunculus.rs)
                crate::homunculus::vaporize_if_active(c, bl_id);
            }
        }
        SC__LAZINESS => {
            st.val2 = 10 + 10 * st.val1; // Cast Increase
            st.val3 = 10 * st.val1; // Flee Reduction
        }
        SC__UNLUCKY => {
            let rand_eff = match c.rnd() % 3 {
                1 => SC_BLIND,
                2 => SC_SILENCE,
                _ => SC_POISON,
            };
            st.val2 = 10 * st.val1; // Crit and Flee2 Reduction
            let v3 = if rand_eff == SC_POISON { st.src_id() as i32 } else { 0 };
            status_change_start(c, st.src.as_ref(), &st.bl, rand_eff, 10000, st.val1, 0, v3, 0, st.tick as i64, SCSTART_NOTICKDEF | SCSTART_NORATEDEF, 0);
        }
        SC__WEAKNESS => {
            st.val2 = 10 * st.val1;
            // Bypasses coating protection and MADO
            sc_start(c, st.src.as_ref(), &st.bl, SC_STRIPWEAPON, 100, st.val1, st.tick as i64);
            sc_start(c, st.src.as_ref(), &st.bl, SC_STRIPSHIELD, 100, st.val1, st.tick as i64);
        }
        SC_GN_CARTBOOST => {
            if st.val1 < 3 {
                st.val2 = 50;
            } else if st.val1 > 2 && st.val1 < 5 {
                st.val2 = 75;
            } else {
                st.val2 = 100;
            }
        }
        SC_PROPERTYWALK => {
            st.val3 = 0;
        }
        SC_STRIKING => {
            // val2 = watk bonus already calc
            st.val3 = 6 - st.val1; // spcost = 6 - level (lvl1:5 ... lvl 5: 1)
            st.val4 = per(st.tick, 1000);
            st.tick_time = 1000; // [GodLesZ] tick time
        }
        SC_WARMER => {
            st.val4 = per(st.tick, 3000);
            st.tick = INFINITE_TICK as i32; // Duration sent to the client should be infinite
            st.tick_time = 3000;
        }
        SC_HELLS_PLANT => {
            st.tick_time = status_get_sc_interval(t);
            st.val4 = st.tick - st.tick_time; // Remaining time
        }
        SC_SWINGDANCE => {
            st.val3 = 3 * st.val1 + st.val2; // Walk speed and aspd reduction.
        }
        SC_SYMPHONYOFLOVER => {
            st.val3 = 2 * st.val1 + st.val2 + job_level_or(st, 50) / 4; // MDEF Increase
        }
        SC_MOONLITSERENADE | SC_RUSHWINDMILL => {
            // MATK Increase / ATK Increase
            st.val3 = 4 + st.val1 * 3 + st.val2 + job_level_or(st, 50) / 5;
        }
        SC_ECHOSONG => {
            st.val3 = 6 * st.val1 + st.val2 + job_level_or(st, 50) / 4; // DEF Increase
        }
        SC_HARMONIZE => {
            st.val2 = 5 + 5 * st.val1;
        }
        SC_DEEPSLEEP => {
            st.val4 = per(st.tick, 2000);
            st.tick_time = 2000; // [GodLesZ] tick time
        }
        SC_SIRCLEOFNATURE => {
            st.val2 = 50 * st.val1; // HP recovery rate
        }
        SC_SONGOFMANA => {
            let max_sp = st.status().max_sp;
            let pct: u32 = if st.val1 <= 2 {
                10
            } else if st.val1 <= 4 {
                15
            } else {
                20
            };
            super::hp::status_heal(c, bl_id, 0, (max_sp.wrapping_mul(pct) / 100) as i64, 0, 1);
            st.val3 = 50 * st.val1;
        }
        SC_SATURDAYNIGHTFEVER => {
            if st.val4 == 0 {
                st.val4 = skill_get_time2(st.scdb.skill_id, st.val1 as u16);
            }
            if st.val4 == 0 {
                st.val4 = 3000;
            }
            st.val3 = st.tick / st.val4;
            st.tick_time = st.val4; // [GodLesZ] tick time
        }
        SC_GLOOMYDAY => {
            st.val2 = 20 + 5 * st.val1; // Flee reduction.
            st.val3 = 15 + 5 * st.val1; // ASPD reduction.
            if is_pc && ((c.rnd() % 100) as i32) < st.val1 {
                // (Skill Lv) %
                st.val4 = 1; // Reduce walk speed by half.
                dismount_riding_and_dragon(c, bl_id);
            }
        }
        SC_GLOOMYDAY_SK => {
            // Random number between [15 ~ (Voice Lesson Skill Level x 5) + (Skill Level x 10)] %.
            let lesson = if is_pc { pc_checkskill(c, bl_id, WM_LESSON) * 5 } else { 0 };
            let m = lesson + st.val1 * 10;
            // (C's int32 `%`, truncating like Rust's; a modulus of 0 would crash rAthena: `rnd() % 0`)
            st.val2 = 15 + if m != 0 { c.rnd() as i32 % m } else { 0 };
        }
        SC_SITDOWN_FORCE | SC_BANANA_BOMB_SITDOWN => {
            if is_pc && !pc_issit(c, bl_id) {
                sit_down(c, bl_id);
            }
        }
        SC_DANCEWITHWUG => {
            st.val3 = 5 * st.val1; // ASPD Increase
            st.val4 = 20 + 10 * st.val1; // Fixed Cast Time Reduction
        }
        SC_LERADSDEW => {
            st.val3 = 2 + 3 * st.val1 + (3 * st.val2).min(25); // MaxHP Increase
        }
        SC_MELODYOFSINK => {
            st.val2 = 10 * st.val1; // INT Reduction.
            st.val3 = 2 + 2 * st.val1; // MaxSP reduction
        }
        SC_BEYONDOFWARCRY => {
            st.val2 = 10 + 10 * st.val1; // STR Reduction
            st.val3 = 4 * st.val1; // MaxHP Reduction
        }
        SC_UNLIMITEDHUMMINGVOICE => {
            st.val3 = 4 * st.val1 + (3 * st.val2).min(15); // !TODO: What's the Lesson bonus?
        }
        SC_REFLECTDAMAGE => {
            st.val2 = 10 * st.val1; // Reflect reduction amount
            st.val4 = per(st.tick, 1000); // Number of SP cycles (duration)
            st.tick_time = 1000; // [GodLesZ] tick time
        }
        SC_FORCEOFVANGUARD => {
            st.val2 = 8 + 12 * st.val1; // Chance
            st.val3 = 5 + 2 * st.val1; // Max rage counters
            st.tick = INFINITE_TICK as i32; // Endless duration in the client
            st.tick_time = 10000; // [GodLesZ] tick time
        }
        SC_EXEEDBREAK => {
            st.val2 = 150 * st.val1;
            if is_pc {
                // Players
                let index = crate::inventory::st(c, bl_id).and_then(|s| s.equip_index.get(crate::inventory::EQI_HAND_R).copied()).unwrap_or(-1);
                if index >= 0 {
                    if let Some(it) = crate::inventory::inv_get(c, bl_id, index as usize) {
                        let data = crate::inventory::data_of(&it.item);
                        if data.r#type == crate::db::items::IT_WEAPON {
                            let lv = super::status_get_lv(c, &st.bl);
                            st.val2 += 15 * job_level_or(st, 0) + data.weight as i32 / 10 * data.weapon_level as i32 * lv / 100;
                        }
                    }
                }
            } else {
                // Monster
                st.val2 += 750;
            }
        }
        SC_PRESTIGE => {
            let s = st.status();
            let lv = super::status_get_lv(c, &st.bl);
            st.val2 = (s.int_ as i32 + s.luk as i32) * st.val1 / 20 * lv / 200 + st.val1; // Chance to evade magic damage.
            let defender = if is_pc { pc_checkskill(c, bl_id, CR_DEFENDER) } else { skill_get_max(CR_DEFENDER) };
            st.val3 = ((st.val1 * 15) + (10 * defender)) * lv / 100; // Defence added
        }
        SC_SHIELDSPELL_HP => {
            st.val2 = 5; // 5% HP every 3 seconds
            st.tick_time = status_get_sc_interval(t);
            st.val4 = st.tick - st.tick_time; // Remaining time
        }
        SC_SHIELDSPELL_SP => {
            st.val2 = 3; // 3% SP every 5 seconds
            st.tick_time = status_get_sc_interval(t);
            st.val4 = st.tick - st.tick_time; // Remaining time
        }
        SC_SHIELDSPELL_ATK => {
            st.val2 = 150; // WATK/MATK bonus
        }
        SC_BANDING => {
            st.val2 = if is_pc { skill_banding_count(c, bl_id) } else { 1 };
            st.tick_time = 5000; // [GodLesZ] tick time
        }
        SC_MAGNETICFIELD => {
            st.tick_time = 1000; // [GodLesZ] tick time
            st.val4 = st.tick / st.tick_time;
        }
        SC_INSPIRATION => {
            st.val2 = 40 * st.val1; // ATK/MATK
            st.val3 = 6 * st.val1; //All stat bonus
            st.val4 = per(st.tick, 5000);
            st.tick_time = 5000; // [GodLesZ] tick time
            status_change_clear_buffs(c, bl_id, SCCB_DEBUFFS); // Remove debuffs
        }
        SC_CRESCENTELBOW => {
            st.val2 = job_level_or(st, 50) / 2 + (50 + 5 * st.val1);
        }
        SC_LIGHTNINGWALK => {
            // [(Job Level / 2) + (40 + 5 * Skill Level)] %
            st.val1 = job_level_or(st, 2) / 2 + 40 + 5 * st.val1;
        }
        SC_GT_ENERGYGAIN => {
            st.val2 = 10 + 5 * st.val1; // Sphere gain chance.
        }
        SC_GT_CHANGE => {
            // Take note there is no def increase as skill desc says. [malufett]
            st.val2 = st.val1 * 8; // ATK increase
            st.val3 = st.status().agi as i32 * st.val1 / 60; // ASPD increase: [(Target AGI x Skill Level) / 60] %
        }
        SC_GT_REVITALIZE => {
            // Take note there is no vit, aspd, speed increase as skill desc says. [malufett]
            st.val2 = 2 * st.val1; // MaxHP: [(Skill Level * 2)]%
            st.val3 = st.val1 * 30 + 50; // Natural HP recovery increase: [(Skill Level x 30) + 50] %
            // The stat def is not shown in the status window and it is processed differently
            st.val4 = st.val1 * 20; // STAT DEF increase
        }
        SC_PYROTECHNIC_OPTION => {
            st.val2 = 60; // Eatk Renewal (Atk2)
        }
        SC_HEATER_OPTION => {
            st.val2 = 120; // Eatk Renewal (Atk2)
            st.val3 = ELE_FIRE; // Change into fire element.
        }
        SC_TROPIC_OPTION => {
            st.val2 = 180; // Eatk Renewal (Atk2)
            st.val3 = MG_FIREBOLT as i32;
        }
        SC_AQUAPLAY_OPTION => {
            st.val2 = 40;
        }
        SC_COOLER_OPTION => {
            st.val2 = 80;
            st.val3 = ELE_WATER; // Change into water element.
        }
        SC_CHILLY_AIR_OPTION => {
            st.val2 = 120; // Matk. Renewal (Matk1)
            st.val3 = MG_COLDBOLT as i32;
        }
        SC_WIND_STEP_OPTION => {
            st.val2 = 50; // % Increase speed and flee.
        }
        SC_BLAST_OPTION => {
            st.val2 = 20;
            st.val3 = ELE_WIND; // Change into wind element.
        }
        SC_WILD_STORM_OPTION => {
            st.val2 = MG_LIGHTNINGBOLT as i32;
        }
        SC_PETROLOGY_OPTION => {
            st.val2 = 5; //HP Rate bonus
            st.val3 = 50;
        }
        SC_SOLID_SKIN_OPTION => {
            st.val2 = 33; //% Increase DEF
        }
        SC_CURSED_SOIL_OPTION => {
            st.val2 = 10; //HP rate bonus
            st.val3 = ELE_EARTH; // Change into earth element.
        }
        SC_UPHEAVAL_OPTION => {
            st.val2 = 15; //HP rate bonus
            st.val3 = WZ_EARTHSPIKE as i32;
        }
        SC_CIRCLE_OF_FIRE_OPTION => {
            st.val2 = 300;
        }
        SC_WATER_SCREEN_OPTION => {
            st.tick_time = 10000;
        }
        SC_FIRE_CLOAK_OPTION | SC_WATER_DROP_OPTION | SC_WIND_CURTAIN_OPTION | SC_STONE_SHIELD_OPTION => {
            st.val2 = 100; // Elemental modifier.
        }
        SC_TROPIC | SC_CHILLY_AIR | SC_WILD_STORM | SC_UPHEAVAL | SC_HEATER | SC_COOLER | SC_BLAST | SC_CURSED_SOIL | SC_PYROTECHNIC | SC_AQUAPLAY
        | SC_GUST | SC_PETROLOGY | SC_CIRCLE_OF_FIRE | SC_FIRE_CLOAK | SC_WATER_DROP | SC_WATER_SCREEN | SC_WIND_CURTAIN | SC_WIND_STEP
        | SC_STONE_SHIELD | SC_SOLID_SKIN => {
            // The case chain with [[fallthrough]]: each group adds its part and falls into the next one.
            if matches!(t, SC_TROPIC | SC_CHILLY_AIR | SC_WILD_STORM | SC_UPHEAVAL) {
                st.val2 += 10;
            }
            if matches!(t, SC_TROPIC | SC_CHILLY_AIR | SC_WILD_STORM | SC_UPHEAVAL | SC_HEATER | SC_COOLER | SC_BLAST | SC_CURSED_SOIL) {
                st.val2 += 10;
            }
            if matches!(
                t,
                SC_TROPIC | SC_CHILLY_AIR | SC_WILD_STORM | SC_UPHEAVAL | SC_HEATER | SC_COOLER | SC_BLAST | SC_CURSED_SOIL | SC_PYROTECHNIC | SC_AQUAPLAY | SC_GUST | SC_PETROLOGY
            ) {
                st.val2 += 5;
                st.val3 += 9000;
            }
            st.val2 += 5;
            st.val3 += 1000;
            st.tick_time = st.val3; // [GodLesZ] tick time
        }
        SC_WATER_BARRIER => {
            st.val2 = 30; // Reductions. Atk2 and Flee1
        }
        SC_ZEPHYR => {
            st.val2 = 25; // Flee.
        }
        SC_TIDAL_WEAPON => {
            st.val2 = 20; // Increase Elemental's attack.
        }
        SC_ROCK_CRUSHER | SC_ROCK_CRUSHER_ATK | SC_POWER_OF_GAIA => {
            st.val2 = 33; //Def rate bonus/Speed rate reduction
            st.val3 = 20; //HP rate bonus
        }
        SC_TEARGAS => {
            st.val2 = (st.status().max_hp.wrapping_mul(5) / 100) as i32; // Drain 5% HP
            st.val4 = per(st.tick, 2000);
            st.tick_time = 2000;
        }
        SC_TEARGAS_SOB => {
            st.val4 = per(st.tick, 3000);
            st.tick_time = 3000;
        }
        SC_STOMACHACHE => {
            st.val2 = 8; // SP consume.
            st.val4 = per(st.tick, 10000);
            st.tick_time = 10000; // [GodLesZ] tick time
        }
        SC_PROMOTE_HEALTH_RESERCH => {
            //val1: 1 = Regular Potion, 2 = Thrown Potion
            //val2: 1 = Small Potion, 2 = Medium Potion, 3 = Large Potion
            //val3: MaxHP Increase By Fixed Amount
            if st.val1 == 1 {
                // If potion was normally used, take the user's BaseLv
                st.val3 = 1000 * st.val2 - 500 + super::status_get_lv(c, &st.bl) * 10 / 3;
            } else if st.val1 == 2 {
                // If potion was thrown at someone, take the thrower's BaseLv
                let lv = st.src.as_ref().map(|s| super::status_get_lv(c, s)).unwrap_or(0);
                st.val3 = 1000 * st.val2 - 500 + lv * 10 / 3;
            }
            if st.val3 <= 0 {
                // Prevents a negeative value from happening
                st.val3 = 0;
            }
        }
        SC_ENERGY_DRINK_RESERCH => {
            //val1: 1 = Regular Potion, 2 = Thrown Potion
            //val2: 1 = Small Potion, 2 = Medium Potion, 3 = Large Potion
            //val3: MaxSP Increase By Percentage Amount
            if st.val1 == 1 {
                // If potion was normally used, take the user's BaseLv
                st.val3 = super::status_get_lv(c, &st.bl) / 10 + 5 * st.val2 - 10;
            } else if st.val1 == 2 {
                // If potion was thrown at someone, take the thrower's BaseLv
                let lv = st.src.as_ref().map(|s| super::status_get_lv(c, s)).unwrap_or(0);
                st.val3 = lv / 10 + 5 * st.val2 - 10;
            }
            if st.val3 <= 0 {
                // Prevents a negeative value from happening
                st.val3 = 0;
            }
        }
        SC_KYOUGAKU => {
            // (C's int32 `%`, truncating like Rust's; rnd() % val1 with val1 0 would crash rAthena)
            st.val2 = 2 * st.val1 + if st.val1 != 0 { c.rnd() as i32 % st.val1 } else { 0 };
            super::clif::status_change(c, &st.bl_now(), EFST_ACTIVE_MONSTER_TRANSFORM, 1, 0, 1002, 0, 0);
        }
        SC_KAGEMUSYA => {
            st.val2 = 20; // Damage increase bonus
            st.val3 = st.val1 * 2;
            st.tick_time = 1000;
            st.val4 = st.tick / st.tick_time;
        }
        SC_ZANGETSU => {
            let s = st.status();
            let lv = super::status_get_lv(c, &st.bl);
            if s.hp % 2 == 0 {
                st.val2 = (lv / 3) + (20 * st.val1); //+Watk
            } else {
                st.val2 -= (lv / 3) + (30 * st.val1); //-Watk
            }
            if s.sp % 2 == 0 {
                st.val3 = (lv / 3) + (20 * st.val1); //+Matk
            } else {
                st.val3 -= (lv / 3) + (30 * st.val1); //-Matk
            }
        }
        SC_GENSOU => {
            let s = st.status();
            let hp = s.hp as i32;
            let mut lv = 5;
            // int16 per = 100 / (status_get_max_hp(bl) / hp): the divisions are unsigned (uint32 max_hp)
            let q = if hp != 0 { s.max_hp / hp as u32 } else { 0 };
            let pc = if q != 0 { (100u32 / q) as i16 } else { 0 };
            if pc <= 15 {
                lv = 1;
            } else if pc <= 30 {
                lv = 2;
            } else if pc <= 50 {
                lv = 3;
            } else if pc <= 75 {
                lv = 4;
            }
            if hp % 2 == 0 {
                super::hp::status_heal(c, bl_id, (hp * (6 - lv) * 4 / 100) as i64, (s.sp.wrapping_mul(((6 - lv) * 3) as u32) / 100) as i64, 0, 1);
            } else {
                super::hp::status_zap(c, bl_id, (hp * (lv * 4) / 100) as i64, (s.sp.wrapping_mul((lv * 3) as u32) / 100) as i64, 0);
            }
        }
        SC_ANGRIFFS_MODUS => {
            st.val2 = 50 + 20 * st.val1; // atk bonus
            st.val3 = 25 + 10 * st.val1; // Flee reduction.
            st.val4 = per(st.tick, 1000); // hp/sp reduction timer
            st.tick_time = 1000;
        }
        SC_GOLDENE_FERSE => {
            st.val2 = 10 + 10 * st.val1; // flee bonus
            st.val3 = 6 + 4 * st.val1; // Aspd Bonus
            st.val4 = 2 + 2 * st.val1; // Chance of holy attack
        }
        SC_STONE_WALL => {
            st.val2 = 100 * st.val1; // DEF bonus
            st.val3 = 30 * st.val1; // MDEF bonus
        }
        SC_OVERED_BOOST => {
            st.val2 = 400 + 40 * st.val1; // flee bonus
            st.val3 = 180 + 2 * st.val1; // aspd bonus
            st.val4 = 50; // def reduc %
        }
        SC_GRANITIC_ARMOR => {
            st.val2 = 2 * st.val1; // dmg reduction
            st.val3 = 6 * st.val1; // dmg taken on status end (6%:12%:18%:24%:30%)
            st.val4 = 5 * st.val1; // unknow formula
        }
        SC_MAGMA_FLOW => {
            st.val2 = 3 * st.val1; // Activation chance
        }
        SC_PYROCLASTIC => {
            st.val2 += 100 + 10 * st.val1; // atk bonus // !TODO: Confirm formula
        }
        SC_TEMPERING => {
            st.val2 += 5 + st.val1; // patk bonus
        }
        SC_GOLDENE_TONE => {
            st.val2 += 3 * st.val1; // res/mres bonus
        }
        SC_PARALYSIS => {
            // [Lighta] need real info
            st.val2 = 2 * st.val1; // def reduction
            st.val3 = 500 * st.val1; // varcast augmentation
        }
        SC_TOXIN_OF_MANDARA => {
            st.val2 = 15 * st.val1; // res reduction
        }
        SC_LIGHT_OF_REGENE => {
            // Yommy leak need confirm
            st.val2 = 20 * st.val1; // hp reco on death %
        }
        SC_PAIN_KILLER => {
            // Yommy leak need confirm
            let lv = st.src.as_ref().map(|s| super::status_get_lv(c, s)).unwrap_or(0);
            st.val2 = (((200 * st.val1) * lv) / 150).min(1000); // dmg reduction linear. upto a maximum of 1000 [iRO Wiki]
            if st.has(SC_PARALYSIS) {
                sc_start(c, st.src.as_ref(), &st.bl, SC_ENDURE, 100, st.val1, st.tick as i64); // Start endure for same duration
            }
        }
        SC_STYLE_CHANGE => {
            st.tick = INFINITE_TICK as i32; // Infinite duration
        }
        SC_CBC => {
            st.val3 = 10; // Drain sp % dmg
            st.val4 = per(st.tick, 1000); // dmg each sec
            st.tick = 1000;
        }
        SC_EQC => {
            st.val2 = 5 * st.val1; // def % reduc
            st.val3 = 2 * st.val1; // HP drain %
        }
        SC_ASH => {
            st.val2 = 0; // hit % reduc
            st.val3 = 0; // def % reduc
            st.val4 = 0; // atk flee % reduc
            let s = st.status();
            if s.mode & MD_STATUSIMMUNE != MD_STATUSIMMUNE {
                st.val2 = 50;
                if s.race as i32 == RC_PLANT {
                    // plant type
                    st.val3 = 50;
                }
                if s.def_ele as i32 == ELE_WATER {
                    // defense water type
                    st.val4 = 50;
                }
            }
        }
        SC_FULL_THROTTLE => {
            st.val2 = if st.val1 == 1 { 6 } else { 6 - st.val1 };
            st.val3 = 20; //+% AllStats
            st.tick_time = 1000;
            st.val4 = st.tick / st.tick_time;
        }
        SC_REBOUND => {
            st.tick_time = 2000;
            st.val4 = st.tick / st.tick_time;
            crate::clif::emotion(c, &st.bl_now(), ET_SWEAT);
        }
        SC_KINGS_GRACE => {
            st.val2 = 3 + st.val1; //HP Recover rate
            st.tick_time = 1000;
            st.val4 = st.tick / st.tick_time;
        }
        SC_TELEKINESIS_INTENSE => {
            st.val2 = 10 * st.val1; // sp consum / casttime reduc %
            st.val3 = 40 * st.val1; // magic dmg bonus
        }
        SC_OFFERTORIUM => {
            st.val2 = 30 * st.val1; // heal power bonus
            st.val3 = 100 + 20 * st.val1; // sp cost inc
        }
        SC_FRIGG_SONG => {
            st.val2 = 5 * st.val1; // maxhp bonus
            st.val3 = 80 + 20 * st.val1; // healing
            st.tick_time = 1000;
            st.val4 = st.tick / st.tick_time;
        }
        SC_FLASHCOMBO => {
            st.val2 = 20 * st.val1 + 20; // atk bonus
        }
        SC_DARKCROW => {
            st.val2 = 30 * st.val1; // ATK bonus
        }
        SC_UNLIMIT => {
            st.val2 = 50 * st.val1;
        }
        SC_MONSTER_TRANSFORM | SC_ACTIVE_MONSTER_TRANSFORM => {
            if crate::db::mobs::mobdb_checkid(st.val1) == 0 {
                st.val1 = crate::db::mobs::MOBID_PORING as i32; // Default poring
            }
        }
        _ => return None,
    }
    Some(Flow::Continue)
}
