//! skills/npc/hellsjudgement.cpp: class SkillHellsJudgement : public SkillImplRecursiveDamageSplash
//! (NPC_HELLJUDGEMENT), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_CURSE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHellsJudgement;

impl SkillImpl for SkillHellsJudgement {
    fn skill_id(&self) -> u16 {
        NPC_HELLJUDGEMENT
    }

    crate::splash_skill_defaults!();

    /// SkillHellsJudgement::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_CURSE, 100, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillHellsJudgement::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 100 * (skill_lv as i32 - 1);
    }

    /// SkillHellsJudgement::castendNoDamageId: the splash around the caster (skill_castend_damage_id on itself).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_castend_damage_id(c, src, src, self.skill_id(), skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillHellsJudgement {}
