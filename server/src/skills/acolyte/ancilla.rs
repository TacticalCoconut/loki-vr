//! skills/acolyte/ancilla.cpp: class SkillAncilla : public SkillImpl (AB_ANCILLA).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::condition::ITEMID_ANCILLA;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillAncilla;

impl SkillImpl for SkillAncilla {
    fn skill_id(&self) -> u16 {
        AB_ANCILLA
    }

    /// SkillAncilla::castendNoDamageId: the animation, then an Ancilla is made (skill_produce_mix).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind == BL_PC {
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            crate::itemui::produce::skill_produce_mix(c, src.id, id, ITEMID_ANCILLA, 0, 0, 0, 1, -1);
        }
    }
}
