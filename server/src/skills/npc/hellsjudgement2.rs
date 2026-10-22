//! skills/npc/hellsjudgement2.cpp: class SkillHellsJudgement2 : public SkillImplRecursiveDamageSplash
//! (NPC_HELLJUDGEMENT2), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_CONFUSION, SC_CURSE, SC_FEAR, SC_HALLUCINATION, SC_SLEEP, SC_STUN};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHellsJudgement2;

impl SkillImpl for SkillHellsJudgement2 {
    fn skill_id(&self) -> u16 {
        NPC_HELLJUDGEMENT2
    }

    crate::splash_skill_defaults!();

    /// SkillHellsJudgement2::applyAdditionalEffects: one of six statuses at random (`switch (rnd() % 6)`).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let t = match c.rnd() % 6 {
            0 => SC_SLEEP,
            1 => SC_CONFUSION,
            2 => SC_HALLUCINATION,
            3 => SC_STUN,
            4 => SC_FEAR,
            _ => SC_CURSE,
        };
        sc::sc_start(c, Some(src), target, t, 100, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillHellsJudgement2::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 100 * (skill_lv as i32 - 1);
    }

    /// SkillHellsJudgement2::castendNoDamageId: the splash around the caster (skill_castend_damage_id on itself).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_castend_damage_id(c, src, src, self.skill_id(), skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillHellsJudgement2 {}
