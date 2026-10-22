//! skills/thief/closeconfine.cpp: class SkillCloseConfine : public SkillImpl (RG_CLOSECONFINE).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillCloseConfine;

impl SkillImpl for SkillCloseConfine {
    fn skill_id(&self) -> u16 {
        RG_CLOSECONFINE
    }

    /// SkillCloseConfine::castendNoDamageId: SC_CLOSECONFINE2 on the target (val2 = the caster), then the animation
    /// with its result.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let ok = sc::sc_start4(c, Some(src), target, type_, 100, skill_lv as i32, src.id as i32, 0, 0, skill_get_time(id, skill_lv) as i64);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, ok);
    }
}
