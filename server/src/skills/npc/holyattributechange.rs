//! skills/npc/holyattributechange.cpp: class SkillHolyAttributeChange : public SkillImpl (NPC_CHANGEHOLY), rAthena e985006.

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillHolyAttributeChange;

impl SkillImpl for SkillHolyAttributeChange {
    fn skill_id(&self) -> u16 {
        NPC_CHANGEHOLY
    }

    /// SkillHolyAttributeChange::castendNoDamageId: `clif_skill_nodamage(src, *target, id, lv, sc_start2(src, target, skill_get_sc(id),
    /// 100, lv, skill_get_ele(id, lv), skill_get_time(id, lv)))` (the argument first).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let ok = sc::sc_start2(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_ele(id, skill_lv), skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
    }
}
