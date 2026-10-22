//! skills/archer/soundofdestruction.cpp: class SkillSoundOfDestruction : public SkillImpl (WM_SOUND_OF_DESTRUCTION).

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

pub struct SkillSoundOfDestruction;

impl SkillImpl for SkillSoundOfDestruction {
    fn skill_id(&self) -> u16 {
        WM_SOUND_OF_DESTRUCTION
    }

    /// SkillSoundOfDestruction::castendNoDamageId: on the recursive call the status for skill_get_time + 500 ms per
    /// Lesson level of a player caster (!TODO: Confirm Lesson increase); the caster's call reaches every player
    /// around it, then the no-damage packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if *flag & 1 != 0 {
            let lesson = if src.kind == crate::map::BL_PC { pc_checkskill(c, src, WM_LESSON) * 500 } else { 0 };
            sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, (lesson + skill_get_time(id, skill_lv)) as i64);
        } else {
            let f = *flag | BCT_ENEMY | 1;
            foreach_in_range(c, src, skill_get_splash(id, skill_lv) as i16, crate::map::BL_PC, Wall::None, |c, bl| skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id));
            clif_skill_nodamage(c, Some(&super::fresh(c, src)), &super::fresh(c, target), id, skill_lv as i32, true);
        }
    }
}
