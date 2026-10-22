//! skills/npc/lick.cpp: class SkillLick : public SkillImpl (NPC_LICK), rAthena e985006.

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillLick;

impl SkillImpl for SkillLick {
    fn skill_id(&self) -> u16 {
        NPC_LICK
    }

    /// SkillLick::castendNoDamageId: the target loses 100 SP, then `clif_skill_nodamage(…, sc_start(src, target,
    /// skill_get_sc(id), lv * 20, lv, skill_get_time2(id, lv)))` (the argument first).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        crate::status::hp::status_zap(c, target.id, 0, 100, 0);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        let ok = sc::sc_start(c, Some(src), &t, skill_get_sc(id) as i32, skill_lv as i32 * 20, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or(t);
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
    }
}
