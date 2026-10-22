//! skills/novice/firstaid.cpp: class SkillFirstAid : public SkillImpl (NV_FIRSTAID).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFirstAid;

impl SkillImpl for SkillFirstAid {
    fn skill_id(&self) -> u16 {
        NV_FIRSTAID
    }

    /// SkillFirstAid::castendNoDamageId: the packet shows level 5, then status_heal(target, 5, 0, 0).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, _skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), 5, true);
        crate::status::hp::status_heal(c, target.id, 5, 0, 0, 0);
    }
}
