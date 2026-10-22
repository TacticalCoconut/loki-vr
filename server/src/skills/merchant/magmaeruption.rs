//! skills/merchant/magmaeruption.cpp: class SkillMagmaEruption : public WeaponSkillImpl (NC_MAGMA_ERUPTION); class
//! SkillMagmaEruptionDotDamage : public SkillImpl (NC_MAGMA_ERUPTION_DOTDAMAGE).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skill::SD_ANIMATION;
use crate::skills::novice::status_get_amotion;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_BURNING, SC_STUN};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillMagmaEruption::SkillMagmaEruption() : WeaponSkillImpl(NC_MAGMA_ERUPTION).
pub struct SkillMagmaEruption;

impl SkillImpl for SkillMagmaEruption {
    fn skill_id(&self) -> u16 {
        NC_MAGMA_ERUPTION
    }

    /// WeaponSkillImpl::castendDamageId (inherited).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        crate::skills::weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillMagmaEruption::applyAdditionalEffects: stun effect from 'slam' (90%).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_STUN, 90, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillMagmaEruption::calculateSkillRatio: 'Slam' damage, +350% + 50% per level.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 350 + 50 * skill_lv as i32;
    }

    /// SkillMagmaEruption::castendPos2: 1st, the AoE 'slam' (map_foreachinarea, flag | BCT_ENEMY | SD_ANIMATION | 1);
    /// 2nd, the 'eruption' unit placed by a timerskill two attack motions later.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let i = skill_get_splash(id, skill_lv) as i16;
        let f = *flag | BCT_ENEMY | SD_ANIMATION | 1;
        foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, BL_CHAR, Wall::Config, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
        let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        skill_addtimerskill(c, &s, tick + status_get_amotion(c, &s) as Tick * 2, 0, x, y, id, skill_lv, 0, *flag);
    }
}

/// SkillMagmaEruptionDotDamage::SkillMagmaEruptionDotDamage() : SkillImpl(NC_MAGMA_ERUPTION_DOTDAMAGE).
pub struct SkillMagmaEruptionDotDamage;

impl SkillImpl for SkillMagmaEruptionDotDamage {
    fn skill_id(&self) -> u16 {
        NC_MAGMA_ERUPTION_DOTDAMAGE
    }

    /// SkillMagmaEruptionDotDamage::applyAdditionalEffects: burning effect from 'eruption' (10% per level; val2 1000,
    /// val3 the caster).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start4(c, Some(src), target, SC_BURNING, 10 * skill_lv as i32, skill_lv as i32, 1000, src.id as i32, 0, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }
}
