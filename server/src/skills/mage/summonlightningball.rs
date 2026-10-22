//! skills/mage/summonlightningball.cpp: class SkillSummonLightningBall : public SkillImpl (WL_SUMMONBL) and class
//! SkillSummonAttackWind : public SkillImpl (WL_SUMMON_ATK_WIND), rAthena e985006. The castendNoDamageId body is the
//! one of summonfireball.cpp with WLS_WIND (super::summonfireball::summon_sphere_castend_nodamage_id).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

use super::summonfireball::{summon_sphere_castend_nodamage_id, WLS_WIND};

/// WL_SUMMONBL
pub struct SkillSummonLightningBall;

impl SkillImpl for SkillSummonLightningBall {
    fn skill_id(&self) -> u16 {
        WL_SUMMONBL
    }

    /// SkillSummonLightningBall::castendNoDamageId (element WLS_WIND).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        summon_sphere_castend_nodamage_id(c, self.skill_id(), WLS_WIND, src, target, skill_lv);
    }
}

/// WL_SUMMON_ATK_WIND
pub struct SkillSummonAttackWind;

impl SkillImpl for SkillSummonAttackWind {
    fn skill_id(&self) -> u16 {
        WL_SUMMON_ATK_WIND
    }

    /// SkillSummonAttackWind::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 200;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
