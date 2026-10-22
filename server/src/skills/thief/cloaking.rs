//! skills/thief/cloaking.cpp: class SkillCloaking : public SkillImpl (AS_CLOAKING).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::fail::USESKILL_FAIL_LEVEL;
use crate::skill::clif::{clif_skill_fail, clif_skill_nodamage, skill_fail};
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_NONE;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillCloaking;

impl SkillImpl for SkillCloaking {
    fn skill_id(&self) -> u16 {
        AS_CLOAKING
    }

    /// SkillCloaking::castendNoDamageId: a toggle (a running Cloaking ends); the skill-scream animation is hidden
    /// (level -1).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let is_sd = src.kind == BL_PC;
        if type_ != SC_NONE && sce(c, target, type_).is_some() {
            let i = sc::status_change_end(c, target.id, type_, 0);
            if i != 0 {
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                clif_skill_nodamage(c, Some(src), &t, id, -1, i != 0);
            } else if is_sd {
                skill_fail(c, src, id);
            }
            return;
        }
        let i = sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        if i {
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(src), &t, id, -1, i);
        } else if is_sd {
            clif_skill_fail(c, src, id, USESKILL_FAIL_LEVEL, 0, 0);
        }
    }
}
