//! skills/merchant/mammonite.cpp: class SkillMammonite : public WeaponSkillImpl (MC_MAMMONITE). Its zeny cost is the
//! skill database's (ZenyCost, consumed by skill_consume_requirement).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillMammonite::SkillMammonite() : WeaponSkillImpl(MC_MAMMONITE).
pub struct SkillMammonite;

impl SkillImpl for SkillMammonite {
    fn skill_id(&self) -> u16 {
        MC_MAMMONITE
    }

    /// WeaponSkillImpl::castendDamageId (inherited).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        crate::skills::weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillMammonite::calculateSkillRatio: +50% per level.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 50 * skill_lv as i32;
    }
}
