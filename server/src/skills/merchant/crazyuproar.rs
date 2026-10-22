//! skills/merchant/crazyuproar.cpp: class SkillCrazyUproar : public StatusSkillImpl (MC_LOUD); its castendNoDamageId
//! override is RENEWAL only (RENEWAL is defined).

use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skills::novice::party_foreachsamemap_area_sub;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillCrazyUproar::SkillCrazyUproar() : StatusSkillImpl(MC_LOUD).
pub struct SkillCrazyUproar;

impl SkillImpl for SkillCrazyUproar {
    fn skill_id(&self) -> u16 {
        MC_LOUD
    }

    /// SkillCrazyUproar::castendNoDamageId (RENEWAL): StatusSkillImpl's status on the target without a party (a
    /// monster caster, a party-less player, or a party member's recursive call: flag & 1), otherwise the recursive
    /// call on every party member in the splash range (SplashArea -1: AREA_SIZE).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // map_session_data *sd = BL_CAST(BL_PC, src)
        let sd = if src.kind == BL_PC { crate::pc::character_of(c, src.id) } else { None };
        match sd {
            Some(ch) if ch.party_id != 0 && *flag & 1 == 0 => {
                party_foreachsamemap_area_sub(c, src, skill_get_splash(id, skill_lv) as i16, src, id, skill_lv, tick, *flag | BCT_PARTY | 1, skill_castend_nodamage_id);
            }
            // StatusSkillImpl::castendNoDamageId (end_if_running = false)
            _ => status_castend_nodamage_id(c, id, false, src, target, skill_lv),
        }
    }
}
