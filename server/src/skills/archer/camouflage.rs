//! skills/archer/camouflage.cpp: class SkillCamouflage : public SkillImpl (RA_CAMOUFLAGE).

use crate::db::skills::*;
use crate::skill::clif::{clif_skill_fail, clif_skill_nodamage, fail, skill_fail};
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_NONE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillCamouflage;

impl SkillImpl for SkillCamouflage {
    fn skill_id(&self) -> u16 {
        RA_CAMOUFLAGE
    }

    /// SkillCamouflage::castendNoDamageId: a running Camouflage ends (no requirement consumed), else it starts; a
    /// failure is reported to a player.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let is_pc = src.kind == crate::map::BL_PC;
        let tsce = if type_ != SC_NONE { sce(c, target, type_) } else { None };
        if tsce.is_some() {
            let i = sc::status_change_end(c, target.id, type_, 0) != 0;
            if i {
                clif_skill_nodamage(c, Some(src), &super::fresh(c, target), id, skill_lv as i32, i);
            } else if is_pc {
                skill_fail(c, src, id);
            }
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        let i = sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        if i {
            clif_skill_nodamage(c, Some(src), &super::fresh(c, target), id, skill_lv as i32, i);
        } else if is_pc {
            clif_skill_fail(c, src, id, fail::USESKILL_FAIL_LEVEL, 0, 0);
        }
    }
}
