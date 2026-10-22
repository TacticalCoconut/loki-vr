//! skills/merchant/neutralbarrier.cpp: class SkillNeutralBarrier : public SkillImpl (NC_NEUTRALBARRIER).

use crate::db::skills::*;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::skillunit::skill_clear_unitgroup;
use crate::status::consts::SC_NEUTRALBARRIER_MASTER;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillNeutralBarrier::SkillNeutralBarrier() : SkillImpl(NC_NEUTRALBARRIER).
pub struct SkillNeutralBarrier;

impl SkillImpl for SkillNeutralBarrier {
    fn skill_id(&self) -> u16 {
        NC_NEUTRALBARRIER
    }

    /// SkillNeutralBarrier::castendPos2: a second cast while SC_NEUTRALBARRIER_MASTER runs removes the caster's unit groups (requirements
    /// kept); otherwise the caster's groups are removed ("To remove previous skills - cannot used combined"), the
    /// unit is placed on the caster's cell and SC_NEUTRALBARRIER_MASTER starts with its group id.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, _x: i16, _y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if sce(c, src, SC_NEUTRALBARRIER_MASTER).is_some() {
            skill_clear_unitgroup(c, src.id);
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        skill_clear_unitgroup(c, src.id);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        if let Some(group_id) = crate::skillunit::skill_unitsetting(c, &s, id, skill_lv, s.x, s.y, 0) {
            let s = unit::get(c, src.id).unwrap_or(s);
            sc::sc_start2(c, Some(&s), &s, SC_NEUTRALBARRIER_MASTER, 100, skill_lv as i32, group_id, skill_get_time(id, skill_lv) as i64);
        }
    }
}
