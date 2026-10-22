//! skill.cpp skill_attack (a skill's hit: damage calculation, magic reflection, the display, copy, knockback, the
//! delayed damage, devotion, post-damage effects), skill_attack_blow, skill_blown, skill_magic_reflect, skill_do_copy
//! and skill_check_shadowform.
//!
//! `src`, `dsrc` and `bl` are the units as the caller holds them (block_list pointers in rAthena); everything read
//! after an effect that may have changed them is read again.

use crate::battle::calc::{battle_calc_attack, Damage, ELE_ENDOWED, ELE_RANDOM, ELE_WEAPON};
use crate::battle::fix::battle_attr_fix;
use crate::battle::*;
use crate::db::skills::*;
use crate::map::{BL_MER, BL_PC, BL_PET, BL_SKILL};
use crate::status::consts::*;
use crate::status::defs::*;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

use super::area;
use super::clif::clif_skill_damage;
use super::{SD_ANIMATION, SD_LEVEL};

/// script.hpp EF_ATTACKENERGY2 (Kaite's reflection effect).
pub const EF_ATTACKENERGY2: i32 = 438;
/// itemdb.hpp ITEMID_FRAGMENT_OF_CRYSTAL.
pub const ITEMID_FRAGMENT_OF_CRYSTAL: u32 = 7321;

/// skill.hpp enum e_skill_blown.
pub const BLOWN_NONE: i32 = 0x00;
pub const BLOWN_DONT_SEND_PACKET: i32 = 0x01;
pub const BLOWN_IGNORE_NO_KNOCKBACK: i32 = 0x02;
pub const BLOWN_NO_KNOCKBACK_MAP: i32 = 0x04;
pub const BLOWN_MD_KNOCKBACK_IMMUNE: i32 = 0x08;
pub const BLOWN_TARGET_NO_KNOCKBACK: i32 = 0x10;
pub const BLOWN_TARGET_BASILICA: i32 = 0x20;

thread_local! {
    /// skill.cpp `char dir_ka`: the direction to the target for SR_KNUCKLEARROW (-1 = none).
    static DIR_KA: std::cell::Cell<i8> = const { std::cell::Cell::new(-1) };
}

/// skill.cpp dir_ka.
pub fn dir_ka() -> i8 {
    DIR_KA.with(|d| d.get())
}

/// skill.cpp `dir_ka = v`.
pub fn set_dir_ka(v: i8) {
    DIR_KA.with(|d| d.set(v));
}

fn has_sc(c: &Ctx, u: &Unit, t: i32) -> bool {
    crate::battle::mobif::has_sc(u.kind) && sc::has_sce(c, u.id, t)
}

/// skill.cpp skill_blown: knocks `target` back `count` cells away from `src` (or against `dir`, the direction
/// OPPOSITE to the knockback; -1: from src). Returns the cells moved (see e_skill_blown for what counts).
pub fn skill_blown(c: &Ctx, src: &Unit, target: &Unit, count: i32, dir: i8, flag: i32) -> i32 {
    use crate::battle::knockback::*;
    if count == 0 {
        return count; // Actual knockback distance is 0.
    }
    // Create flag needed in unit_blown_immune
    let mut checkflag: u8 = 0;
    if src.id != target.id {
        checkflag |= 0x1; // Offensive
    }
    if flag & BLOWN_IGNORE_NO_KNOCKBACK == 0 {
        checkflag |= 0x2; // Knockback type
    }
    if crate::battle::bl::class_type(c, src) == CLASS_BOSS {
        checkflag |= 0x4; // Boss attack
    }
    // Get reason and check for flags
    match unit_blown_immune(c, target, checkflag) {
        UB_NO_KNOCKBACK_MAP => return if flag & BLOWN_NO_KNOCKBACK_MAP != 0 { count } else { 0 }, // No knocking back in WoE / BG
        UB_MD_KNOCKBACK_IMMUNE => return if flag & BLOWN_MD_KNOCKBACK_IMMUNE != 0 { count } else { 0 }, // Immune can't be knocked back
        UB_TARGET_BASILICA => return if flag & BLOWN_TARGET_BASILICA != 0 { count } else { 0 }, // Basilica caster can't be knocked-back by normal monsters.
        UB_TARGET_NO_KNOCKBACK => return if flag & BLOWN_TARGET_NO_KNOCKBACK != 0 { count } else { 0 }, // Target has special_state.no_knockback (equip)
        UB_TARGET_TRAP => return count, // Trap cannot be knocked back
        _ => {}
    }
    let mut dir = dir;
    if dir == -1 {
        // <optimized>: do the computation here instead of outside
        dir = crate::map::calc_dir(target, src.x, src.y) as i8; // Direction from src to target, reversed
    }
    let (mut dx, mut dy) = (0i16, 0i16);
    if (0..8).contains(&dir) {
        // Take the reversed 'direction' and reverse it
        dx = -crate::path::DIRX[dir as usize];
        dy = -crate::path::DIRY[dir as usize];
    }
    if crate::battle::mobif::has_sc(target.kind) {
        if sc::has_sce(c, target.id, SC_SU_STOOP) {
            sc::status_change_end(c, target.id, SC_SU_STOOP, 0); // Any knockback will cancel it.
        }
        if sc::has_sce(c, target.id, SC_ROLLINGCUTTER) {
            sc::status_change_end(c, target.id, SC_ROLLINGCUTTER, 0);
        }
        if sc::has_sce(c, target.id, SC_CRESCIVEBOLT) {
            sc::status_change_end(c, target.id, SC_CRESCIVEBOLT, 0);
        }
        if sc::has_sce(c, target.id, SC_KI_SUL_RAMPAGE) {
            sc::status_change_end(c, target.id, SC_KI_SUL_RAMPAGE, 0);
        }
        if sc::has_sce(c, target.id, SC_SV_ROOTTWIST) {
            return 0; // Shouldn't move.
        }
    }
    unit_blown(c, target.id, dx, dy, count, flag) // Send over the proper flag
}

