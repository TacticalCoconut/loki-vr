//! skills/novice/overcomingcrisis.cpp: class SkillOvercomingCrisis : public StatusSkillImpl (HN_OVERCOMING_CRISIS).

use crate::db::skills::*;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillOvercomingCrisis;

impl SkillImpl for SkillOvercomingCrisis {
    fn skill_id(&self) -> u16 {
        HN_OVERCOMING_CRISIS
    }

    /// SkillOvercomingCrisis::castendNoDamageId: StatusSkillImpl::castendNoDamageId, then status_percent_heal(target,
    /// 100, 0).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        status_castend_nodamage_id(c, self.skill_id(), false, src, target, skill_lv);
        crate::status::hp::status_percent_heal(c, target.id, 100, 0, 0);
    }
}
