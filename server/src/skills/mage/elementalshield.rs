//! skills/mage/elementalshield.cpp: class SkillElementalShield : public SkillImpl (SO_ELEMENTAL_SHIELD).
//!
//! The elemental itself (sd->ed, elemental_delete) is the mercelem agent's: see elem_iface.rs.

use super::elem_iface::{elemental_delete, sd_ed};
use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::iface;
use crate::skills::novice::party_foreachsamemap_area_sub;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillElementalShield;

impl SkillElementalShield {
    /// `if (sd->ed && skill_get_state(getSkillId()) == ST_ELEMENTALSPIRIT2) elemental_delete(sd->ed);`
    fn consume_elemental(c: &Ctx, aid: u32, id: u16) {
        if let Some(ed) = sd_ed(c, aid) {
            if skill_get_state(id) == ST_ELEMENTALSPIRIT2 {
                elemental_delete(c, ed);
            }
        }
    }
}

impl SkillImpl for SkillElementalShield {
    fn skill_id(&self) -> u16 {
        SO_ELEMENTAL_SHIELD
    }

    /// SkillElementalShield::castendNoDamageId: without a party (or on a party member, flag & 1, or a non-player
    /// caster), a Safety Wall (level lv + 5) and a Pneuma placed by the target on its own cell (a partyless caster
    /// first sends the packet and gives up its elemental); a caster in a party sends the packet, gives up its
    /// elemental and repeats the skill (flag | BCT_PARTY | 1) on every party member within the splash range.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // map_session_data* sd = BL_CAST(BL_PC, src); sd->status.party_id
        let party_id = if src.kind == BL_PC { crate::pc::character_of(c, src.id).map(|ch| ch.party_id) } else { None };

        match party_id {
            Some(pid) if pid != 0 && *flag & 1 == 0 => {
                clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
                Self::consume_elemental(c, src.id, id);
                let f = *flag | BCT_PARTY | 1;
                party_foreachsamemap_area_sub(c, src, skill_get_splash(id, skill_lv) as i16, src, id, skill_lv, tick, f, skill_castend_nodamage_id);
            }
            _ => {
                if party_id == Some(0) {
                    clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
                    Self::consume_elemental(c, src.id, id);
                }
                iface::skill_unitsetting(c, target, MG_SAFETYWALL, skill_lv + 5, target.x, target.y, 0);
                let target = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                iface::skill_unitsetting(c, &target, AL_PNEUMA, 1, target.x, target.y, 0);
            }
        }
    }
}
