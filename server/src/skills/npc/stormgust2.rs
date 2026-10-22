//! skills/npc/stormgust2.cpp: class SkillStormGust2 : public SkillImpl (NPC_STORMGUST2).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface::skill_unitsetting;
use crate::skills::SkillImpl;
use crate::status::consts::SC_FREEZE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillStormGust2;

impl SkillImpl for SkillStormGust2 {
    fn skill_id(&self) -> u16 {
        NPC_STORMGUST2
    }

    /// SkillStormGust2::applyAdditionalEffects: Freeze, 10% at level 1, 7% at level 2, 3% above.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        let t2 = skill_get_time2(id, skill_lv) as i64;
        if skill_lv == 1 {
            sc::sc_start(c, Some(src), target, SC_FREEZE, 10, skill_lv as i32, t2);
        } else if skill_lv == 2 {
            sc::sc_start(c, Some(src), target, SC_FREEZE, 7, skill_lv as i32, t2);
        } else {
            sc::sc_start(c, Some(src), target, SC_FREEZE, 3, skill_lv as i32, t2);
        }
    }

    /// SkillStormGust2::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 200 * skill_lv as i32;
    }

    /// SkillStormGust2::castendPos2: the skill's ground unit (skill_unitsetting: skillunit agent, skill/iface.rs).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
