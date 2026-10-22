//! skills/npc/caneofevileye.cpp: class SkillCaneOfEvilEye : public SkillImpl (NPC_CANE_OF_EVIL_EYE), rAthena
//! e985006. Its effect is its skill unit (skill_unitsetting: skillunit agent).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_poseffect;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillCaneOfEvilEye;

impl SkillImpl for SkillCaneOfEvilEye {
    fn skill_id(&self) -> u16 {
        NPC_CANE_OF_EVIL_EYE
    }

    /// SkillCaneOfEvilEye::castendPos2: the ground effect when the unit group was placed.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        *flag |= 1;
        if iface::skill_unitsetting(c, src, id, skill_lv, x, y, 0) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            clif_skill_poseffect(c, &s, id, skill_lv, x, y, tick);
        }
    }
}
