//! skills/npc/widesouldrain.cpp: class SkillWideSoulDrain : public SkillImpl (NPC_WIDESOULDRAIN).

use super::widebleeding::wide_area;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWideSoulDrain;

impl SkillImpl for SkillWideSoulDrain {
    fn skill_id(&self) -> u16 {
        NPC_WIDESOULDRAIN
    }

    /// SkillWideSoulDrain::castendNoDamageId: `flag & 1` (a target of the area): the target loses
    /// ((skill_lv - 1) % 5 + 1) * 20 % of its current SP (status_percent_damage(src, target, 0, …, false): it may not
    /// kill); else the area (widebleeding.rs wide_area).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        if *flag & 1 != 0 {
            let sp_rate = ((skill_lv as i32 - 1) % 5 + 1) * 20;
            crate::status::hp::status_percent_damage(c, Some(src), target.id, 0, sp_rate, 0, false);
        } else {
            wide_area(c, self.skill_id(), src, target, skill_lv, tick, *flag);
        }
    }
}
