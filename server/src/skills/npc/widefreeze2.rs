//! skills/npc/widefreeze2.cpp: class SkillWideFreeze2 : public SkillImpl (NPC_WIDEFREEZE2). Its castendNoDamageId is
//! widebleeding.cpp's statement for statement (widebleeding.rs wide_status_castend).

use super::widebleeding::wide_status_castend;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWideFreeze2;

impl SkillImpl for SkillWideFreeze2 {
    fn skill_id(&self) -> u16 {
        NPC_WIDEFREEZE2
    }

    /// SkillWideFreeze2::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        wide_status_castend(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }
}
