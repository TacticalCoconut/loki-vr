//! skills/archer/rhythmshooting.cpp: class SkillRhythmShooting : public WeaponSkillImpl (TR_RHYTHMSHOOTING).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::{SC_MYSTIC_SYMPHONY, SC_SOUNDBLEND};
use crate::status::defs::{RC_DEMIHUMAN, RC_FISH};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRhythmShooting;

impl SkillImpl for SkillRhythmShooting {
    fn skill_id(&self) -> u16 {
        TR_RHYTHMSHOOTING
    }

    /// SkillRhythmShooting::castendDamageId: the no-damage packet, then WeaponSkillImpl::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        weapon_castend_damage_id(c, self.skill_id(), src, &super::fresh(c, target), skill_lv, tick, *flag);
    }

    /// SkillRhythmShooting::calculateSkillRatio: -100 + 550 + 950 * lv, + 5 * CON with Stage Manner, + 300 + 100 * lv
    /// + 2 * CON on a Sound Blend target, RE_LVL_DMOD(100), doubled under Mystic Symphony (+ 50% more against fish and
    /// demi-humans).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let tstatus = crate::status::status_get_status_data(c, target);
        *skillratio += -100 + 550 + 950 * skill_lv as i32;
        if src.kind == crate::map::BL_PC && pc_checkskill(c, src, TR_STAGE_MANNER) > 0 {
            *skillratio += 5 * sstatus.con as i32;
        }
        if sce(c, target, SC_SOUNDBLEND).is_some() {
            *skillratio += 300 + 100 * skill_lv as i32;
            *skillratio += 2 * sstatus.con as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
        if sce(c, src, SC_MYSTIC_SYMPHONY).is_some() {
            *skillratio *= 2;
            if tstatus.race as i32 == RC_FISH || tstatus.race as i32 == RC_DEMIHUMAN {
                *skillratio += *skillratio * 50 / 100;
            }
        }
    }
}
