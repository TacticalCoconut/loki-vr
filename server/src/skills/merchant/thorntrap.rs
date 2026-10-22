//! skills/merchant/thorntrap.cpp: class SkillThornTrap : public SkillImpl (GN_THORNS_TRAP).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillThornTrap::SkillThornTrap() : SkillImpl(GN_THORNS_TRAP).
pub struct SkillThornTrap;

impl SkillImpl for SkillThornTrap {
    fn skill_id(&self) -> u16 {
        GN_THORNS_TRAP
    }

    /// SkillThornTrap::castendDamageId: skill_attack of the skill's own type.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        crate::skill::attack::skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
    }

    /// SkillThornTrap::castendPos2: the trap unit; flag |= 1 so the ammo is not consumed now (deleted with the group).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; //Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        crate::skill::iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
