//! skills/ninja/moonlightfantasy.cpp: class SkillMoonlightFantasy : public StatusSkillImpl (OB_OBOROGENSOU), rAthena
//! e985006.

use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::skill_fail;
use crate::skills::SkillImpl;
use crate::status::defs::MD_STATUSIMMUNE;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMoonlightFantasy;

impl SkillImpl for SkillMoonlightFantasy {
    fn skill_id(&self) -> u16 {
        OB_OBOROGENSOU
    }

    /// SkillMoonlightFantasy::castendNoDamageId: this skill does not work on monsters, and it does not work on status
    /// immune monsters (a player's cast fails); else StatusSkillImpl's start and the damage-less animation.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        if src.kind == BL_PC && (target.kind == BL_MOB || crate::battle::bl::has_mode(c, target, MD_STATUSIMMUNE)) {
            skill_fail(c, src, self.skill_id());
            return;
        }
        super::status_then_damage_animation(c, self.skill_id(), src, target, skill_lv, tick);
    }
}
