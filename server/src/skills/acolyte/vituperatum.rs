//! skills/acolyte/vituperatum.cpp: class SkillVituperatum : public StatusSkillImpl (AB_VITUPERATUM).

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillVituperatum;

impl SkillImpl for SkillVituperatum {
    fn skill_id(&self) -> u16 {
        AB_VITUPERATUM
    }

    /// SkillVituperatum::castendNoDamageId: every enemy in the splash range around the target (map_foreachinrange);
    /// each one (flag & 1) gets StatusSkillImpl::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            status_castend_nodamage_id(c, id, false, src, target, skill_lv);
        } else {
            let f = *flag | BCT_ENEMY | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::Config, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id));
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
        }
    }
}
