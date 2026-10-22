//! skills/swordman/holycross.cpp: class SkillHolyCross : public WeaponSkillImpl (CR_HOLYCROSS).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_BLIND;
use crate::status::defs::W_2HSPEAR;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHolyCross;

impl SkillImpl for SkillHolyCross {
    fn skill_id(&self) -> u16 {
        CR_HOLYCROSS
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillHolyCross::calculateSkillRatio (RENEWAL: doubled with a two-handed spear).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let two_handed_spear = src.kind == BL_PC && crate::pc::character_of(c, src.id).map(|ch| ch.weapon as i32 == W_2HSPEAR).unwrap_or(false);
        if two_handed_spear {
            *base_skillratio += 70 * skill_lv as i32;
        } else {
            *base_skillratio += 35 * skill_lv as i32;
        }
    }

    /// SkillHolyCross::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_BLIND, 3 * skill_lv as i32, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }
}
