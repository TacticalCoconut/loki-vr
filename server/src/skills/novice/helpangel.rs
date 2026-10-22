//! skills/novice/helpangel.cpp: class SkillHelpAngel : public StatusSkillImpl (NV_HELPANGEL).

use super::party_foreachsamemap_area_sub;
use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHelpAngel;

impl SkillImpl for SkillHelpAngel {
    fn skill_id(&self) -> u16 {
        NV_HELPANGEL
    }

    /// SkillHelpAngel::castendNoDamageId: the status on the caster alone (no party, or a party member's recursive
    /// call), else on every party member within the splash range.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // map_session_data* sd = BL_CAST(BL_PC, src)
        let sd = if src.kind == BL_PC { crate::pc::character_of(c, src.id) } else { None };
        match sd {
            Some(ch) if ch.party_id != 0 && *flag & 1 == 0 => {
                let f = *flag | BCT_PARTY | 1;
                party_foreachsamemap_area_sub(c, src, skill_get_splash(id, skill_lv) as i16, src, id, skill_lv, tick, f, skill_castend_nodamage_id);
            }
            // StatusSkillImpl::castendNoDamageId (end_if_running = false)
            _ => status_castend_nodamage_id(c, id, false, src, target, skill_lv),
        }
    }
}
