//! skills/gunslinger/firerain.cpp: class SkillFireRain : public SkillImpl (RL_FIRE_RAIN), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::path::{DIR_EAST, DIR_NORTH, DIR_NORTHEAST, DIR_NORTHWEST, DIR_SOUTH, DIR_SOUTHEAST, DIR_SOUTHWEST, DIR_WEST};
use crate::skill::timerskill::skill_addtimerskill;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFireRain;

impl SkillImpl for SkillFireRain {
    fn skill_id(&self) -> u16 {
        RL_FIRE_RAIN
    }

    /// SkillFireRain::castendPos2: level + 6 waves (w = 0 ..= level + 5), 80 ms apart from gettick(), each a
    /// timerskill on the caster's line in the direction of (x, y) (north / south variants move y, west / east move x;
    /// the other coordinate keeps its last value), carrying the direction as type.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let wave = skill_lv as i32 + 5;
        let dir = crate::map::calc_dir(src, x, y);
        let mut sx = src.x as i32;
        let mut sy = src.y as i32;
        let (x, y) = (src.x as i32, src.y as i32);
        for w in 0..=wave {
            match dir {
                DIR_NORTH | DIR_NORTHWEST | DIR_NORTHEAST => sy = y + w,
                DIR_WEST => sx = x - w,
                DIR_SOUTHWEST | DIR_SOUTH | DIR_SOUTHEAST => sy = y - w,
                DIR_EAST => sx = x + w,
                _ => {}
            }
            let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
            skill_addtimerskill(c, &s, c.tick() + 80 * w as i64, 0, sx as i16, sy as i16, id, skill_lv, dir as i32, *flag);
        }
    }

    /// SkillFireRain::calculateSkillRatio: 3500 + 300% per level.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 3500 + 300 * skill_lv as i32;
    }
}
