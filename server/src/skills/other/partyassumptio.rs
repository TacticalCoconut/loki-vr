//! skills/other/partyassumptio.cpp: class SkillPartyAssumptio : public SkillImpl (CASH_ASSUMPTIO).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillPartyAssumptio;

impl SkillImpl for SkillPartyAssumptio {
    fn skill_id(&self) -> u16 {
        CASH_ASSUMPTIO
    }

    /// SkillPartyAssumptio::castendNoDamageId: see [`party_status_castend_nodamage_id`].
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        party_status_castend_nodamage_id(c, self.skill_id(), src, target, skill_lv, tick, flag);
    }
}

/// The castendNoDamageId partyassumptio.cpp, partyblessing.cpp and partyincreaseagi.cpp share (only the skill id
/// differs): the status on the target and its packet (from the target to itself), when the caster is not a player,
/// has no party, or this is the party call (flag & 1); else every party member within the splash range.
pub(crate) fn party_status_castend_nodamage_id(c: &Ctx, id: u16, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
    use crate::battle::BCT_PARTY;
    use crate::skill::castend::skill_castend_nodamage_id;
    use crate::skill::clif::clif_skill_nodamage;
    use crate::skills::novice::party_foreachsamemap_area_sub;
    let type_ = skill_get_sc(id) as i32;
    let party_id = super::sd_status(c, src).map(|ch| ch.party_id);
    if party_id.is_none() || party_id == Some(0) || *flag & 1 != 0 {
        let ok = crate::status::sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&t), &t, id, skill_lv as i32, ok);
    } else {
        let f = *flag | BCT_PARTY | 1;
        party_foreachsamemap_area_sub(c, src, skill_get_splash(id, skill_lv) as i16, src, id, skill_lv, tick, f, skill_castend_nodamage_id);
    }
}
