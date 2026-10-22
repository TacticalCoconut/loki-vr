//! skills/npc/metamorphosis.cpp: class SkillMetamorphosis : public SkillImpl (NPC_METAMORPHOSIS), rAthena e985006;
//! and mob.cpp mob_random_class, which the module had no port of (mob/ is its natural home; transformation.rs's
//! NPC_TRANSFORMATION calls it too).

use crate::db::mobs::mobdb_checkid;
use crate::db::skills::*;
use crate::map::BL_MOB;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMetamorphosis;

impl SkillImpl for SkillMetamorphosis {
    fn skill_id(&self) -> u16 {
        NPC_METAMORPHOSIS
    }

    /// SkillMetamorphosis::castendNoDamageId: the monster turns into one of the monsters of its mob_skill_db entry
    /// (val[0..4]); above level 1 it also summons skill_lv - 1 of them. [Skotlex]
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
            let val = ms.val;
            let class_ = mob_random_class(c, &val, 0);
            if skill_lv > 1 {
                // Multiply the rest of mobs. [Skotlex]
                crate::mob::spawn::mob_summonslave(c, md.id, &val, skill_lv as i32 - 1, self.skill_id());
            }
            if class_ != 0 {
                crate::mob::dead::mob_class_change(c, md.id, class_);
            }
        }
    }
}

/// mob.cpp mob_random_class: a random valid monster of `value` (count 0: the first valid ones of the list, at most
/// 5; else the first `count`, when value[0] is valid), 0 when there is none. One rnd() roll when an id is picked.
pub(crate) fn mob_random_class(c: &Ctx, value: &[i32], count: usize) -> i32 {
    let mut count = count;
    // no count specified, look into the array manually, but take only max 5 elements
    if count < 1 {
        count = 0;
        while count < 5 && value.get(count).map(|&v| mobdb_checkid(v) != 0).unwrap_or(false) {
            count += 1;
        }
        if count < 1 {
            return 0; // nothing found
        }
    } else {
        // check if at least the first value is valid
        if value.first().map(|&v| mobdb_checkid(v)).unwrap_or(0) == 0 {
            return 0;
        }
    }
    // Pick a random value, hoping it exists. [Skotlex]
    let i = c.rnd() as usize % count;
    value.get(i).map(|&v| mobdb_checkid(v)).unwrap_or(0)
}
