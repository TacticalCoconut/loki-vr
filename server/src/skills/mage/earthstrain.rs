//! skills/mage/earthstrain.cpp: class SkillEarthStrain : public SkillImpl (WL_EARTHSTRAIN), rAthena e985006. The
//! waves are WL_EARTHSTRAIN timerskills without a target (skill.cpp skill_timerskill: skill_unitsetting of one wave
//! cell each 140 ms, ported in skill/timerskill.rs).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillEarthStrain;

impl SkillImpl for SkillEarthStrain {
    fn skill_id(&self) -> u16 {
        WL_EARTHSTRAIN
    }

    /// SkillEarthStrain::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 1000 + 600 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillEarthStrain::castendPos2: `skill_lv + 4` waves in the direction of the cast cell, starting next to the
    /// caster. rAthena queues them at `gettick() + 140 * w` (the current tick, not the `tick` argument).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let wave = skill_lv as i32 + 4;
        let dir = crate::map::calc_dir(src, x, y);
        // Store first caster's location to avoid glitch on unit setting
        let (x, y) = (src.x, src.y);
        let (mut sx, mut sy) = (x, y);
        for w in 1..=wave {
            let wi = w as i16;
            match dir {
                0 | 1 | 7 => sy = y + wi,
                3..=5 => sy = y - wi,
                2 => sx = x - wi,
                6 => sx = x + wi,
                _ => {}
            }
            skill_addtimerskill(c, src, c.tick() + (140 * w) as Tick, 0, sx, sy, self.skill_id(), skill_lv, dir as i32, *flag & 2);
        }
    }
}
