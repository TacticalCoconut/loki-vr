//! skills/other/pronterarecall.cpp: class SkillPronteraRecall : public SkillImpl (ALL_PRONTERA_RECALL).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillPronteraRecall;

impl SkillImpl for SkillPronteraRecall {
    fn skill_id(&self) -> u16 {
        ALL_PRONTERA_RECALL
    }

    /// SkillPronteraRecall::castendNoDamageId: to MAP_PRONTERA ("prontera") 115,72 at level 1, 159,192 at level 2.
    /// rAthena leaves x / y uninitialised for any other level (the skill has 2); 0,0 here (pc_setpos: a random cell).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let (x, y) = match skill_lv {
            1 => (115, 72),
            2 => (159, 192),
            _ => (0, 0),
        };
        super::recall_castend_nodamage_id(c, self.skill_id(), src, target, skill_lv, flag, "prontera", x, y);
    }
}
