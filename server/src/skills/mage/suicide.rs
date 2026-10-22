//! skills/mage/suicide.cpp: class SkillSuicide : public SkillImpl (SA_INSTANTDEATH).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::hp::status_kill;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSuicide;

impl SkillImpl for SkillSuicide {
    fn skill_id(&self) -> u16 {
        SA_INSTANTDEATH
    }

    /// SkillSuicide::castendNoDamageId: the caster dies (status_kill(src)).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        status_kill(c, src.id);
    }
}
