//! skills/thief/rollingcutter.cpp: class SkillRollingCutter : public SkillImplRecursiveDamageSplash (GC_ROLLINGCUTTER).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, skill_area_sub, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::{SD_PREAMBLE, SD_SPLASH};
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_ROLLINGCUTTER;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillRollingCutter;

impl SkillImpl for SkillRollingCutter {
    fn skill_id(&self) -> u16 {
        GC_ROLLINGCUTTER
    }

    crate::splash_skill_defaults!();

    /// SkillRollingCutter::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 50 + 80 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillRollingCutter::castendNoDamageId: the damage part on every character around the caster (with a preamble
    /// packet), then the counter on the target goes up by one (max 10: the status is restarted), then the animation.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let mut count: i16 = 1;
        area::set_temp(2, 0);
        let f = *flag | BCT_ENEMY | SD_PREAMBLE | SD_SPLASH | 1;
        foreach_in_range(c, src, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::Config, |c, bl| skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
        if let Some(e) = sce(c, target, SC_ROLLINGCUTTER) {
            // Every time the skill is casted the status change is reseted adding a counter.
            count += e.val1 as i16;
            if count > 10 {
                count = 10; // Max coounter
            }
            sc::status_change_end(c, target.id, SC_ROLLINGCUTTER, 0);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        sc::sc_start(c, Some(&s), &t, SC_ROLLINGCUTTER, 100, count as i32, skill_get_time(id, skill_lv) as i64);
        let s = unit::get(c, src.id).unwrap_or(s);
        clif_skill_nodamage(c, Some(&s), &s, id, skill_lv as i32, true);
    }
}

impl RecursiveDamageSplash for SkillRollingCutter {}
