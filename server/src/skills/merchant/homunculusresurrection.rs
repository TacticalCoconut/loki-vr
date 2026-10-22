//! skills/merchant/homunculusresurrection.cpp: class SkillHomunculusResurrection : public SkillImpl (AM_RESURRECTHOMUN).
//!
//! homunculus.cpp hom_ressurect is the homun agent's (wave E): a stub here until its interface is in COORD (see
//! [`super::homun`]).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::skill_fail;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillHomunculusResurrection::SkillHomunculusResurrection() : SkillImpl(AM_RESURRECTHOMUN).
pub struct SkillHomunculusResurrection;

impl SkillImpl for SkillHomunculusResurrection {
    fn skill_id(&self) -> u16 {
        AM_RESURRECTHOMUN
    }

    /// SkillHomunculusResurrection::castendPos2: hom_ressurect(sd, 20 * skill_lv, x, y), clif_skill_fail when it fails.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind == BL_PC && crate::homunculus::hom_ressurect(c, src.id, (20 * skill_lv) as u8, x, y) == 0 {
            skill_fail(c, src, self.skill_id());
        }
    }
}
