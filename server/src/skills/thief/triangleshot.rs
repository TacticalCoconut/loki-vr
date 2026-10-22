//! skills/thief/triangleshot.cpp: class SkillTriangleShot : public WeaponSkillImpl (SC_TRIANGLESHOT).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillTriangleShot;

impl SkillImpl for SkillTriangleShot {
    fn skill_id(&self) -> u16 {
        SC_TRIANGLESHOT
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillTriangleShot::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 230 * skill_lv as i32 + 3 * sstatus.agi as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
