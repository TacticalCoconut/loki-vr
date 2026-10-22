//! skills/archer/amp.cpp: class SkillAmp : public StatusSkillImpl (BD_ADAPTATION).

use crate::db::skills::*;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillAmp;

impl SkillImpl for SkillAmp {
    fn skill_id(&self) -> u16 {
        BD_ADAPTATION
    }

    /// SkillAmp::castendNoDamageId: Renewal: `StatusSkillImpl::castendNoDamageId(src, target, skill_lv, tick, flag)`
    /// (the pre-Renewal body ends the target's SC_DANCING).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        status_castend_nodamage_id(c, self.skill_id(), false, src, target, skill_lv);
    }
}
