//! skills/swordman/sonicwave.cpp: class SkillSonicWave : public WeaponSkillImpl (RK_SONICWAVE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSonicWave;

impl SkillImpl for SkillSonicWave {
    fn skill_id(&self) -> u16 {
        RK_SONICWAVE
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillSonicWave::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 1050 + 150 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillSonicWave::modifyHitRate (!TODO in rAthena: confirm the hitrate bonus).
    fn modify_hit_rate(&self, _c: &Ctx, hit_rate: &mut i16, _src: &Unit, _target: &Unit, skill_lv: u16) {
        let h = *hit_rate as i32;
        *hit_rate = (h + h * 3 * skill_lv as i32 / 100) as i16;
    }
}
