//! skill.cpp skill_castfix, skill_vfcastfix (RENEWAL_CAST), skill_delayfix and pc.cpp pc_get_skillcooldown. The
//! float / double arithmetic follows rAthena's types (the `(float)` casts are f32 here, `time` is f64).
//! (skill_castfix_sc is the pre-RENEWAL_CAST path: not compiled in rAthena's Renewal build, so not ported.)

use crate::db::skills::*;
use crate::map::{BL_MOB, BL_NPC, BL_PC};
use crate::status::consts::*;
use crate::status::defs::{BF_MAGIC, ELE_WATER, ELE_WIND};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::Ctx;

use super::conf;

fn status(c: &Ctx, bl: &Unit) -> crate::status::data::StatusData {
    crate::status::status_get_status_data(c, bl)
}

/// sd->skill… item bonus of a skill (the first entry with that id), 0 without one.
fn item_bonus(list: &[crate::status::data::ItemBonus], skill_id: u16) -> Option<i32> {
    list.iter().find(|it| it.id == skill_id).map(|it| it.val)
}

/// skill.cpp skill_castfix: the skill's cast time (RENEWAL_CAST: only casting_rate applies here; the rest is
/// skill_vfcastfix at cast begin).
pub fn skill_castfix(_c: &Ctx, _bl: &Unit, skill_id: u16, skill_lv: u16) -> i32 {
    let mut time = skill_get_cast(skill_id, skill_lv) as f64;
    // config cast time multiplier
    if conf::casting_rate() != 100 {
        time = time * conf::casting_rate() as f64 / 100.0;
    }
    // return final cast time
    time = (time as i32).max(0) as f64;
    time as i32
}

/// SC_MEMORIZE's counter: `--sce->val2 <= 0` ends it.
fn memorize_tick(c: &Ctx, bl: &Unit) {
    if let Some(mut e) = sc::get_sce(c, bl.id, SC_MEMORIZE) {
        e.val2 -= 1;
        let left = e.val2;
        sc::put_sce(c, &e);
        if left <= 0 {
            sc::status_change_end(c, bl.id, SC_MEMORIZE, 0);
        }
    }
}

