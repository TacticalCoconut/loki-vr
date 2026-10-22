//! skills/other/imissyou.cpp: class SkillIMissYou : public SkillImpl (WE_CALLPARTNER).

use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillIMissYou;

impl SkillImpl for SkillIMissYou {
    fn skill_id(&self) -> u16 {
        WE_CALLPARTNER
    }

    /// SkillIMissYou::castendPos2: the Callfamily unit (skill_unitsetting keeps the partner in val1; the unit warps it to
    /// its cell when it expires: skill_unit_timer_sub).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
