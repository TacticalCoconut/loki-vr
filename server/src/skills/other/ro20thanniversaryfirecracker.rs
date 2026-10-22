//! skills/other/ro20thanniversaryfirecracker.cpp: class SkillRo20thAnniversaryFirecracker : public SkillImpl
//! (ALL_EVENT_20TH_ANNIVERSARY).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRo20thAnniversaryFirecracker;

impl SkillImpl for SkillRo20thAnniversaryFirecracker {
    fn skill_id(&self) -> u16 {
        ALL_EVENT_20TH_ANNIVERSARY
    }

    /// SkillRo20thAnniversaryFirecracker::castendNoDamageId: the packet on the caster only.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), src, self.skill_id(), skill_lv as i32, true);
    }
}
