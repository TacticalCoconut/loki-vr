//! skills/acolyte/redemptio.cpp: class SkillRedemptio : public SkillImpl (PR_REDEMPTIO).

use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::npc::{mapflag, mapflag_gvg2, MF_BATTLEGROUND};
use crate::skill::area;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::skill_fail;
use crate::skills::novice::party_foreachsamemap_area_sub;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRedemptio;

impl SkillImpl for SkillRedemptio {
    fn skill_id(&self) -> u16 {
        PR_REDEMPTIO
    }

    /// SkillRedemptio::castendNoDamageId: the caster's cast revives every dead party member in range at
    /// Resurrection level 3 (skill_area_temp[0] counts them; none: the cast fails), then the caster drops to 1 HP.
    /// RENEWAL: no exp penalty and no SP loss (the `#ifndef RENEWAL` block).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let is_sd = src.kind == BL_PC;
        if is_sd && *flag & 1 == 0 {
            let party_id = crate::pc::character_of(c, src.id).map(|ch| ch.party_id).unwrap_or(0);
            if party_id == 0 {
                skill_fail(c, src, id);
                return;
            }
            area::set_temp(0, 0);
            let f = *flag | BCT_PARTY | 1;
            party_foreachsamemap_area_sub(c, src, skill_get_splash(id, skill_lv) as i16, src, id, skill_lv, tick, f, skill_castend_nodamage_id);
            let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
            if area::temp(0) == 0 {
                skill_fail(c, &s, id);
                return;
            }
            // (#ifndef RENEWAL: the exp penalty of exp_cost_redemptio and status_set_sp(src, 0, 0))
            crate::status::hp::status_set_hp(c, src.id, 1, 0);
            return;
        } else if !(sc::status_isdead(c, target) && *flag & 1 != 0) {
            // Invalid target, skip resurrection.
            return;
        }
        // Revive
        area::add_temp(0, 1); // Count it in, then fall-through to the Resurrection code.
        let skill_lv: u16 = 3; // Resurrection level 3 is used
        if is_sd && (mapflag_gvg2(c, target.map) || mapflag(c, target.map, MF_BATTLEGROUND)) {
            // No reviving in WoE grounds!
            skill_fail(c, src, id);
            return;
        }
        super::resurrection::revive(c, src, target, skill_lv);
    }
}
