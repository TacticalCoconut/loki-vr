//! skills/mage/golddigger.cpp: class SkillGoldDigger : public SkillImpl (SA_FORTUNE).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGoldDigger;

impl SkillImpl for SkillGoldDigger {
    fn skill_id(&self) -> u16 {
        SA_FORTUNE
    }

    /// SkillGoldDigger::castendNoDamageId: the caster gets the target's level x 100 zeny.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        if src.kind == BL_PC {
            // pc_getzeny(sd, status_get_lv(target) * 100, LOG_TYPE_STEAL): int32 math
            let zeny = crate::status::status_get_lv(c, target).wrapping_mul(100);
            crate::inventory::pc_getzeny(c, src.id, zeny as i64, crate::map_log::LOG_TYPE_STEAL, 0);
        }
    }
}
