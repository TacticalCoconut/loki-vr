//! skills/npc/energydrain.cpp: class SkillEnergyDrain : public SkillImpl (NPC_ENERGYDRAIN), rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillEnergyDrain;

impl SkillImpl for SkillEnergyDrain {
    fn skill_id(&self) -> u16 {
        NPC_ENERGYDRAIN
    }

    /// SkillEnergyDrain::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 100 * skill_lv as i32;
    }

    /// SkillEnergyDrain::castendDamageId: the caster heals the damage it dealt.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let heal = skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag) as i32;
        if heal > 0 {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            clif_skill_nodamage(c, None, &s, AL_HEAL, heal, true);
            // status_heal(src, heal, 0, 0): hap 0, flag 0
            crate::status::hp::status_heal(c, src.id, heal as i64, 0, 0, 0);
        }
    }
}
