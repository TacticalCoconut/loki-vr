//! skills/merchant/triplelaser.cpp: class SkillTripleLaser : public WeaponSkillImpl (MT_TRIPLE_LASER).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillTripleLaser::SkillTripleLaser() : WeaponSkillImpl(MT_TRIPLE_LASER).
pub struct SkillTripleLaser;

impl SkillImpl for SkillTripleLaser {
    fn skill_id(&self) -> u16 {
        MT_TRIPLE_LASER
    }

    /// WeaponSkillImpl::castendDamageId (inherited).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        crate::skills::weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillTripleLaser::calculateSkillRatio: 650 + 1150 * lv %, + 12 * POW, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 650 + 1150 * skill_lv as i32;
        *base_skillratio += 12 * sstatus.pow as i32;
        re_lvl_dmod(c, src, base_skillratio, 100);
    }
}
