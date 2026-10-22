//! status.cpp status_change_start_post_delay: the value setup `switch (type)`, first part (status.cpp:10815-11826,
//! "Permanent effects" .. SC_INVINCIBLE): each status's val1..val4, duration and first interval.

use super::consts::*;
use super::defs::*;
use super::sc::{get_sce, put_sce, sc_start, sc_start2, sc_start4, status_change_start, status_get_sc_interval, INFINITE_TICK};
use super::sc_clear::{status_change_clear_buffs, SCCB_BUFFS, SCCB_CHEM_PROTECT, SCCB_DEBUFFS};
use super::sc_ext::{clif_bossmapinfo, has_status_change, map_getmob_boss, pc_checkskill, sit_down, BOSS_INFO_NOT, ET_SWEAT};
use super::sc_st::{Flow, StartSt};
use crate::db::jobs::ids::{MAPID_FIRSTMASK, MAPID_NINJA};
use crate::db::skills::ids::*;
use crate::db::skills::{skill_get_blewcount, skill_get_max, skill_get_splash, skill_get_time, skill_get_time2};
use crate::unit;

/// `for (i = 0; i < MAX_DEVOTION; i++) if (sd->devotion[i] && (tsd = map_id2sd(sd->devotion[i])))
/// status_change_start(src, tsd, type, 10000, v1, v2, v3, v4, tick, flag);`: the status passes to the players this
/// player devotes (Devotion). `sd->devotion[i]` is read again at every step, as rAthena does.
#[allow(clippy::too_many_arguments)]
fn devotion_start(st: &StartSt, v1: i32, v2: i32, v3: i32, v4: i32, flag: u8) {
    let c = st.c;
    for i in 0..crate::skill::state::MAX_DEVOTION {
        let d = crate::skill::state::get(c, st.bl.id).devotion.get(i).copied().unwrap_or(0);
        if d == 0 {
            continue;
        }
        if let Some(tsd) = unit::get(c, d).filter(|u| u.kind == BL_PC) {
            status_change_start(c, st.src.as_ref(), &tsd, st.t, 10000, v1, v2, v3, v4, st.tick as i64, flag, 0);
        }
    }
}

/// Every status this part has a case for (a unit test checks the three parts against rAthena's case list).
pub const CASES: &[i32] = &[
    SC_AETERNA, SC_MODECHANGE, SC_WEIGHT50, SC_WEIGHT90, SC_BROKENWEAPON, SC_BROKENARMOR, SC_READYSTORM, SC_READYDOWN, SC_READYCOUNTER,
    SC_READYTURN, SC_DODGE, SC_PUSH_CART, SC_SPRITEMABLE, SC_CLAN_INFO, SC_DAILYSENDMAILCNT, SC_SOULATTACK, SC_KEEPING, SC_BARRIER,
    SC_DECREASEAGI, SC_INCREASEAGI, SC_ADORAMUS, SC_ENDURE, SC_AUTOBERSERK, SC_SIGNUMCRUCIS, SC_MAXIMIZEPOWER, SC_EDP, SC_POISONREACT,
    SC_MAGICROD, SC_KYRIE, SC_MAGICPOWER, SC_SACRIFICE, SC_ENCPOISON, SC_ELEMENTALCHANGE, SC_PROVIDENCE, SC_REFLECTSHIELD,
    SC_STRIPWEAPON, SC_STRIPSHIELD, SC_STRIPARMOR, SC_STRIPHELM, SC_AUTOSPELL, SC_VOLCANO, SC_VIOLENTGALE, SC_DELUGE, SC_SUITON,
    SC_ONEHAND, SC_TWOHANDQUICKEN, SC_MERC_QUICKEN, SC_DANCING, SC_ENSEMBLEFATIGUE, SC_RICHMANKIM, SC_DRUMBATTLE, SC_NIBELUNGEN,
    SC_SIEGFRIED, SC_WHISTLE, SC_ASSNCROS, SC_POEMBRAGI, SC_APPLEIDUN, SC_HUMMING, SC_DONTFORGETME, SC_FORTUNE, SC_SERVICE4U,
    SC_EXPLOSIONSPIRITS, SC_ASPDPOTION0, SC_ASPDPOTION1, SC_ASPDPOTION2, SC_ASPDPOTION3, SC_NOCHAT, SC_DPOISON, SC_STONE, SC_POISON,
    SC_BLEEDING, SC_BURNING, SC_KILLING_AURA, SC_WINKCHARM, SC_VOICEOFSIREN, SC_TOXIN, SC_DEATHHURT, SC_LEECHESEND, SC_PYREXIA,
    SC_VENOMBLEED, SC_MAGICMUSHROOM, SC_GRADUAL_GRAVITY, SC_ALL_STAT_DOWN, SC_DAMAGE_HEAL, SC_BOSSMAPINFO, SC_HIDING, SC_CHASEWALK,
    SC_CLOAKING, SC_SIGHT, SC_RUWACH, SC_SIGHTBLASTER, SC_AUTOGUARD, SC_DEFENDER, SC_TENSIONRELAX, SC_PARRYING, SC_WINDWALK,
    SC_JOINTBEAT, SC_BERSERK, SC_GOSPEL, SC_MARIONETTE, SC_MARIONETTE2, SC_SPIRIT, SC_REJECTSWORD, SC_MEMORIZE, SC_REGENERATION,
    SC_DEVOTION, SC_COMA, SC_CLOSECONFINE2, SC_KAITE, SC_KAUPE, SC_COMBO, SC_EARTHSCROLL, SC_RUN, SC_KAAHI, SC_BLESSING,
    SC_TRICKDEAD, SC_CONCENTRATE, SC_MAXOVERTHRUST, SC_OVERTHRUST, SC_ADRENALINE2, SC_ADRENALINE, SC_WEAPONPERFECTION,
    SC_CONCENTRATION, SC_ANGELUS, SC_IMPOSITIO, SC_MELTDOWN, SC_TRUESIGHT, SC_SUN_COMFORT, SC_MOON_COMFORT, SC_STAR_COMFORT,
    SC_QUAGMIRE, SC_GATLINGFEVER, SC_FLING, SC_PROVOKE, SC_AVOID, SC_DEFENCE, SC_BLOODLUST, SC_FLEET, SC_MINDBREAKER, SC_JAILED,
    SC_UTSUSEMI, SC_BUNSINJYUTSU, SC_CHANGE, SC_SWOO, SC_ARMOR, SC_ENCHANTARMS, SC_CRITICALWOUND, SC_MAGICMIRROR, SC_SLOWCAST,
    SC_ARMORCHANGE, SC_EXPBOOST, SC_JEXPBOOST, SC_PERIOD_RECEIVEITEM_2ND, SC_PERIOD_PLUSEXP_2ND, SC_SUFFRAGIUM, SC_INCHEALRATE,
    SC_DOUBLECAST, SC_KAIZEL, SC_MERC_FLEEUP, SC_MERC_ATKUP, SC_MERC_HITUP, SC_MERC_HPUP, SC_MERC_SPUP, SC_REBIRTH, SC_INVINCIBLE,
];

