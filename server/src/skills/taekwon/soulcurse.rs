//! skills/taekwon/soulcurse.cpp: class SkillSoulCurse : public SkillImpl (SP_SOULCURSE).

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSoulCurse;

impl SkillImpl for SkillSoulCurse {
    fn skill_id(&self) -> u16 {
        SP_SOULCURSE
    }

    /// SkillSoulCurse::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let lv = skill_lv as i32;
        if *flag & 1 != 0 {
            sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 30 + 10 * lv, lv, skill_get_time(id, skill_lv) as i64);
        } else {
            // map_foreachinrange(skill_area_sub, target, …)
            let f = *flag | BCT_ENEMY | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::Config, |c, bl| {
                area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id)
            });
            let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(&s), &t, id, lv, true);
        }
    }
}
