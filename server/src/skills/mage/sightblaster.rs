//! skills/mage/sightblaster.cpp: class SkillSightBlaster : public SkillImpl (WZ_SIGHTBLASTER). The blast itself
//! is SC_SIGHTBLASTER's (status.cpp status_change_timer_sub: skill_attack when an enemy comes near).

use crate::battle::calc::Damage;
use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSightBlaster;

impl SkillImpl for SkillSightBlaster {
    fn skill_id(&self) -> u16 {
        WZ_SIGHTBLASTER
    }

    /// SkillSightBlaster::castendNoDamageId: `clif_skill_nodamage(src, *target, id, lv, sc_start2(src, target,
    /// skill_get_sc(id), 100, skill_lv, id, skill_get_time(id, skill_lv)))`.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let ok = sc::sc_start2(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, id as i32, skill_get_time(id, skill_lv) as i64);
        let target = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &target, id, skill_lv as i32, ok);
    }

    /// SkillSightBlaster::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }

    /// SkillSightBlaster::calculateSkillRatio (RENEWAL; pre-renewal has no modifier).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 500;
    }
}
