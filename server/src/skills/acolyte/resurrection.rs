//! skills/acolyte/resurrection.cpp: class SkillResurrection : public SkillImpl (ALL_RESURRECTION).

use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::npc::{mapflag, mapflag_gvg2, MF_BATTLEGROUND, MF_PVP};
use crate::skill::attack::skill_attack;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_HELLPOWER;
use crate::status::sc::{self, battle_check_undead};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

use super::conf::resurrection_exp;

pub struct SkillResurrection;

impl SkillImpl for SkillResurrection {
    fn skill_id(&self) -> u16 {
        ALL_RESURRECTION
    }

    /// SkillResurrection::castendDamageId: the offensive cast on an undead target (skill_castend_nodamage_id
    /// redirects it).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let tstatus = crate::status::status_get_status_data(c, target);
        if !battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) {
            return;
        }
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }

    /// SkillResurrection::castendNoDamageId: no reviving in WoE grounds and battlegrounds.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if mapflag_gvg2(c, target.map) || mapflag(c, target.map, MF_BATTLEGROUND) {
            // No reviving in WoE grounds!
            if src.kind == BL_PC {
                skill_fail(c, src, self.skill_id());
            }
            return;
        }
        revive(c, src, target, skill_lv);
    }
}

/// The part SkillResurrection::castendNoDamageId and SkillRedemptio::castendNoDamageId share (their code from
/// `if (!status_isdead(*target)) return;` on is the same): Hell Power blocks the revival (the animation still
/// plays), a PvP player with negative points stays dead, the HP rate by level (10 / 30 / 50 / 80 %, or full with
/// restart_full_recover), the ALL_RESURRECTION animation, and battle_config.resurrection_exp's exp for the caster.
pub(crate) fn revive(c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16) {
    if !sc::status_isdead(c, target) {
        return;
    }
    let (mut per, mut sper): (i32, i32) = (0, 0);
    if sce(c, target, SC_HELLPOWER).is_some() {
        clif_skill_nodamage(c, Some(src), target, ALL_RESURRECTION, skill_lv as i32, true);
        return;
    }
    let dstsd = target.kind == BL_PC;
    if mapflag(c, target.map, MF_PVP) && dstsd && crate::battle::state::get(c, target.id).pvp_point < 0 {
        return;
    }
    match skill_lv {
        1 => per = 10,
        2 => per = 30,
        3 => per = 50,
        4 => per = 80,
        _ => {}
    }
    if dstsd && crate::status::data::sd_get(c, target.id).is_some_and(|sd| sd.special_state.restart_full_recover) {
        per = 100;
        sper = 100;
    }
    if crate::status::hp::status_revive(c, target.id, per as u8, sper as u8, 0) != 0 {
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        clif_skill_nodamage(c, Some(&s), &t, ALL_RESURRECTION, skill_lv as i32, true); // Both Redemptio and Res show this skill-animation.
        if src.kind == BL_PC && dstsd && resurrection_exp() > 0 {
            let (Some(sch), Some(dch)) = (crate::pc::character_of(c, src.id), crate::pc::character_of(c, target.id)) else { return };
            let (mut exp, mut jexp): (u64, u64) = (0, 0);
            let lv = dch.base_level as i32 - sch.base_level as i32;
            let jlv = dch.job_level as i32 - sch.job_level as i32;
            if lv > 0 && crate::db::jobs::pc_nextbaseexp(dch.class, dch.base_level as u32) != 0 {
                exp = res_exp(dch.base_exp, lv);
                if exp < 1 {
                    exp = 1;
                }
            }
            if jlv > 0 && crate::db::jobs::pc_nextjobexp(dch.class, dch.job_level as u32) != 0 {
                // (rAthena multiplies the job exp by the base level difference `lv`, not `jlv`: kept)
                jexp = res_exp(dch.job_exp, lv);
                if jexp < 1 {
                    jexp = 1;
                }
            }
            if exp > 0 || jexp > 0 {
                crate::status::progress::pc_gainexp(c, src.id, Some(&t), exp, jexp, 0);
            }
        }
    }
}

/// `(t_exp)(exp * lv * battle_config.resurrection_exp / 1000000.)`: the product is uint64 arithmetic (the int32
/// factors are converted to uint64 and the product wraps), only the division is in double.
fn res_exp(exp: u64, lv: i32) -> u64 {
    (exp.wrapping_mul(lv as i64 as u64).wrapping_mul(resurrection_exp() as i64 as u64) as f64 / 1000000.0) as u64
}
