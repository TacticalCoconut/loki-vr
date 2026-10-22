//! skills/archer/detonator.cpp: class SkillDetonator : public SkillImpl (RA_DETONATOR).

use crate::battle::DMG_SINGLE;
use crate::db::skills::*;
use crate::skill::area::{foreach_in_area, Wall};
use crate::skill::clif::{clif_skill_damage, DMGVAL_IGNORE};
use crate::skills::novice::status_get_amotion;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDetonator;

impl SkillImpl for SkillDetonator {
    fn skill_id(&self) -> u16 {
        RA_DETONATOR
    }

    /// SkillDetonator::castendPos2: the caster's traps in the area go off (skill_detonator, skillunit's port), then
    /// the caster's damage packet (DMGVAL_IGNORE).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let i = skill_get_splash(id, skill_lv) as i16;
        // map_foreachinallarea(skill_detonator, src->m, x-i, y-i, x+i, y+i, BL_SKILL, src): skill_detonator also reads
        // an `int32 flag` argument this call does not pass (used only for Kunai Distortion units, which are no
        // Ranger's); 0 here.
        foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, crate::map::BL_SKILL, Wall::None, |c, bl| crate::skillunit::ontimer::skill_detonator(c, bl, src, 0));
        let s = super::fresh(c, src);
        clif_skill_damage(c, &s, &s, tick, status_get_amotion(c, &s), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
    }
}
