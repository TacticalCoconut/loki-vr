//! skills/npc/widepetrify2.cpp: class SkillWidePetrify2 : public SkillImpl (NPC_WIDESTONE2). Its castendNoDamageId is
//! widepetrify.cpp's statement for statement (widepetrify.rs wide_petrify_castend).

use super::widepetrify::wide_petrify_castend;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWidePetrify2;

impl SkillImpl for SkillWidePetrify2 {
    fn skill_id(&self) -> u16 {
        NPC_WIDESTONE2
    }

    /// SkillWidePetrify2::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        wide_petrify_castend(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }
}