/// skill.hpp e_nibelungen_status RINGNBL_MAX.
const RINGNBL_MAX: u32 = 12;

/// `status->max_hp * n / d` with rAthena's types: uint32 * int is computed in uint32 (wrapping), then stored in an
/// int32.
fn max_hp_pct(max_hp: u32, n: i32, d: u32) -> i32 {
    (max_hp.wrapping_mul(n as u32) / d) as i32
}

/// The cases of this part. None: the status has no case here.
pub fn vals(st: &mut StartSt) -> Option<Flow> {
    let c = st.c;
    let bl_id = st.bl.id;
    let is_pc = st.is_pc();
    let t = st.t;
    match t {
        /* Permanent effects */
        SC_AETERNA | SC_MODECHANGE | SC_WEIGHT50 | SC_WEIGHT90 | SC_BROKENWEAPON | SC_BROKENARMOR | SC_READYSTORM | SC_READYDOWN | SC_READYCOUNTER
        | SC_READYTURN | SC_DODGE | SC_PUSH_CART | SC_SPRITEMABLE | SC_CLAN_INFO | SC_DAILYSENDMAILCNT | SC_SOULATTACK => {
            st.tick = INFINITE_TICK as i32;
        }
        SC_KEEPING | SC_BARRIER => {
            if let Some(mut u) = unit::get(c, bl_id) {
                let endtick = c.tick() + st.tick as i64;
                u.ud.attackabletime = endtick;
                u.ud.canact_tick = endtick;
                u.ud.canmove_tick = endtick;
                u.ud.endure_tick = endtick;
                unit::save(c, &u);
            }
        }
        SC_DECREASEAGI | SC_INCREASEAGI | SC_ADORAMUS => {
            if t == SC_ADORAMUS {
                // 1000% base chance to blind, but still can be resisted
                sc_start(c, st.src.as_ref(), &st.bl, SC_BLIND, 1000, st.val1, skill_get_time(st.scdb.skill_id, st.val1 as u16) as i64);
                if st.has(SC_ADORAMUS) {
                    return Some(Flow::Return(false)); //Adoramus can't refresh itself, but it can cause blind again
                }
            }
            st.val2 = 2 + st.val1; // Agi change
        }
        SC_ENDURE => {
            st.val2 = 7; // Hit-count [Celest]
            if st.flag & SCSTART_NOAVOID == 0
                && st.bl.kind & (BL_PC | BL_MER) != 0
                && !super::map_flag_gvg2(c, st.bl.map)
                && !crate::npc::mapflag(c, st.bl.map, crate::npc::MF_BATTLEGROUND)
                && st.val4 == 0
            {
                if is_pc {
                    devotion_start(st, st.val1, st.val2, st.val3, st.val4, SCSTART_NOAVOID | SCSTART_NOICON);
                } else if st.bl.kind == BL_MER {
                    // ((TBL_MER*)bl)->devotion_flag: the mercenary passes it to its master (mercenary.rs)
                    if let Some(tsd) = crate::mercenary::devotion_master(c, st.bl.id) {
                        status_change_start(c, st.src.as_ref(), &tsd, st.t, 10000, st.val1, st.val2, st.val3, st.val4, st.tick as i64, SCSTART_NOAVOID | SCSTART_NOICON, 0);
                    }
                }
            }
            if st.val4 != 0 {
                st.tick = INFINITE_TICK as i32;
            }
        }
        SC_AUTOBERSERK => {
            st.tick_time = 100; // Check to start/end provoke every interval
            st.tick = INFINITE_TICK as i32;
        }
        SC_SIGNUMCRUCIS => {
            st.val2 = 10 + 4 * st.val1; // Def reduction
            st.tick = INFINITE_TICK as i32;
            crate::clif::emotion(c, &st.bl_now(), ET_SWEAT);
        }
        SC_MAXIMIZEPOWER => {
            if st.bl.kind & BL_CONSUME != 0 {
                st.val2 = if st.tick > 0 { st.tick } else { 10000 };
                st.tick_time = st.val2; // SP consumption interval
                st.tick = INFINITE_TICK as i32; // Duration sent to the client should be infinite
            } else {
                // If unit cannot consume SP, the duration is fixed to 10 seconds
                st.tick = 10000;
            }
        }
        SC_EDP => {
            st.val2 = (st.val1 + 1) / 2 + 2; // Chance to Poison enemies.
            if is_pc {
                let poison_level = pc_checkskill(c, bl_id, GC_RESEARCHNEWPOISON);
                if poison_level > 0 {
                    st.tick += 30000; // Base of 30 seconds
                    st.tick += poison_level * 15 * 1000; // Additional 15 seconds per level
                }
            }
        }
        SC_POISONREACT => {
            st.val2 = st.val1 / 2; // Number of Envenom autocasts
            st.val3 = 50; // Chance to autocast Envenom on hit / Poison chance of attack after block
            st.val4 = 0; // 0: Poison Block Mode; 1: Damage Boost Mode
        }
        SC_MAGICROD => {
            st.val2 = st.val1 * 20; // SP gained
        }
        SC_KYRIE => {
            let max_hp = st.status().max_hp;
            if st.val4 != 0 {
                // Formulas for Praefatio
                st.val2 = (max_hp_pct(max_hp, st.val1 * 2 + 10, 100) as u32).wrapping_add((st.val4 * 2) as u32) as i32; //%Max HP to absorb
                st.val3 = 6 + st.val1; //Hits
            } else {
                // Formulas for Kyrie Eleison
                st.val2 = max_hp_pct(max_hp, st.val1 * 2 + 10, 100);
                st.val3 = st.val1 / 2 + 5;
            }
        }
        SC_MAGICPOWER => {
            st.val3 = 5 * st.val1; // Matk% increase
        }
        SC_SACRIFICE => {
            st.val2 = 5; // Lasts 5 hits
            st.tick = INFINITE_TICK as i32;
        }
        SC_ENCPOISON => {
            st.val2 = 250 + 50 * st.val1; // Poisoning Chance (2.5+0.5%) in 1/10000 rate
        }
        SC_ELEMENTALCHANGE => {
            // val1 : Element Lvl (if called by skill lvl 1, takes random value between 1 and 4)
            // val2 : Element (When no element, random one is picked)
            // val3 : 0 = called by skill 1 = called by script (fixed level)
            if st.val2 == 0 {
                st.val2 = (c.rnd() % ELE_ALL as u32) as i32;
            }
            if st.val1 == 1 && st.val3 == 0 {
                st.val1 = 1 + (c.rnd() % 4) as i32;
            } else if st.val1 > 4 {
                st.val1 = 4; // Max Level
            }
            st.val3 = 0; // Not need to keep this info.
        }
        SC_PROVIDENCE => {
            st.val2 = st.val1 * 5; // Race/Ele resist
        }
        SC_REFLECTSHIELD => {
            st.val2 = 10 + st.val1 * 3; // %Dmg reflected
            // val4 used to mark if reflect shield is an inheritance bonus from Devotion
            if st.flag & SCSTART_NOAVOID == 0 && st.bl.kind & (BL_PC | BL_MER) != 0 {
                if is_pc {
                    devotion_start(st, st.val1, st.val2, 0, 1, SCSTART_NOAVOID | SCSTART_NOICON);
                } else if st.bl.kind == BL_MER {
                    // ((TBL_MER*)bl)->devotion_flag: the mercenary passes it to its master (mercenary.rs)
                    if let Some(tsd) = crate::mercenary::devotion_master(c, st.bl.id) {
                        status_change_start(c, st.src.as_ref(), &tsd, st.t, 10000, st.val1, st.val2, 0, 1, st.tick as i64, SCSTART_NOAVOID | SCSTART_NOICON, 0);
                    }
                }
            }
        }
        SC_STRIPWEAPON => {
            if !is_pc {
                st.val2 = 25; // Watk reduction
            }
        }
        SC_STRIPSHIELD => {
            if !is_pc {
                st.val2 = 15; // Def reduction
            }
        }
        SC_STRIPARMOR => {
            if !is_pc {
                st.val2 = 40; // Vit reduction
            }
        }
        SC_STRIPHELM => {
            if !is_pc {
                st.val2 = 40; // Int reduction
            }
        }
        SC_AUTOSPELL => {
            // Val1 Skill LV of Autospell
            // Val2 Skill ID to cast
            // Val3 Max Lv to cast
            st.val4 = st.val1 * 2; // Chance of casting
        }
        SC_VOLCANO => {
            const ENCHANT_EFF: [i32; 5] = [10, 14, 17, 19, 20]; // Enchant addition
            let i = ((st.val1 - 1) % 5).max(0) as u8 as usize;
            st.val2 = 5 + st.val1 * 5; // ATK/MATK increase
            st.val3 = ENCHANT_EFF[i];
        }
        SC_VIOLENTGALE => {
            const ENCHANT_EFF: [i32; 5] = [10, 14, 17, 19, 20]; // Enchant addition
            let i = ((st.val1 - 1) % 5).max(0) as u8 as usize;
            st.val2 = st.val1 * 3; // Flee increase
            st.val3 = ENCHANT_EFF[i];
        }
        SC_DELUGE => {
            const DELUGE_EFF: [i32; 5] = [5, 9, 12, 14, 15]; // HP addition rate n/100
            const ENCHANT_EFF: [i32; 5] = [10, 14, 17, 19, 20]; // Enchant addition
            let i = ((st.val1 - 1) % 5).max(0) as u8 as usize;
            st.val2 = DELUGE_EFF[i]; // HP increase
            st.val3 = ENCHANT_EFF[i];
        }
        SC_SUITON => {
            let ninja = is_pc && super::data::sd_get(c, bl_id).map(|sd| (sd.class_ & MAPID_FIRSTMASK) == MAPID_NINJA).unwrap_or(false);
            if st.val2 == 0 || ninja {
                // No penalties.
                st.val2 = 0; // Agi penalty
                st.val3 = 0; // Walk speed penalty
                return Some(Flow::Continue);
            }
            st.val3 = 50;
            st.val2 = 3 * ((st.val1 + 1) / 3);
            if st.val1 > 4 {
                st.val2 -= 1;
            }
            //Suiton is a special case, stop effect is forced and only happens when target enters it
            let mut u = st.bl_now();
            if crate::battle::knockback::unit_blown_immune(c, &u, 0x1) == crate::battle::knockback::UB_KNOCKABLE {
                unit::stop_walking(c, &mut u, unit::USW_FIXPOS | unit::USW_FORCE_STOP, 0);
                unit::save(c, &u);
            }
        }
        SC_ONEHAND | SC_TWOHANDQUICKEN => {
            st.val2 = 300;
            if st.val1 > 10 {
                // For boss casted skills [Skotlex]
                st.val2 += 20 * (st.val1 - 10);
            }
        }
        SC_MERC_QUICKEN => {
            st.val2 = 300;
        }
        SC_DANCING => {
            // val1 : Skill ID + LV
            // val2 : Skill Group of the Dance.
            // val3 : Brings the skill_lv (merged into val1 here)
            // val4 : Partner
            if st.val1 == CG_MOONLIT as i32 {
                super::clif::status_change(c, &st.bl_now(), EFST_MOON, 1, st.tick as i64, 0, 0, 0);
            }
            st.val1 |= st.val3 << 16;
            st.val3 = st.tick / 1000; // Tick duration
            st.tick_time = 1000; // [GodLesZ] tick time
        }
        SC_ENSEMBLEFATIGUE => {
            st.val2 = 30; // Speed and ASPD penalty
        }
        SC_RICHMANKIM => {
            st.val2 = 10 + 10 * st.val1; // Exp increase bonus
        }
        SC_DRUMBATTLE => {
            st.val2 = 15 + st.val1 * 5; // Atk increase
            st.val3 = st.val1 * 15; // Def increase
        }
        SC_NIBELUNGEN => {
            st.val2 = (c.rnd() % RINGNBL_MAX) as i32; // See e_nibelungen_status
        }
        SC_SIEGFRIED => {
            st.val2 = st.val1 * 3; // Elemental Resistance
            st.val3 = st.val1 * 5; // Status ailment resistance
        }
        SC_WHISTLE => {
            st.val2 = 18 + 2 * st.val1; // Flee increase
            st.val3 = ((st.val1 + 1) / 2) * 10; // Perfect dodge increase
        }
        SC_ASSNCROS => {
            st.val2 = if st.val1 < 10 { st.val1 * 2 - 1 } else { 20 }; // ASPD increase
        }
        SC_POEMBRAGI => {
            st.val2 = 2 * st.val1; // Cast time reduction
            st.val3 = 3 * st.val1; // After-cast delay reduction
        }
        SC_APPLEIDUN => {
            st.val2 = if st.val1 < 10 { 9 + st.val1 } else { 20 }; // HP rate increase
            st.val3 = 2 * st.val1; // Potion recovery rate
        }
        SC_HUMMING => {
            st.val2 = 4 * st.val1; // Hit increase
        }
        SC_DONTFORGETME => {
            st.val2 = 1 + 30 * st.val1; // ASPD decrease
            st.val3 = 5 + 2 * st.val1; // Movement speed adjustment.
        }
        SC_FORTUNE => {
            st.val2 = st.val1 * 10; // Critical increase
        }
        SC_SERVICE4U => {
            st.val2 = if st.val1 < 10 { 9 + st.val1 } else { 20 }; // MaxSP percent increase
            st.val3 = 5 + st.val1; // SP cost reduction
        }
        SC_EXPLOSIONSPIRITS => {
            st.val2 = 75 + 25 * st.val1; // Cri bonus
        }
        SC_ASPDPOTION0 | SC_ASPDPOTION1 | SC_ASPDPOTION2 | SC_ASPDPOTION3 => {
            st.val2 = 50 * (2 + t - SC_ASPDPOTION0);
        }
        SC_NOCHAT => {
            // A hardcoded interval of 60 seconds is expected, as the time that SC_NOCHAT uses is defined by
            // mmocharstatus.manner, each negative point results in 1 minute with this status activated.
            // This is done this way because the message that the client displays is hardcoded, and only
            // shows how many minutes are remaining. [Panikon]
            st.tick = 60000;
            st.val1 = crate::conf::manner_system(); // Mute filters.
            if is_pc {
                super::clif::changemanner(c, bl_id);
                super::clif::updatestatus(c, bl_id, SP_MANNER);
            }
        }
        SC_DPOISON | SC_STONE | SC_POISON | SC_BLEEDING | SC_BURNING | SC_KILLING_AURA | SC_WINKCHARM | SC_VOICEOFSIREN => {
            if t == SC_DPOISON {
                // Lose 10/15% of your life as long as it doesn't brings life below 25%
                let status = st.status();
                if status.hp > status.max_hp / 4 {
                    let mut diff = (status.max_hp.wrapping_mul(if is_pc { 10 } else { 15 }) / 100) as i32;
                    if status.hp.wrapping_sub(diff as u32) < status.max_hp / 4 {
                        diff = status.hp.wrapping_sub(status.max_hp / 4) as i32;
                    }
                    super::hp::status_zap(c, bl_id, diff as i64, 0, 0);
                }
                // [[fallthrough]]
            }
            st.tick_time = status_get_sc_interval(t);
            st.val4 = st.tick - st.tick_time; // Remaining time
        }
        SC_TOXIN => {
            if st.val3 == 1 {
                // Target
                st.tick_time = status_get_sc_interval(t);
            } else {
                // Caster
                st.tick_time = 1000;
            }
            st.val4 = st.tick - st.tick_time; // Remaining time
        }
        SC_DEATHHURT => {
            if st.val3 != 1 {
                st.tick_time = status_get_sc_interval(t);
                st.val4 = st.tick - st.tick_time; // Remaining time
            }
        }
        SC_LEECHESEND => {
            if st.val3 != 0 {
                st.tick_time = status_get_sc_interval(t);
                st.val4 = st.tick - st.tick_time; // Remaining time
            }
        }
        SC_PYREXIA => {
            if st.val3 == 1 {
                // Target
                // Causes blind for duration of pyrexia, unreducable and unavoidable, but can be healed with e.g. green potion
                status_change_start(c, st.src.as_ref(), &st.bl, SC_BLIND, 10000, st.val1, 0, 0, 0, st.tick as i64, SCSTART_NOAVOID | SCSTART_NOTICKDEF | SCSTART_NORATEDEF, 0);
                st.tick_time = status_get_sc_interval(t);
                st.val4 = st.tick - st.tick_time; // Remaining time
            } else {
                // Caster
                st.val2 = 15; // CRIT % and ATK % increase
            }
        }
        SC_VENOMBLEED => {
            if st.val3 == 0 {
                // Caster
                st.val2 = 30; // Reflect damage % reduction
            }
        }
        SC_MAGICMUSHROOM => {
            if st.val3 == 1 {
                // Target
                st.tick_time = status_get_sc_interval(t);
                st.val4 = st.tick - st.tick_time; // Remaining time
            } else {
                // Caster
                st.val2 = 10; // After-cast delay % reduction
            }
        }
        SC_GRADUAL_GRAVITY => {
            st.val2 = 10 * st.val1;
            st.tick_time = status_get_sc_interval(t);
            st.val4 = st.tick - st.tick_time; // Remaining time
        }
        SC_ALL_STAT_DOWN => {
            st.val2 = 20 * st.val1;
            if st.val1 < skill_get_max(NPC_ALL_STAT_DOWN) {
                st.val2 -= 10;
            }
        }
        SC_DAMAGE_HEAL => match st.val1 {
            1 => st.val2 = BF_WEAPON,
            2 => st.val2 = BF_MAGIC,
            3 => {
                //TODO: Absorb MISC damage? Both WEAPON & MAGIC damage? Which is correct on level 3?
                st.val2 = BF_MISC;
            }
            _ => {}
        },
        SC_BOSSMAPINFO => {
            if !is_pc {
                return Some(Flow::Return(false));
            }
            // Search for Boss on this Map
            let bl = st.bl_now();
            let Some(boss_md) = map_getmob_boss(c, bl.map) else {
                // No MVP on this map
                clif_bossmapinfo(c, &bl, None, BOSS_INFO_NOT);
                return Some(Flow::Return(false));
            };
            st.val1 = boss_md.id as i32;
            st.tick_time = status_get_sc_interval(t);
            st.val4 = st.tick - st.tick_time; // Remaining time
        }
        SC_HIDING => {
            st.val2 = st.tick / 1000;
            st.tick_time = 1000; // [GodLesZ] tick time
            st.val3 = 0; // Unused, previously speed adjustment
            st.val4 = st.val1 + 3; // Seconds before SP substraction happen.
        }
        SC_CHASEWALK => {
            st.val2 = if st.tick > 0 { st.tick } else { 10000 }; // Interval at which SP is drained.
            st.val3 = 35 - 5 * st.val1; // Speed adjustment.
            if get_sce(c, bl_id, SC_SPIRIT).map(|e| e.val2 == SL_ROGUE as i32).unwrap_or(false) {
                st.val3 -= 40;
            }
            st.val4 = 10 + st.val1 * 2; // SP cost.
            if super::map_flag_gvg2(c, st.bl.map) || crate::npc::mapflag(c, st.bl.map, crate::npc::MF_BATTLEGROUND) {
                st.val4 *= 5;
            }
        }
        SC_CLOAKING => {
            if st.bl.kind & BL_CONSUME != 0 {
                st.val2 = if st.tick > 0 { st.tick } else { 10000 };
                st.tick_time = st.val2; // SP consumption interval
                st.tick = INFINITE_TICK as i32; // Duration sent to the client should be infinite
            } else {
                // If unit cannot consume SP, there are no walk penalties and the duration is fixed to 10 seconds
                st.val1 = 10;
                st.tick = 10000;
            }
            st.val3 = 0; // Unused, previously walk speed adjustment
            // val4&1 signals the presence of a wall.
            // val4&2 makes cloak not end on normal attacks [Skotlex]
            // val4&4 makes cloak not end on using skills
            let clone = st.bl.kind == BL_MOB && crate::mob::get(c, bl_id).map(|md| md.special_clone).unwrap_or(false);
            if is_pc || clone {
                // Standard cloaking.
                st.val4 |= super::sc_conf::pc_cloak_check_type() & 7;
            } else {
                st.val4 |= super::sc_conf::monster_cloak_check_type() & 7;
            }
        }
        SC_SIGHT | SC_RUWACH | SC_SIGHTBLASTER => {
            /* splash status */
            st.val3 = skill_get_splash(st.val2 as u16, st.val1 as u16); // Val2 should bring the skill-id.
            st.val2 = st.tick / 20;
            st.tick_time = 20; // [GodLesZ] tick time
        }
        SC_AUTOGUARD => {
            if st.flag & SCSTART_NOAVOID == 0 {
                st.val2 = 0;
                for i in 0..st.val1 {
                    let tt = 5 - (i / 2);
                    st.val2 += if tt < 0 { 1 } else { tt };
                }
                if st.bl.kind & (BL_PC | BL_MER) != 0 {
                    if is_pc {
                        devotion_start(st, st.val1, st.val2, 0, 0, SCSTART_NOAVOID | SCSTART_NOICON);
                    } else if st.bl.kind == BL_MER {
                        // ((TBL_MER*)bl)->devotion_flag: the mercenary passes it to its master (mercenary.rs)
                        if let Some(tsd) = crate::mercenary::devotion_master(c, st.bl.id) {
                            status_change_start(c, st.src.as_ref(), &tsd, st.t, 10000, st.val1, st.val2, 0, 0, st.tick as i64, SCSTART_NOAVOID | SCSTART_NOICON, 0);
                        }
                    }
                }
            }
        }
        SC_DEFENDER => {
            if st.flag & SCSTART_NOAVOID == 0 {
                st.val2 = 5 + 15 * st.val1; // Damage reduction
                st.val3 = 0; // Unused, previously speed adjustment
                st.val4 = 250 - 50 * st.val1; // Aspd adjustment
                if is_pc {
                    // See if there are devoted characters, and pass the status to them. [Skotlex]
                    devotion_start(st, st.val1, st.val2, st.val3, st.val4, SCSTART_NOAVOID);
                }
            }
        }
        SC_TENSIONRELAX => {
            if is_pc {
                sit_down(c, bl_id);
            }
            st.val2 = 12; // SP cost
            st.tick_time = 10000; // Decrease at 10secs intervals.
            st.val3 = st.tick / st.tick_time;
            st.tick = INFINITE_TICK as i32; // Duration sent to the client should be infinite
        }
        SC_PARRYING => {
            st.val2 = 20 + st.val1 * 3; // Block Chance
        }
        SC_WINDWALK => {
            st.val2 = (st.val1 + 1) / 2; // Flee bonus is 1/1/2/2/3/3/4/4/5/5
        }
        SC_JOINTBEAT => {
            if st.val2 & BREAK_NECK != 0 {
                sc_start2(c, st.src.as_ref(), &st.bl, SC_BLEEDING, 100, st.val1, st.val3, skill_get_time2(st.scdb.skill_id, st.val1 as u16) as i64);
            }
        }
        SC_BERSERK => {
            if st.val3 == SC__BLOODYLUST {
                sc_start(c, st.src.as_ref(), &st.bl, st.val3, 100, st.val1, st.tick as i64);
            } else {
                sc_start4(c, st.src.as_ref(), &st.bl, SC_ENDURE, 100, 10, 0, 0, 1, st.tick as i64);
            }
            // HP healing is performing after the calc_status call.
            // Val2 holds HP penalty
            if st.val4 == 0 {
                st.val4 = skill_get_time2(st.scdb.skill_id, st.val1 as u16);
            }
            if st.val4 == 0 {
                st.val4 = 10000; // Val4 holds damage interval
            }
            st.val3 = st.tick / st.val4; // val3 holds skill duration
            st.tick_time = st.val4; // [GodLesZ] tick time
        }
        SC_GOSPEL => {
            if st.val4 == BCT_SELF {
                // Self effect
                st.val2 = st.tick / 10000;
                st.tick_time = 10000; // [GodLesZ] tick time
                status_change_clear_buffs(c, bl_id, SCCB_BUFFS | SCCB_DEBUFFS | SCCB_CHEM_PROTECT); // Remove buffs/debuffs
            }
        }
        SC_MARIONETTE => {
            st.val3 = 0;
            st.val4 = 0;
            let (s, a, v, i, d, l) = match (is_pc, crate::pc::character_of(c, bl_id)) {
                (true, Some(ch)) => (ch.str as i32, ch.agi as i32, ch.vit as i32, ch.int as i32, ch.dex as i32, ch.luk as i32),
                _ => {
                    let b = super::status_get_base_status(c, &st.bl);
                    (b.str as i32, b.agi as i32, b.vit as i32, b.int_ as i32, b.dex as i32, b.luk as i32)
                }
            };
            st.val3 |= cap_value(s / 2, 0, 0xFF) << 16;
            st.val3 |= cap_value(a / 2, 0, 0xFF) << 8;
            st.val3 |= cap_value(v / 2, 0, 0xFF);
            st.val4 |= cap_value(i / 2, 0, 0xFF) << 16;
            st.val4 |= cap_value(d / 2, 0, 0xFF) << 8;
            st.val4 |= cap_value(l / 2, 0, 0xFF);
        }
        SC_MARIONETTE2 => {
            // Fetch caster information
            let psce = unit::get(c, st.val1 as u32).filter(|p| has_status_change(p.kind)).and_then(|p| get_sce(c, p.id, SC_MARIONETTE));
            let Some(psce) = psce else { return Some(Flow::Return(false)) };
            // Fetch target's stats
            let status2 = st.status(); // Battle status
            st.val3 = 0;
            st.val4 = 0;
            let max_stat = super::sc_conf::max_parameter(); // Cap to 99 (default)
            let f = |packed: i32, shift: i32, cur: i16| cap_value(((packed >> shift) & 0xFF).min(max_stat - cur as i32), 0, 0xFF);
            st.val3 |= f(psce.val3, 16, status2.str) << 16;
            st.val3 |= f(psce.val3, 8, status2.agi) << 8;
            st.val3 |= f(psce.val3, 0, status2.vit);
            st.val4 |= f(psce.val4, 16, status2.int_) << 16;
            st.val4 |= f(psce.val4, 8, status2.dex) << 8;
            st.val4 |= f(psce.val4, 0, status2.luk);
        }
        SC_SPIRIT => {
            //1st Transcendent Spirit works similar to Marionette Control
            if is_pc && st.val2 == SL_HIGH as i32 {
                let status2 = super::status_get_base_status(c, &st.bl);
                st.val3 = 0;
                st.val4 = 0;
                let lv = super::status_get_lv(c, &st.bl);
                let max_stat = if lv - 10 < 50 { lv - 10 } else { 50 };
                let f = |cur: i16| cap_value(0.max(max_stat - cur as i32), 0, 0xFF);
                st.val3 |= f(status2.str) << 16;
                st.val3 |= f(status2.agi) << 8;
                st.val3 |= f(status2.vit);
                st.val4 |= f(status2.int_) << 16;
                st.val4 |= f(status2.dex) << 8;
                st.val4 |= f(status2.luk);
            }
        }
        SC_REJECTSWORD => {
            st.val2 = 15 * st.val1; // Reflect chance
            st.val3 = 3; // Reflections
            st.tick = INFINITE_TICK as i32;
        }
        SC_MEMORIZE => {
            st.val2 = 5; // Memorized casts.
            st.tick = INFINITE_TICK as i32;
        }
        SC_REGENERATION => {
            if st.val1 == 1 {
                st.val2 = 2;
            } else {
                st.val2 = st.val1; // HP Regerenation rate: 200% 200% 300%
            }
            st.val3 = st.val1; // SP Regeneration Rate: 100% 200% 300%
            // If val4 comes set, this blocks regen rather than increase it.
        }
        SC_DEVOTION => {
            let Some(d_bl) = unit::get(c, st.val1 as u32) else { return Some(Flow::Continue) };
            if has_status_change(d_bl.kind) && !super::sc::load(c, d_bl.id).empty() {
                // Inherits status from source
                const TYPES: [i32; 4] = [SC_AUTOGUARD, SC_DEFENDER, SC_REFLECTSHIELD, SC_ENDURE];
                let m = st.bl.map;
                let mut i: i32 = if super::map_flag_gvg2(c, m) || crate::npc::mapflag(c, m, crate::npc::MF_BATTLEGROUND) { 2 } else { 3 };
                while i >= 0 {
                    let type2 = TYPES[i as usize];
                    if let Some(d) = get_sce(c, d_bl.id, type2) {
                        let dur = skill_get_time(crate::db::misc::status::status_db().get_skill(type2), d.val1 as u16) as i64;
                        let fl = if type2 == SC_DEFENDER { SCSTART_NOAVOID } else { SCSTART_NOAVOID | SCSTART_NOICON };
                        status_change_start(c, Some(&d_bl), &st.bl, type2, 10000, d.val1, 0, 0, (type2 == SC_REFLECTSHIELD) as i32, dur, fl, 0);
                    }
                    i -= 1;
                }
            }
        }
        SC_COMA => {
            // Coma. Sends a char to 1HP. If val2, do not zap sp
            let status = st.status();
            // status->hp - 1 / status->sp - 1 are uint32 expressions
            super::hp::status_zap(c, bl_id, status.hp.wrapping_sub(1) as i64, if st.val2 != 0 { 0 } else { status.sp.wrapping_sub(1) as i64 }, 0);
            return Some(Flow::Return(true));
        }
        SC_CLOSECONFINE2 => {
            let src2 = if st.val2 != 0 { unit::get(c, st.val2 as u32) } else { None };
            match src2 {
                Some(src2) if has_status_change(src2.kind) => {
                    match get_sce(c, src2.id, SC_CLOSECONFINE) {
                        None => {
                            // Start lock on caster.
                            st.val3 = 50; // Flee increase
                            sc_start4(c, Some(&src2), &src2, SC_CLOSECONFINE, 100, st.val1, 1, st.val3, 0, st.tick as i64 + 1000);
                        }
                        Some(mut sce2) => {
                            // Increase count of locked enemies and refresh time.
                            sce2.val2 += 1;
                            if sce2.timer != 0 {
                                crate::timer::delete(c, sce2.timer);
                            }
                            sce2.timer = crate::timer::add(c, c.tick() + st.tick as i64 + 1000, crate::timer::Func::StatusChangeTimer, src2.id, SC_CLOSECONFINE as i64);
                            put_sce(c, &sce2);
                        }
                    }
                }
                // Status failed.
                _ => return Some(Flow::Return(false)),
            }
        }
        SC_KAITE => {
            st.val2 = 1 + st.val1 / 5; // Number of bounces: 1 + skill_lv/5
        }
        SC_KAUPE => match st.val1 {
            1..=3 => {
                if st.val1 == 3 {
                    // 33*3 + 1 -> 100%
                    st.val2 += 1;
                }
                // 33, 66%
                st.val2 += 33 * st.val1;
                st.val3 = 1; // Dodge 1 attack total.
            }
            _ => {
                // Custom. For high level mob usage, higher level means more blocks. [Skotlex]
                st.val2 = 100;
                st.val3 = st.val1 - 2;
            }
        },
        SC_COMBO => {
            // val1: Skill ID
            // val2: When given, target (for autotargetting skills)
            // val3: When set, this combo time should NOT delay attack/movement
            // val3: If set to 2 this combo will delay ONLY attack
            // val3: TK: Last used kick
            // val4: TK: Combo time
            if st.val3 == 0 || st.val3 == 2 {
                if let Some(mut u) = unit::get(c, bl_id) {
                    st.tick += 300 * super::sc_conf::combo_delay_rate() / 100;
                    u.ud.attackabletime = c.tick() + st.tick as i64;
                    unit::save(c, &u);
                    if st.val3 == 0 {
                        crate::battle::unit_attack::unit_set_walkdelay(c, bl_id, c.tick(), st.tick as i64, 1, 0);
                    }
                }
            }
            st.val3 = 0;
            st.val4 = st.tick;
        }
        SC_EARTHSCROLL => {
            st.val2 = 11 - st.val1; // Chance to consume: 11-skill_lv%
        }
        SC_RUN => {
            //Store time at which you started running.
            let currenttick = c.tick();
            // Note: this int64 value is stored in two separate int32 variables (FIXME)
            st.val3 = (currenttick & 0x0000_0000_ffff_ffff) as i32;
            st.val4 = ((currenttick as u64 & 0xffff_ffff_0000_0000) >> 32) as i32;
            st.tick = INFINITE_TICK as i32;
        }
        SC_KAAHI => {
            st.val2 = 200 * st.val1; // HP heal
            st.val3 = 5 * st.val1; // SP cost
        }
        SC_BLESSING => {
            if is_pc || (!st.undead_flag && st.status().race as i32 != RC_DEMON) {
                st.val2 = st.val1;
            } else {
                st.val2 = 0; // 0 -> Half stat.
            }
        }
        SC_TRICKDEAD => {
            if let Some(mut u) = unit::get(c, bl_id) {
                u.vd.dead_sit = 1;
                unit::save(c, &u);
            }
            st.tick = INFINITE_TICK as i32;
        }
        SC_CONCENTRATE => {
            st.val2 = 2 + st.val1;
            if is_pc {
                // Store the card-bonus data that should not count in the %
                let pb = super::data::sd_get(c, bl_id).map(|sd| sd.indexed_bonus.param_bonus.clone()).unwrap_or_default();
                st.val3 = pb.get(1).copied().unwrap_or(0); // Agi
                st.val4 = pb.get(4).copied().unwrap_or(0); // Dex
            } else {
                st.val3 = 0;
                st.val4 = 0;
            }
        }
        SC_MAXOVERTHRUST => {
            st.val2 = 20 * st.val1; // Power increase
        }
        SC_OVERTHRUST | SC_ADRENALINE2 | SC_ADRENALINE | SC_WEAPONPERFECTION => {
            if t == SC_OVERTHRUST {
                // val2 holds if it was casted on self, or is bonus received from others
                st.val3 = if st.val2 != 0 {
                    5 * st.val1
                } else if st.val1 > 4 {
                    15
                } else if st.val1 > 2 {
                    10
                } else {
                    5
                }; // Power increase
            } else if t == SC_ADRENALINE2 || t == SC_ADRENALINE {
                st.val3 = if st.val2 != 0 { 300 } else { 200 }; // Aspd increase
            }
            if st.src_kind() == BL_PC && pc_checkskill(c, st.src_id(), BS_HILTBINDING) > 0 {
                st.tick += st.tick / 10; //If caster has Hilt Binding, duration increases by 10%
            }
        }
        SC_CONCENTRATION => {
            st.val2 = 5 + st.val1 * 2; // Batk/Watk Increase
            st.val4 = 5 + st.val1 * 2; // Def reduction
            st.val3 = 10 * st.val1; // Hit Increase
            sc_start(c, st.src.as_ref(), &st.bl, SC_ENDURE, 100, 1, st.tick as i64); // Level 1 Endure effect
        }
        SC_ANGELUS => {
            st.val2 = 5 * st.val1; // def increase
        }
        SC_IMPOSITIO => {
            st.val2 = 5 * st.val1; // WATK/MATK increase
        }
        SC_MELTDOWN => {
            st.val2 = 100 * st.val1; // Chance to break weapon
            st.val3 = 70 * st.val1; // Change to break armor
        }
        SC_TRUESIGHT => {
            st.val2 = 10 * st.val1; // Critical increase
            st.val3 = 3 * st.val1; // Hit increase
        }
        SC_SUN_COMFORT => {
            let s = st.status();
            st.val2 = (super::status_get_lv(c, &st.bl) + s.dex as i32 + s.luk as i32) / 2; // def increase
        }
        SC_MOON_COMFORT => {
            let s = st.status();
            st.val2 = (super::status_get_lv(c, &st.bl) + s.dex as i32 + s.luk as i32) / 10; // flee increase
        }
        SC_STAR_COMFORT => {
            let s = st.status();
            st.val2 = super::status_get_lv(c, &st.bl) + s.dex as i32 + s.luk as i32; // Aspd increase
        }
        SC_QUAGMIRE => {
            st.val2 = (if is_pc { 5 } else { 10 }) * st.val1; // Agi/Dex decrease.
        }
        // gs_something1 [Vicious]
        SC_GATLINGFEVER => {
            st.val2 = 20 * st.val1; // Aspd increase
            st.val3 = 20 + 10 * st.val1; // Atk increase
            st.val4 = 5 * st.val1; // Flee decrease
        }
        SC_FLING => {
            if is_pc {
                st.val2 = 0; // No armor reduction to players.
            } else {
                st.val2 = 5 * st.val1; // Def reduction
            }
            st.val3 = 5 * st.val1; // Def2 reduction
        }
        SC_PROVOKE => {
            if st.src.is_some() && st.src_kind() != BL_PC && st.val1 == 10 {
                st.val2 = 0; // 0% Atk increase
                st.val3 = 100; // 100% Def reduction
            } else {
                st.val2 = 2 + 3 * st.val1; // Atk increase
                st.val3 = 5 + 5 * st.val1; // Def reduction
            }
            // val4 signals autoprovoke.
        }
        SC_AVOID => {
            // Speed change rate.
            if st.bl.kind == BL_HOM {
                st.val2 = 40 * st.val1;
            } else {
                st.val2 = 10 * st.val1;
            }
        }
        SC_DEFENCE => {
            // Vit bonus for players / Def bonus for homunculus
            st.val2 = 5 + (5 * st.val1);
        }
        SC_BLOODLUST => {
            // Atk rate change
            st.val2 = 20 + (10 * st.val1);
            // Leech chance
            // It's actually 9 * level on both pre-re and renewal, despite the description
            st.val3 = 9 * st.val1;
            // Leech percent
            st.val4 = 20;
        }
        SC_FLEET => {
            st.val2 = 30 * st.val1; // Aspd change
            st.val3 = 5 + 5 * st.val1; // bAtk/wAtk rate change
        }
        SC_MINDBREAKER => {
            st.val2 = 20 * st.val1; // matk increase.
            st.val3 = 12 * st.val1; // mdef2 reduction.
        }
        SC_JAILED => {
            // Val1 is duration in minutes. Use INT_MAX to specify 'unlimited' time.
            // (map indexes: the module's map id stands for rAthena's mapindex everywhere, pc_setpos included)
            if is_pc {
                let mapindex = crate::pc::get(c, bl_id).and_then(|p| p.mapindex).unwrap_or(0) as i32;
                if mapindex != st.val2 {
                    let bl = st.bl_now();
                    // Current Coordinates
                    let pos = (bl.x as i32 & 0xFFFF) | ((bl.y as i32) << 16);
                    let map_idx = mapindex; // Current Map
                    // 1. Place in Jail (val2 -> Jail Map, val3 -> x, val4 -> y
                    // (pc_setpos takes int32 coordinates)
                    crate::script::buildins::script2::mapctl::setpos32(c, bl_id, st.val2 as u16, st.val3, st.val4, unit::CLR_TELEPORT);
                    // 2. Set restore point (val3 -> return map, val4 return coords
                    st.val3 = map_idx;
                    st.val4 = pos;
                } else if st.val3 == 0 || st.val3 == mapindex {
                    // Use save point.
                    if let Some(ch) = crate::pc::character_of(c, bl_id) {
                        st.val3 = crate::data::maps().id_of(&ch.save_map).map(|m| m as i32).unwrap_or(0);
                        st.val4 = (ch.save_x as i32 & 0xFFFF) | ((ch.save_y as i32) << 16);
                    }
                }
            }
        }
        SC_UTSUSEMI => {
            st.val2 = (st.val1 + 1) / 2; // Number of hits blocked
            st.val3 = skill_get_blewcount(NJ_UTSUSEMI, st.val1 as u16); // knockback value.
        }
        SC_BUNSINJYUTSU => {
            st.val2 = (st.val1 + 1) / 2; // Number of hits blocked
        }
        SC_CHANGE => {
            st.val2 = 30 * st.val1; // Vit increase
            st.val3 = 20 * st.val1; // Int increase
        }
        SC_SWOO => {
            if st.status().mode & MD_STATUSIMMUNE == MD_STATUSIMMUNE {
                st.tick /= 5; // !TODO: Reduce skill's duration. But for how long?
            }
        }
        SC_ARMOR => {
            // NPC_DEFENDER:
            st.val2 = 8; // Damage will be divided by this value
            // Attack requirements to be blocked:
            st.val3 = BF_LONG; // Range
            st.val4 = BF_WEAPON | BF_MISC; // Type
        }
        SC_ENCHANTARMS => {
            // Make sure the received element is valid.
            if st.val1 >= ELE_ALL {
                st.val1 %= ELE_ALL;
            } else if st.val1 < 0 {
                st.val1 = (c.rnd() % ELE_ALL as u32) as i32;
            }
        }
        SC_CRITICALWOUND => {
            // Level 1 ~ 5 & 6 ~ 10 has different duration
            // Level 6 ~ 10 use effect of level 1 ~ 5
            st.val1 = 1 + ((st.val1 - 1) % 5);
            st.val2 = 20 * st.val1; // Heal effectiveness decrease
        }
        SC_MAGICMIRROR | SC_SLOWCAST => {
            if t == SC_MAGICMIRROR {
                // Level 1 ~ 5 & 6 ~ 10 has different duration
                // Level 6 ~ 10 use effect of level 1 ~ 5
                st.val1 = 1 + ((st.val1 - 1) % 5);
                // [[fallthrough]]
            }
            st.val2 = 20 * st.val1; // Magic reflection/cast rate
        }
        SC_ARMORCHANGE => {
            if st.val2 == NPC_ANTIMAGIC as i32 {
                // Boost mdef
                st.val2 = -20;
                st.val3 = 20;
            } else {
                // Boost def
                st.val2 = 20;
                st.val3 = -20;
            }
            // Level 1 ~ 5 & 6 ~ 10 has different duration
            // Level 6 ~ 10 use effect of level 1 ~ 5
            st.val1 = 1 + ((st.val1 - 1) % 5);
            st.val2 *= st.val1; // 20% per level
            st.val3 *= st.val1;
        }
        SC_EXPBOOST | SC_JEXPBOOST | SC_PERIOD_RECEIVEITEM_2ND | SC_PERIOD_PLUSEXP_2ND => {
            if st.val1 < 1 {
                return Some(Flow::Return(false));
            }
        }
        SC_SUFFRAGIUM => {
            st.val2 = 5 + st.val1 * 5; // Speed cast decrease
        }
        SC_INCHEALRATE => {
            if st.val1 < 1 {
                st.val1 = 1;
            }
        }
        SC_DOUBLECAST => {
            st.val2 = 30 + 10 * st.val1; // Trigger rate
        }
        SC_KAIZEL => {
            st.val2 = 10 * st.val1; // % of life to be revived with
        }
        SC_MERC_FLEEUP | SC_MERC_ATKUP | SC_MERC_HITUP => {
            st.val2 = 15 * st.val1;
        }
        SC_MERC_HPUP | SC_MERC_SPUP => {
            st.val2 = 5 * st.val1;
        }
        SC_REBIRTH => {
            st.val2 = 20 * st.val1; // % of life to be revived with
        }
        SC_INVINCIBLE => {
            st.val2 = 100; // ATKpercent increase
            st.val3 = 50; // Speed increase
            st.val4 = 700; // ASPD increase
        }
        _ => return None,
    }
    Some(Flow::Continue)
}
