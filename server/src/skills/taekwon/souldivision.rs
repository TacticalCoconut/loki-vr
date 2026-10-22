//! skills/taekwon/souldivision.cpp: class SkillSoulDivision : public StatusSkillImpl (SP_SOULDIVISION).

use crate::battle::DMG_SINGLE;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_damage, skill_fail, DMGVAL_IGNORE};
use crate::skills::novice::status_get_amotion;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSoulDivision;

impl SkillImpl for SkillSoulDivision {
    fn skill_id(&self) -> u16 {
        SP_SOULDIVISION
    }

    /// SkillSoulDivision::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if target.kind != BL_PC {
            if src.kind == BL_PC {
                skill_fail(c, src, id);
            }
            return;
        }

        status_castend_nodamage_id(c, id, false, src, target, skill_lv);

        let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_damage(c, &s, &t, tick, status_get_amotion(c, &s), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
    }
}
