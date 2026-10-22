//! battle.cpp battle_calc_weapon_attack (RENEWAL) and its parts: the Damage structure, hit / critical / lucky dodge,
//! the Renewal ATK parts (status, weapon, equip, mastery, percent ATK), size and element fixes, masteries,
//! multi-hits (Double Attack), skill ratio and ATK percent, status bonuses, DEF / Res reduction, left / right hands,
//! GvG / battleground rates, final modifiers, absorb and reflect; the skill damage adjustments (battle_skill_damage:
//! skill_damage_db and the skill_damage mapflag), battle_range_type, battle_blewcount_bonus and battle_calc_attack's
//! dispatch to the magic (`magic.rs`) and misc (`misc.rs`) calculations.
//!
//! Every skill_id branch of these functions is here; the per-skill ratios, hit rates, damage data and elements that
//! rAthena moved to the skill classes are reached through `skill->impl->…` at rAthena's call sites
//! (`crate::skills::skill_impl`). Float arithmetic follows rAthena's float / double exactly (f32 where it uses float).

use super::bl::{self, Sd};
use super::fix::{self, battle_attr_fix, battle_calc_cardfix, nk_has, Nk};
use super::*;
use crate::db::skills::*;
use crate::inventory::{EQI_AMMO, EQI_HAND_L, EQI_HAND_R};
use crate::map::{BL_ELEM, BL_MOB, BL_PC, BL_SKILL};
use crate::status::consts::*;
use crate::status::defs::*;
use crate::status::sc::{self, Sc};
use crate::unit::Unit;
use crate::world::Ctx;

/// map.hpp e_element pseudo-elements of skills.
pub const ELE_WEAPON: i32 = 12;
pub const ELE_ENDOWED: i32 = 13;
pub const ELE_RANDOM: i32 = 14;

/// battle.hpp e_base_damage_flag.
pub const BDMG_NONE: i32 = 0x0000;
pub const BDMG_CRIT: i32 = 0x0001;
pub const BDMG_ARROW: i32 = 0x0002;
pub const BDMG_MAGIC: i32 = 0x0004;
pub const BDMG_NOSIZE: i32 = 0x0008;

/// battle.hpp e_bonus_chk_flag.
pub const BCHK_ALL: u8 = 0;
pub const BCHK_REFINE: u8 = 1;
pub const BCHK_STAR: u8 = 2;

/// battle.hpp struct Damage (RENEWAL).
#[derive(Clone, Debug, Default)]
pub struct Damage {
    pub status_atk: i64,
    pub status_atk2: i64,
    pub weapon_atk: i64,
    pub weapon_atk2: i64,
    pub equip_atk: i64,
    pub equip_atk2: i64,
    pub mastery_atk: i64,
    pub mastery_atk2: i64,
    pub percent_atk: i64,
    pub percent_atk2: i64,
    /// Right hand damage
    pub damage: i64,
    /// Left hand damage
    pub damage2: i64,
    /// e_damage_type (clif_damage)
    pub type_: u8,
    /// Number of hits
    pub div_: i16,
    pub amotion: i32,
    pub dmotion: i32,
    /// Number of knockback cells
    pub blewcount: i32,
    /// e_battle_flag
    pub flag: i32,
    pub miscflag: i32,
    /// damage_lv (ATK_LUCKY, ATK_FLEE, ATK_DEF …)
    pub dmg_lv: u8,
    /// Display blue damage numbers in clif_damage
    pub isspdamage: bool,
}

// ---------------------------------------------------------------------------------------------- the ATK macros
// (ATK_RATE2 is used by pre-renewal code only; RE_ALLATK_ADD only by homunculi's SC_STYLE_CHANGE)

/// ATK_RATE.
fn atk_rate(lh: bool, d: &mut i64, d2: &mut i64, a: i64) {
    *d = *d * a / 100;
    if lh {
        *d2 = *d2 * a / 100;
    }
}
/// ATK_ADDRATE.
fn atk_addrate(lh: bool, d: &mut i64, d2: &mut i64, a: i64) {
    *d += *d * a / 100;
    if lh {
        *d2 += *d2 * a / 100;
    }
}
/// ATK_ADD.
fn atk_add(lh: bool, d: &mut i64, d2: &mut i64, a: i64) {
    *d += a;
    if lh {
        *d2 += a;
    }
}
/// ATK_ADD2.
fn atk_add2(lh: bool, d: &mut i64, d2: &mut i64, a: i64, b: i64) {
    *d += a;
    if lh {
        *d2 += b;
    }
}
/// RE_ALLATK_ADD (RENEWAL).
fn re_allatk_add(lh: bool, wd: &mut Damage, a: i64) {
    atk_add(lh, &mut wd.status_atk, &mut wd.status_atk2, a);
    atk_add(lh, &mut wd.weapon_atk, &mut wd.weapon_atk2, a);
    atk_add(lh, &mut wd.equip_atk, &mut wd.equip_atk2, a);
    atk_add(lh, &mut wd.mastery_atk, &mut wd.mastery_atk2, a);
}
/// RE_ALLATK_ADDRATE.
fn re_allatk_addrate(lh: bool, wd: &mut Damage, a: i64) {
    atk_addrate(lh, &mut wd.status_atk, &mut wd.status_atk2, a);
    atk_addrate(lh, &mut wd.weapon_atk, &mut wd.weapon_atk2, a);
    atk_addrate(lh, &mut wd.equip_atk, &mut wd.equip_atk2, a);
    atk_addrate(lh, &mut wd.mastery_atk, &mut wd.mastery_atk2, a);
}
// ---------------------------------------------------------------------------------------------- predicates

/// battle.cpp is_skill_using_arrow.
pub fn is_skill_using_arrow(c: &Ctx, src: &Unit, skill_id: u16) -> bool {
    if src.kind == BL_PC {
        if state::get(c, src.id).arrow_atk {
            return true;
        }
    } else {
        if skill_id != 0 && skill_get_ammotype(skill_id) != crate::db::items::AMMO_NONE as i32 {
            return true;
        }
        if bl::status(c, src).rhw.range > 3 {
            return true;
        }
    }
    matches!(skill_id, HT_FREEZINGTRAP | HT_PHANTASMIC | GS_GROUNDDRIFT | SS_KUNAIKUSSETSU | SS_KUNAIKAITEN)
}

/// battle.cpp is_attack_right_handed: skills always use the right hand only.
pub fn is_attack_right_handed(c: &Ctx, src: &Unit, skill_id: u16) -> bool {
    if skill_id == 0 && src.kind == BL_PC {
        if let Some(it) = crate::inventory::st(c, src.id) {
            if it.weapontype1 as i32 == W_FIST && it.weapontype2 as i32 != W_FIST {
                return false;
            }
        }
    }
    true
}

/// battle.cpp is_attack_left_handed.
pub fn is_attack_left_handed(c: &Ctx, src: &Unit, skill_id: u16) -> bool {
    if skill_id == 0 {
        if src.kind == BL_PC {
            if let Some(it) = crate::inventory::st(c, src.id) {
                if it.weapontype1 as i32 == W_FIST && it.weapontype2 as i32 != W_FIST {
                    return true;
                }
            }
            if crate::pc::character_of(c, src.id).map(|ch| ch.weapon as i32 == W_KATAR).unwrap_or(false) {
                return true;
            }
        }
        if bl::status(c, src).lhw.atk != 0 {
            return true;
        }
    }
    false
}

/// battle.cpp battle_skill_get_damage_properties: the normal attack has no NK_ flags, a splash one ignores ATK cards
/// and flee.
pub fn battle_skill_get_damage_properties(skill_id: u16, is_splash: i32) -> Nk {
    if skill_id == 0 {
        if is_splash != 0 {
            return (1u64 << NK_IGNOREATKCARD) | (1u64 << NK_IGNOREFLEE);
        }
        return 0;
    }
    skill_db().find(skill_id).map(|s| s.nk).unwrap_or(0)
}

/// battle.cpp is_attack_critical.
pub fn is_attack_critical(c: &Ctx, wd: &Damage, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16, first_call: bool) -> bool {
    if !first_call {
        return wd.type_ == DMG_CRITICAL || wd.type_ == DMG_MULTI_HIT_CRITICAL;
    }
    if skill_id == NPC_CRITICALSLASH || skill_id == LG_PINPOINTATTACK {
        return true; // Always critical skills
    }
    if skill_id != 0 && !skill_get_nk(skill_id, &[NK_CRITICAL]) {
        return false;
    }
    let sstatus = bl::status(c, src);
    if sstatus.cri == 0 {
        return false;
    }
    let sd = bl::sd(c, src);
    if wd.type_ == DMG_MULTI_HIT {
        // Multiple Hit Attack Skills.
        if bl::checkskill(c, sd.as_ref(), GS_CHAINACTION) != 0 && !skill_get_nk(GS_CHAINACTION, &[NK_CRITICAL]) {
            return false; // Chain Action
        }
        if bl::checkskill(c, sd.as_ref(), TF_DOUBLE) != 0 && !skill_get_nk(TF_DOUBLE, &[NK_CRITICAL]) {
            return false; // Double Attack
        }
    }
    let tstatus = bl::status(c, target);
    let scl = bl::sc(c, src);
    let tsc = bl::sc(c, target);
    let tsd = bl::sd(c, target);
    // int16 cri: every step is stored back into 16 bits
    let mut cri: i16 = sstatus.cri;
    if let Some(sd) = &sd {
        let ib = &sd.st.indexed_bonus;
        cri = (cri as i32 + ib.critaddrace.get(tstatus.race as usize).copied().unwrap_or(0) + ib.critaddrace[RC_ALL as usize]) as i16;
        if skill_id == 0 && is_skill_using_arrow(c, src, skill_id) {
            cri = (cri as i32 + sd.st.bonus.arrow_cri) as i16;
            cri = (cri as i32 + sd.st.bonus.critical_rangeatk as i32) as i16;
        }
    }
    if let Some(s) = &scl {
        if s.has(SC_CAMOUFLAGE) {
            cri = (cri as i32 + 100 * s.v3(SC_CAMOUFLAGE).min(10)) as i16; // max 100% (1K)
        }
    }
    // The official equation is *2, but that only applies when sd's do critical.
    // Therefore, we use the old value 3 on cases when an sd gets attacked by a mob
    let mult = if sd.is_none() && tsd.is_some() { 3 } else { 2 };
    cri = (cri as i32 - tstatus.luk as i32 * mult) as i16;
    if tsc.as_ref().map(|s| s.has(SC_SLEEP)).unwrap_or(false) {
        cri = (cri as i32 * 2) as i16;
    }
    let autocounter = |cri: &mut i16| -> Option<bool> {
        if conf::auto_counter_type() != 0 && conf::auto_counter_type() & src.kind != 0 {
            return Some(true);
        }
        *cri = (*cri as i32 * 2) as i16;
        None
    };
    match skill_id {
        0 => {
            let has_ac = scl.as_ref().map(|s| s.has(SC_AUTOCOUNTER)).unwrap_or(false);
            if scl.is_none() || has_ac {
                crate::clif::specialeffect(c, src, fix::EF_AUTOCOUNTER, crate::clif::Target::Area);
                sc::status_change_end(c, src.id, SC_AUTOCOUNTER, 0);
                if let Some(r) = autocounter(&mut cri) {
                    return r;
                }
            }
        }
        KN_AUTOCOUNTER => {
            if let Some(r) = autocounter(&mut cri) {
                return r;
            }
        }
        SN_SHARPSHOOTING | MA_SHARPSHOOTING => cri = (cri as i32 + 300) as i16, // !TODO: Confirm new bonus
        NJ_KIRIKAGE => cri = (cri as i32 + 250 + 50 * skill_lv as i32) as i16,
        ASC_BREAKER | GC_CROSSIMPACT | SHC_SAVAGE_IMPACT | SHC_ETERNAL_SLASH | SHC_CROSS_SLASH => cri /= 2,
        SHC_IMPACT_CRATER => {
            if sd.as_ref().map(|s| s.it.weapontype1 as i32 != W_KATAR).unwrap_or(true) {
                return false;
            }
            cri /= 2;
        }
        WH_GALESTORM => {
            if scl.as_ref().map(|s| !s.has(SC_CALAMITYGALE)).unwrap_or(false) {
                return false;
            }
        }
        NW_ONLY_ONE_BULLET | NW_SPIRAL_SHOOTING => {
            if sd.as_ref().map(|s| s.it.weapontype1 as i32 != W_RIFLE).unwrap_or(true) {
                return false;
            }
        }
        NW_MAGAZINE_FOR_ONE => {
            if sd.as_ref().map(|s| s.it.weapontype1 as i32 != W_REVOLVER).unwrap_or(true) {
                return false;
            }
        }
        SH_CHUL_HO_SONIC_CLAW => {
            let communion = scl.as_ref().map(|s| s.has(SC_TEMPORARY_COMMUNION)).unwrap_or(false);
            if !(bl::checkskill(c, sd.as_ref(), SH_COMMUNE_WITH_CHUL_HO) > 0 || communion) {
                return false;
            }
        }
        SKE_NOON_BLAST => match &scl {
            None => return false,
            Some(s) if !s.has(SC_NOON_SUN) && !s.has(SC_SKY_ENCHANT) => return false,
            _ => {}
        },
        SKE_SUNSET_BLAST => match &scl {
            None => return false,
            Some(s) if !s.has(SC_SUNSET_SUN) && !s.has(SC_SKY_ENCHANT) => return false,
            _ => {}
        },
        _ => {}
    }
    if let Some(tsd) = &tsd {
        if tsd.st.bonus.critical_def != 0 {
            cri = (cri as i32 * (100 - tsd.st.bonus.critical_def) / 100) as i16;
        }
    }
    ((c.rnd() % 1000) as i32) < cri as i32
}

/// battle.cpp is_attack_piercing: DEF-ratio bonuses (bDefRatioAtk*). 2 for the always-piercing skills.
pub fn is_attack_piercing(c: &Ctx, src: &Unit, target: &Unit, skill_id: u16, weapon_position: usize) -> i32 {
    if skill_id == MO_INVESTIGATE || skill_id == RL_MASS_SPIRAL {
        return 2;
    }
    let Some(sd) = bl::sd(c, src) else { return 0 };
    let tstatus = bl::status(c, target);
    if !matches!(skill_id, PA_SACRIFICE | CR_GRANDCROSS | NPC_GRANDDARKNESS | PA_SHIELDCHAIN | KO_HAPPOKUNAI) {
        let hits = |w: &crate::status::data::WeaponData| {
            w.def_ratio_atk_ele & (1 << tstatus.def_ele) != 0
                || w.def_ratio_atk_ele & (1 << ELE_ALL) != 0
                || w.def_ratio_atk_race & (1 << tstatus.race) != 0
                || w.def_ratio_atk_race & (1 << RC_ALL) != 0
                || w.def_ratio_atk_class & (1 << tstatus.class_) != 0
                || w.def_ratio_atk_class & (1 << CLASS_ALL) != 0
        };
        if hits(&sd.st.right_weapon) && weapon_position == EQI_HAND_R {
            return 1;
        }
        if hits(&sd.st.left_weapon) {
            // Pass effect onto right hand if configured so. [Skotlex]
            if crate::conf::left_cardfix_to_right() && is_attack_right_handed(c, src, skill_id) {
                if weapon_position == EQI_HAND_R {
                    return 1;
                }
            } else if weapon_position == EQI_HAND_L {
                return 1;
            }
        }
    }
    0
}

/// battle.cpp is_attack_hitting.
pub fn is_attack_hitting(c: &Ctx, wd: &Damage, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16, first_call: bool) -> bool {
    let sstatus = bl::status(c, src);
    let tstatus = bl::status(c, target);
    let scl = bl::sc(c, src);
    let tsc = bl::sc(c, target);
    let sd = bl::sd(c, src);
    let nk = battle_skill_get_damage_properties(skill_id, wd.miscflag);
    if !first_call {
        return wd.dmg_lv != ATK_FLEE;
    }
    if is_attack_critical(c, wd, src, target, skill_id, skill_lv, false) {
        return true;
    } else if sd.as_ref().map(|s| s.st.bonus.perfect_hit > 0 && ((c.rnd() % 100) as i32) < s.st.bonus.perfect_hit).unwrap_or(false) {
        return true;
    } else if scl.as_ref().map(|s| s.has(SC_FUSION)).unwrap_or(false) {
        return true;
    } else if (skill_id == AS_SPLASHER || skill_id == GN_SPORE_EXPLOSION) && wd.miscflag == 0 {
        return true;
    } else if skill_id == CR_SHIELDBOOMERANG && scl.as_ref().map(|s| s.has(SC_SPIRIT) && s.v2(SC_SPIRIT) == SL_CRUSADER as i32).unwrap_or(false) {
        return true;
    } else if tsc.is_some() && target.sc.opt1 != 0 && target.sc.opt1 != OPT1_STONEWAIT as i16 && target.sc.opt1 != OPT1_BURNING as i16 {
        return true;
    } else if nk_has(nk, NK_IGNOREFLEE) {
        return true;
    }
    if tsc.as_ref().map(|s| s.has(SC_NEUTRALBARRIER)).unwrap_or(false) && wd.flag & (BF_LONG | BF_MAGIC) == BF_LONG {
        return false;
    }
    // int16 flee / hitrate
    let mut flee: i16 = tstatus.flee;
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
    hitrate = (hitrate as i32 + sstatus.hit as i32 - flee as i32) as i16;
    // Fogwall's hit penalty is only for normal ranged attacks.
    if wd.flag & (BF_LONG | BF_MAGIC) == BF_LONG && skill_id == 0 && tsc.as_ref().map(|s| s.has(SC_FOGWALL)).unwrap_or(false) {
        hitrate = (hitrate as i32 - 50) as i16;
    }
    if let Some(sd) = &sd {
        if is_skill_using_arrow(c, src, skill_id) {
            hitrate = (hitrate as i32 + sd.st.bonus.arrow_hit) as i16;
        }
        // in Renewal hit bonus from Vultures Eye is not anymore shown in status window
        hitrate = (hitrate as i32 + bl::checkskill(c, Some(sd), AC_VULTURE)) as i16;
    }
    if skill_id != 0 {
        // skill_db.find(skill_id)->impl->modifyHitRate
        if let Some(imp) = crate::skills::skill_impl(skill_id) {
            imp.modify_hit_rate(c, &mut hitrate, src, target, skill_lv);
        }
    } else if sd.is_some() && wd.type_ & DMG_MULTI_HIT != 0 && wd.div_ == 2 {
        // +1 hit per level of Double Attack on a successful double attack
        hitrate = (hitrate as i32 + bl::checkskill(c, sd.as_ref(), TF_DOUBLE)) as i16;
    }
    if let Some(sd) = &sd {
        // Weaponry Research hidden bonus
        let skill = bl::checkskill(c, Some(sd), BS_WEAPONRESEARCH);
        if skill > 0 {
            hitrate = (hitrate as i32 + hitrate as i32 * (2 * skill) / 100) as i16;
        }
        let w = sd.ch.weapon as i32;
        let skill = bl::checkskill(c, Some(sd), GN_TRAINING_SWORD);
        if (w == W_1HSWORD || w == W_DAGGER) && skill > 0 {
            hitrate = (hitrate as i32 + 3 * skill) as i16;
        }
    }
    hitrate = (hitrate as i32).clamp(conf::min_hitrate(), conf::max_hitrate()) as i16;
    if skill_id == PA_SHIELDCHAIN {
        hitrate += 20; // Rapid Smiting gives a flat +20 hit after the hitrate was capped
    }
    ((c.rnd() % 100) as i32) < hitrate as i32
}

