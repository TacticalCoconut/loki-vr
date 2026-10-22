//! skills/merchant/skill_vending.cpp: class SkillVending (MC_VENDING). Its castendNoDamageId is vending.rs'
//! [`crate::vending::skill_vending_castend_nodamage_id`] (the shop preparation with the cart round trip).

use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillVending::SkillVending() : SkillImpl(MC_VENDING).
pub struct SkillVending;

impl SkillImpl for SkillVending {
    fn skill_id(&self) -> u16 {
        crate::db::skills::ids::MC_VENDING
    }

    /// SkillVending::castendNoDamageId: `BL_CAST(BL_PC, src)` — nothing for a source that is not a player.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind != crate::map::BL_PC {
            return;
        }
        crate::vending::skill_vending_castend_nodamage_id(c, src.id, skill_lv);
    }
}
