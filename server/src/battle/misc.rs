//! battle.cpp battle_calc_misc_attack (RENEWAL): "misc"-type attacks and skills (traps, Blitz Beat, Gospel, Zeny /
//! coin throws, Final Strike and the other fixed-formula damages) — the per-skill base damage, the split of
//! NK_SPLASHSPLIT skills, the misc hit check, card fixes, skill atk bonuses, the element table, the plant rule,
//! DAMAGE_DIV_FIX, the Ranger traps' weapon part, Zeny consumption, battle_calc_damage, the GvG / battleground rates,
//! the skill damage adjustments, bAbsorbDmgMaxHP and the reflect. Float arithmetic follows rAthena's exactly.

use super::bl;
use super::calc::{
    battle_apply_div_fix, battle_blewcount_bonus, battle_calc_weapon_attack, battle_do_reflect, battle_get_misc_element, battle_range_type, battle_skill_damage, is_infinite_defense, Damage,
};
use super::fix::{self, battle_attr_fix, battle_calc_cardfix, nk_has, Nk};
use super::*;
use crate::db::skills::*;
use crate::status::consts::*;
use crate::status::defs::*;
use crate::unit::Unit;
use crate::world::Ctx;

/// DAMAGE_DIV_FIX2: `if (div > 1) dmg *= div`.
fn damage_div_fix2(dmg: &mut i64, div: i32) {
    if div > 1 {
        *dmg *= div as i64;
    }
}

