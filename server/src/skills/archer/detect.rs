//! skills/archer/detect.cpp: class SkillDetect : public SkillImpl (HT_DETECTING).

use crate::db::skills::*;
use crate::skill::area::{foreach_in_area, Wall};
use crate::skills::SkillImpl;
use crate::status::consts::SC_SIGHT;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDetect;

impl SkillImpl for SkillDetect {
    fn skill_id(&self) -> u16 {
        HT_DETECTING
    }

    /// SkillDetect::castendPos2: every character in the area gets status_change_timer_sub(SC_SIGHT) (the hidden ones
    /// are revealed), then the hidden traps there are revealed (skill_reveal_trap_inarea).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        let i = skill_get_splash(self.skill_id(), skill_lv) as i16;
        // map_foreachinallarea(status_change_timer_sub, src->m, x-i, y-i, x+i, y+i, BL_CHAR, src, nullptr, SC_SIGHT, tick)
        foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, crate::map::BL_CHAR, Wall::None, |c, bl| {
            crate::status::sc_clear::status_change_timer_sub(c, bl, src, None, SC_SIGHT, tick)
        });
        let s = super::fresh(c, src);
        crate::skillunit::ontimer::skill_reveal_trap_inarea(c, &s, i, x, y);
    }
}
