//! skills/npc/npcphantomthrust.cpp: class SkillNpcPhantomThrust : public WeaponSkillImpl (NPC_PHANTOMTHRUST).
//! SkillNpcPhantomThrust::calculateSkillRatio : ATK = 100% for all level (not overridden).

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::skill::attack::{skill_blown, BLOWN_NONE};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillNpcPhantomThrust;

impl SkillImpl for SkillNpcPhantomThrust {
    fn skill_id(&self) -> u16 {
        NPC_PHANTOMTHRUST
    }

    /// SkillNpcPhantomThrust::castendDamageId: the caster turns to the target and pulls it next to itself; an enemy
    /// is hit (WeaponSkillImpl::castendDamageId).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // unit_setdir(src, map_calc_dir(src, target->x, target->y)) (send_update defaults to true)
        let mut s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let dir = crate::map::calc_dir(&s, target.x, target.y);
        unit::setdir(c, &mut s, dir, true);
        unit::save(c, &s);
        clif_skill_nodamage(c, Some(&s), target, id, skill_lv as i32, true);
        // skill_blown(src, target, distance_bl(src, target) - 1, unit_getdir(src), BLOWN_NONE) (its count is a `char`)
        let dist = crate::path::distance((s.x - target.x) as i32, (s.y - target.y) as i32);
        skill_blown(c, &s, target, (dist - 1) as i8 as i32, unit::getdir(&s) as i8, BLOWN_NONE);
        let s = unit::get(c, src.id).unwrap_or(s);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        if crate::battle::target::battle_check_target(c, &s, &t, BCT_ENEMY) > 0 {
            weapon_castend_damage_id(c, id, &s, &t, skill_lv, tick, *flag);
        }
    }
}
