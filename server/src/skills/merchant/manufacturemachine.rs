//! skills/merchant/manufacturemachine.cpp: class SkillManufactureMachine : public SkillImpl (MT_M_MACHINE). The list
//! and the production are itemui's (itemui/produce.rs: clif_cooking_list, skill_can_produce_mix, skill_produce_mix).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillManufactureMachine::SkillManufactureMachine() : SkillImpl(MT_M_MACHINE).
pub struct SkillManufactureMachine;

impl SkillImpl for SkillManufactureMachine {
    fn skill_id(&self) -> u16 {
        MT_M_MACHINE
    }

    /// SkillManufactureMachine::castendNoDamageId: a player caster remembers the skill (sd->skill_id_old /
    /// skill_lv_old, read back by skill_produce_mix), gets the production list (trigger 31, qty 1, list type 7),
    /// then the skill packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        // map_session_data* sd = BL_CAST(BL_PC, src)
        if src.kind == BL_PC {
            crate::skill::state::with(c, src.id, |s| {
                s.skill_id_old = id;
                s.skill_lv_old = skill_lv;
            });
            crate::itemui::produce::clif_cooking_list(c, src.id, 31, id, 1, 7);
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        }
    }
}