/// battle.cpp attack_ignores_def (bIgnoreDef*).
pub fn attack_ignores_def(c: &Ctx, wd: &Damage, src: &Unit, target: &Unit, skill_id: u16, weapon_position: usize) -> bool {
    let tstatus = bl::status(c, target);
    let scl = bl::sc(c, src);
    let sd = bl::sd(c, src);
    let nk = battle_skill_get_damage_properties(skill_id, wd.miscflag);
    if scl.as_ref().map(|s| s.has(SC_FUSION)).unwrap_or(false) {
        return true;
    }
    if let Some(sd) = &sd {
        let w = sd.ch.weapon as i32;
        match skill_id {
            RK_WINDCUTTER if w == W_2HSWORD => return true,
            NW_THE_VIGILANTE_AT_NIGHT if w == W_GATLING => return true,
            NW_ONLY_ONE_BULLET if w == W_REVOLVER => return true,
            _ => {}
        }
    }
    if skill_id != CR_GRANDCROSS && skill_id != NPC_GRANDDARKNESS {
        if let Some(sd) = &sd {
            let hits = |w: &crate::status::data::WeaponData| {
                w.ignore_def_ele & (1 << tstatus.def_ele) != 0
                    || w.ignore_def_ele & (1 << ELE_ALL) != 0
                    || w.ignore_def_race & (1 << tstatus.race) != 0
                    || w.ignore_def_race & (1 << RC_ALL) != 0
                    || w.ignore_def_class & (1 << tstatus.class_) != 0
                    || w.ignore_def_class & (1 << CLASS_ALL) != 0
            };
            if hits(&sd.st.right_weapon) && weapon_position == EQI_HAND_R {
                return true;
            }
            if hits(&sd.st.left_weapon) {
                if crate::conf::left_cardfix_to_right() && is_attack_right_handed(c, src, skill_id) {
                    // Move effect to right hand. [Skotlex]
                    if weapon_position == EQI_HAND_R {
                        return true;
                    }
                } else if weapon_position == EQI_HAND_L {
                    return true;
                }
            }
        }
    }
    nk_has(nk, NK_IGNOREDEFENSE)
}

/// battle.cpp battle_skill_stacks_masteries_vvs: skills unaffected by refine bonus, masteries, Star Crumbs, Spirit
/// Spheres and ATK percent.
pub fn battle_skill_stacks_masteries_vvs(skill_id: u16, _chk_flag: u8) -> bool {
    !matches!(
        skill_id,
        PA_SHIELDCHAIN | CR_SHIELDBOOMERANG | AM_ACIDTERROR | MO_INVESTIGATE | MO_EXTREMITYFIST | PA_SACRIFICE | RK_DRAGONBREATH | RK_DRAGONBREATH_WATER | NC_SELFDESTRUCTION | LG_SHIELDPRESS | LG_EARTHDRIVE | NPC_DRAGONBREATH
    )
}

/// battle.cpp battle_calc_equip_attack (RENEWAL): equipment ATK (+ arrow ATK for arrow attacks).
fn battle_calc_equip_attack(c: &Ctx, src: &Unit, skill_id: u16) -> i32 {
    let mut eatk = 0;
    let status = bl::status(c, src);
    if let Some(sd) = bl::sd(c, src) {
        if is_skill_using_arrow(c, src, skill_id) {
            // Attack of cannon balls is not added to equip attack, the skills that use them add it
            if let Some((_, data)) = bl::equip(c, &sd, EQI_AMMO) {
                if data.subtype != crate::db::items::AMMO_CANNONBALL {
                    eatk += sd.st.bonus.arrow_atk;
                }
            }
        }
    }
    eatk + status.eatk as i32
}

/// map_session_data spiritcharm / spiritcharm_type of a player (skill/state.rs: the `pc_skill` row).
fn spiritcharm(c: &Ctx, sd: Option<&Sd>) -> (i32, i32) {
    match sd {
        Some(sd) => {
            let s = crate::skill::state::get(c, sd.aid);
            (s.spiritcharm as i32, s.spiritcharm_type)
        }
        None => (0, crate::skill::state::CHARM_TYPE_NONE),
    }
}

/// battle.cpp battle_get_weapon_element.
#[allow(clippy::too_many_arguments)]
pub fn battle_get_weapon_element(c: &Ctx, wd: &Damage, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16, weapon_position: usize, calc_for_damage_only: bool) -> i32 {
    use crate::skill::state::{CHARM_TYPE_NONE, MAX_SPIRITCHARM};
    let mut element = skill_get_ele(skill_id, skill_lv);
    let sd = bl::sd(c, src);
    let scl = bl::sc(c, src);
    let sstatus = bl::status(c, src);
    if skill_id == 0 || element == ELE_WEAPON {
        // Take weapon's element
        element = if weapon_position == EQI_HAND_R { sstatus.rhw.ele as i32 } else { sstatus.lhw.ele as i32 };
        if let Some(sd) = &sd {
            if is_skill_using_arrow(c, src, skill_id) && sd.st.bonus.arrow_ele != 0 && weapon_position == EQI_HAND_R {
                element = sd.st.bonus.arrow_ele;
            }
        }
        let (charm, charm_type) = spiritcharm(c, sd.as_ref());
        if sd.is_some() && charm_type != CHARM_TYPE_NONE && charm >= MAX_SPIRITCHARM {
            element = charm_type; // Summoning 10 spiritcharm will endow your weapon
        }
        if let Some(s) = &scl {
            // on official endows override all other elements [helvetica]
            if s.has(SC_ENCHANTARMS) {
                element = s.v1(SC_ENCHANTARMS);
            }
            if skill_id == 0 && s.has(SC_GOLDENE_FERSE) && ((c.rnd() % 100) as i32) < s.v4(SC_GOLDENE_FERSE) {
                element = ELE_HOLY;
            }
        }
    } else if element == ELE_ENDOWED {
        // Use enchantment's element
        element = crate::status::calc_sc::status_calc_attack_element(&scl.unwrap_or_default(), 0) as i32;
    } else if element == ELE_RANDOM {
        element = (c.rnd() % ELE_ALL as u32) as i32;
    }
    // Modify the element type of skill attack (skill_db.find(skill_id)->impl->modifyElement)
    if let Some(imp) = crate::skills::skill_impl(skill_id) {
        imp.modify_element(c, wd, src, target, skill_lv, &mut element, calc_for_damage_only as i32);
    }
    element
}

/// battle.cpp battle_get_magic_element.
pub fn battle_get_magic_element(c: &Ctx, dmg: &Damage, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16) -> i32 {
    use crate::skill::state::{CHARM_TYPE_NONE, MAX_SPIRITCHARM};
    let mut element = skill_get_ele(skill_id, skill_lv);
    match element {
        ELE_WEAPON => {
            // pl = -1 : the skill takes the weapon's element
            element = bl::status(c, src).rhw.ele as i32;
            let sd = bl::sd(c, src);
            let (charm, charm_type) = spiritcharm(c, sd.as_ref());
            if sd.is_some() && charm_type != CHARM_TYPE_NONE && charm >= MAX_SPIRITCHARM {
                element = charm_type; // Summoning 10 spiritcharm will endow your weapon
            }
        }
        // Use status element
        ELE_ENDOWED => element = crate::status::calc_sc::status_calc_attack_element(&bl::sc(c, src).unwrap_or_default(), 0) as i32,
        // Use random element
        ELE_RANDOM => element = (c.rnd() % ELE_ALL as u32) as i32,
        _ => {}
    }
    // Modify the element type of skill attack
    if let Some(imp) = crate::skills::skill_impl(skill_id) {
        imp.modify_element(c, dmg, src, target, skill_lv, &mut element, 0);
    }
    element
}

/// battle.cpp battle_get_misc_element.
pub fn battle_get_misc_element(c: &Ctx, dmg: &Damage, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16) -> i32 {
    let mut element = skill_get_ele(skill_id, skill_lv);
    match element {
        // Attack that takes weapon's element for misc attacks? Make it neutral [Skotlex]
        ELE_WEAPON | ELE_ENDOWED => element = ELE_NEUTRAL,
        // Use random element
        ELE_RANDOM => element = (c.rnd() % ELE_ALL as u32) as i32,
        _ => {}
    }
    // Modify the element type of skill attack
    if let Some(imp) = crate::skills::skill_impl(skill_id) {
        imp.modify_element(c, dmg, src, target, skill_lv, &mut element, 0);
    }
    element
}

/// battle.cpp battle_min_damage: caps the right hand to `min`, the left hand always to 0.
pub(super) fn battle_min_damage(c: &Ctx, wd: &mut Damage, src: &Unit, skill_id: u16, min: i64) {
    if is_attack_right_handed(c, src, skill_id) {
        wd.damage = wd.damage.max(min);
    }
    if is_attack_left_handed(c, src, skill_id) {
        wd.damage2 = wd.damage2.max(0);
    }
}

/// battle.cpp battle_get_spiritball_damage: the bonus damage of Spirit Spheres (the spheres a skill used are gone
/// before the calculation; each skill counts them its own way).
fn battle_get_spiritball_damage(c: &Ctx, wd: &Damage, src: &Unit, skill_id: u16) -> i32 {
    // Return 0 for non-players
    let Some(sd) = bl::sd(c, src) else { return 0 };
    let s = crate::skill::state::get(c, sd.aid);
    let (spiritball, spiritball_old) = (s.spiritball as i32, s.spiritball_old as i32);
    match skill_id {
        // These skills used as many spheres as they do hits
        MO_INVESTIGATE => (wd.div_ as i32 + spiritball) * 3,
        // These skills store the spheres used in spiritball_old (RENEWAL)
        MO_FINGEROFFENSIVE => (spiritball_old + spiritball) * 3,
        // These skills store the number of spheres the player had before cast
        MO_EXTREMITYFIST => spiritball_old * 3,
        // Any skills that do not consume spheres or do not care
        _ => spiritball * 3,
    }
}

/// battle.cpp battle_calc_element_damage (RENEWAL: the player's element was applied to the ATK parts already).
fn battle_calc_element_damage(c: &Ctx, wd: &mut Damage, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16) {
    let nk = battle_skill_get_damage_properties(skill_id, wd.miscflag);
    let sd = bl::sd(c, src);
    let tstatus = bl::status(c, target);
    let right_element = battle_get_weapon_element(c, wd, src, target, skill_id, skill_lv, EQI_HAND_R, true);
    let lh = is_attack_left_handed(c, src, skill_id);
    // Elemental attribute fix
    if !nk_has(nk, NK_IGNOREELEMENT) && (wd.damage > 0 || wd.damage2 > 0) {
        let left_element = battle_get_weapon_element(c, wd, src, target, skill_id, skill_lv, EQI_HAND_L, true);
        let (de, dl) = (tstatus.def_ele as i32, tstatus.ele_lv as i32);
        match skill_id {
            PA_SACRIFICE | RK_DRAGONBREATH | RK_DRAGONBREATH_WATER | NC_SELFDESTRUCTION | HFLI_SBR44 => {
                wd.damage = battle_attr_fix(c, Some(src), Some(target), wd.damage, right_element, de, dl, 1);
                if lh {
                    wd.damage2 = battle_attr_fix(c, Some(src), Some(target), wd.damage2, left_element, de, dl, 1);
                }
            }
            _ => {
                if skill_id == 0 && conf::attack_attr_none() & src.kind != 0 {
                    return; // Non-player basic attacks are non-elemental, they deal 100% against all defense elements
                }
                // Renewal player's elemental damage calculation is already done before this point
                if sd.is_none() {
                    wd.damage = battle_attr_fix(c, Some(src), Some(target), wd.damage, right_element, de, dl, 1);
                    if lh {
                        wd.damage2 = battle_attr_fix(c, Some(src), Some(target), wd.damage2, left_element, de, dl, 1);
                    }
                }
            }
        }
        // Forced to neutral skills [helvetica]
        if matches!(skill_id, MO_INVESTIGATE | MO_EXTREMITYFIST | CR_SHIELDBOOMERANG | PA_SHIELDCHAIN | MC_CARTREVOLUTION | HW_MAGICCRASHER | SR_FALLENEMPIRE | SR_CRESCENTELBOW_AUTOSPELL | SR_GATEOFHELL | GN_FIRE_EXPANSION_ACID) {
            wd.damage = battle_attr_fix(c, Some(src), Some(target), wd.damage, ELE_NEUTRAL, de, dl, 1);
            if lh {
                wd.damage2 = battle_attr_fix(c, Some(src), Some(target), wd.damage2, ELE_NEUTRAL, de, dl, 1);
            }
        }
    }
    // Cap damage to 0
    if !conf::attr_recover() {
        battle_min_damage(c, wd, src, skill_id, 0);
    }
}

// ---------------------------------------------------------------------------------------------- masteries

/// battle.cpp battle_addmastery (RENEWAL: the mastery ATK only, `dmg` is not added).
fn battle_addmastery(c: &Ctx, sd: &Sd, target: &Unit, _dmg: i64, type_: i32) -> i64 {
    let status = bl::status(c, target);
    let chk = |id| bl::checkskill(c, Some(sd), id);
    let mut damage: i64 = 0;
    let (race, ele) = (status.race as i32, status.def_ele as i32);
    let skill = chk(AL_DEMONBANE);
    if skill > 0 && target.kind == BL_MOB && (sc::battle_check_undead(race, ele) || race == RC_DEMON) {
        // This bonus doesn't work against players.
        damage += (skill as f64 * (sd.ch.base_level as f64 / 20.0 + 3.0)) as i64;
    }
    let skill = chk(RA_RANGERMAIN);
    if skill > 0 && (race == RC_BRUTE || race == RC_PLAYER_DORAM || race == RC_PLANT || race == RC_FISH) {
        damage += skill as i64 * 5;
    }
    let skill = chk(NC_RESEARCHFE);
    if skill > 0 && (ele == ELE_FIRE || ele == ELE_EARTH) {
        damage += skill as i64 * 10;
    }
    damage += 15 * chk(NC_MADOLICENCE) as i64; // Attack bonus is granted even without the Madogear
    let skill = chk(HT_BEASTBANE);
    if skill > 0 && (race == RC_INSECT || race == RC_BRUTE || race == RC_PLAYER_DORAM) {
        damage += skill as i64 * 4;
        if let Some(s) = sc::get_sce(c, sd.aid, SC_SPIRIT) {
            if s.val2 == SL_HUNTER as i32 {
                damage += sd.ch.str as i64;
            }
        }
    }
    // Weapon Research bonus applies to all weapons
    let skill = chk(BS_WEAPONRESEARCH);
    if skill > 0 {
        damage += skill as i64 * 2;
    }
    let skill = chk(NV_BREAKTHROUGH);
    if skill > 0 {
        damage += 15 * skill as i64 + if skill > 4 { 25 } else { 0 };
    }
    // Kagerou/Oboro Spirit Charm bonus
    {
        use crate::skill::state::{CHARM_TYPE_FIRE, CHARM_TYPE_LAND, CHARM_TYPE_WATER, CHARM_TYPE_WIND, MAX_SPIRITCHARM};
        let (charm, charm_type) = spiritcharm(c, Some(sd));
        if charm >= MAX_SPIRITCHARM
            && ((charm_type == CHARM_TYPE_FIRE && ele == ELE_EARTH)
                || (charm_type == CHARM_TYPE_WATER && ele == ELE_FIRE)
                || (charm_type == CHARM_TYPE_LAND && ele == ELE_WIND)
                || (charm_type == CHARM_TYPE_WIND && ele == ELE_WATER))
        {
            damage += damage * 30 / 100;
        }
    }
    let weapon = if type_ == 0 { sd.it.weapontype1 as i32 } else { sd.it.weapontype2 as i32 };
    match weapon {
        W_1HSWORD | W_DAGGER => {
            if weapon == W_1HSWORD {
                let skill = chk(AM_AXEMASTERY);
                if skill > 0 {
                    damage += skill as i64 * 3;
                }
            }
            let skill = chk(SM_SWORD);
            if skill > 0 {
                damage += skill as i64 * 4;
            }
            let skill = chk(GN_TRAINING_SWORD);
            if skill > 0 {
                damage += skill as i64 * 10;
            }
        }
        W_2HSWORD => {
            let skill = chk(SM_TWOHAND);
            if skill > 0 {
                damage += skill as i64 * 4;
            }
        }
        W_1HSPEAR | W_2HSPEAR => {
            let skill = chk(KN_SPEARMASTERY);
            if skill > 0 {
                let opt = crate::unit::get(c, sd.aid).map(|u| u.sc.option).unwrap_or(0);
                // pc_isriding / pc_isridingdragon
                if opt & OPTION_RIDING == 0 && opt & OPTION_DRAGON == 0 {
                    damage += skill as i64 * 4;
                } else {
                    damage += skill as i64 * 5;
                }
                // Increase damage by level of KN_SPEARMASTERY * 10
                if chk(RK_DRAGONTRAINING) > 0 {
                    damage += skill as i64 * 10;
                }
            }
        }
        W_1HAXE | W_2HAXE => {
            let skill = chk(AM_AXEMASTERY);
            if skill > 0 {
                damage += skill as i64 * 3;
            }
            let skill = chk(NC_TRAININGAXE);
            if skill > 0 {
                damage += skill as i64 * 5;
            }
        }
        W_MACE | W_2HMACE => {
            let skill = chk(PR_MACEMASTERY);
            if skill > 0 {
                damage += skill as i64 * 3;
            }
            let skill = chk(NC_TRAININGAXE);
            if skill > 0 {
                damage += skill as i64 * 4;
            }
        }
        W_FIST | W_KNUCKLE => {
            if weapon == W_FIST {
                let skill = chk(TK_RUN);
                if skill > 0 {
                    damage += skill as i64 * 10;
                }
            }
            let skill = chk(MO_IRONHAND);
            if skill > 0 {
                damage += skill as i64 * 3;
            }
        }
        W_MUSICAL => {
            let skill = chk(BA_MUSICALLESSON);
            if skill > 0 {
                damage += skill as i64 * 3;
            }
        }
        W_WHIP => {
            let skill = chk(DC_DANCINGLESSON);
            if skill > 0 {
                damage += skill as i64 * 3;
            }
        }
        W_BOOK => {
            let skill = chk(SA_ADVANCEDBOOK);
            if skill > 0 {
                damage += skill as i64 * 3;
            }
        }
        W_KATAR => {
            let skill = chk(AS_KATAR);
            if skill > 0 {
                damage += skill as i64 * 3;
            }
        }
        _ => {}
    }
    damage
}

