//! skills/swordman/pierce.cpp: class SkillPierce : public WeaponSkillImpl (KN_PIERCE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::sce;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_CHARGINGPIERCE_COUNT;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillPierce;

impl SkillImpl for SkillPierce {
    fn skill_id(&self) -> u16 {
        KN_PIERCE
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillPierce::modifyDamageData: one hit per size step of the target (small 1, medium 2, large 3).
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, _src: &Unit, target: &Unit, _skill_lv: u16) {
        let size = crate::status::status_get_status_data(c, target).size as i32;
        dmg.div_ = (if dmg.div_ > 0 { size + 1 } else { -(size + 1) }) as i16;
    }

    /// SkillPierce::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 10 * skill_lv as i32;
        if sce(c, src, SC_CHARGINGPIERCE_COUNT).map(|e| e.val1 >= 10).unwrap_or(false) {
            *base_skillratio *= 2;
        }
    }

    /// SkillPierce::modifyHitRate.
    fn modify_hit_rate(&self, _c: &Ctx, hit_rate: &mut i16, _src: &Unit, _target: &Unit, skill_lv: u16) {
        let h = *hit_rate as i32;
        *hit_rate = (h + h * 5 * skill_lv as i32 / 100) as i16;
    }
}
