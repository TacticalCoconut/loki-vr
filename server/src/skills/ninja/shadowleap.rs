//! skills/ninja/shadowleap.cpp: class SkillShadowLeap : public SkillImpl (NJ_SHADOWJUMP), rAthena e985006.

use crate::db::skills::*;
use crate::map::CellChk;
use crate::skill::misc::skill_check_unit_movepos;
use crate::skills::SkillImpl;
use crate::status::consts::SC_HIDING;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillShadowLeap;

impl SkillImpl for SkillShadowLeap {
    fn skill_id(&self) -> u16 {
        NJ_SHADOWJUMP
    }

    /// SkillShadowLeap::castendPos2: the caster jumps to a reachable cell (not on GvG grounds: movepos flag 5), then
    /// Hiding ends (also when it did not move).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, _skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if crate::map::getcell(c, src.map, x, y, CellChk::ChkReach) && skill_check_unit_movepos(c, 5, src, x, y, 1, false) {
            // You don't move on GVG grounds.
            if let Some(s) = crate::unit::get(c, src.id) {
                crate::battle::clif::clif_blown(c, &s);
            }
        }
        sc::status_change_end(c, src.id, SC_HIDING, 0);
    }
}