/// skill.cpp skill_vfcastfix (RENEWAL_CAST): variable and fixed cast time after statuses and item bonuses.
pub fn skill_vfcastfix(c: &Ctx, bl: &Unit, time: f64, skill_id: u16, skill_lv: u16) -> i32 {
    let mut time = time;
    if time < 0.0 {
        return 0;
    }
    if bl.kind == BL_MOB || bl.kind == BL_NPC {
        return time as i32;
    }
    let scl = crate::battle::bl::sc_nonempty(c, bl);
    let sd = if bl.kind == BL_PC { crate::status::data::sd_get(c, bl.id) } else { None };
    let mut fixed = skill_get_fixed_cast(skill_id, skill_lv);
    let mut fixcast_r: i32 = 0;
    let mut varcast_r: i32 = 0;
    let mut reduce_cast_rate: i32 = 0;
    let flag = skill_get_castnodex(skill_id);
    if fixed < 0 {
        if conf::default_fixed_castrate() > 0 {
            fixed = time as i32 * conf::default_fixed_castrate() / 100; // fixed time
            time = time * (100 - conf::default_fixed_castrate()) as f64 / 100.0; // variable time
        } else {
            fixed = 0;
        }
    }
    // Additive Variable Cast bonus adjustments by items
    if let Some(sd) = &sd {
        if flag & 4 == 0 {
            if sd.bonus.varcastrate != 0 {
                reduce_cast_rate += sd.bonus.varcastrate; // bonus bVariableCastrate
            }
            if sd.bonus.fixcastrate != 0 {
                fixcast_r -= sd.bonus.fixcastrate; // bonus bFixedCastrate
            }
            if sd.bonus.add_varcast != 0 {
                time += sd.bonus.add_varcast as f64; // bonus bVariableCast
            }
            if sd.bonus.add_fixcast != 0 {
                fixed += sd.bonus.add_fixcast; // bonus bFixedCast
            }
            if let Some(v) = item_bonus(&sd.lists.skillfixcast, skill_id) {
                fixed += v; // bonus2 bSkillFixedCast
            }
            if let Some(v) = item_bonus(&sd.lists.skillvarcast, skill_id) {
                time += v as f64; // bonus2 bSkillVariableCast
            }
            if let Some(v) = item_bonus(&sd.lists.skillcastrate, skill_id) {
                reduce_cast_rate += v; // bonus2 bVariableCastrate
            }
            if let Some(v) = item_bonus(&sd.lists.skillfixcastrate, skill_id) {
                fixcast_r = fixcast_r.max(v); // bonus2 bFixedCastrate
            }
        }
    }
    // VARCAST_REDUCTION(val): if ((varcast_r += val) != 0 && varcast_r >= 0) time *= 1 - (float)min(val, 100) / 100
    let varcast_reduction = |time: &mut f64, varcast_r: &mut i32, val: i32| {
        *varcast_r += val;
        if *varcast_r != 0 && *varcast_r >= 0 {
            *time *= (1.0f32 - val.min(100) as f32 / 100.0) as f64;
        }
    };
    // Adjusted by active statuses
    if let Some(s) = scl.as_ref().filter(|_| flag & 2 == 0) {
        // Multiplicative Variable CastTime values
        if s.has(SC_SLOWCAST) {
            varcast_reduction(&mut time, &mut varcast_r, -s.v2(SC_SLOWCAST));
        }
        if s.has(SC__LAZINESS) {
            varcast_reduction(&mut time, &mut varcast_r, -s.v2(SC__LAZINESS));
        }
        if s.has(SC_SUFFRAGIUM) {
            varcast_reduction(&mut time, &mut varcast_r, s.v2(SC_SUFFRAGIUM));
            // (#ifndef RENEWAL: status_change_end SC_SUFFRAGIUM — Renewal keeps it)
        }
        if s.has(SC_MEMORIZE) && (sd.is_none() || crate::skill::checkskill(c, bl.id, skill_id) > 0) {
            // Foresight only decreases cast times from learned skills, not skills granted by items
            reduce_cast_rate += 50;
            memorize_tick(c, bl);
        }
        if s.has(SC_POEMBRAGI) {
            reduce_cast_rate += s.v2(SC_POEMBRAGI);
        }
        if s.has(SC_IZAYOI) {
            varcast_reduction(&mut time, &mut varcast_r, 50);
        }
        if s.has(SC_WATER_INSIGNIA) && s.v1(SC_WATER_INSIGNIA) == 3 && skill_get_type(skill_id) == BF_MAGIC && skill_get_ele(skill_id, skill_lv) == ELE_WATER {
            varcast_reduction(&mut time, &mut varcast_r, 30); // Reduces 30% Variable Cast Time of magic Water spells.
        }
        if s.has(SC_TELEKINESIS_INTENSE) {
            varcast_reduction(&mut time, &mut varcast_r, s.v2(SC_TELEKINESIS_INTENSE));
        }
        if s.has(SC_SOULFAIRY) {
            varcast_reduction(&mut time, &mut varcast_r, s.v3(SC_SOULFAIRY));
        }
        // Multiplicative Fixed CastTime values
        if s.has(SC_SECRAMENT) {
            fixcast_r = fixcast_r.max(s.v2(SC_SECRAMENT));
        }
        if sd.is_some() {
            let radius = crate::skill::checkskill(c, bl.id, WL_RADIUS) as i32;
            // (rAthena assigns skill_lv = pc_checkskill(sd, WL_RADIUS) here; skill_lv is read again below)
            if radius != 0 && (WL_WHITEIMPRISON..=WL_FREEZE_SP).contains(&skill_id) {
                let st = status(c, bl);
                fixcast_r = fixcast_r.max((st.int_ as i32 + crate::status::status_get_lv(c, bl)) / 15 + radius * 5);
            }
        }
        // the assignment above changes skill_lv for SC_SWINGDANCE / SC_GLOOMYDAY below when the caster has Radius
        let skill_lv = if sd.is_some() { crate::skill::checkskill(c, bl.id, WL_RADIUS) as u16 } else { skill_lv };
        if s.has(SC_DANCEWITHWUG) {
            fixcast_r = fixcast_r.max(s.v4(SC_DANCEWITHWUG));
        }
        if s.has(SC_HEAT_BARREL) {
            fixcast_r = fixcast_r.max(s.v2(SC_HEAT_BARREL));
        }
        if s.has(SC_FREEZING) {
            fixcast_r -= 50;
        }
        if s.has(SC_SWINGDANCE) {
            fixcast_r = fixcast_r.max(skill_lv as i32 * 6);
        }
        // Additive Fixed CastTime values
        if s.has(SC_MANDRAGORA) {
            fixed += s.v1(SC_MANDRAGORA) * 500;
        }
        if s.has(SC_GUST_OPTION) || s.has(SC_BLAST_OPTION) || s.has(SC_WILD_STORM_OPTION) {
            fixed -= 1000;
        }
        if s.has(SC_IZAYOI) {
            fixed = 0;
        }
        if s.has(SC_GLOOMYDAY) {
            fixed += skill_lv as i32 * 500;
        }
    }
    if let Some(s) = &scl {
        if s.has(SC_SECRAMENT) && skill_id == HW_MAGICPOWER && flag & 2 != 0 {
            // Sacrament lowers Mystical Amplification cast time
            fixcast_r = fixcast_r.max(s.v2(SC_SECRAMENT));
        }
    }
    if varcast_r < 0 {
        time *= (1.0f32 - varcast_r.min(100) as f32 / 100.0) as f64;
    }
    // Apply Variable CastTime calculation by INT & DEX
    if flag & 1 == 0 {
        let st = status(c, bl);
        // (float)(dex * 2 + int) / vcast_stat_scale is float; the unqualified sqrt of <cmath> is C's
        // sqrt(double) on the oracle's GCC build (its map-server: cvtss2sd, sqrtsd, then 1.0 - x in double)
        let ratio = (st.dex as i32 * 2 + st.int_ as i32) as f32 / conf::vcast_stat_scale() as f32;
        time *= 1.0f64 - (ratio as f64).sqrt();
    }
    time *= (1.0f32 - reduce_cast_rate.min(100) as f32 / 100.0) as f64;
    // Underflow checking/capping: int + float * int is float arithmetic
    let t = (time as i32).max(0) as f32 + (1.0f32 - fixcast_r.min(100) as f32 / 100.0) * fixed.max(0) as f32;
    t as f64 as i32
}

