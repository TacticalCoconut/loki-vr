//! skills/summoner/lope.cpp: class SkillLope : public SkillImpl (SU_LOPE).

use crate::db::skills::*;
use crate::map::{CellChk, BL_MOB, BL_NPC, BL_PC};
use crate::npc::{mapflag, MF_BATTLEGROUND, MF_NOTELEPORT};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillLope;

impl SkillImpl for SkillLope {
    fn skill_id(&self) -> u16 {
        SU_LOPE
    }

    /// SkillLope::castendPos2: a jump to the cell (the caster's own cell on a noteleport map, except on GvG and
    /// battleground maps) when no player, NPC or monster stands there and the cell is reachable
    /// (unit_movepos(src, x, y, 1, 0), then clif_blown).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let (mut x, mut y) = (x, y);
        // Fails on noteleport maps, except for GvG and BG maps
        if mapflag(c, src.map, MF_NOTELEPORT) && !(mapflag(c, src.map, MF_BATTLEGROUND) || crate::status::map_flag_gvg2(c, src.map)) {
            x = src.x;
            y = src.y;
        }
        clif_skill_nodamage(c, Some(src), src, self.skill_id(), skill_lv as i32, true);
        if crate::map::count_oncell(c, src.map, x, y, BL_PC | BL_NPC | BL_MOB, 0) == 0
            && crate::map::getcell(c, src.map, x, y, CellChk::ChkReach)
            && unit::movepos(c, src.id, x, y, 1, false)
        {
            if let Some(s) = unit::get(c, src.id) {
                crate::battle::clif::clif_blown(c, &s);
            }
        }
    }
}
