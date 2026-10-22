//! skills/archer/concentration.cpp: class SkillConcentration : public SkillImpl (AC_CONCENTRATION).

use crate::db::skills::*;
use crate::skill::area::{foreach_in_range, Wall};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillConcentration;

impl SkillImpl for SkillConcentration {
    fn skill_id(&self) -> u16 {
        AC_CONCENTRATION
    }

    /// SkillConcentration::castendNoDamageId: the status, then the hidden traps around the caster are revealed
    /// (skill_reveal_trap_inarea) and every character around it gets status_change_timer_sub(SC_CONCENTRATE): the
    /// hidden ones are revealed.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let splash = skill_get_splash(id, skill_lv) as i16;
        let ok = sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        clif_skill_nodamage(c, Some(src), &super::fresh(c, target), id, skill_lv as i32, ok);
        let s = super::fresh(c, src);
        crate::skillunit::ontimer::skill_reveal_trap_inarea(c, &s, splash, s.x, s.y);
        // map_foreachinallrange(status_change_timer_sub, src, splash, BL_CHAR, src, nullptr, type, tick)
        foreach_in_range(c, &s, splash, crate::map::BL_CHAR, Wall::None, |c, bl| crate::status::sc_clear::status_change_timer_sub(c, bl, &s, None, type_, tick));
    }
}
