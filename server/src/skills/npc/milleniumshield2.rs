//! skills/npc/milleniumshield2.cpp: class SkillMilleniumShield2 : public SkillImpl (NPC_MILLENNIUMSHIELD), rAthena
//! e985006.

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMilleniumShield2;

impl SkillImpl for SkillMilleniumShield2 {
    fn skill_id(&self) -> u16 {
        NPC_MILLENNIUMSHIELD
    }

    /// SkillMilleniumShield2::castendNoDamageId: the skill packet only when the status started.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64) {
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
        }
    }
}
