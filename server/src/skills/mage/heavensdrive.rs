//! skills/mage/heavensdrive.cpp: class SkillHeavensDrive : public SkillImpl (WZ_HEAVENDRIVE). The hits come from
//! its skill unit (skillunit/ontimer.rs).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::status::consts::SC_SV_ROOTTWIST;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHeavensDrive;

impl SkillImpl for SkillHeavensDrive {
    fn skill_id(&self) -> u16 {
        WZ_HEAVENDRIVE
    }

    /// SkillHeavensDrive::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        //Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        *flag |= 1;
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }

    /// SkillHeavensDrive::calculateSkillRatio (RENEWAL; pre-renewal has no modifier).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 25;
    }

    /// SkillHeavensDrive::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, _src: &Unit, target: &Unit, _skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::status_change_end(c, target.id, SC_SV_ROOTTWIST, 0);
    }
}
