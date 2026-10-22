//! skills/mage/drainlife.cpp: class SkillDrainLife : public SkillImpl (WL_DRAINLIFE), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_SKILL;
use crate::skill::attack::skill_attack;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::status::hp::status_heal;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillDrainLife;

impl SkillImpl for SkillDrainLife {
    fn skill_id(&self) -> u16 {
        WL_DRAINLIFE
    }

    /// SkillDrainLife::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 200 * skill_lv as i32 + sstatus.int_ as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillDrainLife::castendDamageId: the caster absorbs (5 + 5 * level)% of the damage dealt, at a
    /// (70 + 5 * level)% chance.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // (int32) cast of skill_attack's int64
        let mut heal = skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag) as i32;
        let rate = 70 + 5 * skill_lv as i32;
        heal = heal.wrapping_mul(5 + 5 * skill_lv as i32) / 100;
        if target.kind == BL_SKILL {
            heal = 0; // Don't absorb heal from Ice Walls or other skill units.
        }
        if heal != 0 && ((c.rnd() % 100) as i32) < rate {
            status_heal(c, src.id, heal as i64, 0, 0, 0);
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            clif_skill_nodamage(c, None, &s, AL_HEAL, heal, true);
        }
    }
}
