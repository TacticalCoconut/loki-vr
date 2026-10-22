//! skills/npc/npcarrowstorm.cpp: class SkillNpcArrowStorm : public SkillImplRecursiveDamageSplash (NPC_ARROWSTORM),
//! rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::Ctx;

pub struct SkillNpcArrowStorm;

impl SkillImpl for SkillNpcArrowStorm {
    fn skill_id(&self) -> u16 {
        NPC_ARROWSTORM
    }

    crate::splash_skill_defaults!();

    /// SkillNpcArrowStorm::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        if skill_lv > 4 {
            *base_skillratio += 1900;
        } else {
            *base_skillratio += 900;
        }
    }
}

impl RecursiveDamageSplash for SkillNpcArrowStorm {}
