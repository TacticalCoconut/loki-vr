//! skills/merchant/thewholeprotection.cpp: class SkillTheWholeProtection : public SkillImpl (BO_THE_WHOLE_PROTECTION).

use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::inventory::{EQP_ARMOR, EQP_HEAD_TOP, EQP_SHIELD, EQP_WEAPON};
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::party_foreachsamemap_area_sub;
use crate::skills::SkillImpl;
use crate::status::consts::SC_CP_WEAPON;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillTheWholeProtection::SkillTheWholeProtection() : SkillImpl(BO_THE_WHOLE_PROTECTION).
pub struct SkillTheWholeProtection;

impl SkillImpl for SkillTheWholeProtection {
    fn skill_id(&self) -> u16 {
        BO_THE_WHOLE_PROTECTION
    }

    /// SkillTheWholeProtection::castendNoDamageId: without a party (or for each member, flag & 1): SC_CP_WEAPON + i for
    /// every piece a player target wears (no packet); in a party: the skill packet, then the recursive call on every
    /// member within the splash (party_foreachsamemap, flag | BCT_PARTY | 1).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let party_id = if src.kind == BL_PC { crate::pc::character_of(c, src.id).map(|ch| ch.party_id).unwrap_or(0) } else { 0 };
        if src.kind != BL_PC || party_id == 0 || *flag & 1 != 0 {
            let equip = [EQP_WEAPON, EQP_SHIELD, EQP_ARMOR, EQP_HEAD_TOP];
            for (i_eqp, eqp) in equip.iter().enumerate() {
                if target.kind != BL_PC || crate::pc_equip::pc_checkequip(c, target.id, *eqp, false) < 0 {
                    continue;
                }
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                sc::sc_start(c, Some(src), &t, SC_CP_WEAPON + i_eqp as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
            }
        } else {
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            let f = *flag | BCT_PARTY | 1;
            party_foreachsamemap_area_sub(c, src, skill_get_splash(id, skill_lv) as i16, src, id, skill_lv, tick, f, skill_castend_nodamage_id);
        }
    }
}
