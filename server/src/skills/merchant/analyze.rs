//! skills/merchant/analyze.cpp: class SkillAnalyze : public SkillImpl (NC_ANALYZE).

use crate::battle::DMG_SINGLE;
use crate::db::skills::*;
use crate::skill::clif::{clif_skill_damage, DMGVAL_IGNORE};
use crate::skills::novice::status_get_amotion;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillAnalyze::SkillAnalyze() : SkillImpl(NC_ANALYZE).
pub struct SkillAnalyze;

impl SkillImpl for SkillAnalyze {
    fn skill_id(&self) -> u16 {
        NC_ANALYZE
    }

    /// SkillAnalyze::castendNoDamageId: the skill's damage packet without a value (DMGVAL_IGNORE), then its status on
    /// the target at 30% + 12% per level.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_damage(c, src, target, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        sc::sc_start(c, Some(src), &t, skill_get_sc(id) as i32, 30 + 12 * skill_lv as i32, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
    }
}
