//! skills/mercenary/mercenary_sense.cpp: class SkillMercenarySense : public SkillImpl (MER_ESTIMATION).

use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::{clif_skill_estimation, clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMercenarySense;

impl SkillImpl for SkillMercenarySense {
    fn skill_id(&self) -> u16 {
        MER_ESTIMATION
    }

    /// SkillMercenarySense::castendNoDamageId: the master sees the monster's estimation; fails on players.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        // s_mercenary_data* mer = BL_CAST(BL_MER, src); if( !mer ) return;
        if super::mer_data(c, src).is_none() {
            return;
        }
        // sd = mer->master; if( sd == nullptr ) return;
        let Some(sd) = super::mer_master(c, src) else { return };
        if target.kind == BL_PC {
            // Fail on Players
            skill_fail(c, &sd, id);
            return;
        }
        // if (dstmd != nullptr) clif_skill_estimation( *sd, *dstmd ) (skill/clif.rs)
        if target.kind == BL_MOB {
            clif_skill_estimation(c, &sd, target);
        }
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
    }
}
