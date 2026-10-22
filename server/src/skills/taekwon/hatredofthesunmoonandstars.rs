//! skills/taekwon/hatredofthesunmoonandstars.cpp: class SkillHatredoftheSunMoonandStars : public SkillImpl (SG_HATE).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHatredoftheSunMoonandStars;

impl SkillImpl for SkillHatredoftheSunMoonandStars {
    fn skill_id(&self) -> u16 {
        SG_HATE
    }

    /// SkillHatredoftheSunMoonandStars::castendNoDamageId: the target's class becomes the level's Hatred target
    /// (pc_set_hate_mob).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind == BL_PC {
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            if !crate::pc_taekwon::pc_set_hate_mob(c, src, skill_lv as i32 - 1, target) {
                skill_fail(c, src, id);
            }
        }
    }
}
