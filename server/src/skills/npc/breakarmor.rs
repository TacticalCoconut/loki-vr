//! skills/npc/breakarmor.cpp: class SkillBreakArmor : public WeaponSkillImpl (NPC_ARMORBRAKE), rAthena e985006.

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::inventory::EQP_ARMOR;
use crate::skill::effect::skill_break_equip;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillBreakArmor;

impl SkillImpl for SkillBreakArmor {
    fn skill_id(&self) -> u16 {
        NPC_ARMORBRAKE
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillBreakArmor::applyAdditionalEffects: `skill_break_equip(src, target, EQP_ARMOR, 150*skill_lv, BCT_ENEMY)`.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        skill_break_equip(c, src, target, EQP_ARMOR as u16, 150 * skill_lv as i32, BCT_ENEMY);
    }
}
