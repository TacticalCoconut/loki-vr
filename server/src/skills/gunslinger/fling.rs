//! skills/gunslinger/fling.cpp: class SkillFling : public SkillImpl (GS_FLING), rAthena e985006.

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::attack::skill_attack;
use crate::skills::SkillImpl;
use crate::status::consts::SC_FLING;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFling;

impl SkillImpl for SkillFling {
    fn skill_id(&self) -> u16 {
        GS_FLING
    }

    /// SkillFling::castendDamageId: a hit of the skill's own type.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
    }

    /// SkillFling::applyAdditionalEffects: SC_FLING with the coins the cast used (sd->spiritball_old; 5 for a
    /// non-player).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let coins = if src.kind == BL_PC { crate::skill::state::get(c, src.id).spiritball_old as i32 } else { 5 };
        sc::sc_start(c, Some(src), target, SC_FLING, 100, coins, skill_get_time(self.skill_id(), skill_lv) as i64);
    }
}
