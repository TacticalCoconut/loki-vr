//! skills/mage/sight.cpp: class SkillSight : public SkillImpl (MG_SIGHT).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSight;

impl SkillImpl for SkillSight {
    fn skill_id(&self) -> u16 {
        MG_SIGHT
    }

    /// SkillSight::castendNoDamageId: `clif_skill_nodamage(src, *target, id, lv, sc_start2(src, target, type, 100,
    /// skill_lv, id, skill_get_time(id, skill_lv)))`.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let ok = sc::sc_start2(c, Some(src), target, type_, 100, skill_lv as i32, id as i32, skill_get_time(id, skill_lv) as i64);
        let target = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &target, id, skill_lv as i32, ok);
    }
}