/// battle.cpp battle_calc_attack_masteries (RENEWAL).
fn battle_calc_attack_masteries(c: &Ctx, wd: &mut Damage, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16) {
    let sd = bl::sd(c, src);
    let scl = bl::sc(c, src);
    let Some(sd) = sd.filter(|_| battle_skill_stacks_masteries_vvs(skill_id, BCHK_ALL)) else { return };
    let lh = is_attack_left_handed(c, src, skill_id);
    // Add mastery damage
    wd.damage = battle_addmastery(c, &sd, target, wd.damage, 0);
    wd.mastery_atk = battle_addmastery(c, &sd, target, wd.weapon_atk, 0);
    if lh {
        wd.damage2 = battle_addmastery(c, &sd, target, wd.damage2, 1);
        wd.mastery_atk2 = battle_addmastery(c, &sd, target, wd.weapon_atk2, 1);
    }
    // General skill masteries
    if skill_id == TF_POISON {
        // Additional ATK from Envenom is treated as mastery type damage [helvetica]
        atk_add(lh, &mut wd.mastery_atk, &mut wd.mastery_atk2, 15 * skill_lv as i64);
    }
    if skill_id != MC_CARTREVOLUTION && bl::checkskill(c, Some(&sd), BS_HILTBINDING) > 0 {
        atk_add(lh, &mut wd.mastery_atk, &mut wd.mastery_atk2, 4);
    }
    // Star Crumb bonus damage
    atk_add2(lh, &mut wd.mastery_atk, &mut wd.mastery_atk2, sd.st.right_weapon.star as i64, sd.st.left_weapon.star as i64);
    // Spirit Sphere bonus damage
    let spirit = battle_get_spiritball_damage(c, wd, src, skill_id) as i64;
    atk_add(lh, &mut wd.mastery_atk, &mut wd.mastery_atk2, spirit);
    if skill_id == NJ_SYURIKEN {
        let skill = bl::checkskill(c, Some(&sd), NJ_TOBIDOUGU);
        if skill > 0 {
            atk_add(lh, &mut wd.damage, &mut wd.damage2, 3 * skill as i64);
            atk_add(lh, &mut wd.mastery_atk, &mut wd.mastery_atk2, 3 * skill as i64);
        }
    }
    match skill_id {
        RA_WUGDASH | RA_WUGSTRIKE | RA_WUGBITE => {
            let skill = bl::checkskill(c, Some(&sd), RA_TOOTHOFWUG);
            atk_add(lh, &mut wd.damage, &mut wd.damage2, 30 * skill as i64);
            atk_add(lh, &mut wd.mastery_atk, &mut wd.mastery_atk2, 30 * skill as i64);
        }
        GN_CARTCANNON | NC_ARMSCANNON => {
            // Arrow attack of these skills is not influenced by P.ATK so we add it as mastery attack
            let tstatus = bl::status(c, target);
            let v = battle_attr_fix(c, Some(src), Some(target), sd.st.bonus.arrow_atk as i64, sd.st.bonus.arrow_ele, tstatus.def_ele as i32, tstatus.ele_lv as i32, 0);
            atk_add(lh, &mut wd.mastery_atk, &mut wd.mastery_atk2, v);
        }
        _ => {}
    }
    if let Some(s) = &scl {
        // Status change considered as masteries
        if s.has(SC_NIBELUNGEN) {
            // With renewal, the level 4 weapon limitation has been removed
            atk_add(lh, &mut wd.mastery_atk, &mut wd.mastery_atk2, s.v2(SC_NIBELUNGEN) as i64);
        }
        if s.has(SC_CAMOUFLAGE) {
            let v = 30 * s.v3(SC_CAMOUFLAGE).min(10) as i64;
            atk_add(lh, &mut wd.damage, &mut wd.damage2, v);
            atk_add(lh, &mut wd.mastery_atk, &mut wd.mastery_atk2, v);
        }
        if s.has(SC_GN_CARTBOOST) {
            let v = 10 * s.v1(SC_GN_CARTBOOST) as i64;
            atk_add(lh, &mut wd.damage, &mut wd.damage2, v);
            atk_add(lh, &mut wd.mastery_atk, &mut wd.mastery_atk2, v);
        }
        if s.has(SC_P_ALTER) {
            let v = s.v2(SC_P_ALTER) as i64;
            atk_add(lh, &mut wd.damage, &mut wd.damage2, v);
            atk_add(lh, &mut wd.mastery_atk, &mut wd.mastery_atk2, v);
        }
    }
}

// ---------------------------------------------------------------------------------------------- base damage

/// battle.cpp battle_add_weapon_damage: overrefine bonus and bWeaponDamageRate of a hand.
fn battle_add_weapon_damage(c: &Ctx, sd: Option<&Sd>, damage: &mut i64, lr_type: usize) {
    let Some(sd) = sd else { return };
    let ib = &sd.st.indexed_bonus;
    if lr_type == EQI_HAND_L {
        if sd.st.left_weapon.overrefine != 0 {
            *damage = *damage + (c.rnd() % sd.st.left_weapon.overrefine as u32) as i64 + 1;
        }
        let r = ib.weapon_damage_rate.get(sd.it.weapontype2 as usize).copied().unwrap_or(0);
        if r != 0 {
            *damage += *damage * r as i64 / 100;
        }
    } else if lr_type == EQI_HAND_R {
        if sd.st.right_weapon.overrefine != 0 {
            *damage = *damage + (c.rnd() % sd.st.right_weapon.overrefine as u32) as i64 + 1;
        }
        let r = ib.weapon_damage_rate.get(sd.it.weapontype1 as usize).copied().unwrap_or(0);
        if r != 0 {
            *damage += *damage * r as i64 / 100;
        }
    }
}

/// battle.cpp battle_calc_sizefix (RENEWAL): the weapon's size modifier (players only; flag skips it).
fn battle_calc_sizefix(damage: i64, sd: Option<&Sd>, t_size: u8, weapon_type: usize, flag: bool) -> i32 {
    let mut damage = damage;
    if let Some(sd) = sd {
        if !sd.st.special_state.no_sizefix && !flag {
            let mods = if weapon_type == EQI_HAND_L { &sd.st.left_weapon.atkmods } else { &sd.st.right_weapon.atkmods };
            damage = damage * mods.get(t_size as usize).copied().unwrap_or(100) as i64 / 100;
        }
    }
    damage.clamp(i32::MIN as i64, i32::MAX as i64) as i32
}

/// battle.cpp battle_calc_base_weapon_attack (RENEWAL): weapon ATK with variance, the stat bonus, overrefine and
/// the size modifier. `lhw` picks the left-hand weapon.
fn battle_calc_base_weapon_attack(c: &Ctx, src: &Unit, t_size: u8, lhw: bool, sd: Option<&Sd>, critical: bool) -> i32 {
    let status = bl::status(c, src);
    let type_ = if lhw { EQI_HAND_L } else { EQI_HAND_R };
    let wa = if lhw { &status.lhw } else { &status.rhw };
    let mut atkmin: u16 = if type_ == EQI_HAND_L { status.watk2 } else { status.watk };
    let mut atkmax: u16 = atkmin;
    let mut damage: i64 = atkmin as i64;
    let scl = bl::sc(c, src);
    if let Some(sd) = sd {
        if bl::equip(c, sd, type_).is_some() {
            let base_stat: i16 = match sd.ch.weapon as i32 {
                W_BOW | W_MUSICAL | W_WHIP | W_REVOLVER | W_RIFLE | W_GATLING | W_SHOTGUN | W_GRENADE => {
                    if bl::checkskill(c, Some(sd), SU_SOULATTACK) > 0 {
                        status.str
                    } else {
                        status.dex
                    }
                }
                _ => status.str,
            };
            // float arithmetic as rAthena
            let variance: f32 = 5.0f32 * wa.atk as f32 * wa.wlv as f32 / 100.0f32;
            let base_stat_bonus: f32 = wa.atk as f32 * base_stat as f32 / 200.0f32;
            atkmin = 0i32.max((atkmin as f32 - variance + base_stat_bonus) as i32) as u16;
            atkmax = (u16::MAX as i32).min((atkmax as f32 + variance + base_stat_bonus) as i32) as u16;
            if scl.as_ref().map(|s| s.has(SC_MAXIMIZEPOWER)).unwrap_or(false) || critical {
                damage = atkmax as i64;
            } else {
                damage = c.rnd_value(atkmin as i32, atkmax as i32) as u16 as i64;
            }
        }
    }
    let weapon_perfection = scl.as_ref().map(|s| s.has(SC_WEAPONPERFECTION)).unwrap_or(false);
    battle_add_weapon_damage(c, sd, &mut damage, type_);
    let damage = battle_calc_sizefix(damage, sd, t_size, type_, weapon_perfection) as i64;
    damage.clamp(i32::MIN as i64, i32::MAX as i64) as i32
}

/// battle.cpp battle_calc_base_damage: the standard damage of a normal attack (renewal: non-players only).
pub(super) fn battle_calc_base_damage(c: &Ctx, src: &Unit, lhw: bool, t_size: u8, flag: i32) -> i64 {
    let status = bl::status(c, src);
    let wa = if lhw { &status.lhw } else { &status.rhw };
    let scl = bl::sc(c, src);
    let sd = bl::sd(c, src);
    let mut atkmin: u32;
    let mut atkmax: u32;
    let mut type_ = 0usize;
    let mut damage: i64;
    match &sd {
        None => {
            // Mobs/Pets
            if flag & BDMG_MAGIC != 0 {
                atkmin = status.matk_min as u32;
                atkmax = status.matk_max as u32;
            } else {
                atkmin = wa.atk as u32;
                atkmax = wa.atk2 as u32;
            }
            if atkmin > atkmax {
                atkmin = atkmax;
            }
        }
        Some(sd) => {
            // PCs
            if flag & BDMG_MAGIC != 0 {
                atkmin = status.matk_min as u32;
                atkmax = status.matk_max as u32;
            } else {
                atkmax = wa.atk as u32;
                type_ = if lhw { EQI_HAND_L } else { EQI_HAND_R };
                atkmin = 0;
                if flag & BDMG_CRIT == 0 || flag & BDMG_ARROW != 0 {
                    // Normal attacks
                    atkmin = status.dex as i32 as u32;
                    if let Some((_, data)) = bl::equip(c, sd, type_) {
                        if data.r#type == crate::db::items::IT_WEAPON {
                            atkmin = atkmin * (80 + data.weapon_level as u32 * 20) / 100;
                        }
                    }
                    if atkmin > atkmax {
                        atkmin = atkmax;
                    }
                    if flag & BDMG_ARROW != 0 {
                        // Bows
                        atkmin = atkmin * atkmax / 100;
                        if atkmin > atkmax {
                            atkmax = atkmin;
                        }
                    }
                }
            }
        }
    }
    if scl.as_ref().map(|s| s.has(SC_MAXIMIZEPOWER)).unwrap_or(false) {
        atkmin = atkmax;
    }
    // Weapon Damage calculation
    if flag & BDMG_CRIT == 0 {
        damage = (if atkmax > atkmin { c.rnd() % (atkmax - atkmin) } else { 0 }) as i64 + atkmin as i64;
    } else {
        damage = atkmax as i64;
    }
    if let Some(sd) = &sd {
        // rodatazone says the range is 0~arrow_atk-1 for non crit
        if flag & BDMG_ARROW != 0 && sd.st.bonus.arrow_atk != 0 {
            damage += if flag & BDMG_CRIT != 0 { sd.st.bonus.arrow_atk as i64 } else { (c.rnd() % sd.st.bonus.arrow_atk as u32) as i64 };
        }
        // Size fix only for players
        if !(sd.st.special_state.no_sizefix || flag & BDMG_NOSIZE != 0) {
            let mods = if type_ == EQI_HAND_L { &sd.st.left_weapon.atkmods } else { &sd.st.right_weapon.atkmods };
            damage = damage * mods.get(t_size as usize).copied().unwrap_or(100) as i64 / 100;
        }
    } else if src.kind == BL_ELEM {
        // ele_sc = status_get_sc(src); ele_class = status_get_class(src) (an elemental's ed->elemental.class_)
        if let Some(ele_sc) = bl::sc(c, src) {
            use crate::elemental::data::{
                ELEMENTALID_AGNI_L, ELEMENTALID_AGNI_M, ELEMENTALID_AGNI_S, ELEMENTALID_AQUA_L, ELEMENTALID_AQUA_M, ELEMENTALID_AQUA_S, ELEMENTALID_ARDOR, ELEMENTALID_DILUVIO,
                ELEMENTALID_PROCELLA, ELEMENTALID_SERPENS, ELEMENTALID_TERA_L, ELEMENTALID_TERA_M, ELEMENTALID_TERA_S, ELEMENTALID_TERREMOTUS, ELEMENTALID_VENTUS_L, ELEMENTALID_VENTUS_M,
                ELEMENTALID_VENTUS_S,
            };
            let ele_class = crate::elemental::data::get(c, src.id).map(|ed| ed.elemental.class_ as i32).unwrap_or(0);
            let insignia = match ele_class {
                ELEMENTALID_AGNI_S | ELEMENTALID_AGNI_M | ELEMENTALID_AGNI_L | ELEMENTALID_ARDOR => SC_FIRE_INSIGNIA,
                ELEMENTALID_AQUA_S | ELEMENTALID_AQUA_M | ELEMENTALID_AQUA_L | ELEMENTALID_DILUVIO => SC_WATER_INSIGNIA,
                ELEMENTALID_VENTUS_S | ELEMENTALID_VENTUS_M | ELEMENTALID_VENTUS_L | ELEMENTALID_PROCELLA => SC_WIND_INSIGNIA,
                ELEMENTALID_TERA_S | ELEMENTALID_TERA_M | ELEMENTALID_TERA_L | ELEMENTALID_TERREMOTUS | ELEMENTALID_SERPENS => SC_EARTH_INSIGNIA,
                _ => crate::status::consts::SC_NONE,
            };
            if ele_sc.get(insignia).map(|e| e.val1 == 1).unwrap_or(false) {
                damage += damage * 20 / 100;
            }
        }
    }
    // Finally, add baseatk
    if flag & BDMG_MAGIC == 0 {
        damage += status.batk as i64;
    }
    if sd.is_some() {
        battle_add_weapon_damage(c, sd.as_ref(), &mut damage, type_);
    }
    if flag & BDMG_CRIT != 0 {
        damage = damage * 14 / 10;
    }
    damage
}

/// battle.cpp battle_calc_damage_parts (RENEWAL): status, weapon, equip, percent and mastery ATK of a player.
fn battle_calc_damage_parts(c: &Ctx, wd: &mut Damage, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16) {
    let sstatus = bl::status(c, src);
    let tstatus = bl::status(c, target);
    let sd = bl::sd(c, src);
    let right_element = battle_get_weapon_element(c, wd, src, target, skill_id, skill_lv, EQI_HAND_R, false);
    let left_element = battle_get_weapon_element(c, wd, src, target, skill_id, skill_lv, EQI_HAND_L, false);
    let (de, dl) = (tstatus.def_ele as i32, tstatus.ele_lv as i32);
    wd.status_atk += sstatus.batk as i64;
    wd.status_atk2 += sstatus.batk as i64;
    if sd.is_some() && sc::has_sce(c, src.id, SC_SEVENWIND) {
        // Mild Wind applies element to status ATK as well as weapon ATK [helvetica]
        wd.status_atk = battle_attr_fix(c, Some(src), Some(target), wd.status_atk, sstatus.rhw.ele as i32, de, dl, 0);
        wd.status_atk2 = battle_attr_fix(c, Some(src), Some(target), wd.status_atk2, sstatus.lhw.ele as i32, de, dl, 0);
    } else {
        // status atk is considered neutral on normal attacks [helvetica]
        wd.status_atk = battle_attr_fix(c, Some(src), Some(target), wd.status_atk, ELE_NEUTRAL, de, dl, 0);
        wd.status_atk2 = battle_attr_fix(c, Some(src), Some(target), wd.status_atk2, ELE_NEUTRAL, de, dl, 0);
    }
    // Right-hand status attack is doubled after elemental adjustments
    wd.status_atk *= 2;
    let critical = wd.type_ == DMG_MULTI_HIT_CRITICAL || wd.type_ == DMG_CRITICAL;
    wd.weapon_atk += battle_calc_base_weapon_attack(c, src, tstatus.size, false, sd.as_ref(), critical) as i64;
    wd.weapon_atk2 += battle_calc_base_weapon_attack(c, src, tstatus.size, true, sd.as_ref(), critical) as i64;
    // Weapon ATK gain bonus from SC_SUB_WEAPONPROPERTY here ( +x% pseudo element damage)
    if sd.is_some() {
        if let Some(e) = sc::get_sce(c, src.id, SC_SUB_WEAPONPROPERTY) {
            let mut bonus_atk = ((wd.weapon_atk * e.val2 as i64 / 100) as f32).floor() as i64;
            let mut bonus_atk2 = ((wd.weapon_atk2 * e.val2 as i64 / 100) as f32).floor() as i64;
            bonus_atk = battle_attr_fix(c, Some(src), Some(target), bonus_atk, e.val1, de, dl, 0);
            bonus_atk2 = battle_attr_fix(c, Some(src), Some(target), bonus_atk2, e.val1, de, dl, 0);
            wd.weapon_atk += bonus_atk;
            wd.weapon_atk2 += bonus_atk2;
        }
    }
    wd.weapon_atk = battle_attr_fix(c, Some(src), Some(target), wd.weapon_atk, right_element, de, dl, 0);
    wd.weapon_atk2 = battle_attr_fix(c, Some(src), Some(target), wd.weapon_atk2, left_element, de, dl, 0);
    wd.equip_atk += battle_calc_equip_attack(c, src, skill_id) as i64;
    wd.equip_atk = battle_attr_fix(c, Some(src), Some(target), wd.equip_atk, right_element, de, dl, 0);
    wd.equip_atk2 += battle_calc_equip_attack(c, src, skill_id) as i64;
    wd.equip_atk2 = battle_attr_fix(c, Some(src), Some(target), wd.equip_atk2, left_element, de, dl, 0);
    // AtkRate gives a static bonus from (W.ATK + E.ATK)
    if let Some(sd) = &sd {
        if sd.st.bonus.atk_rate != 0 {
            wd.percent_atk = (wd.weapon_atk + wd.equip_atk) * sd.st.bonus.atk_rate as i64 / 100;
            wd.percent_atk2 = (wd.weapon_atk2 + wd.equip_atk2) * sd.st.bonus.atk_rate as i64 / 100;
        }
    }
    // Mastery ATK is a special kind of ATK that has no elemental properties
    battle_calc_attack_masteries(c, wd, src, target, skill_id, skill_lv);
    wd.damage = 0;
    wd.damage2 = 0;
}

