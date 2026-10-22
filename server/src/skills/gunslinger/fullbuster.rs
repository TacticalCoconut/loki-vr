//! skills/gunslinger/fullbuster.cpp: class SkillFullBuster : public WeaponSkillImpl (GS_FULLBUSTER), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_BLIND;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFullBuster;

impl SkillImpl for SkillFullBuster {
    fn skill_id(&self) -> u16 {
        GS_FULLBUSTER
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillFullBuster::applyCounterAdditionalEffects: the shooter may blind itself (2% per level).
    fn apply_counter_additional_effects(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: &mut i32) {
        sc::sc_start(c, Some(src), src, SC_BLIND, 2 * skill_lv as i32, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillFullBuster::calculateSkillRatio: + 100% x (level + 2).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 100 * (skill_lv as i32 + 2);
    }
}
