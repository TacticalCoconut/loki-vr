//! skills/acolyte/duplelight.cpp: class SkillDupleLightMagic : public SkillImpl (AB_DUPLELIGHT_MAGIC) and class
//! SkillDupleLightMelee : public WeaponSkillImpl (AB_DUPLELIGHT_MELEE): the two autocast hits of Duple Light
//! (AB_DUPLELIGHT itself is a StatusSkillImpl; its status casts these, see battle/weapon_attack.rs).

use crate::battle::calc::Damage;
use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDupleLightMagic;

impl SkillImpl for SkillDupleLightMagic {
    fn skill_id(&self) -> u16 {
        AB_DUPLELIGHT_MAGIC
    }

    /// SkillDupleLightMagic::calculateSkillRatio: +300% + 40% per level.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 300 + 40 * skill_lv as i32;
    }

    /// SkillDupleLightMagic::castendDamageId: the magic hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }
}

pub struct SkillDupleLightMelee;

impl SkillImpl for SkillDupleLightMelee {
    fn skill_id(&self) -> u16 {
        AB_DUPLELIGHT_MELEE
    }

    /// SkillDupleLightMelee::calculateSkillRatio: +50% + 15% per level.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 50 + 15 * skill_lv as i32;
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }
}
