//! skills/thief/reproduce.cpp: class SkillReproduce : public SkillImpl (SC_REPRODUCE).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::fail::USESKILL_FAIL_LEVEL;
use crate::skill::clif::{clif_skill_fail, clif_skill_nodamage, skill_fail};
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_NONE;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillReproduce;

impl SkillImpl for SkillReproduce {
    fn skill_id(&self) -> u16 {
        SC_REPRODUCE
    }

    /// SkillReproduce::castendNoDamageId: a toggle (the skill-scream animation hidden: level -1); ending it consumes no
    /// requirement (SKILL_NOCONSUME_REQ).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
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
            *flag |= SKILL_NOCONSUME_REQ;
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
