//! skills/mage/soulexhale.cpp: class SkillSoulExhale : public SkillImpl (PF_SOULCHANGE).

use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::status::consts::{SC_EXTREMITYFIST, SC_NORECOVER_STATE};
use crate::status::hp::{status_heal, status_set_sp};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSoulExhale;

impl SkillImpl for SkillSoulExhale {
    fn skill_id(&self) -> u16 {
        PF_SOULCHANGE
    }

    /// SkillSoulExhale::castendNoDamageId: on a monster (once per monster: soul_change_flag) the caster gets 3% of
    /// its max SP; otherwise caster and target swap SP (RENEWAL: half of each).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let sstatus = crate::status::status_get_status_data(c, src);
        let tstatus = crate::status::status_get_status_data(c, target);
        let has_tsc = crate::battle::mobif::has_sc(target.kind);
        let is_sd = src.kind == BL_PC;
        let mut sp1: u32;
        let mut sp2: u32;

        if target.kind == BL_MOB {
            if let Some(mut dstmd) = crate::mob::get(c, target.id) {
                if dstmd.soul_change_flag {
                    if is_sd {
                        skill_fail(c, src, id);
                    }
                    return;
                }

                dstmd.soul_change_flag = true;
                crate::mob::put(c, &dstmd);
                sp2 = sstatus.max_sp.wrapping_mul(3) / 100;
                status_heal(c, src.id, 0, sp2 as i64, 0, 2);
                clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
                return;
            }
        }

        sp1 = sstatus.sp;
        sp2 = tstatus.sp;
        // RENEWAL
        sp1 /= 2;
        sp2 /= 2;
        if has_tsc && sc::has_sce(c, target.id, SC_EXTREMITYFIST) {
            sp1 = tstatus.sp;
        }
        if has_tsc && sc::has_sce(c, target.id, SC_NORECOVER_STATE) {
            sp1 = tstatus.sp;
        }

        status_set_sp(c, src.id, sp2, 3);
        status_set_sp(c, target.id, sp1, 3);
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
    }
}
