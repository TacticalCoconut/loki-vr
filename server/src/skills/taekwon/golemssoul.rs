//! skills/taekwon/golemssoul.cpp: class SkillGolemsSoul : public SkillImpl (SP_SOULGOLEM).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_fail, clif_skill_nodamage, fail::USESKILL_FAIL};
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGolemsSoul;

impl SkillImpl for SkillGolemsSoul {
    fn skill_id(&self) -> u16 {
        SP_SOULGOLEM
    }

    /// SkillGolemsSoul::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64) {
            let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
        } else if src.kind == BL_PC {
            clif_skill_fail(c, src, id, USESKILL_FAIL, 0, 0);
        }
    }
}
