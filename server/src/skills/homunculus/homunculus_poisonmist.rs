//! skills/homunculus/homunculus_poisonmist.cpp: class SkillPoisonMist : public SkillImpl (MH_POISON_MIST).

use super::unit_castend_pos2;
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillPoisonMist;

impl SkillImpl for SkillPoisonMist {
    fn skill_id(&self) -> u16 {
        MH_POISON_MIST
    }

    /// SkillPoisonMist::castendPos2: the ground unit (skill_unitsetting).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        unit_castend_pos2(c, self.skill_id(), src, x, y, skill_lv, flag);
    }

    /// SkillPoisonMist::calculateSkillRatio: -100 + 200 * lv * base level / 100 + DEX (!TODO upstream: confirm the
    /// DEX bonus).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 200 * skill_lv as i32 * crate::status::status_get_lv(c, src) / 100 + sstatus.dex as i32;
    }
}
