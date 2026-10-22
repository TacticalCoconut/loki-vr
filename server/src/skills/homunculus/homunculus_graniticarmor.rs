//! skills/homunculus/homunculus_graniticarmor.cpp: class SkillGraniticArmor : public SkillImpl (MH_GRANITIC_ARMOR).

use super::{fresh, hd_of};
use crate::battle::target::battle_get_master;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGraniticArmor;

impl SkillImpl for SkillGraniticArmor {
    fn skill_id(&self) -> u16 {
        MH_GRANITIC_ARMOR
    }

    /// SkillGraniticArmor::castendNoDamageId: the status (val2 = the homunculus' level) on the master, then on the
    /// target. battle_get_master never answers nullptr (without a master it is the homunculus itself). No packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let Some(hd) = hd_of(c, src) else { return };
        let level = hd.homunculus.level as i32;
        let dur = skill_get_time(id, skill_lv) as i64;
        let s_bl = battle_get_master(c, src);
        sc::sc_start2(c, Some(src), &s_bl, type_, 100, skill_lv as i32, level, dur); // start on master
        sc::sc_start2(c, Some(&fresh(c, src)), &fresh(c, target), type_, 100, skill_lv as i32, level, dur);
    }
}
