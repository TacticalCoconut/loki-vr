//! skills/swordman/abundance.cpp: class SkillAbundance : public SkillImpl (RK_ABUNDANCE).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::novice::pc_checkskill;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillAbundance;

impl SkillImpl for SkillAbundance {
    fn skill_id(&self) -> u16 {
        RK_ABUNDANCE
    }

    /// SkillAbundance::castendNoDamageId: players with Rune Mastery 6 or more only.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != BL_PC {
            return;
        }
        if pc_checkskill(c, src, RK_RUNEMASTERY) >= 6 {
            if sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64) {
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
            }
        } else {
            skill_fail(c, src, id);
        }
    }
}