/// skill.cpp skill_magic_reflect: whether `bl` reflects a spell of `src` (type 0: indirect (aoe), 1: direct).
/// 1 regular reflection (Maya), 2 SL_KAITE, 3 NPC_MAGICMIRROR, 0 none.
fn skill_magic_reflect(c: &Ctx, src: &Unit, bl: &Unit, type_: i32) -> i32 {
    let scl = crate::battle::bl::sc(c, bl);
    let sd = crate::battle::bl::sd(c, bl);
    // Deadly Projection null's all magic reflection.
    if scl.as_ref().map(|s| s.has(SC_DEADLY_DEFEASANCE)).unwrap_or(false) {
        return 0;
    }
    if !scl.as_ref().map(|s| s.has(SC_KYOMU)).unwrap_or(false) {
        // Kyomu doesn't reflect
        // Item-based reflection - Bypasses Boss check
        if let Some(sd) = &sd {
            if sd.st.bonus.magic_damage_return != 0 && type_ != 0 && ((c.rnd() % 100) as i32) < sd.st.bonus.magic_damage_return {
                return 1;
            }
        }
        // Magic Mirror reflection - Bypasses Boss check
        if let Some(e) = scl.as_ref().and_then(|s| s.get(SC_MAGICMIRROR)) {
            if ((c.rnd() % 100) as i32) < e.val2 {
                return 3;
            }
        }
    }
    if crate::battle::bl::class_type(c, src) == CLASS_BOSS {
        return 0;
    }
    // status-based reflection
    let Some(scl) = scl.filter(|s| !s.empty()) else { return 0 };
    // Kaite reflection - Does not bypass Boss check
    if scl.has(SC_KAITE) && (src.kind == BL_PC || crate::battle::bl::lv_of(c, src) <= 80) && type_ != 0 {
        // Kaite only works against non-players if they are low-level (RENEWAL: does not reflect AoE).
        // Kyomu doesn't disable Kaite, but the "skill fail chance" part of Kyomu applies to it.
        crate::clif::specialeffect(c, bl, EF_ATTACKENERGY2, crate::clif::Target::Area);
        if let Some(mut e) = sc::get_sce(c, bl.id, SC_KAITE) {
            e.val2 -= 1;
            let left = e.val2;
            sc::put_sce(c, &e);
            if left <= 0 {
                sc::status_change_end(c, bl.id, SC_KAITE, 0);
            }
        }
        return 2;
    }
    0
}

/// skill.cpp skill_check_shadowform: Shadow Form passes the damage to its caster. True while it does.
pub fn skill_check_shadowform(c: &Ctx, bl: &Unit, damage: i64, hit: i16) -> bool {
    if damage == 0 {
        return false;
    }
    let Some(e) = crate::battle::mobif::has_sc(bl.kind).then(|| sc::get_sce(c, bl.id, SC__SHADOWFORM)).flatten() else { return false };
    let src = unit::get(c, e.val2 as u32).filter(|s| s.on_map);
    let Some(src) = src.filter(|s| s.map == bl.map) else {
        sc::status_change_end(c, bl.id, SC__SHADOWFORM, 0);
        return false;
    };
    if sc::status_isdead(c, &src) || crate::battle::target::battle_check_target(c, bl, &src, BCT_ENEMY) == 0 {
        if src.kind == BL_PC {
            super::state::with(c, src.id, |s| s.shadowform_id = 0);
        }
        sc::status_change_end(c, bl.id, SC__SHADOWFORM, 0);
        return false;
    }
    crate::battle::clif::clif_damage(c, &src, &src, c.tick(), 500, 500, damage, hit, if hit > 1 { DMG_MULTI_HIT } else { DMG_NORMAL }, 0, false);
    crate::status::hp::status_damage(c, Some(bl), src.id, damage, 0, 0, 500, 0, SC__SHADOWFORM as u16);
    if let Some(mut e) = sc::get_sce(c, bl.id, SC__SHADOWFORM) {
        e.val3 -= 1;
        let left = e.val3;
        sc::put_sce(c, &e);
        if left <= 0 {
            sc::status_change_end(c, bl.id, SC__SHADOWFORM, 0);
            if src.kind == BL_PC {
                super::state::with(c, src.id, |s| s.shadowform_id = 0);
            }
        }
    }
    true
}

/// skill.cpp skill_do_copy: Plagiarism / Reproduce copy the skill that hit the player.
fn skill_do_copy(c: &Ctx, src: &Unit, bl: &Unit, skill_id: u16, skill_lv: u16) {
    use crate::skilltree::{self, Skills, Slot};
    use crate::status::defs::SKILL_FLAG_PLAGIARIZED;
    if bl.kind != BL_PC {
        return;
    }
    let tsd = bl.id;
    let chk = |id| crate::skill::checkskill(c, tsd, id) as i32;
    if chk(RG_PLAGIARISM) == 0 && chk(SC_REPRODUCE) == 0 {
        return;
    }
    // If SC_PRESERVE is active and SC__REPRODUCE is not active, nothing to do
    if sc::has_sce(c, tsd, SC_PRESERVE) && !sc::has_sce(c, tsd, SC__REPRODUCE) {
        return;
    }
    let skill_id = skill_dummy2skill_id(skill_id);
    // Use skill index, avoiding out-of-bound array [Cydh]
    let idx = skill_get_index(skill_id);
    if idx == 0 {
        return;
    }
    let lv: u16;
    match super::misc::skill_is_copyable(c, tsd, skill_id) {
        1 => {
            // Copied by Plagiarism
            // Delete reproduced skill when the plagiarized skill id is the same
            if let Some(s) = Skills::load(c, tsd) {
                let r = s.meta.reproduceskill_idx;
                if r != 0 && s.at(r).flag == SKILL_FLAG_PLAGIARIZED && s.at(r).id == skill_id {
                    skilltree::pc_skill_plagiarism_reset(c, tsd, 2);
                }
            }
            skilltree::pc_skill_plagiarism_reset(c, tsd, 1);
            // Cap level to RG_PLAGIARISM level
            lv = skill_lv.min(chk(RG_PLAGIARISM) as u16);
            if let Some(mut s) = Skills::load(c, tsd) {
                s.meta.cloneskill_idx = idx;
                s.save(c);
            }
            skilltree::set_char_reg(c, tsd, skilltree::SKILL_VAR_PLAGIARISM, skill_id as i64);
            skilltree::set_char_reg(c, tsd, skilltree::SKILL_VAR_PLAGIARISM_LV, lv as i64);
        }
        2 => {
            // Copied by Reproduce
            // Delete plagiarized skill when the reproduced skill id is the same
            if let Some(s) = Skills::load(c, tsd) {
                let r = s.meta.cloneskill_idx;
                if r > 0 && s.at(r).flag == SKILL_FLAG_PLAGIARIZED && s.at(r).id == skill_id {
                    skilltree::pc_skill_plagiarism_reset(c, tsd, 1);
                }
            }
            skilltree::pc_skill_plagiarism_reset(c, tsd, 2);
            // Copied skill level depends on the Reproduce level used
            let mut l = sc::get_sce(c, tsd, SC__REPRODUCE).map(|e| e.val1 as u16).unwrap_or(1);
            // Cap level depending on the src type
            if src.kind == BL_PC {
                l = l.min(skill_get_max(skill_id) as u16); // If player, max skill level is skill_get_max(skill_id)
            } else {
                l = l.min(skill_lv); // Monster might used skill level > allowed player max skill lv.
            }
            lv = l;
            if let Some(mut s) = Skills::load(c, tsd) {
                s.meta.reproduceskill_idx = idx;
                s.save(c);
            }
            skilltree::set_char_reg(c, tsd, skilltree::SKILL_VAR_REPRODUCE, skill_id as i64);
            skilltree::set_char_reg(c, tsd, skilltree::SKILL_VAR_REPRODUCE_LV, lv as i64);
        }
        _ => return,
    }
    if let Some(mut s) = Skills::load(c, tsd) {
        s.put(idx, Slot { id: skill_id, lv: lv as u8, flag: SKILL_FLAG_PLAGIARIZED });
        s.save(c);
    }
    crate::clif_skill::clif_addskill(c, tsd, skill_id);
}

