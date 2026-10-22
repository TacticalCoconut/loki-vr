//! skills/taekwon/soulcollect.cpp: class SkillSoulCollect : public SkillImpl (SP_SOULCOLLECT).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::pc_checkskill;
use crate::skills::SkillImpl;
use crate::status::consts::SC_NONE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSoulCollect;

impl SkillImpl for SkillSoulCollect {
    fn skill_id(&self) -> u16 {
        SP_SOULCOLLECT
    }

    /// SkillSoulCollect::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        // tsc != nullptr && type != SC_NONE ? tsc->getSCE(type)
        let tsce = crate::battle::mobif::has_sc(target.kind) && type_ != SC_NONE && sc::has_sce(c, target.id, type_);

        if tsce {
            let ended = sc::status_change_end(c, target.id, type_, 0) != 0;
            let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ended);
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }

        // pc_checkskill(sd, SP_SOULENERGY): 0 for a caster that is not a player
        let ok = sc::sc_start2(c, Some(src), target, type_, 100, skill_lv as i32, pc_checkskill(c, src, SP_SOULENERGY), skill_get_time(id, skill_lv) as i64);
        let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
    }
}
