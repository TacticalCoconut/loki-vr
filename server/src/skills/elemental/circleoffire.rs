//! skills/elemental/circleoffire.cpp: class SkillCircleOfFire : public SkillImpl (EL_CIRCLE_OF_FIRE), rAthena e985006.

use super::{option_castend_nodamage_id, OptionAnim};
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillCircleOfFire;

impl SkillImpl for SkillCircleOfFire {
    fn skill_id(&self) -> u16 {
        EL_CIRCLE_OF_FIRE
    }

    /// SkillCircleOfFire::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 200;
    }

    /// SkillCircleOfFire::castendNoDamageId: the elemental's option skill, with the dummy damage packet on the target (see [`option_castend_nodamage_id`]).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        option_castend_nodamage_id(c, self.skill_id(), src, target, skill_lv, tick, OptionAnim::Target);
    }
}
