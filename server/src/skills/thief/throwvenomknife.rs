//! skills/thief/throwvenomknife.cpp: class SkillThrowVenomKnife : public WeaponSkillImpl (AS_VENOMKNIFE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_POISON;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillThrowVenomKnife;

impl SkillImpl for SkillThrowVenomKnife {
    fn skill_id(&self) -> u16 {
        AS_VENOMKNIFE
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillThrowVenomKnife::calculateSkillRatio (RENEWAL).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 400;
    }

    /// SkillThrowVenomKnife::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start2(c, Some(src), target, SC_POISON, 100, skill_lv as i32, src.id as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }
}
