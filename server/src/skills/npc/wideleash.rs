//! skills/npc/wideleash.cpp: class SkillWideLeash : public SkillImpl (NPC_WIDELEASH).

use super::widebleeding::wide_area;
use crate::db::skills::*;
use crate::skill::misc::skill_check_unit_movepos;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillWideLeash;

impl SkillImpl for SkillWideLeash {
    fn skill_id(&self) -> u16 {
        NPC_WIDELEASH
    }

    /// SkillWideLeash::castendNoDamageId: `flag & 1` (a target of the area): the target is pulled onto the caster's
    /// cell (skill_check_unit_movepos(0, target, src->x, src->y, 1, 1), then clif_blown); else the area
    /// (widebleeding.rs wide_area).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        if *flag & 1 != 0 {
            if !skill_check_unit_movepos(c, 0, target, src.x, src.y, 1, true) {
                *flag |= SKILL_NOCONSUME_REQ;
                return;
            }
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            crate::battle::clif::clif_blown(c, &t);
        } else {
            wide_area(c, self.skill_id(), src, target, skill_lv, tick, *flag);
        }
    }
}