/// battle.cpp battle_calc_skill_base_damage (RENEWAL): the basic ATK that goes into the skill formula.
pub(super) fn battle_calc_skill_base_damage(c: &Ctx, wd: &mut Damage, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16) {
    let sc = bl::sc(c, src);
    let sstatus = bl::status(c, src);
    let tstatus = bl::status(c, target);
    let sd = bl::sd(c, src);
    let nk = battle_skill_get_damage_properties(skill_id, wd.miscflag);
    let lh = is_attack_left_handed(c, src, skill_id);
    // Calc base damage according to skill
    match skill_id {
        PA_SACRIFICE => {
            // uint32 max_hp * 9 / 100
            wd.damage = (sstatus.max_hp.wrapping_mul(9) / 100) as i64;
            wd.damage2 = 0;
            wd.weapon_atk = wd.damage;
            wd.weapon_atk2 = wd.damage2;
        }
        LK_SPIRALPIERCE | ML_SPIRALPIERCE => {
            if let Some(sd) = &sd {
                battle_calc_damage_parts(c, wd, src, target, skill_id, skill_lv);
                // Officially statusAtk + weaponAtk + equipAtk make base attack
                // We simulate this here by adding them all into equip attack
                let (a, b) = (wd.status_atk + wd.weapon_atk, wd.status_atk2 + wd.weapon_atk2);
                atk_add2(lh, &mut wd.equip_atk, &mut wd.equip_atk2, a, b);
                // Set statusAtk and weaponAtk to 0
                atk_rate(lh, &mut wd.status_atk, &mut wd.status_atk2, 0);
                atk_rate(lh, &mut wd.weapon_atk, &mut wd.weapon_atk2, 0);
                // Add weight
                if let Some((_, data)) = bl::equip(c, sd, EQI_HAND_R) {
                    if data.r#type == crate::db::items::IT_WEAPON {
                        wd.equip_atk += (data.weight / 10) as i64;
                    }
                }
                // 70% damage modifier is applied to base attack + weight
                atk_rate(lh, &mut wd.equip_atk, &mut wd.equip_atk2, 70);
                // Additional skill-specific size fix
                match tstatus.size as i32 {
                    SZ_SMALL => atk_rate(lh, &mut wd.equip_atk, &mut wd.equip_atk2, 130), // Small: 130%
                    SZ_MEDIUM => atk_rate(lh, &mut wd.equip_atk, &mut wd.equip_atk2, 115), // Medium: 115%
                    _ => {}                                                                   // Large: 100%
                }
            } else {
                // Monsters have no weight and use ATK instead
                wd.damage = battle_calc_base_damage(c, src, false, tstatus.size, 0);
            }
        }
        CR_SHIELDBOOMERANG | PA_SHIELDCHAIN => {
            wd.damage = sstatus.batk as i64;
            if let Some(sd) = &sd {
                // Base damage of shield skills is [batk + 4*refine + weight]
                if let Some((row, data)) = bl::equip(c, sd, EQI_HAND_L) {
                    if data.r#type == crate::db::items::IT_ARMOR {
                        let refine = 4 * row.item.refine as i64;
                        let weight = (data.weight / 10) as i64;
                        atk_add(lh, &mut wd.damage, &mut wd.damage2, refine);
                        atk_add(lh, &mut wd.damage, &mut wd.damage2, weight);
                        atk_add(lh, &mut wd.weapon_atk, &mut wd.weapon_atk2, refine);
                        atk_add(lh, &mut wd.weapon_atk, &mut wd.weapon_atk2, weight);
                    }
                }
            } else {
                atk_add(lh, &mut wd.damage, &mut wd.damage2, sstatus.rhw.atk2 as i64); // Else use Atk2
            }
        }
        RK_DRAGONBREATH | RK_DRAGONBREATH_WATER => {
            // (sstatus->hp / 50 + status_get_max_sp(src) / 4) * skill_lv: uint32 arithmetic stored in an int32
            let mut damagevalue: i32 = (sstatus.hp / 50 + sstatus.max_sp / 4).wrapping_mul(skill_lv as u32) as i32;
            let lv = bl::lv_of(c, src);
            if lv > 100 {
                damagevalue = damagevalue.wrapping_mul(lv) / 100;
            }
            if let Some(sd) = &sd {
                let training = bl::checkskill(c, Some(sd), RK_DRAGONTRAINING);
                if bl::checkskill(c, Some(sd), DK_DRAGONIC_AURA) >= 1 {
                    damagevalue = damagevalue.wrapping_mul(90 + 10 * training + sstatus.pow as i32 / 5) / 100;
                } else {
                    damagevalue = damagevalue.wrapping_mul(90 + 10 * training) / 100;
                }
            }
            if let Some(e) = sc.as_ref().and_then(|s| s.get(SC_DRAGONIC_AURA)) {
                damagevalue = damagevalue.wrapping_add(damagevalue.wrapping_mul(e.val1).wrapping_mul(10) / 100);
            }
            atk_add(lh, &mut wd.damage, &mut wd.damage2, damagevalue as i64);
            atk_add(lh, &mut wd.weapon_atk, &mut wd.weapon_atk2, damagevalue as i64);
            wd.flag |= BF_LONG;
        }
        NC_SELFDESTRUCTION => {
            // (skill_lv + 1) * (mainframe + 8) * (status_get_sp(src) + sstatus->vit): the last factor is uint32
            let mainframe = sd.as_ref().map(|s| bl::checkskill(c, Some(s), NC_MAINFRAME)).unwrap_or(0);
            let left = (skill_lv as i32 + 1) * (mainframe + 8);
            let mut damagevalue: i32 = (left as u32).wrapping_mul(sstatus.sp.wrapping_add(sstatus.vit as i32 as u32)) as i32;
            let lv = bl::lv_of(c, src);
            if lv > 100 {
                damagevalue = damagevalue.wrapping_mul(lv) / 100;
            }
            damagevalue = (damagevalue as u32).wrapping_add(sstatus.hp) as i32;
            atk_add(lh, &mut wd.damage, &mut wd.damage2, damagevalue as i64);
            atk_add(lh, &mut wd.weapon_atk, &mut wd.weapon_atk2, damagevalue as i64);
        }
        KO_HAPPOKUNAI => {
            if let Some(sd) = &sd {
                // 3 * (2 * batk + rhw.atk + ammo atk) * (skill_lv + 5) / 5: the ammo's uint32 atk makes it unsigned
                let ammo_atk = bl::equip(c, sd, EQI_AMMO).map(|(_, d)| d.atk).unwrap_or(0);
                let base = ((2 * sstatus.batk + sstatus.rhw.atk as i32) as u32).wrapping_add(ammo_atk);
                let mut damagevalue: i32 = (3u32.wrapping_mul(base).wrapping_mul((skill_lv as i32 + 5) as u32) / 5) as i32;
                if let Some(e) = sc.as_ref().and_then(|s| s.get(SC_KAGEMUSYA)) {
                    damagevalue = damagevalue.wrapping_add(damagevalue.wrapping_mul(e.val2) / 100);
                }
                atk_add(lh, &mut wd.damage, &mut wd.damage2, damagevalue as i64);
                atk_add(lh, &mut wd.weapon_atk, &mut wd.weapon_atk2, damagevalue as i64);
            } else {
                atk_add(lh, &mut wd.damage, &mut wd.damage2, 5000);
            }
        }
        HFLI_SBR44 => {
            // [orn] (battle.cpp:4130-4133) a homunculus' hit is its intimacy
            if src.kind == crate::map::BL_HOM {
                if let Some(hd) = crate::homunculus::data::get(c, src.id) {
                    wd.damage = hd.homunculus.intimacy as i64;
                }
            }
        }
        _ => {
            // Flags that apply to both pre-renewal and renewal
            let mut bflag = BDMG_NONE;
            if is_attack_critical(c, wd, src, target, skill_id, skill_lv, false) {
                bflag |= BDMG_CRIT;
            }
            if skill_id == 0 && sc.as_ref().map(|s| s.has(SC_CHANGE)).unwrap_or(false) {
                bflag |= BDMG_MAGIC;
            }
            let tsize = tstatus.size;
            if sd.is_some() {
                battle_calc_damage_parts(c, wd, src, target, skill_id, skill_lv);
            } else {
                wd.damage = battle_calc_base_damage(c, src, false, tsize, bflag);
                if is_attack_left_handed(c, src, skill_id) {
                    wd.damage2 = battle_calc_base_damage(c, src, true, tsize, bflag);
                }
            }
            if nk_has(nk, NK_SPLASHSPLIT) {
                // Divide ATK among targets
                if wd.miscflag > 0 {
                    let m = wd.miscflag as i64;
                    wd.damage /= m;
                    wd.status_atk /= m;
                    wd.weapon_atk /= m;
                    wd.equip_atk /= m;
                    wd.mastery_atk /= m;
                } else {
                    log::error!("0 enemies targeted by {skill_id}:{}, divide per 0 avoided!", skill_get_name(skill_id));
                }
            }
            // Add any bonuses that modify the base atk (pre-skills)
            if let Some(sd) = &sd {
                let skill = bl::checkskill(c, Some(sd), TK_POWER);
                if skill > 0 {
                    let dmg_bonus = 20 * skill as i64;
                    atk_addrate(lh, &mut wd.damage, &mut wd.damage2, dmg_bonus);
                    re_allatk_addrate(lh, wd, dmg_bonus);
                }
            }
        }
    }
}

/// DAMAGE_DIV_FIX.
fn damage_div_fix(dmg: &mut i64, div: &mut i16) {
    if *div < 0 {
        *div = -*div;
        *dmg /= *div as i64;
    }
    *dmg *= *div as i64;
}

/// battle.cpp battle_apply_div_fix: DAMAGE_DIV_FIX and the minimum damage.
pub(super) fn battle_apply_div_fix(d: &mut Damage, skill_id: u16) {
    if d.damage != 0 {
        damage_div_fix(&mut d.damage, &mut d.div_);
        // Min damage
        if d.damage < d.div_ as i64 && (matches!(skill_id, SU_LUNATICCARROTBEAT | SU_LUNATICCARROTBEAT2 | SU_CN_METEOR | SU_CN_METEOR2) || conf::skill_min_damage() & d.flag != 0) {
            d.damage = d.div_ as i64;
        }
    } else if d.div_ < 0 {
        d.div_ = -d.div_;
    }
}

/// battle.cpp battle_calc_multi_attack: Double Attack (TF_DOUBLE / bDoubleRate), Chain Action, Fear Breeze.
fn battle_calc_multi_attack(c: &Ctx, wd: &mut Damage, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16) {
    let Some(sd) = bl::sd(c, src) else { return };
    if skill_id != 0 {
        return;
    }
    let _ = target;
    let mut skill_lv = skill_lv;
    let scl = bl::sc(c, src);
    let wt1 = sd.it.weapontype1 as i32;
    if let Some(s) = scl.as_ref().filter(|s| s.has(SC_FEARBREEZE)) {
        if wt1 == W_BOW {
            if let Some((row, _)) = bl::equip(c, &sd, EQI_AMMO) {
                if row.item.amount > 1 {
                    let chance = (c.rnd() % 100) as i32;
                    let lv = s.v1(SC_FEARBREEZE);
                    // switch (val1) { case 5: … [[fallthrough]]; case 4: … case 3: … case 2: case 1: … }
                    if lv == 5 && chance < 4 {
                        wd.div_ = 5; // 3 % chance to attack 5 times.
                    } else if (4..=5).contains(&lv) && chance < 7 {
                        wd.div_ = 4; // 6 % chance to attack 4 times.
                    } else if (3..=5).contains(&lv) && chance < 10 {
                        wd.div_ = 3; // 9 % chance to attack 3 times.
                    } else if (1..=5).contains(&lv) && chance < 13 {
                        wd.div_ = 2; // 12 % chance to attack 2 times.
                    }
                    wd.div_ = wd.div_.min(row.item.amount as i16);
                    if let Some(mut e) = sc::get_sce(c, src.id, SC_FEARBREEZE) {
                        e.val4 = wd.div_ as i32 - 1;
                        sc::put_sce(c, &e);
                    }
                    if wd.div_ > 1 {
                        wd.type_ = DMG_MULTI_HIT;
                    }
                }
            }
        }
    }
    // (wd->div_ == 1 && (skill_lv = pc_checkskill(TF_DOUBLE)) > 0 && dagger) || temporary TF_DOUBLE || bDoubleRate ||
    // SC_KAGEMUSYA — `&&` binds first, so skill_lv is only read when div_ is 1
    let kagemusya = scl.as_ref().map(|s| s.has(SC_KAGEMUSYA)).unwrap_or(false);
    let first = wd.div_ == 1 && {
        skill_lv = bl::checkskill(c, Some(&sd), TF_DOUBLE) as u16;
        skill_lv > 0 && wt1 == W_DAGGER
    };
    let temp_double = bl::checkskill_flag(c, sd.aid, TF_DOUBLE) > SKILL_FLAG_PERMANENT as i32 && wt1 != W_FIST;
    if first || temp_double || (sd.st.bonus.double_rate > 0 && wt1 != W_FIST) || (kagemusya && wt1 != W_FIST) {
        // Success chance is not added, the higher one is used [Skotlex]
        let max_rate = if kagemusya {
            scl.as_ref().map(|s| s.v1(SC_KAGEMUSYA)).unwrap_or(0) * 10 // Same rate as even levels of TF_DOUBLE
        } else {
            (7 * skill_lv as i32).max(sd.st.bonus.double_rate)
        };
        if ((c.rnd() % 100) as i32) < max_rate {
            wd.div_ = skill_get_num(TF_DOUBLE, if skill_lv != 0 { skill_lv } else { 1 }) as i16;
            wd.type_ = DMG_MULTI_HIT;
        }
    }
    if wd.div_ == 1 {
        let mut lv = 0;
        let chain = if wt1 == W_REVOLVER && {
            lv = bl::checkskill(c, Some(&sd), GS_CHAINACTION);
            lv > 0
        } {
            true
        } else if let Some(s) = scl.as_ref().filter(|s| !s.empty() && s.has(SC_E_CHAIN)) {
            lv = s.v1(SC_E_CHAIN);
            lv > 0
        } else {
            false
        };
        if chain && ((c.rnd() % 100) as i32) < 5 * lv {
            wd.div_ = skill_get_num(GS_CHAINACTION, lv as u16) as i16;
            wd.type_ = DMG_MULTI_HIT;
            sc::sc_start(c, Some(src), src, SC_QD_SHOT_READY, 100, target.id as i32, skill_get_time(RL_QD_SHOT, 1) as i64);
        }
    }
}

/// battle.cpp battle_get_atkpercent: the ATK percent of status changes.
fn battle_get_atkpercent(bl_kind: u16, skill_id: u16, s: &Sc) -> u16 {
    if bl_kind == BL_PC && !battle_skill_stacks_masteries_vvs(skill_id, BCHK_ALL) {
        return 100;
    }
    let mut atkpercent: i32 = 100;
    if s.has(SC_CURSE) {
        atkpercent -= 25;
    }
    if s.has(SC_PROVOKE) {
        atkpercent += s.v2(SC_PROVOKE);
    }
    if s.has(SC_STRIPWEAPON) && bl_kind != BL_PC {
        atkpercent -= s.v2(SC_STRIPWEAPON);
    }
    if s.has(SC_CONCENTRATION) {
        atkpercent += s.v2(SC_CONCENTRATION);
    }
    if s.has(SC_TRUESIGHT) {
        atkpercent += 2 * s.v1(SC_TRUESIGHT);
    }
    if s.has(SC_JOINTBEAT) && s.v2(SC_JOINTBEAT) & BREAK_WAIST as i32 != 0 {
        atkpercent -= 25;
    }
    if s.has(SC_INCATKRATE) {
        atkpercent += s.v1(SC_INCATKRATE);
    }
    if s.has(SC_POWERUP) {
        atkpercent += s.v1(SC_POWERUP);
    }
    if s.has(SC_SKE) {
        atkpercent += 300;
    }
    if s.has(SC_BLOODLUST) {
        atkpercent += s.v2(SC_BLOODLUST);
    }
    if s.has(SC_FLEET) {
        atkpercent += s.v3(SC_FLEET);
    }
    if s.has(SC_INVINCIBLE) {
        atkpercent += s.v2(SC_INVINCIBLE);
    }
    atkpercent.clamp(0, u16::MAX as i32) as u16
}

