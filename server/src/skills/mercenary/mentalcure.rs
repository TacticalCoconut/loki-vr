//! skills/mercenary/mercenary_mentalcure.cpp: class SkillMercenaryMentalCure : public SkillImpl (MER_MENTALCURE).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::SC_CONFUSION;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMercenaryMentalCure;

impl SkillImpl for SkillMercenaryMentalCure {
    fn skill_id(&self) -> u16 {
        MER_MENTALCURE
    }

    /// SkillMercenaryMentalCure::castendNoDamageId: cures Confusion.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        sc::status_change_end(c, target.id, SC_CONFUSION, 0);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, self.skill_id(), skill_lv as i32, true);
    }
}
