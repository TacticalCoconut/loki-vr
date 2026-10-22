//! skills/ninja/mirage.cpp: class SkillMirage : public SkillImpl (SS_SHINKIROU), rAthena e985006.

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::iface::skill_unitsetting;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMirage;

impl SkillImpl for SkillMirage {
    fn skill_id(&self) -> u16 {
        SS_SHINKIROU
    }

    /// SkillMirage::castendPos2: the animation, the caster's status (skill_get_time), then the mirage unit at (x, y).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        *flag |= 1;
        clif_skill_nodamage(c, Some(src), src, id, skill_lv as i32, true);
        sc::sc_start(c, Some(src), src, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        skill_unitsetting(c, &s, id, skill_lv, x, y, 0);
    }
}
