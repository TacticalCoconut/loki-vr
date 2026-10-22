//! skills/other/returntolighthalzen.cpp: class SkillReturnToLighthalzen : public SkillImpl (ALL_LIGHTHALZEN_RECALL).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillReturnToLighthalzen;

impl SkillImpl for SkillReturnToLighthalzen {
    fn skill_id(&self) -> u16 {
        ALL_LIGHTHALZEN_RECALL
    }

    /// SkillReturnToLighthalzen::castendNoDamageId: to MAP_LIGHTHALZEN ("lighthalzen") 307,307.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        super::recall_castend_nodamage_id(c, self.skill_id(), src, target, skill_lv, flag, "lighthalzen", 307, 307);
    }
}
