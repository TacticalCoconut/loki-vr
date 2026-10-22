//! skills/acolyte/lexdivina.cpp: class SkillLexDivina : public SkillImpl (PR_LEXDIVINA).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_NONE;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillLexDivina;

impl SkillImpl for SkillLexDivina {
    fn skill_id(&self) -> u16 {
        PR_LEXDIVINA
    }

    /// SkillLexDivina::castendNoDamageId: a silenced target is cured; otherwise the silence comes one second later
    /// (skill_timerskill's PR_LEXDIVINA arm: SC_SILENCE at rate skl->type = 100).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        // tsce = (tsc && type != SC_NONE) ? tsc->getSCE(type) : nullptr
        let tsce = if type_ != SC_NONE { sce(c, target, type_) } else { None };
        if tsce.is_some() {
            sc::status_change_end(c, target.id, type_, 0);
        } else {
            skill_addtimerskill(c, src, tick + 1000, target.id, 0, 0, id, skill_lv, 100, *flag);
        }
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
    }
}
