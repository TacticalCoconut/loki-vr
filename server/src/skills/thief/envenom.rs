//! skills/thief/envenom.cpp: class SkillEnvenom : public WeaponSkillImpl (TF_POISON).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::skill_fail;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_POISON;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillEnvenom;

impl SkillImpl for SkillEnvenom {
    fn skill_id(&self) -> u16 {
        TF_POISON
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillEnvenom::applyAdditionalEffects: poison ((4 x level + 10)%), a player's skill fails when it does not
    /// take.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        let ok = sc::sc_start2(c, Some(src), target, SC_POISON, 4 * skill_lv as i32 + 10, skill_lv as i32, src.id as i32, skill_get_time2(id, skill_lv) as i64);
        if !ok && src.kind == BL_PC {
            skill_fail(c, src, id);
        }
    }
}
