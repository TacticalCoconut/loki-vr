//! skills/ninja/firecharm.cpp: class SkillFireCharm : public SkillImpl (KO_KAHU_ENTEN), rAthena e985006.

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFireCharm;

impl SkillImpl for SkillFireCharm {
    fn skill_id(&self) -> u16 {
        KO_KAHU_ENTEN
    }

    /// SkillFireCharm::castendNoDamageId: a player summons a charm of the skill's element.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        super::summon_charm(c, self.skill_id(), src, target, skill_lv);
    }
}
