//! skills/taekwon/documentofsunmoonandstar.cpp: class SkillDocumentofSunMoonAndStar : public SkillImpl (SJ_DOCUMENT).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDocumentofSunMoonAndStar;

impl SkillImpl for SkillDocumentofSunMoonAndStar {
    fn skill_id(&self) -> u16 {
        SJ_DOCUMENT
    }

    /// SkillDocumentofSunMoonAndStar::castendNoDamageId: level 1 forgets the Feeling maps, 2 the Hatred targets, 3
    /// both.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind == BL_PC {
            match skill_lv {
                1 => {
                    crate::skilltree::pc_resetfeel(c, src.id);
                }
                2 => {
                    crate::skilltree::pc_resethate(c, src.id);
                }
                3 => {
                    crate::skilltree::pc_resetfeel(c, src.id);
                    crate::skilltree::pc_resethate(c, src.id);
                }
                _ => {}
            }
        }
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
    }
}
