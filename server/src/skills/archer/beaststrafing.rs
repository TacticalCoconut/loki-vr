//! skills/archer/beaststrafing.cpp: class SkillBeastStrafing : public SkillImpl (HT_POWER).

use crate::battle::calc::Damage;
use crate::battle::BF_WEAPON;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::status::defs::{RC_BRUTE, RC_INSECT, RC_PLAYER_DORAM};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillBeastStrafing;

impl SkillImpl for SkillBeastStrafing {
    fn skill_id(&self) -> u16 {
        HT_POWER
    }

    /// SkillBeastStrafing::castendDamageId: a weapon hit on brutes, Doram players and insects only.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let race = crate::status::status_get_status_data(c, target).race as i32;
        if race == RC_BRUTE || race == RC_PLAYER_DORAM || race == RC_INSECT {
            crate::skill::attack::skill_attack(c, BF_WEAPON, src, src, target, self.skill_id(), skill_lv, tick, *flag);
        }
    }

    /// SkillBeastStrafing::calculateSkillRatio: `base_skillratio += -50 + 8 * sstatus->str`.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -50 + 8 * sstatus.str as i32;
    }
}
