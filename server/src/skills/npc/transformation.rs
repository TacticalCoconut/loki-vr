//! skills/npc/transformation.cpp: class SkillTransformation : public SkillImpl (NPC_TRANSFORMATION). mob.cpp
//! mob_random_class is metamorphosis.rs's (NPC_METAMORPHOSIS has the same body).

use super::metamorphosis::mob_random_class;
use crate::db::skills::*;
use crate::map::BL_MOB;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillTransformation;

impl SkillImpl for SkillTransformation {
    fn skill_id(&self) -> u16 {
        NPC_TRANSFORMATION
    }

    /// SkillTransformation::castendNoDamageId: the monster becomes a random one of its mob_skill_db entry's values
    /// (val[0..5]); from level 2 it also summons skill_lv - 1 of them.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        // mob_data* md = BL_CAST(BL_MOB, src)
        if src.kind != BL_MOB {
            return;
        }
        let Some(md) = crate::mob::get(c, src.id) else { return };
        if md.skill_idx < 0 {
            return;
        }
        // md->db->skill[md->skill_idx]->val
        let Some(db) = crate::mob::db(c, &md) else { return };
        let Some(ms) = db.skill.get(md.skill_idx as usize) else { return };
        let val = ms.val;
        let class_ = mob_random_class(c, &val, 0);
        if skill_lv > 1 {
            //Multiply the rest of mobs. [Skotlex]
            crate::mob::spawn::mob_summonslave(c, md.id, &val, skill_lv as i32 - 1, self.skill_id());
        }
        if class_ != 0 {
            crate::mob::dead::mob_class_change(c, md.id, class_);
        }
    }
}
