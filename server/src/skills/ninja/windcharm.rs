//! skills/ninja/windcharm.cpp: class SkillWindCharm : public SkillImpl (KO_KAZEHU_SEIRAN), rAthena e985006.

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWindCharm;

impl SkillImpl for SkillWindCharm {
    fn skill_id(&self) -> u16 {
        KO_KAZEHU_SEIRAN
    }

    /// SkillWindCharm::castendNoDamageId: a player summons a charm of the skill's element.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        super::summon_charm(c, self.skill_id(), src, target, skill_lv);
    }
}
