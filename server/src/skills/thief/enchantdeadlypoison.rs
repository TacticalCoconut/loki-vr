//! skills/thief/enchantdeadlypoison.cpp: class SkillEnchantDeadlyPoison : public StatusSkillImpl (ASC_EDP).

use crate::db::skills::*;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::status::consts::SC_SUB_WEAPONPROPERTY;
use crate::status::defs::ELE_POISON;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillEnchantDeadlyPoison;

impl SkillImpl for SkillEnchantDeadlyPoison {
    fn skill_id(&self) -> u16 {
        ASC_EDP
    }

    /// SkillEnchantDeadlyPoison::castendNoDamageId: EDP also give +25% WATK poison pseudo element to user
    /// (RENEWAL: SC_SUB_WEAPONPROPERTY).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        status_castend_nodamage_id(c, id, false, src, target, skill_lv);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        sc::sc_start4(c, Some(&s), &s, SC_SUB_WEAPONPROPERTY, 100, ELE_POISON, 25, id as i32, 0, skill_get_time(id, skill_lv) as i64);
    }
}
