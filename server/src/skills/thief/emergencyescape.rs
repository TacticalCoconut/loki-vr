//! skills/thief/emergencyescape.cpp: class SkillEmergencyEscape : public SkillImpl (SC_ESCAPE).

use crate::db::skills::*;
use crate::skill::attack::{skill_blown, BLOWN_IGNORE_NO_KNOCKBACK};
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::iface::skill_unitsetting;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillEmergencyEscape;

impl SkillImpl for SkillEmergencyEscape {
    fn skill_id(&self) -> u16 {
        SC_ESCAPE
    }

    /// SkillEmergencyEscape::castendPos2: the trap, then the caster slides back.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        skill_unitsetting(c, src, id, skill_lv, x, y, 0);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        // Don't stop the caster from backsliding if special_state.no_knockback is active
        skill_blown(c, &s, &s, skill_get_blewcount(id, skill_lv), unit::getdir(&s) as i8, BLOWN_IGNORE_NO_KNOCKBACK);
        let s = unit::get(c, src.id).unwrap_or(s);
        clif_skill_nodamage(c, Some(&s), &s, id, skill_lv as i32, true);
        *flag |= 1;
    }
}