/// battle.cpp battle_calc_misc_attack (RENEWAL).
pub fn battle_calc_misc_attack(c: &Ctx, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16, mflag: i32) -> Damage {
    let mut md = Damage::default(); // DO NOT CONFUSE with md of mob_data!
    let sstatus = bl::status(c, src);
    let tstatus = bl::status(c, target);

    // Some initial values
    md.amotion = if skill_get_inf(skill_id) & INF_GROUND_SKILL as i32 != 0 { 0 } else { sstatus.amotion as i32 };
    md.dmotion = tstatus.dmotion as i32;
    md.div_ = skill_get_num(skill_id, skill_lv) as i16;
    md.blewcount = skill_get_blewcount(skill_id, skill_lv);
    md.dmg_lv = ATK_DEF;
    md.flag = BF_MISC | BF_SKILL;
    md.miscflag = mflag;

    let skill = skill_db().find(skill_id);
    let mut nk: Nk = skill.map(|s| s.nk).unwrap_or(0);
    let set_nk = |nk: &mut Nk, bit: u8| *nk |= 1u64 << bit;

    let sd = bl::sd(c, src);
    let tsd = bl::sd(c, target);

    if let Some(sd) = &sd {
        state::with(c, sd.aid, |b| b.arrow_atk = false);
        md.blewcount += battle_blewcount_bonus(sd, skill_id);
    }

    // int16 s_ele
    let s_ele = battle_get_misc_element(c, &md, src, target, skill_id, skill_lv) as i16 as i32;

    // Skill Range Criteria
    md.flag |= battle_range_type(c, src, target, skill_id, skill_lv);

    // Update Damage data based on skill
    if let Some(imp) = crate::skills::skill_impl(skill_id) {
        imp.modify_damage_data(c, &mut md, src, target, skill_lv);
    }

    match skill_id {
        TF_THROWSTONE => {
            md.damage = if sd.is_some() { 50 } else { 30 };
            md.flag |= BF_WEAPON;
        }
        NPC_KILLING_AURA => md.damage = 10000,
        HT_LANDMINE | MA_LANDMINE | HT_BLASTMINE | HT_CLAYMORETRAP => {
            // (int64)(skill_lv * dex * (3.0 + (float)lv / 100.0) * (1.0 + (float)int_ / 35.0))
            let base = skill_lv as i32 * sstatus.dex as i32;
            let lv = bl::lv_of(c, src);
            md.damage = (base as f64 * (3.0 + (lv as f32) as f64 / 100.0) * (1.0 + (sstatus.int_ as f32) as f64 / 35.0)) as i64;
            md.damage += md.damage * ((c.rnd() % 20) as i32 - 10) as i64 / 100;
            md.damage += sd.as_ref().map(|s| bl::checkskill(c, Some(s), RA_RESEARCHTRAP) * 40).unwrap_or(0) as i64;
        }
        HT_BLITZBEAT | SN_FALCONASSAULT => {
            // Blitz-beat Damage (uint16 skill)
            let skill: i32 = sd.as_ref().map(|s| bl::checkskill(c, Some(s), HT_STEELCROW)).unwrap_or(0) as u16 as i32;
            md.damage = (skill_lv as i32 * 20 + skill * 6 + ((sstatus.agi as i32 / 2) * 2) + ((sstatus.dex as i32 / 10) * 2)) as i64;
            if skill_id == SN_FALCONASSAULT {
                // Div fix of Blitzbeat
                damage_div_fix2(&mut md.damage, skill_get_num(HT_BLITZBEAT, 5));
                // Falcon Assault Modifier
                md.damage = md.damage * (150 + 70 * skill_lv as i64) / 100;
            }
        }
        NPC_SELFDESTRUCTION => md.damage = sstatus.hp as i64,
        NPC_SMOKING => md.damage = 3,
        NPC_EVILLAND => md.damage = crate::skill::misc::skill_calc_heal(c, src, Some(target), skill_id, skill_lv, false) as i64,
        PA_GOSPEL => {
            if mflag > 0 {
                md.damage = (c.rnd() % 4000) as i64 + 1500;
            } else {
                md.damage = (c.rnd() % 5000) as i64 + 3000;
                md.damage -= bl::def_of(c, target) as i64;
                md.damage -= tstatus.def2 as i64;
                if md.damage < 0 {
                    md.damage = 0;
                }
            }
        }
        GN_FIRE_EXPANSION_ACID => {
            // Official Renewal formula [helvetica]
            // damage = 7 * ((atk + matk)/skill level) * (target vit/100)
            // skill is a "forced neutral" type skill, it benefits from weapon element but final damage
            // is considered "neutral" for purposes of resistances
            let atk = battle_calc_weapon_attack(c, src, target, skill_id, skill_lv, 0);
            let matk = super::magic::battle_calc_magic_attack(c, src, target, skill_id, skill_lv, 0);
            // (skill_lv 0 divides by zero in rAthena; skill_attack never calls with level 0)
            let lv = (skill_lv as i64).max(1);
            let t = bl::status(c, target);
            md.damage = 7 * ((atk.damage / lv + matk.damage / lv) * t.vit as i64 / 100);
            // AD benefits from endow/element but damage is forced back to neutral
            md.damage = battle_attr_fix(c, Some(src), Some(target), md.damage, ELE_NEUTRAL, t.def_ele as i32, t.ele_lv as i32, 0);
            // (Fall through: the pre-renewal CR_ACIDDEMONSTRATION case is not compiled; the next line is `break`)
        }
        NJ_ZENYNAGE => {
            md.damage = skill_get_zeny(skill_id, skill_lv) as i64;
            if md.damage == 0 {
                md.damage = 2;
            }
            md.damage += c.rnd_value_i64(0, md.damage);
            // Specific to Boss Class
            if bl::class_type(c, target) == CLASS_BOSS {
                md.damage /= 3;
            }
            if tsd.is_some() {
                md.damage /= 2;
            }
        }
        KO_MUCHANAGE => {
            md.damage = skill_get_zeny(skill_id, skill_lv) as i64;
            if md.damage == 0 {
                md.damage = 10;
            }
            md.damage = c.rnd_value_i64(md.damage / 2, md.damage);
            if bl::checkskill(c, sd.as_ref(), NJ_TOBIDOUGU) == 0 {
                md.damage /= 2;
            }
            // Specific to Boss Class
            if bl::class_type(c, target) == CLASS_BOSS {
                md.damage /= 2;
            }
        }
        NJ_ISSEN => {
            // Official Renewal formula [helvetica]
            // base damage = currenthp + ((atk * currenthp * skill level) / maxhp)
            // final damage = base damage + ((mirror image count + 1) / 5 * base damage) - (edef + sdef)
            // modified def formula
            let atk = battle_calc_weapon_attack(c, src, target, skill_id, skill_lv, 0);
            let st = bl::status(c, src);
            // (a max_hp of 0 divides by zero in rAthena; the module keeps the division defined)
            md.damage = st.hp as i64 + (atk.damage * st.hp as i64 * skill_lv as i64) / (st.max_hp as i64).max(1);
            if let Some(e) = bl::sc(c, src).as_ref().and_then(|s| s.get(SC_BUNSINJYUTSU)) {
                // mirror image bonus only occurs if active (int16 i)
                let i = e.val2 as i16;
                if i > 0 {
                    md.div_ = -(i + 2); // mirror image count + 2
                    md.damage += (md.damage * (((i as i64 + 1) * 10) / 5)) / 10;
                }
            }
            // modified def reduction, final damage = base damage - (edef + sdef) (int16 totaldef)
            let t = bl::status(c, target);
            let totaldef = (t.def2 as i32 + bl::def_of(c, target) as i32) as i16;
            md.damage -= totaldef as i64;
            md.flag |= BF_WEAPON;
        }
        GS_FLING => {
            md.damage = match &sd {
                Some(sd) => sd.ch.job_level as i64,
                None => bl::lv_of(c, src) as i64,
            };
        }
        HVAN_EXPLOSION => {
            // [orn]
            md.damage = sstatus.max_hp as i64 * (50 + 50 * skill_lv as i64) / 100;
        }
        RA_CLUSTERBOMB | RA_FIRINGTRAP | RA_ICEBOUNDTRAP => {
            md.damage = (skill_lv as i32 * sstatus.dex as i32 + sstatus.int_ as i32 * 5) as i64;
            // RE_LVL_TMDMOD() (RENEWAL_LVDMG)
            let lv = bl::lv_of(c, src);
            if lv > 99 {
                md.damage = md.damage * 150 / 100 + md.damage * lv as i64 / 100;
            }
            let div = if skill_id == RA_CLUSTERBOMB { 50 } else { 100 };
            if let Some(sd) = &sd {
                let researchskill_lv = bl::checkskill(c, Some(sd), RA_RESEARCHTRAP);
                if researchskill_lv != 0 {
                    md.damage = md.damage * 20 * researchskill_lv as i64 / div;
                } else {
                    md.damage = 0;
                }
            } else {
                md.damage = md.damage * 200 / div;
            }
            set_nk(&mut nk, NK_IGNOREELEMENT);
            set_nk(&mut nk, NK_IGNOREFLEE);
            set_nk(&mut nk, NK_IGNOREDEFCARD);
        }
        // 'Eruption' damage
        NC_MAGMA_ERUPTION_DOTDAMAGE => md.damage = 800 + 200 * skill_lv as i64,
        NPC_MAGMA_ERUPTION_DOTDAMAGE => md.damage = 1000 * skill_lv as i64,
        GN_THORNS_TRAP => md.damage = (100 + 200 * skill_lv as i32 + sstatus.int_ as i32) as i64,
        RL_B_TRAP => {
            // kRO 2014-02-12: Damage: Caster's DEX, Target's current HP, Skill Level
            // dex * 10 + (skill_lv * 3 * status_get_hp(target)) / 100: uint32 arithmetic
            let right = (skill_lv as i32 * 3) as u32;
            let right = right.wrapping_mul(tstatus.hp) / 100;
            md.damage = ((sstatus.dex as i32 * 10) as u32).wrapping_add(right) as i64;
            if bl::has_mode(c, target, MD_STATUSIMMUNE) {
                md.damage /= 10;
            }
        }
        NPC_WIDESUCK => md.damage = (tstatus.max_hp.wrapping_mul(15) / 100) as i64,
        SU_SV_ROOTTWIST_ATK => md.damage = 100,
        SP_SOULEXPLOSION => md.damage = (tstatus.hp.wrapping_mul((20 + 10 * skill_lv as i32) as u32) / 100) as i64,
        SJ_NOVAEXPLOSING => {
            // (Base ATK + Weapon ATK) * Ratio
            md.damage = ((sstatus.batk + sstatus.rhw.atk as i32) * (200 + 100 * skill_lv as i32) / 100) as i64;
            // Additional Damage: max_hp / (6 - min(5, skill_lv)) + max_sp * (2 * skill_lv), uint32 arithmetic
            let m = 5.min(skill_lv as i32);
            let add = (sstatus.max_hp / (6 - m) as u32).wrapping_add(sstatus.max_sp.wrapping_mul((2 * skill_lv as i32) as u32));
            md.damage += add as i64;
        }
        NPC_CANE_OF_EVIL_EYE => md.damage = 15000,
        _ => {}
    }

    if nk_has(nk, NK_SPLASHSPLIT) {
        // Divide ATK among targets
        if mflag > 0 {
            md.damage /= mflag as i64;
        } else {
            log::error!("0 enemies targeted by {skill_id}:{}, divide per 0 avoided!", skill_get_name(skill_id));
        }
    }

    if !nk_has(nk, NK_IGNOREFLEE) {
        let tsc = bl::sc(c, target);
        let opt1 = target.sc.opt1;
        // Temp for "hit or no hit"
        let hit = if tsc.is_some() && opt1 != 0 && opt1 != OPT1_STONEWAIT && opt1 != OPT1_BURNING {
            true
        } else {
            let t = bl::status(c, target);
            let mut flee: i16 = t.flee;
            let mut hitrate: i16 = 0; // Default hitrate (RENEWAL)
            if conf::agi_penalty_type() != 0 && conf::agi_penalty_target() & target.kind != 0 {
                // unit_counttargeted: 256 max targets should be a sane max
                let attacker_count = target.ud.target_count as i32;
                if attacker_count >= conf::agi_penalty_count() {
                    if conf::agi_penalty_type() == 1 {
                        flee = (flee as i32 * (100 - (attacker_count - (conf::agi_penalty_count() - 1)) * conf::agi_penalty_num()) / 100) as i16;
                    } else {
                        // assume type 2: absolute reduction
                        flee = (flee as i32 - (attacker_count - (conf::agi_penalty_count() - 1)) * conf::agi_penalty_num()) as i16;
                    }
                    if flee < 1 {
                        flee = 1;
                    }
                }
            }
            hitrate = (hitrate as i32 + bl::status(c, src).hit as i32 - flee as i32) as i16;
            if let Some(sd) = &sd {
                // in Renewal hit bonus from Vultures Eye is not shown anymore in status window
                hitrate = (hitrate as i32 + bl::checkskill(c, Some(sd), AC_VULTURE)) as i16;
            }
            hitrate = (hitrate as i32).clamp(conf::min_hitrate(), conf::max_hitrate()) as i16;
            ((c.rnd() % 100) as i32) < hitrate as i32
        };
        if !hit {
            md.damage = 0;
            md.dmg_lv = ATK_FLEE;
        }
    }

    md.damage += battle_calc_cardfix(c, BF_MISC, src, target, nk, s_ele, 0, md.damage, 0, md.flag) as i64;

    // (int16 i)
    if let Some(sd) = &sd {
        let i = bl::skillatk_bonus(sd, skill_id) as i16;
        if i != 0 {
            md.damage += md.damage * i as i64 / 100;
        }
    }
    if let Some(tsd) = &tsd {
        let i = bl::sub_skillatk_bonus(tsd, skill_id) as i16;
        if i != 0 {
            md.damage -= md.damage * i as i64 / 100;
        }
    }

    if !nk_has(nk, NK_IGNOREELEMENT) {
        let t = bl::status(c, target);
        md.damage = battle_attr_fix(c, Some(src), Some(target), md.damage, s_ele, t.def_ele as i32, t.ele_lv as i32, 0);
    }

    // Plant damage
    if md.damage < 0 {
        md.damage = 0;
    } else if md.damage != 0 && is_infinite_defense(c, target, md.flag) {
        md.damage = 1;
    }

    // Apply DAMAGE_DIV_FIX and check for min damage
    battle_apply_div_fix(&mut md, skill_id);

    match skill_id {
        RA_FIRINGTRAP | RA_ICEBOUNDTRAP | RA_CLUSTERBOMB => {
            // RA_FIRINGTRAP / RA_ICEBOUNDTRAP break out on 1 damage, then fall through to RA_CLUSTERBOMB
            if skill_id == RA_CLUSTERBOMB || md.damage != 1 {
                let wd = battle_calc_weapon_attack(c, src, target, skill_id, skill_lv, mflag);
                md.damage += wd.damage;
            }
        }
        NJ_ZENYNAGE => {
            if let Some(sd) = &sd {
                let zeny = crate::pc::character_of(c, sd.aid).map(|ch| ch.zeny as i64).unwrap_or(sd.ch.zeny as i64);
                if md.damage > zeny {
                    md.damage = zeny;
                }
                // pc_payzeny(sd, static_cast<int32>(md.damage), LOG_TYPE_CONSUME)
                crate::inventory::pc_payzeny(c, sd.aid, md.damage as i32 as i64, crate::map_log::LOG_TYPE_CONSUME, 0);
            }
        }
        _ => {}
    }

    let d = md.damage;
    md.damage = fix::battle_calc_damage(c, src, target, &mut md, d, skill_id, skill_lv);
    if crate::npc::mapflag_gvg2(c, target.map) {
        md.damage = fix::battle_calc_gvg_damage(c, src, target, md.damage, skill_id, md.flag);
    } else if crate::npc::mapflag(c, target.map, crate::npc::MF_BATTLEGROUND) {
        md.damage = fix::battle_calc_bg_damage(c, src, target, md.damage, skill_id, md.flag);
    }

    // Skill damage adjustment
    let skill_damage = battle_skill_damage(c, src, Some(target), skill_id);
    if skill_damage != 0 {
        md.damage += md.damage * skill_damage as i64 / 100;
    }

    fix::battle_absorb_damage(c, target, &mut md);

    battle_do_reflect(c, BF_MISC, &mut md, src, target, skill_id, skill_lv); // WIP [lighta]

    md
}
