//! skills/summoner/silvervineroottwist.cpp: class SkillSilvervineRootTwist : public SkillImpl (SU_SV_ROOTTWIST).

use super::sd_status;
use crate::db::skills::*;
use crate::skill::clif::{clif_skill_fail, clif_skill_nodamage, fail::USESKILL_FAIL_TOTARGET};
use crate::skill::timerskill::skill_addtimerskill;
use crate::skills::novice::{pc_checkskill, sce};
use crate::skills::SkillImpl;
use crate::status::consts::SC_DORAM_MATK;
use crate::status::defs::CLASS_BOSS;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillSilvervineRootTwist;

impl SkillImpl for SkillSilvervineRootTwist {
    fn skill_id(&self) -> u16 {
        SU_SV_ROOTTWIST
    }

    /// SkillSilvervineRootTwist::castendNoDamageId: the packet; a player's cast on a boss fails (TOTARGET); a
    /// running status is only refreshed; a new one also starts Spirit of Land's SC_DORAM_MATK and queues the
    /// SU_SV_ROOTTWIST_ATK hit 1 s later.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let sd = sd_status(c, src);
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        if sd.is_some() && crate::battle::bl::class_type(c, target) == CLASS_BOSS {
            clif_skill_fail(c, src, id, USESKILL_FAIL_TOTARGET, 0, 0);
            return;
        }
        if sce(c, target, type_).is_some() {
            // Refresh the status only if it's already active.
            sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        } else {
            sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
            if let Some(ch) = &sd {
                if pc_checkskill(c, src, SU_SPIRITOFLAND) != 0 {
                    sc::sc_start(c, Some(src), src, SC_DORAM_MATK, 100, ch.base_level as i32, skill_get_time(SU_SPIRITOFLAND, 1) as i64);
                }
            }
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            skill_addtimerskill(c, &s, tick + 1000, target.id, 0, 0, SU_SV_ROOTTWIST_ATK, skill_lv, skill_get_type(SU_SV_ROOTTWIST_ATK), *flag);
        }
    }
}
