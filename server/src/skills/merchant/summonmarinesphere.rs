//! skills/merchant/summonmarinesphere.cpp: class SkillSummonMarineSphere : public SkillImpl (AM_SPHEREMINE).

use super::ids::*;
use crate::db::skills::*;
use crate::mob::spawn::mob_once_spawn_sub;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillSummonMarineSphere::SkillSummonMarineSphere() : SkillImpl(AM_SPHEREMINE).
pub struct SkillSummonMarineSphere;

impl SkillImpl for SkillSummonMarineSphere {
    fn skill_id(&self) -> u16 {
        AM_SPHEREMINE
    }

    /// SkillSummonMarineSphere::castendPos2: a Marine Sphere at the cell, named after the caster, attached to it (special_state.ai
    /// AI_SPHERE) until skill_get_time ends it (mob_timer_delete). "Correct info, don't change any of this! [celest]"
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let class_ = MOBID_MARINE_SPHERE;
        let ai = crate::mob::AI_SPHERE;
        let name = super::status_get_name(c, src);
        let md = mob_once_spawn_sub(c, Some(src), src.map, x, y, Some(&name), class_, Some(""), crate::status::defs::SZ_SMALL as u8, ai);
        if let Some(md) = md {
            super::summon_attach_and_spawn(c, md, Some(src.id), ai as i32, c.tick() + skill_get_time(id, skill_lv) as Tick);
        }
    }
}
