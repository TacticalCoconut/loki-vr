//! skills/ninja/illusionshock.cpp: class SkillIllusionShock : public StatusSkillImpl (KO_KYOUGAKU), rAthena e985006.

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::skill_fail;
use crate::skills::novice::sce;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillIllusionShock;

impl SkillImpl for SkillIllusionShock {
    fn skill_id(&self) -> u16 {
        KO_KYOUGAKU
    }

    /// SkillIllusionShock::castendNoDamageId: on a player without the status, with INT / 2 %: StatusSkillImpl's
    /// start; else a player's cast fails.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if target.kind == BL_PC && sce(c, target, type_).is_none() && ((c.rnd() % 100) as i32) < crate::status::status_get_status_data(c, target).int_ as i32 / 2 {
            status_castend_nodamage_id(c, id, false, src, target, skill_lv);
        } else if src.kind == BL_PC {
            skill_fail(c, src, id);
        }
    }
}
