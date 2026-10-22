//! skills/swordman/shieldboomerang.cpp: class SkillShieldBoomerang : public WeaponSkillImpl (CR_SHIELDBOOMERANG).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::defs::ELE_NEUTRAL;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillShieldBoomerang;

impl SkillImpl for SkillShieldBoomerang {
    fn skill_id(&self) -> u16 {
        CR_SHIELDBOOMERANG
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillShieldBoomerang::calculateSkillRatio (RENEWAL).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + skill_lv as i32 * 80;
    }

    /// SkillShieldBoomerang::modifyElement (RENEWAL): flag 1 means the element should be calculated for damage only.
    fn modify_element(&self, _c: &Ctx, _dmg: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, flag: i32) {
        if flag & 1 != 0 {
            *element = ELE_NEUTRAL;
        }
    }
}
