//! skills/mercenary/mercenary_sacrifice.cpp: class SkillMercenarySacrifice : public SkillImpl (ML_DEVOTION); its
//! devotion link packet is skill/clif.rs clif_devotion's BL_MER arm.

use crate::db::jobs::{MAPID_CRUSADER, MAPID_SECONDMASK};
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_devotion, clif_skill_nodamage};
use crate::skill::misc::skill_get_range2;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::sce;
use crate::skills::swordman::sacrifice::devotion_level_difference;
use crate::skills::SkillImpl;
use crate::status::consts::SC_HELLPOWER;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMercenarySacrifice;

impl SkillImpl for SkillMercenarySacrifice {
    fn skill_id(&self) -> u16 {
        ML_DEVOTION
    }

    /// SkillMercenarySacrifice::castendNoDamageId: a mercenary devotes its own master (level difference, not devoted
    /// by someone else, not a crusader, not under Hell Power); a refusal keeps the requirements (no fail packet).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        // s_mercenary_data* mer = BL_CAST(BL_MER, src); map_session_data* dstsd = BL_CAST(BL_PC, target)
        let mer = super::mer_data(c, src);
        let dstch = if target.kind == BL_PC { crate::pc::character_of(c, target.id) } else { None };
        let (Some(mut mer), Some(dstch)) = (mer, dstch) else {
            // Only players can be devoted
            return;
        };
        let mut lv = crate::status::status_get_lv(c, src) - dstch.base_level as i32;
        if lv < 0 {
            lv = -lv;
        }
        let dclass = crate::status::data::sd_get(c, target.id).map(|s| s.class_).unwrap_or(0);
        // mer != dstsd->md
        let dst_md = crate::mercenary::data::of_master(c, target.id).map(|m| m.id);
        if lv > devotion_level_difference() // Level difference requeriments
            || sce(c, target, type_).map(|e| e.val1 != src.id as i32).unwrap_or(false) // Cannot Devote a player devoted from another source
            || dst_md != Some(mer.id) // Mercenary only can devote owner
            || (dclass & MAPID_SECONDMASK) == MAPID_CRUSADER // Crusader Cannot be devoted
            || sce(c, target, SC_HELLPOWER).is_some()
        // Players affected by SC_HELLPOWER cannot be devoted.
        {
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        let i = 0;
        mer.devotion_flag = true; // Mercenary Devoting Owner
        crate::mercenary::data::put(c, &mer);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let range = skill_get_range2(c, &s, id, skill_lv, true);
        let ok = sc::sc_start4(c, Some(&s), target, type_, 10000, src.id as i32, i, range, 0, skill_get_time2(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        let s = unit::get(c, src.id).unwrap_or(s);
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, ok);
        let s = unit::get(c, src.id).unwrap_or(s);
        clif_devotion(c, &s, None);
    }
}
