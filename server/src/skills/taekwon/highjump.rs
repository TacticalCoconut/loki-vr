//! skills/taekwon/highjump.cpp: class SkillHighJump : public SkillImpl (TK_HIGHJUMP).

use crate::db::skills::*;
use crate::map::{CellChk, BL_MOB, BL_NPC, BL_PC};
use crate::npc::{MF_BATTLEGROUND, MF_NOTELEPORT};
use crate::path::{DIRX, DIRY};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHighJump;

impl SkillImpl for SkillHighJump {
    fn skill_id(&self) -> u16 {
        TK_HIGHJUMP
    }

    /// SkillHighJump::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let dir = crate::unit::getdir(src) as usize & 7;
        let lv = skill_lv as i32;
        let (x, y);
        // Fails on noteleport maps, except for GvG and BG maps [Skotlex]
        if crate::npc::mapflag(c, src.map, MF_NOTELEPORT) && !(crate::npc::mapflag(c, src.map, MF_BATTLEGROUND) || crate::npc::mapflag_gvg(c, src.map)) {
            clif_skill_nodamage(c, Some(src), target, id, lv, true);
            return;
        } else if dir % 2 != 0 {
            // Diagonal
            x = src.x as i32 + DIRX[dir] as i32 * (lv * 4) / 3;
            y = src.y as i32 + DIRY[dir] as i32 * (lv * 4) / 3;
        } else {
            x = src.x as i32 + DIRX[dir] as i32 * lv * 2;
            y = src.y as i32 + DIRY[dir] as i32 * lv * 2;
        }
        let x1 = x + DIRX[dir] as i32;
        let y1 = y + DIRY[dir] as i32;
        clif_skill_nodamage(c, Some(src), target, id, lv, true);
        let (x, y, x1, y1) = (x as i16, y as i16, x1 as i16, y1 as i16);
        let m = src.map;
        let kinds = BL_PC | BL_NPC | BL_MOB;
        if crate::map::count_oncell(c, m, x, y, kinds, 0) == 0
            && crate::map::getcell(c, m, x, y, CellChk::ChkReach)
            && crate::map::count_oncell(c, m, x1, y1, kinds, 0) == 0
            && crate::map::getcell(c, m, x1, y1, CellChk::ChkReach)
            && crate::unit::movepos(c, src.id, x, y, 1, false)
        {
            let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
            crate::battle::clif::clif_blown(c, &s);
        }
    }
}