/// battle.cpp battle_calc_attack_skill_ratio: the linear status modifiers, then the skill's own ratio
/// (skill->impl->calculateSkillRatio).
fn battle_calc_attack_skill_ratio(c: &Ctx, wd: &Damage, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16) -> i32 {
    let sd = bl::sd(c, src);
    let scl = bl::sc(c, src);
    let mut skillratio: i32 = 100;
    // Skill damage modifiers that stack linearly
    if let Some(s) = scl.as_ref().filter(|_| skill_id != PA_SACRIFICE) {
        // ATK percent modifier (in renewal, it's applied before the skillratio)
        skillratio = battle_get_atkpercent(src.kind, skill_id, s) as i32;
        if let Some(sd) = &sd {
            skillratio += sd.st.bonus.skill_ratio;
        }
        if s.has(SC_OVERTHRUST) {
            skillratio += s.v3(SC_OVERTHRUST);
        }
        if s.has(SC_MAXOVERTHRUST) {
            skillratio += s.v2(SC_MAXOVERTHRUST);
        }
        if s.has(SC_BERSERK) {
            skillratio += 200;
        }
        if skill_id == 0 || skill_id == KN_AUTOCOUNTER {
            if let Some(sce) = s.get(SC_POISONREACT).filter(|e| e.val4 == 1) {
                // Damage boost from poison react (for players bonus depends on level learned)
                if sd.is_some() {
                    skillratio += 30 * bl::checkskill(c, sd.as_ref(), AS_POISONREACT);
                } else {
                    skillratio += 30 * sce.val1;
                }
                // This attack has a chance to cause poison
                let amotion = bl::status(c, src).amotion as i32;
                sc::status_change_start(c, Some(src), target, SC_POISON, sce.val3 * 100, sce.val1, src.id as i32, 0, 0, skill_get_time2(AS_POISONREACT, sce.val1 as u16) as i64, 0, amotion);
                sc::status_change_end(c, src.id, SC_POISONREACT, 0);
            }
            if s.has(SC_CRUSHSTRIKE) {
                if let Some(sd) = &sd {
                    // ATK [{Weapon Level * (Weapon Upgrade Level + 6) * 100} + (Weapon ATK) + (Weapon Weight)]%
                    if let Some((row, data)) = bl::equip(c, sd, EQI_HAND_R) {
                        if data.r#type == crate::db::items::IT_WEAPON {
                            skillratio += -100 + data.weight as i32 / 10 + data.atk as i32 + 100 * data.weapon_level as i32 * (row.item.refine as i32 + 6);
                        }
                    }
                }
                sc::status_change_end(c, src.id, SC_CRUSHSTRIKE, 0);
                let src_now = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
                crate::skill::effect::skill_break_equip(c, &src_now, &src_now, crate::inventory::EQP_WEAPON as u16, 2000, BCT_SELF);
            } else if s.has(SC_GIANTGROWTH) && sd.as_ref().map(|sd| sd.st.class_ & crate::db::jobs::MAPID_THIRDMASK == crate::db::jobs::MAPID_RUNE_KNIGHT).unwrap_or(false) {
                // Increase damage again if Crush Strike is not active
                if crate::npc::mapflag_vs(c, src.map) {
                    skillratio += 125; // Only half of the 2.5x increase on versus-type maps
                } else {
                    skillratio += 250;
                }
            }
        }
    }
    // skill_db.find(skill_id)->impl->calculateSkillRatio
    if let Some(imp) = crate::skills::skill_impl(skill_id) {
        imp.calculate_skill_ratio(c, wd, src, target, skill_lv, &mut skillratio, 0);
    }
    skillratio
}

/// battle.cpp battle_calc_skill_constant_addition (constant additions of skills; none for normal attacks).
fn battle_calc_skill_constant_addition(c: &Ctx, src: &Unit, skill_id: u16, skill_lv: u16) -> i64 {
    let sd = bl::sd(c, src);
    match skill_id {
        MO_EXTREMITYFIST => 250 + 150 * skill_lv as i64,
        PA_SHIELDCHAIN => {
            let mut atk = 0;
            if let Some(sd) = &sd {
                if let Some((row, data)) = bl::equip(c, sd, EQI_HAND_L) {
                    if data.r#type == crate::db::items::IT_ARMOR {
                        // Bonus damage: [max(100, Random(100, 0.7*weight + pow(skill level + refine)))]
                        let mut bonus = (7 * data.weight as i32) / 100;
                        bonus += ((skill_lv as i32 + row.item.refine as i32) as f64).powi(2) as i32;
                        atk = 100.max(c.rnd_value(100, bonus)) as i64;
                    }
                }
            }
            atk
        }
        HT_FREEZINGTRAP => sd.as_ref().map(|s| 40 * bl::checkskill(c, Some(s), RA_RESEARCHTRAP) as i64).unwrap_or(0),
        _ => 0,
    }
}

/// battle.cpp battle_attack_sc_bonus (RENEWAL): stackable status bonuses on top of the calculated damage.
fn battle_attack_sc_bonus(c: &Ctx, wd: &mut Damage, src: &Unit, target: &Unit, skill_id: u16, _skill_lv: u16) {
    let sd = bl::sd(c, src);
    let scl = bl::sc(c, src);
    let sstatus = bl::status(c, src);
    let tstatus = bl::status(c, target);
    let lh = is_attack_left_handed(c, src, skill_id);
    let mut anger_id: usize = 0; // SLS Anger
    // Kagerou/Oboro Earth Charm effect +15% wATK
    let (charm, charm_type) = spiritcharm(c, sd.as_ref());
    if sd.is_some() && charm_type == crate::skill::state::CHARM_TYPE_LAND && charm > 0 {
        atk_addrate(lh, &mut wd.damage, &mut wd.damage2, 15 * charm as i64);
        atk_addrate(lh, &mut wd.weapon_atk, &mut wd.weapon_atk2, 15 * charm as i64);
    }
    // The following are applied on top of current damage and are stackable.
    if let Some(s) = &scl {
        if s.has(SC_WATK_ELEMENT) && skill_id != ASC_METEORASSAULT {
            atk_addrate(lh, &mut wd.weapon_atk, &mut wd.weapon_atk2, s.v2(SC_WATK_ELEMENT) as i64);
        }
        if s.has(SC_DRUMBATTLE) {
            atk_add(lh, &mut wd.equip_atk, &mut wd.equip_atk2, s.v2(SC_DRUMBATTLE) as i64);
        }
        if s.has(SC_MADNESSCANCEL) {
            atk_add(lh, &mut wd.equip_atk, &mut wd.equip_atk2, 100);
        }
        if s.has(SC_MAGICALBULLET) {
            let tmdef = (tstatus.mdef as i32 + tstatus.mdef2 as i32) as i16;
            let (mn, mx) = (sstatus.matk_min as i64, sstatus.matk_max as i64);
            if mn > tmdef as i64 && mx > mn {
                let v = (mn + (c.rnd() as i64 % (mx - mn)) - tmdef as i64).max(0);
                atk_add(lh, &mut wd.weapon_atk, &mut wd.weapon_atk2, v);
            } else {
                atk_add(lh, &mut wd.weapon_atk, &mut wd.weapon_atk2, (mn - tmdef as i64).max(0));
            }
        }
        if s.has(SC_GATLINGFEVER) {
            atk_add(lh, &mut wd.equip_atk, &mut wd.equip_atk2, s.v3(SC_GATLINGFEVER) as i64);
        }
        if s.has(SC_SPIRIT) {
            if skill_id == AS_SONICBLOW && s.v2(SC_SPIRIT) == SL_ASSASIN as i32 {
                let v = if crate::npc::mapflag_gvg2(c, src.map) { 25 } else { 100 };
                atk_addrate(lh, &mut wd.damage, &mut wd.damage2, v);
                re_allatk_addrate(lh, wd, v);
            } else if skill_id == CR_SHIELDBOOMERANG && s.v2(SC_SPIRIT) == SL_CRUSADER as i32 {
                atk_addrate(lh, &mut wd.damage, &mut wd.damage2, 100);
                re_allatk_addrate(lh, wd, 100);
            }
        }
        if s.has(SC_GT_CHANGE) {
            atk_addrate(lh, &mut wd.damage, &mut wd.damage2, s.v1(SC_GT_CHANGE) as i64);
        }
        if s.has(SC_EDP) {
            match skill_id {
                // Renewal: Venom Splasher, Meteor Assault, Grimtooth and Venom Knife ignore EDP
                TF_SPRINKLESAND | AS_SPLASHER | ASC_METEORASSAULT | AS_GRIMTOOTH | AS_VENOMKNIFE => {}
                _ => {
                    // weapon atk * (2.5 + (edp level * .3)), equip atk * (2.5 + (edp level * .3))
                    let r = 250 + s.v1(SC_EDP) as i64 * 30;
                    atk_rate(lh, &mut wd.weapon_atk, &mut wd.weapon_atk2, r);
                    atk_rate(lh, &mut wd.equip_atk, &mut wd.equip_atk2, r);
                }
            }
        }
        if s.has(SC_DANCEWITHWUG) {
            let chorus = battle_calc_chorusbonus(c, sd.as_ref()) as i64;
            if skill_get_inf2(skill_id, &[INF2_INCREASEDANCEWITHWUGDAMAGE]) {
                atk_addrate(lh, &mut wd.damage, &mut wd.damage2, s.v1(SC_DANCEWITHWUG) as i64 * 10 * chorus);
                re_allatk_addrate(lh, wd, s.v1(SC_DANCEWITHWUG) as i64 * 10 * chorus);
            }
            atk_addrate(lh, &mut wd.damage, &mut wd.damage2, s.v1(SC_DANCEWITHWUG) as i64 * 2 * chorus);
            atk_addrate(lh, &mut wd.equip_atk, &mut wd.equip_atk2, s.v1(SC_DANCEWITHWUG) as i64 * 2 * chorus);
        }
        if s.has(SC_ZENKAI) && sstatus.rhw.ele as i32 == s.v2(SC_ZENKAI) {
            atk_add(lh, &mut wd.damage, &mut wd.damage2, 200);
            atk_add(lh, &mut wd.equip_atk, &mut wd.equip_atk2, 200);
        }
        if s.has(SC_EQC) {
            atk_addrate(lh, &mut wd.damage, &mut wd.damage2, -(s.v2(SC_EQC) as i64));
            atk_addrate(lh, &mut wd.equip_atk, &mut wd.equip_atk2, -(s.v2(SC_EQC) as i64));
        }
        if s.has(SC_STYLE_CHANGE) {
            // (battle.cpp:4630-4637) hd = BL_CAST(BL_HOM, src): its spirit spheres * 3
            if let Some(hd) = (src.kind == crate::map::BL_HOM).then(|| crate::homunculus::data::get(c, src.id)).flatten() {
                let a = hd.homunculus.spiritball as i64 * 3;
                atk_add(lh, &mut wd.damage, &mut wd.damage2, a);
                re_allatk_add(lh, wd, a);
            }
        }
        if s.has(SC_UNLIMIT) && wd.flag & (BF_LONG | BF_MAGIC) == BF_LONG && !matches!(skill_id, RA_WUGDASH | RA_WUGSTRIKE | RA_WUGBITE) {
            atk_addrate(lh, &mut wd.damage, &mut wd.damage2, s.v2(SC_UNLIMIT) as i64);
            re_allatk_addrate(lh, wd, s.v2(SC_UNLIMIT) as i64);
        }
        if s.has(SC_HEAT_BARREL) {
            atk_addrate(lh, &mut wd.damage, &mut wd.damage2, s.v3(SC_HEAT_BARREL) as i64);
            re_allatk_addrate(lh, wd, s.v3(SC_HEAT_BARREL) as i64);
        }
        if wd.flag & (BF_LONG | BF_MAGIC) == BF_LONG && s.has(SC_ARCLOUSEDASH) && s.v4(SC_ARCLOUSEDASH) != 0 {
            atk_addrate(lh, &mut wd.damage, &mut wd.damage2, s.v4(SC_ARCLOUSEDASH) as i64);
            re_allatk_addrate(lh, wd, s.v4(SC_ARCLOUSEDASH) as i64);
        }
        if sd.is_some() && wd.flag & BF_WEAPON != 0 && s.has(SC_GVG_GIANT) && s.v3(SC_GVG_GIANT) != 0 {
            atk_addrate(lh, &mut wd.damage, &mut wd.damage2, s.v3(SC_GVG_GIANT) as i64);
            re_allatk_addrate(lh, wd, s.v3(SC_GVG_GIANT) as i64);
        }
        if skill_id == 0 && s.has(SC_EXEEDBREAK) {
            atk_addrate(lh, &mut wd.damage, &mut wd.damage2, s.v2(SC_EXEEDBREAK) as i64);
            re_allatk_addrate(lh, wd, s.v2(SC_EXEEDBREAK) as i64);
        }
        if s.has(SC_PYREXIA) && s.v3(SC_PYREXIA) == 0 && skill_id == 0 {
            atk_addrate(lh, &mut wd.damage, &mut wd.damage2, s.v2(SC_PYREXIA) as i64);
            re_allatk_addrate(lh, wd, s.v2(SC_PYREXIA) as i64);
        }
        if s.has(SC_MIRACLE) {
            anger_id = 2; // Always treat all monsters as star flagged monster when in miracle state
        }
        if s.has(SC_HIDDEN_CARD) && wd.flag & BF_LONG == BF_LONG {
            re_allatk_addrate(lh, wd, s.v3(SC_HIDDEN_CARD) as i64);
        }
    }
    if wd.flag & (BF_LONG | BF_MAGIC) == BF_LONG {
        if let Some(sd) = &sd {
            if bl::checkskill(c, Some(sd), SU_POWEROFLIFE) > 0 && bl::checkskill_summoner(c, sd, SUMMONER_POWER_LIFE) >= 20 {
                atk_addrate(lh, &mut wd.damage, &mut wd.damage2, 20);
                re_allatk_addrate(lh, wd, 20);
            }
        }
    }
    let Some(sd) = &sd else { return };
    if anger_id == 0 {
        // ARR_FIND(0, MAX_PC_FEELHATE, anger_id, status_get_class(target) == sd->hate_mob[anger_id])
        let class = bl::class_of(c, target);
        let hate = super::pc_death::hate_mobs(c, sd.aid);
        anger_id = hate.iter().position(|&h| h == class).unwrap_or(MAX_PC_FEELHATE);
    }
    // sg_info[].anger_id: SG_SUN_ANGER, SG_MOON_ANGER, SG_STAR_ANGER
    const ANGER: [u16; MAX_PC_FEELHATE] = [SG_SUN_ANGER, SG_MOON_ANGER, SG_STAR_ANGER];
    if anger_id < MAX_PC_FEELHATE {
        let anger_level = bl::checkskill(c, Some(sd), ANGER[anger_id]);
        if anger_level != 0 {
            let mut skillratio = sd.ch.base_level as i32 + sstatus.dex as i32 + sstatus.luk as i32;
            if anger_id == 2 {
                skillratio += sstatus.str as i32; // SG_STAR_ANGER additionally has STR added in its formula.
            }
            if anger_level < 4 {
                skillratio /= 12 - 3 * anger_level;
            }
            // (renewal) maximum damage bonus limit : (skill level x 25)%
            skillratio = skillratio.min(25 * anger_level);
            atk_addrate(lh, &mut wd.damage, &mut wd.damage2, skillratio as i64);
            re_allatk_addrate(lh, wd, skillratio as i64);
        }
    }
}

/// pc.hpp MAX_PC_FEELHATE.
pub const MAX_PC_FEELHATE: usize = 3;

/// battle.cpp battle_calc_defense_reduction (RENEWAL): hard DEF as a ratio, soft DEF subtracted.
pub(super) fn battle_calc_defense_reduction(c: &Ctx, wd: &mut Damage, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16) {
    let sd = bl::sd(c, src);
    let tsd = bl::sd(c, target);
    let scl = bl::sc(c, src);
    let tsc = bl::sc(c, target);
    let sstatus = bl::status(c, src);
    let tstatus = bl::status(c, target);
    // Defense reduction (defType and int16: stored back into 16 bits)
    let mut vit_def: i16;
    let mut def1: i16 = bl::def_of(c, target); // Don't use tstatus->def1 due to skill timer reductions.
    let mut def2: i16 = tstatus.def2;
    if let Some(sd) = &sd {
        let ib = &sd.st.indexed_bonus;
        let at = |v: &Vec<i32>, i: usize| v.get(i).copied().unwrap_or(0);
        let mut i = at(&ib.ignore_def_by_race, tstatus.race as usize) + at(&ib.ignore_def_by_race, RC_ALL as usize);
        i += at(&ib.ignore_def_by_class, tstatus.class_ as usize) + at(&ib.ignore_def_by_class, CLASS_ALL as usize);
        if i != 0 {
            i = i.min(100); // cap it to 100 for 0 def min
            def1 = (def1 as i32 - def1 as i32 * i / 100) as i16;
            def2 = (def2 as i32 - def2 as i32 * i / 100) as i16;
        }
        // Kagerou/Oboro Earth Charm effect +10% eDEF
        let (charm, charm_type) = spiritcharm(c, Some(sd));
        if charm_type == crate::skill::state::CHARM_TYPE_LAND && charm > 0 {
            let si = (10 * charm) as i16;
            def1 = (def1 as i32 * (100 + si as i32) / 100) as i16;
        }
    }
    if let Some(s) = &scl {
        if s.has(SC_EXPIATIO) {
            let i = ((5 * s.v1(SC_EXPIATIO)) as i16).min(100) as i32; // 5% per level, capped to 100 for 0 def min
            def1 = (def1 as i32 * (100 - i) / 100) as i16;
            def2 = (def2 as i32 * (100 - i) / 100) as i16;
        }
    }
    if let Some(ts) = &tsc {
        if ts.has(SC_FORCEOFVANGUARD) {
            let i = (2 * ts.v1(SC_FORCEOFVANGUARD)) as i16 as i32;
            def1 = (def1 as i32 * (100 + i) / 100) as i16;
        }
        if ts.has(SC_CAMOUFLAGE) {
            let i = ((5 * ts.v3(SC_CAMOUFLAGE)) as i16 as i32).min(100); // 5% per second
            def1 = (def1 as i32 * (100 - i) / 100) as i16;
            def2 = (def2 as i32 * (100 - i) / 100) as i16;
        }
        if ts.has(SC_GT_REVITALIZE) {
            def1 = (def1 as i32 + ts.v4(SC_GT_REVITALIZE)) as i16;
        }
        if ts.has(SC_OVERED_BOOST) && target.kind == BL_PC {
            def1 = (def1 as i32 * ts.v4(SC_OVERED_BOOST) / 100) as i16;
        }
    }
    if conf::vit_penalty_type() != 0 && conf::vit_penalty_target() & target.kind != 0 {
        // Official servers limit the count to 22 targets (unsigned char)
        let target_count = (target.ud.target_count as i32).min((100 / conf::vit_penalty_num()) + (conf::vit_penalty_count() - 1)) as u8 as i32;
        if target_count >= conf::vit_penalty_count() {
            let steel = tsc.as_ref().map(|s| s.has(SC_STEELBODY)).unwrap_or(false);
            let n = (target_count - (conf::vit_penalty_count() - 1)) * conf::vit_penalty_num();
            if conf::vit_penalty_type() == 1 {
                if !steel {
                    def1 = (def1 as i32 * (100 - n) / 100) as i16;
                }
                def2 = (def2 as i32 * (100 - n) / 100) as i16;
            } else {
                if !steel {
                    def1 = (def1 as i32 - n) as i16;
                }
                def2 = (def2 as i32 - n) as i16;
            }
        }
        if def2 < 1 {
            def2 = 1;
        }
    }
    if skill_id == AM_ACIDTERROR {
        def2 = 0; // Ignore only status defense.
    }
    // Damage reduction based on vitality
    if let Some(tsd) = &tsd {
        vit_def = def2;
        let race = sstatus.race as i32;
        let skill = bl::checkskill(c, Some(tsd), AL_DP);
        if src.kind == BL_MOB && (sc::battle_check_undead(race, sstatus.def_ele as i32) || race == RC_DEMON) && skill > 0 {
            // This bonus already doesn't work vs players
            // (int32)(((float)base_level / 25.0 + 3.0) * skill + 0.5): float promoted to double
            vit_def = (vit_def as i32 + (((tsd.ch.base_level as f32) as f64 / 25.0 + 3.0) * skill as f64 + 0.5) as i32) as i16;
        }
        let skill = bl::checkskill(c, Some(tsd), RA_RANGERMAIN);
        if src.kind == BL_MOB && skill > 0 && (race == RC_BRUTE || race == RC_PLAYER_DORAM || race == RC_FISH || race == RC_PLANT) {
            vit_def = (vit_def as i32 + skill * 5) as i16;
        }
        let skill = bl::checkskill(c, Some(tsd), NC_RESEARCHFE);
        if src.kind == BL_MOB && skill > 0 && (sstatus.def_ele as i32 == ELE_FIRE || sstatus.def_ele as i32 == ELE_EARTH) {
            vit_def = (vit_def as i32 + skill * 10) as i16;
        }
    } else {
        // SoftDEF of monsters is floor((BaseLevel+Vit)/2)
        vit_def = def2;
    }
    if conf::weapon_defense_type() != 0 {
        vit_def = (vit_def as i32 + def1 as i32 * conf::weapon_defense_type()) as i16;
        def1 = 0;
    }
    let nk = battle_skill_get_damage_properties(skill_id, wd.miscflag);
    let lh = is_attack_left_handed(c, src, skill_id);
    if nk_has(nk, NK_SIMPLEDEFENSE) {
        // Defense reduction by flat value. This completely bypasses the normal RE DEF Reduction formula.
        wd.damage -= def1 as i64 + vit_def as i64;
        if lh {
            wd.damage2 -= def1 as i64 + vit_def as i64;
        }
    } else {
        // RE DEF Reduction: Damage = Attack * (4000+eDEF)/(4000+eDEF*10) - sDEF; pierce defence gains 1 atk per def/2
        if def1 == -400 {
            def1 = -399; // -400 creates a division by 0 and subsequently crashes
        }
        // (def1 * battle_calc_attack_skill_ratio(…)) / 200: int arithmetic
        let a = if is_attack_piercing(c, src, target, skill_id, EQI_HAND_R) != 0 {
            (def1 as i32).wrapping_mul(battle_calc_attack_skill_ratio(c, wd, src, target, skill_id, skill_lv)) / 200
        } else {
            0
        };
        let b = if is_attack_piercing(c, src, target, skill_id, EQI_HAND_L) != 0 {
            (def1 as i32).wrapping_mul(battle_calc_attack_skill_ratio(c, wd, src, target, skill_id, skill_lv)) / 200
        } else {
            0
        };
        atk_add2(lh, &mut wd.damage, &mut wd.damage2, a as i64, b as i64);
        if !attack_ignores_def(c, wd, src, target, skill_id, EQI_HAND_R) && is_attack_piercing(c, src, target, skill_id, EQI_HAND_R) == 0 {
            wd.damage = wd.damage * (4000 + def1 as i64) / (4000 + 10 * def1 as i64) - vit_def as i64;
        }
        if lh && !attack_ignores_def(c, wd, src, target, skill_id, EQI_HAND_L) && is_attack_piercing(c, src, target, skill_id, EQI_HAND_L) == 0 {
            wd.damage2 = wd.damage2 * (4000 + def1 as i64) / (4000 + 10 * def1 as i64) - vit_def as i64;
        }
    }
}

