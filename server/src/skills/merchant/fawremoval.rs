//! skills/merchant/fawremoval.cpp: class SkillFawRemoval : public SkillImpl (NC_DISJOINT).

use super::ids::{MOBID_MAGICDECOY_WIND, MOBID_SILVERSNIPER};
use crate::db::skills::*;
use crate::map::BL_MOB;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillFawRemoval::SkillFawRemoval() : SkillImpl(NC_DISJOINT).
pub struct SkillFawRemoval;

impl SkillImpl for SkillFawRemoval {
    fn skill_id(&self) -> u16 {
        NC_DISJOINT
    }

    /// SkillFawRemoval::castendNoDamageId: on a monster only; a F.A.W. (Silver Sniper .. Magic Decoy Wind) dies
    /// (status_kill), then the skill packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if target.kind != BL_MOB {
            return;
        }
        if let Some(md) = crate::mob::get(c, target.id) {
            if md.mob_id >= MOBID_SILVERSNIPER && md.mob_id <= MOBID_MAGICDECOY_WIND {
                crate::status::hp::status_kill(c, target.id);
            }
        }
        // (a killed monster keeps its last state for the packet: map_freeblock_lock)
        let t = unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, self.skill_id(), skill_lv as i32, true);
    }
}
