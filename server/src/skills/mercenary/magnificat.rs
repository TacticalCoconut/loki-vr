//! skills/mercenary/mercenary_magnificat.cpp: class SkillMercenaryMagnificat : public SkillImpl (MER_MAGNIFICAT).

use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::party_foreachsamemap_area_sub;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMercenaryMagnificat;

impl SkillImpl for SkillMercenaryMagnificat {
    fn skill_id(&self) -> u16 {
        MER_MAGNIFICAT
    }

    /// SkillMercenaryMagnificat::castendNoDamageId: the status on the target (packet from the target itself), then —
    /// on the first call only — the master's party on the map (skill_castend_nodamage_id with flag | BCT_PARTY | 1),
    /// or without a party the master's packet. rAthena quirk kept: in that last branch the status is started on the
    /// target again (`sc_start(src, target, …)`), not on the master, and the packet names the master.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        // s_mercenary_data* mer = BL_CAST(BL_MER, src); if( mer != nullptr )
        if super::mer_data(c, src).is_none() {
            return;
        }
        let ok = sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&t), &t, id, skill_lv as i32, ok);
        let master = super::mer_master(c, src);
        let party_id = master.as_ref().map(|m| crate::party::party_id_of(c, m.id)).unwrap_or(0);
        if let Some(m) = master.as_ref().filter(|_| party_id != 0 && *flag & 1 == 0) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            party_foreachsamemap_area_sub(c, m, skill_get_splash(id, skill_lv) as i16, &s, id, skill_lv, tick, *flag | BCT_PARTY | 1, skill_castend_nodamage_id);
        } else if let Some(m) = master.as_ref().filter(|_| *flag & 1 == 0) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            let ok = sc::sc_start(c, Some(&s), &t, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
            let m = unit::get(c, m.id).unwrap_or_else(|| m.clone());
            clif_skill_nodamage(c, Some(&s), &m, id, skill_lv as i32, ok);
        }
    }
}
