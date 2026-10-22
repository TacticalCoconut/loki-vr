//! skills/swordman/enchantblade.cpp: class SkillEnchantBlade : public SkillImpl (RK_ENCHANTBLADE).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillEnchantBlade;

impl SkillImpl for SkillEnchantBlade {
    fn skill_id(&self) -> u16 {
        RK_ENCHANTBLADE
    }

    /// SkillEnchantBlade::castendNoDamageId: val2 = ((100 + 20 * lv) * caster level) / 100 + INT.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let sstatus = crate::status::status_get_status_data(c, src);
        let val2 = ((100 + 20 * skill_lv as i32) * crate::status::status_get_lv(c, src)) / 100 + sstatus.int_ as i32;
        let ok = sc::sc_start2(c, Some(src), target, type_, 100, skill_lv as i32, val2, skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
    }
}
