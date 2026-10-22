//! skills/npc/leash.cpp: class SkillLeash : public SkillImpl (NPC_LEASH), rAthena e985006.

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::misc::skill_check_unit_movepos;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillLeash;

impl SkillImpl for SkillLeash {
    fn skill_id(&self) -> u16 {
        NPC_LEASH
    }

    /// SkillLeash::castendNoDamageId: the target is pulled onto the caster's cell.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        if !skill_check_unit_movepos(c, 0, target, src.x, src.y, 1, true) {
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        crate::battle::clif::clif_blown(c, &t);
    }
}
