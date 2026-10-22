//! skills/mage/thunderstorm.cpp: class SkillThunderStorm : public SkillImpl (MG_THUNDERSTORM). The bolts are
//! skill_unit_onplace_timer's UNT_THUNDERSTORM (skillunit/ontimer.rs).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillThunderStorm;

impl SkillImpl for SkillThunderStorm {
    fn skill_id(&self) -> u16 {
        MG_THUNDERSTORM
    }

    /// SkillThunderStorm::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        //Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        *flag |= 1;
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }

    /// SkillThunderStorm::calculateSkillRatio: in Renewal Thunder Storm boost is 100% (in pre-re, 80%): nothing
    /// (`#ifndef RENEWAL base_skillratio -= 20;` dropped).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, _base_skillratio: &mut i32, _mflag: i32) {}
}
