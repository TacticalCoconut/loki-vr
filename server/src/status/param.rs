//! pc.cpp pc_readparam (the parameters the status system computes) and pc_setparam, for the script engine's
//! C_PARAM variables (script/param.rs asks here for what it does not answer itself).

use super::consts::*;
use super::data;
use super::defs::*;
use crate::db::jobs::*;
use crate::world::Ctx;

/// pc_readparam for the calculated parameters (battle status, weight, bonuses, rates, special states …). None when
/// the parameter is not one of them (or the character has no status row).
pub fn readparam(c: &Ctx, aid: u32, typ: i64) -> Option<i64> {
    let sd = data::sd_get(c, aid)?;
    let ch = crate::pc::character_of(c, aid)?;
    let bs = &sd.battle_status;
    let b = &sd.bonus;
    let r = &sd.rates;
    let ss = &sd.special_state;
    let t = typ as i32;
    let v: i64 = match t {
        SP_BASESECOND => pc_mapid2jobid(sd.class_ & MAPID_SECONDMASK, ch.sex as i32) as i64, // Base job, extracting upper type.
        SP_UPPER => {
            if sd.class_ & JOBL_UPPER != 0 {
                1
            } else if sd.class_ & JOBL_BABY != 0 {
                2
            } else {
                0
            }
        }
        SP_BASEFIRST => pc_mapid2jobid(sd.class_ & MAPID_FIRSTMASK, ch.sex as i32) as i64, // Extract base class tree. [Skotlex]
        // sd->weight / sd->max_weight (the items system's pc_items row)
        SP_WEIGHT => crate::inventory::st(c, aid)?.weight as i64,
        SP_MAXWEIGHT => crate::inventory::st(c, aid)?.max_weight as i64,
        SP_NEXTBASEEXP => pc_nextbaseexp(ch.class, ch.base_level as u32).min(MAX_EXP) as i64,
        SP_NEXTJOBEXP => pc_nextjobexp(ch.class, ch.job_level as u32).min(MAX_EXP) as i64,
        SP_HP => bs.hp as i64,
        SP_MAXHP => bs.max_hp as i64,
        SP_SP => bs.sp as i64,
        SP_MAXSP => bs.max_sp as i64,
        SP_AP => bs.ap as i64,
        SP_MAXAP => bs.max_ap as i64,
        SP_PCDIECOUNTER => sd.die_counter as i64,
        // sd->killerrid / killedrid / killedgid (the mob port's pc_kill row, shared with script/param.rs)
        SP_KILLERRID | SP_KILLEDRID | SP_KILLEDGID => crate::mob::dead::kill_param(c, aid, t).unwrap_or(0),
        SP_CRITICAL => (bs.cri / 10) as i64,
        SP_ASPD => ((AMOTION_ZERO_ASPD - bs.amotion as i32) / AMOTION_INTERVAL) as i64,
        SP_BASE_ATK => b.eatk as i64, // RENEWAL
        SP_DEF1 => bs.def as i64,
        SP_DEF2 => bs.def2 as i64,
        SP_MDEF1 => bs.mdef as i64,
        SP_MDEF2 => bs.mdef2 as i64,
        SP_HIT => bs.hit as i64,
        SP_FLEE1 => bs.flee as i64,
        SP_FLEE2 => bs.flee2 as i64,
        SP_PATK => bs.patk as i64,
        SP_SMATK => bs.smatk as i64,
        SP_RES => bs.res as i64,
        SP_MRES => bs.mres as i64,
        SP_HPLUS => bs.hplus as i64,
        SP_CRATE => bs.crate_ as i64,
        SP_DEFELE => bs.def_ele as i64,
        SP_MAXHPRATE => r.hprate as i64,
        SP_MAXSPRATE => r.sprate as i64,
        SP_MAXAPRATE => r.aprate as i64,
        SP_SPRATE => r.dsprate as i64,
        SP_SPEED_RATE => b.speed_rate as i64,
        SP_SPEED_ADDRATE => b.speed_add_rate as i64,
        SP_ASPD_RATE => bs.aspd_rate2 as i64, // RENEWAL_ASPD
        SP_HP_RECOV_RATE => r.hprecov_rate as i64,
        SP_SP_RECOV_RATE => r.sprecov_rate as i64,
        SP_CRITICAL_DEF => b.critical_def as i64,
        SP_NEAR_ATK_DEF => b.near_attack_def_rate as i64,
        SP_LONG_ATK_DEF => b.long_attack_def_rate as i64,
        SP_DOUBLE_RATE => b.double_rate as i64,
        SP_DOUBLE_ADD_RATE => b.double_add_rate as i64,
        SP_MATK_RATE => r.matk_rate as i64,
        SP_ATK_RATE => b.atk_rate as i64, // RENEWAL
        SP_MAGIC_ATK_DEF => b.magic_def_rate as i64,
        SP_MISC_ATK_DEF => b.misc_def_rate as i64,
        SP_PERFECT_HIT_RATE => b.perfect_hit as i64,
        SP_PERFECT_HIT_ADD_RATE => b.perfect_hit_add as i64,
        SP_CRITICAL_RATE => r.critical_rate as i64,
        SP_HIT_RATE => r.hit_rate as i64,
        SP_FLEE_RATE => r.flee_rate as i64,
        SP_FLEE2_RATE => r.flee2_rate as i64,
        SP_DEF_RATE => r.def_rate as i64,
        SP_DEF2_RATE => r.def2_rate as i64,
        SP_MDEF_RATE => r.mdef_rate as i64,
        SP_MDEF2_RATE => r.mdef2_rate as i64,
        SP_PATK_RATE => r.patk_rate as i64,
        SP_SMATK_RATE => r.smatk_rate as i64,
        SP_RES_RATE => r.res_rate as i64,
        SP_MRES_RATE => r.mres_rate as i64,
        SP_HPLUS_RATE => r.hplus_rate as i64,
        SP_CRATE_RATE => r.crate_rate as i64,
        SP_RESTART_FULL_RECOVER => ss.restart_full_recover as i64,
        SP_NO_CASTCANCEL => ss.no_castcancel as i64,
        SP_NO_CASTCANCEL2 => ss.no_castcancel2 as i64,
        SP_NO_SIZEFIX => ss.no_sizefix as i64,
        SP_NO_MAGIC_DAMAGE => ss.no_magic_damage as i64,
        SP_NO_WEAPON_DAMAGE => ss.no_weapon_damage as i64,
        SP_NO_MISC_DAMAGE => ss.no_misc_damage as i64,
        SP_NO_GEMSTONE => (ss.no_gemstone != 0) as i64,
        SP_INTRAVISION => ss.intravision as i64,
        SP_NO_KNOCKBACK => ss.no_knockback as i64,
        SP_NO_MADO_FUEL => ss.no_mado_fuel as i64,
        SP_NO_WALK_DELAY => ss.no_walk_delay as i64,
        SP_SPLASH_RANGE => b.splash_range as i64,
        SP_SPLASH_ADD_RANGE => b.splash_add_range as i64,
        SP_SHORT_WEAPON_DAMAGE_RETURN => b.short_weapon_damage_return as i64,
        SP_LONG_WEAPON_DAMAGE_RETURN => b.long_weapon_damage_return as i64,
        SP_MAGIC_DAMAGE_RETURN => b.magic_damage_return as i64,
        SP_REDUCE_DAMAGE_RETURN => b.reduce_damage_return as i64,
        SP_PERFECT_HIDE => ss.perfect_hiding as i64,
        SP_UNBREAKABLE => b.unbreakable as i64,
        SP_UNBREAKABLE_WEAPON => (b.unbreakable_equip as u32 & EQP_WEAPON != 0) as i64,
        SP_UNBREAKABLE_ARMOR => (b.unbreakable_equip as u32 & EQP_ARMOR != 0) as i64,
        SP_UNBREAKABLE_HELM => (b.unbreakable_equip as u32 & EQP_HELM != 0) as i64,
        SP_UNBREAKABLE_SHIELD => (b.unbreakable_equip as u32 & EQP_SHIELD != 0) as i64,
        SP_UNBREAKABLE_GARMENT => (b.unbreakable_equip as u32 & EQP_GARMENT != 0) as i64,
        SP_UNBREAKABLE_SHOES => (b.unbreakable_equip as u32 & EQP_SHOES != 0) as i64,
        SP_CLASSCHANGE => b.classchange as i64,
        SP_SKILL_RATIO => b.skill_ratio as i64,
        SP_SHORT_ATK_RATE => b.short_attack_atk_rate as i64,
        SP_LONG_ATK_RATE => b.long_attack_atk_rate as i64,
        SP_NON_CRIT_ATK_RATE => b.non_crit_atk_rate as i64,
        SP_BREAK_WEAPON_RATE => b.break_weapon_rate as i64,
        SP_BREAK_ARMOR_RATE => b.break_armor_rate as i64,
        SP_ADD_STEAL_RATE => b.add_steal_rate as i64,
        SP_DELAYRATE => b.delayrate as i64,
        SP_CRIT_ATK_RATE => b.crit_atk_rate as i64,
        SP_UNSTRIPABLE_WEAPON => (b.unstripable_equip as u32 & EQP_WEAPON != 0) as i64,
        SP_UNSTRIPABLE | SP_UNSTRIPABLE_ARMOR => (b.unstripable_equip as u32 & EQP_ARMOR != 0) as i64,
        SP_UNSTRIPABLE_HELM => (b.unstripable_equip as u32 & EQP_HELM != 0) as i64,
        SP_UNSTRIPABLE_SHIELD => (b.unstripable_equip as u32 & EQP_SHIELD != 0) as i64,
        SP_SP_GAIN_VALUE => b.sp_gain_value as i64,
        SP_HP_GAIN_VALUE => b.hp_gain_value as i64,
        SP_LONG_SP_GAIN_VALUE => b.long_sp_gain_value as i64,
        SP_LONG_HP_GAIN_VALUE => b.long_hp_gain_value as i64,
        SP_MAGIC_SP_GAIN_VALUE => b.magic_sp_gain_value as i64,
        SP_MAGIC_HP_GAIN_VALUE => b.magic_hp_gain_value as i64,
        SP_ADD_HEAL_RATE => b.add_heal_rate as i64,
        SP_ADD_HEAL2_RATE => b.add_heal2_rate as i64,
        SP_ADD_ITEM_HEAL_RATE => b.itemhealrate2 as i64,
        SP_EMATK => b.ematk as i64,
        SP_EMATK_HIDDEN => b.ematk_hidden as i64,
        SP_FIXCASTRATE => b.fixcastrate as i64,
        SP_ADD_FIXEDCAST => b.add_fixcast as i64,
        SP_ADD_VARIABLECAST => b.add_varcast as i64,
        SP_CASTRATE | SP_VARCASTRATE => b.varcastrate as i64, // RENEWAL_CAST
        SP_CRIT_DEF_RATE => b.crit_def_rate as i64,
        SP_ADD_ITEM_SPHEAL_RATE => b.itemsphealrate2 as i64,
        _ => return None,
    };
    Some(v)
}

