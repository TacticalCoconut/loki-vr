//! skills/archer/firingtrap.cpp: class SkillFiringTrap : public SkillImpl (RA_FIRINGTRAP). A trap; the unit is skillunit's.

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};
use crate::status::consts::SC_BURNING;

pub struct SkillFiringTrap;

impl SkillImpl for SkillFiringTrap {
    fn skill_id(&self) -> u16 {
        RA_FIRINGTRAP
    }

    /// SkillFiringTrap::castendPos2: `flag |= 1` (the ammo is deleted with the group), skill_unitsetting.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        super::unit_castend_pos2(c, self.skill_id(), src, x, y, skill_lv, flag);
    }

    /// SkillFiringTrap::applyAdditionalEffects: `sc_start4(src, target, SC_BURNING, 50 + skill_lv * 10, skill_lv, 1000, src->id, 0, skill_get_time2(getSkillId(), skill_lv))`.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start4(c, Some(src), target, SC_BURNING, 50 + skill_lv as i32 * 10, skill_lv as i32, 1000, src.id as i32, 0, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }
}
