//! skills/mercenary/mercenary_sight.cpp: class SkillMercenarySight : public SkillImpl (MER_SIGHT).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMercenarySight;

impl SkillImpl for SkillMercenarySight {
    fn skill_id(&self) -> u16 {
        MER_SIGHT
    }

    /// SkillMercenarySight::castendNoDamageId: sc_start2(…, skill_lv, MER_SIGHT, skill_get_time).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let ok = sc::sc_start2(c, Some(src), target, type_, 100, skill_lv as i32, id as i32, skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
    }
}
