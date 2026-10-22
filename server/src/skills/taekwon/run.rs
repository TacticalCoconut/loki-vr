//! skills/taekwon/run.cpp: class SkillRun : public SkillImpl (TK_RUN).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRun;

impl SkillImpl for SkillRun {
    fn skill_id(&self) -> u16 {
        TK_RUN
    }

    /// SkillRun::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let has_sc = crate::battle::mobif::has_sc(target.kind);
        if has_sc && type_ != crate::status::consts::SC_NONE && sc::has_sce(c, target.id, type_) {
            let ended = sc::status_change_end(c, target.id, type_, 0) != 0;
            let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ended);
            return;
        }
        // sc_start4(src, target, type, 100, skill_lv, unit_getdir(target), 0, 0, 0): no duration (the run ends by itself)
        let ok = sc::sc_start4(c, Some(src), target, type_, 100, skill_lv as i32, crate::unit::getdir(target) as i32, 0, 0, 0);
        let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
        if src.kind == BL_PC {
            // If the client receives a skill-use packet inmediately before a walkok packet, it will discard the walk packet! [Skotlex]
            let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
            crate::clif::walkok(c, &s); // So aegis has to resend the walk ok.
        }
    }
}
