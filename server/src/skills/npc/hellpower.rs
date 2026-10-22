//! skills/npc/hellpower.cpp: class SkillHellPower : public WeaponSkillImpl (NPC_HELLPOWER), rAthena e985006.

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillHellPower;

impl SkillImpl for SkillHellPower {
    fn skill_id(&self) -> u16 {
        NPC_HELLPOWER
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillHellPower::castendNoDamageId: `clif_skill_nodamage(src, *target, id, lv, sc_start(src, target,
    /// skill_get_sc(id), lv * 20, lv, skill_get_time2(id, lv)))` (the argument first).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let ok = sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, skill_lv as i32 * 20, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
    }
}
