//! skills/thief/doubleattack.cpp: class SkillDoubleAttack : public WeaponSkillImpl (TF_DOUBLE).

use crate::battle::calc::Damage;
use crate::battle::DMG_MULTI_HIT;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDoubleAttack;

impl SkillImpl for SkillDoubleAttack {
    fn skill_id(&self) -> u16 {
        TF_DOUBLE
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillDoubleAttack::modifyDamageData: for NPC used skill.
    fn modify_damage_data(&self, _c: &Ctx, dmg: &mut Damage, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        dmg.type_ = DMG_MULTI_HIT;
    }
}
