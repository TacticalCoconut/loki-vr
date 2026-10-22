//! skills/thief/enchantpoison.cpp: class SkillEnchantPoison : public SkillImpl (AS_ENCHANTPOISON).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillEnchantPoison;

impl SkillImpl for SkillEnchantPoison {
    fn skill_id(&self) -> u16 {
        AS_ENCHANTPOISON
    }

    /// SkillEnchantPoison::castendNoDamageId: the status; a failure sends the failed animation and the skill fail.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64) {
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
        } else {
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, false);
            if src.kind == BL_PC {
                skill_fail(c, src, id);
            }
        }
    }
}
