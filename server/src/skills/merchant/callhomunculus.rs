//! skills/merchant/callhomunculus.cpp: class SkillCallHomunculus : public SkillImpl (AM_CALLHOMUN).
//!
//! homunculus.cpp is the homun agent's (wave E): hom_call and hom_is_active are stubs here until its interface is in
//! COORD (see [`super::homun`]).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::skill_fail;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillCallHomunculus::SkillCallHomunculus() : SkillImpl(AM_CALLHOMUN).
pub struct SkillCallHomunculus;

impl SkillImpl for SkillCallHomunculus {
    fn skill_id(&self) -> u16 {
        AM_CALLHOMUN
    }

    /// SkillCallHomunculus::castendNoDamageId: hom_call, clif_skill_fail when it fails; RENEWAL: with the homunculus
    /// active, skill_area_temp[0] = 1 ("Already passed pre-cast checks").
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, _skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind != BL_PC {
            return;
        }
        if !crate::homunculus::hom_call(c, src.id) {
            skill_fail(c, src, self.skill_id());
        } else if crate::homunculus::data::is_active_of(c, src.id) {
            crate::skill::area::set_temp(0, 1); // Already passed pre-cast checks
        }
    }
}
