//! skills/acolyte/silentium.cpp: class SkillSilentium : public SkillImpl (AB_SILENTIUM).

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillSilentium;

impl SkillImpl for SkillSilentium {
    fn skill_id(&self) -> u16 {
        AB_SILENTIUM
    }

    /// SkillSilentium::castendNoDamageId: Lex Divina at Silentium's level on every enemy in the splash range around
    /// the caster (map_foreachinallrange). (rAthena: should the level of Lex Divina be equivalent to the level of
    /// Silentium or should the highest level learned be used? [LimitLine])
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let f = *flag | BCT_ENEMY | 1;
        foreach_in_range(c, src, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::None, |c, bl| area::skill_area_sub(c, bl, src, PR_LEXDIVINA, skill_lv, tick, f, skill_castend_nodamage_id));
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
    }
}
