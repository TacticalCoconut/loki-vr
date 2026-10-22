//! skills/merchant/vaporize.cpp: class SkillVaporize : public SkillImpl (AM_REST).
//!
//! homunculus.cpp hom_vaporize is the homun agent's (wave E): a stub here until its interface is in COORD (see
//! [`super::homun`]).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillVaporize::SkillVaporize() : SkillImpl(AM_REST).
pub struct SkillVaporize;

impl SkillImpl for SkillVaporize {
    fn skill_id(&self) -> u16 {
        AM_REST
    }

    /// SkillVaporize::castendNoDamageId: hom_vaporize(sd, HOM_ST_REST): the skill packet when it worked, else
    /// clif_skill_fail.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != BL_PC {
            return;
        }
        if crate::homunculus::hom_vaporize(c, src.id, crate::homunculus::data::HOM_ST_REST) != 0 {
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        } else {
            skill_fail(c, src, id);
        }
    }
}
