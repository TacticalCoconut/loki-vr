//! skills/npc/petrifyattack.cpp: class SkillPetrifyAttack : public WeaponSkillImpl (NPC_PETRIFYATTACK).

use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_STONEWAIT;
use crate::status::defs::SCSTART_NONE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillPetrifyAttack;

impl SkillImpl for SkillPetrifyAttack {
    fn skill_id(&self) -> u16 {
        NPC_PETRIFYATTACK
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillPetrifyAttack::applyAdditionalEffects: `sc_start2(src, target, SC_STONEWAIT, 20 * skill_lv, skill_lv,
    /// src->id, skill_get_time2(…), skill_get_time(…))`: the last argument is sc_start2's `delay` (status.hpp
    /// sc_start2 → status_change_start(…, 100 * rate, val1, val2, 0, 0, duration, SCSTART_NONE, delay)).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        let rate = 20 * skill_lv as i32;
        sc::status_change_start(c, Some(src), target, SC_STONEWAIT, 100 * rate, skill_lv as i32, src.id as i32, 0, 0, skill_get_time2(id, skill_lv) as i64, SCSTART_NONE, skill_get_time(id, skill_lv));
    }

    /// SkillPetrifyAttack::modifyHitRate: +20%.
    fn modify_hit_rate(&self, _c: &Ctx, hit_rate: &mut i16, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        let h = *hit_rate as i32;
        *hit_rate = (h + h * 20 / 100) as i16;
    }
}
