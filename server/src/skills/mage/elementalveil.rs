//! skills/mage/elementalveil.cpp: class SkillElementalVeil : public SkillImpl (EM_ELEMENTAL_VEIL), rAthena e985006.
//!
//! The status goes on the player's elemental (`sd->ed`, a Diluvio .. Serpens one): the **mercelem** agent's
//! (elemental.cpp), read through skills/mage/elem_iface.rs, whose stubs answer "no elemental" until then (so the cast
//! ends in clif_skill_fail, as rAthena's does for a player without one).

use super::elem_iface::{ed_class, sd_ed};
use crate::db::misc::elemental::{ELEMENTALID_DILUVIO, ELEMENTALID_SERPENS};
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillElementalVeil;

impl SkillImpl for SkillElementalVeil {
    fn skill_id(&self) -> u16 {
        EM_ELEMENTAL_VEIL
    }

    /// SkillElementalVeil::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        // map_session_data* sd = BL_CAST(BL_PC, src);
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);

        if src.kind != BL_PC {
            return;
        }
        let sd = src;

        // sd->ed && sd->ed->elemental.class_ >= ELEMENTALID_DILUVIO && sd->ed->elemental.class_ <= ELEMENTALID_SERPENS
        // (mercelem agent: elemental.cpp)
        let ed = sd_ed(c, sd.id).filter(|&e| {
            let class_ = ed_class(c, e);
            (ELEMENTALID_DILUVIO..=ELEMENTALID_SERPENS).contains(&class_)
        });
        match ed {
            Some(ed_id) => {
                // sc_start(src, sd->ed, …): the elemental's block (BL_ELEM), valid while sd->ed is set
                if let Some(ed_bl) = unit::get(c, ed_id) {
                    sc::sc_start(c, Some(src), &ed_bl, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
                }
            }
            None => skill_fail(c, sd, id),
        }
    }
}
