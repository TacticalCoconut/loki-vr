//! skills/npc/siegemode.cpp: class SkillSiegeMode : public SkillImpl (NPC_SIEGEMODE).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSiegeMode;

impl SkillImpl for SkillSiegeMode {
    fn skill_id(&self) -> u16 {
        NPC_SIEGEMODE
    }

    /// SkillSiegeMode::castendNoDamageId: the animation only.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        // Not implemented/used: Gives EFST_SIEGEMODE which reduces speed to 1000.
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
    }
}
