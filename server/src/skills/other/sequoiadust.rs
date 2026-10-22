//! skills/other/sequoiadust.cpp: class SkillSequoiaDust : public SkillImpl (ECL_SEQUOIADUST).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_BLIND, SC_CURSE, SC_DECREASEAGI, SC_ORCISH, SC_POISON, SC_STONE};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSequoiaDust;

impl SkillImpl for SkillSequoiaDust {
    fn skill_id(&self) -> u16 {
        ECL_SEQUOIADUST
    }

    /// SkillSequoiaDust::castendNoDamageId: cures Stone, Poison, Curse, Blind, Orcish and Decrease AGI
    /// ([`super::peonymamy::eclage_cure`]).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        super::peonymamy::eclage_cure(c, self.skill_id(), &[SC_STONE, SC_POISON, SC_CURSE, SC_BLIND, SC_ORCISH, SC_DECREASEAGI], src, target, skill_lv, tick);
    }
}
