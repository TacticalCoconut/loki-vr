//! skills/archer/harmonize.cpp: class SkillHarmonize : public SkillImpl (MI_HARMONIZE).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHarmonize;

impl SkillImpl for SkillHarmonize {
    fn skill_id(&self) -> u16 {
        MI_HARMONIZE
    }

    /// SkillHarmonize::castendNoDamageId: the status on the caster too when it targets someone else, then on the
    /// target, each with its packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let duration = skill_get_time(id, skill_lv) as i64;
        if src.id != target.id {
            let ok = sc::sc_start(c, Some(src), src, type_, 100, skill_lv as i32, duration);
            let s = super::fresh(c, src);
            clif_skill_nodamage(c, Some(&s), &s, id, skill_lv as i32, ok);
        }
        let s = super::fresh(c, src);
        let ok = sc::sc_start(c, Some(&s), target, type_, 100, skill_lv as i32, duration);
        clif_skill_nodamage(c, Some(&s), &super::fresh(c, target), id, skill_lv as i32, ok);
    }
}
