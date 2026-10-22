//! skills/mage/jupitelthunder.cpp: class SkillJupitelThunder : public SkillImpl (WZ_JUPITEL). The hit itself is
//! skill_timerskill's WZ_WATERBALL / WZ_JUPITEL case (skill/timerskill.rs), 150 ms later.

use crate::db::skills::*;
use crate::skill::timerskill::{skill_addtimerskill, TIMERSKILL_INTERVAL};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillJupitelThunder;

impl SkillImpl for SkillJupitelThunder {
    fn skill_id(&self) -> u16 {
        WZ_JUPITEL
    }

    /// SkillJupitelThunder::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        //Jupitel Thunder is delayed by 150ms, you can cast another spell before the knockback
        skill_addtimerskill(c, src, tick + TIMERSKILL_INTERVAL, target.id, 0, 0, self.skill_id(), skill_lv, 1, *flag);
    }
}
