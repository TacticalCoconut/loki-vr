//! skills/novice/napalmvulcanstrike.cpp: class SkillNapalmVulcanStrike : public SkillImpl (HN_NAPALM_VULCAN_STRIKE).

use super::{pc_checkskill, re_lvl_dmod, sce};
use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_SPLASH;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_CURSE, SC_RULEBREAK};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillNapalmVulcanStrike;

impl SkillImpl for SkillNapalmVulcanStrike {
    fn skill_id(&self) -> u16 {
        HN_NAPALM_VULCAN_STRIKE
    }

    /// SkillNapalmVulcanStrike::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_CURSE, 5 * skill_lv as i32, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillNapalmVulcanStrike::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 350 + 650 * lv;
        *skillratio += pc_checkskill(c, src, HN_SELFSTUDY_SOCERY) * 4 * lv;
        *skillratio += 3 * sstatus.spl as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        // After RE_LVL_DMOD calculation, HN_SELFSTUDY_SOCERY amplifies the skill ratio of HN_NAPALM_VULCAN_STRIKE by
        // (2x skill level)%
        *skillratio += *skillratio * 2 * pc_checkskill(c, src, HN_SELFSTUDY_SOCERY) / 100;
        // SC_RULEBREAK increases the skill ratio after HN_SELFSTUDY_SOCERY
        if sce(c, src, SC_RULEBREAK).is_some() {
            *skillratio += *skillratio * 40 / 100;
        }
    }

    /// SkillNapalmVulcanStrike::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        } else {
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            let f = *flag | BCT_ENEMY | SD_SPLASH | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::Config, |c, bl| {
                area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id)
            });
        }
    }
}
