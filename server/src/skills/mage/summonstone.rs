//! skills/mage/summonstone.cpp: class SkillSummonStone : public SkillImpl (WL_SUMMONSTONE) and class
//! SkillSummonAttackEarth : public SkillImpl (WL_SUMMON_ATK_GROUND), rAthena e985006. The castendNoDamageId body is the
//! one of summonfireball.cpp with WLS_STONE (super::summonfireball::summon_sphere_castend_nodamage_id).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

use super::summonfireball::{summon_sphere_castend_nodamage_id, WLS_STONE};

/// WL_SUMMONSTONE
pub struct SkillSummonStone;

impl SkillImpl for SkillSummonStone {
    fn skill_id(&self) -> u16 {
        WL_SUMMONSTONE
    }

    /// SkillSummonStone::castendNoDamageId (element WLS_STONE).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        summon_sphere_castend_nodamage_id(c, self.skill_id(), WLS_STONE, src, target, skill_lv);
    }
}

/// WL_SUMMON_ATK_GROUND
pub struct SkillSummonAttackEarth;

impl SkillImpl for SkillSummonAttackEarth {
    fn skill_id(&self) -> u16 {
        WL_SUMMON_ATK_GROUND
    }

    /// SkillSummonAttackEarth::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 200;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
