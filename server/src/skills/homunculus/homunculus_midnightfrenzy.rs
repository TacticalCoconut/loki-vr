//! skills/homunculus/homunculus_midnightfrenzy.cpp: class SkillMidnightFrenzy : public SkillImpl (MH_MIDNIGHT_FRENZY).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMidnightFrenzy;

impl SkillImpl for SkillMidnightFrenzy {
    fn skill_id(&self) -> u16 {
        MH_MIDNIGHT_FRENZY
    }

    /// SkillMidnightFrenzy::castendDamageId: a hit of the skill's own type.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        crate::skill::attack::skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
    }

    /// SkillMidnightFrenzy::calculateSkillRatio: -100 + 450 * lv * base level / 150 + STR (!TODO upstream: confirm
    /// the STR bonus).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 450 * skill_lv as i32 * crate::status::status_get_lv(c, src) / 150 + sstatus.str as i32;
    }
}
