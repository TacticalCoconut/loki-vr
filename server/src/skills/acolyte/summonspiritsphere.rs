//! skills/acolyte/summonspiritsphere.cpp: class SkillSummoningSpiritSphere : public SkillImpl (MO_CALLSPIRITS).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::state::pc_addspiritball;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_RAISINGDRAGON;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSummoningSpiritSphere;

impl SkillImpl for SkillSummoningSpiritSphere {
    fn skill_id(&self) -> u16 {
        MO_CALLSPIRITS
    }

    /// SkillSummoningSpiritSphere::castendNoDamageId: one sphere, up to the skill level (+ Raising Dragon's level).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != BL_PC {
            return;
        }
        let mut limit = skill_lv as i32;
        if let Some(e) = sce(c, src, SC_RAISINGDRAGON) {
            limit += e.val1;
        }
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        pc_addspiritball(c, src.id, skill_get_time(id, skill_lv), limit);
    }
}
