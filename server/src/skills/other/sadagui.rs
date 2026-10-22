//! skills/other/sadagui.cpp: class SkillSadagui : public SkillImpl (ECL_SADAGUI).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_CONFUSION, SC_FEAR, SC_HALLUCINATION, SC_STUN};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSadagui;

impl SkillImpl for SkillSadagui {
    fn skill_id(&self) -> u16 {
        ECL_SADAGUI
    }

    /// SkillSadagui::castendNoDamageId: cures Stun, Confusion, Hallucination and Fear
    /// ([`super::peonymamy::eclage_cure`]).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        super::peonymamy::eclage_cure(c, self.skill_id(), &[SC_STUN, SC_CONFUSION, SC_HALLUCINATION, SC_FEAR], src, target, skill_lv, tick);
    }
}
