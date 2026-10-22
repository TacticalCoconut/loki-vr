//! skills/acolyte/assumptio.cpp: class SkillAssumptio : public StatusSkillImpl (HP_ASSUMPTIO).

use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::skill_fail;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillAssumptio;

impl SkillImpl for SkillAssumptio {
    fn skill_id(&self) -> u16 {
        HP_ASSUMPTIO
    }

    /// SkillAssumptio::castendNoDamageId: a player cannot cast it on a monster; else
    /// StatusSkillImpl::castendNoDamageId (end_if_running = false).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind == BL_PC && target.kind == BL_MOB {
            skill_fail(c, src, self.skill_id());
        } else {
            status_castend_nodamage_id(c, self.skill_id(), false, src, target, skill_lv);
        }
    }
}
