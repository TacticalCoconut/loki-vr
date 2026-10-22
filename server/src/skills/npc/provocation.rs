//! skills/npc/provocation.cpp: class SkillProvocation : public SkillImpl (NPC_PROVOCATION).

use crate::db::skills::*;
use crate::map::BL_MOB;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillProvocation;

impl SkillImpl for SkillProvocation {
    fn skill_id(&self) -> u16 {
        NPC_PROVOCATION
    }

    /// SkillProvocation::castendNoDamageId: the animation; a monster caster drops its target.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        // mob_data* md = BL_CAST(BL_MOB, src)
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        if src.kind == BL_MOB {
            crate::mob::ai::mob_unlocktarget(c, src.id, tick);
        }
    }
}
