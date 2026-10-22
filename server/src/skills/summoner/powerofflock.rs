//! skills/summoner/powerofflock.cpp: class SkillPowerofFlock : public SkillImpl (SU_POWEROFFLOCK).

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_FEAR, SC_FREEZE};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillPowerofFlock;

impl SkillImpl for SkillPowerofFlock {
    fn skill_id(&self) -> u16 {
        SU_POWEROFFLOCK
    }

    /// SkillPowerofFlock::castendNoDamageId: the area call starts Fear (skill_get_time) and Freeze (skill_get_time2,
    /// "TODO: What's the duration?") on each enemy; the first call sends the packet and searches the splash range
    /// (map_foreachinshootrange when battle_config.skill_wall_check, else map_foreachinrange).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            sc::sc_start(c, Some(src), target, SC_FEAR, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            sc::sc_start(c, Some(src), &t, SC_FREEZE, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64); // TODO: What's the duration?
        } else {
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            let wall = if crate::skill::conf::skill_wall_check() { Wall::Always } else { Wall::Config };
            let f = *flag | BCT_ENEMY | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR, wall, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id));
        }
    }
}
