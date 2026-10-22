//! skills/other/iwillprotectyou.cpp: class SkillIWillProtectYou : public SkillImpl (WE_MALE).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::hp::status_heal;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillIWillProtectYou;

impl SkillImpl for SkillIWillProtectYou {
    fn skill_id(&self) -> u16 {
        WE_MALE
    }

    /// SkillIWillProtectYou::castendNoDamageId: when the caster's HP is above max HP / the skill's HP rate (the uint8
    /// `abs(skill_get_hp_rate)`), the target recovers the same % of its own max HP [Skotlex]; the packet's level is
    /// what status_heal returned.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let tstatus = crate::status::status_get_status_data(c, target);
        let sstatus = crate::status::status_get_status_data(c, src);
        let hp_rate = skill_get_hp_rate(id, skill_lv).unsigned_abs() as u8;
        if hp_rate != 0 && sstatus.hp > sstatus.max_hp / hp_rate as u32 {
            // The earned is the same % of the target HP than it costed the caster. [Skotlex]
            let gain_hp = tstatus.max_hp.wrapping_mul(hp_rate as u32) / 100;
            let healed = status_heal(c, target.id, gain_hp as i32 as i64, 0, 0, 0);
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(src), &t, id, healed, true);
        }
    }
}
