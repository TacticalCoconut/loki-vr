//! skills/mage/sense.cpp: class SkillSense : public SkillImpl (WZ_ESTIMATION).

use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::{clif_skill_estimation, clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSense;

impl SkillImpl for SkillSense {
    fn skill_id(&self) -> u16 {
        WZ_ESTIMATION
    }

    /// SkillSense::castendNoDamageId: the monster's information window (clif_skill_estimation) to a player caster.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        // sd = BL_CAST(BL_PC, src), dstsd = BL_CAST(BL_PC, target), dstmd = BL_CAST(BL_MOB, target)
        if src.kind != BL_PC {
            return;
        }
        if target.kind == BL_PC {
            // Fail on Players
            skill_fail(c, src, id);
            return;
        }
        if target.kind == BL_MOB {
            clif_skill_estimation(c, src, target);
        }
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
    }
}
