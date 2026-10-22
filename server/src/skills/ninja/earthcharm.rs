//! skills/ninja/earthcharm.cpp: class SkillEarthCharm : public SkillImpl (KO_DOHU_KOUKAI), rAthena e985006.

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillEarthCharm;

impl SkillImpl for SkillEarthCharm {
    fn skill_id(&self) -> u16 {
        KO_DOHU_KOUKAI
    }

    /// SkillEarthCharm::castendNoDamageId: a player summons a charm of the skill's element.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        super::summon_charm(c, self.skill_id(), src, target, skill_lv);
    }
}
