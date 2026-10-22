//! skills/mage/summonelementalterremotus.cpp: class SkillSummonElementalTerremotus : public SkillImpl
//! (EM_SUMMON_ELEMENTAL_TERREMOTUS), rAthena e985006.
//!
//! Replaces the player's Tera (large) elemental by ELEMENTALID_TERREMOTUS. elemental_delete / elemental_create
//! and `sd->ed` are the **mercelem** agent's (elemental.cpp), called through skills/mage/elem_iface.rs, whose stubs
//! answer "no elemental" until then (so the cast ends in clif_skill_fail, as rAthena's does for a player without
//! one).

use super::elem_iface::{ed_class, elemental_create, elemental_delete, sd_ed};
use crate::db::misc::elemental::{ELEMENTALID_TERA_L, ELEMENTALID_TERREMOTUS};
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillSummonElementalTerremotus;

impl SkillImpl for SkillSummonElementalTerremotus {
    fn skill_id(&self) -> u16 {
        EM_SUMMON_ELEMENTAL_TERREMOTUS
    }

    /// SkillSummonElementalTerremotus::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        // map_session_data* sd = BL_CAST(BL_PC, src);
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);

        if src.kind != BL_PC {
            return;
        }
        let sd = src;

        // sd->ed && sd->ed->elemental.class_ == ELEMENTALID_TERA_L (mercelem agent: elemental.cpp)
        let ed = sd_ed(c, sd.id);
        if let Some(ed_id) = ed.filter(|&e| {
            let class_ = ed_class(c, e);
            class_ == ELEMENTALID_TERA_L
        }) {
            // Remove the old elemental before summoning the super one.
            elemental_delete(c, ed_id);
            let sd = unit::get(c, sd.id).unwrap_or_else(|| sd.clone());
            if elemental_create(c, &sd, ELEMENTALID_TERREMOTUS, skill_get_time(id, skill_lv) as u32) == 0 {
                skill_fail(c, &sd, id);
            } else {
                // Elemental summoned. Buff the player with the bonus.
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                sc::sc_start(c, Some(&sd), &t, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
            }
        } else {
            skill_fail(c, sd, id);
        }
    }
}
