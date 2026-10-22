//! skills/taekwon/soulrevolution.cpp: class SkillSoulRevolution : public SkillImpl (SP_SOULREVOLVE).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_fail, fail::USESKILL_FAIL};
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_SOULFAIRY, SC_SOULFALCON, SC_SOULGOLEM, SC_SOULSHADOW, SC_SPIRIT};
use crate::status::hp::status_heal;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSoulRevolution;

impl SkillImpl for SkillSoulRevolution {
    fn skill_id(&self) -> u16 {
        SP_SOULREVOLVE
    }

    /// SkillSoulRevolution::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let links = [SC_SPIRIT, SC_SOULGOLEM, SC_SOULSHADOW, SC_SOULFALCON, SC_SOULFAIRY];
        if !links.iter().any(|&t| sce(c, target, t).is_some()) {
            if src.kind == BL_PC {
                clif_skill_fail(c, src, self.skill_id(), USESKILL_FAIL, 0, 0);
            }
            return;
        }
        status_heal(c, target.id, 0, 50 * skill_lv as i64, 0, 2);
        for t in links {
            sc::status_change_end(c, target.id, t, 0);
        }
    }
}
