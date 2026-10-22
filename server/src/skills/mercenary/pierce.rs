//! skills/mercenary/mercenary_pierce.cpp: class SkillMercenaryPierce : public WeaponSkillImpl (ML_PIERCE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMercenaryPierce;

impl SkillImpl for SkillMercenaryPierce {
    fn skill_id(&self) -> u16 {
        ML_PIERCE
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillMercenaryPierce::modifyDamageData: one hit per size step of the target (small 1, medium 2, large 3).
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, _src: &Unit, target: &Unit, _skill_lv: u16) {
        let size = crate::status::status_get_status_data(c, target).size as i32;
        dmg.div_ = (if dmg.div_ > 0 { size + 1 } else { -(size + 1) }) as i16;
    }

    /// SkillMercenaryPierce::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 10 * skill_lv as i32;
    }

    /// SkillMercenaryPierce::modifyHitRate.
    fn modify_hit_rate(&self, _c: &Ctx, hit_rate: &mut i16, _src: &Unit, _target: &Unit, skill_lv: u16) {
        let h = *hit_rate as i32;
        *hit_rate = (h + h * 5 * skill_lv as i32 / 100) as i16;
    }
}
