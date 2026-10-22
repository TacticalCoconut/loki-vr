//! skills/merchant/advanceprotection.cpp: class SkillAdvanceProtection : public StatusSkillImpl (BO_ADVANCE_PROTECTION).

use crate::db::skills::*;
use crate::inventory::EQP_SHADOW_GEAR;
use crate::map::BL_PC;
use crate::skill::clif::skill_fail;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillAdvanceProtection::SkillAdvanceProtection() : StatusSkillImpl(BO_ADVANCE_PROTECTION).
pub struct SkillAdvanceProtection;

impl SkillImpl for SkillAdvanceProtection {
    fn skill_id(&self) -> u16 {
        BO_ADVANCE_PROTECTION
    }

    /// SkillAdvanceProtection::castendNoDamageId: the target must be a player wearing shadow gear
    /// (`pc_checkequip(dstsd, EQP_SHADOW_GEAR) < 0` fails: clif_skill_fail for a player caster, requirements kept),
    /// then StatusSkillImpl::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if target.kind != BL_PC || crate::pc_equip::pc_checkequip(c, target.id, EQP_SHADOW_GEAR, false) < 0 {
            if src.kind == BL_PC {
                skill_fail(c, src, id);
            }
            // Don't consume item requirements
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        crate::skills::status_castend_nodamage_id(c, id, false, src, target, skill_lv);
    }
}
