//! skills/acolyte/ragingpalmstrike.cpp: class SkillRagingPalmStrike : public SkillImpl (CH_PALMSTRIKE).

use crate::battle::calc::Damage;
use crate::battle::{BF_WEAPON, DMG_ENDURE};
use crate::db::skills::*;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skills::novice::{re_lvl_dmod, status_get_amotion};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRagingPalmStrike;

impl SkillImpl for SkillRagingPalmStrike {
    fn skill_id(&self) -> u16 {
        CH_PALMSTRIKE
    }

    /// SkillRagingPalmStrike::castendDamageId: Palm Strike takes effect 1 sec after casting [Skotlex]: an absorbed
    /// damage attack is displayed now (clif_skill_nodamage cannot show the right attack animation delay), the hit is
    /// a timerskill 1000 ms + amotion later.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let amotion = status_get_amotion(c, src);
        crate::battle::clif::clif_damage(c, src, target, tick, amotion, 0, -1, 1, DMG_ENDURE, 0, false); // Display an absorbed damage attack.
        skill_addtimerskill(c, src, tick + (1000 + amotion) as i64, target.id, 0, 0, self.skill_id(), skill_lv, BF_WEAPON, *flag);
    }

    /// SkillRagingPalmStrike::calculateSkillRatio (RENEWAL): +100% + 100% per level + STR (RE_LVL_DMOD 100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += 100 + 100 * skill_lv as i32 + sstatus.str as i32; // !TODO: How does STR play a role?
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
