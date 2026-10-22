//! skills/merchant/explosivepowder.cpp: class SkillExplosivePowder : public SkillImplRecursiveDamageSplash (BO_EXPLOSIVE_POWDER).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::status_get_status_data;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};
use crate::status::consts::SC_RESEARCHREPORT;

/// SkillExplosivePowder::SkillExplosivePowder() : SkillImplRecursiveDamageSplash(BO_EXPLOSIVE_POWDER).
pub struct SkillExplosivePowder;

impl SkillImpl for SkillExplosivePowder {
    fn skill_id(&self) -> u16 {
        BO_EXPLOSIVE_POWDER
    }
    crate::splash_skill_defaults!();

    /// modifyDamageData: `dmg.div_ = 5` while the caster has SC_RESEARCHREPORT.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if crate::skills::novice::sce(c, src, SC_RESEARCHREPORT).is_some() {
            dmg.div_ = 5;
        }
    }

    /// SkillExplosivePowder::calculateSkillRatio: 500% + 650% per level + POW x 5, +100% per level under SC_RESEARCHREPORT, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = status_get_status_data(c, src);
        *skillratio += -100 + 500 + 650 * skill_lv as i32;
        *skillratio += 5 * sstatus.pow as i32;
        if sce(c, src, SC_RESEARCHREPORT).is_some() {
            *skillratio += 100 * skill_lv as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillExplosivePowder::castendNoDamageId: the skill packet, then skill_castend_damage_id on the target (the
    /// splash).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        crate::skill::castend::skill_castend_damage_id(c, src, &t, id, skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillExplosivePowder {}
