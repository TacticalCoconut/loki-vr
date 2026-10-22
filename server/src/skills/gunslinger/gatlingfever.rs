//! skills/gunslinger/gatlingfever.cpp: class SkillGatlingfever : public SkillImpl (GS_GATLINGFEVER), rAthena e985006.

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGatlingfever;

impl SkillImpl for SkillGatlingfever {
    fn skill_id(&self) -> u16 {
        GS_GATLINGFEVER
    }

    /// SkillGatlingfever::castendNoDamageId: a toggle (a running status ends, else it starts for skill_get_time).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if sce(c, target, type_).is_some() {
            let ended = sc::status_change_end(c, target.id, type_, 0) != 0;
            let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ended);
            return;
        }
        let ok = sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
    }
}
