//! skills/npc/demonshockattack.cpp: class SkillDemonShockAttack : public SkillImpl (NPC_MAGICALATTACK), rAthena
//! e985006.

use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skills::SkillImpl;
use crate::status::consts::SC_MAGICALATTACK;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillDemonShockAttack;

impl SkillImpl for SkillDemonShockAttack {
    fn skill_id(&self) -> u16 {
        NPC_MAGICALATTACK
    }

    /// SkillDemonShockAttack::castendDamageId: the hit, then SC_MAGICALATTACK on the caster.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        sc::sc_start(c, Some(&s), &s, SC_MAGICALATTACK, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
    }
}
