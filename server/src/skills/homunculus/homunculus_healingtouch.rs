//! skills/homunculus/homunculus_healingtouch.cpp: class SkillHealingTouch : public SkillImpl (HLIF_HEAL).

use super::fresh;
use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_BERSERK, SC_BITESCAR, SC_KAITE, SC_SATURDAYNIGHTFEVER};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// conf/battle/exp.conf `heal_exp: 0` (the job experience a player earns healing another player, in %).
pub fn heal_exp() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::HEAL_EXP)
}

pub struct SkillHealingTouch;

impl SkillImpl for SkillHealingTouch {
    fn skill_id(&self) -> u16 {
        HLIF_HEAL
    }

    /// SkillHealingTouch::castendNoDamageId: skill_calc_heal on the target (the master: unit_skilluse_id2's
    /// homunculus auto-target); nothing on an immune unit, the Emperium or a battleground monster; Kaite bounces it
    /// back to the caster (voided on oneself), Berserk / Saturday Night Fever void it; SC_BITESCAR ends; the heal
    /// packet, status_heal, and (a player healing another player, heal_exp > 0) the job experience.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        // sd = BL_CAST(BL_PC, src): nullptr for the homunculus casting it
        let sd = (src.kind == BL_PC).then_some(src.id);
        let mut dstsd = (target.kind == BL_PC).then_some(target.id);
        let dstmd = target.kind == BL_MOB;
        let tsc = crate::battle::bl::sc(c, target);
        let sstatus = crate::status::status_get_status_data(c, src);
        let mut heal = crate::skill::misc::skill_calc_heal(c, src, Some(target), id, skill_lv, true);
        let tstatus = crate::status::status_get_status_data(c, target);
        if sc::status_isimmune(c, target) != 0
            || (dstmd && (crate::battle::bl::class_of(c, target) == crate::db::mobs::MOBID_EMPERIUM as i32 || tstatus.class_ as i32 == crate::status::defs::CLASS_BATTLEFIELD))
        {
            heal = 0;
        }
        let mut target = target.clone();
        if let Some(tsc) = tsc.filter(|s| !s.empty()) {
            if tsc.has(SC_KAITE) && sstatus.mode & crate::status::defs::MD_STATUSIMMUNE == 0 {
                // Bounce back heal
                if let Some(mut kaite) = sc::get_sce(c, target.id, SC_KAITE) {
                    kaite.val2 -= 1;
                    if kaite.val2 <= 0 {
                        sc::put_sce(c, &kaite);
                        sc::status_change_end(c, target.id, SC_KAITE, 0);
                    } else {
                        sc::put_sce(c, &kaite);
                    }
                }
                if src.id == target.id {
                    heal = 0; // When you try to heal yourself under Kaite, the heal is voided.
                } else {
                    target = fresh(c, src);
                    dstsd = sd;
                }
            } else if tsc.has(SC_BERSERK) || tsc.has(SC_SATURDAYNIGHTFEVER) {
                heal = 0; // Needed so that it actually displays 0 when healing.
            }
        }
        sc::status_change_end(c, target.id, SC_BITESCAR, 0);
        let t = fresh(c, &target);
        clif_skill_nodamage(c, Some(src), &t, id, heal, true);
        let mut heal_get_jobexp = crate::status::hp::status_heal(c, t.id, heal as i64, 0, 0, 0) as i64;
        if let (Some(sd), Some(dstsd)) = (sd, dstsd) {
            if heal > 0 && sd != dstsd && heal_exp() > 0 {
                heal_get_jobexp = heal_get_jobexp * heal_exp() as i64 / 100;
                if heal_get_jobexp <= 0 {
                    heal_get_jobexp = 1;
                }
                crate::status::progress::pc_gainexp(c, sd, Some(&fresh(c, &t)), 0, heal_get_jobexp as u64, 0);
            }
        }
    }
}
