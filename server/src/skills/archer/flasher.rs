//! skills/archer/flasher.cpp: class SkillFlasher : public SkillImpl (HT_FLASHER). A trap; the unit is skillunit's.

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};
use crate::status::consts::SC_BLIND;
use crate::status::defs::SCSTART_NONE;

pub struct SkillFlasher;

impl SkillImpl for SkillFlasher {
    fn skill_id(&self) -> u16 {
        HT_FLASHER
    }

    /// SkillFlasher::castendPos2: `flag |= 1` (the ammo is deleted with the group), skill_unitsetting.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        super::unit_castend_pos2(c, self.skill_id(), src, x, y, skill_lv, flag);
    }

    /// SkillFlasher::applyAdditionalEffects: `sc_start(src, target, SC_BLIND, 100, skill_lv, skill_get_time2(getSkillId(), skill_lv), 1000)`.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::status_change_start(c, Some(src), target, SC_BLIND, 100 * 100, skill_lv as i32, 0, 0, 0, skill_get_time2(self.skill_id(), skill_lv) as i64, SCSTART_NONE, 1000);
    }
}
