//! skills/mercenary/mercenary_spiralpierce.cpp: class SkillMercenarySpiralPierce : public WeaponSkillImpl
//! (ML_SPIRALPIERCE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMercenarySpiralPierce;

impl SkillImpl for SkillMercenarySpiralPierce {
    fn skill_id(&self) -> u16 {
        ML_SPIRALPIERCE
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillMercenarySpiralPierce::calculateSkillRatio (RENEWAL; unlike the Lord Knight's class it has no
    /// modifyDamageData / modifyElement / applyAdditionalEffects).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 50 + 50 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
