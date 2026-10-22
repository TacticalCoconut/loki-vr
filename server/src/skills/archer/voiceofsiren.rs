//! skills/archer/voiceofsiren.cpp: class SkillVoiceOfSiren : public SkillImpl (WM_VOICEOFSIREN).

use crate::battle::BCT_WOS;
use crate::db::skills::*;
use crate::skill::area::{self, foreach_in_range, skill_area_sub, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::pc_checkskill;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillVoiceOfSiren;

impl SkillImpl for SkillVoiceOfSiren {
    fn skill_id(&self) -> u16 {
        WM_VOICEOFSIREN
    }

    /// SkillVoiceOfSiren::castendNoDamageId: the caster's call computes the chance ("(Skill Level x 6) + (Voice
    /// Lesson Skill Level x 2) + (Caster's Job Level / 2) %") and the duration into skill_area_temp[5] / [6], walks
    /// every character and skill unit around it (BCT_ALL | BCT_WOS) and sends the no-damage packet; the recursive call
    /// starts the status (val2 the caster) with those values.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if *flag & 1 != 0 {
            sc::sc_start2(c, Some(src), target, type_, area::temp(5), skill_lv as i32, src.id as i32, area::temp(6) as i64);
        } else {
            let is_pc = src.kind == crate::map::BL_PC;
            let lesson = if is_pc { pc_checkskill(c, src, WM_LESSON) } else { 1 };
            area::set_temp(5, skill_lv as i32 * 6 + lesson * 2 + super::job_level(c, src).unwrap_or(50) / 2);
            area::set_temp(6, skill_get_time(id, skill_lv));
            let f = *flag | BCT_ALL | BCT_WOS | 1;
            foreach_in_range(c, src, skill_get_splash(id, skill_lv) as i16, crate::map::BL_CHAR | crate::map::BL_SKILL, Wall::None, |c, bl| skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id));
            clif_skill_nodamage(c, Some(&super::fresh(c, src)), &super::fresh(c, target), id, skill_lv as i32, true);
        }
    }
}
