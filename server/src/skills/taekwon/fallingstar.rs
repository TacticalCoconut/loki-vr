//! skills/taekwon/fallingstar.cpp: class SkillFallingStar : public StatusSkillImpl (SJ_FALLINGSTAR); class SkillFallingStarAttack : public SkillImplRecursiveDamageSplash (SJ_FALLINGSTAR_ATK); class SkillFallingStarAttack2 : public SkillImplRecursiveDamageSplash (SJ_FALLINGSTAR_ATK2).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::state::MAX_STELLAR_MARKS;
use crate::skills::novice::{re_lvl_dmod, sce, splash_damage_default};
use crate::skills::taekwon::arr_find;
use crate::skills::{status_castend_nodamage_id, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_FLASHKICK, SC_LIGHTOFSTAR};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

// SJ_FALLINGSTAR
pub struct SkillFallingStar;

impl SkillImpl for SkillFallingStar {
    fn skill_id(&self) -> u16 {
        SJ_FALLINGSTAR
    }

    /// StatusSkillImpl::castendNoDamageId (not overridden).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        status_castend_nodamage_id(c, self.skill_id(), false, src, target, skill_lv);
    }
}

// SJ_FALLINGSTAR_ATK
pub struct SkillFallingStarAttack;

impl SkillImpl for SkillFallingStarAttack {
    fn skill_id(&self) -> u16 {
        SJ_FALLINGSTAR_ATK
    }

    crate::splash_skill_defaults!();

    /// SkillFallingStarAttack::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 100 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if let Some(e) = sce(c, src, SC_LIGHTOFSTAR) {
            *skillratio += *skillratio * e.val2 / 100;
        }
    }

    /// SkillFallingStarAttack::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        skill_castend_damage_id(c, src, target, id, skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillFallingStarAttack {
    /// SkillFallingStarAttack::splashDamage.
    fn splash_damage(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) -> i64 {
        let id = self.skill_id();
        // TODO: refactor logic
        if src.kind == BL_PC {
            // If a player used the skill it will search for targets marked by that player.
            if sce(c, target, SC_FLASHKICK).map(|e| e.val4 == 1).unwrap_or(false) {
                // Mark placed by a player.
                let marks = crate::skill::state::get(c, src.id).stellar_mark;
                let i = arr_find(&marks, MAX_STELLAR_MARKS, target.id);
                if i < MAX_STELLAR_MARKS {
                    let dmg = splash_damage_default(c, id, src, target, skill_lv, tick, flag);
                    let s = crate::unit::get_or_freed(c, src.id).unwrap_or_else(|| src.clone());
                    let t = crate::unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
                    skill_castend_damage_id(c, &s, &t, SJ_FALLINGSTAR_ATK2, skill_lv, tick, 0);
                    return dmg;
                }
            }
        } else if sce(c, target, SC_FLASHKICK).map(|e| e.val4 == 2).unwrap_or(false) {
            // Mark placed by a monster.
            // If a monster used the skill it will search for targets marked by any monster since they can't track their own targets.
            let dmg = splash_damage_default(c, id, src, target, skill_lv, tick, flag);
            let s = crate::unit::get_or_freed(c, src.id).unwrap_or_else(|| src.clone());
            let t = crate::unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
            skill_castend_damage_id(c, &s, &t, SJ_FALLINGSTAR_ATK2, skill_lv, tick, 0);
            return dmg;
        }
        0
    }
}

// SJ_FALLINGSTAR_ATK2
pub struct SkillFallingStarAttack2;

impl SkillImpl for SkillFallingStarAttack2 {
    fn skill_id(&self) -> u16 {
        SJ_FALLINGSTAR_ATK2
    }

    crate::splash_skill_defaults!();

    /// SkillFallingStarAttack2::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 100 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if let Some(e) = sce(c, src, SC_LIGHTOFSTAR) {
            *skillratio += *skillratio * e.val2 / 100;
        }
    }
}

impl RecursiveDamageSplash for SkillFallingStarAttack2 {}
