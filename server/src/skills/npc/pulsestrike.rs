//! skills/npc/pulsestrike.cpp: class SkillPulseStrike : public SkillImplRecursiveDamageSplash (NPC_PULSESTRIKE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillPulseStrike;

impl SkillImpl for SkillPulseStrike {
    fn skill_id(&self) -> u16 {
        NPC_PULSESTRIKE
    }

    crate::splash_skill_defaults!();

    /// SkillPulseStrike::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 100 * (skill_lv as i32 - 1);
    }

    /// SkillPulseStrike::castendNoDamageId: skill_castend_damage_id on the caster itself (the splash around it).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_castend_damage_id(c, src, src, self.skill_id(), skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillPulseStrike {}
