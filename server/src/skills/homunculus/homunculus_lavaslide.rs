//! skills/homunculus/homunculus_lavaslide.cpp: class SkillLavaSlide : public SkillImpl (MH_LAVA_SLIDE).

use super::unit_castend_pos2;
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillLavaSlide;

impl SkillImpl for SkillLavaSlide {
    fn skill_id(&self) -> u16 {
        MH_LAVA_SLIDE
    }

    /// SkillLavaSlide::castendPos2: the ground unit (skill_unitsetting).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        unit_castend_pos2(c, self.skill_id(), src, x, y, skill_lv, flag);
    }

    /// SkillLavaSlide::calculateSkillRatio: -100 + 50 * lv.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + 50 * skill_lv as i32;
    }
}
