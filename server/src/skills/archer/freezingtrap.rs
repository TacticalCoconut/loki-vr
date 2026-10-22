//! skills/archer/freezingtrap.cpp: class SkillFreezingTrap : public SkillImpl (HT_FREEZINGTRAP). A trap; the unit is skillunit's.

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};
use crate::status::consts::SC_FREEZE;
use crate::status::defs::SCSTART_NONE;

pub struct SkillFreezingTrap;

impl SkillImpl for SkillFreezingTrap {
    fn skill_id(&self) -> u16 {
        HT_FREEZINGTRAP
    }

    /// SkillFreezingTrap::castendPos2: `flag |= 1` (the ammo is deleted with the group), skill_unitsetting.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        super::unit_castend_pos2(c, self.skill_id(), src, x, y, skill_lv, flag);
    }

    /// SkillFreezingTrap::applyAdditionalEffects: `sc_start(src, target, SC_FREEZE, 100, skill_lv, skill_get_time2(getSkillId(), skill_lv), sstatus->amotion + 100)`.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let sstatus = crate::status::status_get_status_data(c, src);
        sc::status_change_start(c, Some(src), target, SC_FREEZE, 100 * 100, skill_lv as i32, 0, 0, 0, skill_get_time2(self.skill_id(), skill_lv) as i64, SCSTART_NONE, sstatus.amotion as i32 + 100);
    }
}
