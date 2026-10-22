//! skills/mage/summonwaterball.cpp: class SkillSummonWaterBall : public SkillImpl (WL_SUMMONWB) and class
//! SkillSummonAttackWater : public SkillImpl (WL_SUMMON_ATK_WATER), rAthena e985006. The castendNoDamageId body is the
//! one of summonfireball.cpp with WLS_WATER (super::summonfireball::summon_sphere_castend_nodamage_id).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

use super::summonfireball::{summon_sphere_castend_nodamage_id, WLS_WATER};

/// WL_SUMMONWB
pub struct SkillSummonWaterBall;

impl SkillImpl for SkillSummonWaterBall {
    fn skill_id(&self) -> u16 {
        WL_SUMMONWB
    }

    /// SkillSummonWaterBall::castendNoDamageId (element WLS_WATER).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        summon_sphere_castend_nodamage_id(c, self.skill_id(), WLS_WATER, src, target, skill_lv);
    }
}

/// WL_SUMMON_ATK_WATER
pub struct SkillSummonAttackWater;

impl SkillImpl for SkillSummonAttackWater {
    fn skill_id(&self) -> u16 {
        WL_SUMMON_ATK_WATER
    }

    /// SkillSummonAttackWater::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 200;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
