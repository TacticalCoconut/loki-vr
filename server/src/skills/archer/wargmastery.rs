//! skills/archer/wargmastery.cpp: class SkillWargMastery : public SkillImpl (RA_WUGMASTERY).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::defs::OPTION_WUG;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWargMastery;

impl SkillImpl for SkillWargMastery {
    fn skill_id(&self) -> u16 {
        RA_WUGMASTERY
    }

    /// SkillWargMastery::castendNoDamageId: a player calls or sends away its warg (pc_setoption with OPTION_WUG set or
    /// cleared), then the no-damage packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind != crate::map::BL_PC {
            return;
        }
        let option = src.sc.option;
        // pc_iswug(sd): sd->sc.option & OPTION_WUG
        if option & OPTION_WUG == 0 {
            crate::skilltree::pc_setoption(c, src.id, option | OPTION_WUG, 0);
        } else {
            crate::skilltree::pc_setoption(c, src.id, option & !OPTION_WUG, 0);
        }
        clif_skill_nodamage(c, Some(&super::fresh(c, src)), &super::fresh(c, target), self.skill_id(), skill_lv as i32, true);
    }
}
