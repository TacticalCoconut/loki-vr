//! skills/npc/stunattack.cpp: class SkillStunAttack : public WeaponSkillImpl (NPC_STUNATTACK).

use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_STUN;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillStunAttack;

impl SkillImpl for SkillStunAttack {
    fn skill_id(&self) -> u16 {
        NPC_STUNATTACK
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillStunAttack::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, SC_STUN, 20 * skill_lv as i32, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
    }

    /// SkillStunAttack::modifyHitRate: +20%.
    fn modify_hit_rate(&self, _c: &Ctx, hit_rate: &mut i16, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        let h = *hit_rate as i32;
        *hit_rate = (h + h * 20 / 100) as i16;
    }
}
