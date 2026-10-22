//! skills/thief/sightlessmind.cpp: class SkillSightlessMind : public SkillImplRecursiveDamageSplash (RG_RAID).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_SKILL};
use crate::skill::area::{self, foreach_in_range, skill_area_sub, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_BLIND, SC_HIDING, SC_RAID, SC_STUN};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSightlessMind;

impl SkillImpl for SkillSightlessMind {
    fn skill_id(&self) -> u16 {
        RG_RAID
    }

    crate::splash_skill_defaults!();

    /// SkillSightlessMind::calculateSkillRatio (RENEWAL).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + 50 + skill_lv as i32 * 150;
    }

    /// SkillSightlessMind::castendNoDamageId: the animation, the damage part on everything around the target, then
    /// the caster leaves Hiding.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        area::set_temp(1, 0);
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let f = *flag | BCT_ENEMY | 1;
        foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR | BL_SKILL, Wall::Config, |c, bl| skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
        sc::status_change_end(c, src.id, SC_HIDING, 0);
    }

    /// SkillSightlessMind::applyAdditionalEffects: stun, blind, and (RENEWAL) SC_RAID hardcoded to 10 seconds since
    /// Duration1 and Duration2 are used.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        let lv = skill_lv as i32;
        sc::sc_start(c, Some(src), target, SC_STUN, 10 + 3 * lv, lv, skill_get_time(id, skill_lv) as i64);
        sc::sc_start(c, Some(src), target, SC_BLIND, 10 + 3 * lv, lv, skill_get_time2(id, skill_lv) as i64);
        sc::sc_start(c, Some(src), target, SC_RAID, 100, lv, 10000);
    }
}

impl RecursiveDamageSplash for SkillSightlessMind {}
