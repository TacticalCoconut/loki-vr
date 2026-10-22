//! skills/mercenary/mercenary_bash.cpp: class SkillMercenaryBash : public WeaponSkillImpl (MS_BASH).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMercenaryBash;

impl SkillImpl for SkillMercenaryBash {
    fn skill_id(&self) -> u16 {
        MS_BASH
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillMercenaryBash::calculateSkillRatio: it is proven that bonus is applied on final hitrate, not hit. Base
    /// 100% + 30% per level.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 30 * skill_lv as i32;
    }

    /// SkillMercenaryBash::modifyHitRate: +5% hit per level.
    fn modify_hit_rate(&self, _c: &Ctx, hit_rate: &mut i16, _src: &Unit, _target: &Unit, skill_lv: u16) {
        let h = *hit_rate as i32;
        *hit_rate = (h + h * 5 * skill_lv as i32 / 100) as i16;
    }
}
