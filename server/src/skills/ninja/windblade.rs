//! skills/ninja/windblade.cpp: class SkillWindBlade : public SkillImpl (NJ_HUUJIN), rAthena e985006.

use super::charms_of_type;
use crate::battle::calc::Damage;
use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skill::state::CHARM_TYPE_WIND;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWindBlade;

impl SkillImpl for SkillWindBlade {
    fn skill_id(&self) -> u16 {
        NJ_HUUJIN
    }

    /// SkillWindBlade::calculateSkillRatio (RENEWAL: +50%), + 10% per wind charm.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 50;
        let charms = charms_of_type(c, src, CHARM_TYPE_WIND);
        if charms > 0 {
            *base_skillratio += 10 * charms;
        }
    }

    /// SkillWindBlade::castendDamageId: a magic hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }
}
