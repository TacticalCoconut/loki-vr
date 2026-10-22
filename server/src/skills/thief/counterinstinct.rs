//! skills/thief/counterinstinct.cpp: class SkillCounterInstinct : public StatusSkillImpl (ST_REJECTSWORD).

use crate::db::skills::*;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::status::consts::SC_AUTOCOUNTER;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillCounterInstinct;

impl SkillImpl for SkillCounterInstinct {
    fn skill_id(&self) -> u16 {
        ST_REJECTSWORD
    }

    /// StatusSkillImpl::castendNoDamageId (not overridden).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        status_castend_nodamage_id(c, self.skill_id(), false, src, target, skill_lv);
    }

    /// SkillCounterInstinct::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_AUTOCOUNTER, skill_lv as i32 * 15, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }
}
