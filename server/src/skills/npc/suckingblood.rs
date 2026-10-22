//! skills/npc/suckingblood.cpp: class SkillSuckingBlood : public SkillImpl (NPC_BLOODDRAIN).

use crate::battle::calc::Damage;
use crate::battle::BF_WEAPON;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillSuckingBlood;

impl SkillImpl for SkillSuckingBlood {
    fn skill_id(&self) -> u16 {
        NPC_BLOODDRAIN
    }

    /// SkillSuckingBlood::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 100 * (skill_lv as i32 - 1);
    }

    /// SkillSuckingBlood::castendDamageId: a weapon hit; the caster heals the damage dealt (the heal display is
    /// clif_skill_nodamage(nullptr, *src, AL_HEAL, heal)).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let heal = skill_attack(c, BF_WEAPON, src, src, target, self.skill_id(), skill_lv, tick, *flag) as i32;
        if heal > 0 {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            clif_skill_nodamage(c, None, &s, AL_HEAL, heal, true);
            crate::status::hp::status_heal(c, src.id, heal as i64, 0, 0, 0);
        }
    }
}
