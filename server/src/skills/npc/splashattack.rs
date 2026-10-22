//! skills/npc/splashattack.cpp: class SkillSplashAttack : public SkillImplRecursiveDamageSplash (NPC_SPLASHATTACK).

use crate::db::skills::*;
use crate::skill::SD_PREAMBLE;
use crate::skills::{splash_castend_damage_id, splash_castend_pos2, RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSplashAttack;

impl SkillImpl for SkillSplashAttack {
    fn skill_id(&self) -> u16 {
        NPC_SPLASHATTACK
    }

    /// SkillSplashAttack::castendDamageId: SD_PREAMBLE, then SkillImplRecursiveDamageSplash::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        *flag |= SD_PREAMBLE; // a fake packet will be sent for the first target to be hit
        splash_castend_damage_id(self, c, src, target, skill_lv, tick, flag);
    }

    /// SkillImplRecursiveDamageSplash::castendPos2 (not overridden).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        splash_castend_pos2(self, c, src, x, y, skill_lv, tick, flag);
    }
}

impl RecursiveDamageSplash for SkillSplashAttack {}
