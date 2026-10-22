//! skills/other/snowflip.cpp: class SkillSnowFlip : public SkillImpl (ECL_SNOWFLIP).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_BLEEDING, SC_BURNING, SC_DEEPSLEEP, SC_SLEEP};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSnowFlip;

impl SkillImpl for SkillSnowFlip {
    fn skill_id(&self) -> u16 {
        ECL_SNOWFLIP
    }

    /// SkillSnowFlip::castendNoDamageId: cures Sleep, Bleeding, Burning and Deep Sleep
    /// ([`super::peonymamy::eclage_cure`]).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        super::peonymamy::eclage_cure(c, self.skill_id(), &[SC_SLEEP, SC_BLEEDING, SC_BURNING, SC_DEEPSLEEP], src, target, skill_lv, tick);
    }
}
