//! skills/gunslinger/fallenangel.cpp: class SkillFallenAngel : public SkillImpl (RL_FALLEN_ANGEL), rAthena e985006.

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::skill_fail;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillFallenAngel;

impl SkillImpl for SkillFallenAngel {
    fn skill_id(&self) -> u16 {
        RL_FALLEN_ANGEL
    }

    /// SkillFallenAngel::castendPos2: the caster jumps to (x, y) (unit_movepos easy 1, path checked; clif_snap) and
    /// gets the skill's status (rAthena passes the skill id as val1); a player's cast fails when it cannot move.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if unit::movepos(c, src.id, x, y, 1, true) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            crate::skill::clif::clif_snap(c, &s, s.x, s.y);
            sc::sc_start(c, Some(&s), &s, type_, 100, id as i32, skill_get_time(id, skill_lv) as i64);
        } else if src.kind == BL_PC {
            skill_fail(c, src, id);
        }
    }
}
