//! skills/merchant/mixcooking.cpp: class SkillMixCooking : public SkillImpl (GN_MIX_COOKING).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillMixCooking::SkillMixCooking() : SkillImpl(GN_MIX_COOKING).
pub struct SkillMixCooking;

impl SkillImpl for SkillMixCooking {
    fn skill_id(&self) -> u16 {
        GN_MIX_COOKING
    }

    /// SkillMixCooking::castendNoDamageId: sd->skill_id_old / skill_lv_old, the cooking list (trigger 27, list type 6; 10 at a time from level 2), then the
    /// skill packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind == BL_PC {
            let qty: i32 = if skill_lv > 1 { 10 } else { 1 };
            crate::skill::state::with(c, src.id, |st| {
                st.skill_id_old = id;
                st.skill_lv_old = skill_lv;
            });
            crate::itemui::produce::clif_cooking_list(c, src.id, 27, id, qty, 6);
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        }
    }
}
