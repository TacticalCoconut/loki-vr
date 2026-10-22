//! skills/mage/safetywall.cpp: class SkillSafetyWall : public SkillImpl (MG_SAFETYWALL).

use crate::db::skills::*;
use crate::map::BL_SKILL;
use crate::skill::area::map_foreachincell;
use crate::skill::{iface, SKILL_NOCONSUME_REQ};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSafetyWall;

impl SkillImpl for SkillSafetyWall {
    fn skill_id(&self) -> u16 {
        MG_SAFETYWALL
    }

    /// SkillSafetyWall::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let mut dummy = 1;
        // map_foreachincell(skill_cell_overlap, src->m, x, y, BL_SKILL, getSkillId(), &dummy, src)
        if map_foreachincell(c, src.map, x, y, BL_SKILL, |c, bl| crate::skillunit::setting::skill_cell_overlap(c, bl, id, &mut dummy, src)) != 0 {
            let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
            iface::skill_unitsetting(c, &s, id, skill_lv, x, y, 0);
            // Don't consume gems if cast on Land Protector
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        //Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        *flag |= 1;
        iface::skill_unitsetting(c, src, id, skill_lv, x, y, 0);
    }
}
