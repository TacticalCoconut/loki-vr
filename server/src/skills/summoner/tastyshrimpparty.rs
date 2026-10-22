//! skills/summoner/tastyshrimpparty.cpp: class SkillTastyShrimpParty : public SkillImpl (SU_SHRIMPARTY).

use super::solo_branch;
use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{party_foreachsamemap_area_sub, pc_checkskill};
use crate::skills::SkillImpl;
use crate::status::consts::SC_FRESHSHRIMP;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillTastyShrimpParty;

impl SkillImpl for SkillTastyShrimpParty {
    fn skill_id(&self) -> u16 {
        SU_SHRIMPARTY
    }

    /// SkillTastyShrimpParty::castendNoDamageId: the status on the target, and with Fresh Shrimp learnt its packet and
    /// SC_FRESHSHRIMP at that level; or every party member within the splash range. (No packet of its own.)
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if solo_branch(c, src, *flag) {
            sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
            let i = if src.kind == BL_PC { pc_checkskill(c, src, SU_FRESHSHRIMP) } else { 0 };
            if i > 0 {
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                clif_skill_nodamage(c, Some(&t), &t, SU_FRESHSHRIMP, i, true);
                sc::sc_start(c, Some(src), &t, SC_FRESHSHRIMP, 100, i, skill_get_time(SU_FRESHSHRIMP, i as u16) as i64);
            }
        } else {
            let f = *flag | BCT_PARTY | 1;
            party_foreachsamemap_area_sub(c, src, skill_get_splash(id, skill_lv) as i16, src, id, skill_lv, tick, f, skill_castend_nodamage_id);
        }
    }
}
