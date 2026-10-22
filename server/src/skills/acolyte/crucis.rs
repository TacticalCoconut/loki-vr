//! skills/acolyte/crucis.cpp: class SkillCrucis : public SkillImpl (AL_CRUCIS).

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::{sc, status_get_lv};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillCrucis;

impl SkillImpl for SkillCrucis {
    fn skill_id(&self) -> u16 {
        AL_CRUCIS
    }

    /// SkillCrucis::castendNoDamageId: the caster's cast reaches every enemy in the splash range
    /// (map_foreachinallrange); each one (flag & 1) resists by level.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, bl: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if *flag & 1 != 0 {
            let rate = 25 + skill_lv as i32 * 4 + status_get_lv(c, src) - status_get_lv(c, bl);
            sc::sc_start(c, Some(src), bl, type_, rate, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        } else {
            let f = *flag | BCT_ENEMY | 1;
            foreach_in_range(c, src, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::None, |c, t| area::skill_area_sub(c, t, src, id, skill_lv, tick, f, skill_castend_nodamage_id));
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
            clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
        }
    }
}
