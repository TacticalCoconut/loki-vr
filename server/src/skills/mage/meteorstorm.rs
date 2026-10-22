//! skills/mage/meteorstorm.cpp: class SkillMeteorStorm : public SkillImpl (WZ_METEOR). Each meteor is a skill unit
//! group placed with a delay (skill_unitsetting's WZ_METEOR case: `flag` is the fall delay; skillunit/setting.rs) that
//! skill_unit_timer_sub makes fall (skillunit/timer.rs).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::novice::repeat_count;
use crate::skills::SkillImpl;
use crate::status::consts::SC_STUN;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMeteorStorm;

impl SkillImpl for SkillMeteorStorm {
    fn skill_id(&self) -> u16 {
        WZ_METEOR
    }

    /// SkillMeteorStorm::castendPos2: `skill_get_time / skill_get_unit_interval` meteors, each on a random cell of the
    /// splash area, falling `i * unit_interval` ms apart.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let area = skill_get_splash(id, skill_lv);
        let interval = skill_get_unit_interval(id);
        for i in 1..=repeat_count(skill_get_time(id, skill_lv), interval) {
            // Creates a random Cell in the Splash Area
            // (rnd() is a non-negative int32: `x - area + rnd() % (area * 2 + 1)` in int, stored in an int16)
            let tmpx = (x as i32 - area + (c.rnd() as i32) % (area * 2 + 1)) as i16;
            let tmpy = (y as i32 - area + (c.rnd() as i32) % (area * 2 + 1)) as i16;
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            iface::skill_unitsetting(c, &s, id, skill_lv, tmpx, tmpy, *flag + i * interval);
        }
    }

    /// SkillMeteorStorm::calculateSkillRatio (RENEWAL; pre-renewal has no modifier).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 25;
    }

    /// SkillMeteorStorm::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_STUN, 3 * skill_lv as i32, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }
}
