//! skills/acolyte/fallenempire.cpp: class SkillFallenEmpire : public WeaponSkillImpl (SR_FALLENEMPIRE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFallenEmpire;

impl SkillImpl for SkillFallenEmpire {
    fn skill_id(&self) -> u16 {
        SR_FALLENEMPIRE
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillFallenEmpire::calculateSkillRatio: ATK [(Skill Level x 300 + 100) x Caster Base Level / 150] %.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 300 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 150);
    }
}
