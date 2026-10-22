//! skills/npc/bleeding2.cpp: class SkillBleeding2 : public WeaponSkillImpl (NPC_BLEEDING2), rAthena e985006.

use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_BLEEDING;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillBleeding2;

impl SkillImpl for SkillBleeding2 {
    fn skill_id(&self) -> u16 {
        NPC_BLEEDING2
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillBleeding2::applyAdditionalEffects: SC_BLEEDING at (50+10*skill_lv)%.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_BLEEDING, 50 + 10 * skill_lv as i32, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillBleeding2::modifyHitRate: `hit_rate += hit_rate * 20 / 100` on the int16.
    fn modify_hit_rate(&self, _c: &Ctx, hit_rate: &mut i16, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        let h = *hit_rate as i32;
        *hit_rate = (h + h * 20 / 100) as i16;
    }
}
