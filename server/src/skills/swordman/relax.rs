//! skills/swordman/relax.cpp: class SkillRelax : public SkillImpl (LK_TENSIONRELAX).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillRelax;

impl SkillImpl for SkillRelax {
    fn skill_id(&self) -> u16 {
        LK_TENSIONRELAX
    }

    /// SkillRelax::castendNoDamageId: val4 is skill_get_time2 (the regen interval), the duration skill_get_time.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let ok = sc::sc_start4(c, Some(src), target, type_, 100, skill_lv as i32, 0, 0, skill_get_time2(id, skill_lv), skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
    }
}
