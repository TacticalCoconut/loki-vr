//! skills/summoner/hiss.cpp: class SkillHiss : public SkillImpl (SU_HISS).

use super::solo_branch;
use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::party_foreachsamemap_area_sub;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillHiss;

impl SkillImpl for SkillHiss {
    fn skill_id(&self) -> u16 {
        SU_HISS
    }

    /// SkillHiss::castendNoDamageId: the status on the target, or on every party member within the splash range.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if solo_branch(c, src, *flag) {
            let ok = sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(&t), &t, id, skill_lv as i32, ok);
        } else {
            let f = *flag | BCT_PARTY | 1;
            party_foreachsamemap_area_sub(c, src, skill_get_splash(id, skill_lv) as i16, src, id, skill_lv, tick, f, skill_castend_nodamage_id);
        }
    }
}
