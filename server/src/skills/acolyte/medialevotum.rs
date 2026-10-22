//! skills/acolyte/medialevotum.cpp: class SkillMedialeVotum : public StatusSkillImpl (CD_MEDIALE_VOTUM).

use crate::db::skills::*;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMedialeVotum;

impl SkillImpl for SkillMedialeVotum {
    fn skill_id(&self) -> u16 {
        CD_MEDIALE_VOTUM
    }

    /// SkillMedialeVotum::castendNoDamageId: the caster's cast is StatusSkillImpl::castendNoDamageId (the status'
    /// timer heals the party through skill_castend_nodamage_id with flag 1); flag & 1: the party heal (the same code
    /// as Dilectio Heal's, dilectioheal.rs party_heal).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            super::dilectioheal::party_heal(c, id, src, target, skill_lv, tick, *flag);
        } else {
            status_castend_nodamage_id(c, id, false, src, target, skill_lv);
        }
    }
}
