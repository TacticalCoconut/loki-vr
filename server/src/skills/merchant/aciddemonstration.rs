//! skills/merchant/aciddemonstration.cpp: class SkillAcidDemonstration : public WeaponSkillImpl (CR_ACIDDEMONSTRATION).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::inventory::{EQP_ARMOR, EQP_WEAPON};
use crate::map::BL_PC;
use crate::skill::effect::skill_break_equip;
use crate::skills::SkillImpl;
use crate::status::status_get_status_data;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillAcidDemonstration::SkillAcidDemonstration() : WeaponSkillImpl(CR_ACIDDEMONSTRATION).
pub struct SkillAcidDemonstration;

impl SkillImpl for SkillAcidDemonstration {
    fn skill_id(&self) -> u16 {
        CR_ACIDDEMONSTRATION
    }

    /// SkillAcidDemonstration::castendDamageId: RENEWAL calls WeaponSkillImpl::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        crate::skills::weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillAcidDemonstration::calculateSkillRatio (RENEWAL): 200% per level + caster INT + target VIT ("!TODO:
    /// Confirm status bonus"), halved against players.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = status_get_status_data(c, src);
        let tstatus = status_get_status_data(c, target);
        *base_skillratio += -100 + 200 * skill_lv as i32 + sstatus.int_ as i32 + tstatus.vit as i32;
        if target.kind == BL_PC {
            *base_skillratio /= 2;
        }
    }

    /// SkillAcidDemonstration::applyAdditionalEffects: weapon and armor break, 1% per level (rate x100).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        skill_break_equip(c, src, target, (EQP_WEAPON | EQP_ARMOR) as u16, 100 * skill_lv as i32, BCT_ENEMY);
    }
}
