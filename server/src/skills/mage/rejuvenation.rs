//! skills/mage/rejuvenation.cpp: class SkillRejuvenation : public SkillImpl (SA_FULLRECOVERY).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::hp::status_percent_heal;
use crate::status::sc::status_isimmune;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRejuvenation;

impl SkillImpl for SkillRejuvenation {
    fn skill_id(&self) -> u16 {
        SA_FULLRECOVERY
    }

    /// SkillRejuvenation::castendNoDamageId: full HP / SP unless the target is immune.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        if status_isimmune(c, target) != 0 {
            return;
        }
        // status_percent_heal(target, 100, 100) (ap_rate defaults to 0)
        status_percent_heal(c, target.id, 100, 100, 0);
    }
}
