//! skills/acolyte/reparatio.cpp: class SkillReparatio : public SkillImpl (CD_REPARATIO).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillReparatio;

impl SkillImpl for SkillReparatio {
    fn skill_id(&self) -> u16 {
        CD_REPARATIO
    }

    /// SkillReparatio::castendNoDamageId: a player target is healed to full (0 when immune); only works on players.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let tstatus = crate::status::status_get_status_data(c, target);
        if target.kind != BL_PC {
            // Only works on players.
            if src.kind == BL_PC {
                skill_fail(c, src, id);
            }
            return;
        }
        let mut heal_amount: i32 = 0;
        if sc::status_isimmune(c, target) == 0 {
            heal_amount = tstatus.max_hp as i32;
        }
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, None, &t, AL_HEAL, heal_amount, true);
        crate::status::hp::status_heal(c, target.id, heal_amount as i64, 0, 0, 0);
    }
}
