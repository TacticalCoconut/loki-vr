//! skills/archer/falconassault.cpp: class SkillFalconAssault : public SkillImpl (SN_FALCONASSAULT). Its damage is
//! battle_calc_misc_attack's SN_FALCONASSAULT case.

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFalconAssault;

impl SkillImpl for SkillFalconAssault {
    fn skill_id(&self) -> u16 {
        SN_FALCONASSAULT
    }

    /// SkillFalconAssault::castendDamageId: `skill_attack(skill_get_type(getSkillId()), src, src, target, …)`.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        crate::skill::attack::skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
    }
}
