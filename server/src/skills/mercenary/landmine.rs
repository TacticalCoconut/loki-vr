//! skills/mercenary/mercenary_landmine.cpp: class SkillMercenaryLandMine : public SkillImpl (MA_LANDMINE). The trap
//! itself is skillunit's (skill_unitsetting / skill_unit_onplace_timer's MA_LANDMINE arms).

use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::status::consts::SC_STUN;
use crate::status::defs::SCSTART_NONE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMercenaryLandMine;

impl SkillImpl for SkillMercenaryLandMine {
    fn skill_id(&self) -> u16 {
        MA_LANDMINE
    }

    /// SkillMercenaryLandMine::applyAdditionalEffects: `sc_start(src, target, SC_STUN, 10, skill_lv,
    /// skill_get_time2, 1000)` (status_change_start with the delay).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::status_change_start(c, Some(src), target, SC_STUN, 100 * 10, skill_lv as i32, 0, 0, 0, skill_get_time2(self.skill_id(), skill_lv) as i64, SCSTART_NONE, 1000);
    }

    /// SkillMercenaryLandMine::castendPos2: set flag to 1 to prevent deleting ammo (it will be deleted on
    /// group-delete).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1;
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
