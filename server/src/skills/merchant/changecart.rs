//! skills/merchant/changecart.cpp: class SkillChangeCart : public SkillImpl (MC_CHANGECART). The client answers with
//! CZ_REQ_CHANGECART (cart.rs parse_change_cart).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillChangeCart::SkillChangeCart() : SkillImpl(MC_CHANGECART).
pub struct SkillChangeCart;

impl SkillImpl for SkillChangeCart {
    fn skill_id(&self) -> u16 {
        MC_CHANGECART
    }

    /// SkillChangeCart::castendNoDamageId: the skill packet only.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
    }
}
