//! skills/mage/energycoat.cpp: class SkillEnergyCoat : public StatusSkillImpl (MG_ENERGYCOAT). No override: the
//! StatusSkillImpl behaviour (SC_ENERGYCOAT for skill_get_time on the target, end_if_running = false).

use crate::db::skills::*;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillEnergyCoat;

impl SkillImpl for SkillEnergyCoat {
    fn skill_id(&self) -> u16 {
        MG_ENERGYCOAT
    }

    /// StatusSkillImpl::castendNoDamageId (not overridden; SkillEnergyCoat() : StatusSkillImpl(MG_ENERGYCOAT)).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        status_castend_nodamage_id(c, self.skill_id(), false, src, target, skill_lv);
    }
}