/// skill.cpp skill_attack_blow: the knockback of a skill hit (src: the master; dsrc: the originator, maybe a unit).
#[allow(clippy::too_many_arguments)]
pub fn skill_attack_blow(c: &Ctx, src: &Unit, dsrc: &Unit, target: &Unit, blewcount: u8, skill_id: u16, skill_lv: u16, damage: i64, tick: Tick, flag: i32) {
    let mut dir: i8 = -1; // Default direction
    // Only knockback if it's still alive, otherwise a "ghost" is left behind. [Skotlex]
    // Reflected spells do not bounce back (src == dsrc since it only happens for direct skills)
    if blewcount == 0 || target.id == dsrc.id || sc::status_isdead(c, target) {
        return;
    }
    // Skill specific direction
    match skill_id {
        MG_FIREWALL | EL_FIRE_MANTLE => dir = unit::getdir(target) as i8, // Backwards
        // This ensures the storm randomly pushes instead of exactly a cell backwards per official mechanics.
        NPC_STORMGUST2 | WZ_STORMGUST => {
            if !super::conf::stormgust_knockback() {
                dir = (c.rnd() % 8) as i8;
            }
        }
        MC_CARTREVOLUTION => {
            if super::conf::cart_revo_knockback() {
                dir = 6; // Official servers push target to the West
            }
        }
        AC_SHOWER | WL_CRIMSONROCK => {
            if !super::conf::arrow_shower_knockback() && skill_id == AC_SHOWER {
                dir = crate::map::calc_dir(target, src.x, src.y) as i8;
            } else {
                dir = crate::map::calc_dir(target, area::temp(4) as i16, area::temp(5) as i16) as i8;
            }
        }
        HT_PHANTASMIC => {
            // issue #1378
            if crate::status::status_get_status_data(c, target).hp as i64 - damage <= 0 {
                return;
            }
        }
        _ => {}
    }
    // Blown-specific handling
    match skill_id {
        SR_KNUCKLEARROW => {
            // Ignore knockback damage bonus if in WOE (player cannot be knocked in WOE)
            // Boss & Immune Knockback stay in place and don't get bonus damage
            // Give knockback damage bonus only hits the wall. (bugreport:9096)
            let f = BLOWN_IGNORE_NO_KNOCKBACK | BLOWN_NO_KNOCKBACK_MAP | BLOWN_MD_KNOCKBACK_IMMUNE | BLOWN_TARGET_NO_KNOCKBACK | BLOWN_TARGET_BASILICA;
            if skill_blown(c, dsrc, target, blewcount as i32, dir_ka(), f) < blewcount as i32 {
                let t = tick + 300 * if flag & 2 != 0 { 1 } else { 2 };
                super::timerskill::skill_addtimerskill(c, src, t, target.id, 0, 0, skill_id, skill_lv, BF_WEAPON, flag | 4);
            }
            set_dir_ka(-1);
        }
        RL_R_TRIP => {
            if skill_blown(c, dsrc, target, blewcount as i32, dir, BLOWN_NONE) < blewcount as i32 {
                let amotion = crate::status::status_get_status_data(c, src).amotion as Tick;
                super::timerskill::skill_addtimerskill(c, src, tick + amotion, target.id, 0, 0, RL_R_TRIP_PLUSATK, skill_lv, BF_WEAPON, flag | SD_ANIMATION);
            }
        }
        _ => {
            skill_blown(c, dsrc, target, blewcount as i32, dir, BLOWN_NONE);
            // (a Blast Mine unit hit for damage is knocked back 3 cells: BL_SKILL targets, skillunit agent)
            if blewcount == 0 && target.kind == BL_SKILL && damage > 0 {
                super::iface::blastmine_knockback(c, src, target);
            }
        }
    }
    if let Some(t) = unit::get(c, target.id) {
        crate::clif::fixpos(c, &t);
    }
}

