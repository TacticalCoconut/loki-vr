//! skills/merchant/decoratecart.cpp: class SkillDecorateCart : public SkillImpl (MC_CARTDECORATE). The selection
//! window is cart.rs clif_select_cart (clif.cpp clif_SelectCart); the client answers with CZ_SELECTCART (cart.rs
//! parse_select_cart).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillDecorateCart::SkillDecorateCart() : SkillImpl(MC_CARTDECORATE).
pub struct SkillDecorateCart;

impl SkillImpl for SkillDecorateCart {
    fn skill_id(&self) -> u16 {
        MC_CARTDECORATE
    }

    /// SkillDecorateCart::castendNoDamageId: the skill packet, then the cart selection window for a player caster.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        // map_session_data *sd = BL_CAST(BL_PC, src)
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        if src.kind == BL_PC {
            crate::cart::clif_select_cart(c, src.id);
        }
    }
}
