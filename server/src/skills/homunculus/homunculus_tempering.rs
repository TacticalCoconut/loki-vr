//! skills/homunculus/homunculus_tempering.cpp: class SkillTempering : public SkillImpl (MH_TEMPERING).

use super::fresh;
use crate::battle::target::battle_get_master;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillTempering;

impl SkillImpl for SkillTempering {
    fn skill_id(&self) -> u16 {
        MH_TEMPERING
    }

    /// SkillTempering::castendNoDamageId: the skill packet and the status on the homunculus' master
    /// (battle_get_master never answers nullptr: without a master it is the homunculus itself).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let master_bl = battle_get_master(c, src);
        clif_skill_nodamage(c, Some(src), &master_bl, id, skill_lv as i32, true);
        sc::sc_start(c, Some(src), &fresh(c, &master_bl), type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
    }
}
