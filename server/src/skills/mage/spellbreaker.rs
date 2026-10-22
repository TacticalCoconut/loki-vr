//! skills/mage/spellbreaker.cpp: class SkillSpellBreaker : public SkillImpl (SA_SPELLBREAKER).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::unit::unit_skillcastcancel;
use crate::skills::SkillImpl;
use crate::status::consts::SC_MAGICROD;
use crate::status::defs::MD_STATUSIMMUNE;
use crate::status::hp::{status_heal, status_percent_damage, status_zap};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillSpellBreaker;

impl SkillImpl for SkillSpellBreaker {
    fn skill_id(&self) -> u16 {
        SA_SPELLBREAKER
    }

    /// SkillSpellBreaker::castendNoDamageId (RENEWAL: no HP damage on status immune targets).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let tstatus = crate::status::status_get_status_data(c, target);
        let is_sd = src.kind == BL_PC;
        let is_dstsd = target.kind == BL_PC;

        let sp: i32;
        if is_dstsd && sc::has_sce(c, target.id, SC_MAGICROD) {
            // If target enemy player has Magic Rod, then 20% of your SP is transferred to that player
            // status_percent_damage(target, src, 0, -20, false): 20% of the caster's max SP
            sp = status_percent_damage(c, Some(target), src.id, 0, -20, 0, false);
            status_heal(c, target.id, 0, sp as i64, 0, 2);
        } else {
            // unit_bl2ud(target): the target's cast (ud->skilltimer, skill_id, skill_lv)
            let tnow = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            if !crate::skillunit::group::has_ud(tnow.kind) || tnow.ud.skilltimer == 0 {
                return; //Nothing to cancel.
            }
            // ud->skill_id / skill_lv, read below after unit_skillcastcancel (which leaves them as they are)
            let (ud_skill_id, ud_skill_lv) = (tnow.ud.skill_id, tnow.ud.skill_lv);
            let mut hp: i32 = 0;
            if crate::battle::mobif::has_mode(tstatus.mode, MD_STATUSIMMUNE) {
                //Only 10% success chance against status immune. [Skotlex]
                if c.rnd_chance(90, 100) {
                    if is_sd {
                        skill_fail(c, src, id);
                    }
                    return;
                }
            } else if skill_lv >= 5 && (!is_dstsd || crate::npc::mapflag_vs(c, target.map)) {
                // HP damage does not work on bosses in renewal
                //HP damage only on pvp-maps when against players.
                hp = (tstatus.max_hp / 50) as i32; //Siphon 2% HP at level 5
            }

            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            unit_skillcastcancel(c, target.id, 0);
            sp = skill_get_sp(ud_skill_id, ud_skill_lv);
            status_zap(c, target.id, 0, sp as i64, 0);
            // Recover some of the SP used
            status_heal(c, src.id, 0, (sp.wrapping_mul(25 * (skill_lv as i32 - 1)) / 100) as i64, 0, 2);

            // If damage would be lethal, it does not deal damage
            // (tstatus is the pointer to the target's live status: its hp is read now)
            let t_hp = crate::status::status_data_of(c, target.id).map(|s| s.hp).unwrap_or(tstatus.hp) as i64;
            if hp != 0 && (hp as i64) < t_hp {
                let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                crate::battle::clif::clif_damage(c, &s, &t, tick, 0, 0, hp as i64, 0, DMG_NORMAL, 0, false);
                status_zap(c, target.id, hp as i64, 0, 0);
                // Recover 50% of damage dealt
                status_heal(c, src.id, (hp / 2) as i64, 0, 0, 2);
            }
        }
    }
}
