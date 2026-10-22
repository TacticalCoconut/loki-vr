//! skills/acolyte/impositiomanus.cpp: class SkillImpositioManus : public SkillImpl (PR_IMPOSITIO).
//!
//! RENEWAL: the party cast below (`#ifdef RENEWAL`); pre-renewal the class is a StatusSkillImpl.

use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::party_foreachsamemap_area_sub;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillImpositioManus;

impl SkillImpl for SkillImpositioManus {
    fn skill_id(&self) -> u16 {
        PR_IMPOSITIO
    }

    /// SkillImpositioManus::castendNoDamageId: the status on the target alone (no player, no party, or a party member's
    /// recursive call), else on every party member within the splash range.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, bl: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // map_session_data *sd = BL_CAST(BL_PC, src)
        let sd = if src.kind == BL_PC { crate::pc::character_of(c, src.id) } else { None };
        match sd {
            Some(ch) if ch.party_id != 0 && *flag & 1 == 0 => {
                let f = *flag | BCT_PARTY | 1;
                party_foreachsamemap_area_sub(c, src, skill_get_splash(id, skill_lv) as i16, src, id, skill_lv, tick, f, skill_castend_nodamage_id);
            }
            _ => {
                let type_ = skill_get_sc(id) as i32;
                // Animations don't play when outside visible range
                if crate::mob::unit::check_distance_bl(src, bl, crate::conf::area_size() as i32) {
                    clif_skill_nodamage(c, Some(bl), bl, id, skill_lv as i32, true);
                }
                sc::sc_start(c, Some(src), bl, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
            }
        }
    }
}
