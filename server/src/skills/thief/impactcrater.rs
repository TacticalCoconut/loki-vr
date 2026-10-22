//! skills/thief/impactcrater.cpp: class SkillImpactCrater : public SkillImplRecursiveDamageSplash (SHC_IMPACT_CRATER).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillImpactCrater;

impl SkillImpl for SkillImpactCrater {
    fn skill_id(&self) -> u16 {
        SHC_IMPACT_CRATER
    }

    crate::splash_skill_defaults!();

    /// SkillImpactCrater::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 200 * skill_lv as i32;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillImpactCrater::castendNoDamageId: the status, the animation, then the damage part.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
        skill_castend_damage_id(c, &s, &t, id, skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillImpactCrater {}
