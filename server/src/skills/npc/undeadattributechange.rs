//! skills/npc/undeadattributechange.cpp: class SkillUndeadAttributeChange : public WeaponSkillImpl (NPC_CHANGEUNDEAD).

use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_CHANGEUNDEAD;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillUndeadAttributeChange;

impl SkillImpl for SkillUndeadAttributeChange {
    fn skill_id(&self) -> u16 {
        NPC_CHANGEUNDEAD
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillUndeadAttributeChange::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, SC_CHANGEUNDEAD, 10 * skill_lv as i32, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
    }

    /// SkillUndeadAttributeChange::modifyHitRate: +20%.
    fn modify_hit_rate(&self, _c: &Ctx, hit_rate: &mut i16, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        let h = *hit_rate as i32;
        *hit_rate = (h + h * 20 / 100) as i16;
    }
}
