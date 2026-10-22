//! skills/homunculus/homunculus_avoid.cpp: class SkillAvoid : public SkillImpl (HLIF_AVOID).

use super::fresh;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillAvoid;

impl SkillImpl for SkillAvoid {
    fn skill_id(&self) -> u16 {
        HLIF_AVOID
    }

    /// SkillAvoid::castendNoDamageId: the status on the target (the master: unit_skilluse_id2's homunculus
    /// auto-target), then on the homunculus with the skill packet on it.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let dur = skill_get_time(id, skill_lv) as i64;
        // Master
        sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, dur);
        // Homunculus
        let s = fresh(c, src);
        let ok = sc::sc_start(c, Some(&s), &s, type_, 100, skill_lv as i32, dur);
        let s = fresh(c, src);
        clif_skill_nodamage(c, Some(&s), &s, id, skill_lv as i32, ok);
    }
}
