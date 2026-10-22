//! skills/npc/pulsestrike2.cpp: class SkillPulseStrike2 : public SkillImplRecursiveDamageSplash (NPC_PULSESTRIKE2).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillPulseStrike2;

impl SkillImpl for SkillPulseStrike2 {
    fn skill_id(&self) -> u16 {
        NPC_PULSESTRIKE2
    }

    crate::splash_skill_defaults!();

    /// SkillPulseStrike2::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 100;
    }

    /// SkillPulseStrike2::castendNoDamageId: three timer skills on the target, skill_get_time apart (the first one
    /// now).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        for i in 0..3i64 {
            skill_addtimerskill(c, src, tick + skill_get_time(id, skill_lv) as Tick * i, target.id, 0, 0, id, skill_lv, skill_get_type(id), *flag);
        }
    }
}

impl RecursiveDamageSplash for SkillPulseStrike2 {}
