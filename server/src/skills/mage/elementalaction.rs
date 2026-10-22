//! skills/mage/elementalaction.cpp: class SkillElementalAction : public SkillImpl (SO_EL_ACTION).
//!
//! The elemental itself (sd->ed, elemental_action) is the mercelem agent's: see elem_iface.rs.

use super::elem_iface::{ed_class, elemental_action, sd_ed};
use crate::db::misc::elemental::*;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::cooldown::skill_blockpc_start;
use crate::skill::state;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillElementalAction;

impl SkillImpl for SkillElementalAction {
    fn skill_id(&self) -> u16 {
        SO_EL_ACTION
    }

    /// SkillElementalAction::castendNoDamageId: a player's elemental acts on the target (nothing without one), then
    /// the packet and the skill's own block (3 s; 6 s for a medium, 9 s for a large spirit).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        if src.kind != BL_PC {
            return;
        }
        let id = self.skill_id();
        let mut duration: i32 = 3000;
        // if( !sd->ed ) return;
        let Some(ed) = sd_ed(c, src.id) else { return };
        match ed_class(c, ed) {
            ELEMENTALID_AGNI_M | ELEMENTALID_AQUA_M | ELEMENTALID_VENTUS_M | ELEMENTALID_TERA_M => duration = 6000,
            ELEMENTALID_AGNI_L | ELEMENTALID_AQUA_L | ELEMENTALID_VENTUS_L | ELEMENTALID_TERA_L => duration = 9000,
            _ => {}
        }
        state::with(c, src.id, |s| s.skill_id_old = id);
        elemental_action(c, ed, target, tick);
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        skill_blockpc_start(c, src.id, id, duration as Tick);
    }
}
