//! skills/swordman/judgementcross.cpp: class SkillJudgementCross : public SkillImpl (IG_JUDGEMENT_CROSS).

use super::pc_checkskill_imperial_guard;
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::status::defs::{RC_INSECT, RC_PLANT};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillJudgementCross;

impl SkillImpl for SkillJudgementCross {
    fn skill_id(&self) -> u16 {
        IG_JUDGEMENT_CROSS
    }

    /// SkillJudgementCross::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
    }

    /// SkillJudgementCross::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let tstatus = crate::status::status_get_status_data(c, target);
        let lv = skill_lv as i32;
        *skillratio += -100 + 1950 * lv + 10 * sstatus.spl as i32;
        if tstatus.race as i32 == RC_PLANT || tstatus.race as i32 == RC_INSECT {
            *skillratio += 150 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
        let i = pc_checkskill_imperial_guard(c, src, 3);
        if i > 0 {
            *skillratio += *skillratio * i / 100;
        }
    }
}
