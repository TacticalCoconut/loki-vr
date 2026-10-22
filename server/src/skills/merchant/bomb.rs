//! skills/merchant/bomb.cpp: class SkillBomb : public SkillImpl (AM_DEMONSTRATION).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::inventory::EQP_WEAPON;
use crate::skill::effect::skill_break_equip;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillBomb::SkillBomb() : SkillImpl(AM_DEMONSTRATION).
pub struct SkillBomb;

impl SkillImpl for SkillBomb {
    fn skill_id(&self) -> u16 {
        AM_DEMONSTRATION
    }

    /// SkillBomb::castendPos2: the fire unit; flag |= 1 so the Bottle Grenade is not consumed now (it is deleted with
    /// the group).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        //Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        *flag |= 1;
        crate::skill::iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }

    /// SkillBomb::calculateSkillRatio: +20% per level.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 20 * skill_lv as i32;
    }

    /// SkillBomb::applyAdditionalEffects (RENEWAL): weapon break, 3% per level (rate x100).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        skill_break_equip(c, src, target, EQP_WEAPON as u16, 300 * skill_lv as i32, BCT_ENEMY);
    }
}
