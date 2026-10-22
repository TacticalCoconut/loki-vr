//! skills/npc/rebirth.cpp: class SkillRebirth : public SkillImpl (NPC_REBIRTH).

use crate::db::skills::*;
use crate::map::BL_MOB;
use crate::skills::SkillImpl;
use crate::status::sc::{self, INFINITE_TICK};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRebirth;

impl SkillImpl for SkillRebirth {
    fn skill_id(&self) -> u16 {
        NPC_REBIRTH
    }

    /// SkillRebirth::castendNoDamageId: the skill's status (skill_get_sc) without end, once per monster.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        // mob_data* md = BL_CAST(BL_MOB, src); if (md && md->state.rebirth)
        if src.kind == BL_MOB && crate::mob::get(c, src.id).map(|md| md.rebirth).unwrap_or(false) {
            return; // only works once
        }
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, INFINITE_TICK);
    }
}
