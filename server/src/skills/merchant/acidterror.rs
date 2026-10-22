//! skills/merchant/acidterror.cpp: class SkillAcidTerror : public WeaponSkillImpl (AM_ACIDTERROR).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::inventory::EQP_ARMOR;
use crate::map::BL_PC;
use crate::skill::effect::skill_break_equip;
use crate::skills::novice::pc_checkskill;
use crate::skills::SkillImpl;
use crate::status::consts::SC_BLEEDING;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// script_constants ET_HUK (e_emotion_type).
const ET_HUK: u8 = 23;

/// SkillAcidTerror::SkillAcidTerror() : WeaponSkillImpl(AM_ACIDTERROR).
pub struct SkillAcidTerror;

impl SkillImpl for SkillAcidTerror {
    fn skill_id(&self) -> u16 {
        AM_ACIDTERROR
    }

    /// WeaponSkillImpl::castendDamageId (inherited).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        crate::skills::weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillAcidTerror::calculateSkillRatio (RENEWAL): 200% per level, +100% with Learning Potion ("!TODO: What's this
    /// bonus increase?").
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + 200 * skill_lv as i32;
        if src.kind == BL_PC && pc_checkskill(c, src, AM_LEARNINGPOTION) != 0 {
            *base_skillratio += 100;
        }
    }

    /// SkillAcidTerror::applyAdditionalEffects: bleeding (3% per level), then the armor break (RENEWAL rate
    /// `(1000 * skill_lv + 500) - 1000`) with the ET_HUK emotion on the target when it breaks.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        sc::sc_start2(c, Some(src), target, SC_BLEEDING, skill_lv as i32 * 3, skill_lv as i32, src.id as i32, skill_get_time2(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        if skill_break_equip(c, src, &t, EQP_ARMOR as u16, (1000 * skill_lv as i32 + 500) - 1000, BCT_ENEMY) != 0 {
            if let Some(t) = unit::get(c, target.id) {
                crate::clif::emotion(c, &t, ET_HUK);
            }
        }
    }
}

#[cfg(test)]
mod tests {
    #[test]
    fn et_huk_matches_the_script_constant() {
        assert_eq!(crate::data::constant("ET_HUK"), Some(super::ET_HUK as i64));
    }
}
