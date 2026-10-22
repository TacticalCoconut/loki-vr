//! skills/homunculus/homunculus_lightofregene.cpp: class SkillLightOfRegene : public SkillImpl (MH_LIGHT_OF_REGENE).

use super::{fresh, hd_of};
use crate::battle::target::battle_get_master;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillLightOfRegene;

impl SkillImpl for SkillLightOfRegene {
    fn skill_id(&self) -> u16 {
        MH_LIGHT_OF_REGENE
    }

    /// SkillLightOfRegene::castendNoDamageId: the status on the master, then on the homunculus with val2 = its level
    /// (battle_get_master never answers nullptr: without a master it is the homunculus itself). No packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let Some(hd) = hd_of(c, src) else { return };
        let dur = skill_get_time(id, skill_lv) as i64;
        let s_bl = battle_get_master(c, src);
        sc::sc_start(c, Some(src), &s_bl, type_, 100, skill_lv as i32, dur);
        let s = fresh(c, src);
        sc::sc_start2(c, Some(&s), &s, type_, 100, skill_lv as i32, hd.homunculus.level as i32, dur);
    }
}
