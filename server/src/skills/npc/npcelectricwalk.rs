//! skills/npc/npcelectricwalk.cpp: class SkillNpcElectricWalk : public SkillImpl (NPC_ELECTRICWALK), rAthena e985006.
//! The caster's status (skill_get_sc) leaves the electric cells as it walks (status2 / skillunit agents).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillNpcElectricWalk;

impl SkillImpl for SkillNpcElectricWalk {
    fn skill_id(&self) -> u16 {
        NPC_ELECTRICWALK
    }

    /// SkillNpcElectricWalk::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + 100 * skill_lv as i32;
    }

    /// SkillNpcElectricWalk::castendPos2: the skill's status on the caster, restarted when running.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, _x: i16, _y: i16, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if sce(c, src, type_).is_some() {
            sc::status_change_end(c, src.id, type_, 0);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        sc::sc_start2(c, Some(&s), &s, type_, 100, id as i32, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
    }
}
