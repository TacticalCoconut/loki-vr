//! skills/ninja/icemeteor.cpp: class SkillIceMeteor : public SkillImpl (NJ_HYOUSYOURAKU), rAthena e985006.

use super::charms_of_type;
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface::skill_unitsetting;
use crate::skill::state::CHARM_TYPE_WATER;
use crate::skills::SkillImpl;
use crate::status::consts::SC_FREEZE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillIceMeteor;

impl SkillImpl for SkillIceMeteor {
    fn skill_id(&self) -> u16 {
        NJ_HYOUSYOURAKU
    }

    /// SkillIceMeteor::applyAdditionalEffects: freezing, 10 + 10% per level.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_FREEZE, 10 + 10 * skill_lv as i32, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillIceMeteor::calculateSkillRatio: + 50% per level, + 100% per water charm.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 50 * skill_lv as i32;
        let charms = charms_of_type(c, src, CHARM_TYPE_WATER);
        if charms > 0 {
            *base_skillratio += 100 * charms;
        }
    }

    /// SkillIceMeteor::castendPos2: flag 1 keeps the ammo (it is deleted when the group is).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
