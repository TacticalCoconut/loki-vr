//! skills/ninja/spearofice.cpp: class SkillSpearOfIce : public SkillImpl (NJ_HYOUSENSOU), rAthena e985006.

use super::charms_of_type;
use crate::battle::calc::Damage;
use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skill::state::CHARM_TYPE_WATER;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_SUITON;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSpearOfIce;

impl SkillImpl for SkillSpearOfIce {
    fn skill_id(&self) -> u16 {
        NJ_HYOUSENSOU
    }

    /// SkillSpearOfIce::calculateSkillRatio (RENEWAL: -30%, + 2% per level under Hidden Water), + 20% per water charm.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio -= 30;
        if sce(c, src, SC_SUITON).is_some() {
            *base_skillratio += 2 * skill_lv as i32;
        }
        let charms = charms_of_type(c, src, CHARM_TYPE_WATER);
        if charms > 0 {
            *base_skillratio += 20 * charms;
        }
    }

    /// SkillSpearOfIce::castendDamageId: a magic hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }
}
