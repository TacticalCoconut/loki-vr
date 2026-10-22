//! skills/mage/magicrod.cpp: class SkillMagicRod : public SkillImpl (SA_MAGICROD).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMagicRod;

impl SkillImpl for SkillMagicRod {
    fn skill_id(&self) -> u16 {
        SA_MAGICROD
    }

    /// SkillMagicRod::castendNoDamageId. RENEWAL: the skill packet on the caster first
    /// (`clif_skill_nodamage(src, *src, SA_MAGICROD, skill_lv)`); the status start's result is not shown.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        clif_skill_nodamage(c, Some(src), src, SA_MAGICROD, skill_lv as i32, true);
        sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
    }
}
