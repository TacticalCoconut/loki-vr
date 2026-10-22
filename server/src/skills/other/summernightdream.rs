//! skills/other/summernightdream.cpp: class SkillSummerNightDream : public SkillImpl (ALL_DREAM_SUMMERNIGHT).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSummerNightDream;

impl SkillImpl for SkillSummerNightDream {
    fn skill_id(&self) -> u16 {
        ALL_DREAM_SUMMERNIGHT
    }

    /// SkillSummerNightDream::castendNoDamageId: the packet only.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
    }
}
