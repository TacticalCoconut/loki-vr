//! skills/acolyte/gateofhell.cpp: class SkillGateOfHell : public WeaponSkillImpl (SR_GATEOFHELL).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::{SC_COMBO, SC_GT_REVITALIZE};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGateOfHell;

impl SkillImpl for SkillGateOfHell {
    fn skill_id(&self) -> u16 {
        SR_GATEOFHELL
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillGateOfHell::calculateSkillRatio: 800% per level after Fallen Empire (the combo), else 500% per level
    /// (RE_LVL_DMOD 100); +30% under Gentle Touch - Revitalize (rAthena reads `sc->getSCE` there without a null
    /// check; a caster without a status_change (a pet) would crash rAthena, here it just gets no bonus).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        if sce(c, src, SC_COMBO).is_some_and(|e| e.val1 == SR_FALLENEMPIRE as i32) {
            *skillratio += -100 + 800 * skill_lv as i32;
        } else {
            *skillratio += -100 + 500 * skill_lv as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
        if sce(c, src, SC_GT_REVITALIZE).is_some() {
            *skillratio += *skillratio * 30 / 100;
        }
    }
}
