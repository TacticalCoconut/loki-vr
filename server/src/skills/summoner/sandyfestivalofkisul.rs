//! skills/summoner/sandyfestivalofkisul.cpp: class SkillSandyFestivalofKisul : public SkillImpl
//! (SH_SANDY_FESTIVAL_OF_KI_SUL). Its castendNoDamageId is the same code as marinefestivalofkisul.cpp's (only the skill
//! id differs): [`super::marinefestivalofkisul::festival_castend_nodamage_id`].

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSandyFestivalofKisul;

impl SkillImpl for SkillSandyFestivalofKisul {
    fn skill_id(&self) -> u16 {
        SH_SANDY_FESTIVAL_OF_KI_SUL
    }

    /// SkillSandyFestivalofKisul::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        super::marinefestivalofkisul::festival_castend_nodamage_id(c, self.skill_id(), src, target, skill_lv, tick, flag);
    }
}
