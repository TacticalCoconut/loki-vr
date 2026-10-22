//! skills/npc/widecriticalwounds.cpp: class SkillWideCriticalWounds : public SkillImplRecursiveDamageSplash
//! (NPC_WIDECRITICALWOUND).

use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_CRITICALWOUND;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWideCriticalWounds;

impl SkillImpl for SkillWideCriticalWounds {
    fn skill_id(&self) -> u16 {
        NPC_WIDECRITICALWOUND
    }

    crate::splash_skill_defaults!();

    /// SkillWideCriticalWounds::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, SC_CRITICALWOUND, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
    }

    /// SkillWideCriticalWounds::castendNoDamageId: skill_castend_damage_id on the caster itself (the splash around
    /// it).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_castend_damage_id(c, src, src, self.skill_id(), skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillWideCriticalWounds {}
