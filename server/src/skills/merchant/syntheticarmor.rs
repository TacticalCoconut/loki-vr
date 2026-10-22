//! skills/merchant/syntheticarmor.cpp: class SkillSyntheticArmor : public SkillImpl (AM_CP_ARMOR).

use crate::db::skills::*;
use crate::inventory::EQP_ARMOR;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillSyntheticArmor::SkillSyntheticArmor() : SkillImpl(AM_CP_ARMOR).
pub struct SkillSyntheticArmor;

impl SkillImpl for SkillSyntheticArmor {
    fn skill_id(&self) -> u16 {
        AM_CP_ARMOR
    }

    /// SkillSyntheticArmor::castendNoDamageId: a player's target must be a player wearing the protected equipment
    /// (`pc_checkequip(dstsd, EQP_ARMOR) < 0` fails; the requirements are not consumed), then the chemical protection
    /// status for skill_get_time.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // if( sd && ( target->type != BL_PC || ( dstsd && pc_checkequip(dstsd,EQP_ARMOR) < 0 ) ) )
        if src.kind == BL_PC && (target.kind != BL_PC || crate::pc_equip::pc_checkequip(c, target.id, EQP_ARMOR, false) < 0) {
            skill_fail(c, src, id);
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        let ok = sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
    }
}