/// battle.cpp battle_calc_attack_post_defense (RENEWAL): Aura Blade, the minimum of 1, Sonic Acceleration.
fn battle_calc_attack_post_defense(c: &Ctx, wd: &mut Damage, src: &Unit, _target: &Unit, skill_id: u16, _skill_lv: u16) {
    let sd = bl::sd(c, src);
    let lh = is_attack_left_handed(c, src, skill_id);
    if let Some(s) = bl::sc(c, src) {
        // SC skill damages
        if s.has(SC_AURABLADE) {
            let v = (3 + s.v1(SC_AURABLADE) as i64) * bl::lv_of(c, src) as i64; // !TODO: Confirm formula
            atk_add(lh, &mut wd.damage, &mut wd.damage2, v);
        }
    }
    // Set to min of 1
    battle_min_damage(c, wd, src, skill_id, 1);
    if skill_id == AS_SONICBLOW && bl::checkskill(c, sd.as_ref(), AS_SONICACCEL) > 0 {
        atk_addrate(lh, &mut wd.damage, &mut wd.damage2, 90);
    }
}

/// battle.cpp battle_calc_attack_plant: "plant" targets take 1 damage from every hit.
fn battle_calc_attack_plant(c: &Ctx, wd: &mut Damage, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16) {
    let tstatus = bl::status(c, target);
    let attack_hits = is_attack_hitting(c, wd, src, target, skill_id, skill_lv, false);
    if skill_id != SN_SHARPSHOOTING && skill_id != RA_ARROWSTORM {
        sc::status_change_end(c, src.id, SC_CAMOUFLAGE, 0);
    }
    // Plants receive 1 damage when hit
    if attack_hits || wd.damage > 0 {
        wd.damage = 1; // In some cases, right hand no need to have a weapon to deal a damage
    }
    if is_attack_left_handed(c, src, skill_id) && (attack_hits || wd.damage2 > 0) {
        let katar = bl::sd(c, src).map(|s| s.ch.weapon as i32 == W_KATAR).unwrap_or(false);
        wd.damage2 = if katar { 0 } else { 1 }; // No backhand damage against plants
    }
    if attack_hits && target.kind == BL_MOB {
        if bl::sc(c, target).is_some() {
            let mut damage_dummy: i64 = 1;
            if !fix::battle_status_block_damage(c, src, target, wd, &mut damage_dummy, skill_id, skill_lv) {
                // Statuses that reduce damage to 0.
                wd.damage = 0;
                wd.damage2 = 0;
                return;
            }
        }
    }
    if attack_hits && bl::class_of(c, target) == super::mobif::MOBID_EMPERIUM as i32 {
        let flag = if skill_id != 0 { BF_SKILL } else { 0 };
        if !fix::battle_can_hit_gvg_target(c, src, target, skill_id, flag) && crate::npc::mapflag_gvg2(c, target.map) {
            wd.damage = 0;
            wd.damage2 = 0;
            return;
        }
        let right_element = battle_get_weapon_element(c, wd, src, target, skill_id, skill_lv, EQI_HAND_R, false);
        let left_element = battle_get_weapon_element(c, wd, src, target, skill_id, skill_lv, EQI_HAND_L, false);
        let (de, dl) = (tstatus.def_ele as i32, tstatus.ele_lv as i32);
        if wd.damage > 0 {
            wd.damage = battle_attr_fix(c, Some(src), Some(target), wd.damage, right_element, de, dl, 0);
            wd.damage = fix::battle_calc_gvg_damage(c, src, target, wd.damage, skill_id, wd.flag);
        } else if wd.damage2 > 0 {
            wd.damage2 = battle_attr_fix(c, Some(src), Some(target), wd.damage2, left_element, de, dl, 0);
            wd.damage2 = fix::battle_calc_gvg_damage(c, src, target, wd.damage2, skill_id, wd.flag);
        }
        return;
    }
    // Triple Attack and Finger Offensive have a special property, they do not split damage on plant mode
    if (skill_id == MO_TRIPLEATTACK || skill_id == MO_FINGEROFFENSIVE) && wd.div_ < 0 {
        wd.div_ = -wd.div_;
    }
    // For plants we don't continue with the weapon attack code, so we have to apply DAMAGE_DIV_FIX here
    battle_apply_div_fix(wd, skill_id);
    // If there is left hand damage, total damage can never exceed 2, even on multiple hits
    if wd.damage > 1 && wd.damage2 > 0 {
        wd.damage = 1;
        wd.damage2 = 1;
    }
}

/// battle.cpp battle_calc_attack_left_right_hands: katar back-hand, dual-wield ratios (Righthand / Lefthand
/// Mastery).
fn battle_calc_attack_left_right_hands(c: &Ctx, wd: &mut Damage, src: &Unit, skill_id: u16) {
    let rh = is_attack_right_handed(c, src, skill_id);
    let lh = is_attack_left_handed(c, src, skill_id);
    if let Some(sd) = bl::sd(c, src) {
        let weapon = sd.ch.weapon as i32;
        if weapon == W_KATAR && skill_id == 0 {
            // Katars (offhand damage only applies to normal attacks, tested on Aegis 10.2)
            let skill = bl::checkskill(c, Some(&sd), TF_DOUBLE);
            wd.damage2 = wd.damage * (1 + (skill as i64 * 2)) / 100;
        } else if lh && weapon != W_KATAR {
            // Dual-wield: with only a left-hand weapon, the main hand is an unarmed attack
            let class_ = sd.st.class_;
            if rh && wd.damage != 0 {
                if class_ & crate::db::jobs::MAPID_FIRSTMASK == crate::db::jobs::MAPID_THIEF {
                    let skill = bl::checkskill(c, Some(&sd), AS_RIGHT);
                    wd.damage = wd.damage * (50 + skill as i64 * 10) / 100;
                } else if class_ == crate::db::jobs::MAPID_KAGEROUOBORO {
                    let skill = bl::checkskill(c, Some(&sd), KO_RIGHT);
                    wd.damage = wd.damage * (70 + skill as i64 * 10) / 100;
                }
                if wd.damage < 1 {
                    wd.damage = 1;
                }
            }
            // Left hand damage will always be adjusted, even if you don't have a weapon in the main hand
            if wd.damage2 != 0 {
                if class_ & crate::db::jobs::MAPID_FIRSTMASK == crate::db::jobs::MAPID_THIEF {
                    let skill = bl::checkskill(c, Some(&sd), AS_LEFT);
                    wd.damage2 = wd.damage2 * (30 + skill as i64 * 10) / 100;
                } else if class_ == crate::db::jobs::MAPID_KAGEROUOBORO {
                    let skill = bl::checkskill(c, Some(&sd), KO_LEFT);
                    wd.damage2 = wd.damage2 * (50 + skill as i64 * 10) / 100;
                }
                if wd.damage2 < 1 {
                    wd.damage2 = 1;
                }
            }
        }
    }
    if !rh && !lh && wd.damage != 0 {
        wd.damage = 0;
    }
    if !lh && wd.damage2 != 0 {
        wd.damage2 = 0;
    }
}

/// battle.cpp battle_check_devotion: the devoting player that takes reflected damage (devotion_rdamage chance).
fn battle_check_devotion(c: &Ctx, bl: &Unit) -> Option<Unit> {
    if conf::devotion_rdamage() != 0 && conf::devotion_rdamage() > (c.rnd() % 100) as i32 {
        if let Some(e) = sc::get_sce(c, bl.id, SC_DEVOTION) {
            return crate::unit::get(c, e.val1 as u32);
        }
    }
    None
}

/// battle.cpp battle_calc_attack_gvg_bg: item reflect, then battle_calc_damage and the GvG / battleground rates.
fn battle_calc_attack_gvg_bg(c: &Ctx, wd: &mut Damage, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16) {
    if wd.damage + wd.damage2 == 0 {
        return;
    }
    // Don't reflect your own damage (Grand Cross); `!skill_id || skill_id || …` is always true
    if src.id != target.id {
        let mut damage = wd.damage + wd.damage2;
        let tsd = bl::sd(c, target);
        let sstatus = bl::status(c, src);
        let tick = c.tick();
        let rdamage = fix::battle_calc_return_damage(c, target, src, &mut damage, wd.flag, skill_id, false);
        if rdamage > 0 {
            // Item reflect gets calculated before any mapflag reducing is applicated
            let d_bl = battle_check_devotion(c, src);
            let to = d_bl.clone().unwrap_or_else(|| src.clone());
            super::clif::clif_damage(c, src, &to, tick, wd.amotion, sstatus.dmotion as i32, rdamage, 1, DMG_ENDURE, 0, false);
            if let Some(tsd) = &tsd {
                super::weapon_attack::battle_drain(c, tsd, src, rdamage, rdamage, sstatus.race as i32, sstatus.class_ as i32);
            }
            // Use Reflect Shield to signal this kind of skill trigger [Skotlex]
            super::weapon_attack::battle_delay_damage(c, tick, wd.amotion, target, &to, 0, CR_REFLECTSHIELD, 0, rdamage, ATK_DEF, 1, true, false, false);
            super::addeff::skill_additional_effect(c, target, &to, CR_REFLECTSHIELD, 1, BF_WEAPON | BF_SHORT | BF_NORMAL, ATK_DEF, tick);
        }
    }
    let gvg = crate::npc::mapflag_gvg2(c, target.map);
    let bg = crate::npc::mapflag(c, target.map, crate::npc::MF_BATTLEGROUND);
    if wd.damage2 == 0 {
        let d = wd.damage;
        wd.damage = fix::battle_calc_damage(c, src, target, wd, d, skill_id, skill_lv);
        if gvg {
            wd.damage = fix::battle_calc_gvg_damage(c, src, target, wd.damage, skill_id, wd.flag);
        } else if bg {
            wd.damage = fix::battle_calc_bg_damage(c, src, target, wd.damage, skill_id, wd.flag);
        }
    } else if wd.damage == 0 {
        let d = wd.damage2;
        wd.damage2 = fix::battle_calc_damage(c, src, target, wd, d, skill_id, skill_lv);
        if gvg {
            wd.damage2 = fix::battle_calc_gvg_damage(c, src, target, wd.damage2, skill_id, wd.flag);
        } else if bg {
            wd.damage2 = fix::battle_calc_bg_damage(c, src, target, wd.damage2, skill_id, wd.flag);
        }
    } else {
        let d = wd.damage;
        wd.damage = fix::battle_calc_damage(c, src, target, wd, d, skill_id, skill_lv);
        let d2 = wd.damage2;
        wd.damage2 = fix::battle_calc_damage(c, src, target, wd, d2, skill_id, skill_lv);
        if gvg {
            wd.damage = fix::battle_calc_gvg_damage(c, src, target, wd.damage, skill_id, wd.flag);
            wd.damage2 = fix::battle_calc_gvg_damage(c, src, target, wd.damage2, skill_id, wd.flag);
        } else if bg {
            wd.damage = fix::battle_calc_bg_damage(c, src, target, wd.damage, skill_id, wd.flag);
            wd.damage2 = fix::battle_calc_bg_damage(c, src, target, wd.damage2, skill_id, wd.flag);
        }
        if wd.damage > 1 && wd.damage2 < 1 {
            wd.damage2 = 1;
        }
    }
}

/// battle.cpp battle_calc_weapon_final_atk_modifiers: final ATK modifiers after the GvG / battleground rates.
fn battle_calc_weapon_final_atk_modifiers(c: &Ctx, wd: &mut Damage, src: &Unit, target: &Unit, skill_id: u16, _skill_lv: u16) {
    let sd = bl::sd(c, src);
    let tsd = bl::sd(c, target);
    let tsc = bl::sc(c, target);
    let sstatus = bl::status(c, src);
    let tstatus = bl::status(c, target);
    let lh = is_attack_left_handed(c, src, skill_id);
    let tick = c.tick();
    // Reject Sword bugreport:4493 by Daegaladh
    if let Some(ts) = &tsc {
        if wd.damage != 0 && ts.has(SC_REJECTSWORD) {
            let ok_weapon = src.kind != BL_PC
                || sd
                    .as_ref()
                    .map(|sd| {
                        let (w1, w) = (sd.it.weapontype1 as i32, sd.ch.weapon as i32);
                        w1 == W_DAGGER || w1 == W_1HSWORD || w == W_2HSWORD
                    })
                    .unwrap_or(false);
            if ok_weapon && ((c.rnd() % 100) as i32) < ts.v2(SC_REJECTSWORD) {
                wd.damage = wd.damage * 50 / 100; // ATK_RATER(wd->damage, 50)
                crate::skill::clif::clif_skill_nodamage(c, Some(target), target, ST_REJECTSWORD, ts.v1(SC_REJECTSWORD), true);
                super::clif::clif_damage(c, target, src, tick, 0, 0, wd.damage, 0, DMG_NORMAL, 0, false);
                super::weapon_attack::battle_fix_damage(c, Some(target), src, wd.damage, wd.div_, ST_REJECTSWORD);
                let target_now = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
                if sc::status_isdead(c, &target_now) {
                    return;
                }
                if let Some(mut e) = sc::get_sce(c, target.id, SC_REJECTSWORD) {
                    e.val3 -= 1;
                    sc::put_sce(c, &e);
                    if e.val3 <= 0 {
                        sc::status_change_end(c, target.id, SC_REJECTSWORD, 0);
                    }
                }
            }
        }
    }
    // Poison React Envenom Level 5 Autocast
    if tsc.is_some() && wd.damage > 0 {
        if let Some(sce) = sc::get_sce(c, target.id, SC_POISONREACT) {
            if c.rnd_chance_official(sce.val3, 100) {
                if super::mobif::status_check_skilluse(c, Some(target), Some(src), TF_POISON, 0) {
                    crate::skill::attack::skill_attack(c, BF_WEAPON, target, target, src, TF_POISON, 5, tick, 0);
                }
                // Counter is reduced even if the autocast fails
                if let Some(mut sce) = sc::get_sce(c, target.id, SC_POISONREACT) {
                    sce.val2 -= 1;
                    sc::put_sce(c, &sce);
                    if sce.val2 <= 0 {
                        sc::status_change_end(c, target.id, SC_POISONREACT, 0);
                    }
                }
            }
        }
    }
    if let Some(sce) = sc::get_sce(c, target.id, SC_CRESCENTELBOW).filter(|_| tsc.is_some()) {
        if wd.flag & BF_SHORT != 0 && ((c.rnd() % 100) as i32) < sce.val2 {
            // ATK [{(Target HP / 100) x Skill Level} x Caster Base Level / 125] % + [Received damage x {1 + (Skill Level x 0.2)}]
            let sstatus = bl::status(c, src);
            let ratio64: i64 = (sstatus.hp / 100) as i64 * sce.val1 as i64 * bl::lv_of(c, target) as i64 / 125;
            let mut ratio: i32 = ratio64 as i32;
            if ratio > 5000 {
                ratio = 5000; // Maximum of 5000% ATK
            }
            let mut rdamage = battle_calc_base_damage(c, target, false, sstatus.size, 0);
            rdamage = rdamage * ratio as i64 / 100 + wd.damage * (10 + sce.val1 as i64 * 20 / 10) / 10;
            let dir = crate::unit::getdir(src) as i8;
            crate::skill::attack::skill_blown(c, target, src, skill_get_blewcount(SR_CRESCENTELBOW_AUTOSPELL, sce.val1 as u16), dir, crate::skill::attack::BLOWN_NONE);
            let (src_now, target_now) = (crate::unit::get(c, src.id).unwrap_or_else(|| src.clone()), crate::unit::get(c, target.id).unwrap_or_else(|| target.clone()));
            // This is how official does
            crate::skill::clif::clif_skill_damage(c, &target_now, &src_now, tick, sstatus.amotion as i32, 0, rdamage, 1, SR_CRESCENTELBOW_AUTOSPELL, sce.val1, DMG_SINGLE);
            super::clif::clif_damage(c, &src_now, &target_now, tick, sstatus.amotion as i32 + 1000, 0, rdamage / 10, 1, DMG_NORMAL, 0, false);
            super::weapon_attack::battle_fix_damage(c, Some(&target_now), &src_now, rdamage, 0, SR_CRESCENTELBOW);
            crate::status::hp::status_damage(c, Some(&src_now), target.id, rdamage / 10, 0, 0, 0, 1, 0);
            sc::status_change_end(c, target.id, SC_CRESCENTELBOW, 0);
        }
    }
    if let Some(s) = bl::sc(c, src) {
        // SC_FUSION hp penalty [Komurka]
        if s.has(SC_FUSION) {
            let mut hp = sstatus.max_hp;
            if sd.is_some() && tsd.is_some() {
                hp /= 13;
                if sstatus.hp as i64 * 100 <= sstatus.max_hp as i64 * 20 {
                    hp = sstatus.hp;
                }
            } else {
                hp = 2u32.wrapping_mul(hp) / 100; // 2% hp loss per hit
            }
            crate::status::hp::status_zap(c, src.id, hp as i64, 0, 0);
        }
        if s.has(SC_VIGOR) {
            crate::status::hp::status_zap(c, src.id, s.v2(SC_VIGOR) as i64, 0, 0);
        }
        // Only affecting non-skills
        if skill_id == 0 && wd.dmg_lv > ATK_BLOCK && s.has(SC_ENCHANTBLADE) {
            // [((Skill Lv x 20) + 100) x (casterBaseLevel / 150)] + casterInt + MATK - MDEF - MDEF2
            let mut enchant_dmg = s.v2(SC_ENCHANTBLADE) as i64;
            let (mn, mx) = (sstatus.matk_min as i64, sstatus.matk_max as i64);
            if mx > mn {
                enchant_dmg = enchant_dmg + mn + (c.rnd() as i64 % (mx - mn));
            } else {
                enchant_dmg += mn;
            }
            enchant_dmg -= tstatus.mdef as i64 + tstatus.mdef2 as i64;
            if enchant_dmg > 0 {
                atk_add(lh, &mut wd.damage, &mut wd.damage2, enchant_dmg);
            }
        }
        if skill_id != SN_SHARPSHOOTING && skill_id != RA_ARROWSTORM {
            sc::status_change_end(c, src.id, SC_CAMOUFLAGE, 0);
        }
    }
    // Skill damage adjustment
    let skill_damage = battle_skill_damage(c, src, Some(target), skill_id);
    if skill_damage != 0 {
        atk_addrate(lh, &mut wd.damage, &mut wd.damage2, skill_damage as i64);
    }
}

