//! skills/acolyte/coluceoheal.cpp: class SkillColuceoHeal : public SkillImpl (AB_CHEAL).

use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::misc::skill_calc_heal;
use crate::skills::novice::{party_foreachsamemap_area_sub, pc_checkskill, sce};
use crate::skills::SkillImpl;
use crate::status::consts::{SC_AKAITSUKI, SC_BERSERK};
use crate::status::sc::{self, battle_check_undead};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillColuceoHeal;

impl SkillImpl for SkillColuceoHeal {
    fn skill_id(&self) -> u16 {
        AB_CHEAL
    }

    /// SkillColuceoHeal::castendNoDamageId: the caster's Heal (its AL_HEAL level), +2.5% per party member on the map
    /// when more than one, on every party member in range (or the target alone); not on undead or berserk targets.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let sd = if src.kind == BL_PC { crate::pc::character_of(c, src.id) } else { None };
        match sd {
            Some(ch) if ch.party_id != 0 && *flag & 1 == 0 => {
                let f = *flag | BCT_PARTY | 1;
                party_foreachsamemap_area_sub(c, src, skill_get_splash(id, skill_lv) as i16, src, id, skill_lv, tick, f, skill_castend_nodamage_id);
            }
            None => {} // (!sd: the heal needs sd)
            Some(ch) => {
                let tstatus = crate::status::status_get_status_data(c, target);
                // (`!tsc->getSCE(SC_BERSERK)`: the target always has a status_change here)
                if battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) || sce(c, target, SC_BERSERK).is_some() {
                    return;
                }
                let partycount = if ch.party_id != 0 { party_count_samemap(c, src) } else { 0 };
                let mut i = skill_calc_heal(c, src, Some(target), AL_HEAL, pc_checkskill(c, src, AL_HEAL) as u16, true);
                if partycount > 1 {
                    i += (i / 100) * (partycount * 10) / 4;
                }
                if sc::status_isimmune(c, target) != 0 {
                    i = 0; // Should heal by 0 or won't do anything?? in iRO it breaks the healing to members.. [malufett]
                }
                clif_skill_nodamage(c, Some(src), target, id, i, true);
                if sce(c, target, SC_AKAITSUKI).is_some() && i != 0 {
                    i = (!i).wrapping_add(1);
                }
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                crate::status::hp::status_heal(c, t.id, i as i64, 0, 0, 0);
            }
        }
    }
}

/// `party_foreachsamemap(party_sub_count, sd, 0)`: the party members on sd's map who count (party.cpp
/// party_sub_count: not autotrading, not idle with idle_no_share).
pub(crate) fn party_count_samemap(c: &Ctx, sd: &Unit) -> i32 {
    crate::party::party_foreachsamemap(c, sd, 0).iter().map(|u| crate::party::party_sub_count(c, u.id)).sum()
}
