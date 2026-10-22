//! skills/npc/increasedgravity.cpp: class SkillIncreasedGravity : public SkillImpl (NPC_GRADUAL_GRAVITY), rAthena
//! e985006.

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::defs::{SCSTART_NOAVOID, SCSTART_NORATEDEF, SCSTART_NOTICKDEF};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillIncreasedGravity;

impl SkillImpl for SkillIncreasedGravity {
    fn skill_id(&self) -> u16 {
        NPC_GRADUAL_GRAVITY
    }

    /// SkillIncreasedGravity::castendNoDamageId: the skill's status at 100% that no resistance changes.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        sc::status_change_start(c, Some(src), target, skill_get_sc(id) as i32, 10000, skill_lv as i32, 0, 0, 0, skill_get_time(id, skill_lv) as i64, SCSTART_NOAVOID | SCSTART_NOTICKDEF | SCSTART_NORATEDEF, 0);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
    }
}
