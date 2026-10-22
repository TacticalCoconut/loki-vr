//! skills/gunslinger/antimaterialblast.cpp: class SkillAntiMaterialBlast : public WeaponSkillImpl (RL_AM_BLAST),
//! rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_ANTI_M_BLAST;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillAntiMaterialBlast;

impl SkillImpl for SkillAntiMaterialBlast {
    fn skill_id(&self) -> u16 {
        RL_AM_BLAST
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillAntiMaterialBlast::applyAdditionalEffects: SC_ANTI_M_BLAST, 20 + 10% per level.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_ANTI_M_BLAST, 20 + 10 * skill_lv as i32, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillAntiMaterialBlast::calculateSkillRatio: 3500 + 300% per level.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 3500 + 300 * skill_lv as i32;
    }
}
