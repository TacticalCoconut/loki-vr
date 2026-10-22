//! skills/thief/sandattack.cpp: class SkillSandAttack : public WeaponSkillImpl (TF_SPRINKLESAND).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_BLIND;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSandAttack;

impl SkillImpl for SkillSandAttack {
    fn skill_id(&self) -> u16 {
        TF_SPRINKLESAND
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillSandAttack::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 30;
    }

    /// SkillSandAttack::applyAdditionalEffects: Blind, 20% from a player, 15% otherwise.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let rate = if src.kind == BL_PC { 20 } else { 15 };
        sc::sc_start(c, Some(src), target, SC_BLIND, rate, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }
}
