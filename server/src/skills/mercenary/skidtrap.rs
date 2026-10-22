//! skills/mercenary/mercenary_skidtrap.cpp: class SkillMercenarySkidTrap : public SkillImpl (MA_SKIDTRAP). The trap
//! itself is skillunit's (skill_unitsetting / skill_unit_onplace_timer's MA_SKIDTRAP arms).

use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMercenarySkidTrap;

impl SkillImpl for SkillMercenarySkidTrap {
    fn skill_id(&self) -> u16 {
        MA_SKIDTRAP
    }

    /// SkillMercenarySkidTrap::castendPos2: set flag to 1 to prevent deleting ammo (it will be deleted on
    /// group-delete).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1;
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
