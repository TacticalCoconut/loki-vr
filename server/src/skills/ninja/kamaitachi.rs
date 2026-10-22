//! skills/ninja/kamaitachi.cpp: class SkillKamaitachi : public SkillImpl (NJ_KAMAITACHI), rAthena e985006.

use super::charms_of_type;
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface::skill_unitsetting;
use crate::skill::state::CHARM_TYPE_WIND;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillKamaitachi;

impl SkillImpl for SkillKamaitachi {
    fn skill_id(&self) -> u16 {
        NJ_KAMAITACHI
    }

    /// SkillKamaitachi::calculateSkillRatio: + 100% per level, + 100% per wind charm.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 100 * skill_lv as i32;
        let charms = charms_of_type(c, src, CHARM_TYPE_WIND);
        if charms > 0 {
            *base_skillratio += 100 * charms;
        }
    }

    /// SkillKamaitachi::castendDamageId: `skill_area_temp[1] = target->id`, then skill_attack_area along the path
    /// (skill_eightpath_algorithm: map_foreachindir, and the target alone when the AoE hits nothing; else
    /// map_foreachinpath). Line for line the body of the breath classes' castendDamageId, which
    /// skills/npc/acidbreath.rs ports as breath_castend_damage_id: called with this skill's id.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        crate::skills::npc::acidbreath::breath_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillKamaitachi::castendPos2: flag 1 keeps the ammo (it is deleted when the group is).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
