//! skills/thief/stonefling.cpp: class SkillStoneFling : public SkillImpl (TF_THROWSTONE).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_BLIND, SC_STUN};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillStoneFling;

impl SkillImpl for SkillStoneFling {
    fn skill_id(&self) -> u16 {
        TF_THROWSTONE
    }

    /// SkillStoneFling::applyAdditionalEffects: a player's stone stuns at 3%, and blinds at 3% only when the stun
    /// failed; a monster's stuns at 5% and never blinds.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        let lv = skill_lv as i32;
        if src.kind == BL_PC {
            // Only blind if used by player and stun failed
            if !sc::sc_start(c, Some(src), target, SC_STUN, 3, lv, skill_get_time(id, skill_lv) as i64) {
                sc::sc_start(c, Some(src), target, SC_BLIND, 3, lv, skill_get_time2(id, skill_lv) as i64);
            }
        } else {
            // 5% stun chance and no blind chance when used by monsters
            sc::sc_start(c, Some(src), target, SC_STUN, 5, lv, skill_get_time(id, skill_lv) as i64);
        }
    }

    /// SkillStoneFling::castendDamageId: skill_attack of the skill's own type.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        crate::skill::attack::skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
    }
}
