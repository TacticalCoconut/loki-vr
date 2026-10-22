//! skills/npc/deathsummon.cpp: class SkillDeathSummon : public SkillImpl (NPC_DEATHSUMMON), rAthena e985006.

use crate::db::skills::*;
use crate::map::BL_MOB;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDeathSummon;

impl SkillImpl for SkillDeathSummon {
    fn skill_id(&self) -> u16 {
        NPC_DEATHSUMMON
    }

    /// SkillDeathSummon::castendNoDamageId: the monster summons the monsters of its mob_skill_db entry (val[0..4]).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        // mob_data* md = BL_CAST(BL_MOB, src)
        if src.kind != BL_MOB {
            return;
        }
        let Some(md) = crate::mob::get(c, src.id) else { return };
        if md.skill_idx >= 0 {
            // md->db->skill[md->skill_idx]->val
            let Some(db) = crate::mob::db(c, &md) else { return };
            let Some(ms) = db.skill.get(md.skill_idx as usize) else { return };
            crate::mob::spawn::mob_summonslave(c, md.id, &ms.val, skill_lv as i32, self.skill_id());
        }
    }
}
