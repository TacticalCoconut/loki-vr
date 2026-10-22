//! skills/acolyte/dragoncombo.cpp: class SkillDragonCombo : public WeaponSkillImpl (SR_DRAGONCOMBO).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_STUN;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDragonCombo;

impl SkillImpl for SkillDragonCombo {
    fn skill_id(&self) -> u16 {
        SR_DRAGONCOMBO
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillDragonCombo::applyAdditionalEffects: a (1 + level)% stun.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_STUN, 1 + skill_lv as i32, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }

    /// SkillDragonCombo::calculateSkillRatio: +100% + 80% per level (RE_LVL_DMOD 100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 100 + 80 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
