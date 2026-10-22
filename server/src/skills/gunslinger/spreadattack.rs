//! skills/gunslinger/spreadattack.cpp: class SkillSpreadAttack : public SkillImplRecursiveDamageSplash
//! (GS_SPREADATTACK), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::Ctx;

pub struct SkillSpreadAttack;

impl SkillImpl for SkillSpreadAttack {
    fn skill_id(&self) -> u16 {
        GS_SPREADATTACK
    }

    crate::splash_skill_defaults!();

    /// SkillSpreadAttack::calculateSkillRatio (RENEWAL: + 30% per level).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 30 * skill_lv as i32;
    }
}

impl RecursiveDamageSplash for SkillSpreadAttack {}
