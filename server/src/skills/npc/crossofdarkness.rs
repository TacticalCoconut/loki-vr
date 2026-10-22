//! skills/npc/crossofdarkness.cpp: class SkillCrossOfDarkness : public WeaponSkillImpl (NPC_DARKCROSS), rAthena
//! e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_BLIND;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillCrossOfDarkness;

impl SkillImpl for SkillCrossOfDarkness {
    fn skill_id(&self) -> u16 {
        NPC_DARKCROSS
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillCrossOfDarkness::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_BLIND, 3 * skill_lv as i32, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillCrossOfDarkness::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 35 * skill_lv as i32;
    }
}
