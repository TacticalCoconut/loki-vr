//! skills/mage/earthspike.cpp: class SkillEarthSpike : public SkillImpl (WZ_EARTHSPIKE).

use crate::battle::calc::Damage;
use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_EARTH_CARE_OPTION;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillEarthSpike;

impl SkillImpl for SkillEarthSpike {
    fn skill_id(&self) -> u16 {
        WZ_EARTHSPIKE
    }

    /// SkillEarthSpike::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }

    /// SkillEarthSpike::calculateSkillRatio (RENEWAL; pre-renewal has no modifier).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 100;
        if sce(c, src, SC_EARTH_CARE_OPTION).is_some() {
            *base_skillratio += *base_skillratio * 800 / 100;
        }
    }
}
