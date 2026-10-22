//! skills/acolyte/ruwach.cpp: class SkillRuwach : public SkillImpl (AL_RUWACH).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillRuwach;

impl SkillImpl for SkillRuwach {
    fn skill_id(&self) -> u16 {
        AL_RUWACH
    }

    /// SkillRuwach::castendNoDamageId: SC_RUWACH with val2 = the skill id (the status reveals and hits hidden units).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, bl: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let ok = sc::sc_start2(c, Some(src), bl, type_, 100, skill_lv as i32, id as i32, skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
    }

    /// SkillRuwach::calculateSkillRatio: +45%.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 45;
    }
}
