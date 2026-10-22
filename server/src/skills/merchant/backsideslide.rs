//! skills/merchant/backsideslide.cpp: class SkillBackSideSlide : public SkillImpl (NC_B_SIDESLIDE).

use crate::db::skills::*;
use crate::skill::attack::{skill_blown, BLOWN_IGNORE_NO_KNOCKBACK};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillBackSideSlide::SkillBackSideSlide() : SkillImpl(NC_B_SIDESLIDE).
pub struct SkillBackSideSlide;

impl SkillImpl for SkillBackSideSlide {
    fn skill_id(&self) -> u16 {
        NC_B_SIDESLIDE
    }

    /// SkillBackSideSlide::castendNoDamageId: the target slides skill_get_blewcount cells in the caster's facing direction (unit_getdir) (skill_blown ignoring no-knockback), then the skill packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let dir = unit::getdir(src);
        skill_blown(c, src, target, skill_get_blewcount(id, skill_lv), dir as i8, BLOWN_IGNORE_NO_KNOCKBACK);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
    }
}
