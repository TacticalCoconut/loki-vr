//! skills/custom/skill_factory_custom.cpp: SkillFactoryCustom::create and its example class (rAthena e985006). The
//! factory is first in SkillFactoryImpl::create's list so a server can override any skill; rAthena ships its switch
//! under `#if 0` (the SkillCustomBash example), so it always answers nullptr: ported as it is. The example class is
//! compiled in rAthena but never created; it is ported below the same way (never created).

use super::SkillImpl;
use crate::battle::calc::Damage;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillFactoryCustom::create: `return nullptr` (the example switch, `case SM_BASH: return
/// std::make_unique<SkillCustomBash>();`, is compiled out by `#if 0`).
pub fn create(_skill_id: u16) -> Option<Box<dyn SkillImpl>> {
    None
}

/// `class SkillCustomBash : public SkillBash`: Bash with a stronger ratio (the example of an override; never created,
/// as the factory's switch is compiled out). Every method but calculateSkillRatio is SkillBash's.
#[allow(dead_code)]
pub struct SkillCustomBash;

impl SkillImpl for SkillCustomBash {
    fn skill_id(&self) -> u16 {
        super::swordman::bash::SkillBash.skill_id()
    }

    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        super::swordman::bash::SkillBash.castend_damage_id(c, src, target, skill_lv, tick, flag);
    }

    /// SkillCustomBash::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        // Normal Bash:
        // Base 100% + 30% per level
        // base_skillratio += 30 * skill_lv;

        // But my custom Bash Skill is stronger:
        // Base 100% + 300% per level
        *base_skillratio += 300 * skill_lv as i32;
    }

    fn modify_hit_rate(&self, c: &Ctx, hit_rate: &mut i16, src: &Unit, target: &Unit, skill_lv: u16) {
        super::swordman::bash::SkillBash.modify_hit_rate(c, hit_rate, src, target, skill_lv);
    }

    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, attack_type: i32, dmg_lv: u8) {
        super::swordman::bash::SkillBash.apply_additional_effects(c, src, target, skill_lv, tick, attack_type, dmg_lv);
    }
}
