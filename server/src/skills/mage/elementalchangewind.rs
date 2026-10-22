//! skills/mage/elementalchangewind.cpp: class SkillElementalChangeWind : public SkillImpl (SA_ELEMENTWIND).

use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::defs::MD_STATUSIMMUNE;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillElementalChangeWind;

impl SkillImpl for SkillElementalChangeWind {
    fn skill_id(&self) -> u16 {
        SA_ELEMENTWIND
    }

    /// SkillElementalChangeWind::castendNoDamageId: the target's element becomes skill_get_ele (sc val2).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let tstatus = crate::status::status_get_status_data(c, target);
        let type_ = skill_get_sc(id) as i32;
        let dstmd = target.kind == BL_MOB;
        if src.kind == BL_PC && (!dstmd || crate::battle::mobif::has_mode(tstatus.mode, MD_STATUSIMMUNE)) {
            // Only works on monsters (Except status immune monsters).
            return;
        }
        let ok = sc::sc_start2(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_ele(id, skill_lv), skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
    }
}
