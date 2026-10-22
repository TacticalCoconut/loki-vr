//! skills/mage/coma.cpp: class SkillComa : public SkillImpl (SA_COMA).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillComa;

impl SkillImpl for SkillComa {
    fn skill_id(&self) -> u16 {
        SA_COMA
    }

    /// SkillComa::castendNoDamageId: `clif_skill_nodamage(src, *target, SA_COMA, skill_lv, sc_start(src, target,
    /// type, 100, skill_lv, skill_get_time2(SA_COMA, skill_lv)))` (the status starts first: it is the argument).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let ok = sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
    }
}