// ---------------------------------------------------------------------------------------------- skill damage db

/// battle.cpp battle_skill_damage_type: the rate column of a target (SKILLDMG_PC / MOB / BOSS / OTHER).
fn battle_skill_damage_type(c: &Ctx, bl: &Unit) -> usize {
    match bl.kind {
        BL_PC => SKILLDMG_PC,
        BL_MOB => {
            if bl::class_type(c, bl) == CLASS_BOSS {
                SKILLDMG_BOSS
            } else {
                SKILLDMG_MOB
            }
        }
        _ => SKILLDMG_OTHER,
    }
}

/// battle.cpp battle_skill_damage_skill: the skill's rate from skill_damage_db.txt on this kind of map.
fn battle_skill_damage_skill(c: &Ctx, src: &Unit, target: &Unit, skill_id: u16) -> i32 {
    use crate::npc::{mapflag, mapflag_value, MF_BATTLEGROUND, MF_PVP, MF_RESTRICTED, MF_SKILL_DAMAGE};
    let Some(skill) = skill_db().find(skill_id) else { return 0 };
    let damage = &skill.damage;
    if damage.map == 0 {
        return 0;
    }
    // check the adjustment works for specified type
    if damage.caster & src.kind == 0 {
        return 0;
    }
    let m = src.map;
    let (pvp, gvg2, bg, sdmg, restricted) = (mapflag(c, m, MF_PVP), crate::npc::mapflag_gvg2(c, m), mapflag(c, m, MF_BATTLEGROUND), mapflag(c, m, MF_SKILL_DAMAGE), mapflag(c, m, MF_RESTRICTED));
    // mapdata->zone: the restricted zone bits (npc.rs keeps them as MF_RESTRICTED's value)
    let zone = mapflag_value(c, m, MF_RESTRICTED) as u32;
    if (damage.map & 1 != 0 && !pvp && !gvg2 && !bg && !sdmg && !restricted)
        || (damage.map & 2 != 0 && pvp)
        || (damage.map & 4 != 0 && gvg2)
        || (damage.map & 8 != 0 && bg)
        || (damage.map & 16 != 0 && sdmg)
        || (damage.map & zone != 0 && restricted)
    {
        return damage.rate[battle_skill_damage_type(c, target)];
    }
    0
}

/// battle.cpp battle_skill_damage_map: the rate of the map's 'skill_damage' mapflag: mapdata->damage_adjust's for
/// every skill when its caster bits take src's type, plus this skill's mapdata->skill_damage entry's under the same
/// condition (map_skill_damage.rs).
fn battle_skill_damage_map(c: &Ctx, src: &Unit, target: &Unit, skill_id: u16) -> i32 {
    if !crate::npc::mapflag(c, src.map, crate::npc::MF_SKILL_DAMAGE) {
        return 0;
    }
    let mut rate = 0;
    // Damage rate for all skills at this map
    let adjust = crate::map_skill_damage::damage_adjust(c, src.map);
    if adjust.caster & src.kind != 0 {
        rate = adjust.rate[battle_skill_damage_type(c, target)];
    }
    // Damage rate for specified skill at this map
    if let Some(d) = crate::map_skill_damage::skill_damage(c, src.map, skill_id) {
        if d.caster & src.kind != 0 {
            rate += d.rate[battle_skill_damage_type(c, target)];
        }
    }
    rate
}

/// battle.cpp battle_skill_damage: skill_damage_db.txt and the 'skill_damage' mapflag (total rate, % of damage).
pub(super) fn battle_skill_damage(c: &Ctx, src: &Unit, target: Option<&Unit>, skill_id: u16) -> i32 {
    let Some(target) = target else { return 0 };
    if skill_id == 0 {
        return 0;
    }
    let skill_id = skill_dummy2skill_id(skill_id);
    battle_skill_damage_skill(c, src, target, skill_id) + battle_skill_damage_map(c, src, target, skill_id)
}

/// battle.cpp battle_calc_chorusbonus: the Minstrel / Wanderer party bonus of the Chorus skills (none in RENEWAL).
pub fn battle_calc_chorusbonus(_c: &Ctx, _sd: Option<&Sd>) -> i32 {
    0 // #ifdef RENEWAL: No bonus in renewal
}

/// battle.cpp battle_blewcount_bonus: bSkillBlown of the skill.
pub(super) fn battle_blewcount_bonus(sd: &Sd, skill_id: u16) -> i32 {
    // Apply the bonus blewcount. [Skotlex]
    for it in &sd.st.lists.skillblown {
        if it.id == skill_id {
            return it.val;
        }
    }
    0
}

/// battle.cpp initialize_weapon_data: the Damage of an attack before HIT / MISS / DEF.
pub(super) fn initialize_weapon_data(c: &Ctx, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16, wflag: i32) -> Damage {
    let sstatus = bl::status(c, src);
    let tstatus = bl::status(c, target);
    let scl = bl::sc(c, src);
    let mut wd = Damage {
        type_: DMG_NORMAL,
        div_: if skill_id != 0 { skill_get_num(skill_id, skill_lv) as i16 } else { 1 },
        // Amotion should be 0 for ground skills.
        amotion: if skill_id != 0 && skill_get_inf(skill_id) & INF_GROUND_SKILL as i32 != 0 { 0 } else { sstatus.amotion as i32 },
        dmotion: tstatus.dmotion as i32,
        blewcount: skill_get_blewcount(skill_id, skill_lv),
        miscflag: wflag,
        flag: BF_WEAPON,
        dmg_lv: ATK_DEF, // This assumption simplifies the assignation later
        ..Default::default()
    };
    // Baphomet card's splash damage is counted as a skill. [Inkfish]
    wd.flag |= if skill_id != 0 || wd.miscflag != 0 { BF_SKILL } else { BF_NORMAL };
    if let Some(sd) = bl::sd(c, src) {
        wd.blewcount += battle_blewcount_bonus(&sd, skill_id);
    }
    if skill_id != 0 {
        wd.flag |= battle_range_type(c, src, target, skill_id, skill_lv);
    } else if is_skill_using_arrow(c, src, skill_id) || scl.as_ref().map(|s| s.has(SC_SOULATTACK)).unwrap_or(false) {
        wd.flag |= BF_LONG;
    } else {
        wd.flag |= BF_SHORT;
    }
    wd
}

/// battle.cpp battle_range_type: a skill's range class (BF_SHORT / BF_LONG).
pub(super) fn battle_range_type(c: &Ctx, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16) -> i32 {
    // [Akinari] , [Xynvaroth]: Traps are always short range.
    if skill_get_inf2(skill_id, &[INF2_ISTRAP]) {
        return BF_SHORT;
    }
    match skill_id {
        AC_SHOWER | AM_DEMONSTRATION if src.kind == BL_MOB => return BF_SHORT,
        KN_BRANDISHSPEAR | SR_RAMPAGEBLASTER | BO_ACIDIFIED_ZONE_WATER_ATK | BO_ACIDIFIED_ZONE_FIRE_ATK | BO_ACIDIFIED_ZONE_GROUND_ATK | BO_ACIDIFIED_ZONE_WIND_ATK | NW_THE_VIGILANTE_AT_NIGHT | SS_KUNAIKAITEN | SS_KUNAIKUSSETSU | SS_HITOUAKUMU => return BF_LONG,
        NJ_KIRIKAGE | GC_CROSSIMPACT | DK_SERVANT_W_PHANTOM | SHC_SAVAGE_IMPACT | SHC_FATAL_SHADOW_CROW | MT_RUSH_QUAKE | MT_RUSH_STRIKE | ABC_UNLUCKY_RUSH | ABC_CHASING_BREAK | MH_THE_ONE_FIGHTER_RISES | NPC_MAXPAIN_ATK | SS_SHIMIRU | SKE_STAR_LIGHT_KICK => return BF_SHORT,
        CD_EFFLIGO | CD_PETITIO => {
            if let Some(sd) = bl::sd(c, src) {
                let w = sd.ch.weapon as i32;
                if w == W_MACE || w == W_2HMACE {
                    return BF_LONG;
                }
            }
        }
        DK_HACKANDSLASHER | DK_HACKANDSLASHER_ATK => {
            if let Some(sd) = bl::sd(c, src) {
                let w = sd.ch.weapon as i32;
                if w == W_1HSPEAR || w == W_2HSPEAR {
                    return BF_LONG;
                }
            }
        }
        _ => {}
    }
    // Skill Range Criteria
    if conf::skillrange_by_distance() != 0 && src.kind & conf::skillrange_by_distance() != 0 {
        // based on distance between src/target [Skotlex]
        if crate::path::check_distance((src.x - target.x) as i32, (src.y - target.y) as i32, 3) {
            return BF_SHORT;
        }
        return BF_LONG;
    }
    // based on used skill's range
    if crate::skill::misc::skill_get_range2(c, src, skill_id, skill_lv, true) < 4 {
        BF_SHORT
    } else {
        BF_LONG
    }
}

/// battle.cpp battle_do_reflect: status reflect (Reflect Shield, Max Pain, Reflect Damage, Deathbound) and item
/// reflect of skills. Normal attacks' item reflect is in battle_calc_attack_gvg_bg.
pub(super) fn battle_do_reflect(c: &Ctx, attack_type: i32, wd: &mut Damage, src: &Unit, target: &Unit, skill_id: u16, _skill_lv: u16) {
    // Don't reflect your own damage (Grand Cross); a skill unit reflects only the Warmth skills'
    let warm = matches!(skill_id, SG_SUN_WARM | SG_MOON_WARM | SG_STAR_WARM);
    if wd.damage + wd.damage2 == 0 || src.id == target.id || (src.kind == BL_SKILL && !warm) {
        return;
    }
    let mut damage = wd.damage + wd.damage2;
    let tsd = bl::sd(c, target);
    let Some(tsc) = bl::sc(c, target) else { return };
    let sstatus = bl::status(c, src);
    let tick = c.tick();
    if let Some(mut sce) = tsc.get(SC_MAXPAIN).cloned() {
        sce.val2 = damage as i32;
        sc::put_sce(c, &sce);
        // SC_KYOMU invalidates reflecting ability. SC_DARKCROW also does, but only for short weapon attack.
        if !tsc.has(SC_KYOMU) && !(tsc.has(SC_DARKCROW) && wd.flag & BF_SHORT != 0) {
            let flag = if wd.flag & 1 != 0 { wd.flag - 1 } else { wd.flag };
            crate::skill::castend::skill_castend_damage_id(c, target, src, NPC_MAXPAIN_ATK, sce.val1 as u16, tick, flag);
        }
    }
    // Calculate skill reflect damage separately
    let mut rdamage = 0;
    let target = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
    let target = &target;
    if !target.ud.immune_attack || !bl::has_mode(c, target, MD_SKILLIMMUNE) {
        rdamage = fix::battle_calc_return_damage(c, target, src, &mut damage, wd.flag, skill_id, true);
    }
    if rdamage > 0 {
        let d_bl = battle_check_devotion(c, src);
        if bl::sc(c, src).map(|s| s.has(SC_VITALITYACTIVATION)).unwrap_or(false) {
            rdamage /= 2;
        }
        if attack_type == BF_WEAPON && sc::has_sce(c, target.id, SC_REFLECTDAMAGE) {
            // map_foreachinshootrange(battle_damage_area, target, skill_get_splash(LG_REFLECTDAMAGE, 1), BL_CHAR, tick,
            // target, wd->amotion, sstatus->dmotion, rdamage, wd->flag): the va_list reads the damage as an int32
            let (amotion, dmotion) = (wd.amotion, sstatus.dmotion as i32);
            let range = skill_get_splash(LG_REFLECTDAMAGE, 1) as i16;
            crate::skill::area::foreach_in_range(c, target, range, crate::map::BL_CHAR, crate::skill::area::Wall::Always, |c, bl| {
                super::weapon_attack::battle_damage_area(c, bl, tick, target, amotion, dmotion, rdamage as i32)
            });
        } else if attack_type == BF_WEAPON || attack_type == BF_MISC {
            let to = d_bl.unwrap_or_else(|| src.clone());
            super::clif::clif_damage(c, src, &to, tick, wd.amotion, sstatus.dmotion as i32, rdamage, 1, DMG_ENDURE, 0, false);
            if let Some(tsd) = &tsd {
                super::weapon_attack::battle_drain(c, tsd, src, rdamage, rdamage, sstatus.race as i32, sstatus.class_ as i32);
            }
            super::weapon_attack::battle_delay_damage(c, tick, wd.amotion, target, &to, 0, CR_REFLECTSHIELD, 0, rdamage, ATK_DEF, 1, true, false, false);
            super::addeff::skill_additional_effect(c, target, &to, CR_REFLECTSHIELD, 1, BF_WEAPON | BF_SHORT | BF_NORMAL, ATK_DEF, tick);
        }
    }
}

// ---------------------------------------------------------------------------------------------- the calculation

