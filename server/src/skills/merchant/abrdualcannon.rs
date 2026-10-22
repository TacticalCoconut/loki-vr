//! skills/merchant/abrdualcannon.cpp: class SkillAbrDualCannon : public SkillImpl (MT_SUMMON_ABR_DUAL_CANNON).

use crate::db::skills::*;
use crate::mob::spawn::mob_once_spawn_sub;
use crate::mob::AI_ABR;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::merchant::summon_attach_and_spawn;
use crate::skills::SkillImpl;
use crate::status::defs::SZ_SMALL;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// mob.hpp MOBID_ABR_DUAL_CANNON.
pub const MOBID_ABR_DUAL_CANNON: i32 = 20835;

/// SkillAbrDualCannon::SkillAbrDualCannon() : SkillImpl(MT_SUMMON_ABR_DUAL_CANNON).
pub struct SkillAbrDualCannon;

impl SkillImpl for SkillAbrDualCannon {
    fn skill_id(&self) -> u16 {
        MT_SUMMON_ABR_DUAL_CANNON
    }

    /// SkillAbrDualCannon::castendNoDamageId: the skill packet, the skill's status on the target, then an ABR Dual
    /// Cannon on the caster's cell (AI_ABR, master = the caster), deleted after skill_get_time
    /// (add_timer(gettick() + …, mob_timer_delete)).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let src = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let md = mob_once_spawn_sub(c, Some(&src), src.map, src.x, src.y, Some("--ja--"), MOBID_ABR_DUAL_CANNON, Some(""), SZ_SMALL as u8, AI_ABR);
        if let Some(md) = md {
            // md->master_id = src->id; md->special_state.ai = AI_ABR; deletetimer; mob_spawn(md)
            summon_attach_and_spawn(c, md, Some(src.id), AI_ABR as i32, c.tick() + skill_get_time(id, skill_lv) as Tick);
        }
    }
}

#[cfg(test)]
mod tests {
    /// mob.hpp's MOBID_ABR_DUAL_CANNON is the monster database's ABR_DUAL_CANNON.
    #[test]
    fn mob_id_matches_the_monster_database() {
        let m = crate::db::mobs::mob_db().find(super::MOBID_ABR_DUAL_CANNON as u32).map(|m| m.sprite.clone());
        assert_eq!(m.as_deref(), Some("ABR_DUAL_CANNON"));
    }
}
