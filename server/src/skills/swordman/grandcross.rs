//! skills/swordman/grandcross.cpp: class SkillGrandCross : public SkillImpl (CR_GRANDCROSS). The cross's hits come
//! from its skill unit (skillunit agent); skill_castend_id puts the caster in SC_STRIPSHIELD / SC_ANKLE.

use crate::battle::{BF_MAGIC, BF_WEAPON};
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::status::consts::SC_BLIND;
use crate::status::defs::RC_DEMON;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGrandCross;

impl SkillImpl for SkillGrandCross {
    fn skill_id(&self) -> u16 {
        CR_GRANDCROSS
    }

    /// SkillGrandCross::applyCounterAdditionalEffects.
    fn apply_counter_additional_effects(&self, _c: &Ctx, src: &Unit, target: &Unit, _skill_lv: u16, _tick: Tick, attack_type: &mut i32) {
        if src.id == target.id {
            // Grand Cross on self specifically only triggers "When hit by physical attack" autospells and ignores
            // everything else
            *attack_type |= BF_WEAPON;
            *attack_type &= !BF_MAGIC;
        }
    }

    /// SkillGrandCross::castendPos2: the skill's unit group (skillunit agent).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        *flag |= 1;
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }

    /// SkillGrandCross::applyAdditionalEffects: chance to cause blind status vs demon and undead element, but not
    /// against players.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let tstatus = crate::status::status_get_status_data(c, target);
        let dstsd = target.kind == BL_PC;
        if !dstsd && (sc::battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) || tstatus.race as i32 == RC_DEMON) {
            sc::sc_start(c, Some(src), target, SC_BLIND, 100, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
        }
    }
}
