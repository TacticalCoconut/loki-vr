//! skills/other/peonymamy.cpp: class SkillPeonyMamy : public SkillImpl (ECL_PEONYMAMY).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_CRYSTALIZE, SC_FREEZE, SC_FREEZING};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillPeonyMamy;

impl SkillImpl for SkillPeonyMamy {
    fn skill_id(&self) -> u16 {
        ECL_PEONYMAMY
    }

    /// SkillPeonyMamy::castendNoDamageId: cures Freeze, Freezing and Crystalize; see [`eclage_cure`].
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        eclage_cure(c, self.skill_id(), &[SC_FREEZE, SC_FREEZING, SC_CRYSTALIZE], src, target, skill_lv, tick);
    }
}

/// The body peonymamy.cpp, sadagui.cpp, sequoiadust.cpp and snowflip.cpp share (their status lists differ): the
/// statuses are ended on the target in order, then the damage packet with no damage
/// (`clif_skill_damage(*src, *target, tick, status_get_amotion(src), 0, DMGVAL_IGNORE, 1, skill_id, 1, DMG_SINGLE)`)
/// and the skill packet.
pub(crate) fn eclage_cure(c: &Ctx, id: u16, statuses: &[i32], src: &Unit, target: &Unit, skill_lv: u16, tick: Tick) {
    for &t in statuses {
        crate::status::sc::status_change_end(c, target.id, t, 0);
    }
    let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
    let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
    let amotion = crate::skills::novice::status_get_amotion(c, &s);
    crate::skill::clif::clif_skill_damage(c, &s, &t, tick, amotion, 0, crate::skill::clif::DMGVAL_IGNORE, 1, id, 1, crate::battle::DMG_SINGLE);
    crate::skill::clif::clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
}
