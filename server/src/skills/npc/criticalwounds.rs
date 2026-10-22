//! skills/npc/criticalwounds.cpp: class SkillCriticalWounds : public WeaponSkillImpl (NPC_CRITICALWOUND), rAthena
//! e985006.

use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_CRITICALWOUND;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillCriticalWounds;

impl SkillImpl for SkillCriticalWounds {
    fn skill_id(&self) -> u16 {
        NPC_CRITICALWOUND
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillCriticalWounds::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_CRITICALWOUND, 100, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }
}
