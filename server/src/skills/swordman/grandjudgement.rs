//! skills/swordman/grandjudgement.cpp: class SkillGrandJudgement : public SkillImplRecursiveDamageSplash
//! (IG_GRAND_JUDGEMENT).

use super::pc_checkskill_imperial_guard;
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::defs::{RC_INSECT, RC_PLANT};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillGrandJudgement;

impl SkillImpl for SkillGrandJudgement {
    fn skill_id(&self) -> u16 {
        IG_GRAND_JUDGEMENT
    }

    crate::splash_skill_defaults!();

    /// SkillGrandJudgement::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let tstatus = crate::status::status_get_status_data(c, target);
        let lv = skill_lv as i32;
        *skillratio += -100 + 250 + 1500 * lv + 10 * sstatus.pow as i32;
        if tstatus.race as i32 == RC_PLANT || tstatus.race as i32 == RC_INSECT {
            *skillratio += 100 + 150 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
        let i = pc_checkskill_imperial_guard(c, src, 3);
        if i > 0 {
            *skillratio += *skillratio * i / 100;
        }
    }
}

impl RecursiveDamageSplash for SkillGrandJudgement {
    /// SkillGrandJudgement::splashSearch: the animation and the caster's status, then the splash.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        sc::sc_start(c, Some(&s), &s, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let s = unit::get(c, src.id).unwrap_or(s);
        splash_search_default(self, c, &s, target, skill_lv, tick, flag);
    }
}
