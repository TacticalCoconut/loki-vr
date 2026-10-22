//! skills/summoner/chulhosonicclaw.cpp: class SkillChulhoSonicClaw : public WeaponSkillImpl (SH_CHUL_HO_SONIC_CLAW).

use super::communes;
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillChulhoSonicClaw;

impl SkillImpl for SkillChulhoSonicClaw {
    fn skill_id(&self) -> u16 {
        SH_CHUL_HO_SONIC_CLAW
    }

    /// SkillChulhoSonicClaw::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 1450 + 2650 * lv;
        *skillratio += 50 * pc_checkskill(c, src, SH_MYSTICAL_CREATURE_MASTERY);
        *skillratio += 5 * sstatus.pow as i32;
        if communes(c, src, SH_COMMUNE_WITH_CHUL_HO) {
            *skillratio += -50 + 350 * lv;
            *skillratio += 50 * pc_checkskill(c, src, SH_MYSTICAL_CREATURE_MASTERY);
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillChulhoSonicClaw::castendDamageId: the skill packet, then WeaponSkillImpl::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }
}
