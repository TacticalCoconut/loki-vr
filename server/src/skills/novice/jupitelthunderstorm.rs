//! skills/novice/jupitelthunderstorm.cpp: class SkillJupitelThunderstorm : public SkillImplRecursiveDamageSplash
//! (HN_JUPITEL_THUNDER_STORM).

use super::{pc_checkskill, re_lvl_dmod, sce};
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_RULEBREAK;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillJupitelThunderstorm;

impl SkillImpl for SkillJupitelThunderstorm {
    fn skill_id(&self) -> u16 {
        HN_JUPITEL_THUNDER_STORM
    }

    crate::splash_skill_defaults!();

    /// SkillJupitelThunderstorm::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 1800 * lv;
        *skillratio += pc_checkskill(c, src, HN_SELFSTUDY_SOCERY) * 3 * lv;
        *skillratio += 3 * sstatus.spl as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        // After RE_LVL_DMOD calculation, HN_SELFSTUDY_SOCERY amplifies the skill ratio of HN_JUPITEL_THUNDER_STORM by
        // (skill level)%
        *skillratio += *skillratio * pc_checkskill(c, src, HN_SELFSTUDY_SOCERY) / 100;
        // SC_RULEBREAK increases the skill ratio after HN_SELFSTUDY_SOCERY
        if sce(c, src, SC_RULEBREAK).is_some() {
            *skillratio += *skillratio * 70 / 100;
        }
    }
}

impl RecursiveDamageSplash for SkillJupitelThunderstorm {
    /// SkillJupitelThunderstorm::splashSearch.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
