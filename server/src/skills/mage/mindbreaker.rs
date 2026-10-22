//! skills/mage/mindbreaker.cpp: class SkillMindBreaker : public SkillImpl (PF_MINDBREAKER).

use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::misc::skill_get_range2;
use crate::skill::unit::unit_skillcastcancel;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::SkillImpl;
use crate::status::consts::SC_NONE;
use crate::status::defs::MD_STATUSIMMUNE;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMindBreaker;

impl SkillImpl for SkillMindBreaker {
    fn skill_id(&self) -> u16 {
        PF_MINDBREAKER
    }

    /// SkillMindBreaker::castendNoDamageId: a 55% + 5%/lv chance of SC_MINDBREAKER; the requirements are not
    /// consumed when it does nothing.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let tstatus = crate::status::status_get_status_data(c, target);
        let type_ = skill_get_sc(id) as i32;
        // tsce = (tsc != nullptr && type != SC_NONE) ? tsc->getSCE(type) : nullptr
        let tsce = crate::battle::mobif::has_sc(target.kind) && type_ != SC_NONE && sc::has_sce(c, target.id, type_);

        if crate::battle::mobif::has_mode(tstatus.mode, MD_STATUSIMMUNE) || sc::battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) {
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }

        if tsce {
            //HelloKitty2 (?) explained that this silently fails when target is
            //already inflicted. [Skotlex]
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }

        //Has a 55% + skill_lv*5% success chance.
        let ok = sc::sc_start(c, Some(src), target, type_, 55 + 5 * skill_lv as i32, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        if !clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok) {
            if src.kind == BL_PC {
                skill_fail(c, src, id);
            }
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }

        unit_skillcastcancel(c, target.id, 0);

        if target.kind == BL_MOB {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let range = skill_get_range2(c, &s, id, skill_lv, true);
            crate::mob::ai::mob_target(c, target.id, &s, range);
        }
    }
}
