//! skills/ninja/shadowhiding.cpp: class SkillShadowHiding : public SkillImpl (KO_YAMIKUMO), rAthena e985006.

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_NONE;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillShadowHiding;

impl SkillImpl for SkillShadowHiding {
    fn skill_id(&self) -> u16 {
        KO_YAMIKUMO
    }

    /// SkillShadowHiding::castendNoDamageId: a toggle; the packets carry level -1 (hide skill-scream animation), and
    /// ending the status keeps the requirements.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if type_ != SC_NONE && sce(c, target, type_).is_some() {
            let ended = sc::status_change_end(c, target.id, type_, 0) != 0;
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(src), &t, id, -1, ended); // Hide skill-scream animation.
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        let ok = sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, -1, ok);
    }
}