/// skill.cpp skill_attack: does a skill attack with the given properties.
/// `src` is the master behind the attack (player/mob/pet), `dsrc` the actual originator of the damage (src, or a
/// BL_SKILL unit of src), `bl` the target. `flag`: &1 (recursive), &2 disable re-trigger by double casting, &4 skip
/// the knockback, &8 SC_COMBO bonus damage; &0xFFF goes to battle_calc_attack; &0xF000 e_skill_display;
/// &0x3F0000 e_battle_check_target; &0x1000000 return 0 if the damage was reflected.
#[allow(clippy::too_many_arguments)]
pub fn skill_attack(c: &Ctx, attack_type: i32, src: &Unit, dsrc: &Unit, bl: &Unit, skill_id: u16, skill_lv: u16, tick: Tick, flag: i32) -> i64 {
    use crate::status::check::status_check_skilluse;
    if skill_id > 0 && skill_lv == 0 {
        return 0;
    }
    let mut flag = flag;
    if crate::battle::bl::has_mode(c, bl, MD_SKILLIMMUNE as i32) || (crate::battle::bl::class_of(c, bl) == crate::battle::mobif::MOBID_EMPERIUM as i32 && !skill_get_inf2(skill_id, &[INF2_TARGETEMPERIUM])) {
        return 0;
    }
    if src.id != dsrc.id {
        // When caster is not the src of attack, this is a ground skill, and as such, do the relevant target checking.
        let caster = if super::conf::skill_caster_check() { Some(src) } else { None };
        if !status_check_skilluse(c, caster, Some(bl), skill_id, 2) {
            return 0;
        }
    } else if flag & SD_ANIMATION != 0 && skill_get_nk(skill_id, &[NK_SPLASH]) {
        // Note that splash attacks often only check versus the targetted mob, those around the splash area normally
        // don't get checked for being hidden/cloaked/etc. [Skotlex]
        if !status_check_skilluse(c, Some(src), Some(bl), skill_id, 2) {
            return 0;
        }
    }
    let mut src = src.clone();
    let mut dsrc = dsrc.clone();
    let mut bl = bl.clone();
    let tstatus = crate::battle::bl::status(c, &bl);
    // Trick Dead protects you from damage, but not from buffs and the like, hence it's placed here.
    if has_sc(c, &bl, SC_TRICKDEAD) {
        return 0;
    }
    let mut dmg: Damage = battle_calc_attack(c, attack_type, &src, &bl, skill_id, skill_lv, flag & 0xFFF);
    // If the damage source is a unit, the damage is not delayed
    if src.id != dsrc.id {
        dmg.amotion = 0;
    }
    // CHECKME: This check maybe breaks the battle_calc_attack, and maybe need better calculation.
    // Adjusted to the new system [Skotlex]
    if src.kind == BL_PET {
        // [Valaris]
        if let Some(a) = crate::pet::data::get(c, src.id).and_then(|pd| pd.a_skill) {
            if a.div_ != 0 && a.id == skill_id {
                //petskillattack2
                if crate::pet::pet_ignore_infinite_def() || !crate::battle::calc::is_infinite_defense(c, &bl, dmg.flag) {
                    let element = skill_get_ele(skill_id, skill_lv);
                    if element != ELE_NEUTRAL || crate::battle::conf::attack_attr_none() & BL_PET == 0 {
                        dmg.damage = battle_attr_fix(c, Some(&src), Some(&bl), a.damage as i64, element, tstatus.def_ele as i32, tstatus.ele_lv as i32, 0);
                    } else {
                        dmg.damage = a.damage as i64; // Fixed damage
                    }
                } else {
                    dmg.damage = a.div_ as i64;
                }
                dmg.damage2 = 0;
                dmg.div_ = a.div_ as i16;
            }
        }
    }
    let mut rmdamage = false; // magic reflected
    if dmg.flag & BF_MAGIC != 0 && (skill_id != NPC_EARTHQUAKE || (super::conf::eq_single_target_reflectable() && (flag & 0xFFF) == 1)) {
        // Earthquake on multiple targets is not counted as a target skill. [Inkfish]
        let type_ = if dmg.damage != 0 || dmg.damage2 != 0 { skill_magic_reflect(c, &src, &bl, (src.id == dsrc.id) as i32) } else { 0 };
        if type_ != 0 {
            // Magic reflection, switch caster/target
            let tbl = bl.clone();
            rmdamage = true;
            bl = src.clone();
            src = tbl.clone();
            dsrc = tbl;
            // bugreport:2564 flag&2 disables double casting trigger
            flag |= 2;
            // Reflected magic damage will not cause the caster to be knocked back [Playtester]
            flag |= 4;
            // Spirit of Wizard blocks Kaite's reflection
            if type_ == 2 {
                if let Some(mut sp) = sc::get_sce(c, bl.id, SC_SPIRIT).filter(|e| e.val2 == SL_WIZARD as i32) {
                    // Consume one Fragment per hit of the casted skill? [Skotlex]
                    let idx = if bl.kind == BL_PC { crate::inventory::pc_search_inventory(c, bl.id, ITEMID_FRAGMENT_OF_CRYSTAL) as i32 } else { 0 };
                    if idx >= 0 {
                        if bl.kind == BL_PC {
                            crate::inventory::pc_delitem(c, bl.id, idx, 1, 0, 1, crate::map_log::LOG_TYPE_CONSUME);
                        }
                        dmg.damage = 0;
                        dmg.damage2 = 0;
                        dmg.dmg_lv = ATK_MISS;
                        sp.val3 = skill_id as i32;
                        sp.val4 = dsrc.id as i32;
                        sc::put_sce(c, &sp);
                    }
                }
            }
            // Official Magic Reflection Behavior : damage reflected depends on gears caster wears, not target
            // (MAGIC_REFLECTION_TYPE 1, RENEWAL)
            if dmg.dmg_lv != ATK_MISS {
                // Wiz SL cancelled and consumed fragment
                let sstatus = crate::battle::bl::status(c, &bl); // (sstatus: the original caster's, now `bl`)
                let mut s_ele = skill_get_ele(skill_id, skill_lv);
                if s_ele == ELE_WEAPON {
                    s_ele = sstatus.rhw.ele as i32; // the skill takes the weapon's element
                } else if s_ele == ELE_ENDOWED {
                    // Use status element (of `src`, which is the reflector at this point)
                    let scl = crate::battle::bl::sc(c, &src).unwrap_or_default();
                    s_ele = crate::status::calc_sc::status_calc_attack_element(&scl, 0) as i32;
                } else if s_ele == ELE_RANDOM {
                    s_ele = (c.rnd() % ELE_ALL as u32) as i32; // Use random element
                }
                if type_ == 3 {
                    dmg.flag = BF_WEAPON | BF_NORMAL | BF_SHORT;
                }
                let bst = crate::battle::bl::status(c, &bl);
                dmg.damage = battle_attr_fix(c, Some(&bl), Some(&bl), dmg.damage, s_ele, bst.def_ele as i32, bst.ele_lv as i32, 0);
                if has_sc(c, &bl, SC_ENERGYCOAT) {
                    let status = crate::battle::bl::status(c, &bl);
                    let mut per = (100 * status.sp as i64 / (status.max_sp as i64).max(1) - 1) as i32; // 100% should be counted as the 80~99% interval
                    per /= 20; // Uses 20% SP intervals.
                    // SP Cost: 1% + 0.5% per every 20% SP
                    if crate::status::hp::status_charge(c, bl.id, 0, ((10 + 5 * per) as i64) * status.max_sp as i64 / 1000) == 0 {
                        sc::status_change_end(c, bl.id, SC_ENERGYCOAT, 0);
                    }
                    // Reduction: 6% + 6% every 20%
                    dmg.damage -= dmg.damage * (6 * (1 + per)) as i64 / 100;
                }
                let mut reduce: i64 = 0;
                if let Some(tsd) = crate::battle::bl::sd(c, &bl) {
                    if tsd.st.bonus.reduce_damage_return != 0 {
                        reduce += tsd.st.bonus.reduce_damage_return as i64;
                    }
                }
                if let Some(e) = crate::battle::mobif::has_sc(bl.kind).then(|| sc::get_sce(c, bl.id, SC_REFLECTDAMAGE)).flatten() {
                    reduce += e.val2 as i64;
                }
                if has_sc(c, &bl, SC_REF_T_POTION) {
                    reduce += 100;
                }
                if dmg.damage > 0 {
                    dmg.damage -= dmg.damage * reduce.min(100) / 100;
                    dmg.damage = dmg.damage.max(dmg.div_ as i64);
                }
            }
        }
        if src.id == dsrc.id {
            if let Some(e) = crate::battle::mobif::has_sc(bl.kind).then(|| sc::get_sce(c, bl.id, SC_MAGICROD)).flatten() {
                let mut sp = skill_get_sp(skill_id, skill_lv);
                dmg.damage = 0;
                dmg.damage2 = 0;
                dmg.dmg_lv = ATK_MISS; // This will prevent skill additional effect from taking effect. [Skotlex]
                sp = sp * e.val2 / 100;
                if skill_id == WZ_WATERBALL && skill_lv > 1 {
                    let l = (skill_lv | 1) as i32;
                    sp /= l * l; // Estimate SP cost of a single water-ball
                }
                crate::status::hp::status_heal(c, bl.id, 0, sp as i64, 0, 2);
            }
        }
        if dmg.damage != 0 || dmg.damage2 != 0 {
            let hw = crate::battle::mobif::has_sc(bl.kind)
                && (sc::get_sce(c, bl.id, SC_HALLUCINATIONWALK).map(|e| ((c.rnd() % 100) as i32) < e.val3).unwrap_or(false)
                    || sc::get_sce(c, bl.id, SC_NPC_HALLUCINATIONWALK).map(|e| ((c.rnd() % 100) as i32) < e.val3).unwrap_or(false));
            if hw {
                dmg.damage = 0;
                dmg.damage2 = 0;
                dmg.dmg_lv = ATK_MISS;
            }
        }
    }
    let mut damage: i64 = dmg.damage + dmg.damage2;
    if dmg.flag & BF_MAGIC != 0 {
        if let Some(mut e) = crate::battle::mobif::has_sc(bl.kind).then(|| sc::get_sce(c, bl.id, SC_MAXPAIN)).flatten() {
            e.val3 = damage as i32;
            e.val2 = 0;
            let lv = e.val1;
            sc::put_sce(c, &e);
            if !sc::has_sce(c, bl.id, SC_KYOMU) {
                // SC_KYOMU invalidates reflecting ability.
                super::castend::skill_castend_damage_id(c, &bl, &src, NPC_MAXPAIN_ATK, lv as u16, tick, flag);
            }
        }
    }
    if matches!(skill_id, AL_INCAGI | AL_BLESSING | CASH_BLESSING | CASH_INCAGI | MER_INCAGI | MER_BLESSING) && has_sc(c, &bl, SC_CHANGEUNDEAD) {
        damage = 1;
    }
    if damage != 0 && dmg.flag & BF_MAGIC != 0 && has_sc(c, &bl, SC_GENSOU) {
        if let Some(nbl) = crate::battle::target::battle_getenemyarea(c, &bl, bl.x, bl.y, 2, crate::map::BL_CHAR, bl.id) {
            // Only one target is chosen.
            damage /= 2; // Deflect half of the damage to a target nearby
            let amotion = crate::status::status_get_status_data(c, &src).amotion as i32;
            let fixed = crate::battle::weapon_attack::battle_fix_damage(c, Some(&bl), &nbl, damage, 0, 0);
            clif_skill_damage(c, &bl, &nbl, tick, amotion, 0, fixed as i64, dmg.div_, OB_OBOROGENSOU_TRANSITION_ATK, -1, DMG_SINGLE);
        }
    }
    // Skill hit type
    let mut dmg_type = if skill_id == 0 { DMG_SPLASH } else { skill_get_hit(skill_id) };
    match skill_id {
        SC_TRIANGLESHOT => {
            if (c.rnd() % 100) as i32 > 1 + skill_lv as i32 {
                dmg.blewcount = 0;
            }
        }
        _ => {
            if damage < dmg.div_ as i64 && skill_id != CH_PALMSTRIKE {
                dmg.blewcount = 0; // only pushback when it hit for other
            }
        }
    }
    let sc_src = |c: &Ctx, src: &Unit, t: i32| crate::battle::mobif::has_sc(src.kind) && sc::has_sce(c, src.id, t);
    match skill_id {
        CR_GRANDCROSS | NPC_GRANDDARKNESS => {
            if super::conf::gx_disptype() {
                dsrc = src.clone();
            }
            if src.id == bl.id {
                dmg_type = DMG_ENDURE;
            } else {
                flag |= SD_ANIMATION;
            }
        }
        NJ_TATAMIGAESHI => {
            // For correct knockback.
            dsrc = src.clone();
            flag |= SD_ANIMATION;
        }
        TK_COUNTER => {
            // bonus from SG_FRIEND [Komurka]
            if src.kind == BL_PC {
                let party_id = crate::pc::character_of(c, src.id).map(|ch| ch.party_id).unwrap_or(0);
                let level = crate::skill::checkskill(c, src.id, SG_FRIEND);
                if party_id > 0 && level != 0 {
                    super::misc::party_skill_check(c, src.id, party_id, TK_COUNTER, level as u16);
                }
            }
        }
        SL_STIN | SL_STUN => {
            if skill_lv >= 7 && crate::battle::mobif::has_sc(src.kind) && !sc::has_sce(c, src.id, SC_SMA) {
                sc::sc_start(c, Some(&src), &src, SC_SMA, 100, skill_lv as i32, skill_get_time(SL_SMA, skill_lv) as i64);
            }
        }
        DK_SERVANT_W_DEMOL => {
            // Only give servant's per target after damage calculation.
            super::state::pc_addservantball(c, src.id, 1);
        }
        KN_PIERCE | LK_SPIRALPIERCE | RK_HUNDREDSPEAR | DK_MADNESS_CRUSHER => {
            if sc_src(c, &src, SC_CHARGINGPIERCE) {
                let t2 = skill_get_time2(DK_CHARGINGPIERCE, 1) as i64;
                if let Some(e) = sc::get_sce(c, src.id, SC_CHARGINGPIERCE_COUNT) {
                    if e.val1 < 10 {
                        // If the charge count is below 10, add 1.
                        sc::sc_start(c, Some(&src), &src, SC_CHARGINGPIERCE_COUNT, 100, e.val1 + 1, t2);
                    } else {
                        // If charge count is 10, bonus damage is applied for 1 attack and then the count status ends.
                        crate::clif::specialeffect(c, &bl, 1767, crate::clif::Target::Area);
                        sc::status_change_end(c, src.id, SC_CHARGINGPIERCE_COUNT, 0);
                    }
                } else {
                    // No count status detected? Start charge count at 1.
                    sc::sc_start(c, Some(&src), &src, SC_CHARGINGPIERCE_COUNT, 100, 1, t2);
                }
            }
        }
        MG_FIREWALL => {
            if tstatus.def_ele as i32 == ELE_FIRE || sc::battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) {
                // Fire and undead units hit by firewall cannot be stopped for 2 seconds
                if let Some(mut t) = unit::get(c, bl.id) {
                    t.ud.endure_tick = c.tick() + 2000;
                    unit::save(c, &t);
                }
            }
        }
        MH_BLAZING_AND_FURIOUS => {
            // (skill.cpp:2973-2977) a homunculus loses all its spheres: hom_delspiritball(hd, MAX_SPIRITBALL, 1)
            // (TODO upstream: no update on the client ?)
            if src.kind == crate::map::BL_HOM {
                crate::homunculus::hom_delspiritball(c, src.id, crate::homunculus::MAX_SPIRITBALL, 1);
            }
        }
        _ => {}
    }
    // combo handling
    super::combo::skill_combo(c, &src, &dsrc, &bl, skill_id, skill_lv, tick);
    let src = unit::get(c, src.id).unwrap_or(src);
    let bl = unit::get(c, bl.id).unwrap_or(bl);
    let dsrc = if dsrc.id == src.id { src.clone() } else { unit::get(c, dsrc.id).unwrap_or(dsrc) };
    let amotion_of = |c: &Ctx, u: &Unit| crate::status::status_get_status_data(c, u).amotion as i32;
    let lv_disp = |flag: i32| if flag & SD_LEVEL != 0 { -1 } else { skill_lv as i32 };
    // Display damage.
    match skill_id {
        PA_GOSPEL => {
            // Should look like Holy Cross [Skotlex]
            clif_skill_damage(c, &src, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, CR_HOLYCROSS, -1, DMG_SPLASH);
        }
        // Skills that need be passed as a normal attack for the client to display correctly.
        NPC_SELFDESTRUCTION | HVAN_EXPLOSION | KN_AUTOCOUNTER | NPC_CRITICALSLASH | TF_DOUBLE | GS_CHAINACTION => {
            if skill_id == NPC_SELFDESTRUCTION {
                if src.kind == BL_PC {
                    dmg.blewcount = 10;
                }
                dmg.amotion = 0; // Disable delay or attack will do no damage since source is dead by the time it takes effect. [Skotlex]
            }
            crate::battle::clif::clif_damage(c, &src, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, dmg.type_, dmg.damage2, false);
        }
        AS_SPLASHER => {
            if flag & SD_ANIMATION != 0 {
                // the surrounding targets
                clif_skill_damage(c, &dsrc, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, skill_id, -1, DMG_SPLASH); // needs -1 as skill level
            } else {
                // the central target doesn't display an animation
                clif_skill_damage(c, &dsrc, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, skill_id, -2, DMG_SPLASH); // needs -2(!) as skill level
            }
        }
        SR_EARTHSHAKER => clif_skill_damage(c, &src, &bl, tick, dmg.amotion, dmg.dmotion, damage, 1, skill_id, -2, DMG_SINGLE),
        WL_SOULEXPANSION | WL_COMET | NPC_COMET | KO_MUCHANAGE => {
            clif_skill_damage(c, &src, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, skill_id, skill_lv as i32, DMG_MULTI_HIT);
        }
        WL_CHAINLIGHTNING_ATK => clif_skill_damage(c, &src, &bl, tick, dmg.amotion, dmg.dmotion, damage, 1, WL_CHAINLIGHTNING_ATK, -2, DMG_SINGLE),
        WL_TETRAVORTEX_FIRE => clif_skill_damage(c, &dsrc, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, WL_TETRAVORTEX_WIND, -1, DMG_SPLASH),
        LG_SHIELDPRESS => clif_skill_damage(c, &dsrc, &bl, tick, amotion_of(c, &src), dmg.dmotion, damage, dmg.div_, skill_id, -1, DMG_SINGLE),
        NPC_EARTHQUAKE => clif_skill_damage(c, &src, &bl, tick, amotion_of(c, &src), dmg.dmotion, damage, dmg.div_, skill_id, -1, DMG_SPLASH),
        NPC_DARKPIERCING | EL_FIRE_BOMB | EL_FIRE_BOMB_ATK | EL_FIRE_WAVE | EL_FIRE_WAVE_ATK | EL_FIRE_MANTLE | EL_CIRCLE_OF_FIRE | EL_FIRE_ARROW | EL_ICE_NEEDLE | EL_WATER_SCREW
        | EL_WATER_SCREW_ATK | EL_WIND_SLASH | EL_TIDAL_WEAPON | EL_ROCK_CRUSHER | EL_ROCK_CRUSHER_ATK | EL_HURRICANE | EL_HURRICANE_ATK | KO_BAKURETSU | GN_HELLS_PLANT_ATK
        | SU_SV_ROOTTWIST_ATK => {
            clif_skill_damage(c, &src, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, skill_id, -1, DMG_SPLASH);
        }
        GN_FIRE_EXPANSION_ACID => clif_skill_damage(c, &dsrc, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, CR_ACIDDEMONSTRATION, skill_lv as i32, DMG_MULTI_HIT),
        GN_SLINGITEM_RANGEMELEEATK => clif_skill_damage(c, &src, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, GN_SLINGITEM, -2, DMG_SINGLE),
        EL_STONE_RAIN => {
            let t = if flag & 1 != 0 { DMG_MULTI_HIT } else { DMG_SPLASH };
            clif_skill_damage(c, &dsrc, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, skill_id, -1, t);
        }
        WM_SEVERE_RAINSTORM_MELEE => clif_skill_damage(c, &src, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, WM_SEVERE_RAINSTORM, -2, DMG_SPLASH),
        HT_CLAYMORETRAP | HT_BLASTMINE | HT_FLASHER | HT_FREEZINGTRAP | RA_CLUSTERBOMB | RA_FIRINGTRAP | RA_ICEBOUNDTRAP => {
            clif_skill_damage(c, &src, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, skill_id, lv_disp(flag), DMG_SPLASH);
            if dsrc.id == src.id {
                // [[fallthrough]] to HT_LANDMINE (avoid damage display redundancy when dsrc != src)
                clif_skill_damage(c, &dsrc, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, skill_id, -1, dmg_type);
            }
        }
        HT_LANDMINE => clif_skill_damage(c, &dsrc, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, skill_id, -1, dmg_type),
        WZ_SIGHTBLASTER => {
            // Sightblaster should never call clif_skill_damage twice
            clif_skill_damage(c, &src, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, skill_id, lv_disp(flag), DMG_SPLASH);
        }
        RL_R_TRIP_PLUSATK | RL_S_STORM => clif_skill_damage(c, &dsrc, &bl, tick, amotion_of(c, &src), dmg.dmotion, damage, dmg.div_, skill_id, -1, DMG_SPLASH),
        SU_LUNATICCARROTBEAT | SU_LUNATICCARROTBEAT2 | SP_CURSEEXPLOSION | SP_SPA | SP_SHA | WM_REVERBERATION => {
            if skill_id != WM_REVERBERATION {
                if dmg.div_ < 2 {
                    dmg_type = DMG_SPLASH;
                }
                if flag & SD_ANIMATION == 0 {
                    super::clif::clif_skill_nodamage(c, Some(&dsrc), &bl, skill_id, skill_lv as i32, true);
                }
            }
            clif_skill_damage(c, &dsrc, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, skill_id, -2, dmg_type);
        }
        SJ_FALLINGSTAR_ATK | SJ_FALLINGSTAR_ATK2 => clif_skill_damage(c, &src, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, skill_id, -2, DMG_MULTI_HIT),
        SJ_NOVAEXPLOSING => clif_skill_damage(c, &dsrc, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, skill_id, -2, DMG_SINGLE),
        DK_HACKANDSLASHER_ATK => clif_skill_damage(c, &dsrc, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, skill_id, -1, dmg_type),
        AG_STORM_CANNON | AG_CRIMSON_ARROW => clif_skill_damage(c, &dsrc, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, skill_id, skill_lv as i32, DMG_SPLASH),
        TR_ROSEBLOSSOM_ATK | ABC_FROM_THE_ABYSS_ATK => clif_skill_damage(c, &dsrc, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, skill_id, -1, DMG_SPLASH),
        TR_SOUNDBLEND => {
            if flag & SD_ANIMATION != 0 {
                // For some reason the caster reacts on the splash flag. Best reduce amotion to minimize it for now. [Rytech]
                clif_skill_damage(c, &dsrc, &bl, tick, 10, dmg.dmotion, damage, dmg.div_, skill_id, -1, DMG_SPLASH);
            } else {
                clif_skill_damage(c, &dsrc, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, skill_id, skill_lv as i32, dmg_type);
            }
        }
        _ => {
            if matches!(skill_id, AB_DUPLELIGHT_MELEE | AB_DUPLELIGHT_MAGIC) {
                dmg.amotion = 300; // makes the damage value not overlap with previous damage (when displayed by the client)
            }
            if flag & SD_ANIMATION != 0 && dmg.div_ < 2 {
                dmg_type = DMG_SPLASH; // Disabling skill animation doesn't works on multi-hit.
            }
            if src.kind == BL_SKILL && super::iface::skill_unit_group_is_trap(c, &src) {
                // show damage on trap targets
                clif_skill_damage(c, &src, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, skill_id, lv_disp(flag), DMG_SPLASH);
            } else {
                clif_skill_damage(c, &dsrc, &bl, tick, dmg.amotion, dmg.dmotion, damage, dmg.div_, skill_id, lv_disp(flag), dmg_type);
            }
        }
    }
    // Only copy skill that copyable [Cydh]; cannot copy skills if the blow will kill you. [Skotlex]
    if bl.kind == BL_PC
        && skill_id != 0
        && skill_db().find(skill_id).map(|s| s.copyable.option != 0).unwrap_or(false)
        && dmg.flag & BF_SKILL != 0
        && dmg.damage + dmg.damage2 > 0
        && damage < crate::battle::bl::status(c, &bl).hp as i64
    {
        skill_do_copy(c, &src, &bl, skill_id, skill_lv);
    }
    if dmg.dmg_lv >= ATK_MISS {
        let wd = skill_get_walkdelay(skill_id, skill_lv);
        if wd > 0 {
            // Skills with can't walk delay also stop normal attacking for that duration when the attack connects.
            if let Some(mut s) = unit::get(c, src.id) {
                if s.ud.attackabletime - (tick + wd as i64) < 0 {
                    s.ud.attackabletime = tick + wd as i64;
                    unit::save(c, &s);
                }
            }
        }
    }
    let shadow_flag = skill_check_shadowform(c, &bl, damage, dmg.div_);
    let additional_effects = true;
    let tsc_has = |c: &Ctx, t: i32| crate::battle::mobif::has_sc(bl.kind) && sc::has_sce(c, bl.id, t);
    let tsc_nonempty = crate::battle::mobif::has_sc(bl.kind) && !sc::load(c, bl.id).empty();
    // Instant damage
    if dmg.amotion == 0 {
        // Deal damage before knockback to allow stuff like firewall+storm gust combo.
        if (!tsc_nonempty || (!tsc_has(c, SC_DEVOTION) && skill_id != CR_REFLECTSHIELD && !tsc_has(c, SC_WATER_SCREEN_OPTION)) || skill_id == NPC_EVILLAND) && !shadow_flag {
            crate::battle::weapon_attack::battle_damage(c, Some(&src), &bl, damage, dmg.div_, skill_lv, skill_id, dmg.dmg_lv, dmg.flag as u16, false, tick, false, false);
        }
        if let Some(t) = unit::get(c, bl.id) {
            if !sc::status_isdead(c, &t) && additional_effects {
                let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                super::effect::skill_additional_effect(c, &s, &t, skill_id, skill_lv, dmg.flag, dmg.dmg_lv, tick);
            }
        }
        if damage > 0 {
            // Counter status effects [Skotlex] (on the live `bl`: a monster the blow killed and freed keeps its state
            // under map_freeblock_lock)
            let s = unit::get_or_freed(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get_or_freed(c, bl.id).unwrap_or_else(|| bl.clone());
            super::effect::skill_counter_additional_effect(c, &s, &t, skill_id, skill_lv, dmg.flag, tick);
        }
    }
    // Blow!
    if flag & 4 == 0 {
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let d = if dsrc.id == src.id { s.clone() } else { unit::get(c, dsrc.id).unwrap_or_else(|| dsrc.clone()) };
        if let Some(t) = unit::get(c, bl.id) {
            skill_attack_blow(c, &s, &d, &t, dmg.blewcount as u8, skill_id, skill_lv, damage, tick, flag);
        }
    }
    // Delayed damage must be dealt after the knockback (it needs to know actual position of target)
    if dmg.amotion != 0 {
        let s = unit::get_or_freed(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get_or_freed(c, bl.id).unwrap_or_else(|| bl.clone());
        if shadow_flag {
            if !sc::status_isdead(c, &t) && additional_effects {
                super::effect::skill_additional_effect(c, &s, &t, skill_id, skill_lv, dmg.flag, dmg.dmg_lv, tick);
            }
            if dmg.flag > ATK_BLOCK as i32 {
                super::effect::skill_counter_additional_effect(c, &s, &t, skill_id, skill_lv, dmg.flag, tick);
            }
        } else {
            crate::battle::weapon_attack::battle_delay_damage(c, tick, dmg.amotion, &s, &t, dmg.flag, skill_id, skill_lv, damage, dmg.dmg_lv, dmg.div_, additional_effects, false, false);
        }
    }
    if tsc_nonempty && skill_id != NPC_EVILLAND && skill_id != SP_SOULEXPLOSION && skill_id != SJ_NOVAEXPLOSING {
        // `bl` where it is now (after the knockback)
        let bl = unit::get_or_freed(c, bl.id).unwrap_or_else(|| bl.clone());
        if let Some(sce) = sc::get_sce(c, bl.id, SC_DEVOTION) {
            let d_bl = unit::get(c, sce.val1 as u32);
            let linked = d_bl.as_ref().map(|d| {
                (d.kind == BL_MER && super::iface::mercenary_master_id(c, d) == Some(bl.id))
                    || (d.kind == BL_PC && super::state::get(c, d.id).devotion.get(sce.val2 as usize).copied() == Some(bl.id))
            });
            let in_range = d_bl.as_ref().map(|d| crate::path::check_distance((bl.x - d.x) as i32, (bl.y - d.y) as i32, sce.val3)).unwrap_or(false);
            if let (Some(d_bl), Some(true), true) = (d_bl.clone(), linked, in_range) {
                let mut devotion_damage = damage;
                // Needed to check the devotion master for Rebound Shield status.
                if let Some(rs) = crate::battle::mobif::has_sc(d_bl.kind).then(|| sc::get_sce(c, d_bl.id, SC_REBOUND_S)).flatten() {
                    devotion_damage -= devotion_damage * rs.val2 as i64 / 100;
                }
                if !rmdamage {
                    crate::battle::clif::clif_damage(c, &d_bl, &d_bl, c.tick(), 0, 0, devotion_damage, 0, DMG_NORMAL, 0, false);
                    crate::battle::weapon_attack::battle_fix_damage(c, Some(&src), &d_bl, devotion_damage, 0, 0);
                } else {
                    // devotion_rdamage 0: reflected magics are done directly on the target, not on the paladin
                    let is_devot_rdamage = crate::battle::conf::devotion_rdamage() != 0 && crate::battle::conf::devotion_rdamage() > (c.rnd() % 100) as i32;
                    let to = if is_devot_rdamage { &d_bl } else { &bl };
                    crate::battle::clif::clif_damage(c, &bl, to, c.tick(), 0, 0, devotion_damage, 0, DMG_NORMAL, 0, false);
                    crate::battle::weapon_attack::battle_fix_damage(c, Some(&bl), to, devotion_damage, 0, 0);
                }
            } else {
                sc::status_change_end(c, bl.id, SC_DEVOTION, 0);
                if dmg.amotion == 0 {
                    crate::battle::weapon_attack::battle_fix_damage(c, Some(&src), &bl, damage, dmg.dmotion as i16, 0);
                }
            }
        }
        if let Some(sce) = sc::get_sce(c, bl.id, SC_WATER_SCREEN_OPTION) {
            if let Some(e_bl) = unit::get(c, sce.val1 as u32) {
                if !rmdamage {
                    clif_skill_damage(c, &e_bl, &e_bl, c.tick(), 0, 0, damage, dmg.div_, skill_id, -1, skill_get_hit(skill_id));
                    crate::battle::weapon_attack::battle_fix_damage(c, Some(&src), &e_bl, damage, 0, 0);
                } else {
                    clif_skill_damage(c, &bl, &bl, c.tick(), 0, 0, damage, dmg.div_, skill_id, -1, skill_get_hit(skill_id));
                    crate::battle::weapon_attack::battle_fix_damage(c, Some(&bl), &bl, damage, 0, 0);
                }
            }
        }
    }
    if damage > 0 && !crate::battle::mobif::has_mode(tstatus.mode, MD_STATUSIMMUNE as i32) {
        if skill_id == RG_INTIMIDATE {
            let mut rate = 50 + skill_lv as i32 * 5;
            rate += crate::battle::bl::lv_of(c, &src) - crate::battle::bl::lv_of(c, &bl);
            if ((c.rnd() % 100) as i32) < rate {
                super::timerskill::skill_addtimerskill(c, &src, tick + 800, bl.id, 0, 0, skill_id, skill_lv, 0, flag);
            }
        } else if skill_id == NPC_FATALMENACE {
            let (mut x, mut y) = (area::temp(4) as i16, area::temp(5) as i16);
            if let Some((fx, fy)) = crate::map::search_freecell(c, bl.map, x, y, 2, 2, 1) {
                x = fx;
                y = fy;
            }
            super::timerskill::skill_addtimerskill(c, &bl, tick + 800, bl.id, x, y, skill_id, skill_lv, 0, flag);
        }
    }
    let sd = crate::battle::bl::sd(c, &src);
    if let Some(sd) = &sd {
        if src.id != bl.id && damage > 0 && (dmg.flag & BF_WEAPON != 0 || (dmg.flag & BF_MISC != 0 && matches!(skill_id, RA_CLUSTERBOMB | RA_FIRINGTRAP | RA_ICEBOUNDTRAP))) {
            // tstatus: the target's status taken before the reflection swap
            if super::conf::left_cardfix_to_right() {
                crate::battle::weapon_attack::battle_drain(c, sd, &bl, dmg.damage, dmg.damage, tstatus.race as i32, tstatus.class_ as i32);
            } else {
                crate::battle::weapon_attack::battle_drain(c, sd, &bl, dmg.damage, dmg.damage2, tstatus.race as i32, tstatus.class_ as i32);
            }
        }
        // libc rand() in rAthena (the context RNG here)
        if dmg.flag & BF_LONG != 0 {
            if let Some(e) = crate::battle::mobif::has_sc(bl.kind).then(|| sc::get_sce(c, bl.id, SC_WINDSIGN)).flatten() {
                if ((c.rnd() % 100) as i32) < e.val2 {
                    crate::status::hp::status_heal(c, src.id, 0, 0, 1, 0);
                }
            }
        }
    }
    if damage > 0 {
        // Post-damage effects
        if skill_id == GC_VENOMPRESSURE {
            if let Some(pw) = crate::battle::mobif::has_sc(src.kind).then(|| sc::get_sce(c, src.id, SC_POISONINGWEAPON)).flatten() {
                if ((c.rnd() % 100) as i32) < 70 + 5 * skill_lv as i32 {
                    if let Some(t) = unit::get_or_freed(c, bl.id) {
                        sc::sc_start4(c, Some(&src), &t, pw.val2, 100, pw.val1, 0, 1, 0, skill_get_time2(GC_POISONINGWEAPON, 1) as i64);
                    }
                    sc::status_change_end(c, src.id, SC_POISONINGWEAPON, 0);
                    if let Some(t) = unit::get_or_freed(c, bl.id) {
                        super::clif::clif_skill_nodamage(c, Some(&src), &t, skill_id, skill_lv as i32, true);
                    }
                }
            }
        }
        if sd.is_some() {
            let t = unit::get_or_freed(c, bl.id).unwrap_or_else(|| bl.clone());
            super::effect::skill_onskillusage(c, src.id, Some(&t), skill_id, tick);
        }
    }
    if flag & 2 == 0 {
        match skill_id {
            MG_COLDBOLT | MG_FIREBOLT | MG_LIGHTNINGBOLT => {
                if let Some(e) = crate::battle::mobif::has_sc(src.kind).then(|| sc::get_sce(c, src.id, SC_DOUBLECAST)).flatten() {
                    if ((c.rnd() % 100) as i32) < e.val2 {
                        super::timerskill::skill_addtimerskill(c, &src, tick + dmg.amotion as i64, bl.id, 0, 0, skill_id, skill_lv, BF_MAGIC, flag | 2);
                    }
                }
            }
            SU_BITE | SU_SCRATCH | SU_SV_STEMSPEAR | SU_SCAROFTAROU | SU_PICKYPECK => {
                let lv = crate::battle::bl::lv_of(c, &src);
                if lv > 29 && ((c.rnd() % 100) as i32) < 10 * lv / 30 {
                    let t = tick + dmg.amotion as i64 + skill_get_delay(skill_id, skill_lv) as i64;
                    super::timerskill::skill_addtimerskill(c, &src, t, bl.id, 0, 0, skill_id, skill_lv, attack_type, flag | 2);
                }
            }
            _ => {}
        }
    }
    if flag & 0x1000000 != 0 && rmdamage {
        return 0; // Should return 0 when damage was reflected
    }
    damage
}

#[cfg(test)]
mod tests {
    #[test]
    fn constants_match_script_constants() {
        let k = |n: &str| crate::data::constant(n).unwrap_or_else(|| panic!("constant {n}"));
        assert_eq!(k("EF_ATTACKENERGY2"), super::EF_ATTACKENERGY2 as i64);
    }
}
