//! skills/npc/widepetrify.cpp: class SkillWidePetrify : public SkillImpl (NPC_WIDESTONE).

use super::widebleeding::wide_area;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWidePetrify;

impl SkillImpl for SkillWidePetrify {
    fn skill_id(&self) -> u16 {
        NPC_WIDESTONE
    }

    /// SkillWidePetrify::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        wide_petrify_castend(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }
}

/// SkillWidePetrify::castendNoDamageId (widepetrify2.cpp's is the same, statement for statement): `flag & 1` (a
/// target of the area): `sc_start2(src, target, skill_get_sc(skill), 100, skill_lv, src->id,
/// skill_get_time2(…), skill_get_time(…))`, whose last argument is sc_start2's `delay` (status.hpp sc_start2 →
/// status_change_start(…, 100 * rate, val1, val2, 0, 0, duration, SCSTART_NONE, delay)); else the area
/// (widebleeding.rs wide_area).
pub(super) fn wide_petrify_castend(c: &Ctx, skill_id: u16, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
    if flag & 1 != 0 {
        let t = skill_get_sc(skill_id) as i32;
        crate::status::sc::status_change_start(c, Some(src), target, t, 100 * 100, skill_lv as i32, src.id as i32, 0, 0, skill_get_time2(skill_id, skill_lv) as i64, crate::status::defs::SCSTART_NONE, skill_get_time(skill_id, skill_lv));
    } else {
        wide_area(c, skill_id, src, target, skill_lv, tick, flag);
    }
}
