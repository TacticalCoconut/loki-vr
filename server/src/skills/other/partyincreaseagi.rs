//! skills/other/partyincreaseagi.cpp: class SkillPartyIncreaseAgi : public SkillImpl (CASH_INCAGI). Its
//! castendNoDamageId is partyassumptio.cpp's code with this skill id
//! ([`super::partyassumptio::party_status_castend_nodamage_id`]).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillPartyIncreaseAgi;

impl SkillImpl for SkillPartyIncreaseAgi {
    fn skill_id(&self) -> u16 {
        CASH_INCAGI
    }

    /// SkillPartyIncreaseAgi::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        super::partyassumptio::party_status_castend_nodamage_id(c, self.skill_id(), src, target, skill_lv, tick, flag);
    }
}
