//! skills/other/partyflee.cpp: class SkillPartyFlee : public StatusSkillImpl (ALL_PARTYFLEE).

use super::sd_status;
use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::skill_fail;
use crate::skills::novice::party_foreachsamemap_area_sub;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillPartyFlee;

impl SkillImpl for SkillPartyFlee {
    fn skill_id(&self) -> u16 {
        ALL_PARTYFLEE
    }

    /// SkillPartyFlee::castendNoDamageId: a player's first call fails without a party (requirements consumed), else
    /// runs on every party member within the splash range; the party call (and a non-player's cast) is
    /// StatusSkillImpl's.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        match sd_status(c, src) {
            Some(ch) if *flag & 1 == 0 => {
                if ch.party_id == 0 {
                    skill_fail(c, src, id);
                    return;
                }
                let f = *flag | BCT_PARTY | 1;
                party_foreachsamemap_area_sub(c, src, skill_get_splash(id, skill_lv) as i16, src, id, skill_lv, tick, f, skill_castend_nodamage_id);
            }
            _ => status_castend_nodamage_id(c, id, false, src, target, skill_lv),
        }
    }
}
