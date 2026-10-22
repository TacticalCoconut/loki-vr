//! skills/npc/darknessbreath.cpp: class SkillDarknessBreath : public SkillImpl (NPC_DARKNESSBREATH), rAthena e985006.

use super::acidbreath::breath_castend_damage_id;
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDarknessBreath;

impl SkillImpl for SkillDarknessBreath {
    fn skill_id(&self) -> u16 {
        NPC_DARKNESSBREATH
    }

    /// SkillDarknessBreath::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 100 * (skill_lv as i32 - 1);
    }

    /// SkillDarknessBreath::castendDamageId (the breath body, acidbreath.rs).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        breath_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillDarknessBreath::modifyHitRate: `hit_rate *= 2` on the int16.
    fn modify_hit_rate(&self, _c: &Ctx, hit_rate: &mut i16, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        *hit_rate = (*hit_rate as i32 * 2) as i16;
    }
}
