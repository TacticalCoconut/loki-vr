//! skills/merchant/hammerfall.cpp: class SkillHammerFall : public SkillImpl (BS_HAMMERFALL). The stun itself is
//! skill_timerskill's BS_HAMMERFALL case (skill/timerskill.rs).

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillHammerFall::SkillHammerFall() : SkillImpl(BS_HAMMERFALL).
pub struct SkillHammerFall;

impl SkillImpl for SkillHammerFall {
    fn skill_id(&self) -> u16 {
        BS_HAMMERFALL
    }

    /// SkillHammerFall::castendNoDamageId: each unit of the area gets a timerskill 1 s later whose `type` is the stun
    /// rate, min(20 + 10 * lv, 50 + 5 * lv).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let lv = skill_lv as i32;
        skill_addtimerskill(c, src, tick + 1000, target.id, 0, 0, id, skill_lv, (20 + 10 * lv).min(50 + 5 * lv), *flag);
    }

    /// SkillHammerFall::castendPos2: skill_castend_nodamage_id (flag | BCT_ENEMY | 2) on every character of the
    /// splash square around the cell (map_foreachinallarea: no wall check).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let i = skill_get_splash(id, skill_lv) as i16;
        let f = *flag | BCT_ENEMY | 2;
        foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, BL_CHAR, Wall::None, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id));
    }
}
