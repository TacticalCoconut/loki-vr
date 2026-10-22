//! skills/npc/stop.cpp: class SkillStop : public SkillImpl (NPC_STOP).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillStop;

impl SkillImpl for SkillStop {
    fn skill_id(&self) -> u16 {
        NPC_STOP
    }

    /// SkillStop::castendNoDamageId: the skill's status (skill_get_sc) on the target (val2 = the caster's id), the
    /// animation with the result; when it started, the same status on the caster (val2 = the target's id).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let ok = sc::sc_start2(c, Some(src), target, type_, 100, skill_lv as i32, src.id as i32, skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        if clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            sc::sc_start2(c, Some(&s), &s, type_, 100, skill_lv as i32, target.id as i32, skill_get_time(id, skill_lv) as i64);
        }
    }
}
