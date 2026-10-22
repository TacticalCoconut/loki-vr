//! skills/swordman/sacrifice.cpp: class SkillSacrifice : public SkillImpl (CR_DEVOTION); its devotion list packet is
//! skill/clif.rs clif_devotion.

use crate::db::jobs::{MAPID_CRUSADER, MAPID_SECONDMASK};
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::misc::skill_get_range2;
use crate::skill::state::{self, MAX_DEVOTION};
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_HELLPOWER;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// battle_config.devotion_level_difference (conf/battle/skill.conf `devotion_level_difference: 10`).
pub fn devotion_level_difference() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::DEVOTION_LEVEL_DIFFERENCE)
}

pub struct SkillSacrifice;

impl SkillImpl for SkillSacrifice {
    fn skill_id(&self) -> u16 {
        CR_DEVOTION
    }

    /// SkillSacrifice::castendNoDamageId: a player devotes another player (level difference, not devoted by
    /// someone else, not a crusader, not under Hell Power; a free slot of the first skill_lv ones).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != BL_PC {
            return;
        }
        let type_ = skill_get_sc(id) as i32;
        // map_session_data* dstsd = BL_CAST(BL_PC, target)
        let dst = if target.kind == BL_PC { crate::pc::character_of(c, target.id) } else { None };
        let Some(dstch) = dst else {
            // Only players can be devoted
            skill_fail(c, src, id);
            return;
        };
        let mut lv = crate::status::status_get_lv(c, src) - dstch.base_level as i32;
        if lv < 0 {
            lv = -lv;
        }
        let dclass = crate::status::data::sd_get(c, target.id).map(|s| s.class_).unwrap_or(0);
        if lv > devotion_level_difference() // Level difference requeriments
            || sce(c, target, type_).map(|e| e.val1 != src.id as i32).unwrap_or(false) // Cannot Devote a player devoted from another source
            || (dclass & MAPID_SECONDMASK) == MAPID_CRUSADER // Crusader Cannot be devoted
            || sce(c, target, SC_HELLPOWER).is_some()
        // Players affected by SC_HELLPOWER cannot be devoted.
        {
            skill_fail(c, src, id);
            return;
        }
        let count = (skill_lv as usize).min(MAX_DEVOTION);
        let mut st = state::get(c, src.id);
        let slot = |st: &state::PcSkill, i: usize| st.devotion.get(i).copied().unwrap_or(0);
        let mut i = (0..count).find(|&i| slot(&st, i) == target.id).unwrap_or(count);
        if i == count {
            i = (0..count).find(|&i| slot(&st, i) == 0).unwrap_or(count);
            if i == count {
                // No free slots, skill Fail
                skill_fail(c, src, id);
                return;
            }
        }
        if st.devotion.len() < MAX_DEVOTION {
            st.devotion.resize(MAX_DEVOTION, 0);
        }
        st.devotion[i] = target.id;
        state::put(c, &st);
        let range = skill_get_range2(c, src, id, skill_lv, true);
        let ok = sc::sc_start4(c, Some(src), target, type_, 10000, src.id as i32, i as i32, range, 0, skill_get_time2(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        crate::skill::clif::clif_devotion(c, &s, None);
    }
}
