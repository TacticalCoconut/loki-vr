//! skills/archer/hawkboomerang.cpp: class SkillHawkBoomerang : public WeaponSkillImpl (WH_HAWKBOOMERANG).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::defs::{RC_BRUTE, RC_FISH};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHawkBoomerang;

impl SkillImpl for SkillHawkBoomerang {
    fn skill_id(&self) -> u16 {
        WH_HAWKBOOMERANG
    }

    /// SkillHawkBoomerang::calculateSkillRatio: -100 + 600 * lv + 10 * CON, + 10% per Nature Friendly level, + 50%
    /// against brutes and fish, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let tstatus = crate::status::status_get_status_data(c, target);
        *skillratio += -100 + 600 * skill_lv as i32 + 10 * sstatus.con as i32;
        if src.kind == crate::map::BL_PC {
            *skillratio += *skillratio * pc_checkskill(c, src, WH_NATUREFRIENDLY) / 10;
        }
        if tstatus.race as i32 == RC_BRUTE || tstatus.race as i32 == RC_FISH {
            *skillratio += *skillratio * 50 / 100;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillHawkBoomerang::castendDamageId: the no-damage packet, then WeaponSkillImpl::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        weapon_castend_damage_id(c, self.skill_id(), src, &super::fresh(c, target), skill_lv, tick, *flag);
    }
}
