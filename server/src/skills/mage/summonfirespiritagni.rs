//! skills/mage/summonfirespiritagni.cpp: class SkillSummonFireSpiritAgni : public SkillImpl (SO_SUMMON_AGNI).
//!
//! The elemental itself (sd->ed, elemental_delete, elemental_create) is the mercelem agent's: see elem_iface.rs.

use super::elem_iface::{elemental_create, elemental_delete, sd_ed};
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::misc::skill_get_elemental_type;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSummonFireSpiritAgni;

impl SkillImpl for SkillSummonFireSpiritAgni {
    fn skill_id(&self) -> u16 {
        SO_SUMMON_AGNI
    }

    /// SkillSummonFireSpiritAgni::castendNoDamageId: for a player, replaces its elemental with the spirit of the skill's level
    /// (skill_get_elemental_type) for skill_get_time; clif_skill_fail (USESKILL_FAIL_LEVEL) when it cannot be
    /// created, else the packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind != BL_PC {
            return;
        }
        let id = self.skill_id();
        let elemental_class = skill_get_elemental_type(id, skill_lv);

        // Remove previous elemental first.
        if let Some(ed) = sd_ed(c, src.id) {
            elemental_delete(c, ed);
        }

        // Summoning the new one.
        if elemental_create(c, src, elemental_class, skill_get_time(id, skill_lv) as u32) == 0 {
            skill_fail(c, src, id);
            return;
        }
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
    }
}
