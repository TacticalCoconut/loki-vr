//! skills/mage/ganbantein.cpp: class SkillGanbantein : public SkillImpl (HW_GANBANTEIN).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::area::Wall;
use crate::skill::clif::{clif_skill_poseffect, skill_fail};
use crate::skill::{iface, SKILL_NOCONSUME_REQ};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGanbantein;

impl SkillImpl for SkillGanbantein {
    fn skill_id(&self) -> u16 {
        HW_GANBANTEIN
    }

    /// SkillGanbantein::castendPos2: 80% to remove the skill units of the area (skill_cell_overlap's HW_GANBANTEIN
    /// case); otherwise "skill failed" and the gems are kept.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // map_session_data* sd = BL_CAST( BL_PC, src )
        if c.rnd() % 100 < 80 {
            clif_skill_poseffect(c, src, id, skill_lv, x, y, tick);
            // `bool i = skill_get_splash(...)`: rAthena stores the splash in a bool, so the area is 1 cell around
            // (x, y) whenever the splash is not 0 (kept as is)
            let i = (skill_get_splash(id, skill_lv) != 0) as i16;
            // map_foreachinallarea(skill_cell_overlap, src->m, x-i, y-i, x+i, y+i, BL_SKILL, getSkillId(), &dummy, src)
            iface::skill_cell_overlap_area(c, src, src.map, x - i, y - i, x + i, y + i, id, Wall::None);
        } else {
            if src.kind == BL_PC {
                skill_fail(c, src, id);
            }
            *flag |= SKILL_NOCONSUME_REQ;
        }
    }
}
