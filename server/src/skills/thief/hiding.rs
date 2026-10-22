//! skills/thief/hiding.cpp: class SkillHiding : public SkillImpl (TF_HIDING).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_HIDING;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillHiding;

impl SkillImpl for SkillHiding {
    fn skill_id(&self) -> u16 {
        TF_HIDING
    }

    /// SkillHiding::castendNoDamageId: a toggle; the skill-scream animation is hidden (level -1).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, bl: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if sce(c, bl, SC_HIDING).is_some() {
            let ended = sc::status_change_end(c, bl.id, type_, 0) != 0;
            let b = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
            clif_skill_nodamage(c, Some(src), &b, id, -1, ended); // Hide skill-scream animation.
            return;
        }
        let ok = sc::sc_start(c, Some(src), bl, SC_HIDING, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let b = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
        clif_skill_nodamage(c, Some(&s), &b, id, -1, ok);
    }
}
