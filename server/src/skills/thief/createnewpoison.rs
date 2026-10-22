//! skills/thief/createnewpoison.cpp: class SkillCreateNewPoison : public SkillImpl (GC_CREATENEWPOISON).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillCreateNewPoison;

impl SkillImpl for SkillCreateNewPoison {
    fn skill_id(&self) -> u16 {
        GC_CREATENEWPOISON
    }

    /// SkillCreateNewPoison::castendNoDamageId: the production list (trigger 25), then the animation.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind != BL_PC {
            return;
        }
        let id = self.skill_id();
        crate::itemui::produce::clif_skill_produce_mix_list(c, src.id, id as i32, 25);
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
    }
}
