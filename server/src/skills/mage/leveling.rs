//! skills/mage/leveling.cpp: class SkillLeveling : public SkillImpl (SA_LEVELUP).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillLeveling;

impl SkillImpl for SkillLeveling {
    fn skill_id(&self) -> u16 {
        SA_LEVELUP
    }

    /// SkillLeveling::castendNoDamageId: `if (sd && pc_nextbaseexp(sd)) pc_gainexp(sd, nullptr,
    /// pc_nextbaseexp(sd) * 10 / 100, 0, 0)` (t_exp: uint64 math).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        if src.kind != BL_PC {
            return;
        }
        let Some(ch) = crate::pc::character_of(c, src.id) else { return };
        // pc_nextbaseexp(sd): sd->status.class_ / base_level
        let next = crate::db::jobs::pc_nextbaseexp(ch.class, ch.base_level as u32);
        if next != 0 {
            crate::status::progress::pc_gainexp(c, src.id, None, next * 10 / 100, 0, 0);
        }
    }
}
