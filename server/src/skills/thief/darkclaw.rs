//! skills/thief/darkclaw.cpp: class SkillDarkClaw : public WeaponSkillImpl (GC_DARKCROW).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_DARKCROW;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillDarkClaw;

impl SkillImpl for SkillDarkClaw {
    fn skill_id(&self) -> u16 {
        GC_DARKCROW
    }

    /// SkillDarkClaw::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 100 * (skill_lv as i32 - 1);
    }

    /// SkillDarkClaw::castendDamageId: the hit, then SC_DARKCROW (should be applied even on miss).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        weapon_castend_damage_id(c, id, src, target, skill_lv, tick, *flag);
        let Some(t) = crate::unit::get_or_freed(c, target.id) else { return };
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        sc::sc_start(c, Some(&s), &t, SC_DARKCROW, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64); // Should be applied even on miss
    }
}
