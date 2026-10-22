//! skills/other/ilookuptoyou.cpp: class SkillILookUpToYou : public SkillImpl (WE_FEMALE).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::hp::status_heal;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillILookUpToYou;

impl SkillImpl for SkillILookUpToYou {
    fn skill_id(&self) -> u16 {
        WE_FEMALE
    }

    /// SkillILookUpToYou::castendNoDamageId: when the caster's SP is above max SP / the skill's SP rate (the uint8
    /// `abs(skill_get_sp_rate)`), the target recovers the same % of its own max SP [Skotlex]; the packet's level is
    /// what status_heal returned.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let tstatus = crate::status::status_get_status_data(c, target);
        let sstatus = crate::status::status_get_status_data(c, src);
        let sp_rate = skill_get_sp_rate(id, skill_lv).unsigned_abs() as u8;
        if sp_rate != 0 && sstatus.sp > sstatus.max_sp / sp_rate as u32 {
            // The earned is the same % of the target SP than it costed the caster. [Skotlex]
            let gain_sp = tstatus.max_sp.wrapping_mul(sp_rate as u32) / 100;
            let healed = status_heal(c, target.id, 0, gain_sp as i32 as i64, 0, 0);
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(src), &t, id, healed, true);
        }
    }
}
