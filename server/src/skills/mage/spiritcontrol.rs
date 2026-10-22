//! skills/mage/spiritcontrol.cpp: class SkillSpiritControl : public SkillImpl (SO_EL_CONTROL). The elemental calls
//! go through elem_iface (mercelem agent).

use super::elem_iface;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// elemental.hpp EL_MODE_AGGRESSIVE / EL_MODE_ASSIST / EL_MODE_PASSIVE (the mercelem agent's constants).
pub use crate::elemental::data::{EL_MODE_AGGRESSIVE, EL_MODE_ASSIST, EL_MODE_PASSIVE};

pub struct SkillSpiritControl;

impl SkillImpl for SkillSpiritControl {
    fn skill_id(&self) -> u16 {
        SO_EL_CONTROL
    }

    /// SkillSpiritControl::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind != BL_PC {
            return;
        }
        let Some(ed) = elem_iface::sd_ed(c, src.id) else { return };
        if skill_lv == 4 {
            // At level 4 delete elementals.
            elem_iface::elemental_delete(c, ed);
            return;
        }
        // Select mode bassed on skill level used. (rAthena leaves `mode` uninitialised for other levels; the skill's
        // max level is 4, so they never reach here: the module uses 0.)
        let mode = match skill_lv {
            1 => EL_MODE_PASSIVE, // Standard mode.
            2 => EL_MODE_ASSIST,
            3 => EL_MODE_AGGRESSIVE,
            _ => 0,
        };
        if elem_iface::elemental_change_mode(c, ed, mode) == 0 {
            skill_fail(c, src, self.skill_id());
            return;
        }
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
    }
}
