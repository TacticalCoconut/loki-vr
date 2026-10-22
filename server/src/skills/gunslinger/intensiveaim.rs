//! skills/gunslinger/intensiveaim.cpp: class SkillIntensiveAim : public SkillImpl (NW_INTENSIVE_AIM), rAthena e985006.

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_INTENSIVE_AIM_COUNT;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillIntensiveAim;

impl SkillImpl for SkillIntensiveAim {
    fn skill_id(&self) -> u16 {
        NW_INTENSIVE_AIM
    }

    /// SkillIntensiveAim::castendNoDamageId: a toggle; either way the aim count ends first; then the caster's
    /// animation.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if sce(c, target, type_).is_some() {
            sc::status_change_end(c, src.id, SC_INTENSIVE_AIM_COUNT, 0);
            sc::status_change_end(c, target.id, type_, 0);
        } else {
            sc::status_change_end(c, src.id, SC_INTENSIVE_AIM_COUNT, 0);
            let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
            sc::sc_start(c, Some(&s), &t, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        }
        let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        clif_skill_nodamage(c, Some(&s), &s, id, skill_lv as i32, true);
    }
}
