//! skills/ninja/nightmareerasion.cpp: class SkillNightmareErasion : public SkillImpl (SS_AKUMUKESU), rAthena e985006.

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_SPLASH;
use crate::skills::SkillImpl;
use crate::status::consts::SC_NIGHTMARE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillNightmareErasion;

impl SkillImpl for SkillNightmareErasion {
    fn skill_id(&self) -> u16 {
        SS_AKUMUKESU
    }

    /// SkillNightmareErasion::castendNoDamageId: the cast runs this again (flag | 1) on the enemies of the splash
    /// area, which lose SC_NIGHTMARE.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            sc::status_change_end(c, target.id, SC_NIGHTMARE, 0);
        } else {
            let range = skill_get_splash(id, skill_lv) as i16;
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            let f = *flag | BCT_ENEMY | SD_SPLASH | 1;
            foreach_in_range(c, target, range, BL_CHAR, Wall::Config, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id));
        }
    }
}
