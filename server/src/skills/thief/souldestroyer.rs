//! skills/thief/souldestroyer.cpp: class SkillSoulDestroyer : public WeaponSkillImpl (ASC_BREAKER).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSoulDestroyer;

impl SkillImpl for SkillSoulDestroyer {
    fn skill_id(&self) -> u16 {
        ASC_BREAKER
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillSoulDestroyer::calculateSkillRatio (RENEWAL; `// !TODO: Confirm stat modifier` in rAthena).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 150 * skill_lv as i32 + sstatus.str as i32 + sstatus.int_ as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
