//! skills/npc/helldignity.cpp: class SkillHellDignity : public SkillImpl (NPC_WIDEHELLDIGNITY), rAthena e985006.

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

pub struct SkillHellDignity;

impl SkillImpl for SkillHellDignity {
    fn skill_id(&self) -> u16 {
        NPC_WIDEHELLDIGNITY
    }

    /// SkillHellDignity::castendNoDamageId: the skill's status on every enemy around the target (flag & 1: one of
    /// them).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            sc::sc_start2(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, src.id as i32, skill_get_time2(id, skill_lv) as i64);
        } else {
            area::set_temp(2, 0); // For SD_PREAMBLE
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            let f = *flag | BCT_ENEMY | SD_PREAMBLE | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::None, |c, bl| {
                area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id)
            });
        }
    }
}
