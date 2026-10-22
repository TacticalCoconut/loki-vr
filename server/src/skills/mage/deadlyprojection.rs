//! skills/mage/deadlyprojection.cpp: class SkillDeadlyProjection : public SkillImpl (AG_DEADLY_PROJECTION), rAthena
//! e985006.

use crate::battle::calc::Damage;
use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::status::consts::SC_DEADLY_DEFEASANCE;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillDeadlyProjection;

impl SkillImpl for SkillDeadlyProjection {
    fn skill_id(&self) -> u16 {
        AG_DEADLY_PROJECTION
    }

    /// SkillDeadlyProjection::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 2800 * skill_lv as i32 + 5 * sstatus.spl as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillDeadlyProjection::castendDamageId: SC_DEADLY_DEFEASANCE on the target, then the hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        sc::sc_start(c, Some(src), target, SC_DEADLY_DEFEASANCE, 100, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        skill_attack(c, BF_MAGIC, src, src, &t, self.skill_id(), skill_lv, tick, *flag);
    }
}
