//! skills/npc/multistageattack.cpp: class SkillMultiStageAttack : public WeaponSkillImpl (NPC_COMBOATTACK), rAthena
//! e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMultiStageAttack;

impl SkillImpl for SkillMultiStageAttack {
    fn skill_id(&self) -> u16 {
        NPC_COMBOATTACK
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillMultiStageAttack::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 25 * skill_lv as i32;
    }
}
