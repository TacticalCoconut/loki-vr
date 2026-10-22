//! skills/homunculus/homunculus_painkiller.cpp: class SkillPainKiller : public SkillImpl (MH_PAIN_KILLER).

use crate::battle::target::battle_get_master;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillPainKiller;

impl SkillImpl for SkillPainKiller {
    fn skill_id(&self) -> u16 {
        MH_PAIN_KILLER
    }

    /// SkillPainKiller::castendNoDamageId: the status on the homunculus' master whatever the target (battle_get_master
    /// never answers nullptr: without a master it is the homunculus itself). No packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let target = battle_get_master(c, src);
        sc::sc_start(c, Some(src), &target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
    }
}
