//! skills/npc/rainofmeteor.cpp: class SkillRainOfMeteor : public SkillImpl (NPC_RAINOFMETEOR).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface::skill_unitsetting;
use crate::skills::novice::repeat_count;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRainOfMeteor;

impl SkillImpl for SkillRainOfMeteor {
    fn skill_id(&self) -> u16 {
        NPC_RAINOFMETEOR
    }

    /// SkillRainOfMeteor::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 350; // unknown ratio
    }

    /// SkillRainOfMeteor::castendPos2: one meteor unit per unit interval (skill_get_time / skill_get_unit_interval of
    /// them), each placed with a delay of its interval: two in the first interval (the center and a random cell next
    /// to it), then one per interval on a random cell of the splash area (skill_unitsetting: skillunit agent,
    /// skill/iface.rs). The rolls happen here whatever the units do: rnd() % 3 twice, then rnd() % (area * 2 + 1)
    /// twice per later interval.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let area = skill_get_splash(id, skill_lv);
        let (x32, y32) = (x as i32, y as i32);
        let interval = skill_get_unit_interval(id);
        let n = repeat_count(skill_get_time(id, skill_lv), interval);
        let mut i: i32 = 1;
        while i <= n {
            // Casts a double meteor in the first interval.
            if i == 1 {
                // The first meteor is at the center
                skill_unitsetting(c, src, id, skill_lv, x, y, *flag + interval);
                // The second meteor is near the first
                let tmpx = (x32 - 1 + (c.rnd() % 3) as i32) as i16;
                let tmpy = (y32 - 1 + (c.rnd() % 3) as i32) as i16;
                skill_unitsetting(c, src, id, skill_lv, tmpx, tmpy, *flag + interval);
            } else {
                // Casts 1 meteor per interval in the splash area
                let tmpx = (x32 - area + (c.rnd() as i32) % (area * 2 + 1)) as i16;
                let tmpy = (y32 - area + (c.rnd() as i32) % (area * 2 + 1)) as i16;
                skill_unitsetting(c, src, id, skill_lv, tmpx, tmpy, *flag + i * interval);
            }
            i += 1;
        }
    }
}
