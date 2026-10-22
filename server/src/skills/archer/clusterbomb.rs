//! skills/archer/clusterbomb.cpp: class SkillClusterBomb : public SkillImpl (RA_CLUSTERBOMB). A trap; the unit is
//! skillunit's.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillClusterBomb;

impl SkillImpl for SkillClusterBomb {
    fn skill_id(&self) -> u16 {
        RA_CLUSTERBOMB
    }

    /// SkillClusterBomb::calculateSkillRatio: `base_skillratio += 100 + 100 * skill_lv`.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 100 + 100 * skill_lv as i32;
    }

    /// SkillClusterBomb::castendPos2: `flag |= 1` (the ammo is deleted with the group), skill_unitsetting.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        super::unit_castend_pos2(c, self.skill_id(), src, x, y, skill_lv, flag);
    }
}
