//! skills/thief/remover.cpp: class SkillRemover : public SkillImpl (RG_CLEANER).

use crate::db::skills::*;
use crate::map::BL_SKILL;
use crate::skill::area::{foreach_in_area, Wall};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRemover;

impl SkillImpl for SkillRemover {
    fn skill_id(&self) -> u16 {
        RG_CLEANER
    }

    /// SkillRemover::castendNoDamageId: the animation only.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
    }

    /// SkillRemover::castendPos2: skill_graffitiremover on every skill unit of the splash area
    /// (map_foreachinallarea).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let i = skill_get_splash(self.skill_id(), skill_lv) as i16;
        foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, BL_SKILL, Wall::None, |c, bl| crate::skillunit::ontimer::skill_graffitiremover(c, bl, 1));
    }
}
