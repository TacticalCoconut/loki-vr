//! skills/gunslinger/bindtrap.cpp: class SkillBindTrap : public SkillImpl (RL_B_TRAP), rAthena e985006.

use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skill::iface::skill_unitsetting;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillBindTrap;

impl SkillImpl for SkillBindTrap {
    fn skill_id(&self) -> u16 {
        RL_B_TRAP
    }

    /// SkillBindTrap::castendDamageId: a hit of the skill's own type (the trap's attack).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
    }

    /// SkillBindTrap::castendPos2: flag 1 keeps the ammo (it is deleted when the group is).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
