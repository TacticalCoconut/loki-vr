//! skills/archer/longingforfreedom.cpp: class SkillLongingForFreedom : public SkillImpl (CG_LONGINGFREEDOM).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillLongingForFreedom;

impl SkillImpl for SkillLongingForFreedom {
    fn skill_id(&self) -> u16 {
        CG_LONGINGFREEDOM
    }

    /// SkillLongingForFreedom::castendNoDamageId: its whole body is `#ifndef RENEWAL` (SC_LONGING while dancing an
    /// ensemble other than Moonlight Petals): the class does nothing in this build.
    fn castend_nodamage_id(&self, _c: &Ctx, _src: &Unit, _target: &Unit, _skill_lv: u16, _tick: Tick, _flag: &mut i32) {}
}
