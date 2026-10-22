//! skills/npc/expulsion.cpp: class SkillExpulsion : public SkillImpl (NPC_EXPULSION), rAthena e985006.

use crate::db::skills::*;
use crate::script::buildins::script2::{unitctl::Bl, unitmove};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit, CLR_TELEPORT};
use crate::world::{Ctx, Tick};

pub struct SkillExpulsion;

impl SkillImpl for SkillExpulsion {
    fn skill_id(&self) -> u16 {
        NPC_EXPULSION
    }

    /// SkillExpulsion::castendNoDamageId: the target is warped to a random cell of its map.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        // unit_warp(target, -1, -1, -1, CLR_TELEPORT)
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        unitmove::unit_warp(c, &Bl::Unit(t), -1, -1, -1, CLR_TELEPORT);
    }
}
