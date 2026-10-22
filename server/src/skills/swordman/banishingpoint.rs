//! skills/swordman/banishingpoint.cpp: class SkillBanishingPoint : public WeaponSkillImpl (LG_BANISHINGPOINT).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_SPEAR_SCAR;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillBanishingPoint;

impl SkillImpl for SkillBanishingPoint {
    fn skill_id(&self) -> u16 {
        LG_BANISHINGPOINT
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillBanishingPoint::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + (100 * skill_lv as i32);
        // sd != nullptr
        *skillratio += pc_checkskill(c, src, SM_BASH) * 70;
        if sce(c, src, SC_SPEAR_SCAR).is_some() {
            *skillratio += 800;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillBanishingPoint::modifyHitRate.
    fn modify_hit_rate(&self, _c: &Ctx, hit_rate: &mut i16, _src: &Unit, _target: &Unit, skill_lv: u16) {
        *hit_rate = (*hit_rate as i32 + 5 * skill_lv as i32) as i16;
    }
}
