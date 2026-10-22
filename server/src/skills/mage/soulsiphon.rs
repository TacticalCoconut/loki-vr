//! skills/mage/soulsiphon.cpp: class SkillSoulSiphon : public SkillImpl (PF_SOULBURN).

use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::defs::BF_MAGIC;
use crate::status::hp::status_percent_damage;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillSoulSiphon;

impl SkillImpl for SkillSoulSiphon {
    fn skill_id(&self) -> u16 {
        PF_SOULBURN
    }

    /// SkillSoulSiphon::castendDamageId: a 40% / 50% / 60% / 70% / 70% chance to burn all the target's SP (level 5
    /// also deals the magic damage, skill_attack BF_MAGIC), else the caster's own.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let chance = if skill_lv < 5 { 30 + skill_lv as i32 * 10 } else { 70 };
        if ((c.rnd() % 100) as i32) < chance {
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            if skill_lv == 5 {
                skill_attack(c, BF_MAGIC, src, src, target, id, skill_lv, tick, *flag);
            }
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            status_percent_damage(c, Some(&s), target.id, 0, 100, 0, false);
        } else {
            clif_skill_nodamage(c, Some(src), src, id, skill_lv as i32, true);
            if skill_lv == 5 {
                skill_attack(c, BF_MAGIC, src, src, src, id, skill_lv, tick, *flag);
            }
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            status_percent_damage(c, Some(&s), src.id, 0, 100, 0, false);
        }
    }
}
