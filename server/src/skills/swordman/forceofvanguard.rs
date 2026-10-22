//! skills/swordman/forceofvanguard.cpp: class SkillForceOfVanguard : public SkillImpl (LG_FORCEOFVANGUARD).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_fail, clif_skill_nodamage, fail::USESKILL_FAIL_LEVEL, skill_fail};
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_NONE;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillForceOfVanguard;

impl SkillImpl for SkillForceOfVanguard {
    fn skill_id(&self) -> u16 {
        LG_FORCEOFVANGUARD
    }

    /// SkillForceOfVanguard::castendNoDamageId: a toggle; turning it off consumes nothing.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let is_pc = src.kind == BL_PC;
        let tsce = if type_ != SC_NONE { sce(c, target, type_) } else { None };
        if tsce.is_some() {
            let result = sc::status_change_end(c, target.id, type_, 0);
            if result != 0 {
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
            } else if is_pc {
                skill_fail(c, src, id);
            }
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        let result = sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        if result {
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
        } else if is_pc {
            clif_skill_fail(c, src, id, USESKILL_FAIL_LEVEL, 0, 0);
        }
    }
}
