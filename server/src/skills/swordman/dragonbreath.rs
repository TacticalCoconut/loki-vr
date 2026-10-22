//! skills/swordman/dragonbreath.cpp: class SkillDragonBreath and class SkillDragonBreathWater, both
//! `public SkillImplRecursiveDamageSplash` (RK_DRAGONBREATH, RK_DRAGONBREATH_WATER). They replace castendDamageId and
//! keep the splash's castendPos2.

use crate::battle::calc::Damage;
use crate::battle::BF_WEAPON;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::sce;
use crate::skills::{splash_castend_pos2, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_BURNING, SC_FIGHTINGSPIRIT, SC_FREEZING, SC_GIANTGROWTH, SC_HIDING, SC_LUXANIMA};
use crate::status::defs::{ELE_DARK, ELE_GHOST, ELE_HOLY, ELE_NEUTRAL};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// The shared castendDamageId body of both classes: a hidden target only shows the skill on the caster.
fn breath_castend_damage_id(c: &Ctx, skill_id: u16, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
    if sce(c, target, SC_HIDING).is_some() {
        clif_skill_nodamage(c, Some(src), src, skill_id, skill_lv as i32, true);
    } else {
        skill_attack(c, BF_WEAPON, src, src, target, skill_id, skill_lv, tick, flag);
    }
}

// ---------------------------------------------------------------------------------------------- RK_DRAGONBREATH

pub struct SkillDragonBreath;

impl SkillImpl for SkillDragonBreath {
    fn skill_id(&self) -> u16 {
        RK_DRAGONBREATH
    }

    /// SkillDragonBreath::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start4(c, Some(src), target, SC_BURNING, 15, skill_lv as i32, 1000, src.id as i32, 0, skill_get_time(self.skill_id(), skill_lv) as i64);
    }

    /// SkillDragonBreath::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        breath_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillImplRecursiveDamageSplash::castendPos2 (not overridden).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        splash_castend_pos2(self, c, src, x, y, skill_lv, tick, flag);
    }

    /// SkillDragonBreath::modifyElement.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        if crate::battle::mobif::has_sc(src.kind) {
            if sce(c, src, SC_LUXANIMA).is_some() {
                // Lux Anima has priority over Giant Growth
                *element = ELE_DARK;
            } else if sce(c, src, SC_GIANTGROWTH).is_some() {
                *element = ELE_HOLY;
            }
        }
    }
}

impl RecursiveDamageSplash for SkillDragonBreath {}

// ---------------------------------------------------------------------------------------------- RK_DRAGONBREATH_WATER

pub struct SkillDragonBreathWater;

impl SkillImpl for SkillDragonBreathWater {
    fn skill_id(&self) -> u16 {
        RK_DRAGONBREATH_WATER
    }

    /// SkillDragonBreathWater::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_FREEZING, 15, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }

    /// SkillDragonBreathWater::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        breath_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillImplRecursiveDamageSplash::castendPos2 (not overridden).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        splash_castend_pos2(self, c, src, x, y, skill_lv, tick, flag);
    }

    /// SkillDragonBreathWater::modifyElement.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        if crate::battle::mobif::has_sc(src.kind) {
            if sce(c, src, SC_LUXANIMA).is_some() {
                // Lux Anima has priority over Fighting Spirit
                *element = ELE_NEUTRAL;
            } else if sce(c, src, SC_FIGHTINGSPIRIT).is_some() {
                *element = ELE_GHOST;
            }
        }
    }
}

impl RecursiveDamageSplash for SkillDragonBreathWater {}
