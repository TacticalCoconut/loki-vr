//! skills/mage/monsterchant.cpp: class SkillMonsterChant : public SkillImpl (SA_SUMMONMONSTER).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMonsterChant;

impl SkillImpl for SkillMonsterChant {
    fn skill_id(&self) -> u16 {
        SA_SUMMONMONSTER
    }

    /// SkillMonsterChant::castendNoDamageId: a player summons one random monster (mob_id -1) on its own cell.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        if src.kind == BL_PC {
            // mob_once_spawn(sd, src->m, src->x, src->y, "--ja--", -1, 1, "", SZ_SMALL, AI_NONE)
            crate::mob::spawn::mob_once_spawn(
                c,
                Some(src),
                src.map as i32,
                src.x,
                src.y,
                Some("--ja--"),
                -1,
                1,
                Some(""),
                crate::status::defs::SZ_SMALL as u8,
                crate::mob::AI_NONE,
            );
        }
    }
}
