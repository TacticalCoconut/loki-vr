//! skills/swordman/dragonhowling.cpp: class SkillDragonHowling : public SkillImpl (RK_DRAGONHOWLING).

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_PREAMBLE;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDragonHowling;

impl SkillImpl for SkillDragonHowling {
    fn skill_id(&self) -> u16 {
        RK_DRAGONHOWLING
    }

    /// SkillDragonHowling::castendNoDamageId: the status on each enemy around the caster (recursive call, flag&1).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if *flag & 1 != 0 {
            sc::sc_start(c, Some(src), target, type_, 50 + 6 * skill_lv as i32, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        } else {
            area::set_temp(2, 0);
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            let f = *flag | BCT_ENEMY | SD_PREAMBLE | 1;
            foreach_in_range(c, src, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::None, |c, bl| {
                area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id)
            });
        }
    }
}
