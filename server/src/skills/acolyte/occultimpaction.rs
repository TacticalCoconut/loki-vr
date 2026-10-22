//! skills/acolyte/occultimpaction.cpp: class SkillOccultImpaction : public WeaponSkillImpl (MO_INVESTIGATE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::sce;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_BLADESTOP;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillOccultImpaction;

impl SkillImpl for SkillOccultImpaction {
    fn skill_id(&self) -> u16 {
        MO_INVESTIGATE
    }

    /// SkillOccultImpaction::castendDamageId: WeaponSkillImpl::castendDamageId, then Blade Stop ends.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
        sc::status_change_end(c, src.id, SC_BLADESTOP, 0);
    }

    /// SkillOccultImpaction::calculateSkillRatio (RENEWAL): 100% per level; +50% on a Blade Stopped target.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, _src: &Unit, target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + 100 * skill_lv as i32;
        if sce(c, target, SC_BLADESTOP).is_some() {
            *base_skillratio += *base_skillratio / 2;
        }
    }
}
