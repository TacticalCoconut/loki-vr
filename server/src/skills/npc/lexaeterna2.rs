//! skills/npc/lexaeterna2.cpp: class SkillLexAeterna2 : public SkillImpl (NPC_LEX_AETERNA), rAthena e985006.

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillLexAeterna2;

impl SkillImpl for SkillLexAeterna2 {
    fn skill_id(&self) -> u16 {
        NPC_LEX_AETERNA
    }

    /// SkillLexAeterna2::castendPos2: level 1 PR_LEXAETERNA on every enemy of the area.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let i = skill_get_splash(self.skill_id(), skill_lv) as i16;
        let f = *flag | BCT_ENEMY | 1;
        foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, BL_CHAR, Wall::None, |c, bl| {
            area::skill_area_sub(c, bl, src, PR_LEXAETERNA, 1, tick, f, skill_castend_nodamage_id)
        });
    }
}
