//! skills/mage/firewalk.cpp: class SkillFireWalk : public SkillImpl (SO_FIREWALK).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::SC_HEATER_OPTION;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillFireWalk;

impl SkillImpl for SkillFireWalk {
    fn skill_id(&self) -> u16 {
        SO_FIREWALK
    }

    /// SkillFireWalk::castendPos2: restarts the skill's status (SC_PROPERTYWALK: val1 = the skill, val2 = the
    /// level) on the caster; its units are placed while walking (status2 / skillunit).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, _x: i16, _y: i16, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;

        if sce(c, src, type_).is_some() {
            sc::status_change_end(c, src.id, type_, 0);
        }

        let src = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        sc::sc_start2(c, Some(&src), &src, type_, 100, id as i32, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
    }

    /// SkillFireWalk::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 60 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if sce(c, src, SC_HEATER_OPTION).is_some() {
            // sd ? sd->status.job_level / 2 : 0
            *skillratio += if src.kind == BL_PC { crate::pc::character_of(c, src.id).map_or(0, |ch| ch.job_level as i32 / 2) } else { 0 };
        }
    }
}
