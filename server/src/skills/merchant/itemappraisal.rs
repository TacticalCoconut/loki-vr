//! skills/merchant/itemappraisal.cpp: class SkillItemAppraisal : public SkillImpl (MC_IDENTIFY). The list and the
//! identification are itemui's (itemui/identify.rs: clif_item_identify_list, clif_parse_ItemIdentify, skill_identify).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillItemAppraisal::SkillItemAppraisal() : SkillImpl(MC_IDENTIFY).
pub struct SkillItemAppraisal;

impl SkillImpl for SkillItemAppraisal {
    fn skill_id(&self) -> u16 {
        MC_IDENTIFY
    }

    /// SkillItemAppraisal::castendNoDamageId: a player caster gets the list of its unidentified items; when the list
    /// was not opened (nothing to identify: clif_item_identify_list sends nothing and leaves sd->menuskill_id as it
    /// was) the requirements are not consumed (flag |= SKILL_NOCONSUME_REQ, back to skill_castend_nodamage_id).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, _skill_lv: u16, _tick: Tick, flag: &mut i32) {
        // map_session_data *sd = BL_CAST(BL_PC, src)
        if src.kind == BL_PC {
            crate::itemui::identify::clif_item_identify_list(c, src.id);
            if crate::itemui::menu(c, src.id).0 != self.skill_id() as i32 {
                // failed, dont consume anything
                *flag |= SKILL_NOCONSUME_REQ;
            }
        }
    }
}