/// battle.cpp battle_calc_weapon_attack (RENEWAL).
pub fn battle_calc_weapon_attack(c: &Ctx, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16, wflag: i32) -> Damage {
    let mut wd = initialize_weapon_data(c, src, target, skill_id, skill_lv, wflag);
    // Update Damage data based on skill (skill_db.find(skill_id)->impl->modifyDamageData)
    if let Some(imp) = crate::skills::skill_impl(skill_id) {
        imp.modify_damage_data(c, &mut wd, src, target, skill_lv);
    }
    let right_element = battle_get_weapon_element(c, &wd, src, target, skill_id, skill_lv, EQI_HAND_R, false);
    let left_element = battle_get_weapon_element(c, &wd, src, target, skill_id, skill_lv, EQI_HAND_L, false);
    // Skip checking as there are no status changes active (`sc = nullptr` when empty now; later reads are live).
    let sc_on = bl::sc_nonempty(c, src).is_some();
    let sc_live = |c: &Ctx| if sc_on { bl::sc(c, src) } else { None };
    let sd = bl::sd(c, src);
    let tsd = bl::sd(c, target);
    let tstatus = bl::status(c, target);
    // Check for Lucky Dodge
    if (skill_id == 0 || skill_id == PA_SACRIFICE) && tstatus.flee2 != 0 && ((c.rnd() % 1000) as i32) < tstatus.flee2 as i32 {
        wd.type_ = DMG_LUCY_DODGE;
        wd.dmg_lv = ATK_LUCKY;
        if wd.div_ < 0 {
            wd.div_ = -wd.div_;
        }
        return wd;
    }
    // on official check for multi hit first so we can override crit on double attack [helvetica]
    battle_calc_multi_attack(c, &mut wd, src, target, skill_id, skill_lv);
    // crit check is next since crits always hit on official [helvetica]
    if is_attack_critical(c, &wd, src, target, skill_id, skill_lv, true) {
        // PACKETVER >= 20161207
        wd.type_ = if wd.type_ & DMG_MULTI_HIT != 0 { DMG_MULTI_HIT_CRITICAL } else { DMG_CRITICAL };
    }
    let nk = battle_skill_get_damage_properties(skill_id, wd.miscflag);
    let mut infdef = false;
    // check if we're landing a hit
    if !is_attack_hitting(c, &wd, src, target, skill_id, skill_lv, true) {
        wd.dmg_lv = ATK_FLEE;
    } else if {
        infdef = is_infinite_defense(c, target, wd.flag);
        !infdef
    } {
        // no need for math against plants
        battle_calc_skill_base_damage(c, &mut wd, src, target, skill_id, skill_lv);
        if skill_id == HW_MAGICCRASHER {
            // Add weapon attack for MATK onto Magic Crasher
            let st = bl::status(c, src);
            let lh = is_attack_left_handed(c, src, skill_id);
            let v = if st.matk_max > st.matk_min { st.matk_min as i64 + (c.rnd() % (st.matk_max - st.matk_min) as u32) as i64 } else { st.matk_min as i64 };
            atk_add(lh, &mut wd.weapon_atk, &mut wd.weapon_atk2, v);
        }
        let lh = is_attack_left_handed(c, src, skill_id);
        // In Renewal we only cardfix to the weapon and equip ATK
        // Card Fix for attacker (sd), 2 is added to the "left" flag meaning "attacker cards only"
        if sd.is_some() {
            wd.weapon_atk += battle_calc_cardfix(c, BF_WEAPON, src, target, nk, right_element, left_element, wd.weapon_atk, 2, wd.flag) as i64;
            wd.equip_atk += battle_calc_cardfix(c, BF_WEAPON, src, target, nk, right_element, left_element, wd.equip_atk, 2, wd.flag) as i64;
            if lh {
                wd.weapon_atk2 += battle_calc_cardfix(c, BF_WEAPON, src, target, nk, right_element, left_element, wd.weapon_atk2, 3, wd.flag) as i64;
                wd.equip_atk2 += battle_calc_cardfix(c, BF_WEAPON, src, target, nk, right_element, left_element, wd.equip_atk2, 3, wd.flag) as i64;
            }
        }
        // Card Fix for target (tsd), 2 is not added to the "left" flag meaning "target cards only"
        if tsd.is_some() && skill_id != NJ_ISSEN && skill_id != GN_FIRE_EXPANSION_ACID {
            let ignoreele_nk = nk | (1u64 << NK_IGNOREELEMENT);
            wd.status_atk += battle_calc_cardfix(c, BF_WEAPON, src, target, ignoreele_nk, right_element, left_element, wd.status_atk, 0, wd.flag) as i64;
            wd.weapon_atk += battle_calc_cardfix(c, BF_WEAPON, src, target, nk, right_element, left_element, wd.weapon_atk, 0, wd.flag) as i64;
            wd.equip_atk += battle_calc_cardfix(c, BF_WEAPON, src, target, nk, right_element, left_element, wd.equip_atk, 0, wd.flag) as i64;
            wd.mastery_atk += battle_calc_cardfix(c, BF_WEAPON, src, target, ignoreele_nk, right_element, left_element, wd.mastery_atk, 0, wd.flag) as i64;
            if lh {
                wd.status_atk2 += battle_calc_cardfix(c, BF_WEAPON, src, target, ignoreele_nk, right_element, left_element, wd.status_atk2, 1, wd.flag) as i64;
                wd.weapon_atk2 += battle_calc_cardfix(c, BF_WEAPON, src, target, nk, right_element, left_element, wd.weapon_atk2, 1, wd.flag) as i64;
                wd.equip_atk2 += battle_calc_cardfix(c, BF_WEAPON, src, target, nk, right_element, left_element, wd.equip_atk2, 1, wd.flag) as i64;
                wd.mastery_atk2 += battle_calc_cardfix(c, BF_WEAPON, src, target, ignoreele_nk, right_element, left_element, wd.mastery_atk2, 1, wd.flag) as i64;
            }
        }
        // final attack bonuses that aren't affected by cards
        battle_attack_sc_bonus(c, &mut wd, src, target, skill_id, skill_lv);
        if let Some(sd) = bl::sd(c, src) {
            // monsters, homuns and pets have their damage computed directly
            let sstatus = bl::status(c, src);
            wd.damage = wd.status_atk + wd.weapon_atk + wd.equip_atk + wd.percent_atk;
            if lh {
                wd.damage2 = wd.status_atk2 + wd.weapon_atk2 + wd.equip_atk2 + wd.percent_atk2;
            }
            // Apply P.ATK mod (Dragon Breaths only with Dragonic Aura)
            if (skill_id != RK_DRAGONBREATH && skill_id != RK_DRAGONBREATH_WATER) || bl::checkskill(c, Some(&sd), DK_DRAGONIC_AURA) > 0 {
                wd.damage = ((wd.damage * (100 + sstatus.patk as i64) / 100) as f32).floor() as i64;
                if lh {
                    wd.damage2 = ((wd.damage2 * (100 + sstatus.patk as i64) / 100) as f32).floor() as i64;
                }
            }
            // Apply MasteryATK
            wd.damage += wd.mastery_atk;
            if lh {
                wd.damage2 += wd.mastery_atk2;
            }
            // CritAtkRate modifier
            let crit_atk_rate = sd.st.bonus.crit_atk_rate as i64;
            if wd.type_ == DMG_CRITICAL || wd.type_ == DMG_MULTI_HIT_CRITICAL {
                let div = if skill_id > 0 { 200 } else { 100 };
                wd.damage += ((wd.damage * crit_atk_rate / div) as f32).floor() as i64;
                if lh {
                    wd.damage2 += ((wd.damage2 * crit_atk_rate / div) as f32).floor() as i64;
                }
            } else if !skill_get_inf2(skill_id, &[INF2_IGNORENONCRITATKBONUS]) {
                atk_addrate(lh, &mut wd.damage, &mut wd.damage2, sd.st.bonus.non_crit_atk_rate as i64);
            }
            if wd.flag & BF_SHORT != 0 {
                atk_addrate(lh, &mut wd.damage, &mut wd.damage2, sd.st.bonus.short_attack_atk_rate as i64);
            }
            if wd.flag & BF_LONG != 0 && skill_id != RA_WUGBITE && skill_id != RA_WUGSTRIKE {
                // Long damage rate addition doesn't use weapon + equip attack
                atk_addrate(lh, &mut wd.damage, &mut wd.damage2, sd.st.bonus.long_attack_atk_rate as i64);
            }
        }
        // Skill ratio
        let ratio = battle_calc_attack_skill_ratio(c, &wd, src, target, skill_id, skill_lv) as i64;
        atk_rate(lh, &mut wd.damage, &mut wd.damage2, ratio);
        // Additive damage bonus
        let add = battle_calc_skill_constant_addition(c, src, skill_id, skill_lv);
        atk_add(lh, &mut wd.damage, &mut wd.damage2, add);
        // Advance Katar Mastery
        if let Some(sd) = bl::sd(c, src) {
            let katar_skill = bl::checkskill(c, Some(&sd), ASC_KATAR);
            if sd.ch.weapon as i32 == W_KATAR && katar_skill > 0 {
                // Adv. Katar Mastery applied after calculate with skillratio.
                atk_addrate(lh, &mut wd.damage, &mut wd.damage2, 10 + 2 * katar_skill as i64);
            }
        }
        // Res reduces physical damage by a percentage and is calculated before DEF and other reductions.
        let tstatus = bl::status(c, target);
        if wd.damage + wd.damage2 != 0 && tstatus.res > 0 && !nk_has(nk, NK_SIMPLEDEFENSE) {
            let mut res: i16 = tstatus.res;
            let mut ignore_res: i16 = 0;
            if let Some(sd) = bl::sd(c, src) {
                let ib = &sd.st.indexed_bonus;
                ignore_res = (ignore_res as i32 + ib.ignore_res_by_race.get(tstatus.race as usize).copied().unwrap_or(0) + ib.ignore_res_by_race[RC_ALL as usize]) as i16;
            }
            if let Some(s) = &sc_live(c) {
                // Attacker status's that pierce Res.
                if s.has(SC_A_TELUM) {
                    ignore_res = (ignore_res as i32 + s.v2(SC_A_TELUM)) as i16;
                }
                if s.has(SC_POTENT_VENOM) {
                    ignore_res = (ignore_res as i32 + s.v2(SC_POTENT_VENOM)) as i16;
                }
            }
            // % Res ignored is capped to 50% on official server
            ignore_res = (ignore_res as i32).min(conf::max_res_mres_ignored()) as i16;
            res = (res as f64 - ignore_res as f64 * res as f64 / 100.0) as i16;
            // Apply damage reduction (float cast as rAthena)
            let frac = (res as f64 / (res as f64 + 400.0)) as f32 as f64;
            wd.damage -= (frac * 80.0 / 100.0 * wd.damage as f64) as i64;
            wd.damage2 -= (frac * 80.0 / 100.0 * wd.damage2 as f64) as i64;
        }
        if wd.damage + wd.damage2 != 0 {
            // Check if attack ignores DEF
            if !attack_ignores_def(c, &wd, src, target, skill_id, EQI_HAND_L) || !attack_ignores_def(c, &wd, src, target, skill_id, EQI_HAND_R) {
                battle_calc_defense_reduction(c, &mut wd, src, target, skill_id, skill_lv);
            }
            battle_calc_attack_post_defense(c, &mut wd, src, target, skill_id, skill_lv);
        }
        // add any miscellaneous player ATK bonuses
        if let Some(sd) = bl::sd(c, src) {
            if skill_id != 0 {
                let i = bl::skillatk_bonus(&sd, skill_id);
                if i != 0 {
                    atk_addrate(lh, &mut wd.damage, &mut wd.damage2, i as i64);
                    re_allatk_addrate(lh, &mut wd, i as i64);
                }
            }
        }
        if let Some(tsd) = bl::sd(c, target) {
            let i = bl::sub_skillatk_bonus(&tsd, skill_id);
            if i != 0 {
                atk_addrate(lh, &mut wd.damage, &mut wd.damage2, -(i as i64));
                re_allatk_addrate(lh, &mut wd, -(i as i64));
            }
        }
    }
    battle_calc_element_damage(c, &mut wd, src, target, skill_id, skill_lv);
    if is_attack_critical(c, &wd, src, target, skill_id, skill_lv, false) {
        let lh = is_attack_left_handed(c, src, skill_id);
        if bl::sd(c, src).is_some() {
            // Check for player so we don't crash out, monsters don't have bonus crit rates [helvetica]
            let crate_ = bl::status(c, src).crate_ as f32;
            let m = 1.4f32 + 0.01f32 * crate_;
            wd.damage = (wd.damage as f32 * m).floor() as i64;
            if lh {
                wd.damage2 = (wd.damage2 as f32 * m).floor() as i64;
            }
        } else {
            wd.damage = (wd.damage as f32 * 1.4f32).floor() as i64;
        }
        if let Some(tsd) = bl::sd(c, target) {
            if tsd.st.bonus.crit_def_rate != 0 {
                atk_addrate(lh, &mut wd.damage, &mut wd.damage2, -(tsd.st.bonus.crit_def_rate as i64));
            }
        }
    }
    {
        let lh = is_attack_left_handed(c, src, skill_id);
        match skill_id {
            TK_DOWNKICK | TK_STORMKICK | TK_TURNKICK | TK_COUNTER => {
                if let Some(sd) = &sd {
                    if sd.it.weapontype1 as i32 == W_FIST && sd.it.weapontype2 as i32 == W_FIST {
                        let v = 10 * bl::checkskill(c, Some(sd), TK_RUN) as i64;
                        atk_add(lh, &mut wd.damage, &mut wd.damage2, v);
                    }
                }
            }
            LG_SHIELDPRESS => {
                if let Some(sd) = &sd {
                    let mut damagevalue: i32 = 0;
                    if let Some((row, data)) = bl::equip(c, sd, EQI_HAND_L) {
                        if data.r#type == crate::db::items::IT_ARMOR {
                            damagevalue = bl::status(c, src).vit as i32 * row.item.refine as i32;
                        }
                    }
                    atk_add(lh, &mut wd.damage, &mut wd.damage2, damagevalue as i64);
                }
            }
            SR_TIGERCANNON => {
                // (Tiger Cannon skill level x 240) + (Target Base Level x 40)
                let v = if wd.miscflag & 8 != 0 {
                    skill_lv as i64 * 500 + bl::lv_of(c, target) as i64 * 40
                } else {
                    skill_lv as i64 * 240 + bl::lv_of(c, target) as i64 * 40
                };
                atk_add(lh, &mut wd.damage, &mut wd.damage2, v);
            }
            SR_GATEOFHELL => {
                let sstatus = bl::status(c, src);
                // double bonus = 1 + skill_lv * 2 / 10: the division is an integer one
                let bonus: f64 = (1 + skill_lv as i32 * 2 / 10) as f64;
                atk_add(lh, &mut wd.damage, &mut wd.damage2, sstatus.max_hp.wrapping_sub(sstatus.hp) as i64);
                let combo_fallen = sc_live(c).map(|s| s.get(SC_COMBO).map(|e| e.val1 == SR_FALLENEMPIRE as i32).unwrap_or(false)).unwrap_or(false);
                if combo_fallen {
                    let v = (sstatus.max_sp as f64 * bonus) as i64 + 40 * bl::lv_of(c, src) as i64;
                    atk_add(lh, &mut wd.damage, &mut wd.damage2, v);
                } else {
                    let v = (sstatus.sp as f64 * bonus) as i64 + 10 * bl::lv_of(c, src) as i64;
                    atk_add(lh, &mut wd.damage, &mut wd.damage2, v);
                }
            }
            // !TODO: Confirm base level bonus
            MH_TINDER_BREAKER => atk_add(lh, &mut wd.damage, &mut wd.damage2, 2500 * skill_lv as i64 + bl::lv_of(c, src) as i64),
            MH_CBC => atk_add(lh, &mut wd.damage, &mut wd.damage2, 4000 * skill_lv as i64 + bl::lv_of(c, src) as i64),
            MH_EQC => atk_add(lh, &mut wd.damage, &mut wd.damage2, 6000 * skill_lv as i64 + bl::lv_of(c, src) as i64),
            NPC_MAXPAIN_ATK => {
                if let Some(s) = sc_live(c) {
                    if let Some(sce) = s.get(SC_MAXPAIN) {
                        if sce.val2 != 0 {
                            wd.damage = (sce.val2.wrapping_mul(skill_lv as i32) / 10) as i64;
                        } else if sce.val3 != 0 {
                            wd.damage = (sce.val3.wrapping_mul(skill_lv as i32) / 10) as i64;
                        }
                    }
                } else {
                    wd.damage = 0;
                }
            }
            _ => {}
        }
    }
    // In renewal only do it for non player attacks
    if tsd.is_some() && sd.is_none() {
        // Card Fix for target (tsd), 2 is not added to the "left" flag meaning "target cards only"
        wd.damage += battle_calc_cardfix(c, BF_WEAPON, src, target, nk, right_element, left_element, wd.damage, 0, wd.flag) as i64;
        if is_attack_left_handed(c, src, skill_id) {
            wd.damage2 += battle_calc_cardfix(c, BF_WEAPON, src, target, nk, right_element, left_element, wd.damage2, 1, wd.flag) as i64;
        }
    }
    // only do 1 dmg to plant, no need to calculate rest
    if infdef {
        battle_calc_attack_plant(c, &mut wd, src, target, skill_id, skill_lv);
        return wd;
    }
    // Apply DAMAGE_DIV_FIX and check for min damage
    battle_apply_div_fix(&mut wd, skill_id);
    battle_calc_attack_left_right_hands(c, &mut wd, src, skill_id);
    match skill_id {
        NJ_ISSEN | GN_FIRE_EXPANSION_ACID => return wd, // These skills will do a GVG fix later
        _ => battle_calc_attack_gvg_bg(c, &mut wd, src, target, skill_id, skill_lv),
    }
    battle_calc_weapon_final_atk_modifiers(c, &mut wd, src, target, skill_id, skill_lv);
    fix::battle_absorb_damage(c, target, &mut wd);
    battle_do_reflect(c, BF_WEAPON, &mut wd, src, target, skill_id, skill_lv);
    wd
}

/// battle.cpp is_infinite_defense: "plant"-type targets (1 damage from everything of that kind).
pub fn is_infinite_defense(c: &Ctx, target: &Unit, flag: i32) -> bool {
    use super::mobif::has_mode;
    let tstatus = bl::status(c, target);
    if target.kind == BL_SKILL && crate::skillunit::is_infinite_defense_unit(c, target) {
        return true; // Reverberation / Poem of the Netherworld units (skillunit)
    }
    if has_mode(tstatus.mode, MD_IGNOREMELEE) && flag & (BF_WEAPON | BF_SHORT) == (BF_WEAPON | BF_SHORT) {
        return true;
    }
    if has_mode(tstatus.mode, MD_IGNOREMAGIC) && flag & BF_MAGIC != 0 {
        return true;
    }
    if has_mode(tstatus.mode, MD_IGNORERANGED) && flag & (BF_WEAPON | BF_LONG) == (BF_WEAPON | BF_LONG) {
        return true;
    }
    if has_mode(tstatus.mode, MD_IGNOREMISC) && flag & BF_MISC != 0 {
        return true;
    }
    bl::sc(c, target).map(|s| s.has(SC_INVINCIBLE)).unwrap_or(false)
}

/// battle.cpp battle_calc_attack: the main entry (battle_weapon_attack, skill_attack): weapon, magic or misc.
pub fn battle_calc_attack(c: &Ctx, attack_type: i32, bl: &Unit, target: &Unit, skill_id: u16, skill_lv: u16, flag: i32) -> Damage {
    let mut d = match attack_type {
        BF_WEAPON => battle_calc_weapon_attack(c, bl, target, skill_id, skill_lv, flag),
        BF_MAGIC => super::magic::battle_calc_magic_attack(c, bl, target, skill_id, skill_lv, flag),
        BF_MISC => super::misc::battle_calc_misc_attack(c, bl, target, skill_id, skill_lv, flag),
        _ => {
            log::error!("battle_calc_attack: unknown attack type! {attack_type} (skill_id={skill_id}, skill_lv={skill_lv})");
            Damage::default()
        }
    };
    if d.damage + d.damage2 < 1 {
        // Miss/Absorbed: weapon attacks go through to cause additional effects.
        if d.dmg_lv == ATK_DEF {
            d.dmg_lv = ATK_MISS;
        }
        d.dmotion = 0;
        if bl.kind == BL_PC {
            d.div_ = 1;
        }
        // Weapon Blocking has the ability to trigger on ATK_MISS as well.
        let tsce = super::mobif::has_sc(target.kind).then(|| sc::get_sce(c, target.id, SC_WEAPONBLOCKING)).flatten();
        if let Some(tsce) = tsce {
            if attack_type == BF_WEAPON && ((c.rnd() % 100) as i32) < tsce.val2 {
                let target_now = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
                let bl_now = crate::unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
                crate::skill::clif::clif_skill_nodamage(c, Some(&target_now), &bl_now, GC_WEAPONBLOCKING, tsce.val1, true);
                sc::sc_start(c, Some(&bl_now), &target_now, SC_WEAPONBLOCK_ON, 100, bl.id as i32, skill_get_time2(GC_WEAPONBLOCKING, tsce.val1 as u16) as i64);
            }
        }
    } else {
        // Some skills like Weaponry Research will cause damage even if attack is dodged
        d.dmg_lv = ATK_DEF;
    }
    if let Some(sd) = bl::sd(c, bl) {
        if d.damage + d.damage2 > 1 {
            super::weapon_attack::battle_vanish_damage(c, &sd, target, d.flag);
        }
    }
    d
}
