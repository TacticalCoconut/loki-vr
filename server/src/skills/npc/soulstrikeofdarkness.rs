//! skills/npc/soulstrikeofdarkness.cpp: class SkillSoulStrikeOfDarkness : public SkillImpl (NPC_DARKSTRIKE).

use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSoulStrikeOfDarkness;

impl SkillImpl for SkillSoulStrikeOfDarkness {
    fn skill_id(&self) -> u16 {
        NPC_DARKSTRIKE
    }

    /// SkillSoulStrikeOfDarkness::castendDamageId: a magic attack.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }
}
