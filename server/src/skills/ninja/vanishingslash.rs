//! skills/ninja/vanishingslash.cpp: class SkillVanishingSlash : public WeaponSkillImpl (NJ_KASUMIKIRI), rAthena
//! e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillVanishingSlash;

impl SkillImpl for SkillVanishingSlash {
    fn skill_id(&self) -> u16 {
        NJ_KASUMIKIRI
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillVanishingSlash::applyAdditionalEffects: the caster hides (the skill's status, skill_get_time).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), src, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
    }

    /// SkillVanishingSlash::calculateSkillRatio (RENEWAL: + 20% per level).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 20 * skill_lv as i32;
    }
}
