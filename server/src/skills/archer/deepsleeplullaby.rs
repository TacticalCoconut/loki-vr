//! skills/archer/deepsleeplullaby.cpp: class SkillDeepSleepLullaby : public SkillImpl (WM_LULLABY_DEEPSLEEP).

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

pub struct SkillDeepSleepLullaby;

impl SkillImpl for SkillDeepSleepLullaby {
    fn skill_id(&self) -> u16 {
        WM_LULLABY_DEEPSLEEP
    }

    /// SkillDeepSleepLullaby::castendNoDamageId: on the recursive call the status, with a chance of 4% per level +
    /// 2 per Lesson level + base level / 15 + job level / 5 and a duration reduced by 50 ms per point of the target's
    /// base INT and per base level ("Duration reduction for Deep Sleep Lullaby is doubled"); the caster's call sends
    /// the no-damage packet and walks the characters around the target.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let is_pc = src.kind == crate::map::BL_PC;
        if *flag & 1 != 0 {
            let rate = 4 * skill_lv as i32
                + if is_pc { pc_checkskill(c, src, WM_LESSON) * 2 } else { 0 }
                + crate::status::status_get_lv(c, src) / 15
                + super::job_level(c, src).map(|j| j / 5).unwrap_or(0);
            let tbase = crate::status::status_get_base_status(c, target);
            let duration = skill_get_time(id, skill_lv) - (tbase.int_ as i32 * 50 + crate::status::status_get_lv(c, target) * 50);
            sc::sc_start(c, Some(src), target, type_, rate, skill_lv as i32, duration as i64);
        } else {
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            let f = *flag | BCT_ENEMY | 1;
            let t = super::fresh(c, target);
            foreach_in_range(c, &t, skill_get_splash(id, skill_lv) as i16, crate::map::BL_CHAR, Wall::None, |c, bl| skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id));
        }
    }
}
