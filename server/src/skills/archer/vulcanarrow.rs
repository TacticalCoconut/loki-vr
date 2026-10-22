//! skills/archer/vulcanarrow.cpp: class SkillVulcanArrow : public WeaponSkillImpl (CG_ARROWVULCAN).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};
use crate::skills::novice::re_lvl_dmod;

pub struct SkillVulcanArrow;

impl SkillImpl for SkillVulcanArrow {
    fn skill_id(&self) -> u16 {
        CG_ARROWVULCAN
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillVulcanArrow::calculateSkillRatio: Renewal: `skillratio += 400 + 100 * skill_lv; RE_LVL_DMOD(100);`.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 400 + 100 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
