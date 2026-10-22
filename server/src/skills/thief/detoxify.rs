//! skills/thief/detoxify.cpp: class SkillDetoxify : public SkillImpl (TF_DETOXIFY).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_DPOISON, SC_POISON};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDetoxify;

impl SkillImpl for SkillDetoxify {
    fn skill_id(&self) -> u16 {
        TF_DETOXIFY
    }

    /// SkillDetoxify::castendNoDamageId: the animation, then Poison and Deadly Poison end.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, bl: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), bl, self.skill_id(), skill_lv as i32, true);
        // status_change_end on a unit without statuses does nothing (status_get_sc == nullptr)
        if crate::battle::mobif::has_sc(bl.kind) {
            sc::status_change_end(c, bl.id, SC_POISON, 0);
            sc::status_change_end(c, bl.id, SC_DPOISON, 0);
        }
    }
}
