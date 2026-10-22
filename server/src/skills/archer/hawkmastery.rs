//! skills/archer/hawkmastery.cpp: class SkillHawkMastery : public SkillImpl (WH_HAWK_M).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::defs::OPTION_FALCON;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHawkMastery;

impl SkillImpl for SkillHawkMastery {
    fn skill_id(&self) -> u16 {
        WH_HAWK_M
    }

    /// SkillHawkMastery::castendNoDamageId: a player calls or releases its falcon (pc_setoption with OPTION_FALCON
    /// set or cleared), then the no-damage packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind != crate::map::BL_PC {
            return;
        }
        let option = src.sc.option;
        // pc_isfalcon(sd): sd->sc.option & OPTION_FALCON
        if option & OPTION_FALCON == 0 {
            crate::skilltree::pc_setoption(c, src.id, option | OPTION_FALCON, 0);
        } else {
            crate::skilltree::pc_setoption(c, src.id, option & !OPTION_FALCON, 0);
        }
        clif_skill_nodamage(c, Some(&super::fresh(c, src)), &super::fresh(c, target), self.skill_id(), skill_lv as i32, true);
    }
}
