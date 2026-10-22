//! skills/swordman/stormslash.cpp: class SkillStormSlash : public WeaponSkillImpl (DK_STORMSLASH).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_GIANTGROWTH;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillStormSlash;

impl SkillImpl for SkillStormSlash {
    fn skill_id(&self) -> u16 {
        DK_STORMSLASH
    }

    /// SkillStormSlash::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillStormSlash::calculateSkillRatio: doubled at 60% under Giant Growth.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 300 + 750 * skill_lv as i32;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if sce(c, src, SC_GIANTGROWTH).is_some() && c.rnd_chance(60, 100) {
            *skillratio *= 2;
        }
    }
}
