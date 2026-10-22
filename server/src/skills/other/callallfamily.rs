//! skills/other/callallfamily.cpp: class SkillCallAllFamily : public SkillImpl (WE_CALLALLFAMILY).

use super::{pc_get_child, pc_get_partner, sd_status};
use crate::db::skills::*;
use crate::skill::clif::skill_fail;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::SkillImpl;
use crate::unit::{Unit, CLR_TELEPORT};
use crate::world::{Ctx, Tick};

pub struct SkillCallAllFamily;

impl SkillImpl for SkillCallAllFamily {
    fn skill_id(&self) -> u16 {
        WE_CALLALLFAMILY
    }

    /// SkillCallAllFamily::castendNoDamageId: fails (requirements kept) with no family member online; the partner and
    /// the child, when alive, on the caster's map and in the caster's party, are warped onto the caster's cell.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, _skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let Some(ch) = sd_status(c, src) else { return };
        let p_sd = pc_get_partner(c, src);
        let c_sd = pc_get_child(c, src);
        if p_sd.is_none() && c_sd.is_none() {
            // Fail if no family members are found
            skill_fail(c, src, self.skill_id());
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        // Partner must be on the same map and in same party
        // Child must be on the same map and in same party as the parent casting
        for member in [p_sd, c_sd].into_iter().flatten() {
            let member = crate::unit::get(c, member.id).unwrap_or(member);
            let party = crate::pc::character_of(c, member.id).map(|m| m.party_id).unwrap_or(0);
            if !crate::status::sc::status_isdead(c, &member) && member.map == src.map && party == ch.party_id {
                crate::pc::setpos(c, member.id, src.map, src.x, src.y, CLR_TELEPORT);
            }
        }
    }
}
