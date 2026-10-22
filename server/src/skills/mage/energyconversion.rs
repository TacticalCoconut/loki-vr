//! skills/mage/energyconversion.cpp: class SkillEnergyConversion : public SkillImpl (AG_ENERGY_CONVERSION), rAthena
//! e985006.

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_fail, clif_skill_nodamage, fail::USESKILL_FAIL};
use crate::skills::SkillImpl;
use crate::status::hp::status_heal;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillEnergyConversion;

impl SkillImpl for SkillEnergyConversion {
    fn skill_id(&self) -> u16 {
        AG_ENERGY_CONVERSION
    }

    /// SkillEnergyConversion::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        // map_session_data* sd = BL_CAST( BL_PC, src );
        let is_pc = src.kind == BL_PC;

        // status_get_sp(src) == status_get_max_sp(src)
        let sstatus = crate::status::status_get_status_data(c, src);
        if sstatus.sp == sstatus.max_sp {
            if is_pc {
                clif_skill_fail(c, src, self.skill_id(), USESKILL_FAIL, 0, 0);
            }
            return;
        }

        // Apply the SP gain to the caster
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        let lv = skill_lv as i64;
        // status_heal(target, 0, sp, 1): the 4-argument overload (hap 0, flag 1)
        status_heal(c, target.id, 0, (lv * (lv + 1) / 2) * 80, 0, 1);
    }
}
