//! skills/mage/createelementalconverter.cpp: class SkillCreateElementalConverter : public SkillImpl (SA_CREATECON).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillCreateElementalConverter;

impl SkillImpl for SkillCreateElementalConverter {
    fn skill_id(&self) -> u16 {
        SA_CREATECON
    }

    /// SkillCreateElementalConverter::castendNoDamageId: a player gets the converter list (ZC_MAKINGARROW_LIST;
    /// the choice comes back as CZ_REQ_MAKINGARROW and skill_produce_mix makes it: itemui/produce.rs), then the skill
    /// packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind == BL_PC {
            crate::itemui::produce::clif_elementalconverter_list(c, src.id);
            clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        }
    }
}
