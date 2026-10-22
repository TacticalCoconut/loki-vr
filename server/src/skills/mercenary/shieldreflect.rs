//! skills/mercenary/mercenary_shieldreflect.cpp: class SkillMercenaryShieldReflect : public StatusSkillImpl
//! (MS_REFLECTSHIELD).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_fail, fail::USESKILL_FAIL};
use crate::skills::novice::sce;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::status::consts::SC_DARKCROW;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMercenaryShieldReflect;

impl SkillImpl for SkillMercenaryShieldReflect {
    fn skill_id(&self) -> u16 {
        MS_REFLECTSHIELD
    }

    /// SkillMercenaryShieldReflect::castendNoDamageId: SC_DARKCROW prevents using reflecting skills; else
    /// StatusSkillImpl::castendNoDamageId (end_if_running = false).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if sce(c, target, SC_DARKCROW).is_some() {
            // map_session_data* sd = BL_CAST(BL_PC, src)
            if src.kind == BL_PC {
                clif_skill_fail(c, src, id, USESKILL_FAIL, 0, 0);
            }
            return;
        }
        status_castend_nodamage_id(c, id, false, src, target, skill_lv);
    }
}