/// skill.cpp skill_delayfix: the after-cast delay after dex / agi (off), statuses, item bonuses and delay_rate.
pub fn skill_delayfix(c: &Ctx, bl: &Unit, skill_id: u16, skill_lv: u16) -> i32 {
    if skill_id == SA_ABRACADABRA {
        return 0; // Will use picked skill's delay.
    }
    if bl.kind & conf::no_skill_delay() != 0 {
        return conf::min_skill_delay_limit() as i32;
    }
    let delaynodex = skill_get_delaynodex(skill_id);
    let mut time = skill_get_delay(skill_id, skill_lv) as f64;
    let st = status(c, bl);
    if time < 0.0 {
        time = -time + st.amotion as f64; // If set to <0, add to attack motion.
    }
    let scl = crate::battle::bl::sc_nonempty(c, bl);
    // Delay reductions
    match skill_id {
        // Monk combo skills have their delay reduced by agi/dex.
        MO_TRIPLEATTACK | MO_CHAINCOMBO | MO_COMBOFINISH | CH_TIGERFIST | CH_CHAINCRUSH | SR_DRAGONCOMBO | SR_FALLENEMPIRE | SJ_PROMINENCEKICK => {
            // If delay not specified, it will be 1000 - 4*agi - 2*dex
            if time == 0.0 {
                time = 1000.0;
            }
            time -= (4 * st.agi as i32 + 2 * st.dex as i32) as f64;
        }
        _ => {
            if conf::delay_dependon_dex() && delaynodex & 1 == 0 {
                // if skill delay is allowed to be reduced by dex
                let scale = conf::castrate_dex_scale() - st.dex as i32;
                if scale > 0 {
                    time = time * scale as f64 / conf::castrate_dex_scale() as f64;
                } else {
                    time = 0.0; // To be capped later to minimum.
                }
            }
            if conf::delay_dependon_agi() && delaynodex & 1 == 0 {
                // if skill delay is allowed to be reduced by agi
                let scale = conf::castrate_dex_scale() - st.agi as i32;
                if scale > 0 {
                    time = time * scale as f64 / conf::castrate_dex_scale() as f64;
                } else {
                    time = 0.0; // To be capped later to minimum.
                }
            }
        }
    }
    if let Some(s) = &scl {
        if let Some(e) = s.get(SC_SPIRIT) {
            match skill_id {
                CR_SHIELDBOOMERANG => {
                    if e.val2 == SL_CRUSADER as i32 {
                        time /= 2.0;
                    }
                }
                AS_SONICBLOW => {
                    if !crate::npc::mapflag_gvg2(c, bl.map) && !crate::npc::mapflag(c, bl.map, crate::npc::MF_BATTLEGROUND) && e.val2 == SL_ASSASIN as i32 {
                        time /= 2.0;
                    }
                }
                _ => {}
            }
        }
    }
    let mut delay: i32 = 0;
    if delaynodex & 2 == 0 {
        if let Some(s) = &scl {
            if s.has(SC_POEMBRAGI) {
                delay += s.v3(SC_POEMBRAGI);
            }
            if s.has(SC_WIND_INSIGNIA) && s.v1(SC_WIND_INSIGNIA) == 3 && skill_get_type(skill_id) == BF_MAGIC && skill_get_ele(skill_id, skill_lv) == ELE_WIND {
                delay += 50; // After Delay of Wind element spells reduced by 50%.
            }
            if s.has(SC_MAGICMUSHROOM) && s.v3(SC_MAGICMUSHROOM) == 0 {
                delay += s.v2(SC_MAGICMUSHROOM);
            }
        }
    }
    if delaynodex & 4 == 0 && bl.kind == BL_PC {
        if let Some(sd) = crate::status::data::sd_get(c, bl.id) {
            if sd.bonus.delayrate != 0 {
                delay += sd.bonus.delayrate; // bonus bDelayRate
            }
            if let Some(v) = item_bonus(&sd.lists.skilldelay, skill_id) {
                time += v as f64; // bonus2 bSkillDelay
            }
        }
    }
    if delay != 0 {
        time *= (1.0f32 - delay.min(100) as f32 / 100.0) as f64;
    }
    if conf::delay_rate() != 100 {
        time = time * conf::delay_rate() as f64 / 100.0;
    }
    (time as i32).max(0)
}

/// pc.cpp pc_get_skillcooldown: the skill's cooldown with bSkillCooldown.
pub fn pc_get_skillcooldown(c: &Ctx, aid: u32, skill_id: u16, skill_lv: u16) -> i32 {
    if skill_id == SJ_NOVAEXPLOSING && sc::has_sce(c, aid, SC_DIMENSION) {
        return 0;
    }
    let mut cooldown = skill_get_cooldown(skill_id, skill_lv);
    if skill_id == SU_TUNABELLY && crate::skill::checkskill(c, aid, SU_SPIRITOFSEA) > 0 {
        cooldown -= skill_get_time(SU_TUNABELLY, skill_lv);
    }
    if let Some(sd) = crate::status::data::sd_get(c, aid) {
        if let Some(v) = item_bonus(&sd.lists.skillcooldown, skill_id) {
            cooldown += v;
        }
    }
    cooldown.max(0)
}
