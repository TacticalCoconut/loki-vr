//! skills/homunculus/homunculus_change.cpp: class SkillChange : public StatusSkillImpl (HLIF_CHANGE).

use crate::db::skills::*;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillChange;

impl SkillImpl for SkillChange {
    fn skill_id(&self) -> u16 {
        HLIF_CHANGE
    }

    /// SkillChange::castendNoDamageId: StatusSkillImpl's status start (the pre-renewal status_percent_heal(target,
    /// 100, 100) is `#ifndef RENEWAL`: not in this build).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        status_castend_nodamage_id(c, self.skill_id(), false, src, target, skill_lv);
    }
}
