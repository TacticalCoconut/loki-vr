//! skills/mage/indulge.cpp: class SkillIndulge : public SkillImpl (PF_HPCONVERSION).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::status::hp::{status_charge, status_heal};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillIndulge;

impl SkillImpl for SkillIndulge {
    fn skill_id(&self) -> u16 {
        PF_HPCONVERSION
    }

    /// SkillIndulge::castendNoDamageId: 10% of the caster's max HP becomes `skill_lv` times as much SP.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let sstatus = crate::status::status_get_status_data(c, src);
        // int32 hp = sstatus->max_hp / 10; int32 sp = hp * skill_lv;
        let hp = (sstatus.max_hp / 10) as i32;
        let sp = hp.wrapping_mul(skill_lv as i32);

        if status_charge(c, src.id, hp as i64, 0) == 0 {
            if src.kind == BL_PC {
                skill_fail(c, src, id);
            }
            return;
        }

        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        status_heal(c, target.id, 0, sp as i64, 0, 2);
    }
}
