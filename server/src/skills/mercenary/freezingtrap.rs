//! skills/mercenary/mercenary_freezingtrap.cpp: class SkillMercenaryFreezingTrap : public SkillImpl (MA_FREEZINGTRAP).
//! The trap itself is skillunit's (skill_unitsetting / skill_unit_onplace_timer's MA_FREEZINGTRAP arms).

use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::status::consts::SC_FREEZE;
use crate::status::defs::SCSTART_NONE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMercenaryFreezingTrap;

impl SkillImpl for SkillMercenaryFreezingTrap {
    fn skill_id(&self) -> u16 {
        MA_FREEZINGTRAP
    }

    /// SkillMercenaryFreezingTrap::applyAdditionalEffects: `sc_start(src, target, SC_FREEZE, 100, skill_lv,
    /// skill_get_time2, sstatus->amotion + 100)` (status_change_start with the delay).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let delay = sstatus.amotion as i32 + 100;
        sc::status_change_start(c, Some(src), target, SC_FREEZE, 100 * 100, skill_lv as i32, 0, 0, 0, skill_get_time2(self.skill_id(), skill_lv) as i64, SCSTART_NONE, delay);
    }

    /// SkillMercenaryFreezingTrap::castendPos2: set flag to 1 to prevent deleting ammo (it will be deleted on
    /// group-delete).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1;
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
