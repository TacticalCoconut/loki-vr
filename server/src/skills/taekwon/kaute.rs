//! skills/taekwon/kaute.cpp: class SkillKaute : public SkillImpl (SP_KAUTE).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_fail, clif_skill_nodamage, fail::USESKILL_FAIL, skill_fail};
use crate::skills::taekwon::soul_linker_target_ok;
use crate::skills::SkillImpl;
use crate::status::consts::SC_STUN;
use crate::status::defs::SCSTART_NORATEDEF;
use crate::status::hp::{status_charge, status_heal};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillKaute;

impl SkillImpl for SkillKaute {
    fn skill_id(&self) -> u16 {
        SP_KAUTE
    }

    /// SkillKaute::castendNoDamageId: a player caster may only target the players of the soul linker rule (or the
    /// ones in Soul Unity); costs (10 + 2 * lv)% of the caster's MaxHP, gives the same share of the target's MaxSP.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let lv = skill_lv as i32;
        if !soul_linker_target_ok(c, src, target, true) {
            sc::status_change_start(c, Some(src), src, SC_STUN, 10000, lv, 0, 0, 0, 500, SCSTART_NORATEDEF, 0);
            skill_fail(c, src, id);
            return;
        }
        // sstatus->max_hp * (10 + 2 * skill_lv) / 100: uint32 arithmetic
        let sstatus = crate::status::status_get_status_data(c, src);
        let hp = sstatus.max_hp.wrapping_mul((10 + 2 * lv) as u32) / 100;
        if status_charge(c, src.id, hp as i64, 0) == 0 {
            if src.kind == BL_PC {
                clif_skill_fail(c, src, id, USESKILL_FAIL, 0, 0);
            }
            return;
        }
        let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, lv, true);
        let tstatus = crate::status::status_get_status_data(c, &t);
        let sp = tstatus.max_sp.wrapping_mul((10 + 2 * lv) as u32) / 100;
        status_heal(c, t.id, 0, sp as i64, 0, 2);
    }
}
