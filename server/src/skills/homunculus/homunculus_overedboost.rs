//! skills/homunculus/homunculus_overedboost.cpp: class SkillOveredBoost : public SkillImpl (MH_OVERED_BOOST).

use super::{fresh, hd_of};
use crate::battle::target::battle_get_master;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillOveredBoost;

impl SkillImpl for SkillOveredBoost {
    fn skill_id(&self) -> u16 {
        MH_OVERED_BOOST
    }

    /// SkillOveredBoost::castendNoDamageId: the status on the master, then on the target (battle_get_master never
    /// answers nullptr: without a master it is the homunculus itself). No packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if hd_of(c, src).is_none() {
            return;
        }
        let dur = skill_get_time(id, skill_lv) as i64;
        sc::sc_start(c, Some(src), &battle_get_master(c, src), type_, 100, skill_lv as i32, dur);
        sc::sc_start(c, Some(&fresh(c, src)), &fresh(c, target), type_, 100, skill_lv as i32, dur);
    }
}
