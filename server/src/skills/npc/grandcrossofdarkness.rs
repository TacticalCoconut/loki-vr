//! skills/npc/grandcrossofdarkness.cpp: class SkillGrandCrossOfDarkness : public SkillImpl (NPC_GRANDDARKNESS),
//! rAthena e985006. The cross's hits come from its skill unit (skill_unitsetting: skillunit agent).

use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::status::consts::SC_BLIND;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGrandCrossOfDarkness;

impl SkillImpl for SkillGrandCrossOfDarkness {
    fn skill_id(&self) -> u16 {
        NPC_GRANDDARKNESS
    }

    /// SkillGrandCrossOfDarkness::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_BLIND, 100, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillGrandCrossOfDarkness::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
