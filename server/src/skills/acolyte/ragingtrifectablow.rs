//! skills/acolyte/ragingtrifectablow.cpp: class SkillRagingTrifectaBlow : public WeaponSkillImpl (MO_TRIPLEATTACK).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::SD_ANIMATION;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRagingTrifectaBlow;

impl SkillImpl for SkillRagingTrifectaBlow {
    fn skill_id(&self) -> u16 {
        MO_TRIPLEATTACK
    }

    /// SkillRagingTrifectaBlow::castendDamageId: WeaponSkillImpl::castendDamageId with SD_ANIMATION.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let sflag = *flag | SD_ANIMATION;
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, sflag);
    }

    /// SkillRagingTrifectaBlow::calculateSkillRatio: +20% per level.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 20 * skill_lv as i32;
    }
}
