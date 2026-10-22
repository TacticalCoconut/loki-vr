//! skills/npc/spiritdestruction.cpp: class SkillSpiritDestruction : public WeaponSkillImpl (NPC_MENTALBREAKER).

use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSpiritDestruction;

impl SkillImpl for SkillSpiritDestruction {
    fn skill_id(&self) -> u16 {
        NPC_MENTALBREAKER
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillSpiritDestruction::applyAdditionalEffects: SP damage of 12% / 16% / 25% / 50% / 100% of the target's MaxSP
    /// (status_percent_damage(src, target, 0, -rate, false); the rates are status_percent_change's int8).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        //SP Damage 12%/16%/25%/50%/100% of MaxSP
        let rate: i32 = match skill_lv {
            1 => 12,
            2 => 16,
            3 => 25,
            4 => 50,
            5 => 100,
            // For easy customization
            _ => skill_lv as i32,
        };
        crate::status::hp::status_percent_damage(c, Some(src), target.id, 0, -rate, 0, false);
    }

    /// SkillSpiritDestruction::modifyHitRate: +20%.
    fn modify_hit_rate(&self, _c: &Ctx, hit_rate: &mut i16, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        let h = *hit_rate as i32;
        *hit_rate = (h + h * 20 / 100) as i16;
    }
}
