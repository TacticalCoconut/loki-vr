//! skills/npc/randommove.cpp: class SkillRandomMove : public SkillImpl (NPC_RANDOMMOVE).

use super::npcrun::unit_escape;
use crate::db::skills::*;
use crate::map::BL_MOB;
use crate::mob::MOB_SKILL_INTERVAL;
use crate::skill::clif::clif_skillcasting;
use crate::skills::SkillImpl;
use crate::status::defs::ELE_FIRE;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRandomMove;

impl SkillImpl for SkillRandomMove {
    fn skill_id(&self) -> u16 {
        NPC_RANDOMMOVE
    }

    /// SkillRandomMove::castendNoDamageId: a monster walks away from the target while showing a fake cast bar
    /// (trickcasting: the mob_skill_db condition `trickcasting` and NPC_SPEEDUP read it).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        // mob_data* md = BL_CAST(BL_MOB, src)
        if src.kind != BL_MOB {
            return;
        }
        let Some(mut md) = crate::mob::get(c, src.id) else { return };
        // This skill creates fake casting state where a monster moves while showing a cast bar
        let tricktime: i32 = MOB_SKILL_INTERVAL as i32 * 3;
        md.trickcasting = tick + tricktime as Tick;
        crate::mob::put(c, &md);
        clif_skillcasting(c, src, Some(src), 0, 0, self.skill_id(), skill_lv, ELE_FIRE, tricktime + MOB_SKILL_INTERVAL as i32 / 2);
        // Monster cannot be stopped while moving
        if let Some(mut md) = crate::mob::get(c, src.id) {
            md.can_escape = true;
            crate::mob::put(c, &md);
        }
        // Move up to 8 cells
        unit_escape(c, src.id, target, 8, 3);
    }
}