/// pc.cpp pc_setparam (`set <param>,<value>` in scripts; bank / cash / kafra points, roulette coins, kill params and
/// cook mastery are handled in script/param.rs before calling here). Returns false for unknown parameters.
pub fn setparam(c: &Ctx, aid: u32, typ: i64, val_tmp: i64) -> bool {
    let Some(mut ch) = crate::pc::character_of(c, aid) else { return false };
    if data::sd_get(c, aid).is_none() {
        return false;
    }
    let mut val = val_tmp as u32 as i32;
    let t = typ as i32;
    // mmo_charstatus status_point / skill_point are uint32: an int stored there wraps
    match t {
        SP_BASELEVEL => {
            let maxlv = pc_maxbaselv(ch.class) as i32;
            if val > maxlv {
                // Capping to max
                val = maxlv;
            }
            if val > ch.base_level as i32 {
                for i in 0..(val - ch.base_level as i32) {
                    let lv = (ch.base_level as i32 + i) as u16;
                    ch.status_point = ch.status_point.wrapping_add(statpoint_db().pc_gets_status_point(lv));
                    ch.trait_point += statpoint_db().pc_gets_trait_point(lv);
                }
            }
            ch.base_level = val as u16;
            ch.base_exp = 0;
            super::put_char(c, &ch);
            // clif_updatestatus(*sd, SP_BASELEVEL);  // Gets updated at the bottom
            for s in [SP_NEXTBASEEXP, SP_STATUSPOINT, SP_TRAITPOINT, SP_BASEEXP] {
                super::clif::updatestatus(c, aid, s);
            }
            super::calc_pc(c, aid, SCO_FORCE);
            if ch.party_id != 0 {
                crate::party::party_send_levelup(c, aid);
            }
        }
        SP_JOBLEVEL => {
            if val >= ch.job_level as i32 {
                let maxlv = pc_maxjoblv(ch.class) as i32;
                if val > maxlv {
                    val = maxlv;
                }
                ch.skill_point = ch.skill_point.wrapping_add((val - ch.job_level as i32) as u32);
                super::put_char(c, &ch);
                super::clif::updatestatus(c, aid, SP_SKILLPOINT);
            }
            ch.job_level = val as u16;
            ch.job_exp = 0;
            super::put_char(c, &ch);
            super::clif::updatestatus(c, aid, SP_NEXTJOBEXP);
            super::clif::updatestatus(c, aid, SP_JOBEXP);
            super::calc_pc(c, aid, SCO_FORCE);
        }
        SP_SKILLPOINT => {
            ch.skill_point = val as u32;
            super::put_char(c, &ch);
        }
        SP_STATUSPOINT => {
            ch.status_point = val as u32;
            super::put_char(c, &ch);
        }
        SP_TRAITPOINT => {
            ch.trait_point = val as u32;
            super::put_char(c, &ch);
        }
        SP_ZENY => {
            if val < 0 {
                return false; // can't set negative zeny
            }
            // log_zeny(*sd, LOG_TYPE_SCRIPT, sd->status.char_id, -(sd->status.zeny - cap_value(val, 0, MAX_ZENY)))
            let capped = cap_value(val as i64, 0, MAX_ZENY) as i32;
            crate::map_log::log_zeny(c, aid, crate::map_log::LOG_TYPE_SCRIPT, ch.id, (ch.zeny as i32).wrapping_sub(capped).wrapping_neg());
            ch.zeny = cap_value(val as i64, 0, MAX_ZENY) as u32;
            super::put_char(c, &ch);
        }
        SP_BASEEXP => {
            let cap = if pc_is_maxbaselv(ch.class, ch.base_level as u32) { MAX_LEVEL_BASE_EXP } else { MAX_EXP } as i64;
            let v = cap_value(val_tmp, 0, cap) as u64;
            if v < ch.base_exp {
                // Lost
                super::progress::pc_lostexp(c, aid, ch.base_exp - v, 0);
            } else {
                // Gained
                super::progress::pc_gainexp(c, aid, None, v - ch.base_exp, 0, 2);
            }
            return true;
        }
        SP_JOBEXP => {
            let cap = if pc_is_maxjoblv(ch.class, ch.job_level as u32) { MAX_LEVEL_JOB_EXP } else { MAX_EXP } as i64;
            let v = cap_value(val_tmp, 0, cap) as u64;
            if v < ch.job_exp {
                super::progress::pc_lostexp(c, aid, 0, ch.job_exp - v);
            } else {
                super::progress::pc_gainexp(c, aid, None, 0, v - ch.job_exp, 2);
            }
            return true;
        }
        SP_SEX => {
            ch.sex = if val != 0 { 1 } else { 0 };
            super::put_char(c, &ch);
        }
        SP_WEIGHT | SP_MAXWEIGHT => {
            let Some(mut it) = crate::inventory::st(c, aid) else { return false };
            if t == SP_WEIGHT {
                it.weight = val as u32;
            } else {
                it.max_weight = val as u32;
            }
            crate::inventory::put_st(c, &it);
        }
        SP_HP => {
            data::with_sd(c, aid, |sd| sd.battle_status.hp = cap_value(val, 1, sd.battle_status.max_hp as i32) as u32);
            super::calc::sync_unit(c, aid);
        }
        SP_MAXHP => {
            let cap = if ch.base_level < 100 {
                crate::conf::max_hp_lv99()
            } else if ch.base_level < 151 {
                crate::conf::max_hp_lv150()
            } else {
                crate::conf::max_hp()
            };
            let lowered = data::with_sd(c, aid, |sd| {
                sd.battle_status.max_hp = cap_value(val, 1, cap as i32) as u32;
                if sd.battle_status.max_hp < sd.battle_status.hp {
                    sd.battle_status.hp = sd.battle_status.max_hp;
                    true
                } else {
                    false
                }
            });
            super::calc::sync_unit(c, aid);
            if lowered == Some(true) {
                super::clif::updatestatus(c, aid, SP_HP);
            }
        }
        SP_SP => {
            data::with_sd(c, aid, |sd| sd.battle_status.sp = cap_value(val, 0, sd.battle_status.max_sp as i32) as u32);
        }
        SP_MAXSP => {
            let lowered = data::with_sd(c, aid, |sd| {
                sd.battle_status.max_sp = cap_value(val, 1, crate::conf::max_sp() as i32) as u32;
                if sd.battle_status.max_sp < sd.battle_status.sp {
                    sd.battle_status.sp = sd.battle_status.max_sp;
                    true
                } else {
                    false
                }
            });
            if lowered == Some(true) {
                super::clif::updatestatus(c, aid, SP_SP);
            }
        }
        SP_AP => {
            data::with_sd(c, aid, |sd| sd.battle_status.ap = cap_value(val, 0, sd.battle_status.max_ap as i32) as u32);
        }
        SP_MAXAP => {
            let lowered = data::with_sd(c, aid, |sd| {
                sd.battle_status.max_ap = cap_value(val, 1, crate::conf::max_ap() as i32) as u32;
                if sd.battle_status.max_ap < sd.battle_status.ap {
                    sd.battle_status.ap = sd.battle_status.max_ap;
                    true
                } else {
                    false
                }
            });
            if lowered == Some(true) {
                super::clif::updatestatus(c, aid, SP_AP);
            }
        }
        SP_STR | SP_AGI | SP_VIT | SP_INT | SP_DEX | SP_LUK => {
            let max = super::progress::pc_maxparameter(c, aid, &ch, (t - SP_STR) as usize);
            super::progress::pc_setstat(&mut ch, t, cap_value(val, 1, max));
            super::put_char(c, &ch);
        }
        SP_POW | SP_STA | SP_WIS | SP_SPL | SP_CON | SP_CRT => {
            let max = super::progress::pc_maxparameter(c, aid, &ch, PARAM_POW + (t - SP_POW) as usize);
            super::progress::pc_setstat(&mut ch, t, cap_value(val, 0, max));
            super::put_char(c, &ch);
        }
        SP_KARMA => {
            ch.karma = val as i8; // the low byte of val: the column holds mmo.hpp's unsigned char karma (read as u8)
            super::put_char(c, &ch);
        }
        SP_MANNER => {
            ch.manner = val as i16;
            super::put_char(c, &ch);
            if let Some(u) = crate::unit::get(c, aid) {
                if val < 0 {
                    super::sc::sc_start(c, None, &u, SC_NOCHAT, 100, 0, 0);
                } else {
                    super::sc::status_change_end(c, aid, SC_NOCHAT, 0);
                    super::clif::manner_message(c, aid, 5);
                }
            }
            return true; // status_change_start/status_change_end already sends packets warning the client
        }
        SP_FAME => {
            ch.fame = val as u32;
            super::put_char(c, &ch);
        }
        SP_CHARMOVE => {
            ch.moves = val as _;
            super::put_char(c, &ch);
            return true;
        }
        SP_CHARRENAME => {
            ch.rename = val as _;
            super::put_char(c, &ch);
            return true;
        }
        SP_KILLERRID | SP_KILLEDRID | SP_KILLEDGID => {
            return crate::mob::dead::set_kill_param(c, aid, t, val as i64).unwrap_or(true);
        }
        SP_CHARFONT => {
            // sd->status.font is an unsigned char (mmo.hpp:624); the view packets carry it from the unit
            ch.font = val as u8;
            super::put_char(c, &ch);
            if let Some(mut u) = crate::unit::get(c, aid) {
                u.font = ch.font as i16;
                crate::unit::save(c, &u);
                clif_font(c, &u, ch.font);
            }
            return true;
        }
        SP_PCDIECOUNTER => {
            if val < 0 {
                return false;
            }
            let Some(sd) = data::sd_get(c, aid) else { return false };
            if sd.die_counter == val {
                return true;
            }
            data::with_sd(c, aid, |sd| sd.die_counter = val);
            let connect_new = crate::pc::get(c, aid).map(|p| p.connect_new).unwrap_or(false);
            if !connect_new && val == 1 && (sd.class_ & MAPID_SECONDMASK) == MAPID_SUPER_NOVICE {
                super::calc_pc(c, aid, SCO_NONE); // Lost the bonus.
            }
            super::write_char_reg_num(c, aid, "PC_DIE_COUNTER", val as i64);
            return true;
        }
        _ => {
            log::error!("pc_setparam: parameter {typ} (= {val_tmp}) is not available on this server yet");
            return false;
        }
    }
    super::clif::updatestatus(c, aid, t);
    true
}

/// clif.cpp clif_font: ZC_NOTIFY_FONT (0x02ef: account id, font) to the area.
fn clif_font(c: &Ctx, sd: &crate::unit::Unit, font: u8) {
    let mut w = Vec::with_capacity(8);
    w.extend_from_slice(&0x02efu16.to_le_bytes());
    w.extend_from_slice(&sd.id.to_le_bytes());
    w.extend_from_slice(&(font as u16).to_le_bytes());
    crate::clif::send(c, &w, sd, crate::clif::Target::Area);
}
