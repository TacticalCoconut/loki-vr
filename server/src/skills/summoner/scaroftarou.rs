//! skills/summoner/scaroftarou.cpp: class SkillScarofTarou : public WeaponSkillImpl (SU_SCAROFTAROU).

use super::add_hp_ratio;
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::novice::pc_checkskill;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::{SC_BITESCAR, SC_STUN};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillScarofTarou;

impl SkillImpl for SkillScarofTarou {
    fn skill_id(&self) -> u16 {
        SU_SCAROFTAROU
    }

    /// SkillScarofTarou::applyAdditionalEffects ("TODO: What's the chance/time?").
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_STUN, 10, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillScarofTarou::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + 100 * skill_lv as i32;
        if src.kind == BL_PC && pc_checkskill(c, src, SU_SPIRITOFLIFE) != 0 {
            add_hp_ratio(c, src, base_skillratio);
        }
    }

    /// SkillScarofTarou::castendDamageId: SC_BITESCAR (10%, "TODO: What's the activation chance for the Bite
    /// effect?"), then WeaponSkillImpl::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, SC_BITESCAR, 10, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        weapon_castend_damage_id(c, id, &s, &t, skill_lv, tick, *flag);
    }
}
