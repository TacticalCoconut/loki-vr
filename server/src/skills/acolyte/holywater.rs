//! skills/acolyte/holywater.cpp: class SkillHolyWater : public SkillImpl (AL_HOLYWATER).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::condition::ITEMID_HOLY_WATER;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillHolyWater;

impl SkillImpl for SkillHolyWater {
    fn skill_id(&self) -> u16 {
        AL_HOLYWATER
    }

    /// SkillHolyWater::castendNoDamageId: makes a Holy Water (skill_produce_mix); a Suiton cell under the target is
    /// removed.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, bl: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != BL_PC {
            return;
        }
        if crate::itemui::produce::skill_produce_mix(c, src.id, id, ITEMID_HOLY_WATER, 0, 0, 0, 1, -1) {
            let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
            if let Some(su) = crate::skillunit::movement::map_find_skill_unit_oncell(c, &t, t.x, t.y, NJ_SUITON, None, 0) {
                crate::skillunit::group::skill_delunit(c, su);
            }
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, bl.id).unwrap_or(t);
            clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
        } else {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            skill_fail(c, &s, id);
        }
    }
}
