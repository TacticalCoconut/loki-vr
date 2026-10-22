//! skills/archer/valleyofdeath.cpp: class SkillValleyOfDeath : public SkillImpl (WM_DEADHILLHERE).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillValleyOfDeath;

impl SkillImpl for SkillValleyOfDeath {
    fn skill_id(&self) -> u16 {
        WM_DEADHILLHERE
    }

    /// SkillValleyOfDeath::castendNoDamageId: a dead player comes back with HP = its SP (at least 1) and
    /// (60 - 10 * lv)% of its SP lost (the battle status written directly), the no-damage packet, pc_revive (the HP
    /// and SP updates) and clif_resurrection.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if target.kind != crate::map::BL_PC {
            return;
        }
        if !sc::status_isdead(c, target) {
            return;
        }
        crate::status::data::with_sd(c, target.id, |sd| {
            let st = &mut sd.battle_status;
            st.hp = st.sp.max(1);
            // tstatus->sp -= tstatus->sp * ( 60 - 10 * skill_lv ) / 100; (unsigned arithmetic)
            let cut = st.sp.wrapping_mul((60 - 10 * skill_lv as i32) as u32) / 100;
            st.sp = st.sp.wrapping_sub(cut);
        });
        crate::status::calc::sync_unit(c, target.id);
        clif_skill_nodamage(c, Some(src), &super::fresh(c, target), self.skill_id(), skill_lv as i32, true);
        crate::status::hp::pc_revive(c, target.id, 1, 1, 0);
        crate::status::clif::resurrection(c, &super::fresh(c, target));
    }
}
