//! skills/merchant/wallofthorns.cpp: class SkillWallOfThorns : public SkillImpl (GN_WALLOFTHORN).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillWallOfThorns::SkillWallOfThorns() : SkillImpl(GN_WALLOFTHORN).
pub struct SkillWallOfThorns;

impl SkillImpl for SkillWallOfThorns {
    fn skill_id(&self) -> u16 {
        GN_WALLOFTHORN
    }

    /// SkillWallOfThorns::calculateSkillRatio: +10% per level.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 10 * skill_lv as i32;
    }

    /// SkillWallOfThorns::castendPos2: the wall ("Ammo should be deleted right away": flag untouched).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        crate::skill::iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
