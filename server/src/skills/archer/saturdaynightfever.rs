//! skills/archer/saturdaynightfever.cpp: class SkillSaturdayNightFever : public SkillImpl (WM_SATURDAY_NIGHT_FEVER).

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::skill::area::{foreach_in_range, skill_area_sub, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::pc_checkskill;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSaturdayNightFever;

impl SkillImpl for SkillSaturdayNightFever {
    fn skill_id(&self) -> u16 {
        WM_SATURDAY_NIGHT_FEVER
    }

    /// SkillSaturdayNightFever::castendNoDamageId: the status on the recursive call; a player caster has a
    /// (INT / 6 + job level / 5 + 4 * lv + Lesson)% chance to reach every player around it, then the no-damage packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if *flag & 1 != 0 {
            sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        } else if let Some(job_level) = super::job_level(c, src) {
            let sstatus = crate::status::status_get_status_data(c, src);
            if ((c.rnd() % 100) as i32) < sstatus.int_ as i32 / 6 + job_level / 5 + skill_lv as i32 * 4 + pc_checkskill(c, src, WM_LESSON) {
                // !TODO: What's the Lesson bonus?
                let f = *flag | BCT_ENEMY | 1;
                foreach_in_range(c, src, skill_get_splash(id, skill_lv) as i16, crate::map::BL_PC, Wall::None, |c, bl| skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id));
                clif_skill_nodamage(c, Some(&super::fresh(c, src)), &super::fresh(c, target), id, skill_lv as i32, true);
            }
        }
    }
}
