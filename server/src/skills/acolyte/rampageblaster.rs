//! skills/acolyte/rampageblaster.cpp: class SkillRampageBlaster : public SkillImplRecursiveDamageSplash
//! (SR_RAMPAGEBLASTER).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_EARTHSHAKER, SC_GT_CHANGE};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillRampageBlaster;

impl SkillImpl for SkillRampageBlaster {
    fn skill_id(&self) -> u16 {
        SR_RAMPAGEBLASTER
    }

    crate::splash_skill_defaults!();

    /// SkillRampageBlaster::calculateSkillRatio: +1400% + 550% per level on a target under Earth Shaker
    /// (RE_LVL_DMOD 120), else +900% + 350% per level (RE_LVL_DMOD 150); +30% under Gentle Touch - Change.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        if sce(c, target, SC_EARTHSHAKER).is_some() {
            *skillratio += 1400 + 550 * skill_lv as i32;
            re_lvl_dmod(c, src, skillratio, 120);
        } else {
            *skillratio += 900 + 350 * skill_lv as i32;
            re_lvl_dmod(c, src, skillratio, 150);
        }
        if sce(c, src, SC_GT_CHANGE).is_some() {
            *skillratio += *skillratio * 30 / 100;
        }
    }

    /// SkillRampageBlaster::castendNoDamageId: the animation, then the splash through skill_castend_damage_id.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        skill_castend_damage_id(c, src, &t, id, skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillRampageBlaster {}
