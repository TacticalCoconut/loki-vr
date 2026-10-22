//! skills/mage/grimreaper.cpp: class SkillGrimReaper : public SkillImpl (SA_DEATH).

use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::status::defs::MD_STATUSIMMUNE;
use crate::status::hp::status_kill;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGrimReaper;

impl SkillImpl for SkillGrimReaper {
    fn skill_id(&self) -> u16 {
        SA_DEATH
    }

    /// SkillGrimReaper::castendNoDamageId: kills the target; a player's cast fails on a status immune monster.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind == BL_PC && target.kind == BL_MOB {
            // status_has_mode(&dstmd->status, MD_STATUSIMMUNE)
            if let Some(md) = crate::mob::get(c, target.id) {
                if crate::mob::has_mode(&md, MD_STATUSIMMUNE) {
                    skill_fail(c, src, id);
                    return;
                }
            }
        }
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        status_kill(c, target.id);
    }
}
