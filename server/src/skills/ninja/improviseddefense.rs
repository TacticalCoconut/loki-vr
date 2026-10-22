//! skills/ninja/improviseddefense.cpp: class SkillImprovisedDefense : public SkillImpl (NJ_TATAMIGAESHI), rAthena
//! e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface::skill_unitsetting;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillImprovisedDefense;

impl SkillImpl for SkillImprovisedDefense {
    fn skill_id(&self) -> u16 {
        NJ_TATAMIGAESHI
    }

    /// SkillImprovisedDefense::calculateSkillRatio: + 10% per level, doubled (RENEWAL).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 10 * skill_lv as i32;
        *base_skillratio *= 2;
    }

    /// SkillImprovisedDefense::castendPos2: the units on the caster's cell, and the skill's status on the caster when
    /// the group was made.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, _x: i16, _y: i16, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if skill_unitsetting(c, src, id, skill_lv, src.x, src.y, 0) {
            let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
            sc::sc_start(c, Some(&s), &s, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
        }
    }
}
