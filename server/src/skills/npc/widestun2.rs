//! skills/npc/widestun2.cpp: class SkillWideStun2 : public SkillImpl (NPC_WIDESTUN2). Its castendNoDamageId is
//! widebleeding.cpp's statement for statement (widebleeding.rs wide_status_castend).

use super::widebleeding::wide_status_castend;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWideStun2;

impl SkillImpl for SkillWideStun2 {
    fn skill_id(&self) -> u16 {
        NPC_WIDESTUN2
    }

    /// SkillWideStun2::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        wide_status_castend(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }
}
