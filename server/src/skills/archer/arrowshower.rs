//! skills/archer/arrowshower.cpp: class SkillArrowShower : public SkillImplRecursiveDamageSplash (AC_SHOWER).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{splash_castend_damage_id, splash_castend_pos2, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_CAMOUFLAGE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillArrowShower;

impl SkillImpl for SkillArrowShower {
    fn skill_id(&self) -> u16 {
        AC_SHOWER
    }

    /// SkillImplRecursiveDamageSplash::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        splash_castend_damage_id(self, c, src, target, skill_lv, tick, flag);
    }

    /// SkillArrowShower::calculateSkillRatio: Renewal `base_skillratio += 50 + 10 * skill_lv`.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 50 + 10 * skill_lv as i32;
    }

    /// SkillArrowShower::castendPos2: Camouflage ends, then SkillImplRecursiveDamageSplash::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        sc::status_change_end(c, src.id, SC_CAMOUFLAGE, 0);
        let s = super::fresh(c, src);
        splash_castend_pos2(self, c, &s, x, y, skill_lv, tick, flag);
    }
}

impl RecursiveDamageSplash for SkillArrowShower {}
