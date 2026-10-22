//! skills/other/returntoeldicastes.cpp: class SkillReturnToEldicastes : public SkillImpl (RETURN_TO_ELDICASTES).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillReturnToEldicastes;

impl SkillImpl for SkillReturnToEldicastes {
    fn skill_id(&self) -> u16 {
        RETURN_TO_ELDICASTES
    }

    /// SkillReturnToEldicastes::castendNoDamageId: to MAP_DICASTES ("dicastes01") 198,187.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        super::recall_castend_nodamage_id(c, self.skill_id(), src, target, skill_lv, flag, "dicastes01", 198, 187);
    }
}
