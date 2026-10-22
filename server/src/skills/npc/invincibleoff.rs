//! skills/npc/invincibleoff.cpp: class SkillInvincibleOff : public SkillImpl (NPC_INVINCIBLEOFF), rAthena e985006.

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::SC_INVINCIBLE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillInvincibleOff;

impl SkillImpl for SkillInvincibleOff {
    fn skill_id(&self) -> u16 {
        NPC_INVINCIBLEOFF
    }

    /// SkillInvincibleOff::castendNoDamageId: ends the target's SC_INVINCIBLE.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        sc::status_change_end(c, target.id, SC_INVINCIBLE, 0);
    }
}
