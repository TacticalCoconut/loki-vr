//! skills/thief/divestarmor.cpp: class SkillDivestArmor : public SkillImpl (RG_STRIPARMOR).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::effect::skill_strip_equip;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillDivestArmor;

impl SkillImpl for SkillDivestArmor {
    fn skill_id(&self) -> u16 {
        RG_STRIPARMOR
    }

    /// SkillDivestArmor::castendNoDamageId: skill_strip_equip, the animation with its result, and a failure when nothing
    /// was stripped.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let i = skill_strip_equip(c, src, target, id, skill_lv);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, i);
        //Nothing stripped.
        if src.kind == BL_PC && !i {
            skill_fail(c, src, id);
        }
    }
}
