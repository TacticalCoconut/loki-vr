//! skills/npc/recallslaves.cpp: class SkillRecallSlaves : public SkillImpl (NPC_CALLSLAVE).

use crate::db::skills::*;
use crate::mob::MOB_SLAVEDISTANCE;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRecallSlaves;

impl SkillImpl for SkillRecallSlaves {
    fn skill_id(&self) -> u16 {
        NPC_CALLSLAVE
    }

    /// SkillRecallSlaves::castendNoDamageId: the caster's slaves come next to it (mob_warpslave).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, _skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        crate::mob::spawn::mob_warpslave(c, src, MOB_SLAVEDISTANCE);
    }
}
