//! skills/thief/masqueradeenervation.cpp: class SkillMasqueradeEnervation : public SkillImpl (SC_ENERVATION).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::defs::CLASS_BOSS;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMasqueradeEnervation;

impl SkillImpl for SkillMasqueradeEnervation {
    fn skill_id(&self) -> u16 {
        SC_ENERVATION
    }

    /// SkillMasqueradeEnervation::castendNoDamageId: not on a target that has the status already (skill failure) nor on a boss
    /// class; the chance is super::masquerade_rate.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if sce(c, target, type_).is_none() {
            if crate::status::status_get_status_data(c, target).class_ as i32 == CLASS_BOSS {
                return;
            }
            let rate = super::masquerade_rate(c, src, target, skill_lv);
            let ok = sc::sc_start(c, Some(src), target, type_, rate, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(&s), &t, id, 0, ok);
        } else if src.kind == BL_PC {
            skill_fail(c, src, id);
        }
    }
}
